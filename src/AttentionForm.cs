using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// 「確認が必要です」のお知らせ画面。画面右下（通知領域のそば）に、前面に出す。
    /// 作業中の画面から入力を奪わない（クリックするまで前の画面で入力を続けられる）。
    /// </summary>
    public class AttentionForm : Form
    {
        private readonly FlowLayoutPanel list;
        private readonly Label head;
        public event Action<string, string> OpenRequested;   // action, jobId
        public event Action Snoozed;

        public AttentionForm()
        {
            Ui.SetupForm(this, "自動コピー：確認が必要です", 460, 200);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            Panel stripe = new Panel();
            stripe.Dock = DockStyle.Left;
            stripe.Width = Ui.S(6);
            stripe.BackColor = IconFactory.Orange;

            head = Ui.Heading("確認が必要です");
            head.ForeColor = Ui.Warn;
            list = Ui.Column();
            Label note = Ui.Hint("確認が済むまで、画面右下のアイコンが点滅します。");

            Button openAll = Ui.B("確認する", delegate { Open(null, null); });
            openAll.BackColor = Ui.Accent; openAll.ForeColor = Color.White; openAll.UseVisualStyleBackColor = false;
            openAll.FlatStyle = FlatStyle.Flat; openAll.FlatAppearance.BorderColor = Ui.Accent;
            Button later = Ui.B("あとで（1時間）", delegate { if (Snoozed != null) Snoozed(); Close(); });
            FlowLayoutPanel buttons = Ui.Row(openAll, later);
            buttons.Margin = new Padding(0, Ui.S(6), 0, 0);

            FlowLayoutPanel body = Ui.Column(head, list, note, buttons);
            body.Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(12));
            body.Location = new Point(stripe.Width, 0);
            Controls.Add(body);
            Controls.Add(stripe);
            CancelButton = later;
        }

        /// <summary>入力を奪わずに表示する</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        public void SetItems(List<AppCore.AttentionItem> items)
        {
            SuspendLayout();
            list.Controls.Clear();
            int shown = 0;
            foreach (AppCore.AttentionItem it in items)
            {
                if (shown >= 6) { list.Controls.Add(Ui.Hint("ほか " + (items.Count - shown) + "件")); break; }
                AppCore.AttentionItem item = it;
                LinkLabel l = new LinkLabel();
                l.Text = (it.IsError ? "● " : "▲ ") + it.Text;
                l.AutoSize = true;
                l.MaximumSize = new Size(Ui.MaxWidth(420), 0);
                l.Margin = new Padding(Ui.S(2), Ui.S(4), Ui.S(2), Ui.S(2));
                l.LinkColor = it.IsError ? Ui.Err : Ui.Accent;
                l.LinkBehavior = LinkBehavior.HoverUnderline;
                l.LinkClicked += delegate { Open(item.Action, item.JobId); };
                list.Controls.Add(l);
                shown++;
            }
            head.Text = "確認が必要です（" + items.Count + "件）";
            ResumeLayout(true);
            PlaceBottomRight();
        }

        private void Open(string action, string jobId)
        {
            if (OpenRequested != null) OpenRequested(action, jobId);
            Close();
        }

        /// <summary>画面右下（タスクバーのすぐ上）に置く</summary>
        public void PlaceBottomRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            PerformLayout();
            Location = new Point(wa.Right - Width - Ui.S(12), wa.Bottom - Height - Ui.S(12));
        }
    }
}
