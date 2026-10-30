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
                Ui.L("PC"), cbPc, Ui.L("期間"), dFrom, Ui.L("〜"), dTo, Ui.B("表示", delegate { LoadLogs(); }),
                Ui.L("ジョブ"), cbJob, Ui.L("種類"), cbType, Ui.L("検索"), tSearch,
                Ui.B("CSVに書き出す", delegate { ExportCsv(); }),
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
}
