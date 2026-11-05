using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>作業状況：ジョブごとの進み具合と、今コピーしているファイル</summary>
    public class ProgressForm : Form
    {
        private readonly ListView jobs, files;
        private readonly ProgressBar bar;
        private readonly Label lPhase, lFiles, lBytes, lSpeed, lLast;
        private readonly Button stop;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private string selectedId;

        public ProgressForm(string jobId)
        {
            Ui.SetupForm(this, "作業状況", 900, 600);
            selectedId = jobId;

            jobs = Ui.List("ジョブ|150", "状態|160", "進み具合|70", "ファイル|120", "容量|160", "速さ|90", "残り|80");
            jobs.Dock = DockStyle.Top;
            jobs.Height = Ui.S(140);
            foreach (int i in new int[] { 2, 3, 4, 5, 6 }) jobs.Columns[i].TextAlign = HorizontalAlignment.Right;
            jobs.SelectedIndexChanged += delegate
            {
                if (jobs.SelectedItems.Count > 0) { selectedId = jobs.SelectedItems[0].Tag as string; Refresh2(); }
            };

            lPhase = Ui.L("");
            lPhase.Font = Ui.Bold;
            bar = new ProgressBar();
            bar.Maximum = 1000;
            bar.Width = Math.Min(Ui.S(520), Ui.MaxWidth(520) - Ui.S(10));   // 画面が狭いときは縮める
            bar.Height = Ui.S(20);
            bar.Margin = new Padding(3, 6, 3, 6);
            lFiles = Ui.L("");
            lBytes = Ui.L("");
            lSpeed = Ui.L("");
            lLast = Ui.Hint("");
            Ui.FitToForm(lLast, 800);
            stop = Btn.MakeCaution(Ui.B("このジョブを中止", delegate
            {
                if (selectedId == null) return;
                JobConfig sj = AppCore.FindJob(selectedId);
                if (!Ui.Confirm(this, "「" + (sj != null ? sj.Name : "") + "」を中止します。\nコピーの途中のファイルは、次の実行でコピーし直します。よろしいですか？")) return;
                AppCore.CancelJob(selectedId);
            }));
            FlowLayoutPanel barRow = Ui.WrapRow(bar, stop);
            Ui.FitToForm(barRow, 860);
            foreach (Label l in new Label[] { lFiles, lBytes, lSpeed }) Ui.FitToForm(l, 860);
            FlowLayoutPanel detail = Ui.Column(lPhase, barRow, lFiles, lBytes, lSpeed, lLast,
                Ui.L("今コピーしているファイル"));
            detail.Dock = DockStyle.Top;
            detail.Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), 0);

            files = Ui.List("ファイル|420", "種類|70", "サイズ|100", "済み|100", "進み具合|80");
            files.Dock = DockStyle.Fill;
            foreach (int i in new int[] { 2, 3, 4 }) files.Columns[i].TextAlign = HorizontalAlignment.Right;

            Controls.Add(files);
            Controls.Add(detail);
            Controls.Add(jobs);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }),
                Ui.B("ログを見る", delegate { if (TrayApp.Instance != null) TrayApp.Instance.ShowLog(); })));

            timer.Interval = 500;
            timer.Tick += delegate { Refresh2(); };
            Shown += delegate { Refresh2(); timer.Start(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }

        public void SelectJob(string jobId) { if (jobId != null) selectedId = jobId; Refresh2(); }

        private static string Pct(JobProgress p) { return p.Percent.ToString("0.0") + "%"; }

        public void Refresh2()
        {
            List<JobConfig> list = new List<JobConfig>(AppCore.Settings.Jobs.ToArray());
            // 実行中のジョブを先に、選んでいなければ実行中の最初のジョブを選ぶ
            if (selectedId == null || AppCore.FindJob(selectedId) == null)
                foreach (JobConfig j in list) if (AppCore.GetRuntime(j.Id).Running) { selectedId = j.Id; break; }
            if (selectedId == null && list.Count > 0) selectedId = list[0].Id;

            jobs.BeginUpdate();
            if (jobs.Items.Count != list.Count) jobs.Items.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                JobConfig j = list[i];
                JobRuntime rt = AppCore.GetRuntime(j.Id);
                JobProgress p = rt.Progress != null ? rt.Progress.Snapshot() : null;
                string state, pct = "", fl = "", by = "", sp = "", rem = "";
                if (rt.Running && p != null)
                {
                    state = p.Phase + (p.TotalFiles == 0 && p.Scanned > 0 ? "（" + Fmt.Count(p.Scanned) + "件）" : "");
                    if (p.TotalFiles > 0)
                    {
                        pct = Pct(p);
                        fl = Fmt.Count(p.DoneFiles) + " / " + Fmt.Count(p.TotalFiles);
                        by = Fmt.Size(p.DoneBytes) + " / " + Fmt.Size(p.TotalBytes);
                        sp = p.Speed > 0 ? Fmt.Size((long)p.Speed) + "/秒" : "";
                        rem = JobProgress.Duration(p.RemainingSeconds);
                    }
                }
                else if (rt.Running) state = rt.Phase;
                else if (rt.DupRunning) state = "重複チェック中";
                else if (!j.Enabled) state = "無効";
                else if (p != null && p.Finished != DateTime.MinValue)
                {
                    state = p.Phase + "（" + p.Finished.ToString("HH:mm") + "）";
                    if (p.TotalFiles > 0) { pct = Pct(p); fl = Fmt.Count(p.DoneFiles) + " / " + Fmt.Count(p.TotalFiles); by = Fmt.Size(p.DoneBytes); }
                }
                else state = "待機中（次回 " + (j.StartupOnly && rt.StartupRunDone ? "ログオン時" : rt.NextRun == DateTime.MinValue ? "—" : rt.NextRun.ToString("HH:mm")) + "）";
                string[] cols = { j.Name, state, pct, fl, by, sp, rem };
                ListViewItem it;
                if (i < jobs.Items.Count) it = jobs.Items[i];
                else
                {
                    it = new ListViewItem(cols[0]);
                    for (int c = 1; c < cols.Length; c++) it.SubItems.Add(cols[c]);
                    jobs.Items.Add(it);
                }
                it.Tag = j.Id;
                for (int c = 0; c < cols.Length; c++) if (it.SubItems[c].Text != cols[c]) it.SubItems[c].Text = cols[c];
                it.ForeColor = rt.Running ? Ui.Accent : !j.Enabled ? Ui.Quiet : SystemColors.WindowText;
                it.Font = j.Id == selectedId ? Ui.Bold : Ui.Base;
            }
            ((AcListView)jobs).Resort();   // 見出しで並べ替えている場合は、更新した後に並べ直す
            jobs.EndUpdate();

            // 選んだジョブの詳しい状況
            JobConfig sel = selectedId != null ? AppCore.FindJob(selectedId) : null;
            JobRuntime srt = sel != null ? AppCore.GetRuntime(sel.Id) : null;
            JobProgress sp2 = srt != null && srt.Progress != null ? srt.Progress.Snapshot() : null;
            stop.Enabled = srt != null && srt.Running;
            if (sel == null) { lPhase.Text = "ジョブがありません"; return; }
            if (sp2 == null)
            {
                lPhase.Text = sel.Name + "：" + (srt.Running ? srt.Phase : "まだ実行していません（この画面を開いた後の実行から表示します）");
                bar.Value = 0; lFiles.Text = lBytes.Text = lSpeed.Text = lLast.Text = "";
                files.Items.Clear();
                return;
            }
            lPhase.Text = sel.Name + "：" + sp2.Phase + (sp2.TotalFiles == 0 && sp2.Scanned > 0 ? "（" + Fmt.Count(sp2.Scanned) + "件を調べました）" : "");
            bar.Value = (int)Math.Round(Math.Max(0, Math.Min(100, sp2.Percent)) * 10);
            if (sp2.TotalFiles > 0)
            {
                lFiles.Text = "ファイル：" + Fmt.Count(sp2.DoneFiles) + " / " + Fmt.Count(sp2.TotalFiles) + "件（" + Pct(sp2) + "）"
                    + (sp2.ErrorFiles > 0 ? "　うちエラー・使用中 " + sp2.ErrorFiles + "件" : "")
                    + "　調べたファイル " + Fmt.Count(sp2.Scanned) + "件";
                lBytes.Text = "容量：" + Fmt.Size(sp2.DoneBytes) + " / " + Fmt.Size(sp2.TotalBytes);
                double el = ((sp2.Finished != DateTime.MinValue ? sp2.Finished : DateTime.Now) - sp2.Started).TotalSeconds;
                lSpeed.Text = "速さ：" + (sp2.Speed > 0 ? Fmt.Size((long)sp2.Speed) + "/秒" : "—")
                    + "　経過：" + JobProgress.Duration(el)
                    + (sp2.Finished == DateTime.MinValue ? "　残り：約 " + JobProgress.Duration(sp2.RemainingSeconds) : "");
            }
            else
            {
                lFiles.Text = sp2.Finished != DateTime.MinValue ? "コピーするファイルはありませんでした（調べたファイル " + Fmt.Count(sp2.Scanned) + "件）"
                    : "調べたファイル " + Fmt.Count(sp2.Scanned) + "件";
                lBytes.Text = lSpeed.Text = "";
            }
            lLast.Text = string.IsNullOrEmpty(sp2.LastFile) ? "" : "最後に終わったファイル：" + sp2.LastFile;

            files.BeginUpdate();
            List<ActiveFile> act = sp2.ActiveList;
            if (files.Items.Count != act.Count) files.Items.Clear();
            for (int i = 0; i < act.Count; i++)
            {
                ActiveFile a = act[i];
                string[] cols = { a.Rel, a.Kind, Fmt.Size(a.Size), Fmt.Size(a.Done), a.Size > 0 ? (a.Done * 100.0 / a.Size).ToString("0") + "%" : "" };
                ListViewItem it;
                if (i < files.Items.Count) it = files.Items[i];
                else
                {
                    it = new ListViewItem(cols[0]);
                    for (int c = 1; c < cols.Length; c++) it.SubItems.Add(cols[c]);
                    files.Items.Add(it);
                }
                for (int c = 0; c < cols.Length; c++) if (it.SubItems[c].Text != cols[c]) it.SubItems[c].Text = cols[c];
            }
            ((AcListView)files).Resort();
            files.EndUpdate();
        }
    }
}
