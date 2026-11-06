using System;
using System.Collections.Generic;
using System.IO;

namespace AutoCopy
{
    /// <summary>コピー先のファイル1件の情報（一覧から取ったもの）</summary>
    public sealed class DestEntry
    {
        public bool Exists;
        public long Length;
        public DateTime LastWriteTimeUtc;

        public static DestEntry Of(FileInfo f)
        {
            if (f == null || !f.Exists) return null;
            DestEntry e = new DestEntry(); e.Exists = true; e.Length = f.Length; e.LastWriteTimeUtc = f.LastWriteTimeUtc;
            return e;
        }
    }

    /// <summary>
    /// ネットワークのコピー先：ファイルごとに問い合わせる代わりに、フォルダごとに1回一覧を取る。
    /// 置き換えの直前には、DestLock の中で改めて実際の状態を確かめる（DestExpect）。
    /// 持つのは大きさ・更新日時だけ（メモリを抑える）。持つ件数が MaxEntries を超えたら、それ以降はファイルごとに問い合わせる。
    /// </summary>
    public sealed class DestinationInventory
    {
        public static int MaxEntries = 1000000;
        public int EnumeratedDirectories { get; private set; }
        public int CachedEntries { get; private set; }
        private readonly Dictionary<string, Dictionary<string, DestEntry>> directories =
            new Dictionary<string, Dictionary<string, DestEntry>>(StringComparer.OrdinalIgnoreCase);

        public DestEntry Get(string path)
        {
            string parent = Path.GetDirectoryName(path);
            Dictionary<string, DestEntry> files;
            if (!directories.TryGetValue(parent, out files))
            {
                if (CachedEntries >= MaxEntries) return DestEntry.Of(new FileInfo(PathUtil.Long(path)));
                EnumeratedDirectories++;
                files = new Dictionary<string, DestEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (FileInfo f in new DirectoryInfo(PathUtil.Long(parent)).EnumerateFiles())
                    {
                        DestEntry e = new DestEntry(); e.Exists = true; e.Length = f.Length; e.LastWriteTimeUtc = f.LastWriteTimeUtc;
                        files[f.Name] = e;
                    }
                }
                catch (DirectoryNotFoundException) { }
                CachedEntries += files.Count;
                directories[parent] = files;
            }
            DestEntry item;
            return files.TryGetValue(Path.GetFileName(path), out item) ? item : null;
        }
    }
}
