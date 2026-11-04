using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// 実行結果の予測：コピーを実際には行わずに、実行したら何がコピー・上書き・確認待ち・削除などになるかを一覧で見る。
    /// コピー先・コピー元・前回までの記録は一切変えない。
    /// </summary>
    public class PreviewForm : Form
    {
        private readonly JobConfig job;
        private readonly ListView lv, kinds;
        private readonly TextBox tSearch;
        private readonly Label summary;
        private readonly Button runNow;
        private RunResult result;
        private List<PlanItem> shown = new List<PlanItem>();
        private string kindFilter;   // null = すべて

        public PreviewForm(JobConfig job, bool allowRun)
        {
            this.job = job;
            Ui.SetupForm(this, "実行結果の予測 - " + job.Name, 1100, 660);

            summary = Ui.L("");
            Ui.FitToForm(summary, 1000);
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("実行したらどうなるか（コピーはしていません）"),
                Ui.Hint("ジョブ「" + job.Name + "」：" + CopyEngine.ResolvedSource(job, AppCore.Settings) + (job.Sync ? "  ⇄  " : "  →  ") + CopyEngine.ResolvedDest(job, AppCore.Settings)),
                summary);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), Ui.S(4));

            kinds = Ui.List("種類|200", "件数|70", "容量|90");
            kinds.Dock = DockStyle.Left;
            kinds.Width = Ui.S(380);
            kinds.Columns[1].TextAlign = HorizontalAlignment.Right;
            kinds.Columns[2].TextAlign = HorizontalAlignment.Right;
            kinds.SelectedIndexChanged += delegate
            {
                kindFilter = kinds.SelectedItems.Count > 0 ? kinds.SelectedItems[0].Tag as string : null;
                Fill();
            };

            tSearch = Ui.T("", 220);
            tSearch.TextChanged += delegate { Fill(); };
            FlowLayoutPanel bar = Ui.Column(Ui.Row(Ui.L("ファイル名で絞り込み"), tSearch), Ui.Hint("左の種類をクリックすると、その種類だけを表示します。"));
            bar.Dock = DockStyle.Top;
            bar.Padding = new Padding(Ui.S(8), Ui.S(4), Ui.S(8), Ui.S(2));

            lv = Ui.List("種類|130", "ファイル|330", "サイズ|80", "コピー元の更新日時|130", "コピー先のサイズ|90", "コピー先の更新日時|130", "説明|360");
            lv.Dock = DockStyle.Fill;
            lv.VirtualMode = true;
            lv.RetrieveVirtualItem += OnRetrieve;
            lv.Columns[2].TextAlign = HorizontalAlignment.Right;
            lv.Columns[4].TextAlign = HorizontalAlignment.Right;

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Controls.Add(lv);
            right.Controls.Add(bar);
            Splitter sp = new Splitter();
            sp.Dock = DockStyle.Left;

            runNow = Ui.B("この内容で今すぐ実行", delegate { RunNow(); });
            runNow.Visible = allowRun;
            Controls.Add(right);
            Controls.Add(sp);
            Controls.Add(kinds);
            Controls.Add(top);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), runNow,
                Ui.B("一覧をCSVに書き出す", delegate { Export(); }),
                Ui.B("もう一度調べる", delegate { Analyze(); })));
            Shown += delegate { if (result == null) Analyze(); };
        }

        /// <summary>調べた結果を直接渡す（画面の確認用）</summary>
        public void SetResult(RunResult r) { result = r; ShowResult(); }

        private void Analyze()
        {
            bool stopped;
            RunResult r;
            JobConfig j = job;
            try
            {
                r = Ui.BusyCancel<RunResult>(this, "実行したらどうなるかを調べています（コピーはしません）…", delegate(System.Threading.CancellationToken ct, BusyProgress bp)
                {
                    JobProgress p = new JobProgress();
                    System.Threading.Timer t = new System.Threading.Timer(delegate(object o)
                    {
                        JobProgress s = p.Snapshot();
                        bp.Text = s.Phase + (s.Scanned > 0 ? "（" + Fmt.Count(s.Scanned) + "件）" : "");
                    }, null, 200, 300);
                    try { return AppCore.DryRun(j, false, ct, p); }
                    finally { t.Dispose(); }
                }, out stopped);
            }
            catch (Exception ex) { Ui.Error(this, "調べられませんでした：" + ex.Message); return; }
            if (stopped) { summary.Text = "中止しました。"; return; }
            result = r;
            ShowResult();
        }

        private void ShowResult()
        {
            RunResult r = result;
            Dictionary<string, int> count = new Dictionary<string, int>();
            Dictionary<string, long> bytes = new Dictionary<string, long>();
            foreach (PlanItem p in r.Plan)
            {
                int c; count.TryGetValue(p.Kind, out c); count[p.Kind] = c + 1;
                long b; bytes.TryGetValue(p.Kind, out b);
                bytes[p.Kind] = b + Math.Max(0, p.Size >= 0 ? p.Size : p.DstSize);
            }
            kinds.BeginUpdate();
            kinds.Items.Clear();
            ListViewItem all = new ListViewItem("すべて");
            all.SubItems.Add(Fmt.Count(r.Plan.Count)); all.SubItems.Add("");
            all.Font = Ui.Bold;
            kinds.Items.Add(all);
            foreach (string k in PlanKind.All)
            {
                int c;
                if (!count.TryGetValue(k, out c)) continue;
                ListViewItem it = new ListViewItem(k);
                it.SubItems.Add(Fmt.Count(c));
                it.SubItems.Add(Fmt.Size(bytes[k]));
                it.Tag = k;
                it.ForeColor = KindColor(k);
                kinds.Items.Add(it);
            }
            ListViewItem un = new ListViewItem("変更なし（何もしない）");
            un.SubItems.Add(Fmt.Count(r.Unchanged)); un.SubItems.Add("");
            un.ForeColor = Ui.Quiet;
            un.Tag = "__none";
            kinds.Items.Add(un);
            kinds.EndUpdate();

            int copies = 0, ow = 0;
            count.TryGetValue(PlanKind.Copy, out copies);
            count.TryGetValue(PlanKind.Overwrite, out ow);
            summary.Text = "調べたファイル " + Fmt.Count(r.Scanned) + "件 → コピー " + Fmt.Count(copies) + "件・上書き " + Fmt.Count(ow) + "件（合わせて " + Fmt.Size(r.CopyBytesPlanned) + "）"
                + "・確認待ち " + Fmt.Count(r.Conflicts) + "件・名前変更 " + Fmt.Count(r.Renamed) + "件"
                + (job.Mirror ? "・ミラー削除 " + Fmt.Count(r.Deleted) + "件" : "")
                + (r.PlannedDedup > 0 ? "・重複で省略 " + Fmt.Count(r.PlannedDedup) + "件" : "")
                + (r.PlannedDownloads > 0 ? "・取り込み " + Fmt.Count(r.PlannedDownloads) + "件" : "")
                + "・除外 " + Fmt.Count(r.Excluded) + "件"
                + (r.Excluded > 5000 ? "（一覧は最初の5,000件）" : "")
                + "　" + DateTime.Now.ToString("HH:mm") + " 時点";
            summary.ForeColor = r.Conflicts > 0 || r.Deleted > 0 ? Ui.Warn : SystemColors.WindowText;
            Fill();
        }

        private static Color KindColor(string k)
        {
            if (k == PlanKind.Conflict || k == PlanKind.Hold) return Ui.Warn;
            if (k == PlanKind.Delete) return Ui.Err;
            if (k == PlanKind.Copy || k == PlanKind.Overwrite || k == PlanKind.Rename || k == PlanKind.SyncIn) return Ui.Ok;
            if (k == PlanKind.Excluded || k == PlanKind.Same || k == PlanKind.Deferred || k == PlanKind.Skip) return Ui.Quiet;
            return SystemColors.WindowText;
        }

        private void Fill()
        {
            if (result == null) return;
            string q = tSearch.Text.Trim();
            shown = result.Plan.FindAll(delegate(PlanItem p)
            {
                if (kindFilter == "__none") return false;
                if (kindFilter != null && p.Kind != kindFilter) return false;
                if (q.Length > 0 && (p.Rel ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (p.Note ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            });
            lv.VirtualListSize = 0;
            lv.VirtualListSize = shown.Count;
            lv.Invalidate();
            Text = "実行結果の予測 - " + job.Name + "（" + Fmt.Count(shown.Count) + "件を表示）";
        }

        private static string T(long ticks) { return ticks <= 0 ? "" : new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm"); }

        private void OnRetrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            PlanItem p = e.ItemIndex < shown.Count ? shown[e.ItemIndex] : null;
            if (p == null) { e.Item = new ListViewItem(new string[] { "", "", "", "", "", "", "" }); return; }
            ListViewItem it = new ListViewItem(p.Kind);
            it.SubItems.Add(p.Rel ?? "");
            it.SubItems.Add(p.Size >= 0 ? Fmt.Size(p.Size) : "");
            it.SubItems.Add(T(p.SrcMtime));
            it.SubItems.Add(p.DstSize >= 0 ? Fmt.Size(p.DstSize) : "");
            it.SubItems.Add(T(p.DstMtime));
            it.SubItems.Add(p.Note ?? "");
            it.ForeColor = KindColor(p.Kind);
            e.Item = it;
        }

        private void Export()
        {
            if (result == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV（Excel で開けます）|*.csv";
                d.FileName = "自動コピー_実行結果の予測_" + job.Name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("種類,ファイル,サイズ(バイト),コピー元の更新日時,コピー先のサイズ(バイト),コピー先の更新日時,説明");
                foreach (PlanItem p in shown)
                    sb.AppendLine(string.Join(",", new string[] { Q(p.Kind), Q(p.Rel), p.Size >= 0 ? p.Size.ToString() : "", T(p.SrcMtime),
                        p.DstSize >= 0 ? p.DstSize.ToString() : "", T(p.DstMtime), Q(p.Note) }));
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

        private void RunNow()
        {
            if (!Ui.Confirm(this, "ジョブ「" + job.Name + "」を今すぐ実行します。よろしいですか？\n（調べてから時間がたっている場合、結果が変わることがあります）")) return;
            if (!AppCore.RunJob(job.Id, true, false)) { Ui.Info(this, "このジョブは実行中です。"); return; }
            if (TrayApp.Instance != null) TrayApp.Instance.ShowProgress(job.Id);
            Close();
        }
    }
}
