using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// フォルダ内の重複ファイル検索：コピーとは関係なく、指定したフォルダの中で中身が同じファイルを探す。
    /// 探すだけではファイルは変わらない。移すときは同じフォルダの「_AutoCopy\重複で削除」へ（消さない・戻せる）。
    /// 自動コピーのコピー元のファイルは移さない。
    /// </summary>
    public class FolderDupForm : Form
    {
        public const int MaxRows = 5000;
        private readonly TextBox tFolder;
        private readonly CheckBox cSub, cbContent, cbName, cbMtime, cbSize;
        private readonly ComboBox cbMin;   // 小さいファイルを探さない（よく使う大きさから選ぶ）
        private readonly ListView lv;
        private readonly Label summary, total, protectNote;
        private readonly Button bMove;
        private FolderDupResult result;
        private bool filling, totalPending;

        private class Row
        {
            public FolderDupGroup Group;
            public string Rel;
            public bool Keep;   // 残す候補（まとまりの先頭）
        }

        public FolderDupForm(string folder)
        {
            Ui.SetupForm(this, "フォルダの重複ファイル探し", 1040, 660);
            tFolder = Ui.T(folder ?? "", 420);
            Button browse = Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, tFolder.Text, "重複を探すフォルダを選んでください"); if (f != null) tFolder.Text = f; });
            Button fromJob = Ui.B("ジョブのフォルダ ▾", null);
            ContextMenuStrip jm = new ContextMenuStrip();
            jm.Font = Ui.Base;
            fromJob.Click += delegate
            {
                jm.Items.Clear();
                foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
                {
                    string s = CopyEngine.ResolvedSource(j, AppCore.Settings), d = CopyEngine.ResolvedDest(j, AppCore.Settings);
                    jm.Items.Add(j.Name + "：コピー元　" + s, null, delegate { tFolder.Text = s; });
                    jm.Items.Add(j.Name + "：コピー先　" + d, null, delegate { tFolder.Text = d; });
                }
                if (jm.Items.Count == 0) jm.Items.Add("（ジョブがありません）").Enabled = false;
                jm.Show(fromJob, new Point(0, fromJob.Height));
            };
            cSub = Ui.C("サブフォルダも含める", true);
            // 同じとみなす基準（組み合わせられる）
            cbContent = Ui.C("中身", true);
            cbName = Ui.C("ファイル名", false);
            cbMtime = Ui.C("更新日時", false);
            cbSize = Ui.C("大きさ", false);
            // 中身で比べるときは、大きさも必ず同じ：「大きさ」はチェックを入れた状態で灰色にして「含まれる」ことを示す
            // （中身をやめたら、利用者が前に選んでいた状態に戻す。検索の条件の求め方は変えない）
            EventHandler basisChanged = delegate
            {
                if (cbContent.Checked)
                {
                    if (cbSize.Enabled) sizeChoice = cbSize.Checked;
                    cbSize.Enabled = false;
                    cbSize.Checked = true;
                }
                else if (!cbSize.Enabled)
                {
                    cbSize.Enabled = true;
                    cbSize.Checked = sizeChoice;
                }
            };
            cbContent.CheckedChanged += basisChanged;
            basisChanged(null, EventArgs.Empty);
            new ToolTip().SetToolTip(cbSize, "「中身」で比べるときは、大きさも必ず同じです（自動で含みます）");
            new ToolTip().SetToolTip(cbMtime, "2秒以内の差は同じとみなします（NAS・USBメモリの丸めのため）");
            new ToolTip().SetToolTip(cbName, "フォルダは問いません。大文字・小文字は区別しません");
            cbMin = Ui.SizeChoice(0, "小さいファイルも探す（すべて）", "{0} より小さいファイルは探さない");
            Button search = Ui.B("検索", delegate { Search(); });
            Btn.MakePrimary(search);
            Btn.SetIcon(fromJob, "folder", Ui.Quiet);
            summary = Ui.L("フォルダを選んで「検索」を押してください。");
            Ui.FitToForm(summary, 980);
            protectNote = Ui.L("");
            protectNote.ForeColor = Ui.Warn;
            Ui.FitToForm(protectNote, 980);
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("フォルダの重複ファイル探し（コピーと関係なく、指定したフォルダの中を調べます）"),
                Ui.Hint("コピーとは関係なく、指定したフォルダの中で同じファイルを探します。探すだけでは、ファイルは何も変わりません。"),
                Ui.WrapRow(Ui.L("フォルダ"), tFolder, browse, fromJob),
                Ui.WrapRow(Ui.L("同じとみなす基準"), cbContent, cbName, cbMtime, cbSize, Ui.Hint("（選んだものがすべて同じなら重複。中身を選ばないときはファイルを読まないので速い）")),
                Ui.WrapRow(cSub, Ui.L("小さいファイル"), cbMin, search),
                summary, protectNote);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(10, 8, 10, 4);
            Resize += delegate { foreach (Control c in top.Controls) if (c is FlowLayoutPanel) c.MaximumSize = new Size(Math.Max(Ui.S(300), ClientSize.Width - Ui.S(30)), 0); };

            lv = Ui.List("扱い|170", "ファイル（フォルダの中の場所）|480", "サイズ|90", "更新日時|130");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;
            lv.ItemCheck += OnItemCheck;
            lv.ItemChecked += delegate
            {
                if (filling || totalPending) return;
                totalPending = true;
                try { BeginInvoke(new Action(delegate() { totalPending = false; UpdateTotal(); })); } catch (Exception) { totalPending = false; }
            };
            lv.DoubleClick += delegate { OpenSelected(); };
            // 右クリック：重複ファイルの保存先をエクスプローラーで開く（ダブルクリックと同じ）
            Ui.AddLocationMenu(lv, delegate(ListViewItem x)
            {
                Row r = x.Tag as Row;
                return r == null || result == null ? null : new string[] { PathUtil.Combine(result.Root, r.Rel) };
            });
            Panel bar = Ui.CheckAllBar(lv, "各まとまりで1つ残して、ほかにすべてチェック／すべて外す", delegate(bool on) { CheckAllButFirst(on); },
                Ui.B("条件で選ぶ…", delegate { SelectByCondition(); }));

            total = Ui.L("");
            total.Font = Ui.Bold;
            bMove = Ui.B("チェックしたものを移す…", delegate { MoveChecked(); });
            Btn.MakeDanger(bMove);
            new ToolTip().SetToolTip(bMove, "消さずに、そのフォルダの「_AutoCopy\\重複で削除」へ移します");
            Label note = Ui.Hint("移したファイルは、そのフォルダの「_AutoCopy\\重複で削除\\日時」に入ります（隠しフォルダ）。元に戻すときは、そこから元の場所へ戻してください。自動コピーのコピー元のファイルは移しません。");
            Ui.FitToForm(note, 980);
            // 移した先のフォルダを開く・一覧の書き出しは、移した先の説明のすぐ下に置く（下の帯は「移す」と「閉じる」だけ）
            FlowLayoutPanel sub = Ui.WrapRow(
                Ui.B("移したファイルのフォルダを開く", delegate { string r = tFolder.Text.Trim(); if (r.Length > 0) Ui.OpenFolder(FolderDup.TrashFolder(r)); }),
                Ui.B("CSVに書き出す", delegate { Export(); }));
            FlowLayoutPanel bottom = Ui.Column(total, note, sub);
            bottom.Dock = DockStyle.Bottom;
            bottom.Padding = new Padding(12, 6, 12, 6);

            Controls.Add(lv);
            Controls.Add(bar);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Btn.Gap(), bMove), Help.FolderDup));
            bMove.Enabled = false;
            // 灰色のときは、押せない理由を出す
            reasons.Set(bMove, delegate
            {
                if (result == null) return "まだ検索していません。フォルダを選んで「検索」を押してください";
                if (protectedFolder) return "自動コピーのコピー元のフォルダです。コピー元のファイルは移しません（元データを守るため）";
                if (result.Groups.Count == 0) return "重複が見つかりませんでした";
                return null;
            });
        }

        private readonly ReasonTips reasons = new ReasonTips();
        private bool protectedFolder;

        private long MinBytes { get { return Ui.SizeChoiceKB(cbMin) * 1024L; } }

        /// <summary>画面で選んだ「同じとみなす基準」</summary>
        private bool sizeChoice;   // 「中身」をやめたときに戻す「大きさ」のチェック

        /// <summary>画面の表示：「大きさ」のチェック欄（テスト用）</summary>
        public CheckBox SizeBox { get { return cbSize; } }
        public CheckBox ContentBox { get { return cbContent; } }

        public int Basis
        {
            get
            {
                int b = 0;
                if (cbContent.Checked) b |= DupBasis.Content;
                if (cbName.Checked) b |= DupBasis.Name;
                if (cbMtime.Checked) b |= DupBasis.Mtime;
                if (cbSize.Checked && cbSize.Enabled) b |= DupBasis.Size;
                return b;
            }
            set
            {
                cbContent.Checked = DupBasis.Has(value, DupBasis.Content);
                cbName.Checked = DupBasis.Has(value, DupBasis.Name);
                cbMtime.Checked = DupBasis.Has(value, DupBasis.Mtime);
                if (cbContent.Checked) sizeChoice = DupBasis.Has(value, DupBasis.Size);   // 中身のときは「含まれる」表示のまま
                else cbSize.Checked = DupBasis.Has(value, DupBasis.Size);
            }
        }

        /// <summary>小さいファイルを除く大きさ（画面の確認用にも使う）</summary>
        public void SetMin(int value, bool mb) { Ui.SetSizeChoice(cbMin, Math.Max(0, mb ? value * 1024 : value)); }

        public void Search()
        {
            string root = tFolder.Text.Trim();
            if (root.Length == 0) { Ui.Info(this, "フォルダを選んでください。"); return; }
            root = NetAccess.ToUncIfMapped(root);
            bool sub = cSub.Checked;
            long min = MinBytes;
            int basis = Basis;
            if (!DupBasis.Valid(basis)) { Ui.Info(this, "同じとみなす基準（中身・ファイル名・更新日時・大きさ）を1つ以上選んでください。"); return; }
            bool stopped;
            FolderDupResult r;
            try
            {
                r = Ui.BusyCancel<FolderDupResult>(this, "「" + root + "」の中で、" + DupBasis.Describe(basis) + "が同じファイルを探しています（読むだけです）…", delegate(System.Threading.CancellationToken ct, BusyProgress bp)
                {
                    try { NetAccess.Ensure(root, 1, 5, ct, null); } catch (Exception) { }
                    return FolderDup.Find(root, sub, min, basis, ct, delegate(string t) { bp.Text = t; });
                }, out stopped);
            }
            catch (Exception ex) { Ui.Error(this, "探せませんでした：" + ex.Message); return; }
            if (stopped) { summary.Text = "中止しました。"; return; }
            SetResult(r);
            Logger.Write(null, "DUPCHECK", "フォルダ内の重複検索：" + root + "　基準 " + DupBasis.Describe(basis) + (min > 0 ? "・" + Fmt.Size(min) + "未満は除く" : "") + "　調べたファイル " + r.Scanned + "件・重複 " + r.Groups.Count + "まとまり（" + r.WastedFiles + "件・" + Fmt.Size(r.WastedBytes) + "）", r.WastedBytes, -1);
        }

        /// <summary>結果を表示する（画面の確認用にも使う）</summary>
        public void SetResult(FolderDupResult r)
        {
            result = r;
            tFolder.Text = r.Root;
            Basis = r.Basis;
            bool prot = SafeFs.IsProtected(PathUtil.Combine(r.Root, "__check__"));
            protectNote.Text = prot ? "このフォルダは自動コピーのコピー元（またはその中）のため、探すだけで移すことはできません（元データは変えません）。" : "";
            protectNote.Visible = prot;

            filling = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Groups.Clear();
            List<ListViewItem> buf = new List<ListViewItem>();
            int shownGroups = 0;
            foreach (FolderDupGroup g in r.Groups)
            {
                if (buf.Count >= MaxRows) break;
                shownGroups++;
                string what = DupBasis.Describe(r.Basis) + "が同じ ";
                ListViewGroup lg = new ListViewGroup(what + g.Files.Count + "件（" + (g.SameContent ? "1件 " + Fmt.Size(g.Size) + "・" : "中身は未確認・") + "1つ残すと " + Fmt.Size(g.Wasted()) + " 空く）");
                lv.Groups.Add(lg);
                for (int i = 0; i < g.Files.Count; i++)
                {
                    Row row = new Row(); row.Group = g; row.Rel = g.Files[i]; row.Keep = i == 0;
                    ListViewItem it = new ListViewItem(i == 0 ? "残す候補（" + g.KeepReason() + "）" : "重複", lg);
                    it.SubItems.Add(g.Files[i]);
                    it.SubItems.Add(Fmt.Size(g.SizeAt(i)));
                    it.SubItems[2].Tag = g.SizeAt(i);
                    it.SubItems.Add(Fmt.LocalTime(g.Mtimes[i]));
                    it.SubItems[3].Tag = g.Mtimes[i];
                    it.ForeColor = i == 0 ? Ui.Ok : Ui.Warn;
                    it.Tag = row;
                    buf.Add(it);
                }
            }
            lv.Items.AddRange(buf.ToArray());
            lv.EndUpdate();
            filling = false;
            summary.Text = "調べたファイル " + Fmt.Count(r.Scanned) + "件（" + Fmt.Size(r.ScannedBytes) + "）"
                + (r.MinBytes > 0 && r.SkippedSmall > 0 ? "うち " + Fmt.Size(r.MinBytes) + " より小さい " + Fmt.Count(r.SkippedSmall) + "件は除く" : "")
                + "→ " + DupBasis.Describe(r.Basis) + "が同じもの " + Fmt.Count(r.Groups.Count) + "まとまり。"
                + "1つずつ残すと " + Fmt.Count(r.WastedFiles) + "件・" + Fmt.Size(r.WastedBytes) + " 空きます。"
                + (shownGroups < r.Groups.Count ? "　※件数が多いため、空く容量の大きい順に " + Fmt.Count(shownGroups) + "まとまりだけ表示しています。" : "")
                + (r.Errors.Count > 0 ? "　読めなかったもの " + r.Errors.Count + "件。" : "")
                + (r.CloudSkipped > 0 ? "　OneDrive の「クラウドだけ」にある " + Fmt.Count(r.CloudSkipped) + "件は、ダウンロードしないよう中身を比べていません。" : "")
                + (r.Groups.Count == 0 ? "　重複はありませんでした。" : "")
                + (r.Groups.Count > 0 && !DupBasis.Has(r.Basis, DupBasis.Content) ? "　※中身は比べていません。移すときは、残すファイルと中身が同じものだけを移します。" : "");
            protectedFolder = prot;
            bMove.Enabled = !prot && r.Groups.Count > 0;
            UpdateTotal();
        }

        /// <summary>各まとまりで先頭（パスが短い・古いもの）を残し、ほかにチェック（on = false ならすべて外す）</summary>
        private void CheckAllButFirst(bool on)
        {
            filling = true;
            lv.BeginUpdate();
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    Row r = it.Tag as Row;
                    bool want = on && r != null && !r.Keep;
                    if (it.Checked != want) it.Checked = want;
                }
            }
            finally { lv.EndUpdate(); filling = false; }
            UpdateTotal();
        }

        private static SelectRow Map(ListViewItem it)
        {
            Row r = it.Tag as Row;
            if (r == null) return null;
            SelectRow s = new SelectRow();
            int k = r.Group.Files.IndexOf(r.Rel);
            s.Path = r.Rel; s.Size = r.Group.SizeAt(k);
            s.Mtime = k >= 0 ? r.Group.Mtimes[k] : 0;
            s.Tags.Add(r.Keep ? "残す候補" : "重複");
            string ext = System.IO.Path.GetExtension(r.Rel);
            s.Tags.Add("拡張子：" + (string.IsNullOrEmpty(ext) ? "（なし）" : ext.ToLowerInvariant()));
            return s;
        }

        /// <summary>条件で選ぶ。各まとまりで残すものの決め方（パスが短い・新しい・古い・条件に合う）も選べる。必ず1つは残す</summary>
        private void SelectByCondition()
        {
            if (result == null) return;
            using (SelectDialog d = new SelectDialog("フォルダの重複ファイル", SelectDialog.TagsOf(lv, Map), delegate(SelectCriteria c) { return SelectDialog.Count(lv, Map, c); }, true))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                filling = true;
                lv.BeginUpdate();
                try
                {
                    if (d.Action != SelectAction.KeepOne) SelectDialog.Apply(lv, Map, null, d.Action, d.Criteria);
                    foreach (ListViewGroup g in lv.Groups)
                    {
                        List<ListViewItem> items = new List<ListViewItem>();
                        foreach (ListViewItem it in g.Items) if (it.Tag is Row) items.Add(it);
                        if (items.Count == 0) continue;
                        List<SelectRow> rows = new List<SelectRow>();
                        foreach (ListViewItem it in items) rows.Add(Map(it));
                        int keep = KeepRule.Pick(rows, d.Keep, d.Criteria);
                        if (d.Action == SelectAction.KeepOne)
                        {
                            for (int k = 0; k < items.Count; k++)
                            {
                                bool want = k != keep && (d.Criteria.IsEmpty || d.Keep == KeepRule.Matching || d.Criteria.Matches(rows[k]));
                                if (items[k].Checked != want) items[k].Checked = want;
                            }
                        }
                        else
                        {
                            bool anyKept = false;
                            foreach (ListViewItem it in items) if (!it.Checked) anyKept = true;
                            if (!anyKept) items[keep].Checked = false;   // 必ず1つは残す
                        }
                    }
                }
                finally { lv.EndUpdate(); filling = false; }
                UpdateTotal();
            }
        }

        /// <summary>同じ中身のまとまりで、すべてにチェックすることはできない（必ず1つは残す）</summary>
        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling || e.NewValue != CheckState.Checked || e.Index < 0 || e.Index >= lv.Items.Count) return;
            ListViewGroup g = lv.Items[e.Index].Group;
            if (g == null) return;
            int unchecked0 = 0;
            foreach (ListViewItem it in g.Items)
                if (it.Index != e.Index && !it.Checked) unchecked0++;
            if (unchecked0 == 0)
            {
                e.NewValue = CheckState.Unchecked;
                Ui.Info(this, "同じとみなしたファイルは、まとまりごとに少なくとも1つ残してください。");
            }
        }

        private void UpdateTotal()
        {
            int n = 0; long bytes = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                Row r = it.Tag as Row;
                if (r == null) continue;
                n++; bytes += r.Group.SizeAt(r.Group.Files.IndexOf(r.Rel));
            }
            total.Text = "移すもの：" + Fmt.Count(n) + "件・" + Fmt.Size(bytes);
        }

        private void MoveChecked()
        {
            if (result == null) return;
            List<string> rels = new List<string>();
            long bytes = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                Row r = it.Tag as Row;
                if (r != null) { rels.Add(r.Rel); bytes += r.Group.SizeAt(r.Group.Files.IndexOf(r.Rel)); }
            }
            if (rels.Count == 0) { Ui.Info(this, "移すファイルにチェックを入れてください。"); return; }
            if (!Ui.Confirm(this, rels.Count + "件（" + Fmt.Size(bytes) + "）を、「" + FolderDup.TrashFolder(result.Root) + "\\日時」へ移します。\n消さずに移すだけなので、元に戻せます。よろしいですか？")) return;
            string root = result.Root;
            int moved = 0;
            List<FolderDupGroup> groups = result.Groups;
            List<string> errs = Ui.Busy<List<string>>(this, "中身を確かめて移しています…", delegate() { int m; List<string> e = FolderDup.MoveAway(root, groups, rels, out m); moved = m; return e; });
            // 移したものを一覧から外す
            HashSet<string> done = new HashSet<string>(rels, StringComparer.OrdinalIgnoreCase);
            foreach (string e in errs) { int k = e.IndexOf('：'); if (k > 0) done.Remove(e.Substring(0, k)); }
            foreach (FolderDupGroup g in result.Groups)
            {
                for (int i = g.Files.Count - 1; i >= 0; i--) if (done.Contains(g.Files[i])) g.RemoveAt(i);
            }
            result.Groups.RemoveAll(delegate(FolderDupGroup g) { return g.Files.Count < 2; });
            result.Recount();
            SetResult(result);
            if (errs.Count > 0) Ui.Error(this, moved + "件を移しました。できなかったものがあります：\n" + string.Join("\n", errs.GetRange(0, Math.Min(20, errs.Count)).ToArray()) + (errs.Count > 20 ? "\nほか " + (errs.Count - 20) + "件" : ""));
            else Ui.Info(this, moved + "件を移しました。");
        }

        private void OpenSelected()
        {
            if (result == null || lv.SelectedItems.Count == 0) return;
            Row r = lv.SelectedItems[0].Tag as Row;
            if (r == null) return;
            Ui.ShowInExplorer(this, new string[] { PathUtil.Combine(result.Root, r.Rel) });   // 消えていたら近いフォルダを開いて知らせる
        }

        private void Export()
        {
            if (result == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV（Excel で開けます）|*.csv";
                d.FileName = "自動コピー_フォルダ内の重複_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("同じとみなす基準：" + DupBasis.Describe(result.Basis) + (result.MinBytes > 0 ? "（" + Fmt.Size(result.MinBytes) + " より小さいファイルは除く）" : ""));
                sb.AppendLine("まとまり,扱い,ファイル,サイズ(バイト),更新日時,中身");
                int n = 0;
                foreach (FolderDupGroup g in result.Groups)
                {
                    n++;
                    for (int i = 0; i < g.Files.Count; i++)
                        sb.AppendLine(n + "," + (i == 0 ? "残す候補" : "重複") + "," + Q(PathUtil.Combine(result.Root, g.Files[i])) + "," + g.SizeAt(i) + "," + Fmt.LocalTime(g.Mtimes[i]) + "," + (g.SameContent ? "同じ" : "未確認"));
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
