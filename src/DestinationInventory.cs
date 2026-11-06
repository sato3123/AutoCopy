using System;
using System.Collections.Generic;
using System.IO;

namespace AutoCopy
{
    // One SMB directory enumeration replaces one metadata request per candidate.
    // Commit still revalidates the destination under DestLock.
    public sealed class DestinationInventory
    {
        public int EnumeratedDirectories { get; private set; }
        private readonly Dictionary<string, Dictionary<string, FileInfo>> directories =
            new Dictionary<string, Dictionary<string, FileInfo>>(StringComparer.OrdinalIgnoreCase);

        public FileInfo Get(string path)
        {
            string parent = Path.GetDirectoryName(path);
            Dictionary<string, FileInfo> files;
            if (!directories.TryGetValue(parent, out files))
            {
                EnumeratedDirectories++;
                files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (FileInfo f in new DirectoryInfo(PathUtil.Long(parent)).EnumerateFiles())
                        files[f.Name] = f;
                }
                catch (DirectoryNotFoundException) { }
                directories[parent] = files;
            }
            FileInfo item;
            return files.TryGetValue(Path.GetFileName(path), out item) ? item : null;
        }
    }
}
