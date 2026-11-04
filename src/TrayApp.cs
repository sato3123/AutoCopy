using System;
using System.Collections.Generic;
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

        // 確認が必要なときの目立つ表示
        private readonly System.Windows.Forms.Timer blink;
        private bool blinkOn;
        private Icon normalIcon;
        private AttentionForm attentionForm;
        private HashSet<string> lastAttention = new HashSet<string>();
        private DateTime nextRemind = DateTime.MinValue;

        // 自動更新
        private readonly System.Windows.Forms.Timer updateTimer;
        private DateTime nextUpdateCheck = DateTime.Now.AddMinutes(2);
        private Updater.Candidate pendingUpdate;
        private bool updateChecking;

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

            blink = new System.Windows.Forms.Timer();
            blink.Interval = 700;
            blink.Tick += delegate
            {
                blinkOn = !blinkOn;
                icon.Icon = blinkOn ? IconFactory.Alert : normalIcon;
            };

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 60 * 1000;
            updateTimer.Tick += delegate { UpdateTick(); };
            updateTimer.Start();
            UpdateIcon();
        }

        // ================= 確認が必要なとき =================

        /// <summary>確認が必要なものを調べ、アイコンの点滅・お知らせ画面を出す</summary>
        private void CheckAttention(List<AppCore.AttentionItem> items)
        {
            AppSettings s = AppCore.Settings;
            HashSet<string> now = new HashSet<string>();
            foreach (AppCore.AttentionItem it in items) now.Add(it.Action + "|" + it.JobId + "|" + (it.IsError ? "error" : it.Text));
            bool hasNew = false;
            foreach (string k in now) if (!lastAttention.Contains(k)) { hasNew = true; break; }
            lastAttention = now;

            if (items.Count == 0)
            {
                blink.Stop();
                if (attentionForm != null && !attentionForm.IsDisposed) attentionForm.Close();
                nextRemind = DateTime.MinValue;
                return;
            }
            if (s.AttentionBlink) { if (!blink.Enabled) blink.Start(); }
            else blink.Stop();

            if (!s.AttentionPopup) return;
            if (hasNew || DateTime.Now >= nextRemind)
            {
                ShowAttention(items);
                nextRemind = DateTime.Now.AddMinutes(Math.Max(5, s.AttentionRemindMinutes));
            }
            else if (attentionForm != null && !attentionForm.IsDisposed && attentionForm.Visible)
                attentionForm.SetItems(items);
        }

        public void ShowAttention(List<AppCore.AttentionItem> items)
        {
            if (items == null) items = AppCore.AttentionItems();
            if (items.Count == 0) return;
            if (attentionForm == null || attentionForm.IsDisposed)
            {
                attentionForm = new AttentionForm();
                attentionForm.OpenRequested += delegate(string action, string jobId)
                {
                    if (action == null)
                    {
                        List<AppCore.AttentionItem> cur = AppCore.AttentionItems();
                        if (cur.Count > 0) DoAction(cur[0].Action, cur[0].JobId); else ShowMain();
                    }
                    else DoAction(action, jobId);
                };
                attentionForm.Snoozed += delegate { nextRemind = DateTime.Now.AddHours(1); };
            }
            attentionForm.SetItems(items);
            if (!attentionForm.Visible) attentionForm.Show();
            System.Media.SystemSounds.Exclamation.Play();
            // メイン画面を開いていれば、タスクバーのボタンも点滅させる
            if (mainForm != null && !mainForm.IsDisposed && mainForm.Visible) Native.Flash(mainForm.Handle);
        }

        // ================= 自動更新 =================

        private void UpdateTick()
        {
            if (pendingUpdate != null) { TryApplyUpdate(); return; }
            if (DateTime.Now < nextUpdateCheck) return;
            nextUpdateCheck = DateTime.Now.AddMinutes(60);
            if (AppCore.Settings.AutoUpdate) CheckUpdate(false);
        }

        /// <summary>更新版を探す（ネットワークを使うので裏で）。manual = メニューから：結果を知らせる</summary>
        public void CheckUpdate(bool manual)
        {
            if (updateChecking) return;
            updateChecking = true;
            AppSettings s = AppCore.Settings;
            System.Threading.Tasks.Task.Factory.StartNew(delegate()
            {
                string reason;
                Updater.Candidate c = null;
                try { c = Updater.Find(s, out reason); }
                catch (Exception ex) { reason = ex.Message; }
                Post(delegate
                {
                    updateChecking = false;
                    if (c == null)
                    {
                        if (manual) Ui.Info(null, "更新はありません。\n今の版：" + Updater.CurrentVersion + "\n" + reason);
                        else if (reason != null && (reason.Contains("署名") || reason.Contains("改ざん")))
                            AppCore.Notify(NoticeLevel.Warning, "更新版を使いませんでした", reason, "log", null);
                        if (reason != null && !reason.StartsWith("新しい版ではありません") && !reason.StartsWith("更新版の置き場所が設定"))
                            Logger.Info(null, "更新の確認：" + reason);
                        return;
                    }
                    Logger.Info(null, "更新版を見つけました：" + c.Version + "（今の版 " + Updater.CurrentVersion + "）" + c.Path);
                    pendingUpdate = c;
                    TryApplyUpdate();
                });
            });
        }

        /// <summary>コピー中・画面を開いている間は待ち、手が空いたら入れ替えて起動し直す</summary>
        private void TryApplyUpdate()
        {
            Updater.Candidate c = pendingUpdate;
            if (c == null) return;
            if (AppCore.AnyRunning()) return;
            foreach (Form f in Application.OpenForms)
                if (f.Visible && !(f is AttentionForm)) return;   // 何か操作中：閉じるまで待つ
            pendingUpdate = null;
            Version old = Updater.CurrentVersion;
            string exe;
            try { exe = Updater.Install(c); }
            catch (Exception ex)
            {
                Logger.Error(null, "更新できませんでした：" + ex.Message);
                AppCore.Notify(NoticeLevel.Warning, "更新できませんでした", ex.Message, "log", null);
                return;
            }
            Logger.Write(null, "UPDATE", old + " → " + c.Version + "（" + c.Path + "）", -1, -1);
            Logger.Flush();
            try
            {
                System.Diagnostics.Process.Start(exe, "/updated " + System.Diagnostics.Process.GetCurrentProcess().Id + " " + old);
            }
            catch (Exception ex)
            {
                Logger.Error(null, "新しい版を起動できませんでした：" + ex.Message);
                return;
            }
            AppCore.Stop();
            icon.Visible = false;
            SingleInstance.Release();
            ExitThread();
        }

        public void Post(Action a)
        {
            try { invoker.BeginInvoke(a); } catch (Exception) { }
        }

        public void HandleCommand(string cmd)
        {
            if (cmd == null) return;
            if (cmd == "show") ShowMain();
            else if (cmd == "run") { AppCore.RunAll(true); Balloon("強制データ送信", "すべてのジョブを今すぐ実行しています。", ToolTipIcon.Info); }
            else if (cmd.StartsWith("run|"))
            {
                string name = cmd.Substring(4);
                JobConfig j = AppCore.FindJob(name);
                if (j == null) Balloon("ジョブが見つかりません", "「" + name + "」というジョブはありません。", ToolTipIcon.Warning);
                else { AppCore.RunJob(j.Id, true, false); Balloon("今すぐ実行", j.Name + " を実行しています。", ToolTipIcon.Info); }
            }
            else if (cmd == "dupcheck") { AppCore.RunDupAll(); Balloon("重複チェック", "重複チェックを始めました。終わったらお知らせします。", ToolTipIcon.Info); }
            else if (cmd.StartsWith("import|")) ImportSetup(cmd.Substring(7));
            else if (cmd == "checkupdate") CheckUpdate(false);
            else if (cmd.StartsWith("updated|"))
                Balloon("新しい版に更新しました", (cmd.Length > 8 ? cmd.Substring(8) + " → " : "") + Updater.CurrentVersion, ToolTipIcon.Info);
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

        private bool trayNoticeShown;

        /// <summary>メイン画面を閉じたとき：最初の1回だけ、通知領域で動き続けていることを知らせる</summary>
        public void NotifyInTray()
        {
            if (trayNoticeShown) return;
            trayNoticeShown = true;
            Balloon("自動コピーは動き続けています", "画面右下のアイコンに入りました。アイコンをダブルクリックすると画面が開きます。終了するときはアイコンを右クリック →「終了」。", ToolTipIcon.Info);
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
            List<AppCore.AttentionItem> items = AppCore.AttentionItems();
            bool problem = items.Count > 0;
            if (problem) badge = AppCore.AnyError() ? IconFactory.Red : IconFactory.Orange;
            else if (AppCore.AnyRunning()) badge = IconFactory.Green;
            if (AppCore.GlobalPaused) status = "一時停止中";
            else if (AppCore.AnyRunning()) status = "コピー中";
            else status = "動作中";
            if (problem) status = "確認が必要です（" + items.Count + "件）";
            normalIcon = IconFactory.Get(main, badge);
            if (!blink.Enabled || !blinkOn) icon.Icon = normalIcon;
            CheckAttention(items);
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
            List<AppCore.AttentionItem> att = AppCore.AttentionItems();
            if (att.Count > 0)
            {
                ToolStripMenuItem a = new ToolStripMenuItem("⚠ 確認が必要です（" + att.Count + "件）", null, delegate { ShowAttention(null); });
                a.Font = new Font(menu.Font, FontStyle.Bold);
                a.ForeColor = Ui.Warn;
                menu.Items.Add(a);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("強制データ送信（すべて今すぐ実行）", null, delegate { HandleCommand("run"); });

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
            menu.Items.Add("更新を確認（今の版 " + Updater.CurrentVersion + "）", null, delegate { CheckUpdate(true); });
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
                blink.Dispose();
                if (attentionForm != null && !attentionForm.IsDisposed) attentionForm.Close();
                updateTimer.Dispose();
                icon.Visible = false;
                icon.Dispose();
                menu.Dispose();
                invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
