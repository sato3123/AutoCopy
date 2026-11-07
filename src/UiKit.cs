using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>画面部品を作る小さなヘルパー（デザイナー不要で組み立てる）</summary>
    public static class Ui
    {
        /// <summary>
        /// 文字の拡大率（確認用）。環境変数 AUTOCOPY_FONTSCALE=1.25 などで、
        /// Windows 7 の 125%・150% 表示（文字だけが大きくなる）と同じ状態を作って表示切れを調べる。通常は 1。
        /// </summary>
        public static readonly float TextScale = ReadScale();
        public static readonly Font Base = new Font("Meiryo UI", 9f * TextScale);
        public static readonly Font Bold = new Font("Meiryo UI", 9f * TextScale, FontStyle.Bold);
        public static readonly Font Title = new Font("Meiryo UI", 12f * TextScale, FontStyle.Bold);
        public static readonly Font Small = new Font("Meiryo UI", 8.25f * TextScale);

        /// <summary>
        /// 画面の部品の大きさの倍率。Windows 7 の 125% 表示（XP 形式の拡大：文字だけが大きくなる）では 1.25。
        /// Windows 8 以降・Windows 7 の 150% は Windows が画面ごと拡大するため 1。
        /// </summary>
        public static readonly float Factor = ReadFactor();

        private static float ReadFactor()
        {
            float dpi = 1f;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f; } catch (Exception) { }
            return Math.Max(1f, Math.Min(3f, dpi * TextScale));
        }

        /// <summary>100% 表示のときの大きさ（px）を、今の表示倍率に合わせる</summary>
        public static int S(int px) { return (int)Math.Round(px * Factor); }

        /// <summary>
        /// 折り返す文章などの最大幅。表示倍率に合わせて広げるが、画面が狭いときは画面に収まる幅までにする
        /// （左の見出しの列とスクロールバーの分を残す）。
        /// </summary>
        public static int MaxWidth(int px)
        {
            int w = S(px);
            try { w = Math.Min(w, Ui.WorkArea.Width - S(220)); } catch (Exception) { }
            return Math.Max(S(200), w);
        }

        /// <summary>
        /// 表示したときに、画面（フォーム）の右端を越えないよう最大幅を縮める。
        /// 文字が大きい・画面が狭いときに横スクロールや欠けが出ないようにする（文章は折り返す）。
        /// </summary>
        public static void FitToForm(Control c, int px)
        {
            c.MaximumSize = new Size(MaxWidth(px), 0);
            EventHandler fit = null;
            fit = delegate
            {
                Form f = c.FindForm();
                if (f == null || c.Parent == null) return;
                int left = f.PointToClient(c.Parent.PointToScreen(c.Location)).X;
                int avail = f.ClientSize.Width - left - S(36);   // スクロールバーと余白の分
                int w = Math.Max(S(160), Math.Min(MaxWidth(px), avail));
                if (c.MaximumSize.Width != w) { c.MaximumSize = new Size(w, 0); if (c.Parent != null) c.Parent.PerformLayout(); }
            };
            c.HandleCreated += fit;
            c.VisibleChanged += delegate { if (c.Visible) fit(c, EventArgs.Empty); };
        }

        public static void Relayout(Control root)
        {
            List<Control> all = new List<Control>();
            Stack<Control> st = new Stack<Control>();
            st.Push(root);
            while (st.Count > 0)
            {
                Control c = st.Pop();
                all.Add(c);
                foreach (Control ch in c.Controls) st.Push(ch);
            }
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i] is ScrollableControl && all[i].Controls.Count > 0) all[i].PerformLayout();
        }

        /// <summary>文字の幅（px）</summary>
        public static int TextWidth(string text, Font font)
        {
            return TextRenderer.MeasureText(text ?? "", font).Width;
        }

        private static float ReadScale()
        {
            try
            {
                string v = Environment.GetEnvironmentVariable("AUTOCOPY_FONTSCALE");
                float f;
                if (!string.IsNullOrEmpty(v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f))
                    return Math.Max(1f, Math.Min(2f, f));
            }
            catch (Exception) { }
            return 1f;
        }
        public static readonly Color Accent = Color.FromArgb(0, 95, 184);
        public static readonly Color Quiet = Color.FromArgb(85, 85, 85);
        public static readonly Color Ok = Color.FromArgb(15, 123, 15);
        public static readonly Color Warn = Color.FromArgb(138, 75, 0);
        public static readonly Color Err = Color.FromArgb(196, 43, 28);
        public static readonly Color WarnBack = Color.FromArgb(253, 246, 236);
        public static readonly Color InfoBack = Color.FromArgb(234, 243, 251);

        public static void SetupForm(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.Font = Base;
            f.Icon = IconFactory.App;
            f.StartPosition = FormStartPosition.CenterScreen;
            // 小さい画面でもはみ出さないよう、作業領域（タスクバーを除いた画面）に収める。中身はスクロールできる
            Size want = new Size(S(w), S(h));
            try
            {
                Rectangle wa = Ui.WorkArea;
                want.Width = Math.Min(want.Width, wa.Width - 24);
                want.Height = Math.Min(want.Height, wa.Height - 48);
            }
            catch (Exception) { }
            f.ClientSize = want;
            // 表示した直後に、部品の大きさ（文字の大きさで変わる）に合わせて並びを奥から順に直す
            f.Shown += delegate { Relayout(f); };
            f.BackColor = Color.FromArgb(249, 249, 249);
        }

        public static void SetupDialog(Form f, string title, int w, int h)
        {
            SetupForm(f, title, w, h);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = true;
        }

        public static Label L(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(3, 7, 3, 3);
            return l;
        }

        public static Label L(string text, int width)
        {
            Label l = L(text);
            l.AutoSize = false;
            l.Width = Math.Max(S(width), TextWidth(text, Base) + 8);   // 文字が大きくても欠けないように
            l.Height = Math.Max(S(22), TextRenderer.MeasureText("あ", Base).Height + 6);
            return l;
        }

        public static Label Hint(string text)
        {
            Label l = L(text);
            l.ForeColor = Quiet;
            l.Font = Small;
            FitToForm(l, 2000);   // 長い説明は画面の幅で折り返す
            return l;
        }

        public static Label Wrap(string text, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(3, 4, 3, 4);
            FitToForm(l, width);
            return l;
        }

        public static Label Heading(string text)
        {
            Label l = L(text);
            l.Font = Title;
            l.Margin = new Padding(3, 3, 3, 6);
            return l;
        }

        public static Button B(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 2, 6, 2);
            b.MinimumSize = new Size(0, S(28));
            b.UseVisualStyleBackColor = true;
            if (onClick != null) b.Click += onClick;
            Btn.AutoIcon(b);   // 文字に合うアイコン（全画面で同じ）
            return b;
        }

        public static TextBox T(string text, int width)
        {
            TextBox t = new TextBox();
            t.Text = text ?? "";
            t.Width = S(width);
            t.Margin = new Padding(3, 4, 3, 3);
            return t;
        }

        public static CheckBox C(string text, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.AutoSize = true;
            c.Margin = new Padding(3, 6, 3, 3);
            return c;
        }

        public static RadioButton R(string text, bool value)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.Checked = value;
            r.AutoSize = true;
            r.Margin = new Padding(3, 6, 12, 3);
            return r;
        }

        public static NumericUpDown N(int min, int max, int value, int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            // 最大の桁数まで入れても数字が欠けない幅にする
            n.Width = Math.Max(S(width), TextWidth(Math.Max(Math.Abs(max), Math.Abs(min)).ToString("0"), Base) + SystemInformation.VerticalScrollBarWidth + 14);
            n.TextAlign = HorizontalAlignment.Right;
            n.Margin = new Padding(3, 4, 3, 3);
            return n;
        }

        public static ComboBox Combo(int width, params string[] items)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = S(width);
            c.Items.AddRange(items);
            // 表示する前に、いちばん長い選択肢が入る幅にする（後から追加した選択肢も含む）
            c.HandleCreated += delegate { FitCombo(c); };
            // 文字の大きさで高さが変わったら、入れ物の並びを直す
            c.SizeChanged += delegate { for (Control p = c.Parent; p != null && !(p is Form); p = p.Parent) p.PerformLayout(); };
            if (items.Length > 0) c.SelectedIndex = 0;
            c.Margin = new Padding(3, 4, 3, 3);
            c.Font = Base;
            FitCombo(c);
            return c;
        }

        public static void FitCombo(ComboBox c)
        {
            int need = 0;
            foreach (object o in c.Items) need = Math.Max(need, TextWidth(c.GetItemText(o), c.Font));
            need += SystemInformation.VerticalScrollBarWidth + 12;
            if (need > c.Width) c.Width = Math.Min(need, S(600));
            // 大きくなった分だけ並びを直す（入れ物が自動の大きさなら、上の入れ物まで）
            for (Control p = c.Parent; p != null && !(p is Form); p = p.Parent) p.PerformLayout();
            if (c.DropDownWidth < need) c.DropDownWidth = need;
        }

        public static FlowLayoutPanel Row(params Control[] controls)
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.LeftToRight;
            p.WrapContents = false;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Margin = new Padding(0);
            p.Controls.AddRange(controls);
            // 選択欄は文字が大きいと表示後に少し高くなることがあるため、下に少し余白を取る
            foreach (Control c in controls) if (c is ComboBox) { p.Padding = new Padding(0, 0, 0, S(3)); break; }
            return p;
        }

        /// <summary>横に並べ、幅が足りなければ折り返す行（MaximumSize で幅を決める）</summary>
        public static FlowLayoutPanel WrapRow(params Control[] controls)
        {
            FlowLayoutPanel p = Row(controls);
            p.WrapContents = true;
            return p;
        }

        public static FlowLayoutPanel Column(params Control[] controls)
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.TopDown;
            p.WrapContents = false;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Margin = new Padding(0);
            p.Controls.AddRange(controls);
            return p;
        }

        /// <summary>見出し付きの枠</summary>
        public static GroupBox Section(string title, params Control[] rows)
        {
            GroupBox g = new GroupBox();
            g.Text = title;
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.Padding = new Padding(8, 6, 8, 8);
            g.Margin = new Padding(3, 3, 3, 8);
            FlowLayoutPanel inner = Column(rows);
            inner.Location = new Point(S(10), TextRenderer.MeasureText("あ", Base).Height + S(8));
            g.Controls.Add(inner);
            return g;
        }

        /// <summary>縦に並べるスクロール可能な本文</summary>
        public static FlowLayoutPanel Body()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.TopDown;
            p.WrapContents = false;
            p.AutoScroll = true;
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(12);
            AddScrollSpacer(p);
            return p;
        }

        public const string ScrollSpacerTag = "scroll-spacer";

        /// <summary>
        /// スクロールできる縦の並びの最後に、空白の部品を置く（常に最後に保つ）。
        /// Windows のスクロールは、上の余白や最後の部品の下の余白を数えないため、いちばん下までスクロールしても
        /// 最後の部品の下が数ピクセル欠ける。その分を空白で足して、最後の部品まで必ず見えるようにする。
        /// </summary>
        public static void AddScrollSpacer(FlowLayoutPanel p)
        {
            Panel spacer = new Panel();
            spacer.Tag = ScrollSpacerTag;
            spacer.Width = 1;
            spacer.Height = p.Padding.Top + p.Padding.Bottom + S(12);
            spacer.Margin = new Padding(0);
            spacer.TabStop = false;
            p.Controls.Add(spacer);
            p.ControlAdded += delegate(object s, ControlEventArgs e)
            {
                if (e.Control != spacer && p.Controls.Contains(spacer)) p.Controls.SetChildIndex(spacer, p.Controls.Count - 1);
            };
        }

        /// <summary>下部のボタン帯（右寄せ）</summary>
        public static FlowLayoutPanel Footer(params Control[] buttons)
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.RightToLeft;
            p.Dock = DockStyle.Bottom;
            p.AutoSize = true;   // 文字が大きいときはボタンに合わせて高くする
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.MinimumSize = new Size(0, S(48));
            p.Padding = new Padding(8, 8, 8, 4);
            p.BackColor = Color.FromArgb(243, 243, 243);
            p.Controls.AddRange(buttons);
            return p;
        }

        public static Panel Banner(Color back, params Control[] controls)
        {
            FlowLayoutPanel p = Row(controls);
            p.AutoSize = true;
            p.Padding = new Padding(8, 4, 8, 4);
            p.BackColor = back;
            p.Margin = new Padding(3, 3, 3, 8);
            return p;
        }

        /// <summary>
        /// 画面の作業領域。確認用に環境変数 AUTOCOPY_SCREEN=1093x582 などで狭い画面を再現できる。
        /// </summary>
        public static Rectangle WorkArea
        {
            get
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                try
                {
                    string v = Environment.GetEnvironmentVariable("AUTOCOPY_SCREEN");
                    if (!string.IsNullOrEmpty(v))
                    {
                        string[] p = v.ToLowerInvariant().Split('x');
                        int w, h;
                        if (p.Length == 2 && int.TryParse(p[0], out w) && int.TryParse(p[1], out h) && w >= 640 && h >= 400)
                            wa = new Rectangle(wa.X, wa.Y, Math.Min(w, wa.Width), Math.Min(h, wa.Height));
                    }
                }
                catch (Exception) { }
                return wa;
            }
        }

        /// <summary>
        /// チェック欄の真上に置く「すべてにチェック／すべて外す」のアイコン（表示中の状態に合わせて ☑・□・■ に変わる）。
        /// toggle を渡すと、自分で全件を切り替える（ジョブの有効/無効をまとめて保存する場合など）。
        /// </summary>
        public static Panel CheckAllBar(ListView lv, string tip, Action<bool> toggle, params Control[] extra)
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.Dock = DockStyle.Top;
            p.AutoSize = true;
            p.WrapContents = false;
            p.Padding = new Padding(0);
            p.Margin = new Padding(0);
            CheckAllButton b = new CheckAllButton(lv, toggle);
            new ToolTip().SetToolTip(b, tip);
            b.AccessibleName = tip;
            p.Controls.Add(b);
            Label l = L(tip);
            l.ForeColor = Quiet;
            l.Margin = new Padding(S(2), S(5), S(12), 0);
            l.Cursor = Cursors.Hand;
            l.Click += delegate { b.PerformClick(); };
            p.Controls.Add(l);
            foreach (Control c in extra) p.Controls.Add(c);
            return p;
        }

        public static ListView List(params string[] columns)
        {
            ListView lv = new AcListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.GridLines = false;
            foreach (string c in columns)
            {
                string[] parts = c.Split('|');
                int w = parts.Length > 1 ? int.Parse(parts[1]) : 120;
                // 見出しが欠けない幅にする（幅 0 はわざと隠す列）
                if (w > 0) w = Math.Max(S(w), TextWidth(parts[0], Base) + 16);
                lv.Columns.Add(parts[0], w);
            }
            ((AcListView)lv).RememberWidths();
            return lv;
        }

        public static string BrowseFolder(IWin32Window owner, string initial, string description)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = description;
                d.ShowNewFolderButton = true;
                try { if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) d.SelectedPath = initial; } catch (Exception) { }
                if (d.ShowDialog(owner) == DialogResult.OK) return d.SelectedPath;
            }
            return null;
        }

        public static void Info(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, FitMessage(text, false), "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Error(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, FitMessage(text, true), "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static bool Confirm(IWin32Window owner, string text)
        {
            return MessageBox.Show(owner, FitMessage(text, false), "自動コピー", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }

        /// <summary>
        /// メッセージの行数を、画面の高さに収まる数までにする（Windows のメッセージは長いと画面より高くなり、下のボタンが画面の外に出て押せない）。
        /// 省いた行は「ほか n行」とし、エラーのときは全文をログに残す。
        /// </summary>
        public static string FitMessage(string text, bool logAll)
        {
            int max = MessageMaxLines(WorkArea.Height);
            string clipped = ClipLines(text, max, 120);
            if (logAll && clipped != text)
            {
                try { Logger.Error(null, "（画面に出しきれなかったメッセージの全文）" + (text ?? "").Replace("\r", "").Replace("\n", " / ")); } catch (Exception) { }
                clipped += "\n（全文はログに記録しました）";
            }
            return clipped;
        }

        /// <summary>画面の高さから、メッセージに出せる行数（題名・ボタン・余白を除く）</summary>
        public static int MessageMaxLines(int workAreaHeight)
        {
            int line = Math.Max(15, TextRenderer.MeasureText("あ", SystemFonts.MessageBoxFont ?? Base).Height + 2);
            return Math.Max(8, Math.Min(40, (workAreaHeight - S(220)) / line));
        }

        /// <summary>行数を maxLines までにし（超えた分は「ほか n行」）、長すぎる行は maxChars で切る。表示用の純粋な関数</summary>
        public static string ClipLines(string text, int maxLines, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            List<string> keep = new List<string>();
            bool changed = false;
            foreach (string l in lines)
            {
                if (l.Length > maxChars) { keep.Add(l.Substring(0, maxChars) + "…"); changed = true; }
                else keep.Add(l);
            }
            if (keep.Count > maxLines)
            {
                int rest = keep.Count - (maxLines - 1);
                keep = keep.GetRange(0, maxLines - 1);
                keep.Add("…ほか " + rest + "行");
                changed = true;
            }
            return changed ? string.Join("\n", keep.ToArray()) : text;
        }

        public static void OpenFolder(string path)
        {
            try
            {
                string p = PathUtil.Plain(path);
                if (Directory.Exists(PathUtil.Long(p))) Process.Start("explorer.exe", "\"" + p + "\"");
            }
            catch (Exception) { }
        }

        public static void OpenManual()
        {
            OpenManual(null);
        }

        /// <summary>操作説明書を開く。anchor を渡すと、その章（id）の場所を開く</summary>
        public static void OpenManual(string anchor)
        {
            string f = ManualPath();
            if (f == null) { Error(null, "操作説明書（" + AppPaths.ManualFileName + "）が見つかりません。AutoCopy.exe と同じフォルダに置いてください。"); return; }
            try
            {
                if (string.IsNullOrEmpty(anchor)) { Process.Start(f); return; }
                // ファイルを直接開くと「#章」が渡らないため、章へ移る小さなページを経由する
                string jump = Path.Combine(Path.GetTempPath(), "autocopy-help.html");
                string url = new Uri(f).AbsoluteUri + "#" + anchor;
                File.WriteAllText(jump, "<!DOCTYPE html><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0; url=" + url + "\"><a href=\"" + url + "\">操作説明書を開く</a>", new System.Text.UTF8Encoding(false));
                Process.Start(jump);
            }
            catch (Exception) { try { Process.Start(f); } catch (Exception ex) { Error(null, "操作説明書を開けません：" + ex.Message); } }
        }

        public static string ManualPath()
        {
            string f = Path.Combine(AppPaths.ExeDir, AppPaths.ManualFileName);
            if (!File.Exists(f)) f = Path.Combine(AppPaths.BinDir, AppPaths.ManualFileName);
            return File.Exists(f) ? f : null;
        }

        /// <summary>画面ごとの「？ 説明」ボタン（操作説明書のその画面の章を開く）</summary>
        /// <summary>下のボタンの帯の左端に「？ 説明」を置く</summary>
        public static FlowLayoutPanel WithHelp(FlowLayoutPanel footer, string anchor)
        {
            footer.Controls.Add(HelpButton(anchor));
            return footer;
        }

        public static Button HelpButton(string anchor)
        {
            Help.Used(anchor);
            Button b = B("？ 説明", delegate { OpenManual(anchor); });
            new ToolTip().SetToolTip(b, "この画面の説明（操作説明書）を開きます");
            b.AccessibleName = "この画面の説明";
            return b;
        }

        /// <summary>日付欄（表示は yyyy/MM/dd に固定。Windows の地域の設定で「1/ 1/2020」のようにならないように）</summary>
        public static DateTimePicker DateBox(DateTime value)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = "yyyy/MM/dd";
            d.Width = S(120);
            d.Value = value < d.MinDate || value > d.MaxDate ? DateTime.Today : value;   // 未設定（MinValue）でも落ちない
            return d;
        }

        /// <summary>
        /// 「小さいファイルは探さない」などの大きさの選択肢（KB）。0 = すべて。今の値が選択肢にないときは、その値も選択肢に足す。
        /// </summary>
        public static ComboBox SizeChoice(int currentKB, string zeroText, string format)
        {
            ComboBox cb = Combo(220);
            List<int> values = new List<int>(new int[] { 0, 1, 10, 100, 1024, 10240 });
            if (currentKB > 0 && !values.Contains(currentKB)) { values.Add(currentKB); values.Sort(); }
            foreach (int v in values) cb.Items.Add(new SizeItem(v, v == 0 ? zeroText : string.Format(format, Fmt.Size(v * 1024L))));
            SetSizeChoice(cb, currentKB);
            return cb;
        }

        public static void SetSizeChoice(ComboBox cb, int kb)
        {
            for (int i = 0; i < cb.Items.Count; i++) if (((SizeItem)cb.Items[i]).KB == kb) { cb.SelectedIndex = i; return; }
            cb.Items.Add(new SizeItem(kb, Fmt.Size(kb * 1024L)));
            cb.SelectedIndex = cb.Items.Count - 1;
        }

        public static int SizeChoiceKB(ComboBox cb)
        {
            SizeItem it = cb.SelectedItem as SizeItem;
            return it == null ? 0 : it.KB;
        }

        public class SizeItem
        {
            public readonly int KB; private readonly string text;
            public SizeItem(int kb, string text) { KB = kb; this.text = text; }
            public override string ToString() { return text; }
        }

        /// <summary>
        /// 重い処理を「中止」ボタン付きの待ち表示で実行する。中止されたら stopped = true で既定値を返す。
        /// 作業側は token を見て止まり、progress に途中経過を書く。
        /// </summary>
        public static T BusyCancel<T>(IWin32Window owner, string message, Func<CancellationToken, BusyProgress, T> work, out bool stopped)
        {
            T result = default(T);
            Exception error = null;
            bool canceled = false;
            using (BusyForm f = new BusyForm(message))
            {
                CancellationToken token = f.Token;
                BusyProgress progress = f.Progress;
                f.Shown += delegate
                {
                    System.Threading.Tasks.Task.Factory.StartNew(delegate()
                    {
                        try { result = work(token, progress); }
                        catch (OperationCanceledException) { canceled = true; }
                        catch (Exception ex) { if (token.IsCancellationRequested) canceled = true; else error = ex; }
                        try { f.BeginInvoke(new Action(delegate() { f.Close(); })); } catch (Exception) { }
                    });
                };
                f.ShowDialog(owner);
                if (f.StopRequested) canceled = true;
            }
            stopped = canceled;
            if (canceled) return default(T);
            if (error != null) throw error;
            return result;
        }

        /// <summary>重い処理を待ち表示付きで実行する（画面は固まらない）</summary>
        public static T Busy<T>(IWin32Window owner, string message, Func<T> work)
        {
            T result = default(T);
            Exception error = null;
            using (Form f = new Form())
            {
                SetupDialog(f, "自動コピー", 360, 90);
                f.ControlBox = false;
                Label l = L(message);
                l.Location = new Point(S(16), S(20));
                l.MaximumSize = new Size(f.ClientSize.Width - S(32), 0);
                ProgressBar pb = new ProgressBar();
                pb.Style = ProgressBarStyle.Marquee;
                int lh = TextRenderer.MeasureText(message ?? "", Base, new Size(f.ClientSize.Width - S(32), 0), TextFormatFlags.WordBreak).Height;
                pb.SetBounds(S(16), S(20) + lh + S(10), f.ClientSize.Width - S(32), S(16));
                f.ClientSize = new Size(f.ClientSize.Width, pb.Bottom + S(24));
                f.Controls.Add(l);
                f.Controls.Add(pb);
                f.Shown += delegate
                {
                    System.Threading.Tasks.Task.Factory.StartNew(delegate()
                    {
                        try { result = work(); } catch (Exception ex) { error = ex; }
                        try { f.BeginInvoke(new Action(delegate() { f.Close(); })); } catch (Exception) { }
                    });
                };
                f.ShowDialog(owner);
            }
            if (error != null) throw error;
            return result;
        }
    }

    /// <summary>途中経過（作業側のスレッドが書き、画面が0.3秒ごとに読む）</summary>
    public class BusyProgress
    {
        private volatile string text = "";
        public string Text { get { return text; } set { text = value ?? ""; } }
    }

    /// <summary>
    /// 中止ボタン付きの待ち表示。中止を押すと CancellationToken が取り消され、作業側は区切りの良いところで止まる。
    /// </summary>
    public class BusyForm : Form
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly Label progressLabel;
        private readonly Button stop;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public readonly BusyProgress Progress = new BusyProgress();

        public BusyForm(string message)
        {
            Ui.SetupDialog(this, "自動コピー", 440, 150);
            ControlBox = false;
            int w = ClientSize.Width - Ui.S(32);
            int lineH = TextRenderer.MeasureText("あ", Ui.Base).Height;
            Label l = Ui.L(message);
            l.Location = new Point(Ui.S(16), Ui.S(16));
            l.MaximumSize = new Size(w, 0);
            int lh = TextRenderer.MeasureText(message ?? "", Ui.Base, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            ProgressBar pb = new ProgressBar();
            pb.Style = ProgressBarStyle.Marquee;
            pb.SetBounds(Ui.S(16), Ui.S(16) + lh + Ui.S(10), w, Ui.S(16));
            progressLabel = Ui.L("");
            progressLabel.ForeColor = Ui.Quiet;
            progressLabel.AutoSize = false;
            progressLabel.AutoEllipsis = true;
            progressLabel.SetBounds(Ui.S(16), pb.Bottom + Ui.S(8), w, lineH + Ui.S(6));
            stop = Ui.B("中止", delegate { RequestStop(); });
            stop.Tag = "safe";   // 調べるのを止めるだけ（ファイルは変えない）
            stop.Location = new Point(ClientSize.Width - Ui.S(16) - stop.PreferredSize.Width, progressLabel.Bottom + Ui.S(8));
            ClientSize = new Size(ClientSize.Width, stop.Location.Y + stop.PreferredSize.Height + Ui.S(14));
            CancelButton = stop;
            Controls.Add(l);
            Controls.Add(pb);
            Controls.Add(progressLabel);
            Controls.Add(stop);
            timer.Interval = 300;
            timer.Tick += delegate { ShowProgress(); };
            timer.Start();
        }

        public CancellationToken Token { get { return cts.Token; } }
        public bool StopRequested { get { return cts.IsCancellationRequested; } }

        public void RequestStop()
        {
            if (cts.IsCancellationRequested) return;
            cts.Cancel();
            stop.Enabled = false;
            stop.Text = "中止しています…";
            stop.Width = stop.PreferredSize.Width + 8;
            stop.Left = ClientSize.Width - 16 - stop.Width;
            // ネットワークの応答待ちなど、すぐ止まれない処理でも画面は1.5秒で閉じる（作業は裏で止まり、結果は捨てる）
            System.Windows.Forms.Timer giveUp = new System.Windows.Forms.Timer();
            giveUp.Interval = 1500;
            giveUp.Tick += delegate { giveUp.Stop(); giveUp.Dispose(); if (!IsDisposed) Close(); };
            giveUp.Start();
        }

        public void ShowProgress()
        {
            string t = Progress.Text;
            int sec = (int)watch.Elapsed.TotalSeconds;
            progressLabel.Text = (t.Length > 0 ? t + "　" : "") + "経過 " + (sec >= 60 ? (sec / 60) + "分" + (sec % 60) + "秒" : sec + "秒");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); cts.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>文字列の一覧を見せるだけの画面</summary>
    public class TextListForm : Form
    {
        public TextListForm(string title, string header, System.Collections.Generic.IEnumerable<string> items)
        {
            Ui.SetupForm(this, title, 640, 480);
            ListBox lb = new ListBox();
            lb.Dock = DockStyle.Fill;
            lb.HorizontalScrollbar = true;
            foreach (string s in items) lb.Items.Add(s);
            Label h = Ui.L(header);
            h.Dock = DockStyle.Top;
            h.Padding = new Padding(8);
            h.AutoSize = false;
            h.Height = TextRenderer.MeasureText("あ", Ui.Base).Height + Ui.S(16);
            Controls.Add(lb);
            Controls.Add(h);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); })));
        }
    }

    /// <summary>
    /// 一覧表。見出しをクリックすると並べ替え（もう一度で逆順、▲▼で表示）、
    /// 横幅に合わせて列の幅を自動調整、行のダブルクリックではチェックが変わらない。
    /// </summary>
    public class AcListView : ListView
    {
        private int sortColumn = -1;
        private bool sortAsc = true;
        private bool suppressCheck;
        private bool fitting;
        private int[] designWidths = new int[0];
        private readonly List<string> headers = new List<string>();

        /// <summary>仮想表示（VirtualMode）の一覧で、並べ替えを自分で行うときの通知（列, 昇順）</summary>
        public event Action<int, bool> SortRequested;

        /// <summary>列を最小の幅まで縮めても一覧の幅に収まらない（横にスクロールして見る）</summary>
        public bool MinOverflow;

        /// <summary>並べ替えを無効にする（順番に意味がある一覧）</summary>
        public bool Sortable = true;

        public int SortColumn { get { return sortColumn; } }
        public bool SortAscending { get { return sortAsc; } }

        public AcListView()
        {
            DoubleBuffered = true;
            ShowItemToolTips = true;   // 列が狭くて欠けた文字は、マウスを重ねると全部見える
        }

        internal void RememberWidths()
        {
            headers.Clear();
            designWidths = new int[Columns.Count];
            for (int i = 0; i < Columns.Count; i++) { headers.Add(Columns[i].Text); designWidths[i] = Columns[i].Width; }
        }

        // ---------- 並べ替え ----------

        protected override void OnColumnClick(ColumnClickEventArgs e)
        {
            base.OnColumnClick(e);
            if (!Sortable) return;
            if (e.Column == sortColumn) sortAsc = !sortAsc;
            else { sortColumn = e.Column; sortAsc = !LooksLikeNewestFirst(e.Column); }
            ApplySort();
        }

        /// <summary>日時の列は、最初のクリックで新しい順にする</summary>
        private bool LooksLikeNewestFirst(int col)
        {
            string h = col < headers.Count ? headers[col] : "";
            return h.Contains("日時") || h.Contains("時刻") || h.Contains("更新") || h.Contains("前回") || h.Contains("最後");
        }

        /// <summary>並べ替えの状態を指定して並べ直す（col = -1 で解除）</summary>
        public void SetSort(int col, bool asc)
        {
            sortColumn = col; sortAsc = asc;
            ApplySort();
        }

        /// <summary>中身を更新した後に、今の並べ替えを掛け直す</summary>
        public void Resort()
        {
            if (sortColumn < 0) return;
            if (VirtualMode) { if (SortRequested != null) SortRequested(sortColumn, sortAsc); return; }
            if (ListViewItemSorter == null) ListViewItemSorter = new ItemComparer(this);
            BeginUpdate();
            try { Sort(); } finally { EndUpdate(); }
        }

        private void ApplySort()
        {
            for (int i = 0; i < Columns.Count && i < headers.Count; i++)
            {
                Columns[i].Text = headers[i] + (i == sortColumn ? (sortAsc ? " ▲" : " ▼") : "");
                // ▲▼ を付けても見出しが欠けない幅にする
                int need = Ui.TextWidth(Columns[i].Text, Font) + 14;
                if (i < designWidths.Length && designWidths[i] > 0 && designWidths[i] < need) designWidths[i] = need;
            }
            FitColumns();
            if (sortColumn < 0) { ListViewItemSorter = null; return; }
            if (VirtualMode)
            {
                if (SortRequested != null) SortRequested(sortColumn, sortAsc);
                Invalidate();
                return;
            }
            ListViewItemSorter = new ItemComparer(this);
            BeginUpdate();
            try { Sort(); } finally { EndUpdate(); }
        }

        private class ItemComparer : System.Collections.IComparer
        {
            private readonly AcListView lv;
            public ItemComparer(AcListView lv) { this.lv = lv; }
            public int Compare(object x, object y)
            {
                ListViewItem a = (ListViewItem)x, b = (ListViewItem)y;
                int c = lv.sortColumn;
                // グループ表示のときは、グループの中で並べ替える（グループの順番は変えない）
                object x1 = Cell(a, c), y1 = Cell(b, c);
                // 空欄は、昇順でも降順でも後ろ
                bool ex = SortKey.IsBlank(x1), ey = SortKey.IsBlank(y1);
                if (ex != ey) return ex ? 1 : -1;
                int r = SortKey.Compare(x1, y1);
                if (r == 0) r = a.Index.CompareTo(b.Index);
                return lv.sortAsc ? r : -r;
            }
            private static object Cell(ListViewItem it, int c)
            {
                if (c >= it.SubItems.Count) return "";
                ListViewItem.ListViewSubItem s = it.SubItems[c];
                if (s.Tag is IComparable) return s.Tag;
                return s.Text ?? "";
            }
        }

        // ---------- ダブルクリックでチェックが変わらないようにする ----------

        private const int WM_LBUTTONDBLCLK = 0x0203;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDBLCLK && CheckBoxes)
            {
                // チェック欄そのものをダブルクリックしたときだけは、通常どおり切り替える
                int lp = m.LParam.ToInt32();
                Point pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                ListViewHitTestInfo h = null;
                try { h = HitTest(pt); } catch (Exception) { }
                bool onBox = h != null && h.Location == ListViewHitTestLocations.StateImage;
                if (!onBox)
                {
                    suppressCheck = true;
                    try { base.WndProc(ref m); } finally { suppressCheck = false; }
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnItemCheck(ItemCheckEventArgs e)
        {
            if (suppressCheck) { e.NewValue = e.CurrentValue; return; }
            base.OnItemCheck(e);
        }

        // ---------- 列の幅を横幅に合わせる ----------

        protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
        {
            base.OnColumnWidthChanged(e);
            // 利用者が手で幅を変えたら、それを基準にする
            if (!fitting && e.ColumnIndex < designWidths.Length) designWidths[e.ColumnIndex] = Columns[e.ColumnIndex].Width;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitColumns();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FitColumns();
        }

        /// <summary>
        /// 縮めても残す幅：見出し（▲▼を含む）が読める幅と、短い中身（日時・サイズ・件数・種類など、最初の100行で
        /// いちばん長いもの。ただし約160pxまで）が読める幅の大きい方。
        /// </summary>
        private int MinWidth(int col)
        {
            string h = col < headers.Count ? headers[col] : "";
            int m = Math.Max(Ui.S(40), Ui.TextWidth(h + " ▼", Font) + 14);
            int content = 0;
            if (h.Contains("日時") || h.Contains("時刻")) content = Ui.TextWidth(VirtualMode ? "0000/00/00 00:00:00" : "0000/00/00 00:00", Font) + 14;
            if (!VirtualMode)
            {
                try
                {
                    int n = Math.Min(100, Items.Count);
                    for (int r = 0; r < n; r++)
                    {
                        ListViewItem it = Items[r];
                        if (it != null && col < it.SubItems.Count && it.SubItems[col] != null)
                            content = Math.Max(content, Ui.TextWidth(it.SubItems[col].Text, Font) + 14);
                    }
                }
                catch (Exception) { }
            }
            int cap = h.Contains("日時") || h.Contains("時刻") ? Ui.S(170) : Ui.S(110);
            return Math.Max(m, Math.Min(content, cap));
        }

        /// <summary>列の合計が横幅を越えるときは縮め、余るときは一番広い列を広げる</summary>
        public void FitColumns()
        {
            if (fitting || View != View.Details || Columns.Count == 0 || designWidths.Length != Columns.Count) return;
            int avail = ClientSize.Width - 2;
            // 縦のスクロールバーがまだ出ていなければ、出たときの分を空けておく（行が増えても横スクロールが出ないように）
            if (IsHandleCreated && (GetWindowLong(Handle, -16) & 0x00200000) == 0) avail -= SystemInformation.VerticalScrollBarWidth;
            if (avail < Ui.S(120)) return;
            fitting = true;
            try
            {
                int sum = 0, widest = -1;
                for (int i = 0; i < designWidths.Length; i++)
                {
                    if (designWidths[i] <= 0) continue;
                    sum += designWidths[i];
                    if (widest < 0 || designWidths[i] > designWidths[widest]) widest = i;
                }
                if (sum <= 0) return;
                int[] w = (int[])designWidths.Clone();
                MinOverflow = false;
                if (sum > avail)
                {
                    // 全体を同じ割合で縮める（長い列ほど多く縮む）。ただし、見出しと短い中身（日時・サイズ・種類など）が読める幅は残す
                    int[] min = new int[w.Length];
                    for (int i = 0; i < w.Length; i++)
                        if (w[i] > 0) min[i] = Math.Min(w[i], MinWidth(i));
                    bool[] fixedCol = new bool[w.Length];
                    for (int round = 0; round < 6; round++)
                    {
                        int fixedSum = 0, flexSum = 0;
                        for (int i = 0; i < w.Length; i++)
                        {
                            if (designWidths[i] <= 0) continue;
                            if (fixedCol[i]) fixedSum += min[i]; else flexSum += designWidths[i];
                        }
                        if (flexSum <= 0) break;
                        double scale = Math.Max(0, (double)(avail - fixedSum) / flexSum);
                        bool changed = false;
                        for (int i = 0; i < w.Length; i++)
                        {
                            if (designWidths[i] <= 0) continue;
                            if (fixedCol[i]) { w[i] = min[i]; continue; }
                            int x = (int)Math.Floor(designWidths[i] * scale);
                            if (x < min[i]) { fixedCol[i] = true; w[i] = min[i]; changed = true; }
                            else w[i] = x;
                        }
                        if (!changed) break;
                    }
                    int minSum = 0;
                    for (int i = 0; i < w.Length; i++) if (designWidths[i] > 0) minSum += min[i];
                    MinOverflow = minSum > avail;   // 見出しが読める幅だけでも収まらない（横にスクロールして見る）
                }
                else if (widest >= 0) w[widest] += avail - sum;
                for (int i = 0; i < w.Length; i++)
                    if (Columns[i].Width != w[i]) Columns[i].Width = w[i];
            }
            finally { fitting = false; }
        }
    }

    /// <summary>並べ替えの比較（日時・容量・数値・文字の順に見分ける）</summary>
    public static class SortKey
    {
        private static readonly string[] DateFormats = {
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/MM/dd", "yyyy/M/d H:mm:ss", "yyyy/M/d H:mm", "yyyy/M/d",
            "MM/dd HH:mm:ss", "MM/dd HH:mm", "M/d H:mm", "MM/dd", "HH:mm:ss", "HH:mm", "H:mm" };

        public static int Compare(object a, object b)
        {
            if (a is IComparable && !(a is string) && b != null && a.GetType() == b.GetType()) return ((IComparable)a).CompareTo(b);
            string x = a == null ? "" : a.ToString(), y = b == null ? "" : b.ToString();
            bool ex = IsBlank(x), ey = IsBlank(y);
            if (ex || ey) return ex == ey ? 0 : ex ? 1 : -1;   // 空欄は後ろ
            double dx, dy;
            DateTime tx, ty;
            if (TryDate(x, out tx) && TryDate(y, out ty)) return tx.CompareTo(ty);
            if (TryNumber(x, out dx) && TryNumber(y, out dy)) return dx.CompareTo(dy);
            return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool IsBlank(object o)
        {
            if (o == null) return true;
            if (!(o is string)) return false;
            string s = ((string)o).Trim();
            return s.Length == 0 || s == "-" || s == "－" || s == "—";
        }

        public static bool TryDate(string s, out DateTime t)
        {
            s = s.Trim();
            return DateTime.TryParseExact(s, DateFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out t);
        }

        /// <summary>「1.5 GB」「12,345」「45%」「3件」などを数にする</summary>
        public static bool TryNumber(string s, out double v)
        {
            v = 0;
            s = s.Trim().Replace(",", "");
            int i = 0;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == start) return false;
            if (!double.TryParse(s.Substring(0, i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
            string unit = s.Substring(i).Trim().ToUpperInvariant();
            if (unit.StartsWith("KB")) v *= 1024;
            else if (unit.StartsWith("MB")) v *= 1024.0 * 1024;
            else if (unit.StartsWith("GB")) v *= 1024.0 * 1024 * 1024;
            else if (unit.StartsWith("TB")) v *= 1024.0 * 1024 * 1024 * 1024;
            else if (unit.Length > 0 && unit != "B" && unit != "%" && unit != "件" && unit != "回" && unit != "秒" && unit != "個" && !unit.StartsWith("ファイル") && !unit.StartsWith("B/") && !unit.StartsWith("(") && !unit.StartsWith("（")) return false;
            return true;
        }
    }

    /// <summary>チェック欄の真上に置く、全件チェックの切り替えアイコン</summary>
    public class CheckAllButton : Button
    {
        private readonly ListView lv;
        private readonly Action<bool> toggle;
        private CheckState state = CheckState.Unchecked;

        public CheckAllButton(ListView lv, Action<bool> toggle)
        {
            this.lv = lv;
            this.toggle = toggle;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 241, 251);
            int sz = Math.Max(Ui.S(22), System.Windows.Forms.SystemInformation.SmallIconSize.Height + Ui.S(6));
            Size = new Size(sz, sz);
            Margin = new Padding(Ui.S(1), Ui.S(1), 0, Ui.S(1));
            TabStop = true;
            Cursor = Cursors.Hand;
            // 1件ごとに数え直すと、件数が多い一覧で行を足すたびに全件を読むことになり固まるため、まとめて1回数える
            lv.ItemChecked += delegate { RefreshLater(); };
            // 一覧のハンドルを作っている最中は行を読めない（読むと一覧が壊れる）ため、作り終えてから数える
            lv.HandleCreated += delegate { try { lv.BeginInvoke(new Action(Refresh2)); } catch (Exception) { } };
            VisibleChanged += delegate { if (Visible) Refresh2(); };
        }

        public CheckState State { get { return state; } }

        private bool pending;

        private void RefreshLater()
        {
            if (pending) return;
            if (!lv.IsHandleCreated) { Refresh2(); return; }
            pending = true;
            try { lv.BeginInvoke(new Action(delegate() { pending = false; Refresh2(); })); }
            catch (Exception) { pending = false; }
        }

        /// <summary>一覧のチェックの状態から、アイコンを ☑（すべて）・■（一部）・□（なし）に変える</summary>
        public void Refresh2()
        {
            if (lv.IsDisposed || lv.VirtualMode) return;
            if (lv.IsHandleCreated && lv.RecreatingHandle) return;
            int on = 0, n = lv.Items.Count;
            try { foreach (ListViewItem it in lv.Items) if (it.Checked) on++; }
            catch (Exception) { return; }
            CheckState s = n > 0 && on == n ? CheckState.Checked : on > 0 ? CheckState.Indeterminate : CheckState.Unchecked;
            if (s != state) { state = s; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            bool check = state != CheckState.Checked;
            if (toggle != null) toggle(check);
            else
            {
                lv.BeginUpdate();
                try { foreach (ListViewItem it in lv.Items) it.Checked = check; }
                finally { lv.EndUpdate(); }
            }
            Refresh2();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            System.Windows.Forms.VisualStyles.CheckBoxState cs =
                state == CheckState.Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal :
                state == CheckState.Indeterminate ? System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal :
                System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            Size gs = CheckBoxRenderer.GetGlyphSize(e.Graphics, cs);
            // 一覧のチェック欄と同じ見た目・同じ位置（左端から少し）に描く
            Point at = new Point(Math.Max(0, Ui.S(4) - 1), (Height - gs.Height) / 2);
            CheckBoxRenderer.DrawCheckBox(e.Graphics, at, cs);
        }
    }

    /// <summary>
    /// 灰色（操作できない）の項目に、マウスを重ねると「なぜ操作できないか」を出す。
    /// Windows では操作できない部品はマウスの動きを受け取らないため、入れ物の側で見て出す。
    /// </summary>
    public class ReasonTips
    {
        private readonly Dictionary<Control, Func<string>> reasons = new Dictionary<Control, Func<string>>();
        private readonly ToolTip tip = new ToolTip();
        private readonly HashSet<Control> hooked = new HashSet<Control>();
        private Control shownFor;

        public ReasonTips() { tip.ShowAlways = true; tip.InitialDelay = 200; tip.AutoPopDelay = 15000; }

        /// <summary>c が灰色のときに出す理由（その時点の設定に合わせて変わるもの）</summary>
        public void Set(Control c, Func<string> reason) { reasons[c] = reason; Hook(c.Parent); c.ParentChanged += delegate { Hook(c.Parent); }; }

        public void Set(Control c, string reason) { Set(c, delegate { return reason; }); }

        /// <summary>今、灰色で理由が出ないもの（確認用）。ラベル・入れ物は除く</summary>
        public string ReasonFor(Control c)
        {
            Func<string> f;
            return reasons.TryGetValue(c, out f) ? f() : null;
        }

        public bool Has(Control c) { return reasons.ContainsKey(c); }

        private void Hook(Control parent)
        {
            if (parent == null || hooked.Contains(parent)) return;
            hooked.Add(parent);
            parent.MouseMove += delegate(object s, MouseEventArgs e)
            {
                Control child = parent.GetChildAtPoint(e.Location, GetChildAtPointSkip.Invisible);
                Func<string> f;
                if (child != null && !child.Enabled && reasons.TryGetValue(child, out f))
                {
                    if (shownFor == child) return;
                    shownFor = child;
                    string r = f();
                    if (!string.IsNullOrEmpty(r)) tip.Show("操作できない理由：" + r, parent, e.X + 12, e.Y + 18, 8000);
                }
                else if (shownFor != null) { tip.Hide(parent); shownFor = null; }
            };
            parent.MouseLeave += delegate { if (shownFor != null) { tip.Hide(parent); shownFor = null; } };
        }
    }

    /// <summary>操作説明書の章（id）。画面の「？ 説明」ボタンが使う。すべて説明書にあることをテストで確かめる</summary>
    public static class Help
    {
        public const string Jobs = "c3", Preview = "preview", Main = "c5", Notices = "notices", Conflicts = "c7", Mirror = "c8",
            Sync = "c8s", SyncDelete = "syncdel", DupCheck = "c9", FolderDup = "folderdup", Dedup = "dedup", Log = "c11",
            Usage = "usage", Settings = "appsettings", Select = "list", Restore = "restore", Exclude = "c4", Setup = "c2", Skip = "skiplist", Trouble = "c12";
        public static readonly string[] All = { Jobs, Preview, Main, Notices, Conflicts, Mirror, Sync, SyncDelete, DupCheck, FolderDup, Dedup, Log, Usage, Settings, Select, Restore, Exclude, Setup, Skip, Trouble };
        private static readonly HashSet<string> used = new HashSet<string>();
        public static void Used(string anchor) { lock (used) used.Add(anchor ?? ""); }
        public static string[] UsedAnchors() { lock (used) { string[] a = new string[used.Count]; used.CopyTo(a); return a; } }
    }
}
