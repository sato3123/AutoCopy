using System;
using System.IO;
using System.Collections.Generic;

namespace AutoCopy
{
    public static partial class AppCore
    {
        // ---------------- 通知・状態 ----------------

        public static void Notify(NoticeLevel level, string title, string text, string action, string jobId)
        {
            Notice n = AddNotice(level, title, text, action, jobId);
            // 確認済みのお知らせと同じ内容が続いたときは、吹き出しを出さない（点滅もしない）
            if (n.NeedsAction && n.Handled && n.Count > 1) { RaiseChanged(); return; }
            bool show = level == NoticeLevel.Error ? Settings.NotifyErrors
                : level == NoticeLevel.Warning || level == NoticeLevel.Info ? Settings.NotifyConfirm : Settings.NotifyDone;
            if (show && NoticeAdded != null) { try { NoticeAdded(n); } catch (Exception) { } }
        }

        private static void AddNoticeOnly(NoticeLevel level, string text, string action, string jobId)
        {
            AddNotice(level, text, null, action, jobId);
            RaiseChanged();
        }

        public const int MaxNotices = 300;
        private static bool noticesLoaded;

        public static string NoticePath { get { return Path.Combine(AppPaths.StateDir, "notices.json"); } }

        private static void EnsureNotices()
        {
            lock (notices)
            {
                if (noticesLoaded) return;
                noticesLoaded = true;
                try
                {
                    string problem;
                    NoticeBook b = Json.LoadSafe<NoticeBook>(NoticePath, out problem);
                    if (problem != null) Logger.Error(null, "お知らせ：" + problem);
                    if (b != null && b.Items != null)
                        foreach (Notice n in b.Items) if (n != null) { if (string.IsNullOrEmpty(n.Id)) n.Id = Guid.NewGuid().ToString("N"); if (n.Count < 1) n.Count = 1; notices.Add(n); }
                }
                catch (Exception) { }
            }
        }

        private static void SaveNotices()
        {
            NoticeBook b = new NoticeBook();
            lock (notices) { b.Items = new List<Notice>(notices); }
            try { Directory.CreateDirectory(AppPaths.StateDir); Json.Save(NoticePath, b); }
            catch (Exception ex) { Logger.Error(null, "お知らせを保存できません: " + ex.Message); }
        }

        private static Notice AddNotice(NoticeLevel level, string title, string text, string action, string jobId)
        {
            EnsureNotices();
            Notice n;
            lock (notices)
            {
                // 同じお知らせが続くとき（同じエラーが毎回出るなど）は、1件にまとめて回数を数える
                n = notices.Find(delegate(Notice x)
                {
                    return x.Level == level && x.Title == title && x.Text == text && x.Action == action && x.JobId == jobId
                        && (!x.Handled || x.HandledNote == NoticeNotes.Seen || x.HandledNote == NoticeNotes.Manual || x.HandledNote == NoticeNotes.NotNeeded);
                });
                if (n != null && level != NoticeLevel.Ok)
                {
                    notices.Remove(n);
                    n.Time = DateTime.Now;
                    n.Count++;
                    notices.Insert(0, n);
                }
                else
                {
                    n = new Notice();
                    n.Time = n.First = DateTime.Now; n.Level = level; n.Title = title; n.Text = text; n.Action = action; n.JobId = jobId;
                    if (!n.NeedsAction) { n.Handled = true; n.HandledNote = NoticeNotes.NotNeeded; n.HandledAt = n.Time; }
                    notices.Insert(0, n);
                    // 古いものから消す（未対応のものはなるべく残す）
                    while (notices.Count > MaxNotices)
                    {
                        int idx = notices.FindLastIndex(delegate(Notice x) { return x.Handled; });
                        notices.RemoveAt(idx >= 0 ? idx : notices.Count - 1);
                    }
                }
            }
            SaveNotices();
            return n;
        }

        public static List<Notice> RecentNotices()
        {
            EnsureNotices();
            lock (notices) { return new List<Notice>(notices); }
        }

        public static int OpenNoticeCount()
        {
            EnsureNotices();
            int c = 0;
            lock (notices) { foreach (Notice n in notices) if (!n.Handled) c++; }
            return c;
        }

