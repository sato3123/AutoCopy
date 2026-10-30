using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// グラフの共通部分（追加の部品を使わず GDI+ で描く。Windows 7〜11 共通）。
    /// 色は検証済みの分類色を決まった順に使う。文字は系列の色ではなく文字色で書く。
    /// </summary>
    public abstract class ChartBase : Control
    {
        public static readonly Color[] Series = {
            ColorTranslator.FromHtml("#2a78d6"), ColorTranslator.FromHtml("#eb6834"), ColorTranslator.FromHtml("#1baf7a"),
            ColorTranslator.FromHtml("#eda100"), ColorTranslator.FromHtml("#e87ba4"), ColorTranslator.FromHtml("#008300"),
            ColorTranslator.FromHtml("#6250d6"), ColorTranslator.FromHtml("#e34948") };
        public static readonly Color OtherColor = ColorTranslator.FromHtml("#9a9893");
        public static readonly Color Surface = ColorTranslator.FromHtml("#fcfcfb");
        public static readonly Color Ink = ColorTranslator.FromHtml("#0b0b0b");
        public static readonly Color Ink2 = ColorTranslator.FromHtml("#52514e");
        public static readonly Color Grid = ColorTranslator.FromHtml("#e4e3df");
        public static readonly Color Axis = ColorTranslator.FromHtml("#b9b8b2");

        protected class Hit { public RectangleF Rect; public string Text; }
        protected readonly List<Hit> hits = new List<Hit>();
        private readonly ToolTip tip = new ToolTip();
        private Hit current;

        protected ChartBase()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Surface;
            Font = Ui.Base;
            tip.InitialDelay = 0;
            tip.ReshowDelay = 0;
            tip.AutoPopDelay = 20000;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Surface);
            hits.Clear();
            Draw(g, ClientRectangle);
        }

        protected abstract void Draw(Graphics g, Rectangle area);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hit found = null;
            foreach (Hit h in hits)
            {
                RectangleF r = h.Rect;
                r.Inflate(2, 4); // 当たり判定は見た目より少し大きく
                if (r.Contains(e.Location)) { found = h; break; }
            }
            if (found == current) return;
            current = found;
            if (found == null) tip.Hide(this);
            else tip.Show(found.Text, this, e.X + 14, e.Y + 14);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            current = null;
            tip.Hide(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>軸の目盛り：単位（B/KB/MB/GB）・刻み・上限を決める</summary>
        protected static void NiceScale(long max, out double unit, out string unitName, out double step, out double top)
        {
            string[] names = { "B", "KB", "MB", "GB", "TB" };
            unit = 1; unitName = "B";
            for (int i = 0; i < names.Length; i++)
            {
                double u = Math.Pow(1024, i);
                if (max / u >= 1 || i == 0) { unit = u; unitName = names[i]; }
            }
            double m = Math.Max(1e-9, max / unit);
            double rough = m / 4;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double norm = rough / mag;
            double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
            step = nice * mag;
            top = Math.Ceiling(m / step) * step;
            if (top <= 0) top = step;
        }

        protected static string Num(double v)
        {
            return v >= 100 || v == Math.Floor(v) ? v.ToString("#,0") : v.ToString("0.#");
        }

        protected void Legend(Graphics g, float x, float y, float maxWidth, string[] names, Color[] colors, out float bottom)
        {
            float cx = x, cy = y;
            using (Brush ink = new SolidBrush(Ink2))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    SizeF sz = g.MeasureString(names[i], Font);
                    float w = 14 + 6 + sz.Width + 16;
                    if (cx + w > x + maxWidth && cx > x) { cx = x; cy += 22; }
                    using (Brush b = new SolidBrush(colors[i])) FillRound(g, b, new RectangleF(cx, cy + 3, 14, 14), 3);
                    g.DrawString(names[i], Font, ink, cx + 20, cy + 1);
                    cx += w;
                }
            }
            bottom = cy + 22;
        }

        protected static void FillRound(Graphics g, Brush b, RectangleF r, float radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            float rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (rad < 0.5f) { g.FillRectangle(b, r); return; }
            using (GraphicsPath p = new GraphicsPath())
            {
                float d = rad * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        protected void Empty(Graphics g, Rectangle area, string text)
        {
            using (Brush ink = new SolidBrush(Ink2))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(text, Font, ink, area, sf);
            }
        }
    }

    /// <summary>PCごとのコピー容量（横棒。新規と上書きの積み上げ）</summary>
    public class PcVolumeChart : ChartBase
    {
        private List<CopyStat> rows = new List<CopyStat>();

        public void SetData(List<CopyStat> stats)
        {
            rows = new List<CopyStat>();
            foreach (CopyStat s in stats) if (!(s.Folder == "合計" && s.Pc == null)) rows.Add(s);
            // 多すぎる場合は上位20台＋その他
            if (rows.Count > 20)
            {
                CopyStat other = new CopyStat();
                other.Folder = "その他 " + (rows.Count - 19) + "台";
                for (int i = 19; i < rows.Count; i++)
                {
                    other.CopiedBytes += rows[i].CopiedBytes; other.OverwrittenBytes += rows[i].OverwrittenBytes;
                    other.Copied += rows[i].Copied; other.Overwritten += rows[i].Overwritten;
                }
                rows = rows.GetRange(0, 19);
                rows.Add(other);
            }
            Invalidate();
        }

        public int PreferredHeight { get { return 90 + Math.Max(1, rows.Count) * 34; } }

        protected override void Draw(Graphics g, Rectangle area)
        {
            long max = 0;
            foreach (CopyStat s in rows) max = Math.Max(max, s.TotalBytes);
            if (rows.Count == 0 || max == 0) { Empty(g, area, "この期間のコピーはありません"); return; }

            float legendBottom;
            Legend(g, 16, 10, area.Width - 32, new string[] { "新規コピー", "上書き" }, new Color[] { Series[0], Series[1] }, out legendBottom);

            float labelW = 190, valueW = 90;
            float left = 16 + labelW, right = area.Width - 16 - valueW;
            float plotW = Math.Max(50, right - left);
            float rowH = 34, barH = 18;
            float y0 = legendBottom + 8;
            float plotBottom = y0 + rows.Count * rowH;

            double unit, step, top; string unitName;
            NiceScale(max, out unit, out unitName, out step, out top);

            using (Pen grid = new Pen(Grid, 1))
            using (Pen axis = new Pen(Axis, 1))
            using (Brush ink = new SolidBrush(Ink))
            using (Brush ink2 = new SolidBrush(Ink2))
            using (Font small = new Font(Font.FontFamily, 8.25f))
            {
                // 目盛り（控えめに）
                for (double v = 0; v <= top + 1e-9; v += step)
                {
                    float x = left + (float)(v / top) * plotW;
                    g.DrawLine(v == 0 ? axis : grid, x, y0 - 4, x, plotBottom);
                    string t = Num(v) + (v == 0 ? "" : " " + unitName);
                    SizeF sz = g.MeasureString(t, small);
                    g.DrawString(t, small, ink2, x - sz.Width / 2, plotBottom + 4);
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    CopyStat s = rows[i];
                    float cy = y0 + i * rowH + (rowH - barH) / 2;
                    // 名前（フォルダ名）と PC 名
                    string name = s.Folder ?? "";
                    g.DrawString(Clip(g, name, Font, labelW - 8), Font, ink, 16, cy - 1);
                    if (!string.IsNullOrEmpty(s.Pc))
                        g.DrawString(Clip(g, s.Pc, small, labelW - 8), small, ink2, 16, cy + 15);

                    float wNew = (float)(s.CopiedBytes / unit / top) * plotW;
                    float wOver = (float)(s.OverwrittenBytes / unit / top) * plotW;
                    float x = left + 1;
                    if (wNew > 0)
                    {
                        RectangleF r = new RectangleF(x, cy, Math.Max(1, wNew - (wOver > 0 ? 2 : 0)), barH);
                        using (Brush b = new SolidBrush(Series[0])) FillRound(g, b, r, wOver > 0 ? 1 : 4);
                        AddHit(r, name, "新規コピー", s.CopiedBytes, s.Copied, s);
                        x += wNew;
                    }
                    if (wOver > 0)
                    {
                        RectangleF r = new RectangleF(x, cy, Math.Max(1, wOver), barH);
                        using (Brush b = new SolidBrush(Series[1])) FillRound(g, b, r, 4);
                        AddHit(r, name, "上書き", s.OverwrittenBytes, s.Overwritten, s);
                        x += wOver;
                    }
                    // 合計は棒の右端に文字色で
                    g.DrawString(Fmt.Size(s.TotalBytes), Font, ink, x + 6, cy - 1);
                }
            }
        }

        private void AddHit(RectangleF r, string name, string kind, long bytes, int count, CopyStat s)
        {
            Hit h = new Hit();
            h.Rect = r;
            h.Text = name + (string.IsNullOrEmpty(s.Pc) ? "" : "（" + s.Pc + "）") + "\n" + kind + "：" + Fmt.Size(bytes) + "（" + Fmt.Count(count) + "件）"
                + "\n合計：" + Fmt.Size(s.TotalBytes);
            hits.Add(h);
        }

        private static string Clip(Graphics g, string s, Font f, float width)
        {
            if (g.MeasureString(s, f).Width <= width) return s;
            while (s.Length > 1 && g.MeasureString(s + "…", f).Width > width) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }
    }

    /// <summary>日ごとのコピー容量（縦棒。PCごとの積み上げ）</summary>
    public class DailyVolumeChart : ChartBase
    {
        private List<DateTime> days = new List<DateTime>();
        private List<string> pcs = new List<string>();          // 色の順（容量の多い順、7台＋その他）
        private List<long[]> values = new List<long[]>();       // days × pcs
        private bool monthly;

        public void SetData(SortedDictionary<DateTime, Dictionary<string, long>> daily)
        {
            days.Clear(); pcs.Clear(); values.Clear();
            if (daily.Count == 0) { Invalidate(); return; }
            DateTime first = DateTime.MaxValue, last = DateTime.MinValue;
            Dictionary<string, long> totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<DateTime, Dictionary<string, long>> d in daily)
            {
                if (d.Key < first) first = d.Key;
                if (d.Key > last) last = d.Key;
                foreach (KeyValuePair<string, long> p in d.Value)
                {
                    long v; totals.TryGetValue(p.Key, out v); totals[p.Key] = v + p.Value;
                }
            }
            List<string> ranked = new List<string>(totals.Keys);
            ranked.Sort(delegate(string a, string b) { return totals[b].CompareTo(totals[a]); });
            bool fold = ranked.Count > 8;
            for (int i = 0; i < ranked.Count && i < (fold ? 7 : 8); i++) pcs.Add(ranked[i]);
            if (fold) pcs.Add("その他");

            monthly = (last - first).TotalDays > 92;
            DateTime cur = monthly ? new DateTime(first.Year, first.Month, 1) : first;
            while (cur <= last)
            {
                days.Add(cur);
                long[] row = new long[pcs.Count];
                DateTime next = monthly ? cur.AddMonths(1) : cur.AddDays(1);
                foreach (KeyValuePair<DateTime, Dictionary<string, long>> d in daily)
                {
                    if (d.Key < cur || d.Key >= next) continue;
                    foreach (KeyValuePair<string, long> p in d.Value)
                    {
                        int idx = pcs.IndexOf(p.Key);
                        if (idx < 0) idx = pcs.Count - 1; // その他
                        row[idx] += p.Value;
                    }
                }
                values.Add(row);
                cur = next;
            }
            Invalidate();
        }

        private Color ColorOf(int i)
        {
            if (pcs[i] == "その他") return OtherColor;
            return Series[i % Series.Length];
        }

        protected override void Draw(Graphics g, Rectangle area)
        {
            long max = 0;
            foreach (long[] r in values) { long s = 0; foreach (long v in r) s += v; max = Math.Max(max, s); }
            if (days.Count == 0 || max == 0) { Empty(g, area, "この期間のコピーはありません"); return; }

            Color[] colors = new Color[pcs.Count];
            for (int i = 0; i < pcs.Count; i++) colors[i] = ColorOf(i);
            float legendBottom;
            Legend(g, 16, 10, area.Width - 32, pcs.ToArray(), colors, out legendBottom);

            double unit, step, top; string unitName;
            NiceScale(max, out unit, out unitName, out step, out top);

            float left = 78, right = area.Width - 20, y0 = legendBottom + 14, bottom = area.Height - 40;
            float plotW = Math.Max(50, right - left), plotH = Math.Max(40, bottom - y0);
            float slot = plotW / days.Count;
            float barW = Math.Max(2, Math.Min(42, slot * 0.62f));

            using (Pen grid = new Pen(Grid, 1))
            using (Pen axis = new Pen(Axis, 1))
            using (Brush ink = new SolidBrush(Ink))
            using (Brush ink2 = new SolidBrush(Ink2))
            using (Font small = new Font(Font.FontFamily, 8.25f))
            {
                for (double v = 0; v <= top + 1e-9; v += step)
                {
                    float y = bottom - (float)(v / top) * plotH;
                    g.DrawLine(v == 0 ? axis : grid, left - 4, y, right, y);
                    string t = Num(v) + " " + unitName;
                    SizeF sz = g.MeasureString(t, small);
                    g.DrawString(t, small, ink2, left - 8 - sz.Width, y - sz.Height / 2);
                }

                // 日付ラベルは重ならない間隔で
                float labelW = g.MeasureString(monthly ? "2026/10" : "10/30", small).Width + 10;
                int every = Math.Max(1, (int)Math.Ceiling(labelW / slot));

                for (int i = 0; i < days.Count; i++)
                {
                    float cx = left + slot * i + slot / 2;
                    float yb = bottom;
                    long dayTotal = 0;
                    foreach (long v in values[i]) dayTotal += v;
                    int lastSeg = -1;
                    for (int k = 0; k < pcs.Count; k++) if (values[i][k] > 0) lastSeg = k;
                    for (int k = 0; k < pcs.Count; k++)
                    {
                        long v = values[i][k];
                        if (v <= 0) continue;
                        float h = (float)(v / unit / top) * plotH;
                        bool isTop = k == lastSeg;
                        // 積み上げの間に 2px のすき間
                        RectangleF r = new RectangleF(cx - barW / 2, yb - h, barW, Math.Max(1, h - (k == 0 || h < 3 ? 0 : 2)));
                        using (Brush b = new SolidBrush(colors[k])) FillRound(g, b, r, isTop ? 3 : 0.4f);
                        Hit hit = new Hit();
                        hit.Rect = r;
                        hit.Text = Label(days[i]) + "　" + pcs[k] + "\n" + Fmt.Size(v) + "\nこの日の合計：" + Fmt.Size(dayTotal);
                        hits.Add(hit);
                        yb -= h;
                    }
                    if (i % every == 0)
                    {
                        string t = Label(days[i]);
                        SizeF sz = g.MeasureString(t, small);
                        g.DrawString(t, small, ink2, cx - sz.Width / 2, bottom + 6);
                    }
                }
            }
        }

        private string Label(DateTime d)
        {
            return monthly ? d.ToString("yyyy/MM") : d.ToString("M/d");
        }
    }
}
