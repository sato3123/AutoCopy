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
        private RunResult result;          // 終わった結果（途中は null）
        private RunResult live;            // 調べている途中の結果（予測の一覧が増えていく）
        private readonly List<PlanItem> items = new List<PlanItem>();   // 画面に取り込んだ予測
        private List<PlanItem> shown = new List<PlanItem>();
        private string kindFilter;   // null = すべて
        private readonly Dictionary<string, int> kindCount = new Dictionary<string, int>();
        private readonly Dictionary<string, long> kindBytes = new Dictionary<string, long>();
        private System.Threading.CancellationTokenSource cts;
        private JobProgress progress;
        private readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        private readonly ProgressBar bar2;
        private readonly Label liveLabel;
        private readonly Button stopBtn, againBtn;
        private readonly Panel liveBand;
        private DateTime started, lastSort = DateTime.MinValue;
        private bool fillingKinds;

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
            kinds.Width = Math.Min(Ui.S(330), ClientSize.Width * 3 / 10);   // 画面が狭いときは右の一覧を広く
            kinds.Columns[1].TextAlign = HorizontalAlignment.Right;
            kinds.Columns[2].TextAlign = HorizontalAlignment.Right;
            ((AcListView)kinds).Sortable = false;
            kinds.SelectedIndexChanged += delegate
            {
                if (fillingKinds) return;
                kindFilter = kinds.SelectedItems.Count > 0 ? kinds.SelectedItems[0].Tag as string : null;
                Fill();
            };

            tSearch = Ui.T("", 180);
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
            ((AcListView)lv).SortRequested += delegate(int col, bool asc) { SortShown(); lv.Invalidate(); };

            // 調べている途中の表示（進み具合・中止）
            bar2 = new ProgressBar();
            bar2.Style = ProgressBarStyle.Marquee;
            bar2.Width = Ui.S(160); bar2.Height = Ui.S(16);
            bar2.Margin = new Padding(Ui.S(4), Ui.S(8), Ui.S(8), 3);
            liveLabel = Ui.L("");
            liveLabel.ForeColor = Ui.Accent;
            liveLabel.Margin = new Padding(3, Ui.S(8), 3, 3);
            stopBtn = Ui.B("中止", delegate { StopAnalyze(); });
            stopBtn.Tag = "safe";   // 調べるのを止めるだけ（コピーはしていない）
            FlowLayoutPanel lb = Ui.Row(bar2, liveLabel, stopBtn);
            liveBand = new Panel();
            liveBand.Dock = DockStyle.Top;
            liveBand.BackColor = Ui.InfoBack;
            liveBand.Height = lb.PreferredSize.Height + Ui.S(4);
            lb.Dock = DockStyle.Fill;
            liveBand.Controls.Add(lb);
            liveBand.Visible = false;
            tick.Interval = 500;
            tick.Tick += delegate { Pull(false); };

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Controls.Add(lv);
            right.Controls.Add(bar);
            Splitter sp = new Splitter();
            sp.Dock = DockStyle.Left;

            runNow = Btn.MakePrimary(Ui.B("この内容で今すぐ実行", delegate { RunNow(); }));
            runNow.Visible = allowRun;
            againBtn = Ui.B("もう一度調べる", delegate { Analyze(); });
            Controls.Add(right);
            Controls.Add(sp);
            Controls.Add(kinds);
            Controls.Add(liveBand);
            Controls.Add(top);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Btn.Gap(), runNow, Btn.Gap(),
                Ui.B("一覧をCSVに書き出す", delegate { Export(); }),
                againBtn));
            Shown += delegate { if (result == null && live == null) Analyze(); };
            FormClosed += delegate { tick.Stop(); if (cts != null) cts.Cancel(); };
        }

        /// <summary>調べた結果を直接渡す（画面の確認用）</summary>
        public void SetResult(RunResult r) { Reset(); result = r; live = r; Pull(true); }

        /// <summary>調べている途中の状態を作る（画面の確認用）：partial の予測を途中経過として表示する</summary>
        public void SetPartial(RunResult partial, JobProgress p)
        {
            Reset();
            live = partial; progress = p; started = DateTime.Now.AddSeconds(-12);
            liveBand.Visible = true;
            againBtn.Enabled = false; runNow.Enabled = false;
            Pull(false);
        }

        private void Reset()
        {
            items.Clear(); shown.Clear(); kindCount.Clear(); kindBytes.Clear();
            result = null; live = null;
            lv.VirtualListSize = 0;
        }

        /// <summary>
        /// 裏で調べ始め、見つかったものから順に一覧に出す（調べ終わるのを待たずに見られる）。
        /// </summary>
        private void Analyze()
        {
            if (cts != null) return;
            Reset();
            RunResult r = new RunResult();
            live = r;
            progress = new JobProgress();
            cts = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationToken ct = cts.Token;
            JobConfig j = job;
            JobProgress p = progress;
            started = DateTime.Now;
            liveBand.Visible = true;
            stopBtn.Enabled = true; stopBtn.Text = "中止";
            againBtn.Enabled = false;
            runNow.Enabled = false;
            summary.Text = "実行したらどうなるかを調べています（コピーはしません）。見つかったものから順に表示します…";
            summary.ForeColor = SystemColors.WindowText;
            tick.Start();
            System.Threading.Tasks.Task.Factory.StartNew(delegate()
            {
                Exception error = null;
                bool canceled = false;
                try { AppCore.DryRun(j, false, ct, p, r); }
                catch (OperationCanceledException) { canceled = true; }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new Action(delegate() { Finished(r, error, canceled); })); } catch (Exception) { }
            }, System.Threading.Tasks.TaskCreationOptions.LongRunning);
        }

        private void StopAnalyze()
        {
            if (cts == null) return;
            cts.Cancel();
            stopBtn.Enabled = false;
            stopBtn.Text = "中止しています…";
        }

        private void Finished(RunResult r, Exception error, bool canceled)
        {
            if (IsDisposed) return;
            tick.Stop();
            if (cts != null) { cts.Dispose(); cts = null; }
            liveBand.Visible = false;
            againBtn.Enabled = true;
            runNow.Enabled = true;
            if (r != live) return;
            if (error != null)
            {
                Pull(false);
                summary.Text = "調べられませんでした：" + error.Message + (items.Count > 0 ? "（途中までの " + Fmt.Count(items.Count) + "件を表示しています）" : "");
                summary.ForeColor = Ui.Err;
                return;
            }
            if (canceled)
            {
                Pull(false);
                summary.Text = "中止しました。途中までに見つかった " + Fmt.Count(items.Count) + "件を表示しています（全部ではありません）。";
                summary.ForeColor = Ui.Warn;
                return;
            }
            result = r;
            Pull(true);
        }

        /// <summary>途中経過（または結果）を取り込んで表示を更新する</summary>
        private void Pull(bool final)
        {
            RunResult r = live;
            if (r == null) return;
            List<PlanItem> fresh;
            lock (r.Plan)
            {
                int n = r.Plan.Count - items.Count;
                fresh = n > 0 ? r.Plan.GetRange(items.Count, n) : new List<PlanItem>();
            }
            foreach (PlanItem p in fresh)
            {
                int c; kindCount.TryGetValue(p.Kind, out c); kindCount[p.Kind] = c + 1;
                long b; kindBytes.TryGetValue(p.Kind, out b);
                kindBytes[p.Kind] = b + Math.Max(0, p.Size >= 0 ? p.Size : p.DstSize);
            }
            items.AddRange(fresh);
            FillKinds(r);
            if (final || result != null) ShowSummary(r);
            else if (progress != null)
            {
                JobProgress s = progress.Snapshot();
                int sec = (int)(DateTime.Now - started).TotalSeconds;
                liveLabel.Text = (s.Phase ?? "") + (s.Scanned > 0 ? "（調べたファイル " + Fmt.Count(s.Scanned) + "件）" : "")
                    + "　見つかったもの " + Fmt.Count(items.Count) + "件　経過 " + (sec >= 60 ? (sec / 60) + "分" + (sec % 60) + "秒" : sec + "秒");
            }
            // 一覧：並べ替えをしていなければ、増えた分を後ろに足す（スクロール位置はそのまま）
            AcListView alv = (AcListView)lv;
            if (fresh.Count == 0 && !final) return;
            if (!final && alv.SortColumn < 0 && shown.Count + fresh.Count <= items.Count)
            {
                string q = tSearch.Text.Trim();
                foreach (PlanItem p in fresh) if (Match(p, q)) shown.Add(p);
                lv.VirtualListSize = shown.Count;
                Text = "実行結果の予測 - " + job.Name + "（" + Fmt.Count(shown.Count) + "件を表示・調べています）";
                return;
            }
            if (!final && (DateTime.Now - lastSort).TotalSeconds < 2) return;   // 並べ替え中は2秒ごと
            Fill();
        }

        private void FillKinds(RunResult r)
        {
            fillingKinds = true;
            kinds.BeginUpdate();
            try
            {
                kinds.Items.Clear();
                ListViewItem all = new ListViewItem("すべて");
                all.SubItems.Add(Fmt.Count(items.Count)); all.SubItems.Add("");
                all.Font = Ui.Bold;
                kinds.Items.Add(all);
                if (kindFilter == null) all.Selected = true;
                // 危険な操作（削除・上書き・確認待ち）を上に並べる
                foreach (string k in KindOrder)
                {
                    int c;
                    if (!kindCount.TryGetValue(k, out c)) continue;
                    long bytes = kindBytes[k];
                    long[] full;
                    lock (r.Plan) { if (r.PlanCounts.TryGetValue(k, out full)) { c = (int)full[0]; bytes = full[1]; } }
                    ListViewItem it = new ListViewItem((IsDanger(k) ? "⚠ " : "") + k);
                    it.SubItems.Add(Fmt.Count(c));
                    it.SubItems.Add(Fmt.Size(bytes));
                    if (IsDanger(k)) it.Font = Ui.Bold;
                    it.Tag = k;
                    it.ForeColor = KindColor(k);
                    kinds.Items.Add(it);
                    if (k == kindFilter) it.Selected = true;
                }
                ListViewItem un = new ListViewItem("変更なし（何もしない）");
                un.SubItems.Add(Fmt.Count(r.Unchanged)); un.SubItems.Add("");
                un.ForeColor = Ui.Quiet;
                un.Tag = "__none";
                kinds.Items.Add(un);
                if (kindFilter == "__none") un.Selected = true;
            }
            finally { kinds.EndUpdate(); fillingKinds = false; }
        }

        /// <summary>一覧の種類の並び：削除・上書き・確認待ちなど、確かめたいものを先に</summary>
        private static readonly string[] KindOrder = {
            PlanKind.Delete, PlanKind.Hold, PlanKind.SyncDelete, PlanKind.Overwrite, PlanKind.Conflict, PlanKind.DedupAsk,
            PlanKind.Copy, PlanKind.Rename, PlanKind.SyncIn, PlanKind.Dedup, PlanKind.Skip, PlanKind.SyncNotTaken, PlanKind.SyncNotPushed,
            PlanKind.Same, PlanKind.Deferred, PlanKind.Excluded };

        private static bool IsDanger(string k)
        {
            return k == PlanKind.Delete || k == PlanKind.Hold || k == PlanKind.SyncDelete || k == PlanKind.Overwrite || k == PlanKind.Conflict;
        }

        private void ShowSummary(RunResult r)
        {
            int copies = 0, ow = 0;
            kindCount.TryGetValue(PlanKind.Copy, out copies);
            kindCount.TryGetValue(PlanKind.Overwrite, out ow);
            summary.Text = "調べたファイル " + Fmt.Count(r.Scanned) + "件 → コピー " + Fmt.Count(copies) + "件・上書き " + Fmt.Count(ow) + "件（合わせて " + Fmt.Size(r.CopyBytesPlanned) + "）"
                + "・確認待ち " + Fmt.Count(r.Conflicts) + "件・名前変更 " + Fmt.Count(r.Renamed) + "件"
                + (job.Mirror ? "・ミラー削除 " + Fmt.Count(r.Deleted) + "件" : "")
                + (r.PlannedDedup > 0 ? "・重複で省略 " + Fmt.Count(r.PlannedDedup) + "件" : "")
                + (r.PlannedDownloads > 0 ? "・取り込み " + Fmt.Count(r.PlannedDownloads) + "件" : "")
                + (r.SyncDeleteCandidates > 0 ? "・削除の確認 " + Fmt.Count(r.SyncDeleteCandidates) + "件" : "")
                + (r.SyncNotTaken > 0 ? "・取り込まない（設定）" + Fmt.Count(r.SyncNotTaken) + "件" : "")
                + (r.SyncNotPushed > 0 ? "・共有へ送らない（設定）" + Fmt.Count(r.SyncNotPushed) + "件" : "")
                + "・除外 " + Fmt.Count(r.Excluded) + "件"
                + (r.Excluded > 5000 ? "（一覧は最初の5,000件）" : "")
                + (r.PlanTruncated ? "　※件数が多いため、一覧は最初の " + Fmt.Count(RunResult.PlanLimit) + "件まで（左の件数・容量はすべて）" : "")
                + DangerText(r)
                + "　" + DateTime.Now.ToString("HH:mm") + " 時点";
            long del = Count(r, PlanKind.Delete) + Count(r, PlanKind.Hold) + Count(r, PlanKind.SyncDelete);
            summary.ForeColor = del > 0 ? Ui.Err : r.Conflicts > 0 || Count(r, PlanKind.Overwrite) > 0 ? Ui.Warn : SystemColors.WindowText;
        }

        private static long Count(RunResult r, string k)
        {
            long[] c;
            lock (r.Plan) return r.PlanCounts.TryGetValue(k, out c) ? c[0] : 0;
        }

        /// <summary>削除・上書き・確認待ちがあれば、目立つように書き足す</summary>
        private static string DangerText(RunResult r)
        {
            long del = Count(r, PlanKind.Delete), hold = Count(r, PlanKind.Hold), sd = Count(r, PlanKind.SyncDelete), ow = Count(r, PlanKind.Overwrite), cf = Count(r, PlanKind.Conflict);
            if (del + hold + sd + ow + cf == 0) return "";
            return "\n⚠ 確かめてください：" + (del > 0 ? "削除 " + Fmt.Count(del) + "件　" : "") + (hold > 0 ? "削除の保留 " + Fmt.Count(hold) + "件　" : "")
                + (sd > 0 ? "同期の削除の確認 " + Fmt.Count(sd) + "件　" : "") + (ow > 0 ? "上書き " + Fmt.Count(ow) + "件　" : "") + (cf > 0 ? "確認待ち " + Fmt.Count(cf) + "件　" : "")
                + "（左の「⚠」の種類をクリックすると、そのファイルだけを表示します）";
        }

        private static Color KindColor(string k)
        {
            if (k == PlanKind.Conflict || k == PlanKind.Hold) return Ui.Warn;
            if (k == PlanKind.Delete) return Ui.Err;
            if (k == PlanKind.Copy || k == PlanKind.Overwrite || k == PlanKind.Rename || k == PlanKind.SyncIn) return Ui.Ok;
            if (k == PlanKind.Excluded || k == PlanKind.Same || k == PlanKind.Deferred || k == PlanKind.Skip || k == PlanKind.SyncNotTaken || k == PlanKind.SyncNotPushed) return Ui.Quiet;
            return SystemColors.WindowText;
        }

        private bool Match(PlanItem p, string q)
        {
            if (kindFilter == "__none") return false;
            if (kindFilter != null && p.Kind != kindFilter) return false;
            if (q.Length > 0 && (p.Rel ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (p.Note ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void Fill()
        {
            string q = tSearch.Text.Trim();
            shown = items.FindAll(delegate(PlanItem p) { return Match(p, q); });
            SortShown();
            lv.VirtualListSize = 0;
            lv.VirtualListSize = shown.Count;
            lv.Invalidate();
            Text = "実行結果の予測 - " + job.Name + "（" + Fmt.Count(shown.Count) + "件を表示" + (result == null && cts != null ? "・調べています" : "") + "）";
        }

        /// <summary>見出しのクリックでの並べ替え（仮想表示なので、元の一覧を並べ替える）</summary>
        private void SortShown()
        {
            AcListView alv = (AcListView)lv;
            int col = alv.SortColumn;
            bool asc = alv.SortAscending;
            lastSort = DateTime.Now;
            if (col < 0) return;
            Comparison<PlanItem> cmp;
            switch (col)
            {
                case 0: cmp = delegate(PlanItem a, PlanItem b) { return Array.IndexOf(PlanKind.All, a.Kind).CompareTo(Array.IndexOf(PlanKind.All, b.Kind)); }; break;
                case 2: cmp = delegate(PlanItem a, PlanItem b) { return a.Size.CompareTo(b.Size); }; break;
                case 3: cmp = delegate(PlanItem a, PlanItem b) { return a.SrcMtime.CompareTo(b.SrcMtime); }; break;
                case 4: cmp = delegate(PlanItem a, PlanItem b) { return a.DstSize.CompareTo(b.DstSize); }; break;
                case 5: cmp = delegate(PlanItem a, PlanItem b) { return a.DstMtime.CompareTo(b.DstMtime); }; break;
                case 6: cmp = delegate(PlanItem a, PlanItem b) { return string.Compare(a.Note, b.Note, StringComparison.CurrentCultureIgnoreCase); }; break;
                default: cmp = delegate(PlanItem a, PlanItem b) { return string.Compare(a.Rel, b.Rel, StringComparison.CurrentCultureIgnoreCase); }; break;
            }
            List<KeyValuePair<int, PlanItem>> idx = new List<KeyValuePair<int, PlanItem>>(shown.Count);
            for (int i = 0; i < shown.Count; i++) idx.Add(new KeyValuePair<int, PlanItem>(i, shown[i]));
            idx.Sort(delegate(KeyValuePair<int, PlanItem> a, KeyValuePair<int, PlanItem> b)
            {
                int r = cmp(a.Value, b.Value);
                if (r == 0) r = a.Key.CompareTo(b.Key);
                return asc ? r : -r;
            });
            for (int i = 0; i < idx.Count; i++) shown[i] = idx[i].Value;
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
            if (items.Count == 0) return;
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
