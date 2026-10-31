using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>フォルダ別の割合（フォルダをクリックすると中に入る）</summary>
    public class UsageView : Panel
    {
                private readonly ShareChart chart = new ShareChart();
        private readonly ChartHost host;
        private readonly Button up;
        private readonly Label where;
        private UsageTree tree;
        private UsageNode node;
        private readonly bool logMode;

        public UsageView(bool logMode)
        {
            this.logMode = logMode;
            Dock = DockStyle.Fill;
            BackColor = ChartBase.Surface;
            host = new ChartHost(chart);
            up = Ui.B("↑ 上のフォルダへ", delegate { if (node != null && node.Parent != null) Show(node.Parent); });
            up.Enabled = false;
            where = Ui.L("");
            where.Font = Ui.Bold;
            where.Margin = new Padding(8, 8, 8, 0);
            FlowLayoutPanel bar = Ui.Row(up, where);
            bar.Dock = DockStyle.Top;
            bar.Padding = new Padding(8, 6, 8, 0);
            bar.BackColor = ChartBase.Surface;
            chart.ItemClicked += delegate(object o) { UsageNode n = o as UsageNode; if (n != null) Show(n); };
            Controls.Add(host);
            Controls.Add(bar);
        }

        public ChartBase Chart { get { return chart; } }
        public UsageNode Current { get { return node; } }

        public void SetTree(UsageTree t)
        {
            tree = t;
            Show(t == null ? null : t.Root);
        }

        /// <summary>表示名のパスでフォルダを開く（確認用）</summary>
        public void Open(string path)
        {
            if (tree == null) return;
            UsageNode n = tree.Find(path);
            if (n != null) Show(n);
        }

        private void Show(UsageNode n)
        {
            node = n;
            up.Enabled = n != null && n.Parent != null;
            if (n == null) { where.Text = ""; chart.SetData(null, 0, null); host.Fit(); return; }
            where.Text = (n.Parent == null ? n.Name : tree.Root.Name + "\\" + n.Path) + "　" + Fmt.Size(n.Size) + "・" + Fmt.Count(n.Files) + "ファイル"
                + (n.Parent == null ? "" : "（全体の " + Pct(n.Size, tree.Total) + "）");
            chart.SetData(UsageItems.Items(tree, n, logMode), n.Size, logMode ? "この期間のコピーはありません" : "ファイルがありません");
            host.Fit();
            host.AutoScrollPosition = new Point(0, 0);
        }

        private static string Pct(long v, long t) { return UsageItems.Pct(v, t); }

        /// <summary>大きいファイルの一覧（ListView）を作る</summary>
        public static ListView BigList(bool logMode)
        {
            ListView lv = logMode
                ? Ui.List("順位|46", "ファイル名|220", "場所（PC＼ジョブ＼フォルダ）|330", "コピー容量|90", "割合|60", "回数|50", "1回のサイズ|90", "最後のコピー|120")
                : Ui.List("順位|46", "ファイル名|260", "場所（調べたフォルダの中）|400", "サイズ|90", "割合|60", "更新日時|130");
            lv.Dock = DockStyle.Fill;
            foreach (int i in logMode ? new int[] { 0, 3, 4, 5, 6 } : new int[] { 0, 3, 4 }) lv.Columns[i].TextAlign = HorizontalAlignment.Right;
            return lv;
        }

        public static void FillBig(ListView lv, UsageTree t, bool logMode)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            if (t != null)
            {
                int rank = 0;
                foreach (UsageFile f in t.Biggest)
                {
                    rank++;
                    ListViewItem it = new ListViewItem(rank.ToString());
                    it.SubItems.Add(f.Name);
                    it.SubItems.Add(f.Dir ?? "");
                    it.SubItems.Add(Fmt.Size(f.Size));
                    it.SubItems.Add(Pct(f.Size, t.Total));
                    if (logMode)
                    {
                        it.SubItems.Add(Fmt.Count(f.Count));
                        it.SubItems.Add(Fmt.Size(f.LastSize));
                        it.SubItems.Add(f.Time.ToString("yyyy/MM/dd HH:mm"));
                    }
                    else it.SubItems.Add(f.Time.ToString("yyyy/MM/dd HH:mm"));
                    it.Tag = f;
                    lv.Items.Add(it);
                }
            }
            lv.EndUpdate();
        }

        public static string BigCsv(UsageTree t, bool logMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(logMode ? "順位,ファイル名,場所,コピー容量(バイト),割合(%),回数,1回のサイズ(バイト),最後のコピー"
                                  : "順位,ファイル名,場所,サイズ(バイト),割合(%),更新日時,フルパス");
            int rank = 0;
            foreach (UsageFile f in t.Biggest)
            {
                rank++;
                string pct = t.Total > 0 ? (f.Size * 100.0 / t.Total).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "";
                if (logMode)
                    sb.AppendLine(string.Join(",", new string[] { rank.ToString(), Q(f.Name), Q(f.Dir), f.Size.ToString(), pct, f.Count.ToString(), f.LastSize.ToString(), f.Time.ToString("yyyy/MM/dd HH:mm:ss") }));
                else
                    sb.AppendLine(string.Join(",", new string[] { rank.ToString(), Q(f.Name), Q(f.Dir), f.Size.ToString(), pct, f.Time.ToString("yyyy/MM/dd HH:mm:ss"), Q(f.FullPath) }));
            }
            return sb.ToString();
        }

        public static string Q(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new char[] { ',', '"', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        /// <summary>エクスプローラーでファイルを選んだ状態で開く</summary>
        public static void Reveal(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return;
            try { Process.Start("explorer.exe", "/select,\"" + PathUtil.Plain(fullPath) + "\""); }
            catch (Exception) { }
        }

        public static void SaveImage(IWin32Window owner, Control c, string name)
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "PNG 画像|*.png";
                d.FileName = "自動コピー_" + name + "_" + DateTime.Now.ToString("yyyyMMdd") + ".png";
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                int w = Math.Max(1, c.Width), h = Math.Max(1, c.Height);
                using (Bitmap bmp = new Bitmap(w, h))
                {
                    c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
                }
                Ui.Info(owner, "保存しました：" + d.FileName);
            }
        }
    }

    /// <summary>
    /// 容量の内訳：コピー先（またはコピー元）のフォルダを調べ、どこの何が容量を使っているかを表示する。
    /// 読むだけで、ファイルの変更・削除は一切しない。
    /// </summary>
    public class UsageForm : Form
    {
        private readonly ComboBox cbJob;
        private readonly RadioButton rDest, rSrc;
        private readonly Label summary;
        private readonly TabControl tabs;
        private readonly UsageView folders = new UsageView(false);
        private readonly ShareChart extChart = new ShareChart();
        private readonly ChartHost extHost;
        private readonly ListView big;
        private readonly Button saveImg, csv;
        private UsageTree tree;
        private readonly List<JobConfig> jobs = new List<JobConfig>();

        public UsageForm(string jobId)
        {
            Ui.SetupForm(this, "容量の内訳", 1040, 680);
            cbJob = Ui.Combo(220);
            foreach (JobConfig j in AppCore.Settings.Jobs) { jobs.Add(j); cbJob.Items.Add(j.Name); }
            int sel = jobs.FindIndex(delegate(JobConfig j) { return j.Id == jobId; });
            if (cbJob.Items.Count > 0) cbJob.SelectedIndex = Math.Max(0, sel);
            rDest = Ui.R("コピー先", true);
            rSrc = Ui.R("コピー元", false);
            summary = Ui.Hint("ジョブと対象を選んで「調べる」を押してください。フォルダの中を読むだけで、ファイルは変更しません。");

            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("容量の内訳"),
                Ui.Row(Ui.L("ジョブ"), cbJob, Ui.L("　対象"), rDest, rSrc, Ui.B("調べる", delegate { Analyze(); })),
                summary);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12, 8, 12, 4);

            extHost = new ChartHost(extChart);
            big = UsageView.BigList(false);
            big.DoubleClick += delegate
            {
                if (big.SelectedItems.Count == 0) return;
                UsageFile f = big.SelectedItems[0].Tag as UsageFile;
                if (f != null) UsageView.Reveal(f.FullPath);
            };

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(14, 5);
            TabPage p1 = new TabPage("フォルダ別の割合"); p1.Controls.Add(folders);
            TabPage p2 = new TabPage("種類別の割合"); p2.Controls.Add(extHost);
            TabPage p3 = new TabPage("大きいファイル"); p3.Controls.Add(big);
            tabs.TabPages.Add(p1); tabs.TabPages.Add(p2); tabs.TabPages.Add(p3);

            saveImg = Ui.B("グラフを画像で保存", delegate
            {
                if (tabs.SelectedIndex == 0) UsageView.SaveImage(this, folders.Chart, "容量の内訳_フォルダ別");
                else UsageView.SaveImage(this, extChart, "容量の内訳_種類別");
            });
            csv = Ui.B("一覧をCSVに書き出す", delegate { ExportCsv(); });
            Button open = Ui.B("場所を開く", delegate { OpenSelected(); });
            tabs.SelectedIndexChanged += delegate { UpdateButtons(); };

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), csv, saveImg, open));
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = tree != null;
            saveImg.Enabled = has && tabs.SelectedIndex <= 1;
            csv.Enabled = has;
        }

        private JobConfig Job { get { return cbJob.SelectedIndex >= 0 ? jobs[cbJob.SelectedIndex] : null; } }

        public void ShowTab(int index) { tabs.SelectedIndex = index; }
        public UsageView Folders { get { return folders; } }

        private string Root(JobConfig j)
        {
            return rSrc.Checked ? CopyEngine.ResolvedSource(j, AppCore.Settings) : CopyEngine.ResolvedDest(j, AppCore.Settings);
        }

        /// <summary>選んだフォルダを調べる（待ち表示付き）</summary>
        private void Analyze()
        {
            JobConfig j = Job;
            if (j == null) return;
            string root = Root(j);
            UsageTree t;
            bool stopped;
            try
            {
                t = Ui.BusyCancel<UsageTree>(this, "フォルダを調べています…（ファイルが多いと時間がかかります。「中止」でいつでも止められます）",
                    delegate(CancellationToken ct, BusyProgress p)
                    {
                        p.Text = "接続しています…";
                        NetAccess.Ensure(root, Math.Min(1, j.RetryCount), j.RetryIntervalSeconds, ct, null);
                        return DiskUsage.Scan(root, ct, delegate(int files, long bytes, string dir)
                        {
                            p.Text = Fmt.Count(files) + "ファイル・" + Fmt.Size(bytes) + "　" + (dir.Length == 0 ? "" : dir);
                        });
                    }, out stopped);
            }
            catch (Exception ex) { Ui.Error(this, "調べられませんでした：" + ex.Message); return; }
            if (stopped)
            {
                // 途中までの結果は不完全なので出さない（前回の結果はそのまま）
                summary.Text = "中止しました。" + (tree != null ? "下の表示は前回調べた結果です。" : "もう一度調べるには「調べる」を押してください。");
                summary.ForeColor = Ui.Warn;
                return;
            }
            SetTree(t, j, rSrc.Checked);
        }

        /// <summary>待ち表示なしで調べる（画面確認用）</summary>
        public void AnalyzeNow()
        {
            JobConfig j = Job;
            if (j == null) return;
            SetTree(DiskUsage.Scan(Root(j), CancellationToken.None), j, rSrc.Checked);
        }

        private void SetTree(UsageTree t, JobConfig j, bool src)
        {
            tree = t;
            summary.Text = j.Name + "の" + (src ? "コピー元" : "コピー先") + "：" + t.Root.Name + "　合計 " + Fmt.Size(t.Total) + "・" + Fmt.Count(t.FileCount) + "ファイル"
                + "（" + t.Checked.ToString("yyyy/MM/dd HH:mm") + " 時点）"
                + (t.Errors.Count > 0 ? "　※読めなかったフォルダ " + Fmt.Count(t.Errors.Count) + "件" : "");
            summary.ForeColor = t.Errors.Count > 0 ? Ui.Warn : Ui.Quiet;
            folders.SetTree(t);
            extChart.SetData(UsageItems.ExtItems(t), t.Total, "ファイルがありません");
            extHost.Fit();
            UsageView.FillBig(big, t, false);
            UpdateButtons();
        }

        private void OpenSelected()
        {
            if (tabs.SelectedIndex == 2 && big.SelectedItems.Count > 0)
            {
                UsageFile f = big.SelectedItems[0].Tag as UsageFile;
                if (f != null) { UsageView.Reveal(f.FullPath); return; }
            }
            if (tree == null) { JobConfig j = Job; if (j != null) Ui.OpenFolder(Root(j)); return; }
            UsageNode n = folders.Current;
            string p = n == null || n.Parent == null ? tree.Root.Name : tree.Root.Name + "\\" + n.Path;
            Ui.OpenFolder(p);
        }

        private void ExportCsv()
        {
            if (tree == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV（Excel で開けます）|*.csv";
                d.FileName = "自動コピー_大きいファイル_" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, UsageView.BigCsv(tree, false), new UTF8Encoding(true));
                Ui.Info(this, "書き出しました：" + d.FileName);
            }
        }
    }
}
