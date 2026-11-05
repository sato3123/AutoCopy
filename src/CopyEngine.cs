using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy
{
    public class RunResult
    {
        public int SyncDeleteCandidates;   // 同期：削除の確認待ち
        public int SyncNotTaken;           // 同期：共有フォルダにだけあるが、設定により取り込まなかった
        public int SyncNotPushed;          // 同期：このPCにだけあるが、設定により共有フォルダへコピーしなかった
        public int ScanFailedDirs;         // 読めなかったフォルダの数（この中は削除の対象にしない）
        public bool RemoteScanSkipped;     // 同期：共有フォルダの詳しい確認を省略した（前回の結果を使った）

        /// <summary>結果の分類：成功／一部失敗（エラー・読めないフォルダあり）。中断・失敗は呼び出し側で決める</summary>
        public string Outcome { get { return Errors > 0 || ScanFailedDirs > 0 ? JobOutcome.Partial : JobOutcome.Success; } }
        public int SyncDeleteNew;          // うち今回新しく見つかったもの

        public int PlannedCopyCount, Scanned, Copied, Overwritten, Renamed, Conflicts, Deleted, Errors, Locked, Excluded, Deferred, PendingDeletes, SameAdopted;
        public int Deduped, PlannedDedup;          // 重複のためコピーせず、場所を書いたテキストを置いた件数
        public int DedupAsked;                     // 大きいファイルの重複：扱いを選んでもらうまでコピーしない件数
        public bool DedupNew;                      // 選んでもらう重複が新しく見つかった
        public long DedupBytes;                    // それで省いた容量
        public int Downloaded, PlannedDownloads;   // 同期：共有フォルダからこのPCへ取り込んだ件数
        public long DownloadBytes;
        public List<string> PlannedDownloadList = new List<string>();
        /// <summary>実行したらどうなるか（コピーせずに確かめたときだけ記録する）</summary>
        public List<PlanItem> Plan = new List<PlanItem>();
        /// <summary>予測の種類ごとの件数・容量（一覧の上限を超えた分も数える）</summary>
        public Dictionary<string, long[]> PlanCounts = new Dictionary<string, long[]>();
        public bool PlanTruncated;   // 一覧が上限（PlanLimit）を超えたため、一部だけ載せた
        public const int PlanLimit = 200000;
        public int Unchanged;     // 変わっていないため何もしないファイル
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
                Fmt.Count(Scanned), Copied, Overwritten, Renamed, Deleted, Conflicts, Errors + Locked, Seconds)
                + (Deduped > 0 ? "・重複で省略 " + Deduped + "件（" + Fmt.Size(DedupBytes) + "）" : "")
                + (Downloaded > 0 ? "・取り込み " + Downloaded + "件" : "")
                + (DedupAsked > 0 ? "・重複の扱い待ち " + DedupAsked + "件" : "")
                + (SyncDeleteCandidates > 0 ? "・削除の確認待ち " + SyncDeleteCandidates + "件" : "");
        }
    }

    /// <summary>「実行したらどうなるか」の1件</summary>
    public class PlanItem
    {
        public string Kind;          // PlanKind のどれか
        public string Rel;
        public long Size = -1;       // コピー元（同期の取り込みは共有フォルダ）のサイズ
        public long SrcMtime;        // UTC ticks（0 = なし）
        public long DstSize = -1;    // コピー先のサイズ（-1 = なし）
        public long DstMtime;
        public string Note = "";
    }

    public static class PlanKind
    {
        public const string Copy = "コピー（新規）";
        public const string Overwrite = "上書き";
        public const string Conflict = "確認待ちになる";
        public const string Skip = "上書きしない";
        public const string Rename = "名前変更";
        public const string Delete = "削除（ミラー）";
        public const string Hold = "削除の保留（ミラー）";
        public const string Dedup = "重複で省略";
        public const string DedupAsk = "重複：扱いを選ぶ";
        public const string SyncIn = "取り込み（同期）";
        public const string Same = "同じファイル（記録だけ）";
        public const string Deferred = "次回に回す";
        public const string Excluded = "除外";
        public const string SyncDelete = "削除の確認（同期）";
        public const string SyncNotTaken = "取り込まない（同期の設定）";
        public const string SyncNotPushed = "共有へ送らない（同期の設定）";
        public static readonly string[] All = { Copy, Overwrite, Conflict, Skip, Rename, Delete, Hold, Dedup, DedupAsk, SyncIn, SyncDelete, SyncNotTaken, SyncNotPushed, Same, Deferred, Excluded };
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

        /// <summary>作業状況（画面に出す）</summary>
        public JobProgress Progress = new JobProgress();
        private RunResult current;

        /// <summary>結果を書き込む先（渡しておくと、調べている途中の予測の一覧を画面から読める）</summary>
        public RunResult Live;

        /// <summary>コピーせずに確かめるときだけ、「実行したらどうなるか」を記録する</summary>
        private void Plan(string kind, string rel, ScanItem it, long dstSize, long dstMtime, string note)
        {
            if (!dryRun || current == null) return;
            PlanItem p = new PlanItem();
            p.Kind = kind; p.Rel = rel; p.Note = note ?? "";
            if (it != null) { p.Size = it.Size; p.SrcMtime = it.Mtime; }
            p.DstSize = dstSize; p.DstMtime = dstMtime;
            AddPlan(p);
        }

        /// <summary>予測の一覧に1件足す（画面が途中経過を読むので、一覧を lock してから）</summary>
        private void AddPlan(PlanItem p)
        {
            if (!dryRun || current == null) return;
            lock (current.Plan)
            {
                long[] c;
                if (!current.PlanCounts.TryGetValue(p.Kind, out c)) { c = new long[2]; current.PlanCounts[p.Kind] = c; }
                c[0]++; c[1] += Math.Max(0, p.Size >= 0 ? p.Size : p.DstSize);
                // 一覧は上限まで（大量のファイルでもメモリを使いすぎない）。件数・容量はすべて数える
                if (current.Plan.Count < RunResult.PlanLimit) current.Plan.Add(p); else current.PlanTruncated = true;
            }
        }

        // 判定したときに予測の一覧にも載せる（結果の予測の画面で、調べている途中から順に見えるように）
        private void AddTask(List<CopyTask> list, CopyTask t)
        {
            list.Add(t);
            if (!dryRun) return;
            string note = t.Note;
            if (job.Sync) note = (t.Overwrite ? "このPCで変更 → 共有フォルダを更新" : "このPCで追加 → 共有フォルダへ");
            Plan(t.Overwrite ? PlanKind.Overwrite : PlanKind.Copy, t.Rel, t.Item, t.DstSize, t.DstMtime, note);
        }

        private void AddConflict(List<ConflictItem> list, ConflictItem c)
        {
            list.Add(c);
            if (!dryRun) return;
            PlanItem p = new PlanItem();
            p.Kind = PlanKind.Conflict; p.Rel = c.Rel; p.Size = c.SrcSize; p.SrcMtime = c.SrcMtime; p.DstSize = c.DstSize; p.DstMtime = c.DstMtime;
            p.Note = c.Reason == "newer" ? "コピー先の方が新しい（上書きの確認で選ぶ）" : c.Reason == "both" ? "このPCと共有フォルダの両方で変更（上書きの確認で選ぶ）"
                : c.Reason == "first" ? "初めての同期で、同じ名前で中身が違う（上書きの確認で選ぶ）" : "日時が同じでサイズが違う（上書きの確認で選ぶ）";
            AddPlan(p);
        }

        private void AddDown(List<SyncDown> list, SyncDown d)
        {
            list.Add(d);
            if (!dryRun) return;
            PlanItem p = new PlanItem();
            p.Kind = PlanKind.SyncIn; p.Rel = d.Rel; p.Size = d.Item.Size; p.SrcMtime = d.Item.Mtime;
            p.Note = d.Overwrite ? "他のPCで更新 → このPCのファイルを更新（前の版は控えに）" : "他のPCで追加 → このPCに取り込む";
            AddPlan(p);
        }

        private void AddPlaceholder(List<PlaceholderTask> list, PlaceholderTask t)
        {
            list.Add(t);
            if (dryRun) Plan(PlanKind.Dedup, t.Rel, t.Item, -1, 0, "中身が「" + t.Target + "」と同じ → コピーせず " + PathUtil.FileName(t.Rel) + AppPaths.PlaceholderSuffix + " を置く");
        }

        public CopyEngine(JobConfig job, AppSettings app, JobState state, bool dryRun, bool forceMirror, CancellationToken ct)
        {
            this.job = job; this.app = app; this.state = state; this.dryRun = dryRun; this.forceMirror = forceMirror; this.ct = ct;
        }

        private bool MirrorOn { get { return !job.Sync && (job.Mirror || forceMirror); } }

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
            if (job.Sync && PathUtil.HasFolderVariable(job.Dest))
                return "同期の共有フォルダには {フォルダ名} を使えません（PCごとに別のフォルダになり、同期されないため）。";
            if (job.Sync && job.Mirror) return "同期とミラーは同時に使えません。";
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
            RunResult res = Live ?? new RunResult();
            Live = res;
            src = ResolvedSource(job, app);
            dst = ResolvedDest(job, app);
            tol = TimeSpan.FromSeconds(Math.Max(0, job.ToleranceSeconds)).Ticks;

            // 実行の直前にも、全ジョブのコピー元・コピー先が重なっていないかを確かめる（設定ファイルを直接直した場合など）
            string v = Validate(job, app, app.Jobs != null ? app.Jobs.ToArray() : new JobConfig[0]);
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
                if (!job.Sync) CheckOwner();   // 同期は複数のPCで同じフォルダを使うため、持ち主の確認をしない
                SafeFs.HideMeta(dst);
            }

            // --- 走査 ---
            matcher = new ExcludeMatcher(job.Exclude);
            Progress.SetPhase(job.Sync ? "このPCのフォルダを調べています" : "コピー元を調べています");
            ScanResult scan = Scanner.Scan(src, job.IncludeSubdirs, matcher, false, ct, delegate(int n) { lock (Progress) Progress.Scanned = n; });
            lock (Progress) Progress.Scanned = scan.Files.Count;
            current = res;
            if (dryRun) foreach (string x in scan.ExcludedSamples) Plan(PlanKind.Excluded, x, null, -1, 0, "除外ルールに当てはまる");
            res.Scanned = scan.Files.Count;
            res.Excluded = scan.ExcludedCount;
            res.ExcludedBytes = scan.ExcludedBytes;
            res.ExcludedSamples = scan.ExcludedSamples;
            if (!dryRun)
                foreach (string e in scan.Errors) Logger.Write(job.Name, "ERROR", "【" + ErrorKind.Label(ErrorKind.AccessDenied) + "】読めないフォルダ: " + e + "（この中のファイルは、なくなったとはみなさず、削除の対象にしません）", -1, -1);
            res.ScanFailedDirs = scan.FailedDirs.Count;

            Dictionary<string, FileRecord> files;
            HashSet<string> skipForever;
            lock (state)
            {
                files = new Dictionary<string, FileRecord>(state.Files, StringComparer.OrdinalIgnoreCase);
                skipForever = new HashSet<string>(state.SkipForever, StringComparer.OrdinalIgnoreCase);
            }

            List<string> missing = new List<string>();
            int unknown = 0;
            foreach (string k in files.Keys)
            {
                if (scan.Files.ContainsKey(k)) continue;
                // 読めなかったフォルダの中のファイルは「なくなった」とは限らない → 削除・名前変更の対象にしない（記録もそのまま）
                if (scan.InFailedDir(k)) { unknown++; continue; }
                missing.Add(k);
            }
            if (unknown > 0 && !dryRun)
                Logger.Write(job.Name, "INFO", "読めなかったフォルダの中の " + unknown + "件は、なくなったとはみなさず、削除の対象にしませんでした", -1, -1);
            List<string> added = new List<string>();
            foreach (KeyValuePair<string, ScanItem> kv in scan.Files)
                if (!files.ContainsKey(kv.Key) && !kv.Value.Recent) added.Add(kv.Key);

            // --- 名前変更・移動への追従 ---
            if (job.FollowRename && missing.Count > 0 && added.Count > 0)
                FollowRenames(files, scan, missing, added, res);

            // --- 判定 ---
            List<CopyTask> tasks = new List<CopyTask>();
            List<ConflictItem> conflicts = new List<ConflictItem>();
            List<SyncDown> downloads = new List<SyncDown>();
            List<PlaceholderTask> placeholders = new List<PlaceholderTask>();
            ScanResult remote = null;
            Dictionary<string, string> dedup = null;
            if (job.Sync)
            {
                // 同期：共有フォルダも調べて、両方向の変更を判定する
                // 共有フォルダの詳しい確認は時間がかかる：前回の確認から一定時間以内で、どのPCも共有フォルダを変えていなければ
                // （共有フォルダの「変更の印」が同じなら）、前回の確認の結果を使う
                string stamp = SyncRemoteCache.ReadStamp(dst);
                SyncRemoteCache.Entry ce = SyncRemoteCache.Get(job.Id, dst);
                bool reuse = !FullVerify && job.SyncRemoteCacheMinutes > 0 && ce != null && stamp != null && stamp == ce.Stamp
                    && (DateTime.Now - ce.FullScanAt).TotalMinutes < job.SyncRemoteCacheMinutes;
                if (reuse)
                {
                    remote = ce.CopyRemote();
                    res.RemoteScanSkipped = true;
                    if (!dryRun) Logger.Write(job.Name, "INFO", "共有フォルダは前回の確認（" + ce.FullScanAt.ToString("HH:mm") + "）から変わっていないため、詳しい確認を省略しました", -1, -1);
                }
                else
                {
                    Progress.SetPhase("共有フォルダを調べています");
                    remote = Directory.Exists(PathUtil.Long(dst)) ? Scanner.Scan(dst, job.IncludeSubdirs, matcher, true, ct) : new ScanResult();
                    if (!dryRun && stamp != null && remote.FailedDirs.Count == 0) SyncRemoteCache.Put(job.Id, dst, remote, stamp);
                    else if (!dryRun) SyncRemoteCache.Invalidate(job.Id);
                }
                Progress.SetPhase("変更を判定しています");
                PlanSync(scan, remote, files, skipForever, tasks, downloads, conflicts, res);
            }
            else if (job.DedupOn) dedup = PlanDedup(scan, files, skipForever, res);
            foreach (KeyValuePair<string, ScanItem> kv in scan.Files)
            {
                if (job.Sync) break;
                ct.ThrowIfCancellationRequested();
                string rel = kv.Key;
                ScanItem it = kv.Value;
                if (skipForever.Contains(rel)) continue;
                FileRecord rec;
                files.TryGetValue(rel, out rec);

                // --- 大きいファイルの重複：選んでもらうまでコピーしない ---
                string holdFirst;
                if (dedupHold.TryGetValue(rel, out holdFirst))
                {
                    res.DedupAsked++;
                    string first = holdFirst;
                    Plan(PlanKind.DedupAsk, rel, it, -1, 0, "中身が「" + first + "」と同じ大きいファイル → 扱いを選ぶまでコピーしない（重複チェックの画面で選ぶ）");
                    continue;
                }
                // --- 大きいファイルの重複：2つ目以降はコピーせず、場所を書いたテキストを置く ---
                string target = null;
                if (dedup != null) dedup.TryGetValue(rel, out target);
                if (rec != null && rec.PlaceholderOf != null)
                {
                    if (target == null)
                    {
                        // 重複でなくなった（中身が変わった・相手が消えた）→ 普通にコピーしてテキストを片付ける
                        if (it.Recent) { res.Deferred++; Plan(PlanKind.Deferred, rel, it, -1, 0, "更新されたばかり（保存中の可能性）"); continue; }
                        CopyTask t0 = new CopyTask(rel, it, false);
                        t0.RemovePlaceholder = true;
                        t0.Note = "重複でなくなったため普通にコピー（重複のテキストは片付け）";
                        AddTask(tasks, t0);
                        continue;
                    }
                    if (!FullVerify && string.Equals(target, rec.PlaceholderOf, StringComparison.OrdinalIgnoreCase)
                        && rec.Size == it.Size && rec.Mtime == it.Mtime
                        && File.Exists(PathUtil.Long(PathUtil.Combine(dst, rel + AppPaths.PlaceholderSuffix)))) { res.Unchanged++; continue; }
                    AddPlaceholder(placeholders, new PlaceholderTask(rel, it, target));
                    continue;
                }
                if (target != null && !File.Exists(PathUtil.Long(PathUtil.Combine(dst, rel))))
                {
                    AddPlaceholder(placeholders, new PlaceholderTask(rel, it, target));
                    continue;
                }

                if (!FullVerify && rec != null && rec.Size == it.Size && rec.Mtime == it.Mtime) { res.Unchanged++; continue; } // 変更なし（読まない）
                if (it.Recent) { res.Deferred++; Plan(PlanKind.Deferred, rel, it, -1, 0, "更新されたばかり（保存中の可能性）"); continue; }

                string dpath = PathUtil.Combine(dst, rel);
                FileInfo dfi = new FileInfo(PathUtil.Long(dpath));
                if (!dfi.Exists)
                {
                    AddTask(tasks, new CopyTask(rel, it, false));
                    continue;
                }
                long dm = dfi.LastWriteTimeUtc.Ticks, ds = dfi.Length;
                CopyTask ow;
                long diff = dm - it.Mtime;
                if (Math.Abs(diff) <= tol)
                {
                    if (ds == it.Size)
                    {
                        // 同じファイル：記録だけ更新
                        res.SameAdopted++;
                        Plan(PlanKind.Same, rel, it, ds, dm, "コピー先に同じファイルがある");
                        if (!dryRun) UpdateRecord(rel, it, dm, rec != null ? rec.Hash : null, rec == null || rec.CopiedByApp, rec);
                    }
                    else if (job.NewerDestPolicy == NewerPolicy.Overwrite) { ow = new CopyTask(rel, it, true); ow.Note = "日時が同じでサイズが違う（設定：常に上書き）"; ow.DstSize = ds; ow.DstMtime = dm; AddTask(tasks, ow); }
                    else AddConflict(conflicts, MakeConflict(rel, it, ds, dm, "sizediff"));
                }
                else if (diff < 0)
                {
                    ow = new CopyTask(rel, it, true); ow.Note = "コピー先が古い"; ow.DstSize = ds; ow.DstMtime = dm;
                    AddTask(tasks, ow);
                }
                else
                {
                    // コピー先が新しい
                    if (job.NewerDestPolicy == NewerPolicy.Overwrite) { ow = new CopyTask(rel, it, true); ow.Note = "コピー先が新しい（設定：常に上書き）"; ow.DstSize = ds; ow.DstMtime = dm; AddTask(tasks, ow); }
                    else if (job.NewerDestPolicy == NewerPolicy.Skip)
                    {
                        Plan(PlanKind.Skip, rel, it, ds, dm, "コピー先が新しい（設定：上書きしない）");
                        if (!dryRun)
                        {
                            Logger.Write(job.Name, "SKIP", rel + "　コピー先が新しいため上書きしません（設定）", it.Size, -1);
                            UpdateRecord(rel, it, dm, null, false, rec);
                        }
                    }
                    else AddConflict(conflicts, MakeConflict(rel, it, ds, dm, "newer"));
                }
            }
            res.Conflicts = conflicts.Count;
            res.PlannedCopyCount = tasks.Count;
            res.PlannedDedup = placeholders.Count;
            foreach (PlaceholderTask p in placeholders) res.DedupBytes += dryRun ? p.Item.Size : 0;
            res.PlannedDownloads = downloads.Count;
            foreach (SyncDown d in downloads) if (res.PlannedDownloadList.Count < 5000) res.PlannedDownloadList.Add(d.Rel);
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
                long planBytes = 0;
                foreach (CopyTask t in tasks) planBytes += t.Item.Size;
                foreach (SyncDown d in downloads) planBytes += d.Item.Size;
                Progress.SetPlan(tasks.Count + downloads.Count, planBytes);
                Progress.SetPhase("コピー中");
                ExecuteCopies(tasks, res);
                if (downloads.Count > 0) { Progress.SetPhase("共有フォルダから取り込み中"); ExecuteDownloads(downloads, res); }
                if (job.Sync && tasks.Count > 0) AfterSyncUpload(tasks);
                if (placeholders.Count > 0) { Progress.SetPhase("重複のテキストを置いています"); WritePlaceholders(placeholders, res); }
            }

            // --- ミラー：コピー元で削除されたもの ---
            if (job.Sync)
            {
                // 同期：削除は伝えない。共有フォルダにまだあるものは記録を残す（このPCで消したものを取り込み直さないため）
                if (!dryRun)
                {
                    lock (state) { foreach (string m in missing) if (!remote.Files.ContainsKey(m) && !remote.InFailedDir(m)) state.Files.Remove(m); }
                    res.SyncDeleteNew = SyncDeleteStore.Replace(job.Id, SyncCandidates);
                }
            }
            else
            {
                if (MirrorOn && missing.Count > 0) Progress.SetPhase("ミラーの削除");
                HandleMissing(files, scan, missing, res);
            }

            // --- 削除済み・退避の古いものを片付け（1日1回） ---
            if (!dryRun) { PurgeTrash(); if (job.Sync) PurgeSyncBackup(); }

            sw.Stop();
            res.Seconds = sw.Elapsed.TotalSeconds;
            lock (Progress) { Progress.Phase = "完了"; Progress.Finished = DateTime.Now; }
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
                if (dryRun)
                {
                    res.Renamed += g.Value.Count;
                    PlanItem pi = new PlanItem();
                    pi.Kind = PlanKind.Rename; pi.Rel = pn; pi.Note = "フォルダ名の変更：「" + po + "」→「" + pn + "」（" + g.Value.Count + "ファイル。コピーし直さない）";
                    AddPlan(pi);
                    MarkRenamed(g.Value, missing, added, files);
                    continue;
                }
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
                if (dryRun)
                {
                    res.Renamed++;
                    Plan(PlanKind.Rename, n, scan.Files.ContainsKey(n) ? scan.Files[n] : null, -1, 0, "「" + o + "」から名前変更・移動（コピーし直さない）");
                    MarkRenamed(new KeyValuePair<string, string>[] { p }, missing, added, files);
                    continue;
                }
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
            public bool RemovePlaceholder;   // コピーできたら、前に置いた「重複」テキストを片付ける
            public string Note = "";
            public long DstSize = -1, DstMtime;
            public CopyTask(string rel, ScanItem it, bool ow) { Rel = rel; Item = it; Overwrite = ow; }
        }

        private class PlaceholderTask
        {
            public string Rel, Target; public ScanItem Item;
            public PlaceholderTask(string rel, ScanItem it, string target) { Rel = rel; Item = it; Target = target; }
        }

        private class SyncDown
        {
            public string Rel; public ScanItem Item; public bool Overwrite;
            public SyncDown(string rel, ScanItem it, bool ow) { Rel = rel; Item = it; Overwrite = ow; }
        }

        // ================= 大きいファイルの重複 =================

        private readonly Dictionary<string, byte[]> dedupHash = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> dedupHold = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // ファイル → 中身が同じ1つ目

        /// <summary>
        /// コピー元の中で、一定以上の大きさで中身が同じファイルを探し、2つ目以降の扱いを決める。
        /// 戻り値：テキストを置くファイル → 1つ目。「毎回選ぶ」で、まだ選んでいないものは dedupHold に入れてコピーせずに待つ。
        /// コピー先に普通にコピー済みのものはそのまま（重複チェックの画面で選べる）。
        /// </summary>
        private Dictionary<string, string> PlanDedup(ScanResult scan, Dictionary<string, FileRecord> files, HashSet<string> skip, RunResult res)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<LargeDupGroup> groups = LargeDup.Find(src, dst, scan, files, job.DedupMinBytes, skip, ct, dedupHash);
            DedupBook book = DedupStore.Get(job.Id);
            List<DedupPendingItem> pend = new List<DedupPendingItem>();
            foreach (LargeDupGroup g in groups)
            {
                foreach (string o in g.Others)
                {
                    ScanItem it = scan.Files[o];
                    FileRecord rec;
                    files.TryGetValue(o, out rec);
                    string dec;
                    lock (book) dec = book.DecisionFor(o, g.Hash);
                    if (dec == "copy") continue;                              // 普通にコピーすると選んだ
                    if (LargeDup.IsNormalCopy(dst, o, it, files)) continue;   // 普通にコピー済み
                    bool isText = rec != null && rec.PlaceholderOf != null;
                    if (dec == "text" || isText || job.Dedup == DedupMode.Auto) { map[o] = g.First; continue; }
                    dedupHold[o] = g.First;                                   // 毎回選ぶ：選ぶまでコピーしない
                    pend.Add(LargeDup.Pending(o, g, it, "copy", false));
                }
            }
            if (!dryRun)
            {
                bool changed;
                lock (book) changed = book.ReplacePending("copy", pend);
                if (changed) { DedupStore.Save(job.Id); res.DedupNew = pend.Count > 0; }
            }
            return map;
        }

        public static byte[] HashFile(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(PathUtil.Long(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan))
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    return md5.ComputeHash(fs);
            }
            catch (Exception) { return null; }
        }

        /// <summary>重複の2つ目以降：コピーの代わりに、1つ目の保管場所を書いたテキストを置く</summary>
        private void WritePlaceholders(List<PlaceholderTask> list, RunResult res)
        {
            foreach (PlaceholderTask p in list)
            {
                ct.ThrowIfCancellationRequested();
                string firstDest = PathUtil.Combine(dst, p.Target);
                if (!File.Exists(PathUtil.Long(firstDest)))
                {
                    // 1つ目のコピーがまだできていない（使用中など）→ 次回に回す
                    res.Deferred++;
                    continue;
                }
                string ph = PathUtil.Combine(dst, p.Rel + AppPaths.PlaceholderSuffix);
                try
                {
                    byte[] h;
                    dedupHash.TryGetValue(p.Rel, out h);
                    SafeFs.WriteTextBom(ph, PlaceholderText(p, firstDest, h));
                    FileRecord rec = new FileRecord();
                    rec.Size = p.Item.Size; rec.Mtime = p.Item.Mtime; rec.Hash = h; rec.CopiedByApp = true;
                    rec.PlaceholderOf = p.Target;
                    try { rec.DestMtime = File.GetLastWriteTimeUtc(PathUtil.Long(ph)).Ticks; } catch (Exception ex) { Logger.Error(job.Name, "更新日時を読めません: " + ph + "　" + ex.Message); }
                    lock (state) { state.Files[p.Rel] = rec; }
                    Logger.Write(job.Name, "DEDUP", p.Rel + " → " + p.Target + "（中身が同じため、コピーせず場所を書いたテキストを置きました）", p.Item.Size, -1);
                    res.Deduped++;
                    res.DedupBytes += p.Item.Size;
                }
                catch (SourceProtectedException) { throw; }
                catch (Exception ex)
                {
                    Logger.Write(job.Name, "ERROR", p.Rel + AppPaths.PlaceholderSuffix + "　書けません: " + ex.Message, -1, -1);
                    res.Errors++;
                }
            }
        }

        private string PlaceholderText(PlaceholderTask p, string firstDest, byte[] h)
        {
            return LargeDup.PlaceholderText(src, dst, p.Rel, p.Target, p.Item.Size, p.Item.Mtime, LargeDup.Hex(h));
        }

        // ================= 同期（複数PC・双方向） =================

        /// <summary>
        /// このPCのフォルダ（src）と共有フォルダ（dst）の両方の変更を判定する。削除は伝えない。
        /// 両方で変わったもの・初回で中身が違うものは確認待ちにする。
        /// </summary>
        private void PlanSync(ScanResult local, ScanResult remote, Dictionary<string, FileRecord> files, HashSet<string> skip,
            List<CopyTask> ups, List<SyncDown> downs, List<ConflictItem> conflicts, RunResult res)
        {
            HashSet<string> all = new HashSet<string>(local.Files.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string k in remote.Files.Keys) all.Add(k);
            foreach (string rel in all)
            {
                ct.ThrowIfCancellationRequested();
                if (skip.Contains(rel)) continue;
                // どちらかで読めなかったフォルダの中は、あるかないか分からないため、何もしない（削除の候補にも、コピーにもしない）
                if (local.InFailedDir(rel) || remote.InFailedDir(rel)) continue;
                ScanItem L, S;
                local.Files.TryGetValue(rel, out L);
                remote.Files.TryGetValue(rel, out S);
                FileRecord rec;
                files.TryGetValue(rel, out rec);
                if ((L != null && L.Recent) || (S != null && S.Recent)) { res.Deferred++; Plan(PlanKind.Deferred, rel, L ?? S, -1, 0, "更新されたばかり（保存中の可能性）"); continue; }
                bool lChanged = L != null && (rec == null || rec.Size != L.Size || rec.Mtime != L.Mtime);
                bool sChanged = S != null && (rec == null || Math.Abs(S.Mtime - (rec.DestMtime != 0 ? rec.DestMtime : rec.Mtime)) > tol);
                if (FullVerify && L != null && S == null) lChanged = true;

                if (L != null && S == null)
                {
                    bool realChange = rec == null || rec.Size != L.Size || rec.Mtime != L.Mtime;
                    if (rec != null && !realChange && !(FullVerify && job.SyncPushNew))
                    {
                        // 記録があり変わっていない＝共有フォルダで誰かが消した → 戻さず、このPCからも消すかを確認してもらう
                        AddSyncCandidate(rel, SyncDeleteItem.SideLocal, L.Size, L.Mtime);
                        Plan(PlanKind.SyncDelete, rel, L, -1, 0, "共有フォルダで削除された → このPCからも消すか確認（消す場合は控えに移す）");
                        continue;
                    }
                    // このPCにだけある（共有フォルダにない）ファイル：初期値では共有フォルダへコピーしない（ジョブ設定で選べる）
                    if (!job.SyncPushNew)
                    {
                        res.SyncNotPushed++;
                        Plan(PlanKind.SyncNotPushed, rel, L, -1, 0, rec == null
                            ? "このPCにだけある → 共有フォルダにはコピーしない（設定）"
                            : "共有フォルダで削除された後にこのPCで更新 → 共有フォルダにはコピーしない（設定）");
                        continue;
                    }
                    AddTask(ups, new CopyTask(rel, L, false));
                    continue;
                }
                if (L == null)
                {
                    if (S != null && sChanged)
                    {
                        // 共有フォルダにだけある（このPCにない）ファイル：初期値では取り込まない（ジョブ設定で選べる）
                        if (!job.SyncTakeNew)
                        {
                            res.SyncNotTaken++;
                            Plan(PlanKind.SyncNotTaken, rel, S, S.Size, S.Mtime, rec == null
                                ? "共有フォルダにだけある → このPCにはコピーしない（設定）"
                                : "このPCで削除した後に共有フォルダで更新 → このPCにはコピーしない（設定）");
                            continue;
                        }
                        AddDown(downs, new SyncDown(rel, S, false));
                        continue;
                    }
                    // 記録があり変わっていない＝このPCで消した → 取り込み直さず、共有フォルダからも消すかを確認してもらう
                    if (S != null && rec != null)
                    {
                        AddSyncCandidate(rel, SyncDeleteItem.SideShared, S.Size, S.Mtime);
                        Plan(PlanKind.SyncDelete, rel, S, S.Size, S.Mtime, "このPCで削除した → 共有フォルダからも消すか確認（消す場合は「削除済み」に移す）");
                    }
                    continue;
                }
                if (!lChanged && !sChanged) continue;
                if (rec != null && lChanged && !sChanged) { AddTask(ups, new CopyTask(rel, L, true)); continue; }
                if (rec != null && !lChanged && sChanged) { AddDown(downs, new SyncDown(rel, S, true)); continue; }

                // 初回、または両方で変わった：中身が同じなら記録だけ、違えば確認待ち
                if (L.Size == S.Size && (Math.Abs(L.Mtime - S.Mtime) <= tol || SameContent(PathUtil.Combine(src, rel), PathUtil.Combine(dst, rel))))
                {
                    res.SameAdopted++;
                    Plan(PlanKind.Same, rel, L, S.Size, S.Mtime, "このPCと共有フォルダで同じ");
                    if (!dryRun) UpdateRecord(rel, L, S.Mtime, null, true, rec);
                    continue;
                }
                AddConflict(conflicts, MakeConflict(rel, L, S.Size, S.Mtime, rec == null ? "first" : "both"));
            }
        }

        /// <summary>
        /// 同期：共有フォルダへコピーした後、前回の確認の結果（覚えている一覧）にも反映し、共有フォルダの「変更の印」を新しくする
        /// （他のPCは印が変わったことで、次は詳しく確認する）
        /// </summary>
        private void AfterSyncUpload(List<CopyTask> ups)
        {
            List<KeyValuePair<string, ScanItem>> done = new List<KeyValuePair<string, ScanItem>>();
            foreach (CopyTask t in ups)
            {
                try
                {
                    FileInfo fi = new FileInfo(PathUtil.Long(PathUtil.Combine(dst, t.Rel)));
                    if (!fi.Exists) continue;
                    ScanItem it = new ScanItem(); it.Size = fi.Length; it.Mtime = fi.LastWriteTimeUtc.Ticks; it.Attr = fi.Attributes;
                    done.Add(new KeyValuePair<string, ScanItem>(t.Rel, it));
                }
                catch (Exception ex) { Logger.Error(job.Name, "共有フォルダの確認: " + t.Rel + "　" + ex.Message); SyncRemoteCache.Invalidate(job.Id); }
            }
            if (done.Count == 0) return;
            string ns = SyncRemoteCache.TouchStamp(dst);
            SyncRemoteCache.Update(job.Id, done, ns);
        }

        /// <summary>同期：削除の確認待ち（今回の判定で見つかったもの）</summary>
        public readonly List<SyncDeleteItem> SyncCandidates = new List<SyncDeleteItem>();

        private void AddSyncCandidate(string rel, string side, long size, long mtime)
        {
            SyncDeleteItem i = new SyncDeleteItem();
            i.Rel = rel; i.Side = side; i.Size = size; i.Mtime = mtime; i.Detected = Fmt.Now();
            SyncCandidates.Add(i);
            current.SyncDeleteCandidates++;
        }

        private static bool SameContent(string a, string b)
        {
            if (!QuickSame(a, b)) return false;
            byte[] x = HashFile(a), y = HashFile(b);
            return x != null && y != null && BitConverter.ToString(x) == BitConverter.ToString(y);
        }

        /// <summary>同期：共有フォルダの新しいファイルをこのPCのフォルダへ取り込む（前の版は控えてから）</summary>
        private void ExecuteDownloads(List<SyncDown> list, RunResult res)
        {
            string backupRoot = Path.Combine(Path.Combine(AppPaths.SyncBackupDir, job.Id), batch);
            bool idReliable = IsIdReliable(src);
            foreach (SyncDown d in list)
            {
                ct.ThrowIfCancellationRequested();
                Stopwatch w = Stopwatch.StartNew();
                string from = PathUtil.Combine(dst, d.Rel), to = PathUtil.Combine(src, d.Rel);
                try
                {
                    ActiveFile af = Progress.Begin(d.Rel, d.Item.Size, "取り込み");
                    byte[] hash;
                    bool okIn = false;
                    try
                    {
                        hash = SafeFs.CopyIntoSync(from, to, d.Rel, new DateTime(d.Item.Mtime, DateTimeKind.Utc), backupRoot, ct,
                            delegate(long done) { Progress.Bytes(af, done); });
                        okIn = true;
                    }
                    finally { Progress.End(af, okIn); }
                    FileRecord rec = new FileRecord();
                    rec.Size = d.Item.Size;
                    try { rec.Mtime = File.GetLastWriteTimeUtc(PathUtil.Long(to)).Ticks; } catch (Exception) { rec.Mtime = d.Item.Mtime; }
                    rec.DestMtime = d.Item.Mtime;
                    rec.Hash = hash;
                    rec.CopiedByApp = true;
                    if (idReliable)
                    {
                        uint vol; ulong idx;
                        if (Native.TryGetFileId(PathUtil.Long(to), out vol, out idx)) { rec.Volume = vol; rec.FileIndex = idx; }
                    }
                    lock (state) { state.Files[d.Rel] = rec; }
                    w.Stop();
                    Logger.Write(job.Name, "SYNCIN", d.Rel + (d.Overwrite ? "　（共有フォルダの新しい版で更新。前の版は控えに保存）" : "　（共有フォルダから取り込み）"), d.Item.Size, w.Elapsed.TotalSeconds);
                    res.Downloaded++;
                    res.DownloadBytes += d.Item.Size;
                }
                catch (SourceProtectedException) { throw; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    string kind = ErrorKind.Classify(ex);
                    if (kind == ErrorKind.NotFound) SyncRemoteCache.Invalidate(job.Id);   // 前回の確認の後で消えた：次は詳しく確認する
                    bool later = kind == ErrorKind.Locked || kind == ErrorKind.Changed || kind == ErrorKind.NotFound || IsSharingViolation(ex as IOException);
                    Logger.Write(job.Name, "ERROR", d.Rel + "　" + (later ? "【" + ErrorKind.Label(kind == ErrorKind.Changed || kind == ErrorKind.NotFound ? kind : ErrorKind.Locked) + "】次回に取り込みます" : "取り込めません: " + ErrorKind.Describe(ex)), d.Item.Size, -1);
                    if (later) res.Locked++; else res.Errors++;
                    if (ErrorKind.IsFatal(kind))
                        throw new JobAbortException(kind, (kind == ErrorKind.DiskFull ? "このPCの容量が足りないため、取り込みを止めました" : "ネットワークが切断されたため、取り込みを止めました") + "（" + ex.Message + "）", ex);
                }
            }
        }

        /// <summary>同期で控えた古い版のうち、保存期間を過ぎたものを片付ける（このPCの中）</summary>
        private void PurgeSyncBackup()
        {
            string root = Path.Combine(AppPaths.SyncBackupDir, job.Id);
            if (!Directory.Exists(root)) return;
            DateTime limit = DateTime.Now.AddDays(-Math.Max(1, app.TrashDays));
            foreach (string dir in Directory.GetDirectories(root))
            {
                DateTime t;
                if (!DateTime.TryParseExact(Path.GetFileName(dir), "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out t) || t >= limit) continue;
                try { Directory.Delete(dir, true); } catch (Exception ex) { Logger.Error(job.Name, "古い控えを片付けられません: " + dir + "　" + ex.Message); }
            }
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
                    ActiveFile af = Progress.Begin(t.Rel, t.Item.Size, t.Overwrite ? "上書き" : "コピー");
                    bool fileOk = false;
                    string backedUp = null;
                    try
                    {
                        if (t.Overwrite && job.BackupBeforeOverwrite && File.Exists(PathUtil.Long(d)))
                            backedUp = SafeFs.MoveToTrash(dst, t.Rel, "退避", batch);
                        byte[] hash;
                        try
                        {
                            hash = SafeFs.CopyFile(s, d, new DateTime(t.Item.Mtime, DateTimeKind.Utc), throttle, stopAll.Token,
                                delegate(long done) { Progress.Bytes(af, done); });
                        }
                        catch
                        {
                            // 上書きに失敗：退避した元のファイルをコピー先に戻す（コピー先を空にしない）
                            if (backedUp != null && !File.Exists(PathUtil.Long(d)))
                            {
                                try { SafeFs.Move(backedUp, d); }
                                catch (Exception rex) { Logger.Write(job.Name, "ERROR", t.Rel + "　上書きに失敗し、元のファイルを戻せませんでした（退避に残っています: " + PathUtil.Plain(backedUp) + "）: " + rex.Message, -1, -1); }
                            }
                            throw;
                        }
                        fileOk = true;
                        FileRecord old;
                        lock (state) state.Files.TryGetValue(t.Rel, out old);
                        FileRecord rec = new FileRecord();
                        rec.CopiedAt = old != null && old.PlaceholderOf == null ? FileRecord.FirstCopied(old) : DateTime.UtcNow.Ticks;
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
                        if (t.RemovePlaceholder)
                        {
                            try { SafeFs.DeleteFile(PathUtil.Combine(dst, t.Rel + AppPaths.PlaceholderSuffix)); }   // 自分で置いたテキストだけ
                            catch (SourceProtectedException) { throw; }
                            catch (Exception) { }
                        }
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
                    catch (Exception ex)
                    {
                        string kind = ErrorKind.Classify(ex);
                        bool later = kind == ErrorKind.Locked || kind == ErrorKind.Changed || IsSharingViolation(ex as IOException);
                        Logger.Write(job.Name, "ERROR", t.Rel + "　" + (later ? "【" + ErrorKind.Label(kind == ErrorKind.Changed ? kind : ErrorKind.Locked) + "】次回に回します" : ErrorKind.Describe(ex)), t.Item.Size, -1);
                        lock (resLock) { if (later) res.Locked++; else res.Errors++; }
                        if (ErrorKind.IsFatal(kind))
                        {
                            // ネットワークの切断・容量不足：続けても失敗するだけなので、ジョブを止める
                            lock (resLock) { if (fatal == null) fatal = new JobAbortException(kind, (kind == ErrorKind.DiskFull ? "コピー先の容量が足りないため、コピーを止めました" : "ネットワークが切断されたため、コピーを止めました") + "（" + ex.Message + "）", ex); }
                            stopAll.Cancel();
                        }
                    }
                    finally { Progress.End(af, fileOk); }
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
            if (ex == null) return false;
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
            if (old != null) { rec.Volume = old.Volume; rec.FileIndex = old.FileIndex; rec.CopiedAt = old.CopiedAt; }
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
            if (dryRun)
            {
                res.Deleted = candidates.Count;
                int tot = Math.Max(1, files.Count);
                bool many = candidates.Count > job.MirrorMaxCount
                    || (candidates.Count > 10 && candidates.Count * 100 > tot * Math.Max(1, job.MirrorMaxPercent));
                foreach (string c in candidates)
                {
                    PlanItem pi = new PlanItem();
                    pi.Kind = many ? PlanKind.Hold : PlanKind.Delete; pi.Rel = c; pi.DstSize = files[c].Size;
                    pi.Note = many ? "コピー元で削除。件数が多いため、削除する前に確認を求める" : "コピー元で削除 → コピー先から「削除済み」へ移す";
                    AddPlan(pi);
                }
                return;
            }

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
                string trashRel = rec != null && rec.PlaceholderOf != null ? rel + AppPaths.PlaceholderSuffix : rel;  // 重複のテキストを置いたものはテキストを片付ける
                string d = PathUtil.Combine(dst, trashRel);
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
                    SafeFs.MoveToTrash(dst, trashRel, "削除済み", batch);
                    Logger.Write(job.Name, "DELETE", trashRel + "　（ミラー：コピー元で削除）", fi.Length, -1);
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
            SafeFs.HideMeta(dst);
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

    /// <summary>同期：削除の確認待ち1件</summary>
    [DataContract]
    public class SyncDeleteItem
    {
        public const string SideLocal = "local";     // 共有フォルダで消えた → このPCから消すか
        public const string SideShared = "shared";   // このPCで消した → 共有フォルダから消すか
        [DataMember] public string Rel;
        [DataMember] public string Side;
        [DataMember] public long Size;
        [DataMember] public long Mtime;
        [DataMember] public string Detected;

        public string SideText { get { return Side == SideLocal ? "共有フォルダで削除 → このPCからも消す" : "このPCで削除 → 共有フォルダからも消す"; } }
    }

    [DataContract]
    public class SyncDeleteBook
    {
        [DataMember] public List<SyncDeleteItem> Items = new List<SyncDeleteItem>();
    }

    /// <summary>同期の削除の確認待ちを、ジョブごとに保存する（state\&lt;id&gt;.syncdel.json）</summary>
    public static class SyncDeleteStore
    {
        private static readonly Dictionary<string, SyncDeleteBook> cache = new Dictionary<string, SyncDeleteBook>();

        public static string PathFor(string jobId) { return Path.Combine(AppPaths.StateDir, jobId + ".syncdel.json"); }

        public static SyncDeleteBook Get(string jobId)
        {
            lock (cache)
            {
                SyncDeleteBook b;
                if (cache.TryGetValue(jobId, out b)) return b;
                string problem;
                b = Json.LoadSafe<SyncDeleteBook>(PathFor(jobId), out problem);
                if (problem != null) Logger.Error(null, "同期の削除の確認：" + problem + "（候補は次の実行で見つけ直します。削除はしません）");
                if (b == null) b = new SyncDeleteBook();
                if (b.Items == null) b.Items = new List<SyncDeleteItem>();
                cache[jobId] = b;
                return b;
            }
        }

        public static List<SyncDeleteItem> GetItems(string jobId)
        {
            SyncDeleteBook b = Get(jobId);
            lock (b) return new List<SyncDeleteItem>(b.Items);
        }

        public static int Count(string jobId)
        {
            SyncDeleteBook b = Get(jobId);
            lock (b) return b.Items.Count;
        }

        public static void Save(string jobId)
        {
            SyncDeleteBook b = Get(jobId);
            // 保存できなければ例外にする（握りつぶさない）：実行はエラーとして知らせ、確認の画面ではエラーを表示する
            lock (b)
            {
                try { Directory.CreateDirectory(AppPaths.StateDir); Json.Save(PathFor(jobId), b); }
                catch (Exception ex)
                {
                    Logger.Error(null, "同期の削除の確認を保存できません: " + ex.Message);
                    throw new IOException("同期の削除の確認を保存できませんでした（" + ex.Message + "）。削除・取り込みは行っていません。", ex);
                }
            }
        }

        /// <summary>今回の判定の結果で置き換える（見つけた日時は前のものを引き継ぐ）。新しく見つかった件数を返す。</summary>
        public static int Replace(string jobId, List<SyncDeleteItem> now)
        {
            SyncDeleteBook b = Get(jobId);
            int fresh = 0;
            lock (b)
            {
                Dictionary<string, SyncDeleteItem> old = new Dictionary<string, SyncDeleteItem>(StringComparer.OrdinalIgnoreCase);
                foreach (SyncDeleteItem i in b.Items) old[i.Side + "|" + i.Rel] = i;
                List<SyncDeleteItem> list = new List<SyncDeleteItem>();
                foreach (SyncDeleteItem i in now)
                {
                    SyncDeleteItem o;
                    if (old.TryGetValue(i.Side + "|" + i.Rel, out o)) i.Detected = o.Detected; else fresh++;
                    list.Add(i);
                }
                bool same = list.Count == b.Items.Count && fresh == 0;
                b.Items = list;
                if (same) return 0;
            }
            Save(jobId);
            return fresh;
        }

        public static void Remove(string jobId, IEnumerable<SyncDeleteItem> items)
        {
            SyncDeleteBook b = Get(jobId);
            lock (b)
            {
                HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SyncDeleteItem i in items) keys.Add(i.Side + "|" + i.Rel);
                b.Items.RemoveAll(delegate(SyncDeleteItem x) { return keys.Contains(x.Side + "|" + x.Rel); });
            }
            Save(jobId);
        }

        public static void Forget(string jobId)
        {
            lock (cache) cache.Remove(jobId);
            try { File.Delete(PathFor(jobId)); File.Delete(PathFor(jobId) + ".bak"); }
            catch (Exception ex) { Logger.Error(null, "同期の削除の確認のファイルを消せません: " + ex.Message); }
        }
    }

    /// <summary>
    /// 同期：共有フォルダの一覧（前回の詳しい確認の結果）を覚えておき、時間のかかる確認を省略できるようにする。
    /// 共有フォルダの「_AutoCopy\sync-stamp.txt」（変更の印）は、どのPCでも共有フォルダを変えたら新しくする。
    /// 印が前回と同じで、前回の詳しい確認から決めた時間以内なら、前回の結果を使う。
    /// （自動コピー以外の方法で共有フォルダを直接変えた場合は、決めた時間がたってから反映される）
    /// </summary>
    public static class SyncRemoteCache
    {
        public class Entry
        {
            public string Root;
            public ScanResult Remote;
            public DateTime FullScanAt;
            public string Stamp;

            public ScanResult CopyRemote()
            {
                ScanResult r = new ScanResult();
                lock (this)
                {
                    foreach (KeyValuePair<string, ScanItem> kv in Remote.Files)
                    {
                        ScanItem it = new ScanItem(); it.Size = kv.Value.Size; it.Mtime = kv.Value.Mtime; it.Attr = kv.Value.Attr; it.Recent = false;
                        r.Files[kv.Key] = it;
                    }
                    foreach (string d in Remote.Dirs) r.Dirs.Add(d);
                }
                return r;
            }
        }

        private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        public const string StampName = "sync-stamp.txt";

        private static string StampPath(string dst) { return PathUtil.Combine(PathUtil.Combine(dst, AppPaths.DestMetaFolder), StampName); }

        /// <summary>共有フォルダの変更の印（ない = ""、読めない = null）</summary>
        public static string ReadStamp(string dst)
        {
            try
            {
                string p = PathUtil.Long(StampPath(dst));
                return File.Exists(p) ? File.ReadAllText(p).Trim() : "";
            }
            catch (Exception ex) { Logger.Error(null, "共有フォルダの変更の印を読めません（毎回詳しく確認します）: " + ex.Message); return null; }
        }

        /// <summary>共有フォルダを変えたので、印を新しくする（他のPCが次に詳しく確認するように）</summary>
        public static string TouchStamp(string dst)
        {
            string v = Guid.NewGuid().ToString("N") + "|" + Environment.MachineName + "|" + DateTime.UtcNow.ToString("o");
            try { SafeFs.WriteText(StampPath(dst), v); SafeFs.HideMeta(dst); return v; }
            catch (Exception ex) { Logger.Error(null, "共有フォルダの変更の印を書けません: " + ex.Message); return null; }
        }

        public static Entry Get(string jobId, string dst)
        {
            lock (cache)
            {
                Entry e;
                if (cache.TryGetValue(jobId, out e) && string.Equals(e.Root, dst, StringComparison.OrdinalIgnoreCase)) return e;
                return null;
            }
        }

        public static void Put(string jobId, string dst, ScanResult remote, string stamp)
        {
            Entry e = new Entry();
            e.Root = dst; e.Remote = remote; e.FullScanAt = DateTime.Now; e.Stamp = stamp;
            lock (cache) cache[jobId] = e;
        }

        /// <summary>自分で共有フォルダへコピーしたものを、覚えている一覧に反映する（印も自分が書いた新しいものにする）</summary>
        public static void Update(string jobId, List<KeyValuePair<string, ScanItem>> items, string newStamp)
        {
            Entry e;
            lock (cache) { if (!cache.TryGetValue(jobId, out e)) return; }
            if (newStamp == null) { Invalidate(jobId); return; }
            lock (e)
            {
                foreach (KeyValuePair<string, ScanItem> kv in items) e.Remote.Files[kv.Key] = kv.Value;
                e.Stamp = newStamp;
            }
        }

        public static void Invalidate(string jobId)
        {
            lock (cache) cache.Remove(jobId);
        }
    }
}
