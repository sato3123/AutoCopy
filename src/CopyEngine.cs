using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy
{
    public class RunResult
    {
        public int PlannedCopyCount, Scanned, Copied, Overwritten, Renamed, Conflicts, Deleted, Errors, Locked, Excluded, Deferred, PendingDeletes, SameAdopted;
        public long Bytes;
        public long DeleteBytes;
        public bool Failed;
        public string Message = "";
        public List<string> PlannedCopies = new List<string>();
        public List<string> PlannedDeletes = new List<string>();
        public List<string> ExcludedSamples = new List<string>();
        public long ExcludedBytes;
        public long CopyBytesPlanned;
        public double Seconds;

        public string Summary()
        {
            return string.Format("走査 {0}件・コピー {1}件・上書き {2}件・名前変更 {3}件・削除 {4}件・確認待ち {5}件・エラー {6}件 {7:0.0}秒",
                Fmt.Count(Scanned), Copied, Overwritten, Renamed, Deleted, Conflicts, Errors + Locked, Seconds);
        }
    }

    public class FolderConflictException : Exception
    {
        public OwnerInfo Owner;
        public FolderConflictException(OwnerInfo o, string dest)
            : base("このフォルダ名は他のPC（" + o.ComputerName + "）が使っています: " + dest + "　アプリ設定で「このPCのフォルダ名」を変えてください。")
        { Owner = o; }
    }

    /// <summary>1つのジョブの差分コピーを実行する</summary>
    public class CopyEngine
    {
        private readonly JobConfig job;
        private readonly AppSettings app;
        private readonly JobState state;
        private readonly CancellationToken ct;
        private readonly bool dryRun;
        private readonly bool forceMirror;
        private string src, dst;
        private long tol;
        private readonly string batch = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        private ExcludeMatcher matcher;

        /// <summary>true なら変更のないファイルもコピー先と照合する（コピー先で消えたファイルの復旧用）</summary>
        public bool FullVerify;

        public CopyEngine(JobConfig job, AppSettings app, JobState state, bool dryRun, bool forceMirror, CancellationToken ct)
        {
            this.job = job; this.app = app; this.state = state; this.dryRun = dryRun; this.forceMirror = forceMirror; this.ct = ct;
        }

        private bool MirrorOn { get { return job.Mirror || forceMirror; } }

        public static string ResolvedSource(JobConfig job, AppSettings app)
        {
            return NetAccess.ToUncIfMapped(PathUtil.Expand(job.Source, app.FolderName));
        }

        public static string ResolvedDest(JobConfig job, AppSettings app)
        {
            return NetAccess.ToUncIfMapped(PathUtil.Expand(job.Dest, app.FolderName));
        }

        /// <summary>設定の整合性チェック。問題があればメッセージ、なければ null。</summary>
        public static string Validate(JobConfig job, AppSettings app, IEnumerable<JobConfig> allJobs)
        {
            if (string.IsNullOrEmpty(job.Source) || job.Source.Trim().Length == 0) return "コピー元フォルダを指定してください。";
            if (string.IsNullOrEmpty(job.Dest) || job.Dest.Trim().Length == 0) return "コピー先フォルダを指定してください。";
            if (PathUtil.HasFolderVariable(job.Source)) return "コピー元には {フォルダ名} は使えません。";
            if (PathUtil.HasFolderVariable(job.Dest) && string.IsNullOrEmpty(app.FolderName))
                return "コピー先に {フォルダ名} を使うには、アプリ設定で「このPCのフォルダ名」を決めてください。";
            string s = ResolvedSource(job, app), d = ResolvedDest(job, app);
            if (PathUtil.IsSameOrInside(d, s)) return "コピー先がコピー元と同じか、コピー元の中にあります。";
            if (PathUtil.IsSameOrInside(s, d)) return "コピー元がコピー先の中にあります。";
            foreach (JobConfig other in allJobs)
            {
                if (other.Id == job.Id) continue;
                string os = ResolvedSource(other, app);
                if (os.Length > 0 && (PathUtil.IsSameOrInside(d, os) || PathUtil.IsSameOrInside(os, d)))
                    return "コピー先が、別のジョブ「" + other.Name + "」のコピー元と重なっています。";
            }
            return null;
        }

        public RunResult Run()
        {
            Stopwatch sw = Stopwatch.StartNew();
            RunResult res = new RunResult();
            src = ResolvedSource(job, app);
            dst = ResolvedDest(job, app);
            tol = TimeSpan.FromSeconds(Math.Max(0, job.ToleranceSeconds)).Ticks;

            string v = Validate(job, app, new JobConfig[0]);
            if (v != null) throw new InvalidOperationException(v);

            // --- アクセス確保 ---
            NetAccess.Ensure(src, job.RetryCount, job.RetryIntervalSeconds, ct, delegate(int a)
            {
                Logger.Write(job.Name, "RETRY", "コピー元への接続を再試行（" + a + " / " + job.RetryCount + "回目）", -1, -1);
            });
            if (!Directory.Exists(PathUtil.Long(src))) throw new AccessException("コピー元が見つかりません: " + src, false);
            NetAccess.Ensure(dst, job.RetryCount, job.RetryIntervalSeconds, ct, delegate(int a)
            {
                Logger.Write(job.Name, "RETRY", "コピー先への接続を再試行（" + a + " / " + job.RetryCount + "回目）", -1, -1);
            });
            if (!dryRun)
            {
                SafeFs.CreateDirectory(dst);
                CheckOwner();
            }

            // --- 走査 ---
            matcher = new ExcludeMatcher(job.Exclude);
            ScanResult scan = Scanner.Scan(src, job.IncludeSubdirs, matcher, false, ct);
            res.Scanned = scan.Files.Count;
            res.Excluded = scan.ExcludedCount;
            res.ExcludedBytes = scan.ExcludedBytes;
            res.ExcludedSamples = scan.ExcludedSamples;
            if (!dryRun)
                foreach (string e in scan.Errors) Logger.Write(job.Name, "ERROR", "読めないフォルダ: " + e, -1, -1);

            Dictionary<string, FileRecord> files;
            HashSet<string> skipForever;
            lock (state)
            {
                files = new Dictionary<string, FileRecord>(state.Files, StringComparer.OrdinalIgnoreCase);
                skipForever = new HashSet<string>(state.SkipForever, StringComparer.OrdinalIgnoreCase);
            }

            List<string> missing = new List<string>();
            foreach (string k in files.Keys) if (!scan.Files.ContainsKey(k)) missing.Add(k);
            List<string> added = new List<string>();
            foreach (KeyValuePair<string, ScanItem> kv in scan.Files)
                if (!files.ContainsKey(kv.Key) && !kv.Value.Recent) added.Add(kv.Key);

            // --- 名前変更・移動への追従 ---
            if (job.FollowRename && missing.Count > 0 && added.Count > 0)
                FollowRenames(files, scan, missing, added, res);

            // --- 判定 ---
            List<CopyTask> tasks = new List<CopyTask>();
            List<ConflictItem> conflicts = new List<ConflictItem>();
            foreach (KeyValuePair<string, ScanItem> kv in scan.Files)
            {
                ct.ThrowIfCancellationRequested();
                string rel = kv.Key;
                ScanItem it = kv.Value;
                if (skipForever.Contains(rel)) continue;
                FileRecord rec;
                files.TryGetValue(rel, out rec);
                if (!FullVerify && rec != null && rec.Size == it.Size && rec.Mtime == it.Mtime) continue; // 変更なし（読まない）
                if (it.Recent) { res.Deferred++; continue; }

                string dpath = PathUtil.Combine(dst, rel);
                FileInfo dfi = new FileInfo(PathUtil.Long(dpath));
                if (!dfi.Exists)
                {
                    tasks.Add(new CopyTask(rel, it, false));
                    continue;
                }
                long dm = dfi.LastWriteTimeUtc.Ticks, ds = dfi.Length;
                long diff = dm - it.Mtime;
                if (Math.Abs(diff) <= tol)
                {
                    if (ds == it.Size)
                    {
                        // 同じファイル：記録だけ更新
                        res.SameAdopted++;
                        if (!dryRun) UpdateRecord(rel, it, dm, rec != null ? rec.Hash : null, rec == null || rec.CopiedByApp, rec);
                    }
                    else if (job.NewerDestPolicy == NewerPolicy.Overwrite) tasks.Add(new CopyTask(rel, it, true));
                    else conflicts.Add(MakeConflict(rel, it, ds, dm, "sizediff"));
                }
                else if (diff < 0)
                {
                    tasks.Add(new CopyTask(rel, it, true)); // コピー先が古い
                }
                else
                {
                    // コピー先が新しい
                    if (job.NewerDestPolicy == NewerPolicy.Overwrite) tasks.Add(new CopyTask(rel, it, true));
                    else if (job.NewerDestPolicy == NewerPolicy.Skip)
                    {
                        if (!dryRun)
                        {
                            Logger.Write(job.Name, "SKIP", rel + "　コピー先が新しいため上書きしません（設定）", it.Size, -1);
                            UpdateRecord(rel, it, dm, null, false, rec);
                        }
                    }
                    else conflicts.Add(MakeConflict(rel, it, ds, dm, "newer"));
                }
            }
            res.Conflicts = conflicts.Count;
            res.PlannedCopyCount = tasks.Count;
            foreach (CopyTask t in tasks)
            {
                res.CopyBytesPlanned += t.Item.Size;
                if (res.PlannedCopies.Count < 5000) res.PlannedCopies.Add(t.Rel);
            }

            // --- コピー実行 ---
            if (!dryRun)
            {
                lock (state) { state.Conflicts = conflicts; }
                foreach (ConflictItem c in conflicts)
                    Logger.Write(job.Name, "CONFLICT", c.Rel + "　" + (c.Reason == "newer" ? "コピー先が新しい" : "日時同じ・サイズ違い"), c.SrcSize, -1);
                ExecuteCopies(tasks, res);
            }

            // --- ミラー：コピー元で削除されたもの ---
            HandleMissing(files, scan, missing, res);

            // --- 削除済み・退避の古いものを片付け（1日1回） ---
            if (!dryRun) PurgeTrash();

            sw.Stop();
            res.Seconds = sw.Elapsed.TotalSeconds;
            return res;
        }

        // ================= 名前変更 =================

        private void FollowRenames(Dictionary<string, FileRecord> files, ScanResult scan, List<string> missing, List<string> added, RunResult res)
        {
            bool idReliable = IsIdReliable(src);
            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            HashSet<string> usedOld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedNew = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (idReliable)
            {
                Dictionary<string, string> byId = new Dictionary<string, string>();
                foreach (string o in missing)
                {
                    FileRecord r = files[o];
                    if (r.FileIndex == 0) continue;
                    byId[r.Volume + ":" + r.FileIndex] = o;
                }
                if (byId.Count > 0)
                {
                    foreach (string n in added)
                    {
                        ct.ThrowIfCancellationRequested();
                        uint vol; ulong idx;
                        if (!Native.TryGetFileId(PathUtil.Long(PathUtil.Combine(src, n)), out vol, out idx)) continue;
                        string o;
                        if (byId.TryGetValue(vol + ":" + idx, out o) && !usedOld.Contains(o))
                        {
                            pairs.Add(new KeyValuePair<string, string>(o, n));
                            usedOld.Add(o); usedNew.Add(n);
                        }
                    }
                }
            }

            // ファイルIDが使えない・見つからない場合：サイズと更新日時が一意に一致するもの
            Dictionary<string, List<string>> oldByKey = new Dictionary<string, List<string>>();
            foreach (string o in missing)
            {
                if (usedOld.Contains(o)) continue;
                FileRecord r = files[o];
                string key = r.Size + ":" + r.Mtime;
                List<string> l;
                if (!oldByKey.TryGetValue(key, out l)) { l = new List<string>(); oldByKey[key] = l; }
                l.Add(o);
            }
            Dictionary<string, List<string>> newByKey = new Dictionary<string, List<string>>();
            foreach (string n in added)
            {
                if (usedNew.Contains(n)) continue;
                ScanItem it = scan.Files[n];
                string key = it.Size + ":" + it.Mtime;
                List<string> l;
                if (!newByKey.TryGetValue(key, out l)) { l = new List<string>(); newByKey[key] = l; }
                l.Add(n);
            }
            foreach (KeyValuePair<string, List<string>> kv in newByKey)
            {
                List<string> ol;
                if (kv.Value.Count != 1 || !oldByKey.TryGetValue(kv.Key, out ol) || ol.Count != 1) continue;
                string o = ol[0], n = kv.Value[0];
                if (files[o].Size > 0 && !dryRun)
                {
                    // 念のため先頭と末尾を比べる（コピー先の旧ファイル と コピー元の新ファイル）
                    if (!QuickSame(PathUtil.Combine(dst, o), PathUtil.Combine(src, n))) continue;
                }
                pairs.Add(new KeyValuePair<string, string>(o, n));
                usedOld.Add(o); usedNew.Add(n);
            }
            if (pairs.Count == 0) return;

            // フォルダ単位の名前変更にまとめる
            Dictionary<string, List<KeyValuePair<string, string>>> groups = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
            List<KeyValuePair<string, string>> single = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> p in pairs)
            {
                string po, pn;
                if (SplitPrefix(p.Key, p.Value, out po, out pn))
                {
                    string gk = po + "|" + pn;
                    List<KeyValuePair<string, string>> l;
                    if (!groups.TryGetValue(gk, out l)) { l = new List<KeyValuePair<string, string>>(); groups[gk] = l; }
                    l.Add(p);
                }
                else single.Add(p);
            }

            foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> g in groups)
            {
                string[] parts = g.Key.Split('|');
                string po = parts[0], pn = parts[1];
                int under = 0;
                foreach (string k in files.Keys) if (k.StartsWith(po + "\\", StringComparison.OrdinalIgnoreCase)) under++;
                bool dirMove = under == g.Value.Count
                    && !scan.Dirs.Contains(po)
                    && Directory.Exists(PathUtil.Long(PathUtil.Combine(dst, po)))
                    && !Directory.Exists(PathUtil.Long(PathUtil.Combine(dst, pn)))
                    && !PathUtil.IsSameOrInsideRel(pn, po);
                if (!dirMove) { single.AddRange(g.Value); continue; }
                if (dryRun) { res.Renamed += g.Value.Count; MarkRenamed(g.Value, missing, added, files); continue; }
                try
                {
                    SafeFs.Move(PathUtil.Combine(dst, po), PathUtil.Combine(dst, pn));
                    Logger.Write(job.Name, "RENAME", po + " → " + pn + "（フォルダ・" + g.Value.Count + "ファイル）", -1, -1);
                    res.Renamed += g.Value.Count;
                    MarkRenamed(g.Value, missing, added, files);
                }
                catch (SourceProtectedException) { throw; }
                catch (Exception ex)
                {
                    Logger.Write(job.Name, "ERROR", "フォルダの名前変更に失敗（通常のコピーで対応）: " + po + " → " + pn + "　" + ex.Message, -1, -1);
                }
            }

            foreach (KeyValuePair<string, string> p in single)
            {
                ct.ThrowIfCancellationRequested();
                string o = p.Key, n = p.Value;
                FileRecord rec = files[o];
                string dOld = PathUtil.Combine(dst, o), dNew = PathUtil.Combine(dst, n);
                FileInfo fo = new FileInfo(PathUtil.Long(dOld));
                if (!fo.Exists || File.Exists(PathUtil.Long(dNew))) continue;
                if (fo.Length != rec.Size) continue;
                if (rec.DestMtime > 0 && Math.Abs(fo.LastWriteTimeUtc.Ticks - rec.DestMtime) > tol) continue;
                if (dryRun) { res.Renamed++; MarkRenamed(new KeyValuePair<string, string>[] { p }, missing, added, files); continue; }
                try
                {
                    SafeFs.Move(dOld, dNew);
                    Logger.Write(job.Name, "RENAME", o + " → " + n, rec.Size, -1);
                    res.Renamed++;
                    MarkRenamed(new KeyValuePair<string, string>[] { p }, missing, added, files);
                }
                catch (SourceProtectedException) { throw; }
                catch (Exception ex)
                {
                    Logger.Write(job.Name, "ERROR", "名前変更に失敗（通常のコピーで対応）: " + o + " → " + n + "　" + ex.Message, -1, -1);
                }
            }
        }

        /// <summary>名前変更済みとして記録を移す</summary>
        private void MarkRenamed(IEnumerable<KeyValuePair<string, string>> ps, List<string> missing, List<string> added, Dictionary<string, FileRecord> files)
        {
            foreach (KeyValuePair<string, string> p in ps)
            {
                FileRecord rec = files[p.Key];
                files.Remove(p.Key);
                files[p.Value] = rec;
                missing.Remove(p.Key);
                added.Remove(p.Value);
                if (!dryRun)
                {
                    lock (state)
                    {
                        state.Files.Remove(p.Key);
                        state.Files[p.Value] = rec;
                    }
                }
            }
        }

        /// <summary>a\x\f.txt と b\x\f.txt → a と b。末尾が一致しない・一方が空なら false</summary>
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

        private static bool IsIdReliable(string path)
        {
            try
            {
                if (PathUtil.IsUnc(path)) return false;
                DriveInfo d = new DriveInfo(Path.GetPathRoot(PathUtil.Plain(path)));
                if (!d.IsReady) return false;
                string f = d.DriveFormat ?? "";
                return f.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || f.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>先頭と末尾 64KB が同じか（どちらも読み取りのみ）</summary>
        public static bool QuickSame(string a, string b)
        {
            const int N = 65536;
            try
            {
                using (FileStream fa = new FileStream(PathUtil.Long(a), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream fb = new FileStream(PathUtil.Long(b), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fa.Length != fb.Length) return false;
                    byte[] x = new byte[N], y = new byte[N];
                    int nx = ReadFull(fa, x), ny = ReadFull(fb, y);
                    if (nx != ny || !Same(x, y, nx)) return false;
                    if (fa.Length > N)
                    {
                        fa.Seek(Math.Max(0, fa.Length - N), SeekOrigin.Begin);
                        fb.Seek(Math.Max(0, fb.Length - N), SeekOrigin.Begin);
                        nx = ReadFull(fa, x); ny = ReadFull(fb, y);
                        if (nx != ny || !Same(x, y, nx)) return false;
                    }
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        private static int ReadFull(Stream s, byte[] buf)
        {
            int total = 0, n;
            while (total < buf.Length && (n = s.Read(buf, total, buf.Length - total)) > 0) total += n;
            return total;
        }

        private static bool Same(byte[] a, byte[] b, int n)
        {
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ================= コピー =================

        private class CopyTask
        {
            public string Rel; public ScanItem Item; public bool Overwrite;
            public CopyTask(string rel, ScanItem it, bool ow) { Rel = rel; Item = it; Overwrite = ow; }
        }

        private void ExecuteCopies(List<CopyTask> tasks, RunResult res)
        {
            if (tasks.Count == 0) return;
            int degree = job.Parallel > 0 ? job.Parallel : ((PathUtil.IsUnc(src) || PathUtil.IsUnc(dst)) ? 2 : 4);
            degree = Math.Max(1, Math.Min(8, degree));
            Throttle throttle = job.BandwidthKBps > 0 ? new Throttle(job.BandwidthKBps) : null;
            bool idReliable = IsIdReliable(src);
            Exception fatal = null;
            object resLock = new object();
            CancellationTokenSource stopAll = CancellationTokenSource.CreateLinkedTokenSource(ct);

            ParallelOptions po = new ParallelOptions();
            po.MaxDegreeOfParallelism = degree;
            po.CancellationToken = stopAll.Token;
            try
            {
                Parallel.ForEach(tasks, po, delegate(CopyTask t)
                {
                    Stopwatch w = Stopwatch.StartNew();
                    string s = PathUtil.Combine(src, t.Rel), d = PathUtil.Combine(dst, t.Rel);
                    try
                    {
                        if (t.Overwrite && job.BackupBeforeOverwrite && File.Exists(PathUtil.Long(d)))
                            SafeFs.MoveToTrash(dst, t.Rel, "退避", batch);
                        byte[] hash = SafeFs.CopyFile(s, d, new DateTime(t.Item.Mtime, DateTimeKind.Utc), throttle, stopAll.Token);
                        FileRecord rec = new FileRecord();
                        rec.Size = t.Item.Size;
                        rec.Mtime = t.Item.Mtime;
                        rec.Hash = hash;
                        rec.CopiedByApp = true;
                        try { rec.DestMtime = File.GetLastWriteTimeUtc(PathUtil.Long(d)).Ticks; } catch (Exception) { rec.DestMtime = t.Item.Mtime; }
                        if (idReliable)
                        {
                            uint vol; ulong idx;
                            if (Native.TryGetFileId(PathUtil.Long(s), out vol, out idx)) { rec.Volume = vol; rec.FileIndex = idx; }
                        }
                        lock (state) { state.Files[t.Rel] = rec; }
                        w.Stop();
                        Logger.Write(job.Name, t.Overwrite ? "OVERWRITE" : "COPY", t.Rel, t.Item.Size, w.Elapsed.TotalSeconds);
                        lock (resLock)
                        {
                            if (t.Overwrite) res.Overwritten++; else res.Copied++;
                            res.Bytes += t.Item.Size;
                        }
                    }
                    catch (SourceProtectedException ex)
                    {
                        lock (resLock) { fatal = ex; }
                        stopAll.Cancel();
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException ex)
                    {
                        bool locked = IsSharingViolation(ex);
                        Logger.Write(job.Name, "ERROR", t.Rel + "　" + (locked ? "使用中のため次回に回します" : ex.Message), t.Item.Size, -1);
                        lock (resLock) { if (locked) res.Locked++; else res.Errors++; }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(job.Name, "ERROR", t.Rel + "　" + ex.Message, t.Item.Size, -1);
                        lock (resLock) { res.Errors++; }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                if (fatal == null) throw;
            }
            if (fatal != null) throw fatal;
            ct.ThrowIfCancellationRequested();
        }

        private static bool IsSharingViolation(IOException ex)
        {
            int code = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
            return code == 32 || code == 33; // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
        }

        private void UpdateRecord(string rel, ScanItem it, long destMtime, byte[] hash, bool managed, FileRecord old)
        {
            FileRecord rec = new FileRecord();
            rec.Size = it.Size;
            rec.Mtime = it.Mtime;
            rec.DestMtime = destMtime;
            rec.Hash = hash;
            rec.CopiedByApp = managed;
            if (old != null) { rec.Volume = old.Volume; rec.FileIndex = old.FileIndex; }
            lock (state) { state.Files[rel] = rec; }
        }

        private ConflictItem MakeConflict(string rel, ScanItem it, long ds, long dm, string reason)
        {
            ConflictItem c = new ConflictItem();
            c.Rel = rel;
            c.SrcSize = it.Size; c.SrcMtime = it.Mtime;
            c.DstSize = ds; c.DstMtime = dm;
            c.Reason = reason;
            c.Detected = Fmt.Now();
            return c;
        }

        // ================= ミラー削除 =================

        private void HandleMissing(Dictionary<string, FileRecord> files, ScanResult scan, List<string> missing, RunResult res)
        {
            if (missing.Count == 0)
            {
                if (!dryRun) lock (state) { state.PendingDeletes.Clear(); }
                return;
            }
            if (!MirrorOn)
            {
                // 追加・更新のみ：コピー先には触れず、記録だけ消す
                if (!dryRun) lock (state) { foreach (string m in missing) state.Files.Remove(m); }
                return;
            }

            if (scan.Files.Count == 0 && scan.Dirs.Count == 0 && files.Count > 0)
            {
                res.Failed = true;
                res.Message = "コピー元が空のため、コピー先を守るために削除を止めました。";
                if (!dryRun) Logger.Write(job.Name, "ERROR", res.Message, -1, -1);
                return;
            }

            List<string> candidates = new List<string>();
            foreach (string m in missing)
            {
                FileRecord r = files[m];
                if (!r.CopiedByApp) continue;
                if (matcher.ExcludeByPathOnly(m)) continue;
                candidates.Add(m);
                res.DeleteBytes += r.Size;
                if (res.PlannedDeletes.Count < 5000) res.PlannedDeletes.Add(m);
            }
            if (dryRun) { res.Deleted = candidates.Count; return; }

            // 削除対象にならないもの（人が置いた・除外）は記録だけ消す
            HashSet<string> candSet = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
            lock (state)
            {
                foreach (string m in missing) if (!candSet.Contains(m)) state.Files.Remove(m);
            }

            int total = Math.Max(1, files.Count);
            bool tooMany = candidates.Count > job.MirrorMaxCount
                || (candidates.Count > 10 && candidates.Count * 100 > total * Math.Max(1, job.MirrorMaxPercent));
            if (tooMany)
            {
                List<PendingDelete> pend = new List<PendingDelete>();
                foreach (string c in candidates)
                {
                    PendingDelete p = new PendingDelete();
                    p.Rel = c; p.Size = files[c].Size;
                    pend.Add(p);
                }
                lock (state) { state.PendingDeletes = pend; }
                res.PendingDeletes = pend.Count;
                Logger.Write(job.Name, "HOLD", "ミラー：" + pend.Count + "件の削除を保留しました（確認待ち）", res.DeleteBytes, -1);
                return;
            }
            lock (state) { state.PendingDeletes.Clear(); }
            res.Deleted = DeleteMirror(candidates, scan.Dirs);
        }

        /// <summary>ミラー削除を実行（削除済みフォルダへ移動）。保留分の承認からも呼ばれる。</summary>
        public int DeleteMirror(IList<string> rels, HashSet<string> sourceDirs)
        {
            if (dst == null) { src = ResolvedSource(job, app); dst = ResolvedDest(job, app); tol = TimeSpan.FromSeconds(Math.Max(0, job.ToleranceSeconds)).Ticks; }
            int n = 0;
            HashSet<string> parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rel in rels)
            {
                ct.ThrowIfCancellationRequested();
                FileRecord rec;
                lock (state) { state.Files.TryGetValue(rel, out rec); }
                // 念のためコピー元に本当にないことを確認
                if (File.Exists(PathUtil.Long(PathUtil.Combine(src, rel)))) continue;
                string d = PathUtil.Combine(dst, rel);
                FileInfo fi = new FileInfo(PathUtil.Long(d));
                if (!fi.Exists) { lock (state) { state.Files.Remove(rel); } continue; }
                if (rec != null && rec.DestMtime > 0 && fi.LastWriteTimeUtc.Ticks > rec.DestMtime + tol)
                {
                    Logger.Write(job.Name, "SKIP", rel + "　コピー先で更新されているためミラー削除しません", fi.Length, -1);
                    lock (state) { state.Files.Remove(rel); }
                    continue;
                }
                try
                {
                    SafeFs.MoveToTrash(dst, rel, "削除済み", batch);
                    Logger.Write(job.Name, "DELETE", rel + "　（ミラー：コピー元で削除）", fi.Length, -1);
                    lock (state) { state.Files.Remove(rel); }
                    n++;
                    string p = PathUtil.ParentRel(rel);
                    if (p.Length > 0) parents.Add(p);
                }
                catch (SourceProtectedException) { throw; }
                catch (Exception ex)
                {
                    Logger.Write(job.Name, "ERROR", rel + "　削除できません: " + ex.Message, -1, -1);
                }
            }
            // 空になったフォルダ（コピー元にもないもの）を片付ける
            foreach (string p0 in parents.OrderByDescending(x => x.Length))
            {
                string p = p0;
                while (p.Length > 0)
                {
                    if (sourceDirs != null && sourceDirs.Contains(p)) break;
                    if (sourceDirs == null && Directory.Exists(PathUtil.Long(PathUtil.Combine(src, p)))) break;
                    try { if (!SafeFs.DeleteEmptyDirectory(PathUtil.Combine(dst, p))) break; }
                    catch (SourceProtectedException) { throw; }
                    catch (Exception) { break; }
                    p = PathUtil.ParentRel(p);
                }
            }
            return n;
        }

        // ================= その他 =================

        private void CheckOwner()
        {
            string ownerPath = PathUtil.Combine(PathUtil.Combine(dst, AppPaths.DestMetaFolder), "owner.json");
            string l = PathUtil.Long(ownerPath);
            if (File.Exists(l))
            {
                OwnerInfo o = null;
                try { o = Json.Deserialize<OwnerInfo>(File.ReadAllText(l)); } catch (Exception) { }
                if (o != null && !string.IsNullOrEmpty(o.InstanceId) && o.InstanceId != app.InstanceId)
                {
                    if (!string.Equals(o.ComputerName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                        throw new FolderConflictException(o, dst);
                    // 同じPCで入れ直した場合は引き継ぐ
                }
                else if (o != null) return;
            }
            OwnerInfo me = new OwnerInfo();
            me.InstanceId = app.InstanceId;
            me.ComputerName = Environment.MachineName;
            me.FolderName = app.FolderName;
            me.Created = Fmt.Now();
            SafeFs.WriteText(ownerPath, Json.Serialize(me));
        }

        /// <summary>コピー先が他のPCに使われていないか（初回セットアップ用）。使われていれば PC 名。</summary>
        public static string FindOtherOwner(string destResolved, string myInstanceId)
        {
            try
            {
                string l = PathUtil.Long(PathUtil.Combine(PathUtil.Combine(destResolved, AppPaths.DestMetaFolder), "owner.json"));
                if (!File.Exists(l)) return null;
                OwnerInfo o = Json.Deserialize<OwnerInfo>(File.ReadAllText(l));
                if (o == null || o.InstanceId == myInstanceId) return null;
                if (string.Equals(o.ComputerName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return null;
                return o.ComputerName;
            }
            catch (Exception) { return null; }
        }

        private void PurgeTrash()
        {
            long dayAgo = DateTime.UtcNow.AddDays(-1).Ticks;
            lock (state)
            {
                if (state.LastTrashPurgeTicks > dayAgo) return;
                state.LastTrashPurgeTicks = DateTime.UtcNow.Ticks;
            }
            DateTime limit = DateTime.Now.AddDays(-Math.Max(1, app.TrashDays));
            foreach (string kind in new string[] { "削除済み", "退避" })
            {
                string root = PathUtil.Combine(PathUtil.Combine(dst, AppPaths.DestMetaFolder), kind);
                string l = PathUtil.Long(root);
                if (!Directory.Exists(l)) continue;
                foreach (string dir in Directory.GetDirectories(l))
                {
                    string name = Path.GetFileName(dir);
                    DateTime t;
                    if (!DateTime.TryParseExact(name, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out t)) continue;
                    if (t >= limit) continue;
                    try
                    {
                        SafeFs.DeleteDirectoryTree(PathUtil.Combine(root, name));
                        Logger.Write(job.Name, "PURGE", kind + "\\" + name + " を保存期間切れで完全に削除しました", -1, -1);
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(job.Name, "ERROR", kind + "\\" + name + " を片付けられません: " + ex.Message, -1, -1);
                    }
                }
            }
        }
    }
}
