using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoCopy
{
    /// <summary>
    /// ログ保管先へ、送信待ちのログ（1回分）を1度だけ書き足す。
    /// 書き足す前に、保管先のファイルの長さを手元（送信待ちのファイルの隣の「.pos」）に記録しておく。
    /// 送信の途中で終了した・送れたのに手元のファイルを消す前に終了した場合は、次の送信でその長さまで戻してから書き足す
    /// （二重にも途中までにもならない）。保管先のファイル全体を書き直さないので、ログが大きくても転送量は1回分だけ。
    /// </summary>
    public static class LogDelivery
    {
        public const string PosSuffix = ".pos";

        public static void AppendOnce(string batchFile, string destination, CancellationToken ct)
        {
            SafeFs.Guard(destination);
            SafeFs.CreateDirectory(Path.GetDirectoryName(destination));
            string ld = PathUtil.Long(destination);
            string pos = batchFile + PosSuffix;
            using (DestLock.Acquire(ld, ct))
            using (FileStream output = new FileStream(ld, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                long start, recorded;
                if (File.Exists(pos) && long.TryParse(File.ReadAllText(pos).Trim(), out recorded) && recorded >= 0 && recorded <= output.Length)
                {
                    start = recorded;
                    if (output.Length > start) output.SetLength(start);   // 前回の途中まで（または全部）書いた分を取り消して書き直す
                }
                else
                {
                    start = output.Length;
                    WriteDurable(pos, start.ToString());
                }
                output.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[65536];
                using (FileStream input = new FileStream(batchFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, n);
                    }
                }
                output.Flush(true);
            }
        }

        /// <summary>送り終えた：送信待ちのファイルと記録を片付ける（この順で消す）</summary>
        public static void Done(string batchFile)
        {
            File.Delete(batchFile);
            try { File.Delete(batchFile + PosSuffix); } catch (Exception) { }
        }

        /// <summary>送信待ちのファイルがもうない記録（片付けの途中で終了した）を消す</summary>
        public static void CleanOrphans(string outbox)
        {
            try
            {
                foreach (string f in Directory.GetFiles(outbox, "*" + PosSuffix))
                    if (!File.Exists(f.Substring(0, f.Length - PosSuffix.Length))) File.Delete(f);
            }
            catch (Exception) { }
        }

        private static void WriteDurable(string path, string text)
        {
            byte[] b = Encoding.ASCII.GetBytes(text);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 256, FileOptions.WriteThrough))
            {
                fs.Write(b, 0, b.Length);
                fs.Flush(true);
            }
        }
    }
}
