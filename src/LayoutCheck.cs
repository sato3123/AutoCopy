using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// 画面の表示切れを自動で調べる（確認用。/screenshots のときだけ使う）。
    /// ・部品が親の枠からはみ出していないか（スクロールできない場所で）
    /// ・ボタン・チェックボックス・ラベルの文字が欠けていないか
    /// ・一覧の見出し、選択肢、数値欄の数字が欠けていないか
    /// ・位置を決めて置いた部品どうしが重なっていないか
    /// ・横スクロールが出ていないか
    /// ・小さい画面（1366×768）に収まるか
    /// </summary>
    public static class LayoutCheck
    {
        public const int SmallScreenWidth = 1366, SmallScreenHeight = 728;   // 768 からタスクバー 40 を引いた高さ

        public static List<string> Check(Form f)
        {
            List<string> issues = new List<string>();
            if (f.Width > SmallScreenWidth || f.Height > SmallScreenHeight)
                issues.Add("画面の大きさ " + f.Width + "×" + f.Height + " が小さい画面（1366×768）に収まらない");
            Walk(f, issues, f);
            // タブは1枚ずつ表示して調べる（表示していないタブの中は Visible = false のため）
            foreach (TabControl tc in FindAll<TabControl>(f))
            {
                if (!IsShown(tc)) continue;
                int keep = tc.SelectedIndex;
                for (int i = 0; i < tc.TabPages.Count; i++)
                {
                    if (i == keep) continue;
                    tc.SelectedIndex = i;
                    Application.DoEvents();
                    Walk(tc.TabPages[i], issues, f);
                }
                tc.SelectedIndex = keep;
                Application.DoEvents();
            }
            // 重複を除く
            List<string> uniq = new List<string>();
            foreach (string s in issues) if (!uniq.Contains(s)) uniq.Add(s);
            return uniq;
        }

        private static bool IsShown(Control c)
        {
            for (Control p = c; p != null; p = p.Parent) if (!p.Visible) return false;
            return true;
        }

        private static List<T> FindAll<T>(Control root) where T : Control
        {
            List<T> list = new List<T>();
            Stack<Control> st = new Stack<Control>();
            st.Push(root);
            while (st.Count > 0)
            {
                Control c = st.Pop();
                T t = c as T;
                if (t != null) list.Add(t);
                foreach (Control ch in c.Controls) st.Push(ch);
            }
            return list;
        }

        private static string Name(Control c)
        {
            string t = (c.Text ?? "").Replace("\r", " ").Replace("\n", " ");
            if (t.Length > 40) t = t.Substring(0, 40) + "…";
            string kind = c is CheckBox ? "チェック" : c is RadioButton ? "選択肢" : c is Button ? "ボタン" : c is Label ? "文字" :
                c is ListView ? "一覧" : c is ComboBox ? "選択欄" : c is NumericUpDown ? "数値欄" : c is TextBox ? "入力欄" : c.GetType().Name;
            // どこにあるか（グループやタブの名前）
            string where = "";
            for (Control p = c.Parent; p != null && !(p is Form); p = p.Parent)
            {
                if ((p is GroupBox || p is TabPage) && !string.IsNullOrEmpty(p.Text)) { where = p.Text; break; }
            }
            return (where.Length > 0 ? "［" + where + "］" : "") + kind + (t.Length > 0 ? "「" + t + "」" : "");
        }

        private static void Walk(Control parent, List<string> issues, Form f)
        {
            ScrollableControl sp = parent as ScrollableControl;
            bool scrolls = sp != null && sp.AutoScroll;
            if (scrolls && sp.HorizontalScroll.Visible && !(parent is ListView))
                issues.Add(Name(parent) + "：横スクロールが出る（幅が足りない）");

            List<Control> placed = new List<Control>();
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                CheckOne(c, parent, scrolls, issues);
                if (c.Dock == DockStyle.None && !(parent is FlowLayoutPanel) && !(parent is TableLayoutPanel) && !(parent is TabControl)) placed.Add(c);
                if (!(c is ListView) && !(c is NumericUpDown) && !(c is ComboBox) && !(c is TextBox) && !(c is DateTimePicker) && !(c is TabControl))
                    Walk(c, issues, f);
                else if (c is TabControl)
                {
                    TabControl tc = (TabControl)c;
                    if (tc.SelectedTab != null) Walk(tc.SelectedTab, issues, f);
                }
            }
            for (int i = 0; i < placed.Count; i++)
                for (int k = i + 1; k < placed.Count; k++)
                {
                    Rectangle a = placed[i].Bounds, b = placed[k].Bounds;
                    a.Inflate(-1, -1); b.Inflate(-1, -1);
                    if (a.IntersectsWith(b)) issues.Add(Name(placed[i]) + " と " + Name(placed[k]) + "：重なっている");
                }
        }

        private static void CheckOne(Control c, Control parent, bool parentScrolls, List<string> issues)
        {
            // 1) 親の枠からのはみ出し（スクロールできない親の中で）
            if (!parentScrolls && !(parent is Form && c.Dock != DockStyle.None) && !(parent is TabControl))
            {
                Size cs = parent.ClientSize;
                if (c.Right > cs.Width + 1 || c.Bottom > cs.Height + 1)
                {
                    int over = Math.Max(c.Right - cs.Width, c.Bottom - cs.Height);
                    if (c.Left < cs.Width && c.Top < cs.Height)
                        issues.Add(Name(c) + "：枠からはみ出して一部が見えない（" + over + "px）");
                    else
                        issues.Add(Name(c) + "：枠の外にあって見えない");
                }
            }

            // 2) 文字の欠け
            if (c is ButtonBase)
            {
                Size pref = c.GetPreferredSize(Size.Empty);
                if (pref.Width > c.Width + 2)
                {
                    // 折り返して表示できる高さがあれば欠けではない
                    Size wrap = TextRenderer.MeasureText(c.Text ?? "", c.Font, new Size(Math.Max(1, c.Width - 24), 0), TextFormatFlags.WordBreak);
                    if (wrap.Height > c.Height + 2)
                        issues.Add(Name(c) + "：文字が欠ける（必要な幅 " + pref.Width + "px／実際 " + c.Width + "px）");
                }
                if (pref.Height > c.Height + 4 && c.Width >= pref.Width)
                    issues.Add(Name(c) + "：文字の高さが足りない（必要 " + pref.Height + "px／実際 " + c.Height + "px）");
            }
            else if (c is Label)
            {
                Label l = (Label)c;
                if (!l.AutoEllipsis && !string.IsNullOrEmpty(l.Text) && l.Width > 0)
                {
                    Size need = TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(1, l.ClientSize.Width - l.Padding.Horizontal), 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    int avail = l.ClientSize.Height - l.Padding.Vertical;
                    if (need.Height > avail + 3)
                        issues.Add(Name(c) + "：文字が欠ける（必要な高さ " + need.Height + "px／実際 " + avail + "px）");
                }
            }
            else if (c is ListView)
            {
                ListView lv = (ListView)c;
                if (lv.View == View.Details)
                    foreach (ColumnHeader col in lv.Columns)
                    {
                        if (col.Width <= 0) continue;   // わざと隠している列
                        int need = TextRenderer.MeasureText(col.Text, lv.Font).Width + 12;
                        if (need > col.Width) issues.Add(Name(c) + "：見出し「" + col.Text + "」が欠ける（必要 " + need + "px／実際 " + col.Width + "px）");
                    }
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    int need = 0;
                    foreach (object o in cb.Items) need = Math.Max(need, TextRenderer.MeasureText(cb.GetItemText(o), cb.Font).Width);
                    need += SystemInformation.VerticalScrollBarWidth + 10;
                    if (need > cb.Width) issues.Add(Name(c) + "：選択肢の文字が欠ける（必要 " + need + "px／実際 " + cb.Width + "px）");
                }
                if (cb.Height < TextRenderer.MeasureText("あ", cb.Font).Height) issues.Add(Name(c) + "：高さが足りない");
            }
            else if (c is NumericUpDown)
            {
                NumericUpDown n = (NumericUpDown)c;
                string longest = n.Maximum.ToString("0");
                int need = TextRenderer.MeasureText(longest, n.Font).Width + SystemInformation.VerticalScrollBarWidth + 10;
                if (need > n.Width) issues.Add(Name(c) + "：最大値 " + longest + " を入れると数字が欠ける（必要 " + need + "px／実際 " + n.Width + "px）");
            }
            else if (c is TextBox)
            {
                TextBox t = (TextBox)c;
                if (!t.Multiline && t.Height < TextRenderer.MeasureText("あ", t.Font).Height) issues.Add(Name(c) + "：入力欄の高さが足りない");
            }
        }

        /// <summary>結果を Markdown の表にする</summary>
        public static string Report(List<KeyValuePair<string, List<string>>> results, string scale)
        {
            StringBuilder sb = new StringBuilder();
            int total = 0;
            foreach (KeyValuePair<string, List<string>> r in results) total += r.Value.Count;
            sb.AppendLine("# 表示切れの確認（文字の大きさ " + scale + "）");
            sb.AppendLine();
            sb.AppendLine("調べた画面 " + results.Count + "・問題 " + total + "件");
            sb.AppendLine();
            sb.AppendLine("| 画面 | 結果 | 内容 |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (KeyValuePair<string, List<string>> r in results)
            {
                if (r.Value.Count == 0) sb.AppendLine("| " + r.Key + " | OK | |");
                else foreach (string s in r.Value) sb.AppendLine("| " + r.Key + " | NG | " + s.Replace("|", "/") + " |");
            }
            return sb.ToString();
        }
    }
}
