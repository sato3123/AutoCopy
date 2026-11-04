using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>ミラーにする前の注意画面</summary>
    public class MirrorWarnForm : Form
    {
        public MirrorWarnForm(JobConfig job)
        {
            Ui.SetupDialog(this, "ミラーにする前に確認してください", 620, 470);
            FlowLayoutPanel b = Ui.Body();
            Label h = Ui.Heading("ミラーにすると、コピー先のファイルが削除されます");
            h.ForeColor = Ui.Warn;
            b.Controls.Add(h);
            b.Controls.Add(Ui.Hint(job.Name + "：" + CopyEngine.ResolvedSource(job, AppCore.Settings) + " → " + CopyEngine.ResolvedDest(job, AppCore.Settings)));
            b.Controls.Add(Ui.Section("ミラーの動き",
                Ui.Wrap("・コピー元で削除したファイル・フォルダは、次の実行でコピー先からも削除されます。", 560),
                Ui.Wrap("・コピー元で誤って消した場合も、コピー先から消えます。", 560),
                Ui.Wrap("・削除したファイルは「_AutoCopy\\削除済み」に " + AppCore.Settings.TrashDays + "日間残り、元に戻せます。", 560),
                Ui.Wrap("・コピー元が空のときや接続できないときは、何も削除しません。一度に " + job.MirrorMaxCount + "件を超える削除は確認を求めます。", 560),
                Ui.Wrap("・コピー先に人が直接置いたファイルは削除しません。コピー元のファイルは変更・削除しません。", 560)));

            Label count = Ui.L("次の実行で削除される予定：数えています…");
            count.Font = Ui.Bold;
            Button list = Ui.B("一覧を見る", null);
            list.Enabled = false;
            b.Controls.Add(Ui.Banner(Ui.WarnBack, count, list));

            CheckBox agree = Ui.C("内容を理解しました", false);
            agree.Font = Ui.Bold;
            b.Controls.Add(agree);

            Button ok = Ui.B("ミラーにする", delegate { DialogResult = DialogResult.OK; Close(); });
            ok.Enabled = false;
            Button cancel = Ui.B("やめる", delegate { DialogResult = DialogResult.Cancel; Close(); });
            agree.CheckedChanged += delegate { ok.Enabled = agree.Checked; };
            CancelButton = cancel;
            Controls.Add(b);
            Controls.Add(Ui.Footer(cancel, ok));

            List<string> planned = new List<string>();
            list.Click += delegate
            {
                using (TextListForm f = new TextListForm("削除される予定のファイル", "次の実行でコピー先から削除される予定のファイル", planned)) f.ShowDialog(this);
            };
            Shown += delegate
            {
                System.Threading.Tasks.Task.Factory.StartNew(delegate()
                {
                    string text;
                    try
                    {
                        RunResult r = AppCore.DryRun(job, true);
                        planned.AddRange(r.PlannedDeletes);
                        text = "次の実行で削除される予定：" + Fmt.Count(r.Deleted) + "件・" + Fmt.Size(r.DeleteBytes);
                    }
                    catch (Exception ex) { text = "削除される件数を数えられませんでした（" + ex.Message + "）"; }
                    try { BeginInvoke(new Action(delegate() { count.Text = text; list.Enabled = planned.Count > 0; })); } catch (Exception) { }
                });
            };
        }
    }

    /// <summary>上書きの確認（全ジョブ分）</summary>
    public class ConflictForm : Form
    {
        private readonly ListView lv;
        private readonly CheckBox backup;

        public ConflictForm()
        {
            Ui.SetupForm(this, "上書きの確認", 1100, 560);
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("コピー先の方が新しいファイルがあります"),
                Ui.Hint("上書きすると、コピー先で行われた変更が失われる可能性があります。ファイルにチェックを入れて、どうするかを選んでください。"),
                Ui.Hint("同期のジョブでは「コピー元」がこのPC、「コピー先」が共有フォルダです。どちらを使っても、使わなかった方の版は控えに残ります。"));
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12, 10, 12, 6);

            lv = Ui.List("ファイル|260", "ジョブ|140", "コピー元|150", "コピー先|200", "状態|120");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;

            backup = Ui.C("上書きする前に、コピー先の古いファイルを「_AutoCopy\\退避」に残す", true);
            // 画面の幅が足りないときは折り返す
            FlowLayoutPanel actions = Ui.WrapRow(Ui.L("チェックしたファイルを"),
                Ui.B("上書きする（このPCの方を使う）", delegate { Apply(AppCore.ActOverwrite); }),
                Ui.B("共有フォルダの方を使う（同期のみ）", delegate { Apply(AppCore.ActTakeShared); }),
                Ui.B("今回はスキップ", delegate { Apply(AppCore.ActSkipOnce); }),
                Ui.B("今後も上書きしない", delegate { Apply(AppCore.ActSkipForever); }),
                Ui.B("すべてチェック", delegate { foreach (ListViewItem it in lv.Items) it.Checked = true; }),
                Ui.B("コピー先のフォルダを開く", delegate { OpenDest(); }));
            FlowLayoutPanel bottom = Ui.Column(actions, backup,
                Ui.Hint("「今回はスキップ」：コピー元がまた変わるまで聞きません。「後で決める」場合は、このまま閉じてください（トレイに確認待ちとして残ります）。"));
            bottom.Dock = DockStyle.Bottom;
            bottom.Padding = new Padding(12, 6, 12, 6);
            Resize += delegate { actions.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 30), 0); };
            actions.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 30), 0);

            Controls.Add(lv);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(Ui.Footer(Ui.B("閉じる（後で決める）", delegate { Close(); }), Ui.B("最新にする", delegate { Reload(); })));
            Reload();
        }

        private class Row
        {
            public JobConfig Job;
            public ConflictItem Item;
        }

        public void Reload()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
            {
                JobState st = AppCore.GetState(j.Id);
                List<ConflictItem> items;
                lock (st) { items = new List<ConflictItem>(st.Conflicts); }
                foreach (ConflictItem c in items)
                {
                    ListViewItem it = new ListViewItem(c.Rel);
                    it.SubItems.Add(j.Name);
                    it.SubItems.Add(Fmt.LocalTime(c.SrcMtime) + "・" + Fmt.Size(c.SrcSize));
                    it.SubItems.Add(Fmt.LocalTime(c.DstMtime) + "・" + Fmt.Size(c.DstSize));
                    it.SubItems.Add(c.Reason == "newer" ? "コピー先が新しい" : c.Reason == "both" ? "同期：両方で変更" : c.Reason == "first" ? "同期：初回で中身が違う" : "日時同じ・サイズ違い");
                    if (j.Sync) it.ForeColor = Ui.Warn;
                    it.ForeColor = SystemColors.WindowText;
                    Row r = new Row(); r.Job = j; r.Item = c;
                    it.Tag = r;
                    lv.Items.Add(it);
                }
            }
            lv.EndUpdate();
            Text = "上書きの確認（" + lv.Items.Count + "件）";
        }

        private void Apply(string action)
        {
            List<Row> rows = new List<Row>();
            foreach (ListViewItem it in lv.CheckedItems) rows.Add((Row)it.Tag);
            if (rows.Count == 0) { Ui.Info(this, "ファイルにチェックを入れてください。"); return; }
            if (action == AppCore.ActOverwrite && !Ui.Confirm(this, rows.Count + "件を、コピー元のファイルで上書きします。よろしいですか？")) return;
            if (action == AppCore.ActTakeShared && !Ui.Confirm(this, rows.Count + "件を、共有フォルダのファイルでこのPCのファイルを置き換えます。\nこのPCの今のファイルは控え（" + AppPaths.SyncBackupDir + "）に残ります。よろしいですか？")) return;
            bool bk = backup.Checked;
            List<string> errors = Ui.Busy<List<string>>(this, "処理しています…", delegate()
            {
                List<string> errs = new List<string>();
                foreach (Row r in rows)
                {
                    string e = AppCore.ResolveConflict(r.Job, r.Item, action, bk);
                    if (e != null) errs.Add(e);
                }
                return errs;
            });
            Reload();
            if (errors.Count > 0) Ui.Error(this, "できなかったものがあります：\n" + string.Join("\n", errors.ToArray()));
        }

        private void OpenDest()
        {
            if (lv.SelectedItems.Count == 0 && lv.CheckedItems.Count == 0) return;
            Row r = (Row)(lv.SelectedItems.Count > 0 ? lv.SelectedItems[0] : lv.CheckedItems[0]).Tag;
            string d = PathUtil.Combine(CopyEngine.ResolvedDest(r.Job, AppCore.Settings), PathUtil.ParentRel(r.Item.Rel));
            Ui.OpenFolder(d);
        }
    }

    /// <summary>ミラーの大量削除の確認</summary>
    public class PendingDeleteForm : Form
    {
        public PendingDeleteForm(JobConfig job)
        {
            Ui.SetupForm(this, "ミラー：保留中の削除 - " + job.Name, 760, 520);
            JobState st = AppCore.GetState(job.Id);
            List<PendingDelete> items;
            lock (st) { items = new List<PendingDelete>(st.PendingDeletes); }
            long total = 0;
            foreach (PendingDelete p in items) total += p.Size;

            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("一度に多くのファイルが削除されるため、確認をお待ちしています"),
                Ui.Wrap(job.Name + "：コピー元で " + Fmt.Count(items.Count) + "件（" + Fmt.Size(total) + "）のファイルが見当たりません。" +
                    "コピー元のドライブの付け替えや、誤って消した可能性がないか確かめてください。", 700),
                Ui.Hint("「削除する」を選んでも、コピー先の「_AutoCopy\\削除済み」に " + AppCore.Settings.TrashDays + "日間残り、元に戻せます。"));
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12);

            ListView lv = Ui.List("ファイル|560", "サイズ|100");
            lv.Dock = DockStyle.Fill;
            lv.BeginUpdate();
            foreach (PendingDelete p in items)
            {
                ListViewItem it = new ListViewItem(p.Rel);
                it.SubItems.Add(Fmt.Size(p.Size));
                lv.Items.Add(it);
                if (lv.Items.Count >= 5000) break;
            }
            lv.EndUpdate();

            Button del = Ui.B("削除する", delegate
            {
                if (!Ui.Confirm(this, Fmt.Count(items.Count) + "件をコピー先から削除します（削除済みフォルダへ移動）。よろしいですか？")) return;
                string err = Ui.Busy<string>(this, "削除しています…", delegate() { return AppCore.ResolvePendingDeletes(job, true); });
                if (err != null) Ui.Error(this, err);
                Close();
            });
            del.ForeColor = Ui.Err;
            Button keep = Ui.B("今回は削除しない（コピー先に残す）", delegate
            {
                string err = AppCore.ResolvePendingDeletes(job, false);
                if (err != null) Ui.Error(this, err);
                Close();
            });
            Controls.Add(lv);
            Controls.Add(top);
            Controls.Add(Ui.Footer(Ui.B("後で決める", delegate { Close(); }), keep, del));
        }
    }

    /// <summary>削除済み・退避から元に戻す</summary>
    public class TrashForm : Form
    {
        private readonly JobConfig job;
        private readonly ListView lv;

        public TrashForm(JobConfig job)
        {
            this.job = job;
            Ui.SetupForm(this, "元に戻す - " + job.Name, 720, 460);
            lv = Ui.List("種類|90", "日時|150", "ファイル数|90", "サイズ|100", "場所|260");
            lv.Dock = DockStyle.Fill;
            Label h = Ui.L("削除・上書きの前に退避したファイルを、まとめて元の場所に戻せます（同名があれば「(復元)」を付けます）。");
            h.Dock = DockStyle.Top;
            h.Padding = new Padding(8);
            h.AutoSize = false;
            h.Height = Ui.S(34);
            Controls.Add(lv);
            Controls.Add(h);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Ui.B("選んだものを元に戻す", delegate { Restore(); }),
                Ui.B("フォルダを開く", delegate { Ui.OpenFolder(PathUtil.Combine(CopyEngine.ResolvedDest(job, AppCore.Settings), AppPaths.DestMetaFolder)); })));
            Shown += delegate { Reload(); };
        }

        private void Reload()
        {
            List<TrashStore.Batch> list;
            try
            {
                string dst = CopyEngine.ResolvedDest(job, AppCore.Settings);
                list = Ui.Busy<List<TrashStore.Batch>>(this, "読み込んでいます…", delegate()
                {
                    NetAccess.Ensure(dst, job.RetryCount, job.RetryIntervalSeconds, System.Threading.CancellationToken.None, null);
                    return TrashStore.List(dst);
                });
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); return; }
            lv.Items.Clear();
            foreach (TrashStore.Batch b in list)
            {
                ListViewItem it = new ListViewItem(b.Kind);
                it.SubItems.Add(b.Time == DateTime.MinValue ? b.Name : b.Time.ToString("yyyy/MM/dd HH:mm:ss"));
                it.SubItems.Add(Fmt.Count(b.FileCount) + (b.EmptyFolderCount > 0 ? "＋空のフォルダ " + Fmt.Count(b.EmptyFolderCount) : ""));
                it.SubItems.Add(Fmt.Size(b.Bytes));
                it.SubItems.Add(PathUtil.Plain(b.Path));
                it.Tag = b;
                lv.Items.Add(it);
            }
        }

        private void Restore()
        {
            if (lv.SelectedItems.Count == 0) { Ui.Info(this, "元に戻すものを選んでください。"); return; }
            TrashStore.Batch b = (TrashStore.Batch)lv.SelectedItems[0].Tag;
            if (!Ui.Confirm(this, b.FileCount + "件" + (b.EmptyFolderCount > 0 ? "（と空のフォルダ " + b.EmptyFolderCount + "件）" : "") + "を元の場所に戻します。よろしいですか？")) return;
            string dst = CopyEngine.ResolvedDest(job, AppCore.Settings);
            try
            {
                int n = Ui.Busy<int>(this, "元に戻しています…", delegate() { return TrashStore.Restore(job.Name, dst, b); });
                Ui.Info(this, n + "件を元に戻しました。");
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
            Reload();
        }
    }

    /// <summary>重複チェック結果</summary>
    public class DupForm : Form
    {
        private readonly ComboBox cbJob;
        private readonly ListView lv;
        private readonly Label summary, total;
        private readonly Button bToText, bToCopy;
        private DupResult current;
        private JobConfig currentJob;
        private bool filling;

        public DupForm()
        {
            Ui.SetupForm(this, "重複チェック結果", 1040, 680);
            cbJob = Ui.Combo(260);
            cbJob.SelectedIndexChanged += delegate { LoadResult(); };
            summary = Ui.L("");
            FlowLayoutPanel top = Ui.Row(Ui.L("ジョブ"), cbJob, Ui.B("今すぐ再チェック", delegate { Recheck(); }), summary);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(10, 8, 10, 4);

            lv = Ui.List("操作|90", "コピー先のパス|440", "ファイル数|80", "サイズ|90", "更新日時|130", "判定|160");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;
            lv.ItemCheck += OnItemCheck;
            lv.ItemChecked += delegate { if (!filling) UpdateTotal(); };

            total = Ui.L("");
            total.Font = Ui.Bold;
            Label note = Ui.Hint("チェックしたものを削除します。削除したものはコピー先の「_AutoCopy\\削除済み」に移り、保存日数の間は元に戻せます。各グループの「今の名前」は削除できません。");
            bToText = Ui.B("選んだ重複をテキストにする（コピーしない）", delegate { ResolveLarge("text"); });
            bToCopy = Ui.B("選んだ重複を普通にコピーする", delegate { ResolveLarge("copy"); });
            Label lnote = Ui.Hint("大きいファイルの重複：チェックしたものを「テキストにする」と、コピー先には1つ目の保管場所を書いたテキストを置きます（コピー済みのものは削除済みへ移し、元に戻せます）。「普通にコピー」を選ぶと、今後もそのままコピーします。");
            Ui.FitToForm(lnote, 1000);
            FlowLayoutPanel bottom = Ui.Column(total, note, Ui.Row(bToText, bToCopy), lnote);
            bottom.Dock = DockStyle.Bottom;
            bottom.Padding = new Padding(12, 6, 12, 6);

            Button del = Ui.B("選んだものを削除…", delegate { DeleteChecked(); });
            del.ForeColor = Ui.Err;
            Controls.Add(lv);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), del,
                Ui.B("元に戻す…", delegate { if (currentJob != null) using (TrashForm f = new TrashForm(currentJob)) f.ShowDialog(this); }),
                Ui.B("削除済みフォルダを開く", delegate { if (currentJob != null) Ui.OpenFolder(PathUtil.Combine(PathUtil.Combine(CopyEngine.ResolvedDest(currentJob, AppCore.Settings), AppPaths.DestMetaFolder), "削除済み")); })));
            FillJobs();
        }

        private void FillJobs()
        {
            cbJob.Items.Clear();
            foreach (JobConfig j in AppCore.Settings.Jobs) cbJob.Items.Add(j.Name);
            if (cbJob.Items.Count > 0) cbJob.SelectedIndex = 0;
        }

        public void SelectJob(string jobId)
        {
            if (cbJob.Items.Count != AppCore.Settings.Jobs.Count) FillJobs();
            if (jobId == null) { LoadResult(); return; }
            for (int i = 0; i < AppCore.Settings.Jobs.Count; i++)
                if (AppCore.Settings.Jobs[i].Id == jobId) { cbJob.SelectedIndex = i; break; }
            LoadResult();
        }

        private void LoadResult()
        {
            int i = cbJob.SelectedIndex;
            if (i < 0 || i >= AppCore.Settings.Jobs.Count) return;
            currentJob = AppCore.Settings.Jobs[i];
            current = DupChecker.LoadResult(currentJob.Id);
            if (current != null && !current.Seen)
            {
                current.Seen = true;
                try { Json.Save(DupChecker.ResultPath(currentJob.Id), current); } catch (Exception) { }
                AppCore.RaiseChanged();
            }
            filling = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Groups.Clear();
            int largeCount = AddLargeGroups();
            if (current == null)
            {
                summary.Text = "まだ重複チェックをしていません。" + (largeCount > 0 ? "（大きいファイルの重複 " + largeCount + "件）" : "");
            }
            else
            {
                summary.Text = current.Checked + " にチェック・グループ " + current.Groups.Count + "件" + (largeCount > 0 ? "・大きいファイルの重複 " + largeCount + "件" : "");
                foreach (DupGroup g in current.Groups)
                {
                    ListViewGroup lg = new ListViewGroup(g.Title);
                    lg.Tag = g.Kind;
                    lv.Groups.Add(lg);
                    foreach (DupEntry e in g.Entries)
                    {
                        ListViewItem it = new ListViewItem(e.CanDelete ? "削除" : "残す", lg);
                        bool empty = e.Status == "empty";
                        it.SubItems.Add(e.Rel + (empty ? "\\（空のフォルダ）" : e.IsFolder ? "\\（フォルダごと）" : ""));
                        it.SubItems.Add(empty ? "—" : Fmt.Count(Math.Max(1, e.FileCount)));
                        it.SubItems.Add(Fmt.Size(e.Size));
                        it.SubItems.Add(Fmt.LocalTime(e.Mtime));
                        string judge = e.Status == "old" ? "古い名前（コピー元になし）" : e.Status == "current" ? "今の名前（コピー元と一致）"
                            : e.Status == "info" ? "情報のみ" : empty ? "空のフォルダ（コピー元になし）" : "孤立（コピー元になし）";
                        it.SubItems.Add(judge);
                        it.Checked = e.CanDelete && e.Recommended;
                        it.ForeColor = e.Status == "old" ? Ui.Warn : e.Status == "current" ? Ui.Ok : Ui.Quiet;
                        it.Tag = e;
                        lv.Items.Add(it);
                    }
                }
            }
            lv.EndUpdate();
            filling = false;
            UpdateTotal();
        }

        /// <summary>大きいファイルの重複（扱いを選んでもらうもの）を、1つ目ごとのまとまりで一覧に入れる。件数を返す。</summary>
        private int AddLargeGroups()
        {
            bToText.Enabled = bToCopy.Enabled = false;
            if (currentJob == null || !currentJob.DedupOn) return 0;
            DedupBook book = DedupStore.Get(currentJob.Id);
            List<DedupPendingItem> items;
            lock (book) items = new List<DedupPendingItem>(book.Pending);
            if (items.Count == 0) return 0;
            items.Sort(delegate(DedupPendingItem a, DedupPendingItem b)
            {
                int c = string.Compare(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase);
            });
            ListViewGroup lg = null;
            string lastTarget = null;
            foreach (DedupPendingItem p in items)
            {
                if (!string.Equals(p.Target, lastTarget, StringComparison.OrdinalIgnoreCase))
                {
                    lastTarget = p.Target;
                    lg = new ListViewGroup("大きいファイルの重複：「" + p.Target + "」と中身が同じ（" + Fmt.Size(p.Size) + "）　扱いを選んでください");
                    lg.Tag = "large";
                    lv.Groups.Add(lg);
                    ListViewItem first = new ListViewItem("1つ目", lg);
                    first.SubItems.Add(p.Target);
                    first.SubItems.Add("1");
                    first.SubItems.Add(Fmt.Size(p.Size));
                    first.SubItems.Add("");
                    first.SubItems.Add("コピーする（1つ目）");
                    first.ForeColor = Ui.Ok;
                    first.Tag = "first";
                    lv.Items.Add(first);
                }
                ListViewItem it = new ListViewItem("選んでください", lg);
                it.SubItems.Add(p.Rel);
                it.SubItems.Add("1");
                it.SubItems.Add(Fmt.Size(p.Size));
                it.SubItems.Add(Fmt.LocalTime(p.Mtime));
                it.SubItems.Add(p.Copied ? "普通にコピー済み（テキストにすると " + Fmt.Size(p.Size) + " 空く）" : "まだコピーしていない（選ぶまで待っています）");
                it.ForeColor = Ui.Warn;
                it.Tag = p;
                lv.Items.Add(it);
            }
            bToText.Enabled = bToCopy.Enabled = true;
            return items.Count;
        }

        private void ResolveLarge(string action)
        {
            if (currentJob == null) return;
            List<DedupPendingItem> sel = new List<DedupPendingItem>();
            foreach (ListViewItem it in lv.CheckedItems) { DedupPendingItem p = it.Tag as DedupPendingItem; if (p != null) sel.Add(p); }
            if (sel.Count == 0) { Ui.Info(this, "大きいファイルの重複の行にチェックを入れてください。"); return; }
            long bytes = 0; int copied = 0;
            foreach (DedupPendingItem p in sel) if (p.Copied) { bytes += p.Size; copied++; }
            string msg = action == "text"
                ? sel.Count + "件をテキストにします（コピー先には1つ目の保管場所を書いたテキストを置きます）。" + (copied > 0 ? "\nコピー済みの " + copied + "件（" + Fmt.Size(bytes) + "）は「_AutoCopy\\削除済み」へ移します（元に戻せます）。" : "") + "\nよろしいですか？"
                : sel.Count + "件を普通にコピーします（今後もこのままコピーします）。よろしいですか？";
            if (!Ui.Confirm(this, msg)) return;
            JobConfig job = currentJob;
            List<string> errs = Ui.Busy<List<string>>(this, "処理しています…", delegate()
            {
                List<string> e = new List<string>();
                JobState st = AppCore.GetState(job.Id);
                foreach (DedupPendingItem p in sel)
                {
                    try { string r = LargeDup.Resolve(job, AppCore.Settings, st, p, action); if (r != null) e.Add(r); }
                    catch (Exception ex) { e.Add(p.Rel + "：" + ex.Message); }
                }
                AppCore.SaveState(job.Id);
                return e;
            });
            AppCore.RaiseChanged();
            LoadResult();
            if (errs.Count > 0) Ui.Error(this, "できなかったものがあります：\n" + string.Join("\n", errs.ToArray()));
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling || e.NewValue == e.CurrentValue) return;
            if (e.Index < 0 || e.Index >= lv.Items.Count) return;
            object tag = lv.Items[e.Index].Tag;
            if (tag is string) { if (e.NewValue == CheckState.Checked) e.NewValue = CheckState.Unchecked; return; }   // 1つ目は選べない
            if (tag is DedupPendingItem) return;
            DupEntry d = lv.Items[e.Index].Tag as DupEntry;
            if (d == null) return;
            if (!d.CanDelete && e.NewValue == CheckState.Checked) { e.NewValue = CheckState.Unchecked; return; }
            if (e.NewValue != CheckState.Checked) return;
            // 残す側が1つもなくなる選び方はできない
            ListViewGroup g = lv.Items[e.Index].Group;
            if (g == null) return;
            if (g.Tag as string == "empty") return;   // 空のフォルダは全部選んでもよい
            int keep = 0, total = 0;
            foreach (ListViewItem it in g.Items)
            {
                if (it.ListView == null || it.Index < 0) continue; // 一覧から外れた古い項目
                total++;
                DupEntry x = (DupEntry)it.Tag;
                bool willCheck = it.Index == e.Index ? true : it.Checked;
                if (!x.CanDelete || !willCheck) keep++;
            }
            if (keep == 0 && total > 1)
            {
                e.NewValue = CheckState.Unchecked;
                Ui.Info(this, "同じ中身のファイルは、少なくとも1つ残してください。");
            }
        }

        private void UpdateTotal()
        {
            int n = 0; long bytes = 0;
            int large = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                DupEntry d = it.Tag as DupEntry;
                if (d == null) { if (it.Tag is DedupPendingItem) large++; continue; }
                n += Math.Max(1, d.FileCount); bytes += d.Size;
            }
            total.Text = "削除するもの：" + Fmt.Count(n) + "件・" + Fmt.Size(bytes) + (large > 0 ? "　／　大きいファイルの重複：" + large + "件を選択中" : "");
        }

        private void Recheck()
        {
            if (currentJob == null) return;
            if (!AppCore.RunDup(currentJob.Id)) { Ui.Info(this, "このジョブは実行中です。終わってからもう一度お試しください。"); return; }
            summary.Text = "重複チェック中です…（終わると自動で表示が変わります）";
            string id = currentJob.Id;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 2000;
            t.Tick += delegate
            {
                if (IsDisposed) { t.Stop(); t.Dispose(); return; }
                if (AppCore.GetRuntime(id).DupRunning) return;
                t.Stop(); t.Dispose();
                if (currentJob != null && currentJob.Id == id) LoadResult();
            };
            t.Start();
        }

        private void DeleteChecked()
        {
            if (currentJob == null) return;
            List<DupEntry> sel = new List<DupEntry>();
            int n = 0; long bytes = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                DupEntry d = it.Tag as DupEntry;
                if (d == null || !d.CanDelete) continue;
                sel.Add(d); n += Math.Max(1, d.FileCount); bytes += d.Size;
            }
            if (sel.Count == 0) { Ui.Info(this, "削除するものにチェックを入れてください。"); return; }
            if (!Ui.Confirm(this, Fmt.Count(n) + "件（" + Fmt.Size(bytes) + "）をコピー先から削除します。\n削除したものは「_AutoCopy\\削除済み」に " + AppCore.Settings.TrashDays + "日間残ります。よろしいですか？")) return;
            JobConfig job = currentJob;
            try
            {
                int done = Ui.Busy<int>(this, "削除しています…", delegate()
                {
                    int r = DupChecker.DeleteSelected(job, AppCore.Settings, AppCore.GetState(job.Id), sel);
                    AppCore.SaveState(job.Id);
                    return r;
                });
                // 結果から消す
                foreach (DupGroup g in current.Groups) g.Entries.RemoveAll(delegate(DupEntry x) { return sel.Contains(x); });
                current.Groups.RemoveAll(delegate(DupGroup g) { return g.Entries.Count < 2 && g.Kind != "orphan" && g.Kind != "empty" || g.Entries.Count == 0; });
                try { Json.Save(DupChecker.ResultPath(job.Id), current); } catch (Exception) { }
                Ui.Info(this, Fmt.Count(done) + "件を削除しました（削除済みフォルダへ移動）。");
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
            LoadResult();
        }
    }
}
