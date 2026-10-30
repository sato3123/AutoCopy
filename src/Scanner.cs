using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AutoCopy
{
    public class ScanItem
    {
        public long Size;
        public long Mtime;          // UTC ticks
        public FileAttributes Attr;
        public bool Recent;         // 更新から数秒以内（保存中の可能性）
    }

    public class ScanResult
    {
        public Dictionary<string, ScanItem> Files = new Dictionary<string, ScanItem>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int ExcludedCount;
        public long ExcludedBytes;
        public List<string> ExcludedSamples = new List<string>();
        public List<string> Errors = new List<string>();
        public long TotalBytes;
    }

    /// <summary>
    /// フォルダの走査。ファイルは開かず、フォルダ一覧のAPIだけでサイズ・日時・属性を得る。
    /// </summary>
    public static class Scanner
    {
        public static ScanResult Scan(string root, bool includeSubdirs, ExcludeMatcher matcher, bool skipMeta, CancellationToken ct)
        {
            ScanResult res = new ScanResult();
            string lroot = PathUtil.Long(root);
            long recentLimit = DateTime.UtcNow.AddSeconds(-5).Ticks;

            Stack<string> stack = new Stack<string>();
            stack.Push("");
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string rel = stack.Pop();
                string dirPath = rel.Length == 0 ? lroot : lroot.TrimEnd('\\') + "\\" + rel;
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = new DirectoryInfo(dirPath).EnumerateFileSystemInfos();
                }
                catch (Exception ex)
                {
                    if (rel.Length == 0) throw;
                    res.Errors.Add(rel + ": " + ex.Message);
                    continue;
                }
                try
                {
                    foreach (FileSystemInfo fsi in entries)
                    {
                        string name = fsi.Name;
                        string childRel = rel.Length == 0 ? name : rel + "\\" + name;
                        FileAttributes a = fsi.Attributes;
                        if ((a & FileAttributes.Directory) != 0)
                        {
                            if (!includeSubdirs) continue;
                            if (skipMeta && rel.Length == 0 && string.Equals(name, AppPaths.DestMetaFolder, StringComparison.OrdinalIgnoreCase)) continue;
                            if ((a & FileAttributes.ReparsePoint) != 0) continue; // ジャンクションのループ防止
                            if (matcher != null && matcher.ExcludeDir(childRel, name, a)) { res.ExcludedCount++; continue; }
                            res.Dirs.Add(childRel);
                            stack.Push(childRel);
                        }
                        else
                        {
                            FileInfo fi = (FileInfo)fsi;
                            long size = fi.Length;
                            DateTime mt = fi.LastWriteTimeUtc;
                            if (name.EndsWith(".ckpart", StringComparison.OrdinalIgnoreCase)) continue;
                            if (matcher != null && matcher.ExcludeFile(childRel, name, size, mt, a))
                            {
                                res.ExcludedCount++;
                                res.ExcludedBytes += size;
                                if (res.ExcludedSamples.Count < 5000) res.ExcludedSamples.Add(childRel);
                                continue;
                            }
                            ScanItem it = new ScanItem();
                            it.Size = size;
                            it.Mtime = mt.Ticks;
                            it.Attr = a;
                            it.Recent = mt.Ticks > recentLimit;
                            res.Files[childRel] = it;
                            res.TotalBytes += size;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (rel.Length == 0 && res.Files.Count == 0 && res.Dirs.Count == 0) throw;
                    res.Errors.Add((rel.Length == 0 ? "(ルート)" : rel) + ": " + ex.Message);
                }
            }
            return res;
        }
    }
}
