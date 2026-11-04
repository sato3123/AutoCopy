using System;
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
            try { w = Math.Min(w, Screen.PrimaryScreen.WorkingArea.Width - S(220)); } catch (Exception) { }
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
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                want.Width = Math.Min(want.Width, wa.Width - 24);
                want.Height = Math.Min(want.Height, wa.Height - 48);
            }
            catch (Exception) { }
            f.ClientSize = want;
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
            return p;
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

        public static ListView List(params string[] columns)
        {
            ListView lv = new ListView();
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
            MessageBox.Show(owner, text, "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Error(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static bool Confirm(IWin32Window owner, string text)
        {
            return MessageBox.Show(owner, text, "自動コピー", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
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
            string f = Path.Combine(AppPaths.ExeDir, AppPaths.ManualFileName);
            if (!File.Exists(f)) f = Path.Combine(AppPaths.BinDir, AppPaths.ManualFileName);
            if (File.Exists(f)) { try { Process.Start(f); return; } catch (Exception) { } }
            Error(null, "操作説明書（" + AppPaths.ManualFileName + "）が見つかりません。AutoCopy.exe と同じフォルダに置いてください。");
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
}
