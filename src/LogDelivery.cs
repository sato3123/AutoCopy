using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoCopy
{
    public static class LogDelivery
    {
        // The marker and payload are committed together. A retry after a crash is a no-op.
        public static void AppendOnce(string batchFile, string destination, CancellationToken ct)
        {
            SafeFs.Guard(destination);
            SafeFs.CreateDirectory(Path.GetDirectoryName(destination));
            string ld = PathUtil.Long(destination);
            string marker = "#AutoCopyBatch:" + Path.GetFileName(batchFile);
            using (DestLock.Acquire(ld, ct))
            {
                if (File.Exists(ld))
                {
                    using (StreamReader reader = new StreamReader(ld, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (line == marker) return;
                        }
                    }
                }
                string temp = PathUtil.Long(SafeFs.TempName(destination));
                try
                {
                    using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536];
                        foreach (string input in new string[] { ld, batchFile })
                        {
                            if (!File.Exists(input)) continue;
                            using (FileStream stream = File.OpenRead(input))
                            {
                                int n;
                                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                                { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, n); }
                            }
                        }
                        byte[] end = Encoding.UTF8.GetBytes(marker + "\r\n");
                        output.Write(end, 0, end.Length);
                        output.Flush(true);
                    }
                    ct.ThrowIfCancellationRequested();
                    SafeFs.Commit(temp, ld);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
}
