using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// ボタンの見た目のきまり（全画面共通）
    /// ・主な操作（実行・検索・OK）は青い塗りつぶし
    /// ・削除・上書きなど取り消しにくい操作は赤（注意は橙）で、押すと必ず確認メッセージを出す
    /// ・アイコンで種類が分かるようにし、似た操作は近くに並べ、種類の違うまとまりの間は Gap で空ける
    /// </summary>
    public static class Btn
    {
        public static readonly Color DangerBack = Color.FromArgb(253, 236, 234);
        public static readonly Color CautionBack = Color.FromArgb(253, 246, 236);

        /// <summary>アイコン付きのふつうのボタン</summary>
        public static Button B(string icon, string text, EventHandler onClick)
        {
            Button b = Ui.B(text, onClick);
            SetIcon(b, icon, Ui.Quiet);
            return b;
        }

        /// <summary>主な操作（青い塗りつぶし・白い文字）</summary>
        public static Button Primary(string icon, string text, EventHandler onClick)
        {
            Button b = Ui.B(text, onClick);
            b.BackColor = Ui.Accent; b.ForeColor = Color.White; b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Ui.Accent;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 84, 163);
            SetIcon(b, icon, Color.White);
            return b;
        }

        /// <summary>
        /// 削除など取り消しにくい操作（赤）。confirm を渡すと、押したときに確認メッセージを出し、OK のときだけ onClick を呼ぶ。
        /// confirm が null のときは、onClick の中で必ず確認すること。
        /// </summary>
        public static Button Danger(string icon, string text, Func<string> confirm, EventHandler onClick)
        {
            Button b = Ui.B(text, null);
            b.ForeColor = Ui.Err;
            b.BackColor = DangerBack; b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(226, 150, 140);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 220, 216);
            SetIcon(b, icon ?? "delete", Ui.Err);
            b.Click += delegate(object s, EventArgs e)
            {
                if (confirm != null)
                {
                    string msg = confirm();
                    if (msg == null) return;   // 確認の前に止めた（選んでいない など）
                    if (MessageBox.Show(b.FindForm(), Ui.FitMessage(msg, false), "確認（元に戻しにくい操作です）", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                }
                if (onClick != null) onClick(s, e);
            };
            return b;
        }

        /// <summary>注意が必要な操作（橙）。確認の扱いは Danger と同じ</summary>
        public static Button Caution(string icon, string text, Func<string> confirm, EventHandler onClick)
        {
            Button b = Danger(icon, text, confirm, onClick);
            b.ForeColor = Ui.Warn;
            b.BackColor = CautionBack;
            b.FlatAppearance.BorderColor = Color.FromArgb(222, 186, 130);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 236, 214);
            SetIcon(b, icon, Ui.Warn);
            return b;
        }

        /// <summary>主な操作の見た目にする（既にあるボタン）</summary>
        public static Button MakePrimary(Button b)
        {
            b.BackColor = Ui.Accent; b.ForeColor = Color.White; b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Ui.Accent;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 84, 163);
            if (b.Image != null) SetIcon(b, IconOf(b.Text) ?? "ok", Color.White);
            return b;
        }

        /// <summary>
        /// 取り消しにくい操作の見た目にする（赤）。押したときの確認メッセージは、ボタンの処理の中で必ず出すこと
        /// （画面の確認で、赤・橙のボタンが確認を出すかを調べている）。
        /// </summary>
        public static Button MakeDanger(Button b)
        {
            b.ForeColor = Ui.Err;
            b.BackColor = DangerBack; b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(226, 150, 140);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 220, 216);
            SetIcon(b, IconOf(b.Text) ?? "delete", Ui.Err);
            return b;
        }

        /// <summary>注意が必要な操作の見た目にする（橙）。確認メッセージの扱いは MakeDanger と同じ</summary>
        public static Button MakeCaution(Button b)
        {
            b.ForeColor = Ui.Warn;
            b.BackColor = CautionBack; b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(222, 186, 130);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 236, 214);
            SetIcon(b, IconOf(b.Text) ?? "warn", Ui.Warn);
            return b;
        }

        /// <summary>赤（削除など）・橙（注意）の見た目か</summary>
        public static bool IsDangerStyled(Button b) { return b.ForeColor == Ui.Err || b.ForeColor == Ui.Warn; }

        /// <summary>取り消しにくい操作を表すボタンの文字か（画面の確認で、赤・橙になっているかを調べる）</summary>
        public static bool LooksDangerous(string t)
        {
            t = t ?? "";
            if (t.Contains("開く") || t.Contains("確認") || t.Contains("見る") || t.Contains("しない") || t.Contains("一覧") || t.Contains("にチェック") || t.Contains("チェックを外す") || t.Contains("ほかは外す")) return false;
            return t.Contains("削除") || t.Contains("上書きする") || t.Contains("移す") || t.Contains("中止") || t.Contains("取り消す") || t.Contains("元に戻す") || t.Contains("を使う") || t.Contains("ミラーにする") || t.Contains("テキストにする");
        }

        /// <summary>ボタンの文字から、種類に合うアイコンを決める（全画面で同じアイコンになる）</summary>
        public static string IconOf(string text)
        {
            string t = (text ?? "").Trim();
            if (t.StartsWith("閉じる") || t == "キャンセル" || t == "やめる" || t.StartsWith("後で決める") || t.StartsWith("あとで")) return "close";
            if (t.Contains("条件で選ぶ")) return "filter";
            if (t.Contains("CSV") || t.Contains("画像で保存")) return "export";
            if (t.Contains("操作説明書") || t == "ヘルプ") return "help";
            if (t.Contains("を開く") || t.StartsWith("参照")) return "folder";
            if (t.Contains("削除") && !t.Contains("しない") && !t.Contains("確認")) return "delete";
            if (t.Contains("元に戻す") || t.Contains("取り消す") || t.Contains("（戻す）")) return "undo";
            if (t.Contains("中止")) return "stop";
            if (t.Contains("一時停止")) return "pause";
            if (t.Contains("コピーせずに") || t.Contains("結果の予測")) return "preview";
            if (t.Contains("実行")) return "run";
            if (t.Contains("再読み込み") || t.Contains("最新にする") || t.Contains("もう一度") || t.Contains("今すぐ確認") || t.Contains("再チェック")) return "refresh";
            if (t.Contains("検索") || t.Contains("探す") || t.StartsWith("重複チェック")) return "search";
            if (t.Contains("追加")) return "add";
            if (t.StartsWith("編集") || t.StartsWith("変更")) return "edit";
            if (t.StartsWith("複製")) return "copy";
            if (t.Contains("集計") || t.Contains("内訳")) return "chart";
            if (t.Contains("作業状況")) return "progress";
            if (t.Contains("ログ")) return "log";
            if (t == "設定") return "settings";
            if (t.Contains("接続テスト") || t.Contains("ID・パスワード")) return "shield";
            if (t.Contains("スキップ")) return "skip";
            if (t.Contains("上書きしない") || t.Contains("削除しない")) return "shield";
            if (t.EndsWith("の確認") || t.StartsWith("確認") || t.Contains("を確認")) return "warn";
            if (t.Contains("上書き") || t.Contains("を使う") || t.Contains("移す") || t.Contains("テキストにする")) return "move";
            if (t == "OK" || t == "保存" || t == "作成" || t.StartsWith("変更する") || t.Contains("にチェック") || t.Contains("使えるか確認")) return "ok";
            if (t.Contains("一覧")) return "list";
            if (t.Contains("コピーする")) return "copy";
            return null;
        }

        /// <summary>Ui.B で作ったボタンに、文字に合うアイコンを付ける</summary>
        public static void AutoIcon(Button b)
        {
            string ic = IconOf(b.Text);
            if (ic != null) SetIcon(b, ic, ic == "delete" || ic == "stop" ? Ui.Err : ic == "run" || ic == "add" || ic == "ok" ? Ui.Ok : Ui.Quiet);
        }

        /// <summary>まとまりの間の空き（種類の違うボタンを離す）</summary>
        public static Control Gap()
        {
            Panel p = new Panel();
            p.Width = Ui.S(18);
            p.Height = Ui.S(4);
            p.Margin = new Padding(0);
            return p;
        }

        public static void SetIcon(Button b, string icon, Color c)
        {
            if (string.IsNullOrEmpty(icon)) return;
            b.Image = Icons.Get(icon, c);
            b.ImageAlign = ContentAlignment.MiddleLeft;
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
    }

    /// <summary>ボタン用の小さなアイコン（線で描く。フォントに頼らないので Windows 7 でも同じに見える）</summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        public static readonly string[] All = { "run", "stop", "pause", "add", "edit", "copy", "delete", "check", "search", "folder", "log",
            "chart", "settings", "help", "undo", "export", "refresh", "close", "skip", "warn", "progress", "preview", "filter", "move", "ok", "list", "shield" };

        public static Image Get(string name, Color c)
        {
            string key = name + "_" + c.ToArgb();
            Image img;
            lock (cache) { if (cache.TryGetValue(key, out img)) return img; }
            int s = Math.Max(16, Ui.S(16));
            Bitmap bmp = new Bitmap(s, s);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float k = s / 16f;
                g.ScaleTransform(k, k);
                Draw(g, name, c);
            }
            lock (cache) cache[key] = bmp;
            return bmp;
        }

        private static void Draw(Graphics g, string name, Color c)
        {
            using (Pen p = new Pen(c, 1.6f))
            using (SolidBrush br = new SolidBrush(c))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "run":
                        g.FillPolygon(br, new PointF[] { new PointF(4, 2.5f), new PointF(13.5f, 8), new PointF(4, 13.5f) });
                        break;
                    case "stop":
                        g.FillRectangle(br, 3.5f, 3.5f, 9, 9);
                        break;
                    case "pause":
                        g.FillRectangle(br, 4, 3, 3, 10); g.FillRectangle(br, 9, 3, 3, 10);
                        break;
                    case "add":
                        using (Pen q = new Pen(c, 2.2f)) { g.DrawLine(q, 8, 2.5f, 8, 13.5f); g.DrawLine(q, 2.5f, 8, 13.5f, 8); }
                        break;
                    case "edit":
                        g.DrawLine(p, 3, 13, 12, 4); g.DrawLine(p, 10.5f, 2.5f, 13.5f, 5.5f); g.DrawLine(p, 3, 13, 2.5f, 13.5f);
                        g.DrawLine(p, 3, 13, 5.5f, 12.5f);
                        break;
                    case "copy":
                        g.DrawRectangle(p, 2.5f, 4.5f, 8, 9); g.DrawLines(p, new PointF[] { new PointF(5.5f, 2.5f), new PointF(13.5f, 2.5f), new PointF(13.5f, 11) });
                        break;
                    case "delete":
                        g.DrawLine(p, 2.5f, 4, 13.5f, 4); g.DrawLine(p, 6, 2.5f, 10, 2.5f);
                        g.DrawLines(p, new PointF[] { new PointF(4, 4), new PointF(5, 13.5f), new PointF(11, 13.5f), new PointF(12, 4) });
                        g.DrawLine(p, 6.8f, 6.5f, 7, 11.5f); g.DrawLine(p, 9.2f, 6.5f, 9, 11.5f);
                        break;
                    case "check":
                    case "ok":
                        using (Pen q = new Pen(c, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(q, new PointF[] { new PointF(2.5f, 8.5f), new PointF(6.5f, 12.5f), new PointF(13.5f, 3.5f) });
                        break;
                    case "search":
                        g.DrawEllipse(p, 2.5f, 2.5f, 8, 8);
                        using (Pen q = new Pen(c, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(q, 9.5f, 9.5f, 13.5f, 13.5f);
                        break;
                    case "folder":
                        g.DrawLines(p, new PointF[] { new PointF(2, 4), new PointF(6, 4), new PointF(7.5f, 5.5f), new PointF(14, 5.5f), new PointF(14, 13), new PointF(2, 13), new PointF(2, 4) });
                        g.DrawLine(p, 2, 7.5f, 14, 7.5f);
                        break;
                    case "log":
                    case "list":
                        g.DrawRectangle(p, 3, 2, 10, 12);
                        g.DrawLine(p, 5.5f, 5.5f, 10.5f, 5.5f); g.DrawLine(p, 5.5f, 8, 10.5f, 8); g.DrawLine(p, 5.5f, 10.5f, 9, 10.5f);
                        break;
                    case "chart":
                        g.FillRectangle(br, 2.5f, 9, 2.8f, 4.5f); g.FillRectangle(br, 6.6f, 5, 2.8f, 8.5f); g.FillRectangle(br, 10.7f, 2.5f, 2.8f, 11);
                        break;
                    case "progress":
                        g.DrawRectangle(p, 1.5f, 5.5f, 13, 5); g.FillRectangle(br, 3, 7, 6, 2);
                        break;
                    case "settings":
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(p, 8 + (float)Math.Cos(a) * 4.5f, 8 + (float)Math.Sin(a) * 4.5f, 8 + (float)Math.Cos(a) * 6.5f, 8 + (float)Math.Sin(a) * 6.5f);
                        }
                        g.DrawEllipse(p, 3.5f, 3.5f, 9, 9); g.DrawEllipse(p, 6.3f, 6.3f, 3.4f, 3.4f);
                        break;
                    case "help":
                        g.DrawEllipse(p, 1.5f, 1.5f, 13, 13);
                        g.DrawArc(p, 5.5f, 4, 5, 4.5f, 180, 230);
                        g.DrawLine(p, 8, 8.8f, 8, 10); g.FillEllipse(br, 7.1f, 11.3f, 1.8f, 1.8f);
                        break;
                    case "undo":
                    case "refresh":
                        g.DrawArc(p, 3, 3, 10, 10, name == "undo" ? 200 : 300, 270);
                        if (name == "undo") g.FillPolygon(br, new PointF[] { new PointF(1.5f, 4.5f), new PointF(6.5f, 4.2f), new PointF(3.3f, 8.8f) });
                        else g.FillPolygon(br, new PointF[] { new PointF(14.5f, 4.5f), new PointF(9.5f, 4.2f), new PointF(12.7f, 8.8f) });
                        break;
                    case "export":
                        g.DrawLine(p, 8, 2, 8, 10); g.DrawLines(p, new PointF[] { new PointF(4.5f, 6.5f), new PointF(8, 10), new PointF(11.5f, 6.5f) });
                        g.DrawLines(p, new PointF[] { new PointF(2.5f, 10.5f), new PointF(2.5f, 13.5f), new PointF(13.5f, 13.5f), new PointF(13.5f, 10.5f) });
                        break;
                    case "move":
                        g.DrawLine(p, 2, 8, 12, 8); g.DrawLines(p, new PointF[] { new PointF(8.5f, 4.5f), new PointF(12, 8), new PointF(8.5f, 11.5f) });
                        g.DrawLine(p, 14, 3, 14, 13);
                        break;
                    case "close":
                        g.DrawLine(p, 4, 4, 12, 12); g.DrawLine(p, 12, 4, 4, 12);
                        break;
                    case "skip":
                        g.DrawLines(p, new PointF[] { new PointF(3, 3.5f), new PointF(7.5f, 8), new PointF(3, 12.5f) });
                        g.DrawLines(p, new PointF[] { new PointF(8.5f, 3.5f), new PointF(13, 8), new PointF(8.5f, 12.5f) });
                        break;
                    case "warn":
                        g.DrawPolygon(p, new PointF[] { new PointF(8, 2), new PointF(14.5f, 13.5f), new PointF(1.5f, 13.5f) });
                        g.DrawLine(p, 8, 6, 8, 9.5f); g.FillEllipse(br, 7.1f, 10.8f, 1.8f, 1.8f);
                        break;
                    case "preview":
                        g.DrawRectangle(p, 2, 2, 9, 11);
                        g.DrawEllipse(p, 7.5f, 7.5f, 5, 5);
                        using (Pen q = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(q, 12, 12, 14.5f, 14.5f);
                        break;
                    case "filter":
                        g.DrawPolygon(p, new PointF[] { new PointF(2, 3), new PointF(14, 3), new PointF(9.5f, 8.5f), new PointF(9.5f, 13.5f), new PointF(6.5f, 12), new PointF(6.5f, 8.5f) });
                        break;
                    case "shield":
                        g.DrawPolygon(p, new PointF[] { new PointF(8, 1.5f), new PointF(13.5f, 3.5f), new PointF(12.5f, 10), new PointF(8, 14.5f), new PointF(3.5f, 10), new PointF(2.5f, 3.5f) });
                        break;
                }
            }
        }
    }
}
