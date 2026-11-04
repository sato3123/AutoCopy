using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;

namespace AutoCopy
{
    [DataContract]
    public class DupResult
    {
        [DataMember] public string JobId;
        [DataMember] public string JobName;
        [DataMember] public string Checked;
        [DataMember] public string DestRoot;
        [DataMember] public List<DupGroup> Groups = new List<DupGroup>();
        [DataMember] public int OrphanCount;

        public int DeleteCandidateCount()
        {
            int n = 0;
            foreach (DupGroup g in Groups) foreach (DupEntry e in g.Entries) if (e.Recommended) n += Math.Max(1, e.FileCount);
            return n;
        }

        public long DeleteCandidateBytes()
        {
            long n = 0;
            foreach (DupGroup g in Groups) foreach (DupEntry e in g.Entries) if (e.Recommended) n += e.Size;
            return n;
        }
    }

    [DataContract]
    public class DupGroup
    {
        [DataMember] public string Kind;   // folder / rename / same / orphan / empty
        [DataMember] public string Title;
        [DataMember] public List<DupEntry> Entries = new List<DupEntry>();
    }

    [DataContract]
    public class DupEntry
    {
        [DataMember] public string Rel;
        [DataMember] public bool IsFolder;
        [DataMember] public int FileCount;
        [DataMember] public long Size;
        [DataMember] public long Mtime;
        [DataMember] public string Status;      // old / current / info / orphan / empty
        [DataMember] public bool Recommended;   // 削除を推奨（初期状態でチェック）
        [DataMember] public bool CanDelete;
    }

    /// <summary>コピー先の重複・不要ファイルを探す（コピー元には一切書き込まない）</summary>
    public static class DupChecker
    {
        public static string ResultPath(string jobId) { return Path.Combine(AppPaths.DupDir, jobId + ".json"); }

        public static DupResult LoadResult(string jobId)
        {
            try { return Json.Load<DupResult>(ResultPath(jobId)); } catch (Exception) { return null; }
        }

        public static DupResult Run(JobConfig job, AppSettings app, JobState state, CancellationToken ct)
        {
            string src = CopyEngine.ResolvedSource(job, app);
            string dst = CopyEngine.ResolvedDest(job, app);
            NetAccess.Ensure(src, job.RetryCount, job.RetryIntervalSeconds, ct, null);
            NetAccess.Ensure(dst, job.RetryCount, job.RetryIntervalSeconds, ct, null);

            DupResult res = new DupResult();
            res.JobId = job.Id; res.JobName = job.Name; res.Checked = Fmt.Now(); res.DestRoot = dst;
            // 同期の共有フォルダには他のPCのファイルもあるため、重複チェックで削除候補を出さない
            if (job.Sync) { Save(res); lock (state) { state.LastDupCheckTicks = DateTime.UtcNow.Ticks; } return res; }
            if (!Directory.Exists(PathUtil.Long(dst))) { Save(res); return res; }

            ScanResult ds = Scanner.Scan(dst, true, null, true, ct);
            ScanResult ss = Scanner.Scan(src, job.IncludeSubdirs, null, false, ct);

            HashSet<string> orphans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in ds.Files.Keys)
            {
                if (ss.Files.ContainsKey(k)) continue;
                // 重複のため代わりに置いたテキスト（元のファイルがコピー元にある）は孤立ではない
                if (k.EndsWith(AppPaths.PlaceholderSuffix, StringComparison.OrdinalIgnoreCase)
                    && ss.Files.ContainsKey(k.Substring(0, k.Length - AppPaths.PlaceholderSuffix.Length))) continue;
                orphans.Add(k);
            }
            res.OrphanCount = orphans.Count;

            Dictionary<string, FileRecord> recs;
            lock (state) { recs = new Dictionary<string, FileRecord>(state.Files, StringComparer.OrdinalIgnoreCase); }

