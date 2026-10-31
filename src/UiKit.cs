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
        public static readonly Font Base = new Font("Meiryo UI", 9f);
        public static readonly Font Bold = new Font("Meiryo UI", 9f, FontStyle.Bold);
        public static readonly Font Title = new Font("Meiryo UI", 12f, FontStyle.Bold);
        public static readonly Font Small = new Font("Meiryo UI", 8.25f);
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
            f.ClientSize = new Size(w, h);
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
            l.Width = width;
            l.Height = 22;
            return l;
        }

        public static Label Hint(string text)
        {
            Label l = L(text);
            l.ForeColor = Quiet;
            l.Font = Small;
            return l;
        }

        public static Label Wrap(string text, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(width, 0);
            l.Margin = new Padding(3, 4, 3, 4);
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
            b.MinimumSize = new Size(0, 28);
            b.UseVisualStyleBackColor = true;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static TextBox T(string text, int width)
        {
            TextBox t = new TextBox();
            t.Text = text ?? "";
            t.Width = width;
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
            n.Width = width;
            n.TextAlign = HorizontalAlignment.Right;
            n.Margin = new Padding(3, 4, 3, 3);
            return n;
        }

        public static ComboBox Combo(int width, params string[] items)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = width;
            c.Items.AddRange(items);
            if (items.Length > 0) c.SelectedIndex = 0;
            c.Margin = new Padding(3, 4, 3, 3);
            return c;
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
            inner.Location = new Point(10, 22);
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
            p.Height = 48;
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
                l.Location = new Point(16, 20);
                ProgressBar pb = new ProgressBar();
                pb.Style = ProgressBarStyle.Marquee;
                pb.SetBounds(16, 50, 328, 16);
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
            Label l = Ui.L(message);
            l.Location = new Point(16, 16);
            l.MaximumSize = new Size(408, 0);
            ProgressBar pb = new ProgressBar();
            pb.Style = ProgressBarStyle.Marquee;
            pb.SetBounds(16, 48, 408, 16);
            progressLabel = Ui.L("");
            progressLabel.ForeColor = Ui.Quiet;
            progressLabel.AutoSize = false;
            progressLabel.AutoEllipsis = true;
            progressLabel.SetBounds(16, 72, 408, 22);
            stop = Ui.B("中止", delegate { RequestStop(); });
            stop.Location = new Point(440 - 16 - stop.PreferredSize.Width - 8, 108);
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
            h.Height = 32;
            Controls.Add(lb);
            Controls.Add(h);
            Controls.Add(Ui.Footer(Ui.B("閉じる", delegate { Close(); })));
        }
    }
}