        /// <summary>手で「対応済み」「未対応に戻す」</summary>
        public static void SetNoticeHandled(IEnumerable<Notice> list, bool handled)
        {
            EnsureNotices();
            lock (notices)
            {
                foreach (Notice n in list)
                {
                    if (n == null) continue;
                    n.Handled = handled;
                    n.ManualOpen = !handled;
                    n.HandledNote = handled ? (n.NeedsAction ? NoticeNotes.Manual : NoticeNotes.NotNeeded) : null;
                    n.HandledAt = handled ? DateTime.Now : DateTime.MinValue;
                    if (n.NeedsAction) LogHandled(n);
                }
            }
            SaveNotices();
            RaiseChanged();
        }

        /// <summary>対応済み・対応不要のお知らせを一覧から消す</summary>
        public static int ClearHandledNotices()
        {
            EnsureNotices();
            int n;
            lock (notices) { n = notices.RemoveAll(delegate(Notice x) { return x.Handled; }); }
            SaveNotices();
            RaiseChanged();
            return n;
        }

        /// <summary>エラーを「確認した」：そのジョブの未対応のエラーのお知らせを対応済みにする（点滅も止まる）</summary>
        public static void AckErrors(string jobId)
        {
            EnsureNotices();
            bool any = false;
            lock (notices)
            {
                foreach (Notice n in notices)
                    if (!n.Handled && n.Level == NoticeLevel.Error && (jobId == null || n.JobId == jobId))
                    { n.Handled = true; n.HandledNote = NoticeNotes.Seen; n.HandledAt = DateTime.Now; n.ManualOpen = false; any = true; LogHandled(n); }
            }
            if (any) { SaveNotices(); RaiseChanged(); }
        }

        /// <summary>
        /// 未対応のお知らせのうち、確認が済んだもの（上書きの確認が0件になった・その後正常に実行できた など）を対応済みにする。
        /// 画面の更新のたびに呼ぶ。変わったら true。
        /// </summary>
        public static bool RefreshNoticeStates()
        {
            EnsureNotices();
            List<Notice> open;
            lock (notices) { open = notices.FindAll(delegate(Notice x) { return !x.Handled && !x.ManualOpen; }); }
            if (open.Count == 0) return false;
            bool changed = false;
            foreach (Notice n in open)
            {
                string why = null;
                try { why = AutoResolved(n); } catch (Exception) { }
                if (why == null) continue;
                lock (notices) { n.Handled = true; n.HandledNote = why; n.HandledAt = DateTime.Now; }
                LogHandled(n);
                changed = true;
            }
            if (changed) SaveNotices();
            return changed;
        }

        /// <summary>対応状況をログにも残す（ログ保管先に送られ、全PCの対応状況をログ画面で見られる）</summary>
        private static void LogHandled(Notice n)
        {
            try
            {
                JobConfig j = n.JobId != null ? FindJob(n.JobId) : null;
                string what = (n.Title ?? "") + (string.IsNullOrEmpty(n.Text) ? "" : "　" + n.Text);
                if (what.Length > 160) what = what.Substring(0, 160) + "…";
                Logger.Write(j != null ? j.Name : null, "HANDLED",
                    (n.Handled ? "対応済み［" + (n.HandledNote ?? "") + "］" : "未対応に戻しました") + "：" + what + "（" + n.First.ToString("M/d HH:mm") + " 発生" + (n.Count > 1 ? "・" + n.Count + "回" : "") + "）", -1, -1);
            }
            catch (Exception) { }
        }

        /// <summary>お知らせの種類（画面用）</summary>
        public static string NoticeKind(Notice n)
        {
            switch (n.Action)
            {
                case "conflicts": return "上書きの確認";
                case "dup": return "重複";
                case "pending": return "ミラーの削除保留";
                case "synccand": return "同期の削除";
                case "partial": return "一部失敗";
            }
            if (n.Level == NoticeLevel.Error) return "エラー";
            if (n.Level == NoticeLevel.Warning) return "注意";
            return "お知らせ";
        }

