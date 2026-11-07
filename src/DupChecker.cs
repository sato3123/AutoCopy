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
        [DataMember] public bool Seen;
        [DataMember] public int LargePending;   // 大きいファイルの重複で、扱いを選んでもらうもの   // 結果の画面で一度見た（見るまでは「確認が必要」として目立たせる）

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

        private static readonly Dictionary<string, KeyValuePair<DateTime, int>> unseenCache = new Dictionary<string, KeyValuePair<DateTime, int>>();

        /// <summary>まだ結果の画面で見ていない削除候補の数（ファイルが変わったときだけ読み直す）</summary>
        public static int UnseenCandidates(string jobId)
        {
            string p = ResultPath(jobId);
            DateTime t;
            try { if (!File.Exists(p)) return 0; t = File.GetLastWriteTimeUtc(p); } catch (Exception) { return 0; }
            lock (unseenCache)
            {
                KeyValuePair<DateTime, int> c;
                if (unseenCache.TryGetValue(jobId, out c) && c.Key == t) return c.Value;
            }
            DupResult r = LoadResult(jobId);
            int n = r == null || r.Seen ? 0 : r.DeleteCandidateCount();
            lock (unseenCache) { unseenCache[jobId] = new KeyValuePair<DateTime, int>(t, n); }
            return n;
        }

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
            // コピー元は除外ルールなしで調べる：除外しているだけでコピー元に今もあるファイルの既存コピーを
            // 「コピー元にない（古いコピー）」と間違えないため。対象外（対象の限定）は下で保護する
            ScanResult ss = Scanner.Scan(src, job.IncludeSubdirs, null, false, ct);
            JobFileScope scope = JobFileScope.Build(job, app, ct);
            ExcludeMatcher scopeMatcher = new ExcludeMatcher(job.Exclude);
            List<string> protectedFiles = new List<string>();
            foreach (string rel in ds.Files.Keys)
                if (scope.Protects(PathUtil.Combine(dst, rel), false) || !scopeMatcher.InScope(rel)) protectedFiles.Add(rel);
            foreach (string rel in protectedFiles) ds.Files.Remove(rel);
            ds.Dirs.RemoveWhere(delegate(string rel) { return scope.Protects(PathUtil.Combine(dst, rel), true); });

            HashSet<string> orphans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in ds.Files.Keys)
            {
                if (ss.Files.ContainsKey(k)) continue;
                if (ss.InFailedDir(k)) continue;   // コピー元で読めなかったフォルダの中：孤立とは限らない
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
            long minDup = Math.Max(0, job.DupMinSizeKB) * 1024L;   // 小さいファイルの重複は探さない（設定したとき）
            foreach (string o in orphans) { long sz = ds.Files[o].Size; if (sz > 0 && sz >= minDup) orphanSizes.Add(sz); }
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
                        if (h == null) h = FullHash(PathUtil.Combine(dst, r), ct);
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
                if (cur.Count > 0)
                {
                    // 同じ名前のものを優先して組にする（名前で引けるようにして、件数が多くても速く）
                    Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string x in cur) { string nm = PathUtil.FileName(x); if (!byName.ContainsKey(nm)) byName[nm] = x; }
                    foreach (string o in old)
                    {
                        string m;
                        if (!byName.TryGetValue(PathUtil.FileName(o), out m)) m = cur[0];
                        pairs.Add(new KeyValuePair<string, string>(o, m));
                    }
                }
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
            // フォルダごとの件数・容量を1回の走査でまとめて数える（フォルダの数 × ファイルの数 の繰り返しをしない）
            HashSet<string> needDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string gk in folderGroups.Keys) { string[] pp = gk.Split('|'); if (!ss.Dirs.Contains(pp[0])) { needDirs.Add(pp[0]); needDirs.Add(pp[1]); } }
            Dictionary<string, DirAgg> agg = AggregateDirs(ds, orphans, needDirs, ct);
            foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> fg in folderGroups)
            {
                ct.ThrowIfCancellationRequested();
                string[] parts = fg.Key.Split('|');
                string po = parts[0], pc = parts[1];
                if (ss.Dirs.Contains(po)) continue; // コピー元にまだあるフォルダは対象外
                DirAgg ao, ac;
                if (!agg.TryGetValue(po, out ao)) continue;
                if (!agg.TryGetValue(pc, out ac)) ac = new DirAgg();
                if (scope.Protects(PathUtil.Combine(dst, po), true)) continue;
                if (!ao.AllOrphan || ao.Count < 2 || fg.Value.Count * 10 < ao.Count * 9) continue;
                DupGroup g = new DupGroup();
                g.Kind = "folder";
                g.Title = "フォルダ名の変更の可能性　「" + po + "」→「" + pc + "」　中身の一致 " + fg.Value.Count + " / " + ao.Count + " ファイル";
                // 中身がすべて一致したときだけ、最初からチェックを入れる（一部だけ一致のときは、確かめてから選んでもらう）
                g.Entries.Add(Entry(po, true, ao.Count, ao.Bytes, ao.Mtime, "old", fg.Value.Count == ao.Count));
                g.Entries.Add(Entry(pc, true, ac.Count, ac.Bytes, ac.Mtime, "current", false));
                res.Groups.Add(g);
                foreach (KeyValuePair<string, string> p in fg.Value) handled.Add(p.Key);
            }
            // ファイル名の変更・移動：同じフォルダどうしのものは1つのまとまりにする（件数が多くても一覧が重くならないように）
            Dictionary<string, List<KeyValuePair<string, string>>> renameGroups = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
            List<string> renameOrder = new List<string>();
            foreach (KeyValuePair<string, string> p in pairs)
            {
                if (handled.Contains(p.Key)) continue;
                string gk = PathUtil.ParentRel(p.Key) + "|" + PathUtil.ParentRel(p.Value);
                List<KeyValuePair<string, string>> l;
                if (!renameGroups.TryGetValue(gk, out l)) { l = new List<KeyValuePair<string, string>>(); renameGroups[gk] = l; renameOrder.Add(gk); }
                l.Add(p);
            }
            foreach (string gk in renameOrder)
            {
                List<KeyValuePair<string, string>> l = renameGroups[gk];
                DupGroup g = new DupGroup();
                g.Kind = "rename";
                if (l.Count == 1)
                {
                    KeyValuePair<string, string> p = l[0];
                    bool renamed = !string.Equals(PathUtil.FileName(p.Key), PathUtil.FileName(p.Value), StringComparison.OrdinalIgnoreCase);
                    g.Title = (renamed ? "ファイル名の変更　" : "移動　") + p.Key + " → " + p.Value;
                }
                else
                {
                    string[] pp = gk.Split('|');
                    string a = pp[0].Length == 0 ? "（いちばん上）" : pp[0], b = pp[1].Length == 0 ? "（いちばん上）" : pp[1];
                    g.Title = (string.Equals(pp[0], pp[1], StringComparison.OrdinalIgnoreCase)
                        ? "ファイル名の変更　フォルダ「" + a + "」の中で " + l.Count + "件"
                        : "移動・名前の変更　「" + a + "」→「" + b + "」　" + l.Count + "件");
                }
                HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> p in l)
                {
                    ScanItem a = ds.Files[p.Key], b = ds.Files[p.Value];
                    if (added.Add(p.Key)) g.Entries.Add(Entry(p.Key, false, 1, a.Size, a.Mtime, "old", true));
                    if (added.Add(p.Value)) g.Entries.Add(Entry(p.Value, false, 1, b.Size, b.Mtime, "current", false));
                }
                res.Groups.Add(g);
            }

            // 中身が同じ（コピー元にも両方ある：情報のみ）
            if (job.DupShowSameContent)
            {
                Dictionary<string, List<string>> byHash = new Dictionary<string, List<string>>();
                foreach (KeyValuePair<string, FileRecord> kv in recs)
                {
                    if (kv.Value.Hash == null || kv.Value.Size == 0 || kv.Value.Size < minDup || !ds.Files.ContainsKey(kv.Key) || orphans.Contains(kv.Key)) continue;
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

            // 大きいファイルの重複（コピー元の中で中身が同じ）：扱いを選んでもらう
            if (job.DedupOn)
            {
                ScanResult sx = Scanner.Scan(src, job.IncludeSubdirs, new ExcludeMatcher(job.Exclude), false, ct);
                List<LargeDupGroup> groups = LargeDup.Find(src, dst, sx, recs, job.DedupMinBytes, null, ct, null);
                DedupBook book = DedupStore.Get(job.Id);
                List<DedupPendingItem> pend = new List<DedupPendingItem>();
                foreach (LargeDupGroup g in groups)
                    foreach (string o in g.Others)
                    {
                        string dec;
                        lock (book) dec = book.DecisionFor(o, g.Hash);
                        if (dec != null) continue;                                   // もう選んである
                        FileRecord rec;
                        if (recs.TryGetValue(o, out rec) && rec.PlaceholderOf != null) continue;   // もうテキスト
                        bool copied = LargeDup.IsNormalCopy(dst, o, sx.Files[o], recs);
                        if (!copied && job.Dedup == DedupMode.Auto) continue;       // 次のコピーで自動でテキストになる
                        pend.Add(LargeDup.Pending(o, g, sx.Files[o], "check", copied));
                    }
                lock (book) book.ReplacePending("check", pend);
                DedupStore.Save(job.Id);
                lock (book) res.LargePending = book.Pending.Count;
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

        private class DirAgg
        {
            public int Count;
            public long Bytes, Mtime;
            public bool AllOrphan = true;
        }

        /// <summary>指定したフォルダ（の中すべて）のファイル数・容量・最終更新・すべて孤立か を、1回の走査で数える</summary>
        private static Dictionary<string, DirAgg> AggregateDirs(ScanResult ds, HashSet<string> orphans, HashSet<string> dirs, CancellationToken ct)
        {
            Dictionary<string, DirAgg> r = new Dictionary<string, DirAgg>(StringComparer.OrdinalIgnoreCase);
            if (dirs.Count == 0) return r;
            int n = 0;
            foreach (KeyValuePair<string, ScanItem> kv in ds.Files)
            {
                if ((++n & 4095) == 0) ct.ThrowIfCancellationRequested();
                string k = kv.Key;
                bool orphan = orphans.Contains(k);
                for (int i = k.LastIndexOf('\\'); i > 0; i = k.LastIndexOf('\\', i - 1))
                {
                    string d = k.Substring(0, i);
                    if (!dirs.Contains(d)) continue;
                    DirAgg a;
                    if (!r.TryGetValue(d, out a)) { a = new DirAgg(); r[d] = a; }
                    a.Count++; a.Bytes += kv.Value.Size; a.Mtime = Math.Max(a.Mtime, kv.Value.Mtime);
                    if (!orphan) a.AllOrphan = false;
                }
            }
            return r;
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
            try { Json.Save(ResultPath(r.JobId), r); } catch (Exception ex) { Logger.Error(r.JobName, "重複チェックの結果を保存できません: " + ex.Message); }
        }

        internal static string Fingerprint(string path)
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

        internal static string FullHash(string path, CancellationToken token = default(CancellationToken))
        {
            try
            {
                return BitConverter.ToString(SafeFs.HashOf(PathUtil.Long(path), token));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 削除の直前の確認：「古い名前」のファイルは、同じまとまりの「今の名前」のファイルと中身が本当に同じか（1バイトずつ）を確かめる。
        /// MD5 だけに頼らず、チェックの後で変わった場合も消さない。同じでないものは skipped に入れて、削除の対象から外す。
        /// </summary>
        public static List<DupEntry> VerifyBeforeDelete(JobConfig job, AppSettings app, DupResult r, List<DupEntry> sel, List<string> skipped)
        {
            string dst = CopyEngine.ResolvedDest(job, app);
            List<DupEntry> ok = new List<DupEntry>();
            JobFileScope scope = JobFileScope.Build(job, app, CancellationToken.None);
            foreach (DupEntry e in sel)
            {
                if (scope.Protects(PathUtil.Combine(dst, e.Rel), e.IsFolder)) { skipped.Add(e.Rel); continue; }
                if (e.Status != "old") { ok.Add(e); continue; }
                DupGroup g = r != null ? r.Groups.Find(delegate(DupGroup x) { return x.Entries.Contains(e); }) : null;
                bool same = false;
                if (g != null)
                    foreach (DupEntry c in g.Entries)
                    {
                        if (c.Status != "current" || (!e.IsFolder && c.Size != e.Size)) continue;
                        try { if (e.IsFolder ? SameTree(PathUtil.Combine(dst, e.Rel), PathUtil.Combine(dst, c.Rel)) : SafeFs.SameBytes(PathUtil.Combine(dst, e.Rel), PathUtil.Combine(dst, c.Rel))) { same = true; break; } }
                        catch (Exception ex) { Logger.Error(job.Name, "中身を比べられません: " + e.Rel + "　" + ex.Message); }
                    }
                if (same) ok.Add(e);
                else
                {
                    skipped.Add(e.Rel);
                    Logger.Write(job.Name, "SKIP", e.Rel + "　今の名前のファイルと中身が同じでないため、削除しませんでした（重複チェック）", e.Size, -1);
                }
            }
            return ok;
        }

        /// <summary>選ばれたものをコピー先の「削除済み」へ移す</summary>
        public static int DeleteSelected(JobConfig job, AppSettings app, JobState state, List<DupEntry> entries)
        {
            return DeleteSelected(job, app, state, entries, new List<DupEntry>(), new List<string>());
        }

        public static bool SameTree(string a, string b)
        {
            ScanResult sa = Scanner.Scan(a, true, null, false, CancellationToken.None);
            ScanResult sb = Scanner.Scan(b, true, null, false, CancellationToken.None);
            if (sa.FailedDirs.Count > 0 || sb.FailedDirs.Count > 0) return false;
            foreach (string rel in sa.Files.Keys)
                if (!sb.Files.ContainsKey(rel) || !SafeFs.SameBytes(PathUtil.Combine(a, rel), PathUtil.Combine(b, rel))) return false;
            return true;
        }

        public static int DeleteSelected(JobConfig job, AppSettings app, JobState state, List<DupEntry> entries, List<DupEntry> completed, List<string> errors)
        {
            string dst = CopyEngine.ResolvedDest(job, app);
            NetAccess.Ensure(dst, job.RetryCount, job.RetryIntervalSeconds, CancellationToken.None, null);
            string batch = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int n = 0;
            foreach (DupEntry e in entries)
            {
                if (!e.CanDelete) continue;
                string p = PathUtil.Long(PathUtil.Combine(dst, e.Rel));
                if (!File.Exists(p) && !Directory.Exists(p)) { completed.Add(e); continue; }
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
                        completed.Add(e);
                    }
                    catch (SourceProtectedException) { throw; }
                    catch (Exception ex) { errors.Add(e.Rel + ": " + ex.Message); Logger.Write(job.Name, "ERROR", e.Rel + "　削除できません: " + ex.Message, -1, -1); }
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
                    completed.Add(e);
                }
                catch (SourceProtectedException) { throw; }
                catch (Exception ex)
                {
                    errors.Add(e.Rel + ": " + ex.Message);
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
                    int suffix = 1;
                    do { target = PathUtil.Combine(destRoot, stem + " (復元" + (suffix == 1 ? "" : " " + suffix) + ")" + ext); suffix++; }
                    while (File.Exists(PathUtil.Long(target)) || Directory.Exists(PathUtil.Long(target)));
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

    /// <summary>
    /// フォルダ内の重複を「同じ」とみなす基準（組み合わせられる。選んだものがすべて同じなら同じとみなす）。
    /// 中身：ファイル全体が同じ（大きさも同じ）。ファイル名：名前（フォルダは問わない・大文字小文字は区別しない）。
    /// 更新日時：2秒以内の差は同じ（FAT・NAS の丸め）。大きさ：バイト数が同じ。
    /// </summary>
    public static class DupBasis
    {
        public const int Content = 1, Name = 2, Mtime = 4, Size = 8;
        public const int Default = Content;
        public const long MtimeTolerance = TimeSpan.TicksPerSecond * 2;

        public static bool Has(int basis, int flag) { return (basis & flag) != 0; }
        public static bool Valid(int basis) { return (basis & (Content | Name | Mtime | Size)) != 0; }

        /// <summary>「中身・ファイル名」のような説明</summary>
        public static string Describe(int basis)
        {
            List<string> l = new List<string>();
            if (Has(basis, Content)) l.Add("中身");
            if (Has(basis, Name)) l.Add("ファイル名");
            if (Has(basis, Mtime)) l.Add("更新日時");
            if (Has(basis, Size) && !Has(basis, Content)) l.Add("大きさ");
            return string.Join("・", l.ToArray());
        }
    }

    /// <summary>フォルダ内の重複（同じとみなしたファイル）のまとまり</summary>
    public class FolderDupGroup
    {
        public long Size;           // 先頭（残す候補）の大きさ（中身で比べたときは全員同じ）
        public string Hash;
        public bool SameContent;    // 中身まで同じと確かめたまとまり
        public List<string> Files = new List<string>();   // 相対パス（残すものの候補順：パスが短い → 更新日時が古い）
        public List<long> Mtimes = new List<long>();
        public List<long> Sizes = new List<long>();

        public long SizeAt(int i) { return i >= 0 && i < Sizes.Count ? Sizes[i] : Size; }

        /// <summary>先頭を残す候補にした理由（並べた順：パスが短い → 更新日時が古い → 名前の順）</summary>
        public string KeepReason()
        {
            if (Files.Count < 2) return "1件だけ";
            if (Files[0].Length < Files[1].Length) return "パスが最短";
            if (Mtimes.Count >= 2 && Mtimes[0] < Mtimes[1]) return "いちばん古い";
            return "名前の順";
        }

        /// <summary>先頭を残してほかを移したときに空く容量</summary>
        public long Wasted()
        {
            long w = 0;
            for (int i = 1; i < Files.Count; i++) w += SizeAt(i);
            return w;
        }

        public void RemoveAt(int i)
        {
            Files.RemoveAt(i);
            if (i < Mtimes.Count) Mtimes.RemoveAt(i);
            if (i < Sizes.Count) Sizes.RemoveAt(i);
            Size = SizeAt(0);
        }
    }

    public class FolderDupResult
    {
        public string Root;
        public int Basis = DupBasis.Default;
        public long MinBytes;
        public int Scanned;
        public long ScannedBytes;
        public int SkippedSmall;   // 小さいため調べなかったファイル
        public List<FolderDupGroup> Groups = new List<FolderDupGroup>();
        public long WastedBytes;   // 1つ残して消したら空く容量
        public int WastedFiles;
        public List<string> Errors = new List<string>();

        public void Recount()
        {
            WastedBytes = 0; WastedFiles = 0;
            foreach (FolderDupGroup g in Groups) { WastedBytes += g.Wasted(); WastedFiles += g.Files.Count - 1; }
        }
    }

    /// <summary>
    /// コピーとは関係なく、指定したフォルダの中で同じファイルを探す（読むだけ。何も変えない）。
    /// 基準は組み合わせられる（中身・ファイル名・更新日時・大きさ）。中身で比べるときは
    /// 大きさ → 先頭と末尾 → 全体のMD5 の順に絞り込む。中身を基準にしないときはファイルを読まない。
    /// </summary>
    public static class FolderDup
    {
        public static FolderDupResult Find(string root, bool includeSubdirs, long minBytes, CancellationToken ct, Action<string> progress)
        {
            return Find(root, includeSubdirs, minBytes, DupBasis.Default, ct, progress);
        }

        public static FolderDupResult Find(string root, bool includeSubdirs, long minBytes, int basis, CancellationToken ct, Action<string> progress)
        {
            if (!DupBasis.Valid(basis)) throw new ArgumentException("同じとみなす基準を1つ以上選んでください。");
            FolderDupResult res = new FolderDupResult();
            res.Root = root;
            res.Basis = basis;
            res.MinBytes = minBytes;
            if (!Directory.Exists(PathUtil.Long(root))) throw new DirectoryNotFoundException("フォルダが見つかりません: " + root);
            ScanResult sc = Scanner.Scan(root, includeSubdirs, null, true, ct, delegate(int n) { if (progress != null) progress("ファイルを数えています（" + Fmt.Count(n) + "件）"); });
            res.Scanned = sc.Files.Count;
            res.Errors.AddRange(sc.Errors);
            bool byContent = DupBasis.Has(basis, DupBasis.Content);
            bool bySizeKey = byContent || DupBasis.Has(basis, DupBasis.Size);
            bool byName = DupBasis.Has(basis, DupBasis.Name);
            bool byMtime = DupBasis.Has(basis, DupBasis.Mtime);

            // 1) 中身以外の基準（大きさ・ファイル名）で分ける
            Dictionary<string, List<string>> byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ScanItem> kv in sc.Files)
            {
                res.ScannedBytes += kv.Value.Size;
                if (kv.Value.Size <= 0 || kv.Value.Size < minBytes) { res.SkippedSmall++; continue; }
                string key = (bySizeKey ? kv.Value.Size.ToString() : "") + "|" + (byName ? PathUtil.FileName(kv.Key).ToLowerInvariant() : "");
                List<string> l;
                if (!byKey.TryGetValue(key, out l)) { l = new List<string>(); byKey[key] = l; }
                l.Add(kv.Key);
            }
            // 2) 更新日時（2秒以内は同じ）：並べて、前のものとの差が2秒以内なら同じまとまり
            List<List<string>> groups = new List<List<string>>();
            foreach (List<string> l in byKey.Values)
            {
                if (l.Count < 2) continue;
                if (!byMtime) { groups.Add(l); continue; }
                l.Sort(delegate(string a, string b) { return sc.Files[a].Mtime.CompareTo(sc.Files[b].Mtime); });
                List<string> cur = new List<string>();
                long startT = 0;
                foreach (string r in l)
                {
                    long t = sc.Files[r].Mtime;
                    if (cur.Count > 0 && t - startT > DupBasis.MtimeTolerance) { if (cur.Count > 1) groups.Add(cur); cur = new List<string>(); }
                    if (cur.Count == 0) startT = t;
                    cur.Add(r);
                }
                if (cur.Count > 1) groups.Add(cur);
            }
            // 3) 中身（必要なときだけ読む）
            int cand = 0;
            foreach (List<string> l in groups) cand += l.Count;
            int done = 0;
            foreach (List<string> l in groups)
            {
                ct.ThrowIfCancellationRequested();
                if (!byContent) { AddGroup(res, sc, l, false, null); done += l.Count; continue; }
                long size = sc.Files[l[0]].Size;
                Dictionary<string, List<string>> byFp = new Dictionary<string, List<string>>();
                foreach (string r in l)
                {
                    done++;
                    if (progress != null && (done & 15) == 0) progress("中身を比べています（" + Fmt.Count(done) + " / " + Fmt.Count(cand) + "件）");
                    string fp = size <= 131072 ? DupChecker.FullHash(PathUtil.Combine(root, r), ct) : DupChecker.Fingerprint(PathUtil.Combine(root, r));
                    if (fp == null) { res.Errors.Add("読めません: " + r); continue; }
                    List<string> fl;
                    if (!byFp.TryGetValue(fp, out fl)) { fl = new List<string>(); byFp[fp] = fl; }
                    fl.Add(r);
                }
                foreach (KeyValuePair<string, List<string>> f in byFp)
                {
                    if (f.Value.Count < 2) continue;
                    Dictionary<string, List<string>> byHash = new Dictionary<string, List<string>>();
                    if (size <= 131072) byHash[f.Key] = f.Value;   // 小さいファイルはもう全体を比べた
                    else
                        foreach (string r in f.Value)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (progress != null) progress("中身を比べています（" + Fmt.Count(done) + " / " + Fmt.Count(cand) + "件）　" + r);
                            string h = DupChecker.FullHash(PathUtil.Combine(root, r), ct);
                            if (h == null) { res.Errors.Add("読めません: " + r); continue; }
                            List<string> hl;
                            if (!byHash.TryGetValue(h, out hl)) { hl = new List<string>(); byHash[h] = hl; }
                            hl.Add(r);
                        }
                    foreach (KeyValuePair<string, List<string>> hh in byHash)
                        if (hh.Value.Count >= 2) AddGroup(res, sc, hh.Value, true, hh.Key);
                }
            }
            // 無駄な容量の大きいまとまりから
            res.Groups.Sort(delegate(FolderDupGroup a, FolderDupGroup b) { return b.Wasted().CompareTo(a.Wasted()); });
            res.Recount();
            return res;
        }

        private static void AddGroup(FolderDupResult res, ScanResult sc, List<string> members, bool sameContent, string hash)
        {
            FolderDupGroup grp = new FolderDupGroup();
            grp.Hash = hash; grp.SameContent = sameContent;
            List<string> files = new List<string>(members);
            // 残す候補を先頭に：パスが短い → 更新日時が古い → 名前の順
            files.Sort(delegate(string a, string b)
            {
                int c = a.Length.CompareTo(b.Length);
                if (c == 0) c = sc.Files[a].Mtime.CompareTo(sc.Files[b].Mtime);
                if (c == 0) c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return c;
            });
            grp.Files = files;
            foreach (string x in files) { grp.Mtimes.Add(sc.Files[x].Mtime); grp.Sizes.Add(sc.Files[x].Size); }
            grp.Size = grp.SizeAt(0);
            res.Groups.Add(grp);
        }

        /// <summary>
        /// 選んだファイルを、そのフォルダの「_AutoCopy\重複で削除\日時」へ移す（消さない。戻せる）。
        /// 自動コピーのコピー元のファイルは移さない（元データは削除しない）。できなかったものを返す。
        /// </summary>
        public static List<string> MoveAway(string root, List<FolderDupGroup> groups, ICollection<string> toMove, out int moved)
        {
            List<string> errs = new List<string>();
            moved = 0;
            HashSet<string> want = new HashSet<string>(toMove, StringComparer.OrdinalIgnoreCase);
            List<string> rels = new List<string>();
            foreach (FolderDupGroup g in groups)
            {
                List<string> mine = g.Files.FindAll(delegate(string f) { return want.Contains(f); });
                if (mine.Count == 0) continue;
                List<string> keep = g.Files.FindAll(delegate(string f) { return !want.Contains(f); });
                if (keep.Count == 0) { foreach (string f in mine) errs.Add(f + "：同じ中身のファイルが1つも残らないため、移しませんでした"); continue; }
                string keeper = PathUtil.Combine(root, keep[0]);
                foreach (string f in mine)
                {
                    // 移す直前に、残すファイルと中身が本当に同じかを1バイトずつ確かめる（MD5 だけに頼らない・検索の後で変わった場合も）
                    bool same;
                    try { same = SafeFs.SameBytes(PathUtil.Combine(root, f), keeper); }
                    catch (Exception ex) { errs.Add(f + "：中身を確かめられないため、移しませんでした（" + ex.Message + "）"); continue; }
                    if (!same)
                    {
                        errs.Add(f + (g.SameContent ? "：残すファイルと中身が同じでなくなったため、移しませんでした"
                            : "：残すファイル（" + keep[0] + "）と中身が違うため、移しませんでした（名前・日時などが同じでも、中身が違うものは移しません）"));
                        continue;
                    }
                    rels.Add(f);
                }
            }
            string batch = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (string r in rels)
            {
                try
                {
                    SafeFs.MoveToTrash(root, r, "重複で削除", batch);
                    moved++;
                    Logger.Write(null, "DELETE", PathUtil.Combine(root, r) + "　（フォルダ内の重複検索で「_AutoCopy\\重複で削除」へ移動）", -1, -1);
                }
                catch (SourceProtectedException) { errs.Add(r + "：自動コピーのコピー元のファイルのため、移しませんでした"); }
                catch (Exception ex) { errs.Add(r + "：" + ex.Message); }
            }
            return errs;
        }

        public static string TrashFolder(string root)
        {
            return PathUtil.Combine(PathUtil.Combine(root, AppPaths.DestMetaFolder), "重複で削除");
        }
    }
}
