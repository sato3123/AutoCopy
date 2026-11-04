using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy
{
    public enum NoticeLevel { Ok, Info, Warning, Error }

    public class Notice
    {
        public DateTime Time;
        public NoticeLevel Level;
        public string Title;
        public string Text;
        public string Action;   // conflicts / dup / pending / job / log / retry
        public string JobId;
    }

    public class JobRuntime
    {
        public DateTime NextRun;
        public bool Running;
        public bool DupRunning;
        public string Phase = "";
        public CancellationTokenSource Cts;
        public DateTime? PausedUntil;
        public DateTime LastRun;
        public string LastResult = "";     // ok / error / ""
        public string LastMessage = "";
    }

    /// <summary>アプリの中心。スケジュール、ジョブの実行、通知を受け持つ。</summary>
    public static class AppCore
    {
        public static AppSettings Settings;
        private static readonly object sync = new object();
        private static readonly Dictionary<string, JobState> states = new Dictionary<string, JobState>();
        private static readonly Dictionary<string, JobRuntime> runtimes = new Dictionary<string, JobRuntime>();
        private static readonly List<Notice> notices = new List<Notice>();
        private static Timer timer;
        private static DateTime? globalPauseUntil;
        private static DateTime lastFlush = DateTime.MinValue, lastPurge = DateTime.MinValue, lastUnsentWarn = DateTime.MinValue;
        private static readonly Dictionary<string, DateTime> lastErrorNotice = new Dictionary<string, DateTime>();

        public static event Action<Notice> NoticeAdded;
        public static event Action Changed;

        // ---------------- 設定 ----------------

        public static bool SettingsExist() { return File.Exists(AppPaths.SettingsFile); }

        public static void LoadSettings()
        {
            AppSettings s = null;
            try { s = Json.Load<AppSettings>(AppPaths.SettingsFile); }
            catch (Exception ex)
            {
                try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".broken", true); } catch (Exception) { }
                Logger.Error(null, "設定ファイルを読めませんでした: " + ex.Message);
            }
            if (s == null) s = new AppSettings();
            if (string.IsNullOrEmpty(s.InstanceId)) s.InstanceId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(s.FolderName)) s.FolderName = Environment.MachineName;
            Settings = s;
            ApplySettings(true);
        }

        public static void SaveSettings()
        {
            lock (sync) { Json.Save(AppPaths.SettingsFile, Settings); }
            ApplySettings(false);
        }

        private static void ApplySettings(bool startup)
        {
            List<string> sources = new List<string>();
            foreach (JobConfig j in Settings.Jobs) sources.Add(CopyEngine.ResolvedSource(j, Settings));
            SafeFs.SetProtectedSources(sources);
            List<string> syncs = new List<string>();
            foreach (JobConfig j in Settings.Jobs) if (j.Sync) syncs.Add(CopyEngine.ResolvedSource(j, Settings));
            SafeFs.SetSyncRoots(syncs);
            Logger.Configure(Settings);
            lock (sync)
            {
                HashSet<string> ids = new HashSet<string>();
                foreach (JobConfig j in Settings.Jobs)
                {
                    ids.Add(j.Id);
                    JobRuntime rt;
                    if (!runtimes.TryGetValue(j.Id, out rt))
                    {
                        rt = new JobRuntime();
                        int delayMin = (startup && j.UseStartupDelay) ? Math.Max(0, j.StartupDelayMinutes) : 0;
                        rt.NextRun = DateTime.Now.AddMinutes(delayMin).AddSeconds(startup ? 5 : 10);
                        JobState st = GetStateNoLock(j.Id);
                        rt.LastResult = st.LastResult ?? "";
                        rt.LastMessage = st.LastMessage ?? "";
                        if (st.LastRunTicks > 0) rt.LastRun = new DateTime(st.LastRunTicks, DateTimeKind.Utc).ToLocalTime();
                        runtimes[j.Id] = rt;
                    }
                }
                List<string> gone = new List<string>();
                foreach (string id in runtimes.Keys) if (!ids.Contains(id)) gone.Add(id);
                foreach (string id in gone) runtimes.Remove(id);
            }
            RaiseChanged();
        }

        public static JobConfig FindJob(string idOrName)
        {
            foreach (JobConfig j in Settings.Jobs)
                if (j.Id == idOrName || string.Equals(j.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return j;
            return null;
        }

        private static JobState GetStateNoLock(string id)
        {
            JobState s;
            if (!states.TryGetValue(id, out s)) { s = JobState.Load(id); states[id] = s; }
            return s;
        }

        public static JobState GetState(string id)
        {
            lock (sync) { return GetStateNoLock(id); }
        }

        public static JobRuntime GetRuntime(string id)
        {
            lock (sync)
            {
                JobRuntime rt;
                if (!runtimes.TryGetValue(id, out rt)) { rt = new JobRuntime(); rt.NextRun = DateTime.Now.AddSeconds(10); runtimes[id] = rt; }
                return rt;
            }
        }

        public static void SaveState(string id)
        {
            JobState s = GetState(id);
            try { lock (s) { s.Save(); } }
            catch (Exception ex) { Logger.Error(null, "状態ファイルを保存できません: " + ex.Message); }
        }

        public static void ForgetJobState(string id)
        {
            lock (sync)
            {
                JobState s;
                if (states.TryGetValue(id, out s)) { s.Delete(); states.Remove(id); }
            }
        }

        // ---------------- スケジュール ----------------

        public static void Start()
        {
            timer = new Timer(delegate(object o) { Tick(); }, null, 3000, 15000);
        }

        public static void Stop()
        {
            if (timer != null) timer.Dispose();
            lock (sync) { foreach (JobRuntime rt in runtimes.Values) if (rt.Cts != null) rt.Cts.Cancel(); }
        }

        private static int ticking;

        private static void Tick()
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1) return;
            try
            {
                DateTime now = DateTime.Now;
                if (globalPauseUntil.HasValue && now >= globalPauseUntil.Value) { globalPauseUntil = null; Notify(NoticeLevel.Info, "自動コピーを再開しました", "一時停止の時間が過ぎました。", null, null); }
                foreach (JobConfig j in Settings.Jobs.ToArray())
                {
                    JobRuntime rt = GetRuntime(j.Id);
                    if (rt.PausedUntil.HasValue && now >= rt.PausedUntil.Value) rt.PausedUntil = null;
                    if (!j.Enabled || IsPaused(j.Id)) continue;
                    if (!rt.Running && !rt.DupRunning && now >= rt.NextRun && TimeUtil.InWindow(j, now)) RunJob(j.Id, false, false);
                    else if (!rt.Running && !rt.DupRunning && DupDue(j, now)) RunDup(j.Id);
                }
                if ((now - lastFlush).TotalMinutes >= 10)
                {
                    lastFlush = now;
                    Task.Factory.StartNew(delegate() { FlushLogs(); });
                }
                if ((now - lastPurge).TotalHours >= 24)
                {
                    lastPurge = now;
                    Task.Factory.StartNew(delegate() { Logger.Purge(Settings); });
                }
            }
            catch (Exception ex) { Logger.Error(null, "スケジュール処理でエラー: " + ex.Message); }
            finally { Interlocked.Exchange(ref ticking, 0); }
        }

        private static void FlushLogs()
        {
            Logger.Flush();
            DateTime? oldest = Logger.OldestUnsent();
            if (oldest.HasValue && (DateTime.Now - oldest.Value).TotalDays >= 1 && (DateTime.Now - lastUnsentWarn).TotalDays >= 1)
            {
                lastUnsentWarn = DateTime.Now;
                Notify(NoticeLevel.Warning, "ログを保管先に送れていません",
                    "1日以上、ネットワークのログ保管先にログを送れていません。" + (Logger.LastSendError ?? ""), "log", null);
            }
        }

        private static bool DupDue(JobConfig j, DateTime now)
        {
            if (!j.DupCheckEnabled || j.DupSchedule == "manual") return false;
            TimeSpan t;
            if (!TimeUtil.TryParseHm(j.DupTime, out t)) t = new TimeSpan(12, 0, 0);
            DateTime todayAt = now.Date + t;
            if (now < todayAt) return false;
            if (j.DupSchedule == "weekly" && (int)now.DayOfWeek != j.DupWeekday) return false;
            JobState st = GetState(j.Id);
            DateTime last;
            lock (st) { last = st.LastDupCheckTicks > 0 ? new DateTime(st.LastDupCheckTicks, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue; }
            return last < todayAt;
        }

        // ---------------- 一時停止 ----------------

        public static bool IsPaused(string jobId)
        {
            if (globalPauseUntil.HasValue && DateTime.Now < globalPauseUntil.Value) return true;
            if (jobId != null)
            {
                JobRuntime rt = GetRuntime(jobId);
                if (rt.PausedUntil.HasValue && DateTime.Now < rt.PausedUntil.Value) return true;
            }
            return false;
        }

        public static bool GlobalPaused { get { return globalPauseUntil.HasValue && DateTime.Now < globalPauseUntil.Value; } }
        public static DateTime? GlobalPauseUntil { get { return globalPauseUntil; } }

        /// <summary>一時停止（保存しないので、PCを再起動すると必ず再開する）。minutes 0 = 再開するまで</summary>
        public static void Pause(string jobId, int minutes)
        {
            DateTime until = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MaxValue;
            if (jobId == null) globalPauseUntil = until;
            else GetRuntime(jobId).PausedUntil = until;
            Logger.Info(jobId == null ? null : FindJobName(jobId), "一時停止しました（" + (minutes > 0 ? minutes + "分" : "再開するまで") + "）");
            RaiseChanged();
        }

        public static void Resume(string jobId)
        {
            if (jobId == null)
            {
                globalPauseUntil = null;
                lock (sync) { foreach (JobRuntime rt in runtimes.Values) rt.PausedUntil = null; }
            }
            else GetRuntime(jobId).PausedUntil = null;
            Logger.Info(jobId == null ? null : FindJobName(jobId), "自動コピーを再開しました");
            RaiseChanged();
        }

        private static string FindJobName(string id)
        {
            JobConfig j = FindJob(id);
            return j == null ? null : j.Name;
        }

        // ---------------- 実行 ----------------

        public static void RunAll(bool manual)
        {
            foreach (JobConfig j in Settings.Jobs.ToArray())
                if (j.Enabled) RunJob(j.Id, manual, false);
        }

        public static void CancelJob(string jobId)
        {
            JobRuntime rt = GetRuntime(jobId);
            if (rt.Cts != null) rt.Cts.Cancel();
        }

        public static bool RunJob(string jobId, bool manual, bool fullVerify)
        {
            JobConfig job = FindJob(jobId);
            if (job == null) return false;
            JobRuntime rt = GetRuntime(job.Id);
            lock (rt)
            {
                if (rt.Running || rt.DupRunning) return false;
                rt.Running = true;
                rt.Phase = "コピー中";
                rt.Cts = new CancellationTokenSource();
            }
            DateTime startedAt = DateTime.Now;
            rt.NextRun = startedAt.AddMinutes(Math.Max(1, job.IntervalMinutes));
            RaiseChanged();
            CancellationToken ct = rt.Cts.Token;
            Task.Factory.StartNew(delegate()
            {
                JobState st = GetState(job.Id);
                RunResult res = null;
                string error = null;
                bool credError = false;
                try
                {
                    CopyEngine eng = new CopyEngine(job, Settings, st, false, false, ct);
                    eng.FullVerify = fullVerify;
                    res = eng.Run();
                    if (res.Failed) error = res.Message;
                }
                catch (OperationCanceledException) { error = "中止しました"; }
                catch (AccessException ex) { error = ex.Message; credError = ex.IsCredentialError; }
                catch (FolderConflictException ex) { error = ex.Message; }
                catch (SourceProtectedException ex) { error = ex.Message + "（このジョブを停止しました）"; job.Enabled = false; }
                catch (Exception ex) { error = ex.Message; }

                rt.LastRun = startedAt;
                rt.LastResult = error == null ? "ok" : "error";
                rt.LastMessage = error ?? (res != null ? res.Summary() : "");
                lock (st)
                {
                    st.LastRunTicks = startedAt.ToUniversalTime().Ticks;
                    st.LastResult = rt.LastResult;
                    st.LastMessage = rt.LastMessage;
                }
                SaveState(job.Id);
                if (res != null) Logger.Write(job.Name, "SUMMARY", res.Summary(), res.Bytes, res.Seconds);
                if (error != null)
                {
                    Logger.Error(job.Name, error);
                    NotifyError(job, error, credError);
                }
                if (res != null)
                {
                    if (res.Conflicts > 0)
                        Notify(NoticeLevel.Warning, "上書きの確認が " + res.Conflicts + "件 あります",
                            job.Name + "：コピー先の方が新しいファイルがあるため、上書きせずに待っています。", "conflicts", job.Id);
                    if (res.PendingDeletes > 0)
                        Notify(NoticeLevel.Warning, "ミラー：" + res.PendingDeletes + "件の削除を保留しています",
                            job.Name + "：一度に多くのファイルが削除されるため、確認をお待ちしています。", "pending", job.Id);
                    if (res.Renamed > 0)
                        AddNoticeOnly(NoticeLevel.Ok, job.Name + "：名前の変更 " + res.Renamed + "件 をコピー先にも反映しました（再コピーなし）", "log", job.Id);
                    if (res.Locked > 0)
                        AddNoticeOnly(NoticeLevel.Info, job.Name + "：使用中のファイル " + res.Locked + "件 は次回コピーします", "log", job.Id);
                    if (error == null && Settings.NotifyDone && (res.Copied + res.Overwritten + res.Deleted + res.Renamed) > 0)
                        Notify(NoticeLevel.Ok, job.Name + "：コピーしました", res.Summary(), "log", job.Id);
                }
                lock (rt) { rt.Running = false; rt.Phase = ""; }
                if (job.Enabled == false) { try { SaveSettings(); } catch (Exception) { } }
                PublishStatus();
                RaiseChanged();
                Logger.Flush();
            }, TaskCreationOptions.LongRunning);
            return true;
        }

        private static void NotifyError(JobConfig job, string error, bool credError)
        {
            // 同じ原因のエラーは1時間に1回まで
            string key = job.Id + "|" + error;
            lock (lastErrorNotice)
            {
                DateTime t;
                if (lastErrorNotice.TryGetValue(key, out t) && (DateTime.Now - t).TotalHours < 1)
                {
                    AddNoticeOnly(NoticeLevel.Error, job.Name + "：" + error, credError ? "job" : "retry", job.Id);
                    return;
                }
                lastErrorNotice[key] = DateTime.Now;
            }
            Notify(NoticeLevel.Error, job.Name + "：エラー", error, credError ? "job" : "retry", job.Id);
        }

        /// <summary>コピーせずに件数を数える（テスト・ミラーの注意画面用）</summary>
        public static RunResult DryRun(JobConfig job, bool forceMirror)
        {
            JobState st = job.Id != null ? GetState(job.Id) : new JobState();
            JobConfig quick = job.Clone();
            quick.RetryCount = 0; // 画面で待たせないよう、数えるだけのときは再試行しない
            CopyEngine eng = new CopyEngine(quick, Settings, st, true, forceMirror, CancellationToken.None);
            return eng.Run();
        }

        public static bool RunDup(string jobId)
        {
            JobConfig job = FindJob(jobId);
            if (job == null) return false;
            JobRuntime rt = GetRuntime(job.Id);
            lock (rt)
            {
                if (rt.Running || rt.DupRunning) return false;
                rt.DupRunning = true;
                rt.Phase = "重複チェック中";
                rt.Cts = new CancellationTokenSource();
            }
            RaiseChanged();
            CancellationToken ct = rt.Cts.Token;
            Task.Factory.StartNew(delegate()
            {
                JobState st = GetState(job.Id);
                try
                {
                    DateTime t0 = DateTime.Now;
                    DupResult r = DupChecker.Run(job, Settings, st, ct);
                    SaveState(job.Id);
                    int n = r.DeleteCandidateCount();
                    Logger.Write(job.Name, "DUPCHECK", "重複チェック完了：削除候補 " + n + "件（" + Fmt.Size(r.DeleteCandidateBytes()) + "）・グループ " + r.Groups.Count + "件",
                        -1, (DateTime.Now - t0).TotalSeconds);
                    if (r.Groups.Count > 0)
                        Notify(NoticeLevel.Info, "重複チェック：" + (n > 0 ? "削除候補 " + n + "件（" + Fmt.Size(r.DeleteCandidateBytes()) + "）" : "情報 " + r.Groups.Count + "件"),
                            job.Name + "：結果を確認してください。削除は確認後に行います。", "dup", job.Id);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    lock (st) { st.LastDupCheckTicks = DateTime.UtcNow.Ticks; }
                    Logger.Error(job.Name, "重複チェックでエラー: " + ex.Message);
                    Notify(NoticeLevel.Error, job.Name + "：重複チェックのエラー", ex.Message, "log", job.Id);
                }
                lock (rt) { rt.DupRunning = false; rt.Phase = ""; }
                RaiseChanged();
            }, TaskCreationOptions.LongRunning);
            return true;
        }

        public static void RunDupAll()
        {
            foreach (JobConfig j in Settings.Jobs.ToArray()) if (j.DupCheckEnabled) RunDup(j.Id);
        }

        // ---------------- 上書き確認・保留削除 ----------------

        public static int TotalConflicts()
        {
            int n = 0;
            foreach (JobConfig j in Settings.Jobs.ToArray()) { JobState s = GetState(j.Id); lock (s) { n += s.Conflicts.Count; } }
            return n;
        }

        public static int TotalPendingDeletes()
        {
            int n = 0;
            foreach (JobConfig j in Settings.Jobs.ToArray()) { JobState s = GetState(j.Id); lock (s) { n += s.PendingDeletes.Count; } }
            return n;
        }

        public const string ActOverwrite = "overwrite", ActSkipOnce = "skiponce", ActSkipForever = "skipforever", ActTakeShared = "takeshared";

        /// <summary>上書き確認の結果を反映する。overwrite はコピーを行う。</summary>
        public static string ResolveConflict(JobConfig job, ConflictItem c, string action, bool backup)
        {
            JobState st = GetState(job.Id);
            string src = CopyEngine.ResolvedSource(job, Settings), dst = CopyEngine.ResolvedDest(job, Settings);
            try
            {
                if (action == ActOverwrite)
                {
                    string s = PathUtil.Combine(src, c.Rel), d = PathUtil.Combine(dst, c.Rel);
                    FileInfo fi = new FileInfo(PathUtil.Long(s));
                    if (!fi.Exists) return "コピー元にファイルがありません: " + c.Rel;
                    if (backup && File.Exists(PathUtil.Long(d)))
                        SafeFs.MoveToTrash(dst, c.Rel, "退避", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    byte[] hash = SafeFs.CopyFile(s, d, fi.LastWriteTimeUtc, null, CancellationToken.None);
                    FileRecord rec = new FileRecord();
                    rec.Size = fi.Length; rec.Mtime = fi.LastWriteTimeUtc.Ticks; rec.Hash = hash; rec.CopiedByApp = true;
                    rec.DestMtime = File.GetLastWriteTimeUtc(PathUtil.Long(d)).Ticks;
                    lock (st) { st.Files[c.Rel] = rec; }
                    Logger.Write(job.Name, "OVERWRITE", c.Rel + "　（確認して上書き）", fi.Length, -1);
                }
                else if (action == ActTakeShared)
                {
                    // 同期：共有フォルダの版をこのPCに取り込む（このPCの版は控えに保存）
                    if (!job.Sync) return c.Rel + "：「共有フォルダの方を使う」は同期のジョブだけで使えます";
                    string s = PathUtil.Combine(dst, c.Rel), l = PathUtil.Combine(src, c.Rel);
                    FileInfo fi = new FileInfo(PathUtil.Long(s));
                    if (!fi.Exists) return "共有フォルダにファイルがありません: " + c.Rel;
                    string backupRoot = Path.Combine(Path.Combine(AppPaths.SyncBackupDir, job.Id), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    byte[] hash = SafeFs.CopyIntoSync(s, l, c.Rel, fi.LastWriteTimeUtc, backupRoot, CancellationToken.None);
                    FileRecord rec = new FileRecord();
                    rec.Size = fi.Length; rec.Mtime = File.GetLastWriteTimeUtc(PathUtil.Long(l)).Ticks; rec.Hash = hash; rec.CopiedByApp = true;
                    rec.DestMtime = fi.LastWriteTimeUtc.Ticks;
                    lock (st) { st.Files[c.Rel] = rec; }
                    Logger.Write(job.Name, "SYNCIN", c.Rel + "　（確認して共有フォルダの版を取り込み。このPCの版は控えに保存）", fi.Length, -1);
                }
                else if (action == ActSkipOnce)
                {
                    FileRecord rec = new FileRecord();
                    rec.Size = c.SrcSize; rec.Mtime = c.SrcMtime; rec.DestMtime = c.DstMtime; rec.CopiedByApp = false;
                    lock (st) { st.Files[c.Rel] = rec; }
                    Logger.Write(job.Name, "SKIP", c.Rel + "　（今回はスキップ）", c.SrcSize, -1);
                }
                else if (action == ActSkipForever)
                {
                    lock (st) { st.SkipForever.Add(c.Rel); }
                    Logger.Write(job.Name, "SKIP", c.Rel + "　（今後も上書きしない）", c.SrcSize, -1);
                }
                lock (st) { st.Conflicts.RemoveAll(delegate(ConflictItem x) { return string.Equals(x.Rel, c.Rel, StringComparison.OrdinalIgnoreCase); }); }
                SaveState(job.Id);
                RaiseChanged();
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(job.Name, c.Rel + "　" + ex.Message);
                return c.Rel + "：" + ex.Message;
            }
        }

        public static string ResolvePendingDeletes(JobConfig job, bool approve)
        {
            JobState st = GetState(job.Id);
            List<string> rels = new List<string>();
            lock (st) { foreach (PendingDelete p in st.PendingDeletes) rels.Add(p.Rel); }
            try
            {
                if (approve)
                {
                    CopyEngine eng = new CopyEngine(job, Settings, st, false, false, CancellationToken.None);
                    NetAccess.Ensure(CopyEngine.ResolvedDest(job, Settings), job.RetryCount, job.RetryIntervalSeconds, CancellationToken.None, null);
                    int n = eng.DeleteMirror(rels, null);
                    Logger.Info(job.Name, "ミラー：保留していた削除 " + n + "件 を実行しました");
                }
                else
                {
                    lock (st) { foreach (string r in rels) st.Files.Remove(r); }
                    Logger.Info(job.Name, "ミラー：保留していた削除 " + rels.Count + "件 を取りやめました（コピー先に残します）");
                }
                lock (st) { st.PendingDeletes.Clear(); }
                SaveState(job.Id);
                RaiseChanged();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------------- 通知・状態 ----------------

        public static void Notify(NoticeLevel level, string title, string text, string action, string jobId)
        {
            Notice n = AddNotice(level, title, text, action, jobId);
            bool show = level == NoticeLevel.Error ? Settings.NotifyErrors
                : level == NoticeLevel.Warning || level == NoticeLevel.Info ? Settings.NotifyConfirm : Settings.NotifyDone;
            if (show && NoticeAdded != null) { try { NoticeAdded(n); } catch (Exception) { } }
        }

        private static void AddNoticeOnly(NoticeLevel level, string text, string action, string jobId)
        {
            AddNotice(level, text, null, action, jobId);
            RaiseChanged();
        }

        private static Notice AddNotice(NoticeLevel level, string title, string text, string action, string jobId)
        {
            Notice n = new Notice();
            n.Time = DateTime.Now; n.Level = level; n.Title = title; n.Text = text; n.Action = action; n.JobId = jobId;
            lock (notices)
            {
                notices.Insert(0, n);
                if (notices.Count > 50) notices.RemoveAt(notices.Count - 1);
            }
            return n;
        }

        public static List<Notice> RecentNotices()
        {
            lock (notices) { return new List<Notice>(notices); }
        }

        public static void RaiseChanged()
        {
            Action h = Changed;
            if (h != null) { try { h(); } catch (Exception) { } }
        }

        public static bool AnyRunning()
        {
            lock (sync) { foreach (JobRuntime rt in runtimes.Values) if (rt.Running || rt.DupRunning) return true; }
            return false;
        }

        /// <summary>確認が必要なもの1件</summary>
        public class AttentionItem
        {
            public string Text;      // 例：上書きの確認 2件
            public string Action;    // conflicts / pending / dup / job
            public string JobId;
            public bool IsError;
        }

        /// <summary>今、あなたの確認が必要なもの（上書きの確認・ミラーの保留・未確認の重複チェック結果・エラー）</summary>
        public static List<AttentionItem> AttentionItems()
        {
            List<AttentionItem> list = new List<AttentionItem>();
            int c = TotalConflicts();
            if (c > 0) list.Add(Item("上書きの確認が " + c + "件 あります", "conflicts", null, false));
            foreach (JobConfig j in Settings.Jobs.ToArray())
            {
                JobState st = GetState(j.Id);
                int p;
                lock (st) { p = st.PendingDeletes.Count; }
                if (p > 0) list.Add(Item(j.Name + "：ミラーの削除 " + p + "件 が保留中です", "pending", j.Id, false));
            }
            foreach (JobConfig j in Settings.Jobs.ToArray())
            {
                int n = DupChecker.UnseenCandidates(j.Id);
                if (n > 0) list.Add(Item(j.Name + "：重複チェックで削除候補が " + n + "件 見つかりました", "dup", j.Id, false));
            }
            foreach (JobConfig j in Settings.Jobs.ToArray())
            {
                if (!j.Enabled) continue;
                JobRuntime rt = GetRuntime(j.Id);
                if (rt.LastResult == "error")
                    list.Add(Item(j.Name + "：エラー　" + (rt.LastMessage ?? ""), "main", j.Id, true));
            }
            return list;
        }

        private static AttentionItem Item(string text, string action, string jobId, bool err)
        {
            AttentionItem i = new AttentionItem();
            i.Text = text; i.Action = action; i.JobId = jobId; i.IsError = err;
            return i;
        }

        public static bool AnyError()
        {
            foreach (JobConfig j in Settings.Jobs.ToArray())
                if (j.Enabled && GetRuntime(j.Id).LastResult == "error") return true;
            return false;
        }

        public static void PublishStatus()
        {
            try
            {
                PcStatus st = new PcStatus();
                st.ComputerName = Environment.MachineName;
                st.FolderName = Settings.FolderName;
                st.UserName = Environment.UserName;
                st.Updated = Fmt.Now();
                st.Jobs = new List<JobStatusInfo>();
                foreach (JobConfig j in Settings.Jobs.ToArray())
                {
                    JobRuntime rt = GetRuntime(j.Id);
                    JobStatusInfo i = new JobStatusInfo();
                    i.Name = j.Name;
                    i.LastRun = rt.LastRun == DateTime.MinValue ? "" : rt.LastRun.ToString("yyyy-MM-dd HH:mm");
                    i.Result = rt.Running ? "running" : rt.LastResult;
                    i.Message = rt.LastMessage;
                    st.Jobs.Add(i);
                }
                Logger.WriteStatus(st);
            }
            catch (Exception) { }
        }
    }
}
