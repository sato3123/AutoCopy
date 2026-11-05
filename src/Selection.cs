using System;
using System.Collections.Generic;

namespace AutoCopy
{
    /// <summary>条件で選ぶときの、一覧の1行の情報</summary>
    public class SelectRow
    {
        public string Path = "";        // ファイル（場所を含む）
        public long Size = -1;          // バイト（-1 = なし）
        public long Mtime;              // 更新日時（UTC ticks、0 = なし）
        public List<string> Tags = new List<string>();   // 種類・ジョブなど（「種類」で絞り込む）
    }

    /// <summary>選ぶ条件（指定したものすべてに当てはまる行が「条件に合う」）</summary>
    public class SelectCriteria
    {
        public List<string> Words = new List<string>();   // 名前・場所に含む文字（どれか1つ）
        public bool WordsNot;                              // true = 含まないもの
        public List<string> Exts = new List<string>();    // 拡張子（.tmp など、どれか1つ）
        public long MinSize = -1, MaxSize = -1;
        public DateTime? Before, After;                    // 更新日時（ローカル）
        public HashSet<string> Tags;                       // null = すべての種類

        public bool IsEmpty
        {
            get { return Words.Count == 0 && Exts.Count == 0 && MinSize < 0 && MaxSize < 0 && !Before.HasValue && !After.HasValue && Tags == null; }
        }

        public bool Matches(SelectRow r)
        {
            string p = r.Path ?? "";
            if (Words.Count > 0)
            {
                bool any = false;
                foreach (string w in Words) if (p.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) { any = true; break; }
                if (any == WordsNot) return false;
            }
            if (Exts.Count > 0)
            {
                bool any = false;
                foreach (string e in Exts) if (p.EndsWith(e, StringComparison.OrdinalIgnoreCase)) { any = true; break; }
                if (!any) return false;
            }
            if (MinSize >= 0 && (r.Size < 0 || r.Size < MinSize)) return false;
            if (MaxSize >= 0 && (r.Size < 0 || r.Size > MaxSize)) return false;
            if (Before.HasValue || After.HasValue)
            {
                if (r.Mtime <= 0) return false;
                DateTime t = new DateTime(r.Mtime, DateTimeKind.Utc).ToLocalTime();
                if (Before.HasValue && t >= Before.Value) return false;
                if (After.HasValue && t < After.Value) return false;
            }
            if (Tags != null)
            {
                bool any = false;
                foreach (string t in r.Tags) if (Tags.Contains(t)) { any = true; break; }
                if (!any) return false;
            }
            return true;
        }

        public static List<string> Split(string s)
        {
            List<string> l = new List<string>();
            foreach (string x in (s ?? "").Split(new char[] { ';', '；', ',', '、', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries))
                if (x.Trim().Length > 0) l.Add(x.Trim());
            return l;
        }
    }

    public enum SelectAction { None, Check, Uncheck, Only, KeepOne }

    /// <summary>重複のまとまりで残す1つの決め方</summary>
    public static class KeepRule
    {
        public const string ShortPath = "short", Newest = "new", Oldest = "old", Matching = "match";

        /// <summary>まとまりの中で残すものの番号（rows は同じまとまりの行）</summary>
        public static int Pick(List<SelectRow> rows, string rule, SelectCriteria c)
        {
            int best = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (best < 0) { best = i; continue; }
                SelectRow a = rows[i], b = rows[best];
                int cmp;
                switch (rule)
                {
                    case Newest: cmp = -a.Mtime.CompareTo(b.Mtime); break;
                    case Oldest: cmp = a.Mtime.CompareTo(b.Mtime); break;
                    case Matching:
                        {
                            bool ma = c != null && c.Matches(a), mb = c != null && c.Matches(b);
                            cmp = ma == mb ? 0 : ma ? -1 : 1;
                            break;
                        }
                    default: cmp = 0; break;
                }
                if (cmp == 0) cmp = a.Path.Length.CompareTo(b.Path.Length);
                if (cmp == 0) cmp = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                if (cmp < 0) best = i;
            }
            return best;
        }
    }

}