            // 孤立ファイルと同じサイズのファイルだけを候補にする
            HashSet<long> orphanSizes = new HashSet<long>();
            foreach (string o in orphans) { long sz = ds.Files[o].Size; if (sz > 0) orphanSizes.Add(sz); }
            Dictionary<long, List<string>> bySize = new Dictionary<long, List<string>>();
            foreach (KeyValuePair<string, ScanItem> kv in ds.Files)
            {
                if (!orphanSizes.Contains(kv.Value.Size)) continue;
                List<string> l;
                if (!bySize.TryGetValue(kv.Value.Size, out l)) { l = new List<string>(); bySize[kv.Value.Size] = l; }
                l.Add(kv.Key);
            }

            Dictionary<string, List<string>> clusters = new Dictionary<string, List<string>>();
            foreach (KeyValuePair<long, List<string>> g in bySize)
            {
                ct.ThrowIfCancellationRequested();
                if (g.Value.Count < 2) continue;
                bool hasOrphan = false;
                foreach (string r in g.Value) if (orphans.Contains(r)) { hasOrphan = true; break; }
                if (!hasOrphan) continue;
                // 先頭と末尾で絞り込み
                Dictionary<string, List<string>> byFp = new Dictionary<string, List<string>>();
                foreach (string r in g.Value)
                {
                    string fp = Fingerprint(PathUtil.Combine(dst, r));
                    if (fp == null) continue;
                    List<string> l;
                    if (!byFp.TryGetValue(fp, out l)) { l = new List<string>(); byFp[fp] = l; }
                    l.Add(r);
                }
                foreach (KeyValuePair<string, List<string>> f in byFp)
                {
                    if (f.Value.Count < 2) continue;
                    foreach (string r in f.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        string h = null;
                        FileRecord rec;
                        if (!orphans.Contains(r) && recs.TryGetValue(r, out rec) && rec.Hash != null && rec.Size == ds.Files[r].Size
                            && (rec.DestMtime == 0 || Math.Abs(rec.DestMtime - ds.Files[r].Mtime) < TimeSpan.TicksPerSecond * 3))
                            h = BitConverter.ToString(rec.Hash);
                        if (h == null) h = FullHash(PathUtil.Combine(dst, r));
                        if (h == null) continue;
                        string key = g.Key + ":" + h;
                        List<string> l;
                        if (!clusters.TryGetValue(key, out l)) { l = new List<string>(); clusters[key] = l; }
                        l.Add(r);
                    }
                }
            }

            // 名前変更・移動の取りこぼし（古い名前 → 今の名前）
            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            List<List<string>> orphanOnly = new List<List<string>>();
            foreach (List<string> c in clusters.Values)
            {
                if (c.Count < 2) continue;
                List<string> cur = new List<string>(), old = new List<string>();
                foreach (string r in c) { if (orphans.Contains(r)) old.Add(r); else cur.Add(r); }
                if (cur.Count > 0) foreach (string o in old) pairs.Add(new KeyValuePair<string, string>(o, BestMatch(o, cur)));
                else if (old.Count > 1) orphanOnly.Add(old);
            }

