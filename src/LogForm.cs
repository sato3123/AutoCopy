using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>ログ表示（このPC／ログ保管先の全PC）</summary>
    public class LogForm : Form
    {
        private readonly ComboBox cbPc, cbJob, cbType;
        private readonly DateTimePicker dFrom, dTo;
        private readonly TextBox tSearch;
        private readonly ListView lv, pcs;
        private readonly Label status;
        private List<LogLine> all = new List<LogLine>();
        private List<LogLine> shown = new List<LogLine>();

        public LogForm()
        {
            Ui.SetupForm(this, "ログ", 1100, 640);
            cbPc = Ui.Combo(150, "このPC", "すべてのPC");
            dFrom = new DateTimePicker(); dFrom.Format = DateTimePickerFormat.Short; dFrom.Width = 110; dFrom.Value = DateTime.Today;
            dTo = new DateTimePicker(); dTo.Format = DateTimePickerFormat.Short; dTo.Width = 110; dTo.Value = DateTime.Today;
            cbJob = Ui.Combo(140, "すべて");
            foreach (JobConfig j in AppCore.Settings.Jobs) cbJob.Items.Add(j.Name);
            cbType = Ui.Combo(130, "すべて", "エラーのみ", "コピー・上書き", "名前変更", "削除・復元", "確認待ち", "集計");
            tSearch = Ui.T("", 140);
            cbJob.SelectedIndexChanged += delegate { ApplyFilter(); };
            cbType.SelectedIndexChanged += delegate { ApplyFilter(); };
            tSearch.TextChanged += delegate { ApplyFilter(); };

            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Top;
            top.AutoSize = true;
            top.Padding = new Padding(8);
            top.BackColor = Color.White;
            top.Controls.AddRange(new Control[] {
                Ui.Row(Ui.L("PC"), cbPc), Ui.Row(Ui.L("期間"), dFrom, Ui.L("〜"), dTo), Ui.B("表示", delegate { LoadLogs(); }),
                Ui.Row(Ui.L("ジョブ"), cbJob), Ui.Row(Ui.L("種類"), cbType), Ui.Row(Ui.L("検索"), tSearch),
                Ui.B("CSVに書き出す", delegate { ExportCsv(); }),
                Ui.B("コピー容量の集計", delegate { ShowStats(); }),
                Ui.B("ログフォルダを開く", delegate { OpenLogFolder(); })
            });

            pcs = Ui.List("フォルダ名|140", "PC名|120", "更新|130", "ジョブ|160", "結果|80", "内容|440");
            pcs.Dock = DockStyle.Top;
            pcs.Height = 120;
            pcs.Visible = false;

            lv = Ui.List("時刻|130", "PC（フォルダ名）|140", "ジョブ|130", "種類|90", "内容|500", "サイズ|80", "時間|60");
            lv.Dock = DockStyle.Fill;
            lv.VirtualMode = true;
            lv.RetrieveVirtualItem += OnRetrieve;

            status = Ui.L("");
            status.Dock = DockStyle.Bottom;
            status.AutoSize = false;
            status.Height = 24;
            status.Padding = new Padding(8, 4, 8, 0);
            status.BackColor = Color.FromArgb(243, 243, 243);

            Controls.Add(lv);
            Controls.Add(pcs);
            Controls.Add(top);
            Controls.Add(status);
            Shown += delegate { LoadLogs(); };
        }

        private void LoadLogs()
        {
            bool allPcs = cbPc.SelectedIndex == 1;
            DateTime from = dFrom.Value.Date, to = dTo.Value.Date;
            if (to < from) { DateTime t = from; from = to; to = t; }
            string note = null;
            List<PcStatus> statuses = null;
            try
            {
                all = Ui.Busy<List<LogLine>>(this, "ログを読み込んでいます…", delegate()
                {
                    string n;
                    List<LogLine> r = Logger.Read(from, to, allPcs, out n);
                    note = n;
                    if (allPcs) statuses = Logger.ReadAllStatus();
                    return r;
                });
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); return; }

            pcs.Visible = allPcs && statuses != null && statuses.Count > 0;
            if (pcs.Visible)
            {
                pcs.BeginUpdate();
                pcs.Items.Clear();
                foreach (PcStatus st in statuses)
                {
                    if (st.Jobs == null || st.Jobs.Count == 0)
                    {
                        ListViewItem it = new ListViewItem(st.FolderName ?? "");
                        it.SubItems.Add(st.ComputerName ?? ""); it.SubItems.Add(st.Updated ?? ""); it.SubItems.Add(""); it.SubItems.Add(""); it.SubItems.Add("");
                        pcs.Items.Add(it);
                        continue;
                    }
                    foreach (JobStatusInfo j in st.Jobs)
                    {
                        ListViewItem it = new ListViewItem(st.FolderName ?? "");
                        it.SubItems.Add(st.ComputerName ?? "");
                        it.SubItems.Add(st.Updated ?? "");
                        it.SubItems.Add(j.Name ?? "");
                        it.SubItems.Add(j.Result == "ok" ? "正常" : j.Result == "error" ? "エラー" : j.Result == "running" ? "実行中" : "");
                        it.SubItems.Add(j.Message ?? "");
                        if (j.Result == "error") it.ForeColor = Ui.Err;
                        pcs.Items.Add(it);
                    }
                }
                pcs.EndUpdate();
            }
            ApplyFilter();
            if (note != null) status.Text = note + "　" + status.Text;
        }

        private bool Match(LogLine l)
        {
            if (cbJob.SelectedIndex > 0 && !string.Equals(l.Job, (string)cbJob.SelectedItem, StringComparison.OrdinalIgnoreCase)) return false;
            switch (cbType.SelectedIndex)
            {
                case 1: if (l.Type != "ERROR" && l.Type != "BLOCKED") return false; break;
                case 2: if (l.Type != "COPY" && l.Type != "OVERWRITE") return false; break;
                case 3: if (l.Type != "RENAME") return false; break;
                case 4: if (l.Type != "DELETE" && l.Type != "RESTORE" && l.Type != "PURGE") return false; break;
                case 5: if (l.Type != "CONFLICT" && l.Type != "HOLD") return false; break;
                case 6: if (l.Type != "SUMMARY" && l.Type != "DUPCHECK") return false; break;
            }
            string q = tSearch.Text.Trim();
            if (q.Length > 0 && (l.Detail ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void ApplyFilter()
        {
            shown = all.FindAll(delegate(LogLine l) { return Match(l); });
            lv.VirtualListSize = 0;
            lv.VirtualListSize = shown.Count;
            lv.Invalidate();
            status.Text = Fmt.Count(shown.Count) + "件を表示（全 " + Fmt.Count(all.Count) + "件）";
        }

        private void OnRetrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            LogLine l = e.ItemIndex < shown.Count ? shown[e.ItemIndex] : null;
            if (l == null) { e.Item = new ListViewItem(new string[] { "", "", "", "", "", "", "" }); return; }
            ListViewItem it = new ListViewItem(l.Time.ToString("yyyy/MM/dd HH:mm:ss"));
            it.SubItems.Add((l.Folder ?? "") + (string.IsNullOrEmpty(l.Pc) ? "" : "（" + l.Pc + "）"));
            it.SubItems.Add(l.Job ?? "");
            it.SubItems.Add(l.Type ?? "");
            it.SubItems.Add(l.Detail ?? "");
            it.SubItems.Add(l.Size >= 0 ? Fmt.Size(l.Size) : "");
            it.SubItems.Add(l.Seconds >= 0 ? l.Seconds.ToString("0.0") + "秒" : "");
            if (l.Type == "ERROR" || l.Type == "BLOCKED") it.ForeColor = Ui.Err;
            else if (l.Type == "CONFLICT" || l.Type == "HOLD") it.ForeColor = Ui.Warn;
            else if (l.Type == "DELETE") it.ForeColor = Color.FromArgb(91, 47, 166);
            else if (l.Type == "COPY" || l.Type == "OVERWRITE" || l.Type == "RENAME") it.ForeColor = Ui.Ok;
            e.Item = it;
        }

        /// <summary>今の絞り込み（期間・ジョブ・PC）で、PCごとのコピー容量を集計して表示する</summary>
        private void ShowStats()
        {
            List<LogLine> target = all.FindAll(delegate(LogLine l)
            {
                return cbJob.SelectedIndex <= 0 || string.Equals(l.Job, (string)cbJob.SelectedItem, StringComparison.OrdinalIgnoreCase);
            });
            string range = dFrom.Value.ToString("yyyy/MM/dd") + "〜" + dTo.Value.ToString("yyyy/MM/dd")
                + (cbJob.SelectedIndex > 0 ? "・ジョブ：" + cbJob.SelectedItem : "")
                + (cbPc.SelectedIndex == 1 ? "・すべてのPC" : "・このPC");
            using (StatsForm f = new StatsForm(target, range)) f.ShowDialog(this);
        }

        private void ExportCsv()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV（Excel で開けます）|*.csv";
                d.FileName = "自動コピー_ログ_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("日時,PC名,ユーザー,フォルダ名,ジョブ,種類,内容,サイズ(バイト),時間(秒)");
                foreach (LogLine l in shown)
                {
                    sb.AppendLine(string.Join(",", new string[] {
                        Csv(l.Time.ToString("yyyy/MM/dd HH:mm:ss")), Csv(l.Pc), Csv(l.User), Csv(l.Folder), Csv(l.Job), Csv(l.Type), Csv(l.Detail),
                        l.Size >= 0 ? l.Size.ToString() : "", l.Seconds >= 0 ? l.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "" }));
                }
                // Excel が文字化けしないよう BOM 付き UTF-8
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                Ui.Info(this, "書き出しました：" + d.FileName);
            }
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new char[] { ',', '"', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private void OpenLogFolder()
        {
            if (cbPc.SelectedIndex == 1 && !string.IsNullOrEmpty(AppCore.Settings.LogRoot)) Ui.OpenFolder(AppCore.Settings.LogRoot);
            else Ui.OpenFolder(AppPaths.LogDir);
        }
    }

    /// <summary>PCごとのコピー容量の集計</summary>
    public class StatsForm : Form
    {
        private readonly List<LogLine> lines;
        private readonly ListView lv;
        private readonly CheckBox byJob;
        private readonly TabControl tabs;
        private readonly PcVolumeChart pcChart = new PcVolumeChart();
        private readonly DailyVolumeChart dailyChart = new DailyVolumeChart();
        private readonly ChartHost pcHost;
        private readonly UsageView places = new UsageView(true);
        private readonly ShareChart extChart = new ShareChart();
        private readonly ChartHost extHost;
        private readonly ListView big;
        private readonly UsageTree usage;
        private List<CopyStat> stats = new List<CopyStat>();

        public StatsForm(List<LogLine> lines, string range)
        {
            this.lines = lines;
            Ui.SetupForm(this, "コピー容量の集計", 1060, 640);
            byJob = Ui.C("ジョブごとに分ける", false);
            byJob.CheckedChanged += delegate { Fill(); };
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("コピー容量の集計"),
                Ui.Hint("期間・条件：" + range + "　（新規コピーと上書きのファイルサイズの合計。名前変更はコピーしないため含みません）"),
                byJob);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12, 8, 12, 4);

            lv = Ui.List("フォルダ名|130", "PC名|120", "ジョブ|130", "コピー容量|100", "うち新規|90", "うち上書き|90", "ファイル数|80", "名前変更|70", "削除|60", "エラー|60", "実行回数|70", "最後の記録|130");
            lv.Dock = DockStyle.Fill;
            foreach (int i in new int[] { 3, 4, 5, 6, 7, 8, 9, 10 }) lv.Columns[i].TextAlign = HorizontalAlignment.Right;

            // 表・PCごと・日ごと・場所別の割合・種類別の割合・大きいファイル
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(12, 5);
            TabPage pTable = new TabPage("表");
            pTable.Controls.Add(lv);
            TabPage pPc = new TabPage("PCごとのグラフ");
            pcHost = new ChartHost(pcChart);
            pPc.Controls.Add(pcHost);
            TabPage pDay = new TabPage("日ごとのグラフ");
            dailyChart.Dock = DockStyle.Fill;
            pDay.Controls.Add(dailyChart);
            TabPage pPlace = new TabPage("場所別の割合");
            pPlace.Controls.Add(places);
            TabPage pExt = new TabPage("種類別の割合");
            extHost = new ChartHost(extChart);
            pExt.Controls.Add(extHost);
            TabPage pBig = new TabPage("大きいファイル");
            big = UsageView.BigList(true);
            pBig.Controls.Add(big);
            tabs.TabPages.AddRange(new TabPage[] { pTable, pPc, pDay, pPlace, pExt, pBig });
            Button saveImg = Ui.B("グラフを画像で保存", delegate { SaveImage(); });
            saveImg.Enabled = false;
            tabs.SelectedIndexChanged += delegate
            {
                saveImg.Enabled = tabs.SelectedIndex >= 1 && tabs.SelectedIndex <= 4;
                byJob.Enabled = tabs.SelectedIndex == 0;
            };

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Ui.B("CSVに書き出す", delegate { Export(); }), saveImg));
            pcChart.SetData(LogStats.Aggregate(lines, false));
            pcHost.Fit();
            dailyChart.SetData(LogStats.Daily(lines));
            usage = LogStats.CopyUsage(lines);
            places.SetTree(usage);
            extChart.SetData(UsageItems.ExtItems(usage), usage.Total, "この期間のコピーはありません");
            extHost.Fit();
            UsageView.FillBig(big, usage, true);
            Fill();
        }

        public UsageView Places { get { return places; } }

        private void Fill()
        {
            stats = LogStats.Aggregate(lines, byJob.Checked);
            lv.Columns[2].Width = byJob.Checked ? 130 : 0;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (CopyStat s in stats)
            {
                bool isTotal = s.Folder == "合計" && s.Pc == null;
                ListViewItem it = new ListViewItem(s.Folder ?? "");
                it.SubItems.Add(s.Pc ?? "");
                it.SubItems.Add(s.Job ?? "");
                it.SubItems.Add(Fmt.Size(s.TotalBytes));
                it.SubItems.Add(Fmt.Size(s.CopiedBytes));
                it.SubItems.Add(Fmt.Size(s.OverwrittenBytes));
                it.SubItems.Add(Fmt.Count(s.TotalFiles));
                it.SubItems.Add(Fmt.Count(s.Renamed));
                it.SubItems.Add(Fmt.Count(s.Deleted));
                it.SubItems.Add(Fmt.Count(s.Errors));
                it.SubItems.Add(Fmt.Count(s.Runs));
                it.SubItems.Add(s.Last == DateTime.MinValue ? "" : s.Last.ToString("yyyy/MM/dd HH:mm"));
                if (isTotal) { it.Font = Ui.Bold; it.BackColor = Ui.InfoBack; }
                if (s.Errors > 0 && !isTotal) it.ForeColor = Ui.Err;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }

        /// <summary>表示中のタブを開く（確認用）</summary>
        public void ShowTab(int index) { tabs.SelectedIndex = index; }

        private void SaveImage()
        {
            switch (tabs.SelectedIndex)
            {
                case 1: UsageView.SaveImage(this, pcChart, "PCごとの容量"); break;
                case 2: UsageView.SaveImage(this, dailyChart, "日ごとの容量"); break;
                case 3: UsageView.SaveImage(this, places.Chart, "場所別の割合"); break;
                case 4: UsageView.SaveImage(this, extChart, "種類別の割合"); break;
            }
        }

        private void Export()
        {
            if (tabs.SelectedIndex == 5)
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Filter = "CSV（Excel で開けます）|*.csv";
                    d.FileName = "自動コピー_コピーした大きいファイル_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(d.FileName, UsageView.BigCsv(usage, true), new UTF8Encoding(true));
                    Ui.Info(this, "書き出しました：" + d.FileName);
                }
                return;
            }
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV（Excel で開けます）|*.csv";
                d.FileName = "自動コピー_PCごとの容量_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("フォルダ名,PC名,ジョブ,コピー容量(バイト),新規(バイト),上書き(バイト),ファイル数,新規件数,上書き件数,名前変更,削除,エラー,実行回数,最初の記録,最後の記録");
                foreach (CopyStat s in stats)
                {
                    sb.AppendLine(string.Join(",", new string[] {
                        Q(s.Folder), Q(s.Pc), Q(s.Job), s.TotalBytes.ToString(), s.CopiedBytes.ToString(), s.OverwrittenBytes.ToString(),
                        s.TotalFiles.ToString(), s.Copied.ToString(), s.Overwritten.ToString(), s.Renamed.ToString(), s.Deleted.ToString(),
                        s.Errors.ToString(), s.Runs.ToString(),
                        s.First == DateTime.MaxValue ? "" : s.First.ToString("yyyy/MM/dd HH:mm:ss"),
                        s.Last == DateTime.MinValue ? "" : s.Last.ToString("yyyy/MM/dd HH:mm:ss") }));
                }
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                Ui.Info(this, "書き出しました：" + d.FileName);
            }
        }

        private static string Q(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new char[] { ',', '"', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
