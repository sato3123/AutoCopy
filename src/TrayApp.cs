using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>タスクトレイに常駐する本体</summary>
    public class TrayApp : ApplicationContext
    {
        public static TrayApp Instance;
        private readonly NotifyIcon icon;
        private readonly ContextMenuStrip menu;
        private readonly System.Windows.Forms.Timer refresh;
        private readonly SynchronizationContext ui;
        private readonly Control invoker;
        private Notice lastBalloon;

        private MainForm mainForm;
        private LogForm logForm;
        private ConflictForm conflictForm;
        private DupForm dupForm;
        private SettingsForm settingsForm;

        public TrayApp()
        {
            Instance = this;
            invoker = new Control();
            IntPtr forceHandle = invoker.Handle; // UI スレッドでハンドルを作っておく（BeginInvoke 用）
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            menu = new ContextMenuStrip();
            menu.Font = Ui.Base;
            menu.Opening += delegate { BuildMenu(); };

            icon = new NotifyIcon();
            icon.Icon = IconFactory.App;
            icon.Text = "自動コピー";
            icon.ContextMenuStrip = menu;
            icon.Visible = true;
            icon.DoubleClick += delegate { ShowMain(); };
            icon.BalloonTipClicked += delegate { if (lastBalloon != null) DoAction(lastBalloon.Action, lastBalloon.JobId); };

            AppCore.NoticeAdded += delegate(Notice n) { Post(delegate { ShowBalloon(n); }); };
            SingleInstance.StartServer(delegate(string cmd) { Post(delegate { HandleCommand(cmd); }); });

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 2000;
            refresh.Tick += delegate { UpdateIcon(); };
            refresh.Start();
            UpdateIcon();
        }

        public void Post(Action a)
        {
            try { invoker.BeginInvoke(a); } catch (Exception) { }
        }

        public void HandleCommand(string cmd)
        {
            if (cmd == null) return;
            if (cmd == "show") ShowMain();
            else if (cmd == "run") { AppCore.RunAll(true); Balloon("今すぐ実行", "すべてのジョブを実行しています。", ToolTipIcon.Info); }
            else if (cmd.StartsWith("run|"))
            {
                string name = cmd.Substring(4);
                JobConfig j = AppCore.FindJob(name);
                if (j == null) Balloon("ジョブが見つかりません", "「" + name + "」というジョブはありません。", ToolTipIcon.Warning);
                else { AppCore.RunJob(j.Id, true, false); Balloon("今すぐ実行", j.Name + " を実行しています。", ToolTipIcon.Info); }
            }
            else if (cmd == "dupcheck") { AppCore.RunDupAll(); Balloon("重複チェック", "重複チェックを始めました。終わったらお知らせします。", ToolTipIcon.Info); }
            else if (cmd.StartsWith("import|")) ImportSetup(cmd.Substring(7));
            // "startup" は何も表示しない
        }

        private void ImportSetup(string path)
        {
            try
            {
                DistEnvelope env = Distribution.ReadEnvelope(path);
                if (env.Version <= AppCore.Settings.DistVersion)
                {
                    Ui.Info(null, "この配布設定（第" + env.Version + "版）は取り込み済みです。");
                    return;
                }
                using (SetupForm f = new SetupForm(path, env, false)) f.ShowDialog();
            }
            catch (Exception ex) { Ui.Error(null, ex.Message); }
        }

        private void Balloon(string title, string text, ToolTipIcon ic)
        {
            lastBalloon = null;
            icon.ShowBalloonTip(5000, title, text, ic);
        }

        private void ShowBalloon(Notice n)
        {
            lastBalloon = n;
            ToolTipIcon ic = n.Level == NoticeLevel.Error ? ToolTipIcon.Error
                : n.Level == NoticeLevel.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
            icon.ShowBalloonTip(8000, n.Title ?? "自動コピー", string.IsNullOrEmpty(n.Text) ? " " : n.Text, ic);
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            Color main = AppCore.GlobalPaused ? IconFactory.Gray : IconFactory.Blue;
            Color? badge = null;
            string status;
            bool problem = AppCore.AnyError() || AppCore.TotalConflicts() > 0 || AppCore.TotalPendingDeletes() > 0;
            if (problem) badge = IconFactory.Orange;
            else if (AppCore.AnyRunning()) badge = IconFactory.Green;
            if (AppCore.GlobalPaused) status = "一時停止中";
            else if (AppCore.AnyRunning()) status = "コピー中";
            else status = "動作中";
            if (problem) status += "（確認が必要です）";
            icon.Icon = IconFactory.Get(main, badge);
            string t = "自動コピー：" + status;
            icon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        private void BuildMenu()
        {
            menu.Items.Clear();
            string status = AppCore.GlobalPaused
                ? (AppCore.GlobalPauseUntil == DateTime.MaxValue ? "一時停止中（再開するまで）" : "一時停止中（" + AppCore.GlobalPauseUntil.Value.ToString("HH:mm") + " まで）")
                : (AppCore.AnyRunning() ? "コピー中" : "動作中");
            ToolStripMenuItem head = new ToolStripMenuItem(status + "　確認待ち " + AppCore.TotalConflicts() + "件");
            head.Enabled = false;
            menu.Items.Add(head);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("すべて今すぐ実行", null, delegate { HandleCommand("run"); });

            ToolStripMenuItem pick = new ToolStripMenuItem("ジョブを選んで実行");
            foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
            {
                string id = j.Id;
                ToolStripMenuItem it = new ToolStripMenuItem(j.Name + (j.Mirror ? "（ミラー）" : ""), null, delegate { AppCore.RunJob(id, true, false); });
                it.Enabled = j.Enabled;
                pick.DropDownItems.Add(it);
            }
            if (pick.DropDownItems.Count == 0) pick.Enabled = false;
            menu.Items.Add(pick);

            ToolStripMenuItem pause = new ToolStripMenuItem("一時停止");
            pause.DropDownItems.Add("30分", null, delegate { AppCore.Pause(null, 30); });
            pause.DropDownItems.Add("1時間", null, delegate { AppCore.Pause(null, 60); });
            pause.DropDownItems.Add("3時間", null, delegate { AppCore.Pause(null, 180); });
            pause.DropDownItems.Add("再開するまで", null, delegate { AppCore.Pause(null, 0); });
            pause.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem resume = new ToolStripMenuItem("再開する", null, delegate { AppCore.Resume(null); });
            resume.Enabled = AppCore.GlobalPaused;
            pause.DropDownItems.Add(resume);
            ToolStripMenuItem note = new ToolStripMenuItem("PCを再起動すると自動で再開します");
            note.Enabled = false;
            pause.DropDownItems.Add(note);
            menu.Items.Add(pause);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("メイン画面を開く", null, delegate { ShowMain(); });
            menu.Items.Add("上書きの確認（" + AppCore.TotalConflicts() + "件）", null, delegate { ShowConflicts(); });
            int pend = AppCore.TotalPendingDeletes();
            if (pend > 0) menu.Items.Add("ミラーの保留中の削除（" + pend + "件）", null, delegate { ShowPending(null); });
            menu.Items.Add("重複チェック結果", null, delegate { ShowDup(null); });
            menu.Items.Add("ログを見る", null, delegate { ShowLog(); });
            menu.Items.Add("設定", null, delegate { ShowSettings(); });
            menu.Items.Add("操作説明書", null, delegate { Ui.OpenManual(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, delegate { ExitApp(); });
        }

        public void DoAction(string action, string jobId)
        {
            switch (action)
            {
                case "conflicts": ShowConflicts(); break;
                case "dup": ShowDup(jobId); break;
                case "pending": ShowPending(jobId); break;
                case "log": ShowLog(); break;
                case "job": ShowMain(); if (mainForm != null && jobId != null) mainForm.EditJob(jobId); break;
                case "retry": if (jobId != null) AppCore.RunJob(jobId, true, false); ShowMain(); break;
                default: ShowMain(); break;
            }
        }

        private static void Bring(Form f)
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Show();
            f.Activate();
        }

        public void ShowMain()
        {
            if (mainForm == null || mainForm.IsDisposed) mainForm = new MainForm();
            Bring(mainForm);
        }

        public void ShowLog()
        {
            if (logForm == null || logForm.IsDisposed) logForm = new LogForm();
            Bring(logForm);
        }

        public void ShowConflicts()
        {
            if (conflictForm == null || conflictForm.IsDisposed) conflictForm = new ConflictForm();
            else conflictForm.Reload();
            Bring(conflictForm);
        }

        public void ShowDup(string jobId)
        {
            if (dupForm == null || dupForm.IsDisposed) dupForm = new DupForm();
            dupForm.SelectJob(jobId);
            Bring(dupForm);
        }

        public void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm();
            Bring(settingsForm);
        }

        public void ShowPending(string jobId)
        {
            foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
            {
                if (jobId != null && j.Id != jobId) continue;
                JobState st = AppCore.GetState(j.Id);
                int n;
                lock (st) { n = st.PendingDeletes.Count; }
                if (n == 0) continue;
                using (PendingDeleteForm f = new PendingDeleteForm(j)) f.ShowDialog();
            }
        }

        public void ExitApp()
        {
            string msg = "自動コピーを終了します。終了すると、次にPCを起動するまで自動コピーは行われません。";
            if (AppCore.AnyRunning()) msg = "コピー中です。中止して終了しますか？\n（コピー中のファイルは次回コピーし直します）";
            if (MessageBox.Show(msg, "自動コピー", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Logger.Info(null, "終了しました");
            AppCore.Stop();
            icon.Visible = false;
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refresh.Dispose();
                icon.Visible = false;
                icon.Dispose();
                menu.Dispose();
                invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
