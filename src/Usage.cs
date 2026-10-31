using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace AutoCopy
{
    /// <summary>容量の内訳に出す1ファイル</summary>
    public class UsageFile
    {
        public string Name;         // ファイル名
        public string Dir;          // 場所（表示用。フォルダ：調べたフォルダからの位置／ログ：PC＼ジョブ＼フォルダ）
        public string FullPath;     // 実際のパス（フォルダを調べた場合だけ）
        public string Pc, Job;      // ログの場合だけ
        public long Size;           // フォルダ：ファイルサイズ／ログ：コピーした容量の合計
        public long LastSize;       // ログ：最後にコピーしたときのサイズ
        public int Count;           // ログ：コピーした回数
        public DateTime Time;       // フォルダ：更新日時／ログ：最後にコピーした日時
    }

    /// <summary>フォルダ1つ分の集計</summary>
    public class UsageNode
    {
        public const int TopPerFolder = 60;
        public string Name;
        public string Path;         // 表示用（ルートからの位置）
        public UsageNode Parent;
        public long Size;           // 中のすべて（サブフォルダ含む）
        public int Files;
        public long DirectSize;     // このフォルダ直下のファイルだけ
        public int DirectFiles;
        public readonly Dictionary<string, UsageNode> Children = new Dictionary<string, UsageNode>(StringComparer.OrdinalIgnoreCase);
        public List<UsageFile> Top = new List<UsageFile>();   // 直下の大きいファイル（上位のみ）
    }

    public class ExtStat
    {
        public string Ext;
        public long Size;
        public int Files;
    }

    /// <summary>
    /// 容量の内訳（フォルダ別・種類別・大きいファイル）。
    /// 何十万ファイルでもメモリを使いすぎないよう、ファイルは上位だけ残す。
    /// </summary>
    public class UsageTree
    {
        public const int BiggestMax = 1000;
        public readonly UsageNode Root = new UsageNode();
        public readonly Dictionary<string, ExtStat> Ext = new Dictionary<string, ExtStat>(StringComparer.OrdinalIgnoreCase);
        public List<UsageFile> Biggest = new List<UsageFile>();
        public readonly List<string> Errors = new List<string>();
        public DateTime Checked = DateTime.Now;

        public UsageTree(string rootName)
        {
            Root.Name = rootName;
            Root.Path = rootName;
        }

        public long Total { get { return Root.Size; } }
        public int FileCount { get { return Root.Files; } }

        public void Add(string[] dirs, UsageFile f)
        {
            UsageNode n = Root;
            n.Size += f.Size; n.Files++;
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                UsageNode c;
                if (!n.Children.TryGetValue(d, out c))
                {
                    c = new UsageNode();
                    c.Name = d;
                    c.Parent = n;
                    c.Path = n == Root ? d : n.Path + "\\" + d;
                    n.Children[d] = c;
                }
                n = c;
                n.Size += f.Size; n.Files++;
            }
            n.DirectSize += f.Size;
            n.DirectFiles++;
            n.Top.Add(f);
            if (n.Top.Count > UsageNode.TopPerFolder * 2) Trim(n.Top, UsageNode.TopPerFolder);

            string ext = ExtOf(f.Name);
            ExtStat e;
            if (!Ext.TryGetValue(ext, out e)) { e = new ExtStat(); e.Ext = ext; Ext[ext] = e; }
            e.Size += f.Size; e.Files++;

            Biggest.Add(f);
            if (Biggest.Count > BiggestMax * 2) Trim(Biggest, BiggestMax);
        }

        /// <summary>集計の最後に呼ぶ（並べ替えと切り詰め）</summary>
        public void Finish()
        {
            Trim(Biggest, BiggestMax);
            Stack<UsageNode> st = new Stack<UsageNode>();
            st.Push(Root);
            while (st.Count > 0)
            {
                UsageNode n = st.Pop();
                Trim(n.Top, UsageNode.TopPerFolder);
                foreach (UsageNode c in n.Children.Values) st.Push(c);
            }
        }

        private static void Trim(List<UsageFile> list, int max)
        {
            list.Sort(delegate(UsageFile a, UsageFile b)
            {
                int c = b.Size.CompareTo(a.Size);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
        }

        /// <summary>種類（拡張子）別。容量の多い順。</summary>
        public List<ExtStat> ExtList()
        {
            List<ExtStat> l = new List<ExtStat>(Ext.Values);
            l.Sort(delegate(ExtStat a, ExtStat b) { int c = b.Size.CompareTo(a.Size); return c != 0 ? c : string.Compare(a.Ext, b.Ext, StringComparison.OrdinalIgnoreCase); });
            return l;
        }

        /// <summary>パス（表示用）からフォルダを探す。見つからなければ null。</summary>
        public UsageNode Find(string path)
        {
            if (string.IsNullOrEmpty(path) || path == Root.Path) return Root;
            UsageNode n = Root;
            foreach (string seg in path.Split('\\'))
            {
                UsageNode c;
                if (!n.Children.TryGetValue(seg, out c)) return null;
                n = c;
            }
            return n;
        }

        public static string ExtOf(string name)
        {
            string e = System.IO.Path.GetExtension(name ?? "");
            return string.IsNullOrEmpty(e) ? "" : e.ToLowerInvariant();
        }

        /// <summary>「.xlsx（Excel）」のような表示名</summary>
        public static string ExtLabel(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return "（拡張子なし）";
            string k = KindOf(ext);
            return k == null ? ext : ext + "（" + k + "）";
        }

        public static string KindOf(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".xlsx": case ".xls": case ".xlsm": case ".xlsb": case ".csv": return "Excel・表";
                case ".docx": case ".doc": case ".docm": case ".rtf": return "Word";
                case ".pptx": case ".ppt": return "PowerPoint";
                case ".pdf": return "PDF";
                case ".txt": case ".log": return "テキスト";
                case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp": case ".tif": case ".tiff": case ".heic": case ".psd": return "画像";
                case ".mp4": case ".mov": case ".avi": case ".wmv": case ".mkv": case ".m4v": case ".mts": return "動画";
                case ".mp3": case ".wav": case ".m4a": case ".wma": return "音声";
                case ".zip": case ".7z": case ".rar": case ".lzh": case ".cab": return "圧縮";
                case ".dwg": case ".dxf": case ".jww": case ".sldprt": case ".sldasm": case ".step": case ".stp": return "図面・CAD";
                case ".pst": case ".ost": case ".msg": case ".eml": return "メール";
                case ".iso": case ".vhd": case ".vhdx": case ".vmdk": return "ディスクイメージ";
                case ".bak": case ".tmp": return "バックアップ・一時";
                case ".mdb": case ".accdb": return "Access";
                case ".exe": case ".msi": case ".dll": return "プログラム";
            }
            return null;
        }
    }

    /// <summary>フォルダの中身を調べて容量の内訳を作る（読むだけ。ファイルは開かない）</summary>
    public static class DiskUsage
    {
        public static UsageTree Scan(string root, CancellationToken ct)
        {
            return Scan(root, ct, null);
        }

        /// <summary>progress は 0.2秒ごとに「ファイル数・容量・今のフォルダ」で呼ばれる。ct が取り消されると OperationCanceledException。</summary>
        public static UsageTree Scan(string root, CancellationToken ct, Action<int, long, string> progress)
        {
            long nextReport = 0;
            Stopwatch sw = Stopwatch.StartNew();
            string plain = PathUtil.Plain(root).TrimEnd('\\');
            string lroot = PathUtil.Long(plain);
            if (!Directory.Exists(lroot)) throw new DirectoryNotFoundException("フォルダが見つかりません：" + plain);
            UsageTree t = new UsageTree(plain);
            Stack<string> stack = new Stack<string>();
            stack.Push("");
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string rel = stack.Pop();
                if (progress != null && sw.ElapsedMilliseconds >= nextReport)
                {
                    progress(t.FileCount, t.Total, rel);
                    nextReport = sw.ElapsedMilliseconds + 200;
                }
                string dirPath = rel.Length == 0 ? lroot : lroot.TrimEnd('\\') + "\\" + rel;
                string[] segs = rel.Length == 0 ? new string[0] : rel.Split('\\');
                try
                {
                    foreach (FileSystemInfo fsi in new DirectoryInfo(dirPath).EnumerateFileSystemInfos())
                    {
                        string childRel = rel.Length == 0 ? fsi.Name : rel + "\\" + fsi.Name;
                        if ((fsi.Attributes & FileAttributes.Directory) != 0)
                        {
                            if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            // 空のフォルダも一覧に出す
                            List<string> s = new List<string>(segs);
                            s.Add(fsi.Name);
                            Touch(t, s.ToArray());
                            stack.Push(childRel);
                        }
                        else
                        {
                            FileInfo fi = (FileInfo)fsi;
                            UsageFile f = new UsageFile();
                            f.Name = fi.Name;
                            f.Dir = rel.Length == 0 ? "（直下）" : rel;   // 調べたフォルダからの位置
                            f.FullPath = plain + "\\" + childRel;
                            f.Size = fi.Length;
                            f.Time = fi.LastWriteTime;
                            f.Count = 1;
                            t.Add(segs, f);
                            if ((t.FileCount & 255) == 0)
                            {
                                ct.ThrowIfCancellationRequested();   // 1フォルダにファイルが多くても止まれるように
                                if (progress != null && sw.ElapsedMilliseconds >= nextReport)
                                {
                                    progress(t.FileCount, t.Total, rel);
                                    nextReport = sw.ElapsedMilliseconds + 200;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (rel.Length == 0) throw;
                    if (t.Errors.Count < 1000) t.Errors.Add(rel + "：" + ex.Message);
                }
            }
            t.Finish();
            return t;
        }

        private static void Touch(UsageTree t, string[] segs)
        {
            UsageNode n = t.Root;
            foreach (string d in segs)
            {
                UsageNode c;
                if (!n.Children.TryGetValue(d, out c))
                {
                    c = new UsageNode();
                    c.Name = d; c.Parent = n;
                    c.Path = n == t.Root ? d : n.Path + "\\" + d;
                    n.Children[d] = c;
                }
                n = c;
            }
        }
    }
    /// <summary>割合の1項目</summary>
    public class ShareItem
    {
        public string Label, Sub, Tip;
        public long Bytes;
        public object Data;         // クリックで開ける場合（フォルダなど）
        public bool Rest;           // 「その他」など、まとめた項目（灰色）
    }

    /// <summary>容量の内訳を割合の項目（グラフの行）にする</summary>
    public static class UsageItems
    {
        public const int MaxRows = 25;

        public static string Pct(long v, long t)
        {
            return t <= 0 ? "" : (v * 100.0 / t).ToString("0.0") + "%";
        }

        /// <summary>フォルダの中身（サブフォルダと直下のファイル）を割合の項目にする</summary>
        public static List<ShareItem> Items(UsageTree tree, UsageNode n, bool logMode)
        {
            List<ShareItem> list = new List<ShareItem>();
            foreach (UsageNode c in n.Children.Values)
            {
                ShareItem it = new ShareItem();
                it.Label = c.Name + "\\";
                bool meta = !logMode && n.Parent == null && string.Equals(c.Name, AppPaths.DestMetaFolder, StringComparison.OrdinalIgnoreCase);
                it.Sub = (logMode && n.Parent == null ? "PC" : logMode && n.Parent != null && n.Parent.Parent == null ? "ジョブ" : "フォルダ")
                    + "・" + Fmt.Count(c.Files) + "ファイル" + (meta ? "（自動コピーの削除済み・退避）" : "");
                it.Bytes = c.Size;
                it.Data = c.Files > 0 || c.Children.Count > 0 ? c : null;
                it.Tip = Fmt.Count(c.Files) + "ファイル";
                list.Add(it);
            }
            long shownFiles = 0; int shownCount = 0;
            foreach (UsageFile f in n.Top)
            {
                ShareItem it = new ShareItem();
                it.Label = f.Name;
                it.Sub = FileSub(f, logMode);
                it.Tip = it.Sub;
                it.Bytes = f.Size;
                list.Add(it);
                shownFiles += f.Size; shownCount++;
            }
            if (n.DirectFiles > shownCount)
            {
                ShareItem it = new ShareItem();
                it.Label = "ほかのファイル " + Fmt.Count(n.DirectFiles - shownCount) + "件";
                it.Sub = "このフォルダ直下の小さいファイル";
                it.Bytes = n.DirectSize - shownFiles;
                it.Rest = true;
                list.Add(it);
            }
            list.Sort(delegate(ShareItem a, ShareItem b)
            {
                if (a.Rest != b.Rest) return a.Rest ? 1 : -1;
                int c = b.Bytes.CompareTo(a.Bytes);
                return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            if (list.Count > MaxRows)
            {
                ShareItem other = new ShareItem();
                other.Rest = true;
                int folded = 0;
                for (int i = MaxRows - 1; i < list.Count; i++) { other.Bytes += list[i].Bytes; folded++; }
                other.Label = "その他 " + Fmt.Count(folded) + "項目";
                other.Sub = "小さい順の残り";
                list.RemoveRange(MaxRows - 1, list.Count - (MaxRows - 1));
                list.Add(other);
            }
            return list;
        }

        public static string FileSub(UsageFile f, bool logMode)
        {
            if (logMode)
                return "ファイル・" + Fmt.Count(f.Count) + "回コピー（1回 " + Fmt.Size(f.LastSize) + "）・最後 " + f.Time.ToString("yyyy/MM/dd HH:mm");
            return "ファイル・" + f.Time.ToString("yyyy/MM/dd HH:mm") + " 更新";
        }

        /// <summary>種類（拡張子）別の割合の項目</summary>
        public static List<ShareItem> ExtItems(UsageTree tree)
        {
            List<ShareItem> list = new List<ShareItem>();
            List<ExtStat> exts = tree.ExtList();
            ShareItem other = null;
            int folded = 0;
            for (int i = 0; i < exts.Count; i++)
            {
                if (i >= 19 && exts.Count > 20)
                {
                    if (other == null) { other = new ShareItem(); other.Rest = true; }
                    other.Bytes += exts[i].Size;
                    folded++;
                    continue;
                }
                ShareItem it = new ShareItem();
                it.Label = UsageTree.ExtLabel(exts[i].Ext);
                it.Sub = Fmt.Count(exts[i].Files) + "ファイル";
                it.Tip = it.Sub;
                it.Bytes = exts[i].Size;
                list.Add(it);
            }
            if (other != null)
            {
                other.Label = "その他 " + Fmt.Count(folded) + "種類";
                other.Sub = "容量の少ない種類";
                list.Add(other);
            }
            return list;
        }

    }
}