        private static string AutoResolved(Notice n)
        {
            JobConfig j = n.JobId != null ? FindJob(n.JobId) : null;
            if (n.JobId != null && j == null) return NoticeNotes.JobGone;
            switch (n.Action)
            {
                case "conflicts":
                    {
                        int c = 0;
                        if (j != null) { JobState st = GetState(j.Id); lock (st) c = st.Conflicts.Count; }
                        else c = TotalConflicts();
                        return c == 0 ? NoticeNotes.Seen : null;
                    }
                case "pending":
                    {
                        if (j == null) return TotalPendingDeletes() == 0 ? NoticeNotes.Seen : null;
                        JobState st = GetState(j.Id);
                        int c; lock (st) c = st.PendingDeletes.Count;
                        return c == 0 ? NoticeNotes.Seen : null;
                    }
                case "dup":
                    if (j == null) return null;
                    return (!j.DedupOn || DedupStore.PendingCount(j.Id) == 0) && DupChecker.UnseenCandidates(j.Id) == 0 ? NoticeNotes.Seen : null;
                case "synccand":
                    if (j == null) return null;
                    return !j.Sync || SyncDeleteStore.Count(j.Id) == 0 ? NoticeNotes.Seen : null;
                case "partial":
                    {
                        if (j == null) return null;
                        if (!j.Enabled) return NoticeNotes.Disabled;
                        JobRuntime rt = GetRuntime(j.Id);
                        if (!rt.Running && rt.LastResult == "ok" && rt.LastRun >= n.Time.AddSeconds(-1) && !(rt.LastMessage ?? "").StartsWith(JobOutcome.Partial))
                            return NoticeNotes.Recovered;
                        return null;
                    }
                case "retry":
                case "job":
                    {
                        if (j == null) return null;
                        if (!j.Enabled) return NoticeNotes.Disabled;
                        JobRuntime rt = GetRuntime(j.Id);
                        if (!rt.Running && rt.LastResult == "ok" && rt.LastRun >= n.Time.AddSeconds(-1)) return NoticeNotes.Recovered;
                        return null;
                    }
            }
            return null;
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
                int lp = DedupStore.PendingCount(j.Id);
                if (lp > 0 && j.DedupOn) list.Add(Item(j.Name + "：大きいファイルの重複 " + lp + "件 の扱いを選んでください", "dup", j.Id, false));
                int n = DupChecker.UnseenCandidates(j.Id);
                if (n > 0) list.Add(Item(j.Name + "：重複チェックで削除候補が " + n + "件 見つかりました", "dup", j.Id, false));
            }
            foreach (JobConfig j in Settings.Jobs.ToArray())
            {
                int sc = j.Sync ? SyncDeleteStore.Count(j.Id) : 0;
                if (sc > 0) list.Add(Item(j.Name + "：同期で削除されたファイル " + sc + "件 の扱いを選んでください", "synccand", j.Id, false));
            }
            // エラー：未対応のエラーのお知らせがあるもの（開いて確認するか、その後正常に実行できれば消える）
            RefreshNoticeStates();
            HashSet<string> errJobs = new HashSet<string>();
            foreach (Notice n in RecentNotices()) if (!n.Handled && n.Level == NoticeLevel.Error && n.JobId != null) errJobs.Add(n.JobId);
            foreach (JobConfig j in Settings.Jobs.ToArray())
            {
                if (!j.Enabled || !errJobs.Contains(j.Id)) continue;
                JobRuntime rt = GetRuntime(j.Id);
                if (rt.LastResult == "error" || rt.LastResult == "partial")
                    list.Add(Item(j.Name + (rt.LastResult == "partial" ? "：一部失敗　" : "：エラー　") + (rt.LastMessage ?? ""), "error", j.Id, true));
            }
            return list;
        }

        private static AttentionItem Item(string text, string action, string jobId, bool err)
        {
            AttentionItem i = new AttentionItem();
            i.Text = text; i.Action = action; i.JobId = jobId; i.IsError = err;
            return i;
        }

        /// <summary>確認が済んでいないエラーがあるか（アイコンを赤にする）</summary>
        public static bool AnyError()
        {
            foreach (AttentionItem i in AttentionItems()) if (i.IsError) return true;
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
