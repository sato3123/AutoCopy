using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// 「条件で選ぶ」画面：名前・場所の文字、拡張子、大きさ、更新日時、種類（ジョブ）で、一覧の行をまとめてチェックする／外す。
    /// 重複の画面では「各まとまりで1つ残して、ほかにチェック」（残すものの決め方を選べる）も使える。
    /// </summary>
    public class SelectDialog : Form
    {
        private readonly TextBox tWords, tExt;
        private readonly ComboBox cbWordsMode, cbMinUnit, cbMaxUnit;
        private readonly CheckBox cMin, cMax, cBefore, cAfter;
        private readonly NumericUpDown nMin, nMax;
        private readonly DateTimePicker dBefore, dAfter;
        private readonly CheckedListBox clTags;
        private readonly Label lCount;
        private readonly RadioButton rShort, rNew, rOld, rMatch;
        private readonly Func<SelectCriteria, int> counter;
        private readonly bool dupMode;

        public SelectAction Action = SelectAction.None;
        public SelectCriteria Criteria;
        public string Keep = KeepRule.ShortPath;

        /// <summary>前回の条件（画面を開き直したときに引き継ぐ）</summary>
        private static SelectCriteria last;
        private static string lastKeep = KeepRule.ShortPath;

        /// <param name="tags">「種類」で選べる値（なければ null）</param>
        /// <param name="counter">条件に合う行の数（画面に出す）</param>
        /// <param name="dupMode">重複の画面（「各まとまりで1つ残して」を使う）</param>
        public SelectDialog(string what, IEnumerable<string> tags, Func<SelectCriteria, int> counter, bool dupMode)
        {
            this.counter = counter;
            this.dupMode = dupMode;
            Ui.SetupDialog(this, "条件で選ぶ - " + what, 640, 560);
            FlowLayoutPanel b = Ui.Body();

            tWords = Ui.T("", 260);
            cbWordsMode = Ui.Combo(110, "を含む", "を含まない");
            tExt = Ui.T("", 160);
            cMin = Ui.C("", false); nMin = Ui.N(0, 100000000, 10, 96); cbMinUnit = Ui.Combo(56, "KB", "MB", "GB"); cbMinUnit.SelectedIndex = 1;
            cMax = Ui.C("", false); nMax = Ui.N(0, 100000000, 100, 96); cbMaxUnit = Ui.Combo(56, "KB", "MB", "GB"); cbMaxUnit.SelectedIndex = 1;
            cBefore = Ui.C("", false); dBefore = DatePicker(DateTime.Today.AddYears(-1));
            cAfter = Ui.C("", false); dAfter = DatePicker(DateTime.Today.AddMonths(-1));
            b.Controls.Add(Ui.Section("条件（指定したものすべてに当てはまるもの）",
                Ui.WrapRow(Ui.L("名前・場所に", 110), tWords, cbWordsMode),
                Ui.WrapRow(Ui.L("", 110), Ui.Hint("「;」で区切ると、どれか1つ")),
                Ui.WrapRow(Ui.L("拡張子", 110), tExt, Ui.Hint("例：.tmp; .bak")),
                Ui.WrapRow(Ui.L("大きさ", 110), cMin, nMin, cbMinUnit, Ui.L("以上"), cMax, nMax, cbMaxUnit, Ui.L("以下")),
                Ui.WrapRow(Ui.L("更新日時", 110), cBefore, dBefore, Ui.L("より前"), cAfter, dAfter, Ui.L("以降"))));

            List<string> tagList = new List<string>();
            if (tags != null) foreach (string t in tags) if (!string.IsNullOrEmpty(t) && !tagList.Contains(t)) tagList.Add(t);
            clTags = new CheckedListBox();
            clTags.CheckOnClick = true;
            clTags.IntegralHeight = false;
            clTags.Width = Ui.MaxWidth(560);
            clTags.Height = Ui.S(Math.Min(6, Math.Max(2, tagList.Count)) * 20 + 6);
            clTags.Font = Ui.Base;
            foreach (string t in tagList) clTags.Items.Add(t, true);
            if (tagList.Count > 0)
                b.Controls.Add(Ui.Section("種類（チェックしたものだけ）", clTags,
                    Ui.Row(Ui.B("すべて", delegate { for (int i = 0; i < clTags.Items.Count; i++) clTags.SetItemChecked(i, true); }),
                        Ui.B("なし", delegate { for (int i = 0; i < clTags.Items.Count; i++) clTags.SetItemChecked(i, false); }))));

            lCount = Ui.L("");
            lCount.Font = Ui.Bold;
            b.Controls.Add(lCount);

            Button bCheck = Ui.B("条件に合うものにチェック", delegate { Done(SelectAction.Check); });
            Button bUncheck = Ui.B("条件に合うもののチェックを外す", delegate { Done(SelectAction.Uncheck); });
            Button bOnly = Ui.B("条件に合うものだけチェック（ほかは外す）", delegate { Done(SelectAction.Only); });
            Btn.SetIcon(bUncheck, "close", Ui.Quiet);
            Btn.SetIcon(bOnly, "filter", Ui.Quiet);
            b.Controls.Add(Ui.WrapRow(bCheck, bUncheck, bOnly));

            rShort = Ui.R("パスが短いもの", true);
            rNew = Ui.R("更新日時が新しいもの", false);
            rOld = Ui.R("更新日時が古いもの", false);
            rMatch = Ui.R("上の条件に合うもの（例：残したい場所の名前）", false);
            if (dupMode)
            {
                Button bKeep = Ui.B("各まとまりで1つ残して、ほかにチェック", delegate { Done(SelectAction.KeepOne); });
                b.Controls.Add(Ui.Section("重複：各まとまりで残すもの",
                    Ui.Column(rShort, rNew, rOld, rMatch),
                    Ui.Hint("条件を指定したときは、残すもの以外のうち、条件に合うものだけにチェックします（「上の条件に合うもの」を残す場合は、ほかのすべてにチェック）。"),
                    bKeep));
            }
            b.Controls.Add(Ui.Hint("同じ中身のファイルは、どの選び方でも必ず1つは残ります（重複の画面）。選んだ後で、一覧のチェックを1つずつ変えることもできます。"));

            Controls.Add(b);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); })));

            EventHandler h = delegate { UpdateCount(); };
            foreach (Control c in new Control[] { tWords, tExt }) c.TextChanged += h;
            foreach (ComboBox c in new ComboBox[] { cbWordsMode, cbMinUnit, cbMaxUnit }) c.SelectedIndexChanged += h;
            foreach (CheckBox c in new CheckBox[] { cMin, cMax, cBefore, cAfter }) c.CheckedChanged += h;
            nMin.ValueChanged += h; nMax.ValueChanged += h; dBefore.ValueChanged += h; dAfter.ValueChanged += h;
            clTags.ItemCheck += delegate { BeginInvoke(new Action(UpdateCount)); };
            Restore();
            UpdateCount();
        }

        private static DateTimePicker DatePicker(DateTime v)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Short;
            d.Width = Math.Max(Ui.S(120), Ui.TextWidth("0000/00/00", Ui.Base) + Ui.S(44));
            d.Value = v;
            return d;
        }

        private static long Unit(ComboBox c) { return c.SelectedIndex == 2 ? 1024L * 1024 * 1024 : c.SelectedIndex == 1 ? 1024L * 1024 : 1024L; }

        public SelectCriteria Read()
        {
            SelectCriteria c = new SelectCriteria();
            c.Words = SelectCriteria.Split(tWords.Text);
            c.WordsNot = cbWordsMode.SelectedIndex == 1;
            foreach (string e in SelectCriteria.Split(tExt.Text)) c.Exts.Add(e.StartsWith(".") ? e : "." + e);
            if (cMin.Checked) c.MinSize = (long)nMin.Value * Unit(cbMinUnit);
            if (cMax.Checked) c.MaxSize = (long)nMax.Value * Unit(cbMaxUnit);
            if (cBefore.Checked) c.Before = dBefore.Value.Date;
            if (cAfter.Checked) c.After = dAfter.Value.Date;
            if (clTags.Items.Count > 0 && clTags.CheckedItems.Count < clTags.Items.Count)
            {
                c.Tags = new HashSet<string>();
                foreach (object o in clTags.CheckedItems) c.Tags.Add((string)o);
            }
            return c;
        }

        private void UpdateCount()
        {
            nMin.Enabled = cbMinUnit.Enabled = cMin.Checked;
            nMax.Enabled = cbMaxUnit.Enabled = cMax.Checked;
            dBefore.Enabled = cBefore.Checked;
            dAfter.Enabled = cAfter.Checked;
            if (counter == null) return;
            SelectCriteria c = Read();
            int n = 0;
            try { n = counter(c); } catch (Exception) { }
            lCount.Text = (c.IsEmpty ? "条件なし（すべて）：" : "条件に合うもの：") + Fmt.Count(n) + "件";
        }

        private void Done(SelectAction a)
        {
            Criteria = Read();
            Action = a;
            Keep = rNew.Checked ? KeepRule.Newest : rOld.Checked ? KeepRule.Oldest : rMatch.Checked ? KeepRule.Matching : KeepRule.ShortPath;
            last = Criteria; lastKeep = Keep;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Restore()
        {
            if (last == null) return;
            SelectCriteria c = last;
            tWords.Text = string.Join("; ", c.Words.ToArray());
            cbWordsMode.SelectedIndex = c.WordsNot ? 1 : 0;
            tExt.Text = string.Join("; ", c.Exts.ToArray());
            if (c.MinSize >= 0) { cMin.Checked = true; nMin.Value = Math.Min(nMin.Maximum, c.MinSize / Unit(cbMinUnit)); }
            if (c.MaxSize >= 0) { cMax.Checked = true; nMax.Value = Math.Min(nMax.Maximum, c.MaxSize / Unit(cbMaxUnit)); }
            if (c.Before.HasValue) { cBefore.Checked = true; dBefore.Value = c.Before.Value; }
            if (c.After.HasValue) { cAfter.Checked = true; dAfter.Value = c.After.Value; }
            if (c.Tags != null)
                for (int i = 0; i < clTags.Items.Count; i++) clTags.SetItemChecked(i, c.Tags.Contains((string)clTags.Items[i]));
            rShort.Checked = lastKeep == KeepRule.ShortPath;
            rNew.Checked = lastKeep == KeepRule.Newest;
            rOld.Checked = lastKeep == KeepRule.Oldest;
            rMatch.Checked = lastKeep == KeepRule.Matching;
        }

        /// <summary>確認用：前回の条件を消す</summary>
        public static void ResetLast() { last = null; lastKeep = KeepRule.ShortPath; }

        /// <summary>
        /// 一覧に当てはめる（Check / Uncheck / Only）。map が null を返す行（見出し・選べない行）は変えない。
        /// canCheck が false の行にはチェックしない。変わった行の数を返す。
        /// </summary>
        public static int Apply(ListView lv, Func<ListViewItem, SelectRow> map, Func<ListViewItem, bool> canCheck, SelectAction a, SelectCriteria c)
        {
            int changed = 0;
            lv.BeginUpdate();
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    SelectRow r = map(it);
                    if (r == null) continue;
                    bool m = c.Matches(r);
                    bool want = it.Checked;
                    if (a == SelectAction.Check && m) want = true;
                    else if (a == SelectAction.Uncheck && m) want = false;
                    else if (a == SelectAction.Only) want = m;
                    if (want && canCheck != null && !canCheck(it)) want = false;
                    if (want != it.Checked) { it.Checked = want; changed++; }
                }
            }
            finally { lv.EndUpdate(); }
            return changed;
        }

        /// <summary>条件に合う行の数</summary>
        public static int Count(ListView lv, Func<ListViewItem, SelectRow> map, SelectCriteria c)
        {
            int n = 0;
            foreach (ListViewItem it in lv.Items) { SelectRow r = map(it); if (r != null && c.Matches(r)) n++; }
            return n;
        }

        /// <summary>一覧の「種類」の候補（map の Tags を集める）</summary>
        public static List<string> TagsOf(ListView lv, Func<ListViewItem, SelectRow> map)
        {
            List<string> l = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (ListViewItem it in lv.Items)
            {
                SelectRow r = map(it);
                if (r == null) continue;
                foreach (string t in r.Tags) if (seen.Add(t)) l.Add(t);
            }
            return l;
        }
    }
}
