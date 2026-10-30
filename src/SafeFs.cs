using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AutoCopy
{
    public class SourceProtectedException : Exception
    {
        public SourceProtectedException(string path)
            : base("コピー元の中を変更しようとしたため止めました: " + PathUtil.Plain(path)) { }
    }

    /// <summary>
    /// ファイルを変更する操作はすべてこのクラスを通す。
    /// どれかのジョブのコピー元の中にあるパスへの書き込み・名前変更・削除・移動は必ず拒否する（元データ保護）。
    /// </summary>
    public static class SafeFs
    {
        private static readonly object sync = new object();
        private static List<string> protectedRoots = new List<string>();

        public static event Action<string> Blocked;

        /// <summary>保護するコピー元を登録する（設定を読み込むたびに呼ぶ）</summary>
        public static void SetProtectedSources(IEnumerable<string> sources)
        {
            List<string> list = new List<string>();
            foreach (string s in sources)
            {
                if (string.IsNullOrEmpty(s)) continue;
                string n = PathUtil.Normalize(s);
                if (n.Length == 0) continue;
                list.Add(n);
                string unc = PathUtil.Normalize(NetAccess.ToUncIfMapped(n));
                if (!string.Equals(unc, n, StringComparison.OrdinalIgnoreCase)) list.Add(unc);
            }
            lock (sync) { protectedRoots = list; }
        }

        public static void Guard(string path)
        {
            string n = PathUtil.Normalize(path);
            string unc = PathUtil.Normalize(NetAccess.ToUncIfMapped(n));
            List<string> roots;
            lock (sync) { roots = protectedRoots; }
            foreach (string r in roots)
            {
                if (PathUtil.IsSameOrInside(n, r) || PathUtil.IsSameOrInside(unc, r))
                {
                    if (Blocked != null) Blocked(n);
                    throw new SourceProtectedException(path);
                }
            }
        }

        public static void CreateDirectory(string dir)
        {
            Guard(dir);
            string l = PathUtil.Long(dir);
            if (!Directory.Exists(l)) Directory.CreateDirectory(l);
        }

        public static void WriteText(string path, string text)
        {
            Guard(path);
            CreateDirectory(Path.GetDirectoryName(PathUtil.Plain(path)));
            File.WriteAllText(PathUtil.Long(path), text, new UTF8Encoding(false));
        }

        public static void AppendText(string path, string text)
        {
            Guard(path);
            CreateDirectory(Path.GetDirectoryName(PathUtil.Plain(path)));
            File.AppendAllText(PathUtil.Long(path), text, new UTF8Encoding(false));
        }

        /// <summary>ファイルまたはフォルダの移動・名前変更（コピー先の中だけ）</summary>
        public static void Move(string from, string to)
        {
            Guard(from);
            Guard(to);
            string lf = PathUtil.Long(from), lt = PathUtil.Long(to);
            string parent = Path.GetDirectoryName(PathUtil.Plain(to));
            if (!string.IsNullOrEmpty(parent)) CreateDirectory(parent);
            if (Directory.Exists(lf)) Directory.Move(lf, lt);
            else File.Move(lf, lt);
        }

        public static void DeleteFile(string path)
        {
            Guard(path);
            string l = PathUtil.Long(path);
            if (File.Exists(l))
            {
                File.SetAttributes(l, FileAttributes.Normal);
                File.Delete(l);
            }
        }

        public static void DeleteDirectoryTree(string path)
        {
            Guard(path);
            string l = PathUtil.Long(path);
            if (!Directory.Exists(l)) return;
            foreach (string f in Directory.GetFiles(l, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(l, true);
        }

        /// <summary>空のフォルダだけ削除する</summary>
        public static bool DeleteEmptyDirectory(string path)
        {
            Guard(path);
            string l = PathUtil.Long(path);
            if (!Directory.Exists(l)) return false;
            using (IEnumerator<string> e = Directory.EnumerateFileSystemEntries(l).GetEnumerator())
            {
                if (e.MoveNext()) return false;
            }
            Directory.Delete(l, false);
            return true;
        }

        /// <summary>
        /// コピー先\_AutoCopy\(削除済み|退避)\日時\相対パス へ移動する。完全には消さない。
        /// 戻り値は移動先パス。
        /// </summary>
        public static string MoveToTrash(string destRoot, string rel, string kind, string batch)
        {
            string from = PathUtil.Combine(destRoot, rel);
            string to = PathUtil.Combine(PathUtil.Combine(PathUtil.Combine(destRoot, AppPaths.DestMetaFolder), kind), batch);
            to = PathUtil.Combine(to, rel);
            string lt = PathUtil.Long(to);
            if (File.Exists(lt) || Directory.Exists(lt)) to = to + "." + DateTime.Now.Ticks;
            Move(from, to);
            return to;
        }

        /// <summary>
        /// ファイルを一時ファイルにコピーしてから置き換える。
        /// コピー元は読み取り専用で開く。コピー先の更新日時はコピー元と同じにする。MD5 も計算して返す。
        /// </summary>
        public static byte[] CopyFile(string src, string dst, DateTime srcMtimeUtc, Throttle throttle, CancellationToken ct)
        {
            Guard(dst);
            string ls = PathUtil.Long(src), ld = PathUtil.Long(dst);
            string tmp = dst + ".ckpart";
            Guard(tmp);
            string lt = PathUtil.Long(tmp);
            string parent = Path.GetDirectoryName(PathUtil.Plain(dst));
            CreateDirectory(parent);

            byte[] hash;
            byte[] buf = new byte[1024 * 1024];
            using (MD5 md5 = MD5.Create())
            {
                try
                {
                    using (FileStream fin = new FileStream(ls, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, buf.Length, FileOptions.SequentialScan))
                    using (FileStream fout = new FileStream(lt, FileMode.Create, FileAccess.Write, FileShare.None, buf.Length))
                    {
                        int n;
                        while ((n = fin.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            fout.Write(buf, 0, n);
                            md5.TransformBlock(buf, 0, n, null, 0);
                            if (throttle != null) throttle.Consume(n, ct);
                        }
                        md5.TransformFinalBlock(buf, 0, 0);
                    }
                }
                catch
                {
                    try { if (File.Exists(lt)) File.Delete(lt); } catch (Exception) { }
                    throw;
                }
                hash = md5.Hash;
            }
            File.SetLastWriteTimeUtc(lt, srcMtimeUtc);

            if (File.Exists(ld))
            {
                FileAttributes a = File.GetAttributes(ld);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(ld, a & ~FileAttributes.ReadOnly);
                try
                {
                    File.Replace(lt, ld, null, true);
                }
                catch (Exception)
                {
                    File.Delete(ld);
                    File.Move(lt, ld);
                }
                File.SetLastWriteTimeUtc(ld, srcMtimeUtc);
            }
            else
            {
                File.Move(lt, ld);
            }
            return hash;
        }
    }

    /// <summary>転送速度の上限（KB/秒）</summary>
    public class Throttle
    {
        private readonly long bytesPerSec;
        private readonly Stopwatch sw = Stopwatch.StartNew();
        private long total;
        private readonly object sync = new object();

        public Throttle(int kbps) { bytesPerSec = (long)kbps * 1024; }

        public void Consume(int bytes, CancellationToken ct)
        {
            if (bytesPerSec <= 0) return;
            int waitMs;
            lock (sync)
            {
                total += bytes;
                double expectedMs = total * 1000.0 / bytesPerSec;
                waitMs = (int)(expectedMs - sw.ElapsedMilliseconds);
            }
            if (waitMs > 0) ct.WaitHandle.WaitOne(Math.Min(waitMs, 5000));
        }
    }
}
