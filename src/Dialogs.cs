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

            // 「内容を理解しました」にチェックしてから押せる（＝確認済み）
            Button ok = Btn.MakeCaution(Ui.B("ミラーにする", delegate { DialogResult = DialogResult.OK; Close(); }));
            ok.Enabled = false;
            Button cancel = Ui.B("やめる", delegate { DialogResult = DialogResult.Cancel; Close(); });
            agree.CheckedChanged += delegate { ok.Enabled = agree.Checked; };
            CancelButton = cancel;
            Controls.Add(b);
            Controls.Add(Ui.WithHelp(Ui.Footer(cancel, ok), Help.Mirror));

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
        private Button takeShared;
        private readonly ReasonTips reasons = new ReasonTips();

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
            Button newerMenu = Ui.B("新しい方で選ぶ ▾", null);
            newerMenu.Click += delegate
            {
                ContextMenuStrip m = new ContextMenuStrip();
                m.Font = Ui.Base;
                m.Items.Add("コピー元（このPC）の方が新しいものにチェック", null, delegate { CheckNewer(NewerSource); });
                m.Items.Add("コピー先（共有フォルダ）の方が新しいものにチェック", null, delegate { CheckNewer(NewerDest); });
                m.Show(newerMenu, new Point(0, newerMenu.Height));
            };
            Panel checkBar = Ui.CheckAllBar(lv, "すべてにチェック／すべて外す", null,
                Ui.B("条件で選ぶ…", delegate { SelectByCondition(); }), newerMenu);

            backup = Ui.C("上書きする前に、コピー先の古いファイルを「_AutoCopy\\退避」に残す", true);
            // 画面の幅が足りないときは折り返す
            // 似た操作を並べ、まとまりの間を空ける：置き換える（橙・確認あり）｜上書きしない｜見る
            FlowLayoutPanel actions = Ui.WrapRow(Ui.L("チェックしたファイルを"),
                Btn.MakeCaution(Ui.B("それぞれ新しい方を使う…", delegate { ApplyNewer(); })),
                Btn.MakeCaution(Ui.B("上書きする（このPCの方を使う）", delegate { Apply(AppCore.ActOverwrite); })),
                takeShared = Btn.MakeCaution(Ui.B("共有フォルダの方を使う（同期のみ）", delegate { Apply(AppCore.ActTakeShared); })),
                Btn.Gap(),
                Ui.B("今回はスキップ", delegate { Apply(AppCore.ActSkipOnce); }),
                Ui.B("今後も上書きしない", delegate { Apply(AppCore.ActSkipForever); }),
                Btn.Gap(),
                Ui.B("コピー先のフォルダを開く", delegate { OpenDest(); }),
                Ui.B("「今後も上書きしない」にしたファイル…", delegate { using (SkipListForm f = new SkipListForm()) f.ShowDialog(this); Reload(); }));
            FlowLayoutPanel bottom = Ui.Column(actions, backup,
                Ui.Hint("「今回はスキップ」：コピー元がまた変わるまで聞きません。「後で決める」場合は、このまま閉じてください（トレイに確認待ちとして残ります）。"));
            bottom.Dock = DockStyle.Bottom;
            bottom.Padding = new Padding(12, 6, 12, 6);
            Resize += delegate { actions.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 30), 0); };
            actions.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 30), 0);

            // 「共有フォルダの方を使う」は、チェックしたものがすべて同期のジョブのときだけ押せる（押せない理由はマウスを重ねると出る）
            // チェックが変わるたびに全件を読むと、件数が多いと固まる（行を足すたびにも起きる）。
            // また一覧を作っている最中に読むと一覧が壊れ、画面が開かないことがある。まとめて1回、作り終えてから調べる
            lv.ItemChecked += delegate { UpdateTakeSharedLater(); };
            reasons.Set(takeShared, delegate { return TakeSharedReason(); });
            Controls.Add(lv);
            Controls.Add(checkBar);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる（後で決める）", delegate { Close(); }), Ui.B("最新にする", delegate { Reload(); })), Help.Conflicts));
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
            List<ListViewItem> add = new List<ListViewItem>();
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
                    it.SubItems[2].Tag = c.SrcMtime;   // 並べ替えは日時の順
                    it.SubItems[3].Tag = c.DstMtime;
                    it.SubItems.Add(c.Reason == "newer" ? "コピー先が新しい" : c.Reason == "both" ? "同期：両方で変更" : c.Reason == "first" ? "同期：初回で中身が違う" : "日時同じ・サイズ違い");
                    it.ForeColor = j.Sync ? Ui.Warn : SystemColors.WindowText;   // 同期のジョブは橙（どちらの版を使うか選べる）
                    Row r = new Row(); r.Job = j; r.Item = c;
                    it.Tag = r;
                    add.Add(it);
                }
            }
            lv.Items.AddRange(add.ToArray());   // まとめて足す（件数が多くても速い）
            ((AcListView)lv).Resort();
            lv.EndUpdate();
            Text = "上書きの確認（" + lv.Items.Count + "件）";
            UpdateTakeShared();
        }

        /// <summary>「共有フォルダの方を使う」を押せない理由（押せる = null）</summary>
        public string TakeSharedReason()
        {
            int n = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                Row r = it.Tag as Row;
                if (r == null) continue;
                if (!r.Job.Sync) return "同期ではないジョブのファイル（" + r.Job.Name + "）にチェックが入っています。同期のジョブのファイルだけにチェックを入れてください";
                n++;
            }
            return n == 0 ? "同期のジョブのファイルにチェックを入れると押せます" : null;
        }

        private void UpdateTakeShared()
        {
            if (takeShared == null || lv.IsDisposed) return;
            if (lv.IsHandleCreated && lv.RecreatingHandle) return;
            takeShared.Enabled = TakeSharedReason() == null;
        }

        private bool takeSharedPending;

        private void UpdateTakeSharedLater()
        {
            if (takeSharedPending) return;
            if (!IsHandleCreated) return;   // 画面を作る前：Reload の最後と、表示したときに調べる
            takeSharedPending = true;
            try { BeginInvoke(new Action(delegate() { takeSharedPending = false; UpdateTakeShared(); })); }
            catch (Exception) { takeSharedPending = false; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateTakeShared();
        }

        // ---------- 新しい方 ----------

        public const int NewerSource = 1, NewerDest = 2, NewerUnknown = 0;

        /// <summary>どちらが新しいか（日時の差がジョブの許容範囲以内なら決められない）</summary>
        public static int WhichNewer(JobConfig job, ConflictItem c)
        {
            long tol = TimeSpan.TicksPerSecond * Math.Max(0, job.ToleranceSeconds);
            if (c.SrcMtime - c.DstMtime > tol) return NewerSource;
            if (c.DstMtime - c.SrcMtime > tol) return NewerDest;
            return NewerUnknown;
        }

        /// <summary>
        /// 「それぞれ新しい方を使う」の扱い（決められない = null）。
        /// コピー元が新しい → 上書きする（コピー元を使う）。
        /// コピー先が新しい → 同期のジョブは「共有フォルダの方を使う」、それ以外は「今回はスキップ」（コピー先をそのまま残す）。
        /// </summary>
        public static string NewerAction(JobConfig job, ConflictItem c)
        {
            int w = WhichNewer(job, c);
            if (w == NewerSource) return AppCore.ActOverwrite;
            if (w == NewerDest) return job.Sync ? AppCore.ActTakeShared : AppCore.ActSkipOnce;
            return null;
        }

        /// <summary>新しい方の行にチェックを入れる（ほかの行のチェックは外す）。戻り値：チェックした件数</summary>
        public int CheckNewer(int side)
        {
            int n = 0;
            lv.BeginUpdate();
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    Row r = it.Tag as Row;
                    bool on = r != null && WhichNewer(r.Job, r.Item) == side;
                    if (it.Checked != on) it.Checked = on;
                    if (on) n++;
                }
            }
            finally { lv.EndUpdate(); }
            return n;
        }

        /// <summary>チェックしたファイルを、それぞれ新しい方に揃える（確認してから）</summary>
        private void ApplyNewer()
        {
            List<Row> rows = new List<Row>();
            foreach (ListViewItem it in lv.CheckedItems) rows.Add((Row)it.Tag);
            if (rows.Count == 0) { Ui.Info(this, "ファイルにチェックを入れてください。\n（「新しい方で選ぶ ▾」でまとめてチェックできます）"); return; }
            int ow = 0, keep = 0, shared = 0, unknown = 0;
            foreach (Row r in rows)
            {
                string a = NewerAction(r.Job, r.Item);
                if (a == AppCore.ActOverwrite) ow++;
                else if (a == AppCore.ActTakeShared) shared++;
                else if (a == AppCore.ActSkipOnce) keep++;
                else unknown++;
            }
            if (ow + keep + shared == 0) { Ui.Info(this, "チェックしたファイルは、どれも更新日時が同じで、どちらが新しいか決められません。\nそれぞれのボタンで選んでください。"); return; }
            string msg = "チェックした " + rows.Count + "件を、それぞれ新しい方に揃えます。\n\n"
                + (ow > 0 ? "・コピー元（このPC）の方が新しい " + ow + "件：コピー先を上書きします" + (backup.Checked ? "（古いファイルは「_AutoCopy\\退避」に残します）" : "（「退避に残す」のチェックが外れています）") + "\n" : "")
                + (shared > 0 ? "・共有フォルダの方が新しい " + shared + "件（同期）：このPCのファイルを共有フォルダの版で置き換えます（このPCの今の版は控えに残ります）\n" : "")
                + (keep > 0 ? "・コピー先の方が新しい " + keep + "件：コピー先をそのまま残します（今回はスキップ）\n" : "")
                + (unknown > 0 ? "・更新日時が同じでどちらとも言えない " + unknown + "件：何もしません（確認待ちのまま）\n" : "")
                + "\nよろしいですか？";
            if (!Ui.Confirm(this, msg)) return;
            bool bk = backup.Checked;
            List<string> errors = Ui.Busy<List<string>>(this, "処理しています…", delegate()
            {
                List<string> errs = new List<string>();
                foreach (Row r in rows)
                {
                    string a = NewerAction(r.Job, r.Item);
                    if (a == null) continue;
                    string e = AppCore.ResolveConflict(r.Job, r.Item, a, bk);
                    if (e != null) errs.Add(e);
                }
                return errs;
            });
            Reload();
            if (errors.Count > 0) Ui.Error(this, "できなかったものがあります：\n" + string.Join("\n", errors.ToArray()));
        }

        private void Apply(string action)
        {
            List<Row> rows = new List<Row>();
            foreach (ListViewItem it in lv.CheckedItems) rows.Add((Row)it.Tag);
            if (rows.Count == 0) { Ui.Info(this, "ファイルにチェックを入れてください。"); return; }
            if (action == AppCore.ActOverwrite && !Ui.Confirm(this, rows.Count + "件を、コピー元のファイルで上書きします。よろしいですか？")) return;
            if (action == AppCore.ActSkipForever && !Ui.Confirm(this, rows.Count + "件を「今後も上書きしない」にします。\nこれらのファイルは、コピー元が変わってもコピーしなくなります（「「今後も上書きしない」にしたファイル…」から取り消せます）。よろしいですか？")) return;
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

        private static SelectRow MapRow(ListViewItem it)
        {
            Row r = it.Tag as Row;
            if (r == null) return null;
            SelectRow s = new SelectRow();
            s.Path = r.Item.Rel; s.Size = r.Item.SrcSize; s.Mtime = r.Item.SrcMtime;
            s.Tags.Add(it.SubItems[4].Text);
            s.Tags.Add("ジョブ：" + r.Job.Name);
            return s;
        }

        /// <summary>条件（名前・拡張子・大きさ・更新日時・状態・ジョブ）でまとめてチェックする</summary>
        private void SelectByCondition()
        {
            using (SelectDialog d = new SelectDialog("上書きの確認", SelectDialog.TagsOf(lv, MapRow), delegate(SelectCriteria c) { return SelectDialog.Count(lv, MapRow, c); }, false))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SelectDialog.Apply(lv, MapRow, null, d.Action, d.Criteria);
            }
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
            // 右クリック：削除されるファイル（コピー先にあるもの）の保存先を開く
            Ui.AddLocationMenu(lv, delegate(ListViewItem x)
            {
                return new string[] { PathUtil.Combine(CopyEngine.ResolvedDest(job, AppCore.Settings), x.Text) };
            });

            Button del = Ui.B("削除する", delegate
            {
                if (!Ui.Confirm(this, Fmt.Count(items.Count) + "件をコピー先から削除します（削除済みフォルダへ移動）。よろしいですか？")) return;
                string err = Ui.Busy<string>(this, "削除しています…", delegate() { return AppCore.ResolvePendingDeletes(job, true); });
                if (err != null) Ui.Error(this, err);
                Close();
            });
            Btn.MakeDanger(del);
            Button keep = Ui.B("今回は削除しない（コピー先に残す）", delegate
            {
                string err = AppCore.ResolvePendingDeletes(job, false);
                if (err != null) Ui.Error(this, err);
                Close();
            });
            Controls.Add(lv);
            Controls.Add(top);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("後で決める", delegate { Close(); }), Btn.Gap(), del, Btn.Gap(), keep), Help.Mirror));
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
            Ui.AddLocationMenu(lv, delegate(ListViewItem x)
            {
                TrashStore.Batch b = x.Tag as TrashStore.Batch;
                return b == null ? null : new string[] { b.Path };
            });
            Controls.Add(lv);
            Controls.Add(h);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Btn.Gap(), Btn.MakeCaution(Ui.B("選んだものを元に戻す", delegate { Restore(); })), Btn.Gap(),
                Ui.B("フォルダを開く", delegate { Ui.OpenFolder(PathUtil.Combine(CopyEngine.ResolvedDest(job, AppCore.Settings), AppPaths.DestMetaFolder)); })), Help.Restore));
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
            string busy = AppCore.BeginResolve(job); if (busy != null) { Ui.Info(this, busy); return; }
            try
            {
                int n = Ui.Busy<int>(this, "元に戻しています…", delegate() { return TrashStore.Restore(job.Name, dst, b); });
                Ui.Info(this, n + "件を元に戻しました。");
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
            finally { AppCore.EndResolve(job); }
            Reload();
        }
    }

    /// <summary>重複チェック結果</summary>
    public class DupForm : Form
    {
        private readonly ComboBox cbJob;
        private readonly ListView lv;
        private readonly Label summary, total;
        private readonly Button bToText, bToCopy, bSkip, bResetSkip;
        private Button deleteButton;
        private ComboBox actionMode;
        private Label actionHelp;
        private DupResult current;
        private JobConfig currentJob;
        private bool filling;
        private bool totalPending;
        /// <summary>画面を開くだけのボタン（取り消しにくい操作ではない）</summary>
        private static Button SafeBtn(Button b) { b.Tag = "safe"; return b; }

        /// <summary>一覧の行の保存先の候補（コピー先にあるもの → コピー元）</summary>
        public static string[] DupPaths(JobConfig job, ListViewItem x)
        {
            if (job == null || x == null) return null;
            string dst = CopyEngine.ResolvedDest(job, AppCore.Settings), src = CopyEngine.ResolvedSource(job, AppCore.Settings);
            DupEntry e = x.Tag as DupEntry;
            if (e != null) return new string[] { PathUtil.Combine(dst, e.Rel) };
            DedupPendingItem p = x.Tag as DedupPendingItem;
            string rel = p != null ? p.Rel : (x.Tag as string) == "first" && x.SubItems.Count > 1 ? x.SubItems[1].Text : null;
            if (string.IsNullOrEmpty(rel)) return null;
            return new string[] { PathUtil.Combine(dst, rel), PathUtil.Combine(src, rel) };
        }
        /// <summary>重複チェックの一覧に一度に出す行数の上限</summary>
        public const int MaxRows = 5000;

        public DupForm()
        {
            Ui.SetupForm(this, "コピー先の整理（重複チェック）の結果", 1040, 680);
            cbJob = Ui.Combo(260);
            cbJob.SelectedIndexChanged += delegate { LoadResult(); };
            summary = Ui.L("");
            Ui.FitToForm(summary, 1000);
            actionMode = Ui.Combo(300, "大きい重複：保存方法を選ぶ", "古いコピー：削除済みへ移す");
            actionMode.SelectedIndexChanged += delegate { LoadResult(); };
            FlowLayoutPanel top = Ui.Column(Ui.Row(Ui.L("操作"), actionMode), Ui.Row(Ui.L("ジョブ"), cbJob, Ui.B("今すぐ再チェック", delegate { Recheck(); }),
                Ui.B("フォルダを指定して探す…", delegate { if (TrayApp.Instance != null) TrayApp.Instance.ShowFolderDup(null); else using (FolderDupForm f = new FolderDupForm(null)) f.ShowDialog(this); })), summary);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(10, 8, 10, 4);

            lv = Ui.List("操作|90", "コピー先のパス|360", "ファイル数|70", "サイズ|80", "更新日時|120", "判定|300");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;
            lv.ItemCheck += OnItemCheck;
            // 右クリック：重複・削除候補の保存先（コピー先。なければコピー元）をエクスプローラーで開く
            Ui.AddLocationMenu(lv, delegate(ListViewItem x) { return DupPaths(currentJob, x); });
            // 合計の計算は、チェックが続けて変わったときもまとめて1回にする（件数が多くても固まらない）
            lv.ItemChecked += delegate
            {
                if (filling || totalPending) return;
                totalPending = true;
                try { BeginInvoke(new Action(delegate() { totalPending = false; UpdateTotal(); })); } catch (Exception) { totalPending = false; }
            };
            Panel checkBar = Ui.CheckAllBar(lv, "操作できる行をすべて選択／選択を解除", delegate(bool on)
            {
                // 1件ずつの確認を通さずに、まとめて決める（件数が多くても速く）：
                // 削除できるものだけチェックし、同じ中身のグループでは少なくとも1つ残す
                filling = true;
                lv.BeginUpdate();
                try
                {
                    Dictionary<ListViewGroup, int> left = new Dictionary<ListViewGroup, int>();
                    foreach (ListViewItem it in lv.Items)
                    {
                        bool want = false;
                        DupEntry d = it.Tag as DupEntry;
                        if (on && it.Tag is DedupPendingItem) want = true;
                        else if (on && d != null && d.CanDelete)
                        {
                            want = true;
                            ListViewGroup g = it.Group;
                            if (g != null && NeedsKeep(g))
                            {
                                int remain;
                                if (!left.TryGetValue(g, out remain))
                                {
                                    remain = 0;
                                    foreach (ListViewItem x in g.Items) { DupEntry e2 = x.Tag as DupEntry; if (e2 != null && !e2.CanDelete) remain = -1; }
                                    if (remain == 0) remain = g.Items.Count;   // 全部が削除できる：最後の1つは残す
                                    left[g] = remain;
                                }
                                if (remain > 0) { remain--; left[g] = remain; if (remain == 0) want = false; }
                            }
                        }
                        if (it.Checked != want) it.Checked = want;
                    }
                }
                finally { lv.EndUpdate(); filling = false; }
                UpdateTotal();
            }, Ui.B("条件で選ぶ…", delegate { SelectByCondition(); }));

            total = Ui.L("");
            total.Font = Ui.Bold;
            Label note = Ui.Hint("チェックしたものを削除します。削除したものはコピー先の「_AutoCopy\\削除済み」に移り、保存日数の間は元に戻せます。各グループの「今の名前」は削除できません。");
            bToText = Btn.MakeCaution(Ui.B("保管場所のテキストに置換…", delegate { ResolveLarge("text"); }));
            bToCopy = Ui.B("実ファイルとして保存…", delegate { ResolveLarge("copy"); });
            bSkip = Ui.B("確認済みにする…", delegate { ResolveLarge("skip"); });
            bResetSkip = Ui.B("確認済みを解除…", delegate { ResetSkipped(); });
            Label lnote = Ui.Hint("大きいファイルの重複：チェックしたものを「テキストにする」と、コピー先には1つ目の保管場所を書いたテキストを置きます（コピー済みのものは削除済みへ移し、元に戻せます）。「普通にコピー」を選ぶと、今後もそのままコピーします。");
            Ui.FitToForm(lnote, 1000);
            actionHelp = Ui.Hint(""); Ui.FitToForm(actionHelp, 1000);
            FlowLayoutPanel bottom = Ui.Column(total, actionHelp, Ui.Row(bResetSkip,
                SafeBtn(Ui.B("削除済みから復元…", delegate { if (currentJob != null) using (TrashForm f = new TrashForm(currentJob)) f.ShowDialog(this); })),
                Ui.B("削除済みフォルダを開く", delegate { if (currentJob != null) Ui.OpenFolder(PathUtil.Combine(PathUtil.Combine(CopyEngine.ResolvedDest(currentJob, AppCore.Settings), AppPaths.DestMetaFolder), "削除済み")); })));
            bottom.Dock = DockStyle.Bottom;
            bottom.Padding = new Padding(12, 6, 12, 6);

            Button del = deleteButton = Btn.MakeDanger(Ui.B("削除済みへ移す…", delegate { DeleteChecked(); }));
            Controls.Add(lv);
            Controls.Add(checkBar);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Btn.Gap(), bSkip, bToCopy, bToText, del), Help.DupCheck));
            FillJobs();
            VisibleChanged += delegate { if (Visible) MarkSeen(); };
        }

        /// <summary>画面に表示した結果を「見た」にする（開く前に別のジョブの結果を見たことにしない）</summary>
        private void MarkSeen()
        {
            if (current == null || current.Seen || currentJob == null) return;
            current.Seen = true;
            try { Json.Save(DupChecker.ResultPath(currentJob.Id), current); } catch (Exception ex) { Logger.Error(currentJob.Name, "重複チェックの結果を保存できません: " + ex.Message); }
            AppCore.RaiseChanged();
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

        public void SelectActionMode(bool large) { actionMode.SelectedIndex = large ? 0 : 1; }

        private void LoadResult()
        {
            int i = cbJob.SelectedIndex;
            if (i < 0 || i >= AppCore.Settings.Jobs.Count) return;
            currentJob = AppCore.Settings.Jobs[i];
            current = DupChecker.LoadResult(currentJob.Id);
            if (Visible) MarkSeen();
            filling = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Groups.Clear();
            bool largeMode = actionMode.SelectedIndex == 0;
            bSkip.Visible = bResetSkip.Visible = bToText.Visible = bToCopy.Visible = largeMode; deleteButton.Visible = !largeMode;
            actionHelp.Text = largeMode ? "行を選んで操作します。「確認済み」は今のファイルを変更せず、以後の確認を省略します。通常コピーは続け、内容が変わると再確認します。"
                : "①古いコピーにチェック → ②削除済みへ移す。保存期間内は「元に戻す」から復元できます。";
            int largeCount = largeMode ? AddLargeGroups() : 0;
            if (current == null)
            {
                summary.Text = "まだコピー先の整理（重複チェック）をしていません。" + (largeCount > 0 ? "（大きいファイルの重複 " + largeCount + "件）" : "");
            }
            else
            {
                summary.Text = current.Checked + " にチェック・グループ " + current.Groups.Count + "件" + (largeCount > 0 ? "・大きいファイルの重複 " + largeCount + "件" : "")
                    + (current.CloudSkipped > 0 ? "・OneDrive の「クラウドだけ」にある " + current.CloudSkipped + "件は、ダウンロードしないよう中身を調べていません" : "");
                int rows = lv.Items.Count, allRows = rows, hiddenGroups = 0;
                // 行は最後にまとめて入れる（1行ずつ入れると、まとまり表示の一覧では件数が多いと極端に遅くなる）
                List<ListViewItem> buf = new List<ListViewItem>();
                foreach (DupGroup g in current.Groups) allRows += g.Entries.Count;
                foreach (DupGroup g in current.Groups)
                {
                    if (largeMode) continue;
                    // 件数がとても多いときは、表示する行数を抑える（画面が固まったり落ちたりしないように）
                    if (rows >= MaxRows) { hiddenGroups++; continue; }
                    rows += g.Entries.Count;
                    ListViewGroup lg = new ListViewGroup(g.Title);
                    lg.Tag = g.Kind;
                    lv.Groups.Add(lg);
                    foreach (DupEntry e in g.Entries)
                    {
                        if (buf.Count >= MaxRows + 2000) { hiddenGroups++; break; }   // 1つのまとまりがとても大きいとき
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
                        buf.Add(it);
                    }
                }
                lv.Items.AddRange(buf.ToArray());
                if (hiddenGroups > 0)
                    summary.Text += "　※件数が多いため、最初の約 " + Fmt.Count(lv.Items.Count) + "行だけ表示しています（全 " + Fmt.Count(allRows) + "行・残り " + hiddenGroups + "グループ）。表示したものを処理してから「今すぐ再チェック」を押すと続きが出ます。";
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
            int shownLarge = 0;
            foreach (DedupPendingItem p in items)
            {
                if (++shownLarge > MaxRows / 2) break;
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
            string msg = action == "skip" ? sel.Count + "件を確認済みにします。\n現在のファイルは変更しません。通常コピーは継続し、同じ内容の重複は今後確認しません。\n内容が変わった場合は再確認します。「確認済みを解除」で戻せます。"
                : action == "text"
                ? sel.Count + "件をテキストにします（コピー先には1つ目の保管場所を書いたテキストを置きます）。" + (copied > 0 ? "\nコピー済みの " + copied + "件（" + Fmt.Size(bytes) + "）は「_AutoCopy\\削除済み」へ移します（元に戻せます）。" : "") + "\nよろしいですか？"
                : sel.Count + "件を普通にコピーします（今後もこのままコピーします）。よろしいですか？";
            if (!Ui.Confirm(this, msg)) return;
            JobConfig job = currentJob;
            string busy = AppCore.BeginResolve(job); if (busy != null) { Ui.Info(this, busy); return; }
            try {
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
            } finally { AppCore.EndResolve(job); }
        }

        private void ResetSkipped()
        {
            if (currentJob == null || !Ui.Confirm(this, "このジョブの「確認済み」をすべて解除します。次回の重複チェックから再び確認対象になります。")) return;
            JobConfig job = currentJob;
            string busy = AppCore.BeginResolve(job); if (busy != null) { Ui.Info(this, busy); return; }
            try { LargeDup.ResetSkipped(job.Id); LoadResult(); AppCore.RaiseChanged(); }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
            finally { AppCore.EndResolve(job); }
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
            if (!NeedsKeep(g)) return;   // 空のフォルダ・孤立したファイルは全部選んでもよい
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

        private static SelectRow MapDup(ListViewItem it)
        {
            SelectRow s = new SelectRow();
            DupEntry e = it.Tag as DupEntry;
            DedupPendingItem p = it.Tag as DedupPendingItem;
            if (e != null)
            {
                s.Path = e.Rel; s.Size = e.Size; s.Mtime = e.Mtime;
                s.Tags.Add(it.SubItems.Count > 5 ? it.SubItems[5].Text : "");
            }
            else if (p != null)
            {
                s.Path = p.Rel; s.Size = p.Size; s.Mtime = p.Mtime;
                s.Tags.Add("大きいファイルの重複");
            }
            else return null;
            return s;
        }

        private static bool CanCheckDup(ListViewItem it)
        {
            DupEntry e = it.Tag as DupEntry;
            return it.Tag is DedupPendingItem || (e != null && e.CanDelete);
        }

        /// <summary>条件で選ぶ（名前・拡張子・大きさ・更新日時・判定）。同じ中身のまとまりでは必ず1つ残す</summary>
        private void SelectByCondition()
        {
            using (SelectDialog d = new SelectDialog("コピー先の整理の結果", SelectDialog.TagsOf(lv, MapDup), delegate(SelectCriteria c) { return SelectDialog.Count(lv, MapDup, c); }, true))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                filling = true;
                try
                {
                    if (d.Action == SelectAction.KeepOne) KeepOneEach(d.Keep, d.Criteria);
                    else SelectDialog.Apply(lv, MapDup, CanCheckDup, d.Action, d.Criteria);
                    EnforceKeep(d.Keep, d.Criteria);
                }
                finally { filling = false; }
                UpdateTotal();
            }
        }

        /// <summary>各まとまりで、残すもの（決め方を選べる）以外にチェック</summary>
        private void KeepOneEach(string rule, SelectCriteria c)
        {
            lv.BeginUpdate();
            try
            {
                foreach (ListViewGroup g in lv.Groups)
                {
                    List<ListViewItem> del = new List<ListViewItem>();
                    bool hasKeep = false;
                    foreach (ListViewItem it in g.Items) { if (CanCheckDup(it)) del.Add(it); else if (it.Tag is DupEntry) hasKeep = true; }
                    if (del.Count == 0) continue;
                    int keep = -1;
                    if (!hasKeep && NeedsKeep(g))
                    {
                        List<SelectRow> rows = new List<SelectRow>();
                        foreach (ListViewItem it in del) rows.Add(MapDup(it));
                        keep = KeepRule.Pick(rows, rule, c);
                    }
                    for (int k = 0; k < del.Count; k++)
                    {
                        bool want = k != keep && (c.IsEmpty || rule == KeepRule.Matching || c.Matches(MapDup(del[k])));
                        if (del[k].Checked != want) del[k].Checked = want;
                    }
                }
            }
            finally { lv.EndUpdate(); }
        }

        /// <summary>同じ中身のまとまりで、すべてにチェックが入っていたら1つ（決め方に従って）外す</summary>
        private void EnforceKeep(string rule, SelectCriteria c)
        {
            foreach (ListViewGroup g in lv.Groups)
            {
                if (!NeedsKeep(g)) continue;
                List<ListViewItem> items = new List<ListViewItem>();
                bool anyKept = false;
                foreach (ListViewItem it in g.Items)
                {
                    if (!(it.Tag is DupEntry)) continue;
                    items.Add(it);
                    if (!it.Checked) anyKept = true;
                }
                if (anyKept || items.Count < 2) continue;
                List<SelectRow> rows = new List<SelectRow>();
                foreach (ListViewItem it in items) rows.Add(MapDup(it));
                items[KeepRule.Pick(rows, rule, c)].Checked = false;
            }
        }

        /// <summary>同じ中身のグループ（少なくとも1つ残す必要がある）か</summary>
        private static bool NeedsKeep(ListViewGroup g)
        {
            string k = g.Tag as string;
            return k != "empty" && k != "orphan" && k != "large";
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
            bSkip.Enabled = bToCopy.Enabled = bToText.Enabled = large > 0;
            deleteButton.Enabled = n > 0;
            total.Text = actionMode.SelectedIndex == 0 ? "大きいファイルの重複：" + large + "件を選択中"
                : "削除済みへ移すもの：" + Fmt.Count(n) + "件・" + Fmt.Size(bytes);
        }

        private void Recheck()
        {
            if (currentJob == null) return;
            if (!AppCore.RunDup(currentJob.Id)) { Ui.Info(this, "このジョブは実行中です。終わってからもう一度お試しください。"); return; }
            summary.Text = "コピー先の整理（重複チェック）中です…（終わると自動で表示が変わります）";
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
            string busy = AppCore.BeginResolve(job);
            if (busy != null) { Ui.Info(this, busy); return; }
            List<string> skipped = new List<string>();
            List<DupEntry> completed = new List<DupEntry>(); List<string> errors = new List<string>();
            DupResult cur = current;
            try
            {
                int done = Ui.Busy<int>(this, "中身を確かめて削除しています…", delegate()
                {
                    List<DupEntry> ok = DupChecker.VerifyBeforeDelete(job, AppCore.Settings, cur, sel, skipped);
                    int r = DupChecker.DeleteSelected(job, AppCore.Settings, AppCore.GetState(job.Id), ok, completed, errors);
                    AppCore.SaveState(job.Id);
                    return r;
                });
                if (skipped.Count > 0)
                {
                    HashSet<string> sk = new HashSet<string>(skipped, StringComparer.OrdinalIgnoreCase);
                    sel.RemoveAll(delegate(DupEntry x) { return sk.Contains(x.Rel); });
                    Ui.Info(this, skipped.Count + "件は、今の名前のファイルと中身が同じでなかったため、削除しませんでした。");
                }
                // 結果から消す
                foreach (DupGroup g in current.Groups) g.Entries.RemoveAll(delegate(DupEntry x) { return completed.Contains(x); });
                current.Groups.RemoveAll(delegate(DupGroup g) { return g.Entries.Count < 2 && g.Kind != "orphan" && g.Kind != "empty" || g.Entries.Count == 0; });
                try { Json.Save(DupChecker.ResultPath(job.Id), current); } catch (Exception ex) { Logger.Error(job.Name, "重複チェックの結果を保存できません: " + ex.Message); }
                Ui.Info(this, Fmt.Count(done) + "件を削除済みへ移動しました。");
                if (errors.Count > 0) Ui.Error(this, "失敗した項目は一覧に残しました。\n" + string.Join("\n", errors.ToArray()));
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
            finally { AppCore.EndResolve(job); }
            LoadResult();
        }
    }

    /// <summary>「今後も上書きしない」にしたファイルの一覧。チェックして取り消すと、次回からまた上書きの確認に出る。</summary>
    public class SkipListForm : Form
    {
        private readonly ListView lv;

        public SkipListForm()
        {
            Ui.SetupForm(this, "「今後も上書きしない」にしたファイル", 860, 520);
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("「今後も上書きしない」にしたファイル"),
                Ui.Hint("ここにあるファイルは、コピー元が変わってもコピーしません（上書きの確認にも出ません）。チェックして「取り消す」を押すと、次回の実行からまた確認するようになります。"));
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12, 10, 12, 6);
            lv = Ui.List("ファイル|360", "ジョブ|160", "コピー元の更新日時|140", "コピー先の更新日時|140");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;
            Panel bar = Ui.CheckAllBar(lv, "すべてにチェック／すべて外す", null, Ui.B("条件で選ぶ…", delegate { SelectByCondition(); }));
            Controls.Add(lv);
            Controls.Add(bar);
            Controls.Add(top);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Btn.Gap(),
                Btn.MakeCaution(Ui.B("チェックしたものを取り消す（また確認する）", delegate { Forget(); }))), Help.Skip));
            Reload();
        }

        private void Reload()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
            {
                JobState st = AppCore.GetState(j.Id);
                List<string> rels;
                lock (st) rels = new List<string>(st.SkipForever);
                rels.Sort(StringComparer.CurrentCultureIgnoreCase);
                string src = CopyEngine.ResolvedSource(j, AppCore.Settings), dst = CopyEngine.ResolvedDest(j, AppCore.Settings);
                foreach (string r in rels)
                {
                    ListViewItem it = new ListViewItem(r);
                    it.SubItems.Add(j.Name);
                    it.SubItems.Add(MtimeOf(PathUtil.Combine(src, r)));
                    it.SubItems.Add(MtimeOf(PathUtil.Combine(dst, r)));
                    it.Tag = new KeyValuePair<JobConfig, string>(j, r);
                    lv.Items.Add(it);
                }
            }
            if (lv.Items.Count == 0)
            {
                ListViewItem none = new ListViewItem("（「今後も上書きしない」にしたファイルはありません）");
                none.ForeColor = Ui.Quiet;
                lv.Items.Add(none);
            }
            lv.EndUpdate();
            Text = "「今後も上書きしない」にしたファイル（" + CountReal() + "件）";
        }

        private static SelectRow MapSkip(ListViewItem it)
        {
            if (!(it.Tag is KeyValuePair<JobConfig, string>)) return null;
            KeyValuePair<JobConfig, string> kv = (KeyValuePair<JobConfig, string>)it.Tag;
            SelectRow s = new SelectRow();
            s.Path = kv.Value;
            DateTime t;
            if (DateTime.TryParse(it.SubItems[2].Text, out t)) s.Mtime = t.ToUniversalTime().Ticks;
            s.Tags.Add("ジョブ：" + kv.Key.Name);
            return s;
        }

        private void SelectByCondition()
        {
            using (SelectDialog d = new SelectDialog("今後も上書きしないファイル", SelectDialog.TagsOf(lv, MapSkip), delegate(SelectCriteria c) { return SelectDialog.Count(lv, MapSkip, c); }, false))
                if (d.ShowDialog(this) == DialogResult.OK) SelectDialog.Apply(lv, MapSkip, null, d.Action, d.Criteria);
        }

        private int CountReal()
        {
            int n = 0;
            foreach (ListViewItem it in lv.Items) if (it.Tag != null) n++;
            return n;
        }

        private static string MtimeOf(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(PathUtil.Long(path));
                return fi.Exists ? fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm") : "（ありません）";
            }
            catch (Exception) { return ""; }
        }

        private void Forget()
        {
            Dictionary<JobConfig, List<string>> by = new Dictionary<JobConfig, List<string>>();
            foreach (ListViewItem it in lv.CheckedItems)
            {
                if (!(it.Tag is KeyValuePair<JobConfig, string>)) continue;
                KeyValuePair<JobConfig, string> kv = (KeyValuePair<JobConfig, string>)it.Tag;
                List<string> l;
                if (!by.TryGetValue(kv.Key, out l)) { l = new List<string>(); by[kv.Key] = l; }
                l.Add(kv.Value);
            }
            int n = 0;
            foreach (List<string> l in by.Values) n += l.Count;
            if (n == 0) { Ui.Info(this, "取り消すファイルにチェックを入れてください。"); return; }
            if (!Ui.Confirm(this, n + "件の「今後も上書きしない」を取り消します。\n次回の実行で、コピー先の方が新しければ、また上書きの確認に出ます。よろしいですか？")) return;
            foreach (KeyValuePair<JobConfig, List<string>> kv in by) AppCore.ForgetSkipForever(kv.Key, kv.Value);
            Reload();
        }
    }

    /// <summary>同期：一方で削除されたファイルを、もう一方からも消すかどうかの確認</summary>
    public class SyncDeleteForm : Form
    {
        private readonly ListView lv;
        private readonly string onlyJob;

        public SyncDeleteForm(string jobId)
        {
            onlyJob = jobId;
            Ui.SetupForm(this, "同期：削除の確認", 1000, 540);
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("同期のフォルダで削除されたファイルがあります"),
                Ui.Hint("一方（このPC または 共有フォルダ）で削除されたファイルです。もう一方からも消すか、残すかを選んでください。選ぶまでは、どちらもそのままにします。"),
                Ui.Hint("「削除する」：このPCの分は控え（" + AppPaths.SyncBackupDir + "）へ、共有フォルダの分は「_AutoCopy\\削除済み」へ移します（保存日数の間は戻せます）。「残す」：このPCで削除したものはすぐにこのPCへ戻し、共有フォルダで削除されたものは次回の実行で共有フォルダへコピーし直します。"));
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12, 10, 12, 6);
            lv = Ui.List("ファイル|330", "ジョブ|140", "どこで削除されたか → どうなるか|280", "サイズ|80", "更新日時|130", "見つけた日時|130");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;
            // 右クリック：これから削除するファイル（残っている側）の保存先を開く
            Ui.AddLocationMenu(lv, delegate(ListViewItem x)
            {
                Row r = x.Tag as Row;
                if (r == null) return null;
                string local = PathUtil.Combine(CopyEngine.ResolvedSource(r.Job, AppCore.Settings), r.Item.Rel);
                string shared = PathUtil.Combine(CopyEngine.ResolvedDest(r.Job, AppCore.Settings), r.Item.Rel);
                return r.Item.Side == SyncDeleteItem.SideLocal ? new string[] { local, shared } : new string[] { shared, local };
            });
            Panel bar = Ui.CheckAllBar(lv, "すべてにチェック／すべて外す", null, Ui.B("条件で選ぶ…", delegate { SelectByCondition(); }));
            Button del = Btn.MakeDanger(Ui.B("チェックしたものを削除する…", delegate { Apply(true); }));
            Controls.Add(lv);
            Controls.Add(bar);
            Controls.Add(top);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる（後で決める）", delegate { Close(); }), Btn.Gap(), del, Btn.Gap(),
                Ui.B("チェックしたものを残す（戻す）", delegate { Apply(false); })), Help.SyncDelete));
            Reload();
        }

        private class Row { public JobConfig Job; public SyncDeleteItem Item; }

        private static SelectRow MapSync(ListViewItem it)
        {
            Row r = it.Tag as Row;
            if (r == null) return null;
            SelectRow s = new SelectRow();
            s.Path = r.Item.Rel; s.Size = r.Item.Size; s.Mtime = r.Item.Mtime;
            s.Tags.Add(r.Item.SideText);
            s.Tags.Add("ジョブ：" + r.Job.Name);
            return s;
        }

        private void SelectByCondition()
        {
            using (SelectDialog d = new SelectDialog("同期：削除の確認", SelectDialog.TagsOf(lv, MapSync), delegate(SelectCriteria c) { return SelectDialog.Count(lv, MapSync, c); }, false))
                if (d.ShowDialog(this) == DialogResult.OK) SelectDialog.Apply(lv, MapSync, null, d.Action, d.Criteria);
        }

        public void Reload()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
            {
                if (!j.Sync || (onlyJob != null && j.Id != onlyJob)) continue;
                foreach (SyncDeleteItem i in SyncDeleteStore.GetItems(j.Id))
                {
                    ListViewItem it = new ListViewItem(i.Rel);
                    it.SubItems.Add(j.Name);
                    it.SubItems.Add(i.SideText);
                    it.SubItems.Add(Fmt.Size(i.Size));
                    it.SubItems.Add(Fmt.LocalTime(i.Mtime));
                    it.SubItems[4].Tag = i.Mtime;
                    DateTime det;
                    bool hasDet = DateTime.TryParse(i.Detected ?? "", out det);
                    it.SubItems.Add(hasDet ? det.ToString("yyyy/MM/dd HH:mm") : (i.Detected ?? ""));
                    if (hasDet) it.SubItems[5].Tag = det;
                    it.ForeColor = i.Side == SyncDeleteItem.SideLocal ? Ui.Warn : SystemColors.WindowText;
                    Row r = new Row(); r.Job = j; r.Item = i;
                    it.Tag = r;
                    lv.Items.Add(it);
                }
            }
            lv.EndUpdate();
            Text = "同期：削除の確認（" + lv.Items.Count + "件）";
        }

        private void Apply(bool delete)
        {
            Dictionary<JobConfig, List<SyncDeleteItem>> by = new Dictionary<JobConfig, List<SyncDeleteItem>>();
            int n = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                Row r = it.Tag as Row;
                if (r == null) continue;
                List<SyncDeleteItem> l;
                if (!by.TryGetValue(r.Job, out l)) { l = new List<SyncDeleteItem>(); by[r.Job] = l; }
                l.Add(r.Item);
                n++;
            }
            if (n == 0) { Ui.Info(this, "ファイルにチェックを入れてください。"); return; }
            string msg = delete
                ? n + "件を、もう一方からも削除します（消さずに控え・削除済みへ移すので、保存日数の間は戻せます）。よろしいですか？"
                : n + "件を残します（消えた側へ戻します）。よろしいですか？";
            if (!Ui.Confirm(this, msg)) return;
            List<string> errors = Ui.Busy<List<string>>(this, "処理しています…", delegate()
            {
                List<string> e = new List<string>();
                foreach (KeyValuePair<JobConfig, List<SyncDeleteItem>> kv in by) e.AddRange(AppCore.ResolveSyncDeletes(kv.Key, kv.Value, delete));
                return e;
            });
            Reload();
            if (errors.Count > 0) Ui.Error(this, "できなかったものがあります：\n" + string.Join("\n", errors.ToArray()));
        }
    }

    /// <summary>新しいジョブを保存した後：無効のままにせず、確かめてから有効にするよう案内する</summary>
    public class EnableJobForm : Form
    {
        public const int Test = 0, EnableNow = 1, Later = 2;
        /// <summary>選んだもの（閉じたときは「あとで」）</summary>
        public int Choice = Later;

        public EnableJobForm(JobConfig job)
        {
            Ui.SetupDialog(this, "ジョブを保存しました", 600, 270);
            FlowLayoutPanel b = Ui.Body();
            b.Controls.Add(Ui.Heading("ジョブ「" + job.Name + "」はまだ「無効」です"));
            b.Controls.Add(Ui.Wrap("無効のジョブは、自動ではコピーしません。\n"
                + "はじめに「テスト」で、コピーしたらどうなるか（コピーされるファイル・上書き・削除されるもの）を確かめてから有効にすると安心です。", 540));
            b.Controls.Add(Ui.Hint("あとで有効にするときは、メイン画面のジョブの左のチェックを入れます。"));
            Button test = Btn.MakePrimary(Ui.B("テストしてから有効にする…", delegate { Choice = Test; DialogResult = DialogResult.OK; Close(); }));
            Button now = Ui.B("今すぐ有効にする", delegate { Choice = EnableNow; DialogResult = DialogResult.OK; Close(); });
            Button later = Ui.B("あとで（無効のまま）", delegate { Choice = Later; DialogResult = DialogResult.Cancel; Close(); });
            CancelButton = later;
            AcceptButton = test;
            Controls.Add(b);
            Controls.Add(Ui.Footer(later, now, test));
        }
    }
}
