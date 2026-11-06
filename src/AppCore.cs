using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy
{
    public enum NoticeLevel { Ok, Info, Warning, Error }

    /// <summary>
    /// お知らせ1件。確認が必要なもの（上書きの確認・エラーなど）は「未対応」で始まり、
    /// 確認が済むと自動で（または手で）「対応済み」になる。ただのお知らせは最初から「対応不要」。
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public class Notice
    {
        [System.Runtime.Serialization.DataMember] public string Id = Guid.NewGuid().ToString("N");
        [System.Runtime.Serialization.DataMember] public DateTime Time;
        [System.Runtime.Serialization.DataMember] public DateTime First;      // 同じお知らせが続いたときの最初の時刻
        [System.Runtime.Serialization.DataMember] public int Count = 1;       // 同じお知らせが続いた回数
        [System.Runtime.Serialization.DataMember] public NoticeLevel Level;
        [System.Runtime.Serialization.DataMember] public string Title;
        [System.Runtime.Serialization.DataMember] public string Text;
        [System.Runtime.Serialization.DataMember] public string Action;   // conflicts / dup / pending / synccand / job / log / retry
        [System.Runtime.Serialization.DataMember] public string JobId;
        [System.Runtime.Serialization.DataMember] public bool Handled;
        [System.Runtime.Serialization.DataMember] public string HandledNote;   // 対応不要 / 確認しました / 手で対応済みにしました など
        [System.Runtime.Serialization.DataMember] public DateTime HandledAt;
        [System.Runtime.Serialization.DataMember] public bool ManualOpen;      // 手で「未対応に戻す」にしたもの（自動では対応済みにしない）

        /// <summary>確認・対応が必要な種類か</summary>
        public bool NeedsAction { get { return Level == NoticeLevel.Error || Level == NoticeLevel.Warning; } }

        public string StateText
        {
            get
            {
                if (!Handled) return "未対応";
                if (HandledNote == NoticeNotes.NotNeeded) return "対応不要";
                return "対応済み";
            }
        }
    }

    public static class NoticeNotes
    {
        public const string NotNeeded = "対応不要（お知らせのみ）";
        public const string Seen = "確認しました";
        public const string Manual = "手で対応済みにしました";
        public const string Recovered = "その後、正常に実行できました";
        public const string Disabled = "ジョブを無効にしました";
        public const string JobGone = "ジョブが削除されました";
    }

    [System.Runtime.Serialization.DataContract]
    public class NoticeBook
    {
        [System.Runtime.Serialization.DataMember] public List<Notice> Items = new List<Notice>();
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
        public JobProgress Progress;       // 実行中・最後に実行したときの作業状況
        public DateTime StartGate;         // 起動後、これより前には実行しない（ネットワークの準備を待つ）
        public bool DupStartupDone;        // 「ログオン時」の重複チェックを、今回の起動で済ませたか
        public string CatchUpNote;         // 止まっている間に予定が過ぎたため、起動後に実行するときの説明
        public bool StartupRunDone;        // 「ログオン時のみ」のジョブを、今回の起動で実行したか
        public string Outcome = "";        // 成功／一部失敗／中断／失敗（JobOutcome）
        public readonly ManualResetEventSlim Idle = new ManualResetEventSlim(true);
        public int ScheduledInterval;
        public bool QueuedManual;          // 手動で実行を頼まれたが、同じフォルダを使う別のジョブが終わるのを待っている
        public string WaitingFor;          // 待っている相手のジョブ名
        public volatile bool Resolving;    // 確認の画面で処理している（上書き・削除の確認など）。この間は実行しない
    }

    /// <summary>アプリの中心。スケジュール、ジョブの実行、通知を受け持つ。</summary>
    public static partial class AppCore
    {
        public static AppSettings Settings;
        private static string savedSettings;
        private static volatile bool stopping;
        private static string lastLogFailure;
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
            string problem;
            AppSettings s = Json.LoadSafe<AppSettings>(AppPaths.SettingsFile, out problem) ?? new AppSettings();
            if (problem != null) Logger.Error(null, problem);
            if (string.IsNullOrEmpty(s.InstanceId)) s.InstanceId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(s.FolderName)) s.FolderName = Environment.MachineName;
            Settings = s;
            savedSettings = Json.Serialize(s);
            ApplySettings(true);
            if (problem != null) Notify(NoticeLevel.Warning, "設定ファイルを復元しました", problem, "log", null);
        }

        public static void SaveSettings()
        {
            lock (sync)
            {
                try { Json.Save(AppPaths.SettingsFile, Settings); savedSettings = Json.Serialize(Settings); }
                catch { if (savedSettings != null) Settings = Json.Deserialize<AppSettings>(savedSettings); throw; }
            }
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
                        DateTime gate = DateTime.Now.AddMinutes(delayMin).AddSeconds(startup ? 5 : 10);
                        rt.StartGate = gate;
                        rt.NextRun = gate;
                        JobState st = GetStateNoLock(j.Id);
                        rt.LastResult = st.LastResult ?? "";
                        rt.LastMessage = st.LastMessage ?? "";
                        if (st.LastRunTicks > 0) rt.LastRun = new DateTime(st.LastRunTicks, DateTimeKind.Utc).ToLocalTime();
                        rt.NextRun = FirstRunAfterStart(j, rt.LastRun, gate, startup, out rt.CatchUpNote);
                        runtimes[j.Id] = rt;
                    }
                    else if (rt.ScheduledInterval != j.IntervalMinutes)
                        rt.NextRun = rt.LastRun == DateTime.MinValue ? DateTime.Now : rt.LastRun.AddMinutes(Math.Max(1, j.IntervalMinutes));
                    rt.ScheduledInterval = j.IntervalMinutes;
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
            if (!states.TryGetValue(id, out s))
            {
                s = JobState.Load(id);
                states[id] = s;
                if (s.LoadProblem != null)
                {
                    // 状態ファイルが壊れていた：知らせる（ロックの外で）
                    string msg = s.LoadProblem;
                    JobConfig j = null;
                    foreach (JobConfig x in Settings.Jobs) if (x.Id == id) j = x;
                    string name = j != null ? j.Name : id;
                    Task.Factory.StartNew(delegate() { Notify(NoticeLevel.Warning, name + "：状態ファイルを読めませんでした", msg, "log", j != null ? j.Id : null); });
                }
            }
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

        /// <summary>状態を保存する（変更中の状態と保存の内容が混ざらないよう、保存の間は状態を変えさせない）。保存できなければ理由。</summary>
        public static string SaveState(string id)
        {
            JobState s = GetState(id);
            try { lock (s) { s.Save(); } return null; }
            catch (Exception ex)
            {
                // ファイルは前回の内容のまま（一時ファイルに書いてから置き換えるため、壊れない）。覚えている内容は次の保存で書く
                Logger.Error(null, "状態ファイルを保存できません: " + ex.Message);
                return "状態ファイルを保存できません（前回の内容のままです。次の実行でもう一度保存します）: " + ex.Message;
            }
        }

        public static void ForgetJobState(string id)
        {
            lock (sync)
            {
                JobState s;
                if (states.TryGetValue(id, out s)) { s.Delete(); states.Remove(id); }
            }
            DedupStore.Forget(id);
            SyncDeleteStore.Forget(id);
        }

        // ---------------- スケジュール ----------------

        public static void Start()
        {
            stopping = false;
            // 前回、上書きの途中で終了していたら元に戻す（NAS の応答待ちで起動を遅らせないよう、別のスレッドで）
            Task.Factory.StartNew(delegate()
            {
                try { CommitJournal.Recover(); }
                catch (Exception ex) { Logger.Error(null, "上書きの途中で終了したファイルの確認に失敗しました: " + ex.Message); }
            });
            timer = new Timer(delegate(object o) { Tick(); }, null, 3000, 15000);
        }

        public static void Stop()
        {
            stopping = true;
            if (timer != null) timer.Dispose();
            List<JobRuntime> active;
            lock (sync) { active = new List<JobRuntime>(runtimes.Values); }
            foreach (JobRuntime rt in active) if (rt.Cts != null) rt.Cts.Cancel();
            foreach (JobRuntime rt in active) rt.Idle.Wait();
        }

        private static int ticking;

        private static void Tick()
        {
            if (stopping || Interlocked.Exchange(ref ticking, 1) == 1) return;
            try
            {
                DateTime now = DateTime.Now;
                if (Logger.LastWriteError != null && Logger.LastWriteError != lastLogFailure)
                {
                    lastLogFailure = Logger.LastWriteError;
                    Notify(NoticeLevel.Warning, "ログを保存できません", lastLogFailure, "log", null);
                }
                if (globalPauseUntil.HasValue && now >= globalPauseUntil.Value) { globalPauseUntil = null; Notify(NoticeLevel.Info, "自動コピーを再開しました", "一時停止の時間が過ぎました。", null, null); }
                foreach (JobConfig j in Settings.Jobs.ToArray())
                {
                    JobRuntime rt = GetRuntime(j.Id);
                    if (rt.PausedUntil.HasValue && now >= rt.PausedUntil.Value) rt.PausedUntil = null;
                    if (!j.Enabled || IsPaused(j.Id)) continue;
                    string dupNote;
                    // 手動で頼まれていたが、同じフォルダを使う別のジョブを待っていたもの
                    if (rt.QueuedManual && !rt.Running && !rt.DupRunning) { RunJob(j.Id, true, false); continue; }
                    if (!rt.Running && !rt.DupRunning && now >= rt.NextRun && TimeUtil.InWindow(j, now) && !(j.StartupOnly && rt.StartupRunDone))
                    {
                        if (rt.CatchUpNote != null) { Logger.Info(j.Name, rt.CatchUpNote); rt.CatchUpNote = null; }
                        RunJob(j.Id, false, false);
                    }
                    else if (!rt.Running && !rt.DupRunning && DupDue(j, rt, now, out dupNote))
                    {
                        if (dupNote != null) Logger.Info(j.Name, dupNote);
                        if (RunDup(j.Id)) rt.DupStartupDone = true;
                    }
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

        /// <summary>
        /// 起動したときの最初の実行時刻。
        /// 「ログオン時にも実行する」なら起動後すぐ（待ち時間の後）。そうでなければ、前回から実行間隔がたって
        /// 予定の時刻が来たら実行する。止まっている間に予定の時刻が過ぎていたら、起動後すぐに実行する（取りこぼさない）。
        /// </summary>
        public static DateTime FirstRunAfterStart(JobConfig j, DateTime lastRun, DateTime gate, bool startup, out string catchUp)
        {
            catchUp = null;
            if (j.StartupOnly || !startup || j.RunAtStartup || lastRun == DateTime.MinValue) return gate;
            DateTime due = lastRun.AddMinutes(Math.Max(1, j.IntervalMinutes));
            if (due <= gate)
            {
                catchUp = "前回の予定（" + due.ToString("M/d HH:mm") + "）に動いていなかったため、起動後に実行します";
                return gate;
            }
            return due;
        }

        /// <summary>
        /// 重複チェックの、now 以前でいちばん新しい予定の時刻（毎日・毎週・毎月・毎年）。予定がない（手動・ログオン時）は DateTime.MinValue。
        /// この時刻より後に一度もチェックしていなければ実行する＝止まっている間に過ぎた予定も、起動後に取りこぼさず実行する。
        /// </summary>
        public static DateTime PrevDupTime(JobConfig j, DateTime now)
        {
            TimeSpan t;
            if (!TimeUtil.TryParseHm(j.DupTime, out t)) t = new TimeSpan(12, 0, 0);
            switch (j.DupSchedule)
            {
                case DupScheduleKind.Daily:
                    {
                        DateTime d = now.Date + t;
                        return d <= now ? d : d.AddDays(-1);
                    }
                case DupScheduleKind.Weekly:
                    for (int k = 0; k <= 7; k++)
                    {
                        DateTime d = now.Date.AddDays(-k);
                        if ((int)d.DayOfWeek == j.DupWeekday && d + t <= now) return d + t;
                    }
                    return DateTime.MinValue;
                case DupScheduleKind.Monthly:
                    for (int k = 0; k <= 2; k++)
                    {
                        DateTime m = new DateTime(now.Year, now.Month, 1).AddMonths(-k);
                        DateTime d = m.AddDays(DayOf(j.DupDay, m.Year, m.Month) - 1) + t;
                        if (d <= now) return d;
                    }
                    return DateTime.MinValue;
                case DupScheduleKind.Yearly:
                    for (int k = 0; k <= 2; k++)
                    {
                        int y = now.Year - k, mo = Math.Max(1, Math.Min(12, j.DupMonth));
                        DateTime d = new DateTime(y, mo, DayOf(j.DupDay, y, mo)) + t;
                        if (d <= now) return d;
                    }
                    return DateTime.MinValue;
            }
            return DateTime.MinValue;
        }

        private static int DayOf(int day, int y, int m)
        {
            int last = DateTime.DaysInMonth(y, m);
            return day == DupScheduleKind.LastDay ? last : Math.Max(1, Math.Min(last, day));
        }

        /// <summary>重複チェックの予定の説明（画面用）</summary>
        public static string DupScheduleText(JobConfig j)
        {
            if (!j.DupCheckEnabled) return "しない";
            string day = j.DupDay == DupScheduleKind.LastDay ? "末日" : j.DupDay + "日";
            string[] wd = { "日", "月", "火", "水", "木", "金", "土" };
            switch (j.DupSchedule)
            {
                case DupScheduleKind.Startup: return "ログオン時（アプリの起動時）";
                case DupScheduleKind.Daily: return "毎日 " + j.DupTime;
                case DupScheduleKind.Weekly: return "毎週" + wd[Math.Max(0, Math.Min(6, j.DupWeekday))] + "曜 " + j.DupTime;
                case DupScheduleKind.Monthly: return "毎月" + day + " " + j.DupTime;
                case DupScheduleKind.Yearly: return "毎年" + j.DupMonth + "月" + day + " " + j.DupTime;
            }
            return "手動のみ";
        }

        /// <summary>重複チェックをする時か。予定の時刻を過ぎていて、その後まだチェックしていなければ実行する</summary>
        public static bool DupDue(JobConfig j, JobRuntime rt, DateTime now, out string catchUp)
        {
            catchUp = null;
            if (!j.DupCheckEnabled || j.Sync || j.DupSchedule == DupScheduleKind.Manual) return false;
            if (now < rt.StartGate) return false;   // 起動直後はネットワークの準備を待つ
            if (j.DupSchedule == DupScheduleKind.Startup) return !rt.DupStartupDone;
            DateTime prev = PrevDupTime(j, now);
            if (prev == DateTime.MinValue) return false;
            JobState st = GetState(j.Id);
            DateTime last;
            lock (st) { last = st.LastDupCheckTicks > 0 ? new DateTime(st.LastDupCheckTicks, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue; }
            if (last >= prev) return false;
            if ((now - prev).TotalMinutes > 5) catchUp = "重複チェックの予定（" + prev.ToString("M/d HH:mm") + "）に動いていなかったため、起動後に実行します";
            return true;
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

        private static readonly object runGate = new object();

        /// <summary>
        /// 同じフォルダ（コピー先どうし・コピー元とコピー先）を使っていて、今動いている別のジョブ。
        /// 同時に動くと同じファイルを書き合ったり、コピー中のファイルを読んだりするため、終わるのを待つ。
        /// </summary>
        public static JobConfig OverlappingRunning(JobConfig job)
        {
            string s = CopyEngine.ResolvedSource(job, Settings), d = CopyEngine.ResolvedDest(job, Settings);
            foreach (JobConfig o in Settings.Jobs.ToArray())
            {
                if (o.Id == job.Id) continue;
                JobRuntime ort = GetRuntime(o.Id);
                if (!ort.Running && !ort.DupRunning && !ort.Resolving) continue;
                string os = CopyEngine.ResolvedSource(o, Settings), od = CopyEngine.ResolvedDest(o, Settings);
                if (Overlaps(d, od) || Overlaps(d, os) || Overlaps(s, od)) return o;
            }
            return null;
        }

        private static bool Overlaps(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return PathUtil.IsSameOrInside(a, b) || PathUtil.IsSameOrInside(b, a);
        }

        public static bool RunJob(string jobId, bool manual, bool fullVerify)
        {
            if (stopping) return false;
            JobConfig job = FindJob(jobId);
            if (job == null) return false;
            JobRuntime rt = GetRuntime(job.Id);
            lock (runGate)
            {
                lock (rt)
                {
                    if (rt.Running || rt.DupRunning || rt.Resolving) return false;
                }
                JobConfig other = OverlappingRunning(job);
                if (other != null)
                {
                    // 同じフォルダを使う別のジョブが動いている：終わってから実行する（手動の実行は覚えておく）
                    if (rt.WaitingFor != other.Name)
                        Logger.Info(job.Name, "ジョブ「" + other.Name + "」が同じフォルダを使っているため、終わってから実行します");
                    rt.WaitingFor = other.Name;
                    if (manual) rt.QueuedManual = true;
                    RaiseChanged();
                    return manual;
                }
                lock (rt)
                {
                    rt.Idle.Reset();
                    rt.Running = true;
                    rt.Phase = "コピー中";
                    rt.Cts = new CancellationTokenSource();
                    rt.QueuedManual = false;
                    rt.WaitingFor = null;
                }
            }
            DateTime startedAt = DateTime.Now;
            rt.NextRun = startedAt.AddMinutes(Math.Max(1, job.IntervalMinutes));
            if (job.StartupOnly && !manual) rt.StartupRunDone = true;   // ログオン時のみ：次のログオンまで自動では実行しない
            RaiseChanged();
            CancellationToken ct = rt.Cts.Token;
            Task.Factory.StartNew(delegate()
            {
                try
                {
                JobState st = GetState(job.Id);
                RunResult res = null;
                string error = null;
                bool credError = false, canceled = false;
                try
                {
                    CopyEngine eng = new CopyEngine(job, Settings, st, false, false, ct);
                    eng.FullVerify = fullVerify;
                    rt.Progress = eng.Progress;
                    eng.Progress.SetPhase("接続しています");
                    res = eng.Run();
                    if (res.Failed) error = res.Message;
                }
                catch (OperationCanceledException) { error = "中止しました"; canceled = true; }
                catch (JobAbortException ex) { error = ex.Message; }
                catch (AccessException ex) { error = ex.Message; credError = ex.IsCredentialError; }
                catch (FolderConflictException ex) { error = ex.Message; }
                catch (SourceProtectedException ex) { error = ex.Message + "（このジョブを停止しました）"; job.Enabled = false; }
                catch (Exception ex) { error = ex.Message; }

                if (rt.Progress != null)
                    lock (rt.Progress)
                    {
                        if (rt.Progress.Finished == DateTime.MinValue) rt.Progress.Finished = DateTime.Now;
                        if (error != null) rt.Progress.Phase = error == "中止しました" ? "中止しました" : "エラー";
                    }
                rt.LastRun = startedAt;
                // 結果の分類：成功／一部失敗（一部のファイル・フォルダでエラー）／中断（中止した）／失敗
                rt.Outcome = canceled ? JobOutcome.Canceled : error != null ? JobOutcome.Failed : res != null ? res.Outcome : JobOutcome.Success;
                rt.LastResult = canceled ? "canceled" : error != null ? "error" : rt.Outcome == JobOutcome.Partial ? "partial" : "ok";
                rt.LastMessage = error ?? (res != null ? (rt.Outcome == JobOutcome.Partial ? JobOutcome.Partial + "（エラー " + res.Errors + "件" + (res.ScanFailedDirs > 0 ? "・読めないフォルダ " + res.ScanFailedDirs + "件" : "") + "）　" : "") + res.Summary() : "");
                lock (st)
                {
                    st.LastRunTicks = startedAt.ToUniversalTime().Ticks;
                    st.LastResult = rt.LastResult;
                    st.LastMessage = rt.LastMessage;
                }
                string saveErr = SaveState(job.Id);
                if (saveErr != null && error == null && !canceled) NotifyError(job, saveErr, false);
                if (res != null) Logger.Write(job.Name, "SUMMARY", res.Summary(), res.Bytes, res.Seconds);
                if (canceled)
                    Logger.Info(job.Name, "中止しました（コピーの途中のファイルは、次の実行でコピーし直します）");
                else if (error != null)
                {
                    Logger.Error(job.Name, error);
                    NotifyError(job, error, credError);
                }
                else if (res != null && rt.Outcome == JobOutcome.Partial)
                    Notify(NoticeLevel.Warning, job.Name + "：一部のファイルをコピーできませんでした",
                        "エラー " + res.Errors + "件" + (res.ScanFailedDirs > 0 ? "・読めないフォルダ " + res.ScanFailedDirs + "件" : "") + "。ログで原因（ネットワーク・容量不足・アクセス拒否など）を確かめてください。", "partial", job.Id);
                if (res != null)
                {
                    if (res.Conflicts > 0)
                        Notify(NoticeLevel.Warning, "上書きの確認が " + res.Conflicts + "件 あります",
                            job.Name + "：コピー先の方が新しいファイルがあるため、上書きせずに待っています。", "conflicts", job.Id);
                    if (res.PendingDeletes > 0)
                        Notify(NoticeLevel.Warning, "ミラー：" + res.PendingDeletes + "件の削除を保留しています",
                            job.Name + "：一度に多くのファイルが削除されるため、確認をお待ちしています。", "pending", job.Id);
                    if (res.DedupNew && res.DedupAsked > 0)
                        Notify(NoticeLevel.Warning, "大きいファイルの重複 " + res.DedupAsked + "件：扱いを選んでください",
                            job.Name + "：中身が同じ大きいファイルを見つけました。選ぶまでコピーせずに待っています（1つ目はコピー済み）。", "dup", job.Id);
                    if (res.SyncDeleteNew > 0)
                        Notify(NoticeLevel.Warning, "同期：削除されたファイル " + res.SyncDeleteCandidates + "件 の扱いを選んでください",
                            job.Name + "：一方で削除されたファイルがあります。もう一方からも消すか、残す（戻す）かを選ぶまで、そのままにしています。", "synccand", job.Id);
                    if (res.Renamed > 0)
                        AddNoticeOnly(NoticeLevel.Ok, job.Name + "：名前の変更 " + res.Renamed + "件 をコピー先にも反映しました（再コピーなし）", "log", job.Id);
                    if (res.Locked > 0)
                        AddNoticeOnly(NoticeLevel.Info, job.Name + "：使用中のファイル " + res.Locked + "件 は次回コピーします", "log", job.Id);
                    if (error == null && Settings.NotifyDone && (res.Copied + res.Overwritten + res.Deleted + res.Renamed) > 0)
                        Notify(NoticeLevel.Ok, job.Name + "：コピーしました", res.Summary(), "log", job.Id);
                }
                if (job.Enabled == false) { try { SaveSettings(); } catch (Exception ex) { Logger.Error(job.Name, "設定を保存できません: " + ex.Message); } }
                PublishStatus();
                RaiseChanged();
                Logger.Flush();
                }
                finally { lock (rt) { rt.Running = false; rt.Phase = ""; rt.Idle.Set(); } PublishStatus(); RaiseChanged(); }
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
                    // 吹き出しは出さず、同じお知らせにまとめる（回数が増える）
                    AddNotice(NoticeLevel.Error, job.Name + "：エラー", error, credError ? "job" : "retry", job.Id);
                    RaiseChanged();
                    return;
                }
                lastErrorNotice[key] = DateTime.Now;
            }
            Notify(NoticeLevel.Error, job.Name + "：エラー", error, credError ? "job" : "retry", job.Id);
        }

        /// <summary>コピーせずに件数を数える（テスト・ミラーの注意画面用）</summary>
        public static RunResult DryRun(JobConfig job, bool forceMirror)
        {
            return DryRun(job, forceMirror, CancellationToken.None, null);
        }

        /// <summary>コピーせずに、実行したらどうなるかを調べる（コピー先・コピー元・記録は一切変えない）</summary>
        public static RunResult DryRun(JobConfig job, bool forceMirror, CancellationToken ct, JobProgress progress)
        {
            return DryRun(job, forceMirror, ct, progress, null);
        }

        /// <summary>live を渡すと、調べている途中から live.Plan に予測が増えていく（lock (live.Plan) して読む）</summary>
        public static RunResult DryRun(JobConfig job, bool forceMirror, CancellationToken ct, JobProgress progress, RunResult live)
        {
            JobState st = job.Id != null ? GetState(job.Id) : new JobState();
            JobState copy = new JobState();
            lock (st)
            {
                foreach (KeyValuePair<string, FileRecord> kv in st.Files) copy.Files[kv.Key] = kv.Value.Clone();
                foreach (string s in st.SkipForever) copy.SkipForever.Add(s);
            }
            JobConfig quick = job.Clone();
            quick.RetryCount = 0; // 画面で待たせないよう、数えるだけのときは再試行しない
            CopyEngine eng = new CopyEngine(quick, Settings, copy, true, forceMirror, ct);
            if (progress != null) eng.Progress = progress;
            eng.Live = live;
            return eng.Run();
        }

        public static bool RunDup(string jobId)
        {
            if (stopping) return false;
            JobConfig job = FindJob(jobId);
            if (job == null) return false;
            JobRuntime rt = GetRuntime(job.Id);
            lock (runGate)
            lock (rt)
            {
                if (rt.Running || rt.DupRunning || rt.Resolving || OverlappingRunning(job) != null) return false;
                rt.Idle.Reset();
                rt.DupRunning = true;
                rt.Phase = "重複チェック中";
                rt.Cts = new CancellationTokenSource();
            }
            RaiseChanged();
            CancellationToken ct = rt.Cts.Token;
            Task.Factory.StartNew(delegate()
            {
                try
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
                    if (r.LargePending > 0)
                        Notify(NoticeLevel.Warning, "大きいファイルの重複 " + r.LargePending + "件：扱いを選んでください",
                            job.Name + "：中身が同じ大きいファイルがあります。テキストにする（容量を節約）か、普通にコピーするかを選んでください。", "dup", job.Id);
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
                RaiseChanged();
                }
                finally { lock (rt) { rt.DupRunning = false; rt.Phase = ""; rt.Idle.Set(); } RaiseChanged(); }
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
        /// <summary>確認の画面での処理の間、そのジョブを実行しない（実行中なら処理しない）。始められなければ理由</summary>
        public static string BeginResolve(JobConfig job)
        {
            JobRuntime rt = GetRuntime(job.Id);
            lock (runGate)
            {
                lock (rt)
                {
                    if (rt.Running || rt.DupRunning || rt.Resolving || OverlappingRunning(job) != null) return "ジョブ「" + job.Name + "」は実行中です。終わってからもう一度お試しください。";
                    rt.Resolving = true; rt.Idle.Reset();
                }
            }
            return null;
        }

        public static void EndResolve(JobConfig job) { lock (sync) { JobRuntime rt; if (runtimes.TryGetValue(job.Id, out rt)) { rt.Resolving = false; if (!rt.Running && !rt.DupRunning) rt.Idle.Set(); } } }

        // Reserve the job first, then drain its worker before changing paths/state.
        public static void StopJobAndWait(string id)
        {
            JobRuntime rt = GetRuntime(id);
            lock (runGate)
            {
                if (rt.Resolving) throw new InvalidOperationException("確認操作の完了後に設定を変更してください。");
                rt.Resolving = true; rt.QueuedManual = false; if (rt.Cts != null) rt.Cts.Cancel();
            }
            rt.Idle.Wait();
        }

        public static string ResolveConflict(JobConfig job, ConflictItem c, string action, bool backup)
        {
            string busy = BeginResolve(job);
            if (busy != null) return busy;
            try { return ResolveConflictCore(job, c, action, backup); }
            finally { EndResolve(job); }
        }

        private static string ResolveConflictCore(JobConfig job, ConflictItem c, string action, bool backup)
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
                    // 確認の画面に出した後で、コピー先（同期では共有フォルダ）が変わっていたら上書きしない
                    DestExpect expect = c.DstSize >= 0 ? DestExpect.Same(c.DstSize, c.DstMtime) : null;
                    string why = expect != null ? expect.Check(PathUtil.Long(d)) : null;
                    if (why != null)
                    {
                        if (job.Sync) SyncRemoteCache.Invalidate(job.Id);
                        return c.Rel + "：確認の後でコピー先が変わったため（" + why + "）、上書きしませんでした。次の実行で確かめ直します";
                    }
                    if (backup && File.Exists(PathUtil.Long(d)))
                    {
                        SafeFs.MoveToTrash(dst, c.Rel, "退避", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                        expect = DestExpect.Absent();
                    }
                    byte[] hash = SafeFs.CopyFile(s, d, fi.LastWriteTimeUtc, null, CancellationToken.None, null, expect);
                    FileRecord old;
                    lock (st) st.Files.TryGetValue(c.Rel, out old);
                    FileRecord rec = new FileRecord();
                    rec.CopiedAt = FileRecord.FirstCopied(old);
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
                    // 確認の画面に出した後で、このPCのファイルが変わっていたら置き換えない
                    DestExpect expect = c.SrcSize >= 0 ? DestExpect.Same(c.SrcSize, c.SrcMtime) : null;
                    string why = expect != null ? expect.Check(PathUtil.Long(l)) : null;
                    if (why != null) return c.Rel + "：確認の後でこのPCのファイルが変わったため（" + why + "）、取り込みませんでした。次の実行で確かめ直します";
                    string backupRoot = Path.Combine(Path.Combine(AppPaths.SyncBackupDir, job.Id), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    byte[] hash = SafeFs.CopyIntoSync(s, l, c.Rel, fi.LastWriteTimeUtc, backupRoot, CancellationToken.None, null, expect);
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
                if (job.Sync && action == ActOverwrite) { SyncRemoteCache.Invalidate(job.Id); SyncRemoteCache.TouchStamp(dst); }
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

        /// <summary>
        /// 同期：削除の確認に答える。approve = 削除する（このPCの分は控えへ、共有フォルダの分は「削除済み」へ移す）。
        /// 削除しない場合は記録を消し、次回の実行で消えた側へ戻す。できなかったものを返す。
        /// </summary>
        public static List<string> ResolveSyncDeletes(JobConfig job, List<SyncDeleteItem> items, bool approve)
        {
            string busy = BeginResolve(job);
            if (busy != null) return new List<string>(new string[] { busy });
            try { return ResolveSyncDeletesCore(job, items, approve); }
            finally { EndResolve(job); }
        }

        private static List<string> ResolveSyncDeletesCore(JobConfig job, List<SyncDeleteItem> items, bool approve)
        {
            List<string> errs = new List<string>();
            if (!job.Sync) { errs.Add("同期のジョブではありません"); return errs; }
            JobState st = GetState(job.Id);
            string local = CopyEngine.ResolvedSource(job, Settings), shared = CopyEngine.ResolvedDest(job, Settings);
            string batch = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupRoot = Path.Combine(Path.Combine(AppPaths.SyncBackupDir, job.Id), batch);
            List<SyncDeleteItem> done = new List<SyncDeleteItem>();
            if (approve)
            {
                try { NetAccess.Ensure(shared, job.RetryCount, job.RetryIntervalSeconds, CancellationToken.None, null); }
                catch (Exception ex) { errs.Add("共有フォルダに接続できません：" + ex.Message); return errs; }
            }
            foreach (SyncDeleteItem i in items)
            {
                try
                {
                    FileSafety.RequireReadableDirectory(local);
                    FileSafety.RequireReadableDirectory(shared);
                    string lp = PathUtil.Combine(local, i.Rel), sp = PathUtil.Combine(shared, i.Rel);
                    bool lEx = File.Exists(PathUtil.Long(lp)), sEx = File.Exists(PathUtil.Long(sp));
                    if (approve)
                    {
                        // 見つけたときから変わっていないことを確かめてから移す（変わっていたら次回の判定に任せる）
                        if (i.Side == SyncDeleteItem.SideLocal)
                        {
                            if (sEx) { errs.Add(i.Rel + "：共有フォルダに戻っているため、消しませんでした"); done.Add(i); continue; }
                            if (lEx)
                            {
                                FileInfo fi = new FileInfo(PathUtil.Long(lp));
                                if (fi.Length != i.Size || fi.LastWriteTimeUtc.Ticks != i.Mtime) { errs.Add(i.Rel + "：このPCで変更されたため、消しませんでした（次回、共有フォルダへコピーします）"); done.Add(i); continue; }
                                SafeFs.MoveOutOfSync(lp, i.Rel, backupRoot);
                            }
                            Logger.Write(job.Name, "SYNCDEL", i.Rel + "　（共有フォルダで削除されたため、確認してこのPCからも外しました。控え：" + backupRoot + "）", i.Size, -1);
                        }
                        else
                        {
                            if (lEx) { errs.Add(i.Rel + "：このPCに戻っているため、消しませんでした"); done.Add(i); continue; }
                            if (sEx)
                            {
                                FileInfo fi = new FileInfo(PathUtil.Long(sp));
                                if (fi.Length != i.Size || Math.Abs(fi.LastWriteTimeUtc.Ticks - i.Mtime) > TimeSpan.TicksPerSecond * 2) { errs.Add(i.Rel + "：共有フォルダで変更されたため、消しませんでした（次回、このPCへ取り込みます）"); done.Add(i); continue; }
                                SafeFs.MoveToTrash(shared, i.Rel, "削除済み", batch);
                            }
                            Logger.Write(job.Name, "SYNCDEL", i.Rel + "　（このPCで削除したため、確認して共有フォルダからも「_AutoCopy\\削除済み」へ移しました）", i.Size, -1);
                        }
                        lock (st) { st.Files.Remove(i.Rel); }
                    }
                    else
                    {
                        // 残す：記録を消す。共有フォルダで消えたもの（このPCにある）は、次回の実行で共有フォルダへコピーし直す。
                        // このPCで消したもの（共有フォルダにある）は、「取り込まない」設定でも戻るよう、ここでこのPCへ戻す
                        lock (st) { st.Files.Remove(i.Rel); }
                        if (i.Side == SyncDeleteItem.SideShared && sEx && !lEx)
                        {
                            FileInfo sf = new FileInfo(PathUtil.Long(sp));
                            byte[] h = SafeFs.CopyIntoSync(sp, lp, i.Rel, sf.LastWriteTimeUtc, null, CancellationToken.None);
                            FileRecord rec = new FileRecord();
                            rec.Size = sf.Length; rec.Mtime = File.GetLastWriteTimeUtc(PathUtil.Long(lp)).Ticks; rec.DestMtime = sf.LastWriteTimeUtc.Ticks;
                            rec.Hash = h; rec.CopiedByApp = true;
                            lock (st) { st.Files[i.Rel] = rec; }
                            Logger.Write(job.Name, "SYNCIN", i.Rel + "　（削除せずに残すことにしたため、共有フォルダからこのPCへ戻しました）", i.Size, -1);
                        }
                        else if (i.Side == SyncDeleteItem.SideLocal && lEx && !sEx)
                        {
                            // 共有フォルダで消えたもの（このPCにある）を残す：「共有へ送らない」設定でも戻るよう、ここで共有フォルダへ戻す
                            FileInfo lf = new FileInfo(PathUtil.Long(lp));
                            byte[] h = SafeFs.CopyFile(lp, sp, lf.LastWriteTimeUtc, null, CancellationToken.None);
                            FileRecord rec = new FileRecord();
                            rec.Size = lf.Length; rec.Mtime = lf.LastWriteTimeUtc.Ticks; rec.Hash = h; rec.CopiedByApp = true;
                            rec.DestMtime = File.GetLastWriteTimeUtc(PathUtil.Long(sp)).Ticks;
                            lock (st) { st.Files[i.Rel] = rec; }
                            Logger.Write(job.Name, "COPY", i.Rel + "　（削除せずに残すことにしたため、このPCから共有フォルダへ戻しました）", i.Size, -1);
                        }
                    }
                    done.Add(i);
                }
                catch (Exception ex)
                {
                    Logger.Error(job.Name, i.Rel + "　" + ex.Message);
                    errs.Add(i.Rel + "：" + ex.Message);
                }
            }
            try { SyncDeleteStore.Remove(job.Id, done); }
            catch (Exception ex) { errs.Add("確認の結果を保存できませんでした（一覧は前のまま。もう一度選ぶと、すでに済んだものは何もしません）：" + ex.Message); }
            if (done.Count > 0)
            {
                // 共有フォルダを変えた（かもしれない）：覚えている一覧を捨て、他のPCにも知らせる
                SyncRemoteCache.Invalidate(job.Id);
                SyncRemoteCache.TouchStamp(shared);
            }
            SaveState(job.Id);
            RaiseChanged();
            return errs;
        }

        public static int TotalSyncDeletes()
        {
            int n = 0;
            foreach (JobConfig j in Settings.Jobs.ToArray()) if (j.Sync) n += SyncDeleteStore.Count(j.Id);
            return n;
        }

        /// <summary>「今後も上書きしない」にしたファイルを、また確認するように戻す</summary>
        public static void ForgetSkipForever(JobConfig job, IEnumerable<string> rels)
        {
            JobState st = GetState(job.Id);
            int n = 0;
            lock (st) { foreach (string r in rels) if (st.SkipForever.Remove(r)) { n++; Logger.Write(job.Name, "INFO", r + "　（「今後も上書きしない」を取り消しました。次回からまた確認します）", -1, -1); } }
            if (n > 0) { SaveState(job.Id); RaiseChanged(); }
        }

        public static string ResolvePendingDeletes(JobConfig job, bool approve)
        {
            string busy = BeginResolve(job);
            if (busy != null) return busy;
            try { return ResolvePendingDeletesCore(job, approve); }
            finally { EndResolve(job); }
        }

        private static string ResolvePendingDeletesCore(JobConfig job, bool approve)
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
                    FileSafety.RequireReadableDirectory(CopyEngine.ResolvedSource(job, Settings));
                    int n = eng.DeleteMirror(rels, null);
                    if (eng.DeleteErrors.Count > 0)
                    {
                        lock (st) st.PendingDeletes.RemoveAll(delegate(PendingDelete p) { return !eng.DeleteRemaining.Contains(p.Rel); });
                        SaveState(job.Id);
                        return "一部を削除できませんでした。確認待ちに残しました。\n" + string.Join("\n", eng.DeleteErrors.ToArray());
                    }
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

    }
}
