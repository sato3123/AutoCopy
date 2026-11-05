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
        private readonly ComboBox cbPc, cbRange, cbDevice, cbJob, cbType;
        private readonly DateTimePicker dFrom, dTo;
        private readonly TextBox tSearch;
        private readonly AcListView lv, pcs;
        private readonly Label status;
        private List<LogLine> all = new List<LogLine>();
        private List<LogLine> shown = new List<LogLine>();
        private DateTime loadedFrom = DateTime.MaxValue, loadedTo = DateTime.MinValue;
        private bool loadedAllPcs;
        private bool quiet;
        private readonly string presetJob;

        public LogForm() : this(null, null) { }

        /// <summary>job・from を渡すと、そのジョブ・その時刻からのログに絞って開く（お知らせから開くとき）</summary>
        public LogForm(string jobName, DateTime? from)
        {
            presetJob = jobName;
            Ui.SetupForm(this, "ログ", 1100, 640);
            cbPc = Ui.Combo(150, "このPC", "すべてのPC（ログ保管先）");
            cbRange = Ui.Combo(120, "今日", "直近1時間", "直近24時間", "直近7日", "直近30日", "期間を指定");
            dFrom = TimePicker(); dTo = TimePicker();
            dFrom.Value = DateTime.Today; dTo.Value = DateTime.Today.AddDays(1).AddMinutes(-1);
            cbDevice = Ui.Combo(200, "すべて");
            cbJob = Ui.Combo(160, "すべて");
            foreach (JobConfig j in AppCore.Settings.Jobs) cbJob.Items.Add(j.Name);
            cbType = Ui.Combo(130, "すべて", "エラーのみ", "コピー・上書き", "名前変更", "削除・復元", "確認待ち", "集計");
            tSearch = Ui.T("", 140);
            cbDevice.SelectedIndexChanged += delegate { if (!quiet) ApplyFilter(); };
            cbJob.SelectedIndexChanged += delegate { if (!quiet) ApplyFilter(); };
            cbType.SelectedIndexChanged += delegate { if (!quiet) ApplyFilter(); };
            tSearch.TextChanged += delegate { if (!quiet) ApplyFilter(); };
            cbRange.SelectedIndexChanged += delegate { if (!quiet) RangeChosen(); };
            dFrom.ValueChanged += delegate { if (!quiet) { quiet = true; cbRange.SelectedIndex = 5; quiet = false; TimeChanged(); } };
            dTo.ValueChanged += delegate { if (!quiet) { quiet = true; cbRange.SelectedIndex = 5; quiet = false; TimeChanged(); } };
            cbPc.SelectedIndexChanged += delegate { if (!quiet) LoadLogs(); };

            FlowLayoutPanel row1 = Ui.WrapRow(
                Ui.Row(Ui.L("読み込むログ"), cbPc), Ui.Row(Ui.L("期間"), cbRange, dFrom, Ui.L("〜"), dTo),
                Ui.B("再読み込み", delegate { LoadLogs(); }),
                Ui.B("CSVに書き出す", delegate { ExportCsv(); }),
                Ui.B("コピー容量の集計", delegate { ShowStats(); }),
                Ui.B("ログフォルダを開く", delegate { OpenLogFolder(); }));
            FlowLayoutPanel row2 = Ui.WrapRow(
                Ui.Row(Ui.L("設備（PC）"), cbDevice), Ui.Row(Ui.L("ジョブ"), cbJob), Ui.Row(Ui.L("種類"), cbType), Ui.Row(Ui.L("検索"), tSearch),
                Ui.B("絞り込みを解除", delegate { ClearFilter(); }));
            FlowLayoutPanel top = Ui.Column(row1, row2);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(8, 6, 8, 2);
            top.BackColor = Color.White;
            Resize += delegate { FitRows(row1, row2); };
            HandleCreated += delegate { FitRows(row1, row2); };

            pcs = (AcListView)Ui.List("フォルダ名|140", "PC名|120", "最終更新|130", "ジョブ|160", "結果|80", "内容|440");
            pcs.Dock = DockStyle.Top;
            pcs.Height = Ui.S(120);
            pcs.Visible = false;
            // PCの一覧の行をクリックすると、その設備・ジョブのログに絞り込む
            pcs.ItemActivate += delegate { FilterFromStatus(); };
            pcs.Click += delegate { FilterFromStatus(); };

            lv = (AcListView)Ui.List("日時|140", "PC（フォルダ名）|140", "ジョブ|130", "種類|170", "内容|440", "サイズ|80", "時間|60");
            lv.Dock = DockStyle.Fill;
            lv.VirtualMode = true;
            lv.RetrieveVirtualItem += OnRetrieve;
            lv.SortRequested += delegate(int col, bool asc) { SortShown(col, asc); };
            lv.SetSort(0, false);   // 新しい順

            status = Ui.L("");
            status.Dock = DockStyle.Bottom;
            status.AutoSize = false;
            status.Height = Ui.S(24);
            status.Padding = new Padding(8, 4, 8, 0);
            status.BackColor = Color.FromArgb(243, 243, 243);

            Controls.Add(lv);
            Controls.Add(pcs);
            Controls.Add(top);
            Controls.Add(status);

            quiet = true;
            if (from.HasValue)
            {
                cbRange.SelectedIndex = 5;
                dFrom.Value = from.Value.AddMinutes(-5);
                dTo.Value = DateTime.Now.Date.AddDays(1).AddMinutes(-1);
            }
            quiet = false;
            Shown += delegate { LoadLogs(); };
        }

        private static DateTimePicker TimePicker()
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = "yyyy/MM/dd HH:mm";
            d.Width = Math.Max(Ui.S(140), Ui.TextWidth("0000/00/00 00:00", Ui.Base) + Ui.S(40));
            return d;
        }

        private void FitRows(params FlowLayoutPanel[] rows)
        {
            foreach (FlowLayoutPanel r in rows) r.MaximumSize = new Size(Math.Max(Ui.S(300), ClientSize.Width - Ui.S(20)), 0);
        }

        private void RangeChosen()
        {
            DateTime now = DateTime.Now;
            DateTime f, t = now.Date.AddDays(1).AddMinutes(-1);
            switch (cbRange.SelectedIndex)
            {
                case 1: f = now.AddHours(-1); break;
                case 2: f = now.AddHours(-24); break;
                case 3: f = now.Date.AddDays(-6); break;
                case 4: f = now.Date.AddDays(-29); break;
                case 5: return;
                default: f = now.Date; break;
            }
            quiet = true;
            dFrom.Value = f; dTo.Value = t;
            quiet = false;
            TimeChanged();
        }

        /// <summary>期間が読み込み済みの範囲の中なら絞り込むだけ、外なら読み込み直す</summary>
        private void TimeChanged()
        {
            DateTime f = dFrom.Value, t = dTo.Value;
            if (t < f) { DateTime x = f; f = t; t = x; }
            if (f.Date >= loadedFrom && t.Date <= loadedTo && loadedAllPcs == (cbPc.SelectedIndex == 1)) ApplyFilter();
            else LoadLogs();
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
                loadedFrom = from; loadedTo = to; loadedAllPcs = allPcs;
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); return; }

            pcs.Visible = allPcs && statuses != null && statuses.Count > 0;
            if (pcs.Visible)
            {
                pcs.BeginUpdate();
                pcs.Items.Clear();
                foreach (PcStatus st in statuses)
                {
                    DateTime up;
                    object upKey = DateTime.TryParse(st.Updated ?? "", out up) ? (object)up : null;
                    if (st.Jobs == null || st.Jobs.Count == 0)
                    {
                        ListViewItem it = new ListViewItem(st.FolderName ?? "");
                        it.SubItems.Add(st.ComputerName ?? ""); it.SubItems.Add(st.Updated ?? ""); it.SubItems.Add(""); it.SubItems.Add(""); it.SubItems.Add("");
                        it.SubItems[2].Tag = upKey;
                        it.Tag = new string[] { Logger.PcLabel(st.FolderName, st.ComputerName), null };
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
                        it.SubItems[2].Tag = upKey;
                        it.Tag = new string[] { Logger.PcLabel(st.FolderName, st.ComputerName), j.Name };
                        if (j.Result == "error") it.ForeColor = Ui.Err;
                        pcs.Items.Add(it);
                    }
                }
                pcs.Resort();
                pcs.EndUpdate();
            }
            FillChoices();
            ApplyFilter();
            if (note != null) status.Text = note + "　" + status.Text;
        }

        private static string DeviceOf(LogLine l) { return Logger.PcLabel(l.Folder, l.Pc); }

        /// <summary>読み込んだログに出てくる設備（PC）とジョブを、絞り込みの選択肢にする</summary>
        private void FillChoices()
        {
            quiet = true;
            try
            {
                string dev = cbDevice.SelectedIndex > 0 ? (string)cbDevice.SelectedItem : null;
                string job = cbJob.SelectedIndex > 0 ? (string)cbJob.SelectedItem : presetJob;
                SortedDictionary<string, int> devs = new SortedDictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
                SortedDictionary<string, int> jobs = new SortedDictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
                foreach (JobConfig j in AppCore.Settings.Jobs) jobs[j.Name] = 0;
                foreach (LogLine l in all)
                {
                    string d = DeviceOf(l);
                    if (d.Length > 0) { int c; devs.TryGetValue(d, out c); devs[d] = c + 1; }
                    if (!string.IsNullOrEmpty(l.Job)) { int c; jobs.TryGetValue(l.Job, out c); jobs[l.Job] = c + 1; }
                }
                cbDevice.Items.Clear(); cbDevice.Items.Add("すべて");
                foreach (string d in devs.Keys) cbDevice.Items.Add(d);
                cbJob.Items.Clear(); cbJob.Items.Add("すべて");
                foreach (string j in jobs.Keys) cbJob.Items.Add(j);
                cbDevice.SelectedIndex = Math.Max(0, dev != null ? cbDevice.Items.IndexOf(dev) : 0);
                int ji = job != null ? IndexOfIgnoreCase(cbJob, job) : 0;
                cbJob.SelectedIndex = Math.Max(0, ji);
                Ui.FitCombo(cbDevice); Ui.FitCombo(cbJob);
            }
            finally { quiet = false; }
        }

        private static int IndexOfIgnoreCase(ComboBox c, string s)
        {
            for (int i = 0; i < c.Items.Count; i++) if (string.Equals((string)c.Items[i], s, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void FilterFromStatus()
        {
            if (pcs.SelectedItems.Count == 0) return;
            string[] t = pcs.SelectedItems[0].Tag as string[];
            if (t == null) return;
            quiet = true;
            int di = IndexOfIgnoreCase(cbDevice, t[0]);
            cbDevice.SelectedIndex = Math.Max(0, di);
            cbJob.SelectedIndex = t[1] == null ? 0 : Math.Max(0, IndexOfIgnoreCase(cbJob, t[1]));
            quiet = false;
            ApplyFilter();
        }

        private void ClearFilter()
        {
            quiet = true;
            cbDevice.SelectedIndex = 0; cbJob.SelectedIndex = 0; cbType.SelectedIndex = 0; tSearch.Text = "";
            quiet = false;
            ApplyFilter();
        }

        private bool Match(LogLine l)
        {
            DateTime f = dFrom.Value, t = dTo.Value;
            if (t < f) { DateTime x = f; f = t; t = x; }
            f = new DateTime(f.Year, f.Month, f.Day, f.Hour, f.Minute, 0);
            t = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0).AddMinutes(1);
            if (l.Time < f || l.Time >= t) return false;
            if (cbDevice.SelectedIndex > 0 && !string.Equals(DeviceOf(l), (string)cbDevice.SelectedItem, StringComparison.OrdinalIgnoreCase)) return false;
            if (cbJob.SelectedIndex > 0 && !string.Equals(l.Job, (string)cbJob.SelectedItem, StringComparison.OrdinalIgnoreCase)) return false;
            switch (cbType.SelectedIndex)
            {
                case 1: if (l.Type != "ERROR" && l.Type != "BLOCKED") return false; break;
                case 2: if (l.Type != "COPY" && l.Type != "OVERWRITE") return false; break;
                case 3: if (l.Type != "RENAME") return false; break;
                case 4: if (l.Type != "DELETE" && l.Type != "RESTORE" && l.Type != "PURGE" && l.Type != "SYNCDEL") return false; break;
                case 5: if (l.Type != "CONFLICT" && l.Type != "HOLD") return false; break;
                case 6: if (l.Type != "SUMMARY" && l.Type != "DUPCHECK") return false; break;
            }
            string q = tSearch.Text.Trim();
            if (q.Length > 0 && (l.Detail ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                && Logger.TypeLabel(l.Type).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void ApplyFilter()
        {
            shown = all.FindAll(delegate(LogLine l) { return Match(l); });
            SortShown(lv.SortColumn, lv.SortAscending);
            lv.VirtualListSize = 0;
            lv.VirtualListSize = shown.Count;
            lv.Invalidate();
            string cond = "";
            if (cbDevice.SelectedIndex > 0) cond += "　設備：" + cbDevice.SelectedItem;
            if (cbJob.SelectedIndex > 0) cond += "　ジョブ：" + cbJob.SelectedItem;
            status.Text = Fmt.Count(shown.Count) + "件を表示（読み込んだ " + Fmt.Count(all.Count) + "件のうち）"
                + "　" + dFrom.Value.ToString("yyyy/MM/dd HH:mm") + "〜" + dTo.Value.ToString("yyyy/MM/dd HH:mm") + cond;
        }

        /// <summary>見出しのクリックで並べ替え（仮想表示なので、元の一覧を並べ替える）</summary>
        private void SortShown(int col, bool asc)
        {
            if (col < 0) return;
            Comparison<LogLine> cmp;
            switch (col)
            {
                case 0: cmp = delegate(LogLine a, LogLine b) { return a.Time.CompareTo(b.Time); }; break;
                case 1: cmp = delegate(LogLine a, LogLine b) { return string.Compare(DeviceOf(a), DeviceOf(b), StringComparison.CurrentCultureIgnoreCase); }; break;
                case 2: cmp = delegate(LogLine a, LogLine b) { return string.Compare(a.Job, b.Job, StringComparison.CurrentCultureIgnoreCase); }; break;
                case 3: cmp = delegate(LogLine a, LogLine b) { return string.CompareOrdinal(Logger.TypeLabel(a.Type), Logger.TypeLabel(b.Type)); }; break;
                case 4: cmp = delegate(LogLine a, LogLine b) { return string.Compare(a.Detail, b.Detail, StringComparison.CurrentCultureIgnoreCase); }; break;
                case 5: cmp = delegate(LogLine a, LogLine b) { return a.Size.CompareTo(b.Size); }; break;
                default: cmp = delegate(LogLine a, LogLine b) { return a.Seconds.CompareTo(b.Seconds); }; break;
            }
            // 同じ値どうしは日時の順（安定した並び）
            List<KeyValuePair<int, LogLine>> idx = new List<KeyValuePair<int, LogLine>>(shown.Count);
            for (int i = 0; i < shown.Count; i++) idx.Add(new KeyValuePair<int, LogLine>(i, shown[i]));
            idx.Sort(delegate(KeyValuePair<int, LogLine> a, KeyValuePair<int, LogLine> b)
            {
                int r = cmp(a.Value, b.Value);
                if (r == 0) r = a.Value.Time.CompareTo(b.Value.Time);
                if (r == 0) r = a.Key.CompareTo(b.Key);
                return asc ? r : -r;
            });
            for (int i = 0; i < idx.Count; i++) shown[i] = idx[i].Value;
            lv.Invalidate();
        }

        private void OnRetrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            LogLine l = e.ItemIndex < shown.Count ? shown[e.ItemIndex] : null;
            if (l == null) { e.Item = new ListViewItem(new string[] { "", "", "", "", "", "", "" }); return; }
            ListViewItem it = new ListViewItem(l.Time.ToString("yyyy/MM/dd HH:mm:ss"));
            it.SubItems.Add((l.Folder ?? "") + (string.IsNullOrEmpty(l.Pc) ? "" : "（" + l.Pc + "）"));
            it.SubItems.Add(l.Job ?? "");
            it.SubItems.Add(Logger.TypeLabel(l.Type));
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
            int keepType = cbType.SelectedIndex; string keepQ = tSearch.Text;
            quiet = true; cbType.SelectedIndex = 0; tSearch.Text = "";
            List<LogLine> target = all.FindAll(delegate(LogLine l) { return Match(l); });
            cbType.SelectedIndex = keepType; tSearch.Text = keepQ; quiet = false;
            ApplyFilter();
            string range = dFrom.Value.ToString("yyyy/MM/dd HH:mm") + "〜" + dTo.Value.ToString("yyyy/MM/dd HH:mm")
                + (cbDevice.SelectedIndex > 0 ? "・設備：" + cbDevice.SelectedItem : "")
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
                sb.AppendLine("日時,PC名,ユーザー,フォルダ名,ジョブ,種類,種類（日本語）,内容,サイズ(バイト),時間(秒)");
                foreach (LogLine l in shown)
                {
                    sb.AppendLine(string.Join(",", new string[] {
                        Csv(l.Time.ToString("yyyy/MM/dd HH:mm:ss")), Csv(l.Pc), Csv(l.User), Csv(l.Folder), Csv(l.Job), Csv(l.Type), Csv(Logger.TypeJa(l.Type)), Csv(l.Detail),
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