            // フォルダ単位にまとめる
            Dictionary<string, List<KeyValuePair<string, string>>> folderGroups = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> p in pairs)
            {
                string po, pc;
                if (!SplitPrefix(p.Key, p.Value, out po, out pc)) continue;
                List<KeyValuePair<string, string>> l;
                string gk = po + "|" + pc;
                if (!folderGroups.TryGetValue(gk, out l)) { l = new List<KeyValuePair<string, string>>(); folderGroups[gk] = l; }
                l.Add(p);
            }
            HashSet<string> handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> fg in folderGroups)
            {
                string[] parts = fg.Key.Split('|');
                string po = parts[0], pc = parts[1];
                if (ss.Dirs.Contains(po)) continue; // コピー元にまだあるフォルダは対象外
                int total = 0; long bytes = 0; long mt = 0; bool allOrphan = true;
                foreach (KeyValuePair<string, ScanItem> kv in ds.Files)
                {
                    if (!kv.Key.StartsWith(po + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    total++; bytes += kv.Value.Size; mt = Math.Max(mt, kv.Value.Mtime);
                    if (!orphans.Contains(kv.Key)) allOrphan = false;
                }
                if (!allOrphan || total < 2 || fg.Value.Count * 10 < total * 9) continue;
                long curBytes = 0; int curCount = 0; long curMt = 0;
                foreach (KeyValuePair<string, ScanItem> kv in ds.Files)
                {
                    if (!kv.Key.StartsWith(pc + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    curCount++; curBytes += kv.Value.Size; curMt = Math.Max(curMt, kv.Value.Mtime);
                }
                DupGroup g = new DupGroup();
                g.Kind = "folder";
                g.Title = "フォルダ名の変更の可能性　「" + po + "」→「" + pc + "」　中身の一致 " + fg.Value.Count + " / " + total + " ファイル";
                g.Entries.Add(Entry(po, true, total, bytes, mt, "old", true));
                g.Entries.Add(Entry(pc, true, curCount, curBytes, curMt, "current", false));
                res.Groups.Add(g);
                foreach (KeyValuePair<string, string> p in fg.Value) handled.Add(p.Key);
            }
            foreach (KeyValuePair<string, string> p in pairs)
            {
                if (handled.Contains(p.Key)) continue;
                bool moved = !string.Equals(PathUtil.FileName(p.Key), PathUtil.FileName(p.Value), StringComparison.OrdinalIgnoreCase);
                DupGroup g = new DupGroup();
                g.Kind = "rename";
                g.Title = (moved ? "ファイル名の変更　" : "移動　") + p.Key + " → " + p.Value;
                ScanItem a = ds.Files[p.Key], b = ds.Files[p.Value];
                g.Entries.Add(Entry(p.Key, false, 1, a.Size, a.Mtime, "old", true));
                g.Entries.Add(Entry(p.Value, false, 1, b.Size, b.Mtime, "current", false));
                res.Groups.Add(g);
            }

            // 中身が同じ（コピー元にも両方ある：情報のみ）
            if (job.DupShowSameContent)
            {
                Dictionary<string, List<string>> byHash = new Dictionary<string, List<string>>();
                foreach (KeyValuePair<string, FileRecord> kv in recs)
                {
                    if (kv.Value.Hash == null || kv.Value.Size == 0 || !ds.Files.ContainsKey(kv.Key) || orphans.Contains(kv.Key)) continue;
                    string key = kv.Value.Size + ":" + BitConverter.ToString(kv.Value.Hash);
                    List<string> l;
                    if (!byHash.TryGetValue(key, out l)) { l = new List<string>(); byHash[key] = l; }
                    l.Add(kv.Key);
                }
                foreach (List<string> l in byHash.Values)
                {
                    if (l.Count < 2) continue;
                    DupGroup g = new DupGroup();
                    g.Kind = "same";
                    g.Title = "中身が同じ（コピー元にも両方あるため削除しません）　" + PathUtil.FileName(l[0]) + (l.Count > 2 ? " ほか" + (l.Count - 1) + "件" : "");
                    foreach (string r in l)
                    {
                        ScanItem it = ds.Files[r];
                        g.Entries.Add(Entry(r, false, 1, it.Size, it.Mtime, "info", false));
                    }
                    res.Groups.Add(g);
                }
            }

            // 孤立ファイル
            if (job.DupShowOrphans)
            {
                HashSet<string> inPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> p in pairs) inPairs.Add(p.Key);
                DupGroup g = new DupGroup();
                g.Kind = "orphan";
                g.Title = "孤立ファイル（コピー元にないもの）";
                foreach (string o in orphans)
                {
                    if (inPairs.Contains(o)) continue;
                    ScanItem it = ds.Files[o];
                    g.Entries.Add(Entry(o, false, 1, it.Size, it.Mtime, "orphan", false));
                    if (g.Entries.Count >= 2000) break;
                }
                if (g.Entries.Count > 0) res.Groups.Add(g);
            }
            foreach (List<string> l in orphanOnly)
            {
                if (!job.DupShowOrphans) break;
                DupGroup g = new DupGroup();
                g.Kind = "orphan";
                g.Title = "孤立ファイル同士で中身が同じ　" + PathUtil.FileName(l[0]);
                foreach (string r in l) { ScanItem it = ds.Files[r]; g.Entries.Add(Entry(r, false, 1, it.Size, it.Mtime, "orphan", false)); }
                res.Groups.Add(g);
            }

            // 空のフォルダ（コピー元にないもの）
            if (job.DupShowEmptyFolders)
            {
                List<string> empties = FindEmptyFolders(dst, ds, ss, ct);
                if (empties.Count > 0)
                {
                    DupGroup g = new DupGroup();
                    g.Kind = "empty";
                    g.Title = "空のフォルダ（中にファイルがなく、コピー元にもないもの）　" + empties.Count + "件";
                    foreach (string d in empties)
                    {
                        long mt = 0;
                        try { mt = Directory.GetLastWriteTimeUtc(PathUtil.Long(PathUtil.Combine(dst, d))).Ticks; } catch (Exception) { }
                        g.Entries.Add(Entry(d, true, 0, 0, mt, "empty", true));
                    }
                    res.Groups.Add(g);
                }
            }

            Save(res);
            lock (state) { state.LastDupCheckTicks = DateTime.UtcNow.Ticks; }
            return res;
        }

        public const int MaxEmptyFolders = 2000;

        /// <summary>
        /// コピー先の空のフォルダ（中のどこにもファイルがないもの）のうち、コピー元に同じフォルダがないもの。
        /// 空のフォルダが入れ子になっている場合は、いちばん上のフォルダだけを挙げる。
        /// 隠し・システムフォルダ、作業中の一時ファイル（.ckpart）が入っているものは対象外。
        /// </summary>
        public static List<string> FindEmptyFolders(string dst, ScanResult ds, ScanResult ss, CancellationToken ct)
        {
            HashSet<string> hasFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in ds.Files.Keys)
            {
                string p = f;
                int cut;
                while ((cut = p.LastIndexOf('\\')) > 0)
                {
                    p = p.Substring(0, cut);
                    if (!hasFiles.Add(p)) break;   // 上は登録済み
                }
            }
            HashSet<string> cand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in ds.Dirs)
            {
                if (hasFiles.Contains(d)) continue;
                if (ss.Dirs.Contains(d)) continue;          // コピー元にもある（コピー元でも空）フォルダは残す
                cand.Add(d);
            }
            List<string> top = new List<string>();
            foreach (string d in cand)
            {
                int cut = d.LastIndexOf('\\');
                if (cut > 0 && cand.Contains(d.Substring(0, cut))) continue;
                top.Add(d);
            }
            top.Sort(StringComparer.OrdinalIgnoreCase);
            List<string> res = new List<string>();
            foreach (string d in top)
            {
                ct.ThrowIfCancellationRequested();
                string full = PathUtil.Combine(dst, d);
                try
                {
                    FileAttributes a = File.GetAttributes(PathUtil.Long(full));
                    if ((a & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                }
                catch (Exception) { continue; }
                if (!IsTreeEmpty(full)) continue;
                res.Add(d);
                if (res.Count >= MaxEmptyFolders) break;
            }
            return res;
        }

        /// <summary>フォルダの中（サブフォルダも）にファイルが1つもないか。読めないときは「空でない」とみなす。</summary>
        public static bool IsTreeEmpty(string folder)
        {
            try
            {
                foreach (string f in Directory.EnumerateFiles(PathUtil.Long(folder), "*", SearchOption.AllDirectories)) return false;
                foreach (string d in Directory.EnumerateDirectories(PathUtil.Long(folder), "*", SearchOption.AllDirectories))
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static DupEntry Entry(string rel, bool folder, int count, long size, long mt, string status, bool rec)
        {
            DupEntry e = new DupEntry();
            e.Rel = rel; e.IsFolder = folder; e.FileCount = count; e.Size = size; e.Mtime = mt; e.Status = status;
            e.Recommended = rec;
            e.CanDelete = status != "info" && status != "current";
            return e;
        }

        private static string BestMatch(string o, List<string> cur)
        {
            string name = PathUtil.FileName(o);
            foreach (string c in cur) if (string.Equals(PathUtil.FileName(c), name, StringComparison.OrdinalIgnoreCase)) return c;
            return cur[0];
        }

        private static bool SplitPrefix(string o, string n, out string po, out string pn)
        {
            po = pn = null;
            string[] a = o.Split('\\'), b = n.Split('\\');
            int k = 0;
            while (k < a.Length && k < b.Length &&
                   string.Equals(a[a.Length - 1 - k], b[b.Length - 1 - k], StringComparison.OrdinalIgnoreCase)) k++;
            if (k == 0 || k >= a.Length || k >= b.Length) return false;
            po = string.Join("\\", a, 0, a.Length - k);
            pn = string.Join("\\", b, 0, b.Length - k);
            return true;
        }

        private static void Save(DupResult r)
        {
            try { Json.Save(ResultPath(r.JobId), r); } catch (Exception) { }
        }

        private static string Fingerprint(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(PathUtil.Long(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (MD5 md5 = MD5.Create())
                {
                    byte[] buf = new byte[65536];
                    int n = fs.Read(buf, 0, buf.Length);
                    md5.TransformBlock(buf, 0, n, null, 0);
                    if (fs.Length > buf.Length)
                    {
                        fs.Seek(Math.Max(buf.Length, fs.Length - buf.Length), SeekOrigin.Begin);
                        n = fs.Read(buf, 0, buf.Length);
                        md5.TransformBlock(buf, 0, n, null, 0);
                    }
                    md5.TransformFinalBlock(buf, 0, 0);
                    return BitConverter.ToString(md5.Hash);
                }
            }
            catch (Exception) { return null; }
        }

        private static string FullHash(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(PathUtil.Long(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan))
                using (MD5 md5 = MD5.Create())
                {
                    return BitConverter.ToString(md5.ComputeHash(fs));
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>選ばれたものをコピー先の「削除済み」へ移す</summary>
        public static int DeleteSelected(JobConfig job, AppSettings app, JobState state, List<DupEntry> entries)
        {
            string dst = CopyEngine.ResolvedDest(job, app);
            NetAccess.Ensure(dst, job.RetryCount, job.RetryIntervalSeconds, CancellationToken.None, null);
            string batch = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int n = 0;
            foreach (DupEntry e in entries)
            {
                if (!e.CanDelete) continue;
                string p = PathUtil.Long(PathUtil.Combine(dst, e.Rel));
                if (!File.Exists(p) && !Directory.Exists(p)) continue;
                if (e.Status == "empty")
                {
                    // チェックの後にファイルが入ったかもしれないので、削除の直前に空かどうかを確かめ直す
                    if (!IsTreeEmpty(PathUtil.Combine(dst, e.Rel)))
                    {
                        Logger.Write(job.Name, "SKIP", e.Rel + "　空ではなくなったため削除しませんでした（重複チェック）", -1, -1);
                        continue;
                    }
                    try
                    {
                        SafeFs.MoveToTrash(dst, e.Rel, "削除済み", batch);
                        Logger.Write(job.Name, "DELETE", e.Rel + "（空のフォルダ）　（重複チェック）", 0, -1);
                        n++;
                    }
                    catch (SourceProtectedException) { throw; }
                    catch (Exception ex) { Logger.Write(job.Name, "ERROR", e.Rel + "　削除できません: " + ex.Message, -1, -1); }
                    continue;
                }
                try
                {
                    SafeFs.MoveToTrash(dst, e.Rel, "削除済み", batch);
                    Logger.Write(job.Name, "DELETE", e.Rel + (e.IsFolder ? "（フォルダ・" + e.FileCount + "ファイル）" : "") + "　（重複チェック）", e.Size, -1);
                    lock (state)
                    {
                        if (e.IsFolder)
                        {
                            List<string> rm = new List<string>();
                            foreach (string k in state.Files.Keys) if (k.StartsWith(e.Rel + "\\", StringComparison.OrdinalIgnoreCase)) rm.Add(k);
                            foreach (string k in rm) state.Files.Remove(k);
                        }
                        else state.Files.Remove(e.Rel);
                    }
                    n += Math.Max(1, e.FileCount);
                }
                catch (SourceProtectedException) { throw; }
                catch (Exception ex)
                {
                    Logger.Write(job.Name, "ERROR", e.Rel + "　削除できません: " + ex.Message, -1, -1);
                }
            }
            return n;
        }
    }

    /// <summary>削除済み・退避からの復元</summary>
    public static class TrashStore
    {
        public class Batch
        {
            public string Kind;     // 削除済み / 退避
            public string Name;     // yyyyMMdd-HHmmss
            public string Path;
            public int FileCount;
            public int EmptyFolderCount;   // 空のフォルダ（中に何もないフォルダ）の数
            public long Bytes;
            public DateTime Time;
        }

        public static List<Batch> List(string destRoot)
        {
            List<Batch> list = new List<Batch>();
            foreach (string kind in new string[] { "削除済み", "退避" })
            {
                string root = PathUtil.Long(PathUtil.Combine(PathUtil.Combine(destRoot, AppPaths.DestMetaFolder), kind));
                if (!Directory.Exists(root)) continue;
                foreach (string d in Directory.GetDirectories(root))
                {
                    Batch b = new Batch();
                    b.Kind = kind;
                    b.Name = System.IO.Path.GetFileName(d);
                    b.Path = d;
                    DateTime t;
                    if (DateTime.TryParseExact(b.Name, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out t)) b.Time = t;
                    try
                    {
                        foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        {
                            b.FileCount++;
                            try { b.Bytes += new FileInfo(f).Length; } catch (Exception) { }
                        }
                        b.EmptyFolderCount = EmptyLeaves(d).Count;
                    }
                    catch (Exception) { }
                    list.Add(b);
                }
            }
            list.Sort(delegate(Batch a, Batch c) { return c.Time.CompareTo(a.Time); });
            return list;
        }

        /// <summary>まとめて元の場所に戻す。同名があれば「(復元)」を付ける。</summary>
        public static int Restore(string jobName, string destRoot, Batch b)
        {
            int n = 0;
            string baseLen = b.Path.TrimEnd('\\') + "\\";
            // 空のフォルダ（中に何もないフォルダ）は作り直す
            foreach (string d in EmptyLeaves(b.Path))
            {
                string rel = d.Substring(baseLen.Length);
                string target = PathUtil.Combine(destRoot, rel);
                if (Directory.Exists(PathUtil.Long(target))) continue;
                SafeFs.CreateDirectory(target);
                Logger.Write(jobName, "RESTORE", rel + "（空のフォルダ・" + b.Kind + " " + b.Name + " から）", -1, -1);
                n++;
            }
            foreach (string f in Directory.GetFiles(b.Path, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(baseLen.Length);
                string target = PathUtil.Combine(destRoot, rel);
                string lt = PathUtil.Long(target);
                if (File.Exists(lt))
                {
                    string ext = System.IO.Path.GetExtension(rel);
                    string stem = rel.Substring(0, rel.Length - ext.Length);
                    target = PathUtil.Combine(destRoot, stem + " (復元)" + ext);
                }
                SafeFs.Move(f, target);
                Logger.Write(jobName, "RESTORE", rel + "（" + b.Kind + " " + b.Name + " から）", -1, -1);
                n++;
            }
            try { SafeFs.DeleteDirectoryTree(b.Path); } catch (Exception) { }
            return n;
        }

        /// <summary>中にファイルもフォルダもないフォルダ（長いパスのまま返す）</summary>
        public static List<string> EmptyLeaves(string root)
        {
            List<string> list = new List<string>();
            try
            {
                foreach (string d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    using (IEnumerator<string> e = Directory.EnumerateFileSystemEntries(d).GetEnumerator())
                        if (!e.MoveNext()) list.Add(d);
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}
