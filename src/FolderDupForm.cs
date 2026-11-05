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
        private readonly CheckBox cSub;
        private readonly NumericUpDown nMin;
        private readonly ComboBox cbUnit;
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
            Ui.SetupForm(this, "フォルダ内の重複ファイル検索", 1040, 660);
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
            nMin = Ui.N(0, 1000000, 0, 80);
            cbUnit = Ui.Combo(60, "KB", "MB");
            Button search = Ui.B("検索", delegate { Search(); });
            Btn.MakePrimary(search);
            Btn.SetIcon(fromJob, "folder", Ui.Quiet);
            summary = Ui.L("フォルダを選んで「検索」を押してください。");
            Ui.FitToForm(summary, 980);
            protectNote = Ui.L("");
            protectNote.ForeColor = Ui.Warn;
            Ui.FitToForm(protectNote, 980);
            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("フォルダ内の重複ファイルを探す"),
                Ui.Hint("コピーとは関係なく、指定したフォルダの中で中身が同じファイルを探します。探すだけでは、ファイルは何も変わりません。"),
                Ui.WrapRow(Ui.L("フォルダ"), tFolder, browse, fromJob),
                Ui.WrapRow(cSub, Ui.L("大きさ"), nMin, cbUnit, Ui.L("以上（0 = すべて）"), search),
                summary, protectNote);
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(10, 8, 10, 4);
            Resize += delegate { foreach (Control c in top.Controls) if (c is FlowLayoutPanel) c.MaximumSize = new Size(Math.Max(Ui.S(300), ClientSize.Width - Ui.S(30)), 0); };

            lv = Ui.List("扱い|70", "ファイル（フォルダの中の場所）|480", "サイズ|90", "更新日時|130");
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
            Panel bar = Ui.CheckAllBar(lv, "各まとまりで1つ残して、ほかにすべてチェック／すべて外す", delegate(bool on) { CheckAllButFirst(on); },
                Ui.B("条件で選ぶ…", delegate { SelectByCondition(); }));

            total = Ui.L("");
            total.Font = Ui.Bold;
            bMove = Ui.B("チェックしたものを移す（消さずに「_AutoCopy\\重複で削除」へ）…", delegate { MoveChecked(); });
            Btn.MakeDanger(bMove);
            Label note = Ui.Hint("移したファイルは、そのフォルダの「_AutoCopy\\重複で削除\\日時」に入ります（隠しフォルダ）。元に戻すときは、そこから元の場所へ戻してください。自動コピーのコピー元のファイルは移しません。");
            Ui.FitToForm(note, 980);
            FlowLayoutPanel bottom = Ui.Column(total, note);
            bottom.Dock = DockStyle.Bottom;
            bottom.Padding = new Padding(12, 6, 12, 6);

            Controls.Add(lv);
            Controls.Add(bar);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), Btn.Gap(), bMove, Btn.Gap(),
                Ui.B("CSVに書き出す", delegate { Export(); }),
                Ui.B("移したファイルのフォルダを開く", delegate { string r = tFolder.Text.Trim(); if (r.Length > 0) Ui.OpenFolder(FolderDup.TrashFolder(r)); })));
            bMove.Enabled = false;
        }

        private long MinBytes { get { return (long)nMin.Value * (cbUnit.SelectedIndex == 1 ? 1024L * 1024 : 1024L); } }

        public void Search()
        {
            string root = tFolder.Text.Trim();
            if (root.Length == 0) { Ui.Info(this, "フォルダを選んでください。"); return; }
            root = NetAccess.ToUncIfMapped(root);
            bool sub = cSub.Checked;
            long min = MinBytes;
            bool stopped;
            FolderDupResult r;
            try
            {
                r = Ui.BusyCancel<FolderDupResult>(this, "「" + root + "」の中で、中身が同じファイルを探しています（読むだけです）…", delegate(System.Threading.CancellationToken ct, BusyProgress bp)
                {
                    try { NetAccess.Ensure(root, 1, 5, ct, null); } catch (Exception) { }
                    return FolderDup.Find(root, sub, min, ct, delegate(string t) { bp.Text = t; });
                }, out stopped);
            }
            catch (Exception ex) { Ui.Error(this, "探せませんでした：" + ex.Message); return; }
            if (stopped) { summary.Text = "中止しました。"; return; }
            SetResult(r);
            Logger.Write(null, "DUPCHECK", "フォルダ内の重複検索：" + root + "　調べたファイル " + r.Scanned + "件・重複 " + r.Groups.Count + "まとまり（" + r.WastedFiles + "件・" + Fmt.Size(r.WastedBytes) + "）", r.WastedBytes, -1);
        }

        /// <summary>結果を表示する（画面の確認用にも使う）</summary>
        public void SetResult(FolderDupResult r)
        {
            result = r;
            tFolder.Text = r.Root;
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
                ListViewGroup lg = new ListViewGroup("中身が同じ " + g.Files.Count + "件（1件 " + Fmt.Size(g.Size) + "・1つ残すと " + Fmt.Size(g.Size * (g.Files.Count - 1)) + " 空く）");
                lv.Groups.Add(lg);
                for (int i = 0; i < g.Files.Count; i++)
                {
                    Row row = new Row(); row.Group = g; row.Rel = g.Files[i]; row.Keep = i == 0;
                    ListViewItem it = new ListViewItem(i == 0 ? "残す候補" : "重複", lg);
                    it.SubItems.Add(g.Files[i]);
                    it.SubItems.Add(Fmt.Size(g.Size));
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
            summary.Text = "調べたファイル " + Fmt.Count(r.Scanned) + "件（" + Fmt.Size(r.ScannedBytes) + "）→ 中身が同じもの " + Fmt.Count(r.Groups.Count) + "まとまり。"
                + "1つずつ残すと " + Fmt.Count(r.WastedFiles) + "件・" + Fmt.Size(r.WastedBytes) + " 空きます。"
                + (shownGroups < r.Groups.Count ? "　※件数が多いため、空く容量の大きい順に " + Fmt.Count(shownGroups) + "まとまりだけ表示しています。" : "")
                + (r.Errors.Count > 0 ? "　読めなかったもの " + r.Errors.Count + "件。" : "")
                + (r.Groups.Count == 0 ? "　重複はありませんでした。" : "");
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
            s.Path = r.Rel; s.Size = r.Group.Size;
            int k = r.Group.Files.IndexOf(r.Rel);
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
            using (SelectDialog d = new SelectDialog("フォルダ内の重複", SelectDialog.TagsOf(lv, Map), delegate(SelectCriteria c) { return SelectDialog.Count(lv, Map, c); }, true))
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
                Ui.Info(this, "中身が同じファイルは、少なくとも1つ残してください。");
            }
        }

        private void UpdateTotal()
        {
            int n = 0; long bytes = 0;
            foreach (ListViewItem it in lv.CheckedItems)
            {
                Row r = it.Tag as Row;
                if (r == null) continue;
                n++; bytes += r.Group.Size;
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
                if (r != null) { rels.Add(r.Rel); bytes += r.Group.Size; }
            }
            if (rels.Count == 0) { Ui.Info(this, "移すファイルにチェックを入れてください。"); return; }
            if (!Ui.Confirm(this, rels.Count + "件（" + Fmt.Size(bytes) + "）を、「" + FolderDup.TrashFolder(result.Root) + "\\日時」へ移します。\n消さずに移すだけなので、元に戻せます。よろしいですか？")) return;
            string root = result.Root;
            int moved = 0;
            List<string> errs = Ui.Busy<List<string>>(this, "移しています…", delegate() { int m; List<string> e = FolderDup.MoveAway(root, rels, out m); moved = m; return e; });
            // 移したものを一覧から外す
            HashSet<string> done = new HashSet<string>(rels, StringComparer.OrdinalIgnoreCase);
            foreach (string e in errs) { int k = e.IndexOf('：'); if (k > 0) done.Remove(e.Substring(0, k)); }
            foreach (FolderDupGroup g in result.Groups)
            {
                for (int i = g.Files.Count - 1; i >= 0; i--) if (done.Contains(g.Files[i])) { g.Files.RemoveAt(i); g.Mtimes.RemoveAt(i); }
            }
            result.Groups.RemoveAll(delegate(FolderDupGroup g) { return g.Files.Count < 2; });
            result.WastedBytes = 0; result.WastedFiles = 0;
            foreach (FolderDupGroup g in result.Groups) { result.WastedBytes += g.Size * (g.Files.Count - 1); result.WastedFiles += g.Files.Count - 1; }
            SetResult(result);
            if (errs.Count > 0) Ui.Error(this, moved + "件を移しました。できなかったものがあります：\n" + string.Join("\n", errs.GetRange(0, Math.Min(20, errs.Count)).ToArray()) + (errs.Count > 20 ? "\nほか " + (errs.Count - 20) + "件" : ""));
            else Ui.Info(this, moved + "件を移しました。");
        }

        private void OpenSelected()
        {
            if (result == null || lv.SelectedItems.Count == 0) return;
            Row r = lv.SelectedItems[0].Tag as Row;
            if (r == null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + PathUtil.Combine(result.Root, r.Rel) + "\""); }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
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
                sb.AppendLine("まとまり,扱い,ファイル,サイズ(バイト),更新日時");
                int n = 0;
                foreach (FolderDupGroup g in result.Groups)
                {
                    n++;
                    for (int i = 0; i < g.Files.Count; i++)
                        sb.AppendLine(n + "," + (i == 0 ? "残す候補" : "重複") + "," + Q(PathUtil.Combine(result.Root, g.Files[i])) + "," + g.Size + "," + Fmt.LocalTime(g.Mtimes[i]));
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
