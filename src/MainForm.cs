using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoCopy
{
    public class MainForm : Form
    {
        private readonly ListView jobs;
        private readonly ListView notices;
        private readonly Label statusLabel;
        private readonly Label confirmLabel;
        private readonly Button confirmButton;
        private readonly Button pendingButton;
        private readonly ToolStripStatusLabel st1, st2, st3;
        private readonly System.Windows.Forms.Timer timer;
        private bool filling;

        public MainForm()
        {
            Ui.SetupForm(this, "自動コピー", 1000, 640);
            MinimumSize = new Size(760, 480);

            // ---- ツールバー ----
            Button pauseBtn = Ui.B("一時停止 ▾", null);
            ContextMenuStrip pauseMenu = new ContextMenuStrip();
            pauseMenu.Font = Ui.Base;
            pauseMenu.Items.Add("30分", null, delegate { AppCore.Pause(null, 30); RefreshView(); });
            pauseMenu.Items.Add("1時間", null, delegate { AppCore.Pause(null, 60); RefreshView(); });
            pauseMenu.Items.Add("3時間", null, delegate { AppCore.Pause(null, 180); RefreshView(); });
            pauseMenu.Items.Add("再開するまで", null, delegate { AppCore.Pause(null, 0); RefreshView(); });
            pauseMenu.Items.Add(new ToolStripSeparator());
            pauseMenu.Items.Add("再開する", null, delegate { AppCore.Resume(null); RefreshView(); });
            pauseBtn.Click += delegate { pauseMenu.Show(pauseBtn, new Point(0, pauseBtn.Height)); };

            Button runAll = Ui.B("▶ すべて今すぐ実行", delegate { AppCore.RunAll(true); RefreshView(); });
            runAll.BackColor = Ui.Accent; runAll.ForeColor = Color.White; runAll.UseVisualStyleBackColor = false;
            runAll.FlatStyle = FlatStyle.Flat; runAll.FlatAppearance.BorderColor = Ui.Accent;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.AutoSize = true;
            bar.Padding = new Padding(8, 8, 8, 4);
            bar.BackColor = Color.White;
            bar.Controls.AddRange(new Control[] {
                runAll,
                Ui.B("選んだジョブを実行", delegate { RunSelected(false); }),
                Ui.B("中止", delegate { foreach (string id in SelectedIds()) AppCore.CancelJob(id); }),
                pauseBtn,
                Sep(),
                Ui.B("＋ ジョブを追加", delegate { AddJob(); }),
                Ui.B("編集", delegate { string id = FirstSelected(); if (id != null) EditJob(id); }),
                Ui.B("複製", delegate { DuplicateJob(); }),
                Ui.B("削除", delegate { DeleteJob(); }),
                Sep(),
                Ui.B("重複チェック", delegate { TrayApp.Instance.ShowDup(FirstSelected()); }),
                Ui.B("上書きの確認", delegate { TrayApp.Instance.ShowConflicts(); }),
                Ui.B("ログ", delegate { TrayApp.Instance.ShowLog(); }),
                Ui.B("容量の内訳", delegate { ShowUsage(); }),
                Ui.B("設定", delegate { TrayApp.Instance.ShowSettings(); }),
                Ui.B("ヘルプ", delegate { Ui.OpenManual(); })
            });

            // ---- 状態の帯 ----
            statusLabel = Ui.L("");
            statusLabel.Font = Ui.Bold;
            confirmLabel = Ui.L("");
            confirmLabel.ForeColor = Ui.Warn;
            confirmLabel.Font = Ui.Bold;
            confirmButton = Ui.B("確認する", delegate { TrayApp.Instance.ShowConflicts(); });
            pendingButton = Ui.B("保留中の削除を確認", delegate { TrayApp.Instance.ShowPending(null); });
            FlowLayoutPanel band = new FlowLayoutPanel();
            band.Dock = DockStyle.Top;
            band.AutoSize = true;
            band.Padding = new Padding(10, 4, 10, 4);
            band.BackColor = Ui.InfoBack;
            band.Controls.AddRange(new Control[] { statusLabel, confirmLabel, confirmButton, pendingButton });

            // ---- ジョブ一覧 ----
            jobs = Ui.List("ジョブ名|160", "方式|70", "コピー元 → コピー先|300", "間隔|56", "前回|70", "結果|240", "次回|70");
            jobs.CheckBoxes = true;
            jobs.Dock = DockStyle.Fill;
            jobs.ItemChecked += OnItemChecked;
            jobs.DoubleClick += delegate { string id = FirstSelected(); if (id != null) EditJob(id); };
            jobs.ContextMenuStrip = BuildJobMenu();

            // ---- お知らせ ----
            notices = Ui.List("時刻|70", "お知らせ|780");
            notices.Dock = DockStyle.Fill;
            notices.DoubleClick += delegate
            {
                if (notices.SelectedItems.Count == 0) return;
                Notice n = notices.SelectedItems[0].Tag as Notice;
                if (n != null) TrayApp.Instance.DoAction(n.Action, n.JobId);
            };
            Panel noticePanel = new Panel();
            noticePanel.Dock = DockStyle.Bottom;
            noticePanel.Height = 170;
            Label nh = Ui.L("最近のお知らせ（ダブルクリックで該当の画面を開きます）");
            nh.Font = Ui.Bold;
            nh.Dock = DockStyle.Top;
            noticePanel.Controls.Add(notices);
            noticePanel.Controls.Add(nh);

            StatusStrip ss = new StatusStrip();
            st1 = new ToolStripStatusLabel();
            st2 = new ToolStripStatusLabel();
            st3 = new ToolStripStatusLabel();
            st3.Spring = true;
            st3.TextAlign = ContentAlignment.MiddleRight;
            ss.Items.AddRange(new ToolStripItem[] { st1, st2, st3 });

            Controls.Add(jobs);
            Controls.Add(noticePanel);
            Controls.Add(band);
            Controls.Add(bar);
            Controls.Add(ss);

            AppCore.Changed += OnCoreChanged;
            FormClosed += delegate { AppCore.Changed -= OnCoreChanged; timer.Stop(); };
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += delegate { RefreshView(); };
            timer.Start();
            RefreshView();
        }

        private static Control Sep()
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Width = 1; l.Height = 26;
            l.BackColor = Color.FromArgb(224, 224, 224);
            l.Margin = new Padding(6, 4, 6, 4);
            return l;
        }

        private void ShowUsage()
        {
            if (AppCore.Settings.Jobs.Count == 0) { Ui.Info(this, "ジョブがありません。"); return; }
            using (UsageForm f = new UsageForm(FirstSelected())) f.ShowDialog(this);
        }

        private ContextMenuStrip BuildJobMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = Ui.Base;
            m.Items.Add("今すぐ実行", null, delegate { RunSelected(false); });
            m.Items.Add("全体を再確認して実行（コピー先で消えたファイルも戻す）", null, delegate { RunSelected(true); });
            m.Items.Add("編集", null, delegate { string id = FirstSelected(); if (id != null) EditJob(id); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("このジョブだけ1時間止める", null, delegate { foreach (string id in SelectedIds()) AppCore.Pause(id, 60); RefreshView(); });
            m.Items.Add("このジョブを再開", null, delegate { foreach (string id in SelectedIds()) AppCore.Resume(id); RefreshView(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("重複チェック", null, delegate { string id = FirstSelected(); if (id != null) { AppCore.RunDup(id); Ui.Info(this, "重複チェックを始めました。終わったらお知らせします。"); } });
            m.Items.Add("削除済み・退避から元に戻す…", null, delegate
            {
                string id = FirstSelected();
                JobConfig j = id != null ? AppCore.FindJob(id) : null;
                if (j != null) using (TrashForm f = new TrashForm(j)) f.ShowDialog(this);
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("容量の内訳を調べる", null, delegate { ShowUsage(); });
            m.Items.Add("コピー元を開く", null, delegate { JobConfig j = SelectedJob(); if (j != null) Ui.OpenFolder(CopyEngine.ResolvedSource(j, AppCore.Settings)); });
            m.Items.Add("コピー先を開く", null, delegate { JobConfig j = SelectedJob(); if (j != null) Ui.OpenFolder(CopyEngine.ResolvedDest(j, AppCore.Settings)); });
            m.Items.Add("この ジョブのショートカットをデスクトップに作る", null, delegate
            {
                JobConfig j = SelectedJob();
                if (j == null) return;
                try
                {
                    Shortcuts.CreateOnDesktop("自動コピー（" + j.Name + "）", "/run \"" + j.Name + "\"");
                    Ui.Info(this, "デスクトップにショートカットを作りました。");
                }
                catch (Exception ex) { Ui.Error(this, ex.Message); }
            });
            return m;
        }

        private void OnCoreChanged()
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(RefreshView)); } catch (Exception) { }
        }

        private JobConfig SelectedJob()
        {
            string id = FirstSelected();
            return id == null ? null : AppCore.FindJob(id);
        }

        private string FirstSelected()
        {
            if (jobs.SelectedItems.Count > 0) return (string)jobs.SelectedItems[0].Tag;
            if (jobs.Items.Count == 1) return (string)jobs.Items[0].Tag;
            return null;
        }

        private List<string> SelectedIds()
        {
            List<string> l = new List<string>();
            foreach (ListViewItem it in jobs.SelectedItems) l.Add((string)it.Tag);
            return l;
        }

        private void RunSelected(bool full)
        {
            List<string> ids = SelectedIds();
            if (ids.Count == 0) { Ui.Info(this, "実行するジョブを選んでください。"); return; }
            foreach (string id in ids) AppCore.RunJob(id, true, full);
            RefreshView();
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (filling) return;
            JobConfig j = AppCore.FindJob((string)e.Item.Tag);
            if (j == null || j.Enabled == e.Item.Checked) return;
            j.Enabled = e.Item.Checked;
            AppCore.SaveSettings();
        }

        public void RefreshView()
        {
            if (IsDisposed) return;
            AppSettings s = AppCore.Settings;
            filling = true;
            try
            {
                jobs.BeginUpdate();
                if (jobs.Items.Count != s.Jobs.Count) jobs.Items.Clear();
                for (int i = 0; i < s.Jobs.Count; i++)
                {
                    JobConfig j = s.Jobs[i];
                    JobRuntime rt = AppCore.GetRuntime(j.Id);
                    JobState stt = AppCore.GetState(j.Id);
                    int conflicts, pending;
                    lock (stt) { conflicts = stt.Conflicts.Count; pending = stt.PendingDeletes.Count; }

                    string result;
                    Color color = SystemColors.WindowText;
                    if (rt.Running || rt.DupRunning) { result = rt.Phase + "…"; color = Ui.Accent; }
                    else if (rt.LastResult == "error") { result = "エラー　" + rt.LastMessage; color = Ui.Err; }
                    else if (rt.LastResult == "ok") { result = "正常　" + rt.LastMessage; color = Ui.Ok; }
                    else result = "未実行";
                    if (conflicts > 0) result = "確認待ち " + conflicts + "件　" + result;
                    if (pending > 0) result = "削除保留 " + pending + "件　" + result;
                    if (!j.Enabled) { result = "無効"; color = Ui.Quiet; }

                    string next;
                    if (!j.Enabled) next = "—";
                    else if (AppCore.IsPaused(j.Id)) next = "停止中";
                    else if (rt.Running) next = "—";
                    else next = rt.NextRun.Date == DateTime.Today ? rt.NextRun.ToString("HH:mm") : rt.NextRun.ToString("M/d HH:mm");

                    string interval = j.IntervalMinutes % 60 == 0 ? (j.IntervalMinutes / 60) + "時間" : j.IntervalMinutes + "分";
                    string last = rt.LastRun == DateTime.MinValue ? "—" : (rt.LastRun.Date == DateTime.Today ? rt.LastRun.ToString("HH:mm") : rt.LastRun.ToString("M/d HH:mm"));
                    string paths = CopyEngine.ResolvedSource(j, s) + "  →  " + CopyEngine.ResolvedDest(j, s);
                    string[] cols = new string[] { j.Name + (j.Scope == JobScope.Dist ? "" : "（このPCだけ）"), j.Mirror ? "ミラー" : "追加・更新", paths, interval, last, result, next };

                    ListViewItem it;
                    if (i < jobs.Items.Count) it = jobs.Items[i];
                    else
                    {
                        it = new ListViewItem(cols[0]);
                        for (int c = 1; c < cols.Length; c++) it.SubItems.Add(cols[c]);
                        it.UseItemStyleForSubItems = false;
                        jobs.Items.Add(it);
                    }
                    it.Tag = j.Id;
                    for (int c = 0; c < cols.Length; c++) if (it.SubItems[c].Text != cols[c]) it.SubItems[c].Text = cols[c];
                    it.Checked = j.Enabled;
                    it.SubItems[5].ForeColor = color;
                    it.SubItems[1].ForeColor = j.Mirror ? Ui.Warn : SystemColors.WindowText;
                    it.ForeColor = j.Enabled ? SystemColors.WindowText : Ui.Quiet;
                }
                jobs.EndUpdate();
            }
            finally { filling = false; }

            // 状態の帯
            if (AppCore.GlobalPaused)
            {
                statusLabel.Text = "● 自動コピー：一時停止中" + (AppCore.GlobalPauseUntil == DateTime.MaxValue ? "（再開するまで）" : "（" + AppCore.GlobalPauseUntil.Value.ToString("HH:mm") + " まで）");
                statusLabel.ForeColor = Ui.Quiet;
            }
            else
            {
                statusLabel.Text = "● 自動コピー：" + (AppCore.AnyRunning() ? "コピー中" : "動作中");
                statusLabel.ForeColor = Ui.Ok;
            }
            int tc = AppCore.TotalConflicts(), tp = AppCore.TotalPendingDeletes();
            confirmLabel.Text = tc > 0 ? "　上書き確認待ち " + tc + "件" : "";
            confirmButton.Visible = tc > 0;
            pendingButton.Visible = tp > 0;
            pendingButton.Text = "ミラーの保留中の削除 " + tp + "件を確認";

            // お知らせ
            List<Notice> list = AppCore.RecentNotices();
            if (notices.Items.Count != list.Count || (list.Count > 0 && notices.Items[0].Tag != list[0]))
            {
                notices.BeginUpdate();
                notices.Items.Clear();
                foreach (Notice n in list)
                {
                    ListViewItem it = new ListViewItem(n.Time.ToString("HH:mm"));
                    it.SubItems.Add((n.Title ?? "") + (string.IsNullOrEmpty(n.Text) ? "" : "　" + n.Text));
                    it.ForeColor = n.Level == NoticeLevel.Error ? Ui.Err : n.Level == NoticeLevel.Warning ? Ui.Warn : n.Level == NoticeLevel.Ok ? Ui.Ok : SystemColors.WindowText;
                    it.Tag = n;
                    notices.Items.Add(it);
                }
                notices.EndUpdate();
            }

            st1.Text = s.FolderName + "（" + Environment.MachineName + "）";
            st2.Text = s.DistVersion > 0 ? "配布設定 " + (s.DistName ?? "") + " 第" + s.DistVersion + "版" : "配布設定なし";
            st3.Text = string.IsNullOrEmpty(s.LogRoot) ? "ログ：このPCのみ" : "ログ保管先：" + s.LogRoot;
        }

        // ---------- ジョブの追加・編集 ----------

        private void AddJob()
        {
            JobConfig j = new JobConfig();
            j.Scope = JobScope.Local;
            using (JobEditForm f = new JobEditForm(j, true))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                AppCore.Settings.Jobs.Add(f.Result);
                AppCore.SaveSettings();
            }
            RefreshView();
        }

        public void EditJob(string id)
        {
            JobConfig j = AppCore.FindJob(id);
            if (j == null) return;
            using (JobEditForm f = new JobEditForm(j.Clone(), false))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                int idx = AppCore.Settings.Jobs.IndexOf(j);
                if (idx < 0) return;
                bool pathsChanged = !string.Equals(j.Source, f.Result.Source, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(j.Dest, f.Result.Dest, StringComparison.OrdinalIgnoreCase);
                if (pathsChanged)
                {
                    // 前回までの記録は別のフォルダのものなので使わない（ミラーで誤って消さないため）
                    AppCore.CancelJob(j.Id);
                    AppCore.ForgetJobState(j.Id);
                }
                AppCore.Settings.Jobs[idx] = f.Result;
                AppCore.SaveSettings();
            }
            RefreshView();
        }

        private void DuplicateJob()
        {
            JobConfig j = SelectedJob();
            if (j == null) return;
            JobConfig c = j.Clone();
            c.Id = Guid.NewGuid().ToString("N");
            c.Name = j.Name + "（コピー）";
            c.Scope = JobScope.Local;
            using (JobEditForm f = new JobEditForm(c, true))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                AppCore.Settings.Jobs.Add(f.Result);
                AppCore.SaveSettings();
            }
            RefreshView();
        }

        private void DeleteJob()
        {
            JobConfig j = SelectedJob();
            if (j == null) return;
            string msg = "ジョブ「" + j.Name + "」を削除しますか？\nコピー元・コピー先のファイルはそのまま残ります。";
            if (j.Scope == JobScope.Dist) msg += "\n\n※ 配布設定のジョブです。新しい配布設定を読み込むと戻ります。";
            if (!Ui.Confirm(this, msg)) return;
            AppCore.CancelJob(j.Id);
            AppCore.Settings.Jobs.Remove(j);
            AppCore.SaveSettings();
            AppCore.ForgetJobState(j.Id);
            Logger.Info(j.Name, "ジョブを削除しました");
            RefreshView();
        }
    }
}
