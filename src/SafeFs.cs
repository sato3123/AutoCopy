using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AutoCopy
{
    /// <summary>ジョブの結果の分類</summary>
    public static class JobOutcome
    {
        public const string Success = "成功", Partial = "一部失敗", Canceled = "中断", Failed = "失敗";
    }

    /// <summary>
    /// エラーの原因の分類（ネットワークの切断・容量不足・アクセス拒否・使用中 など）。
    /// ネットワークの切断と容量不足は、続けても失敗するだけなので、ジョブを止める。
    /// </summary>
    public static class ErrorKind
    {
        public const string Network = "network", DiskFull = "diskfull", AccessDenied = "access", Locked = "locked",
            NotFound = "notfound", TooLong = "toolong", Changed = "changed", Other = "other";

        public static string Classify(Exception ex)
        {
            if (ex == null) return Other;
            if (ex is SourceChangedException) return Changed;
            if (ex is UnauthorizedAccessException) return AccessDenied;
            if (ex is PathTooLongException) return TooLong;
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException) return NotFound;
            int code = Win32Code(ex);
            switch (code)
            {
                case 112: case 39: return DiskFull;                        // ERROR_DISK_FULL / HANDLE_DISK_FULL
                case 5: return AccessDenied;
                case 32: case 33: return Locked;                           // 共有違反・ロック違反
                case 2: case 3: return NotFound;
                case 206: return TooLong;
                case 51: case 53: case 54: case 55: case 59: case 64: case 65: case 67: case 121:
                case 1231: case 1232: case 1236: case 1222: case 2250:
                    return Network;                                         // ネットワークの名前が見つからない・切断 など
            }
            if (ex.InnerException != null && ex.InnerException != ex) return Classify(ex.InnerException);
            return Other;
        }

        public static int Win32Code(Exception ex)
        {
            int hr = System.Runtime.InteropServices.Marshal.GetHRForException(ex);
            if ((hr & 0xFFFF0000) == 0x80070000) return hr & 0xFFFF;
            return -1;
        }

        public static string Label(string kind)
        {
            switch (kind)
            {
                case Network: return "ネットワークの切断";
                case DiskFull: return "容量不足";
                case AccessDenied: return "アクセス拒否";
                case Locked: return "使用中";
                case NotFound: return "見つからない";
                case TooLong: return "パスが長すぎる";
                case Changed: return "コピー中に変更";
            }
            return "その他";
        }

        /// <summary>ジョブを止めるべき原因か（続けても同じ失敗をくり返す）</summary>
        public static bool IsFatal(string kind) { return kind == Network || kind == DiskFull; }

        /// <summary>「【容量不足】…」の形の説明</summary>
        public static string Describe(Exception ex)
        {
            return "【" + Label(Classify(ex)) + "】" + ex.Message;
        }
    }

    /// <summary>ネットワークの切断・容量不足などで、ジョブを途中で止めた</summary>
    public class JobAbortException : Exception
    {
        public readonly string Kind;
        public JobAbortException(string kind, string message, Exception inner) : base(message, inner) { Kind = kind; }
    }

    /// <summary>コピーしている間に、コピー元のファイルが変わった（保存中など）。次回コピーする</summary>
    public class SourceChangedException : IOException
    {
        public SourceChangedException(string path) : base("コピー中にコピー元が変わったため、次回コピーします（保存中の可能性）: " + PathUtil.Plain(path)) { }
    }

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

        private static List<string> syncRoots = new List<string>();

        /// <summary>
        /// 同期ジョブの「このPCのフォルダ」を登録する。ここへは CopyIntoSync だけが書き込める
        /// （新しいファイルの追加と、古い版を控えてからの更新だけ。削除・移動・名前変更は今までどおり拒否）。
        /// </summary>
        public static void SetSyncRoots(IEnumerable<string> roots)
        {
            List<string> list = new List<string>();
            foreach (string s in roots)
            {
                if (string.IsNullOrEmpty(s)) continue;
                string n = PathUtil.Normalize(s);
                if (n.Length == 0) continue;
                list.Add(n);
                string unc = PathUtil.Normalize(NetAccess.ToUncIfMapped(n));
                if (!string.Equals(unc, n, StringComparison.OrdinalIgnoreCase)) list.Add(unc);
            }
            lock (sync) { syncRoots = list; }
        }

        public static bool IsInSyncRoot(string path)
        {
            string n = PathUtil.Normalize(path);
            string unc = PathUtil.Normalize(NetAccess.ToUncIfMapped(n));
            List<string> roots;
            lock (sync) { roots = syncRoots; }
            foreach (string r in roots)
                if (PathUtil.IsSameOrInside(n, r) || PathUtil.IsSameOrInside(unc, r)) return true;
            return false;
        }

        /// <summary>
        /// 同期：共有フォルダのファイルを、このPCのフォルダへ取り込む。
        /// すでにあるファイルは、置き換える前に backupRoot\rel へ「コピー」して控える（元のファイルは置き換えの瞬間まで残る）。
        /// </summary>
        public static byte[] CopyIntoSync(string from, string toLocal, string rel, DateTime mtimeUtc, string backupRoot, CancellationToken ct)
        {
            return CopyIntoSync(from, toLocal, rel, mtimeUtc, backupRoot, ct, null);
        }

        public static byte[] CopyIntoSync(string from, string toLocal, string rel, DateTime mtimeUtc, string backupRoot, CancellationToken ct, Action<long> onBytes)
        {
            if (!IsInSyncRoot(toLocal))
            {
                if (Blocked != null) Blocked(PathUtil.Normalize(toLocal));
                throw new SourceProtectedException(toLocal);
            }
            string lto = PathUtil.Long(toLocal);
            string parent = Path.GetDirectoryName(PathUtil.Plain(toLocal));
            if (!Directory.Exists(PathUtil.Long(parent))) Directory.CreateDirectory(PathUtil.Long(parent));
            if (File.Exists(lto) && backupRoot != null)
            {
                string b = PathUtil.Combine(backupRoot, rel);
                string lb = PathUtil.Long(b);
                Directory.CreateDirectory(Path.GetDirectoryName(lb));
                if (File.Exists(lb)) lb = lb + "." + DateTime.Now.Ticks;
                File.Copy(lto, lb, false);
                if (!SameBytes(lto, lb)) { TryDelete(lb); throw new IOException("控えを正しく作れなかったため、取り込みを止めました: " + rel); }
            }
            return CopyVerifiedAndCommit(from, toLocal, mtimeUtc, null, ct, onBytes);
        }

        /// <summary>一時ファイルの名前（同じファイルへ同時に書いてもぶつからないよう、毎回違う名前にする）</summary>
        public static string TempName(string dst)
        {
            return dst + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + PartSuffix;
        }

        public const string PartSuffix = ".ckpart", AsideSuffix = ".ckold";

        /// <summary>
        /// コピーの本体（CopyFile・CopyIntoSync 共通）：
        /// 1) 一時ファイル（毎回違う名前）に書き、ディスクに書き込みを確定させる（電源断・強制終了でも、元のファイルは変わらない）
        /// 2) コピー中にコピー元が変わったら（大きさ・更新日時）一時ファイルを捨てて、少し待ってやり直す（3回まで）
        /// 3) 中止されたら、置き換える直前でも止める（コピー先は元のまま）
        /// 4) 置き換えは元のファイルを失わない方法で行う（失敗したら元のファイルに戻す）
        /// </summary>
        private static byte[] CopyVerifiedAndCommit(string src, string dst, DateTime srcMtimeUtc, Throttle throttle, CancellationToken ct, Action<long> onBytes)
        {
            string ls = PathUtil.Long(src), ld = PathUtil.Long(dst);
            for (int attempt = 1; ; attempt++)
            {
                string lt = PathUtil.Long(TempName(dst));
                try
                {
                    FileInfo before = new FileInfo(ls);
                    if (!before.Exists) throw new FileNotFoundException("コピー元が見つかりません: " + src);
                    long bs = before.Length, bm = before.LastWriteTimeUtc.Ticks;
                    long copied;
                    byte[] hash = CopyCore(ls, lt, throttle, ct, onBytes, out copied);
                    FileInfo after = new FileInfo(ls);
                    after.Refresh();
                    if (!after.Exists || after.Length != bs || after.LastWriteTimeUtc.Ticks != bm || copied != bs)
                    {
                        TryDelete(lt);
                        if (attempt >= 3) throw new SourceChangedException(src);
                        if (ct.WaitHandle.WaitOne(700 * attempt)) ct.ThrowIfCancellationRequested();
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();   // 中止：置き換える前に止める（コピー先は元のまま）
                    File.SetLastWriteTimeUtc(lt, srcMtimeUtc);
                    Commit(lt, ld);
                    try { File.SetLastWriteTimeUtc(ld, srcMtimeUtc); } catch (Exception ex) { Logger.Error(null, "更新日時を設定できません: " + PathUtil.Plain(dst) + "　" + ex.Message); }
                    return hash;
                }
                catch
                {
                    TryDelete(lt);   // 自分で作った一時ファイルだけ消す
                    throw;
                }
            }
        }

        private static byte[] CopyCore(string ls, string lt, Throttle throttle, CancellationToken ct, Action<long> onBytes, out long copied)
        {
            copied = 0;
            byte[] buf = new byte[1024 * 1024];
            using (MD5 md5 = MD5.Create())
            {
                using (FileStream fin = new FileStream(ls, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buf.Length, FileOptions.SequentialScan))
                using (FileStream fout = new FileStream(lt, FileMode.CreateNew, FileAccess.Write, FileShare.None, buf.Length))
                {
                    int n;
                    while ((n = fin.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        fout.Write(buf, 0, n);
                        md5.TransformBlock(buf, 0, n, null, 0);
                        if (throttle != null) throttle.Consume(n, ct);
                        copied += n;
                        if (onBytes != null) onBytes(copied);
                    }
                    md5.TransformFinalBlock(buf, 0, 0);
                    fout.Flush(true);   // ディスクに書き込みを確定させてから置き換える
                }
                return md5.Hash;
            }
        }

        /// <summary>
        /// 一時ファイルで、コピー先を置き換える。元のファイルを失わない：
        /// File.Replace（元を控えに残す）→ だめなら 元を横へ名前変更 → 新しいものを入れる → 入らなければ元に戻す。
        /// </summary>
        public static void Commit(string lt, string ld)
        {
            if (!File.Exists(ld)) { File.Move(lt, ld); return; }
            FileAttributes a = File.GetAttributes(ld);
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(ld, a & ~FileAttributes.ReadOnly);
            string aside = ld + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + AsideSuffix;
            try
            {
                File.Replace(lt, ld, aside, true);
                TryDelete(aside);
                return;
            }
            catch (Exception ex)
            {
                // Replace の途中で失敗：元のファイルが控えの名前になっていれば戻す
                if (!File.Exists(ld) && File.Exists(aside))
                {
                    try { File.Move(aside, ld); }
                    catch (Exception ex2) { throw new IOException("上書きに失敗し、元のファイルを " + PathUtil.Plain(aside) + " に残しました: " + ex2.Message, ex); }
                }
                if (!File.Exists(lt)) throw;   // 一時ファイルがない：これ以上はしない（元のファイルはそのまま）
            }
            // Replace が使えない場所（一部の NAS など）：元を横へ移してから入れ替える
            File.Move(ld, aside);   // 失敗したら例外（元のファイルはそのまま）
            try { File.Move(lt, ld); }
            catch
            {
                try { File.Move(aside, ld); }
                catch (Exception ex3) { Logger.Error(null, "上書きに失敗しました。元のファイルは " + PathUtil.Plain(aside) + " に残っています: " + ex3.Message); }
                throw;
            }
            TryDelete(aside);
        }

        /// <summary>2つのファイルの中身が同じか（大きさ → 1バイトずつ）</summary>
        public static bool SameBytes(string a, string b)
        {
            string la = PathUtil.Long(a), lb = PathUtil.Long(b);
            FileInfo fa = new FileInfo(la), fb = new FileInfo(lb);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            byte[] x = new byte[1 << 20], y = new byte[1 << 20];
            using (FileStream sa = new FileStream(la, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            using (FileStream sb = new FileStream(lb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            {
                while (true)
                {
                    int n = ReadFull(sa, x), m = ReadFull(sb, y);
                    if (n != m) return false;
                    if (n == 0) return true;
                    for (int i = 0; i < n; i++) if (x[i] != y[i]) return false;
                }
            }
        }

        private static int ReadFull(Stream s, byte[] buf)
        {
            int t = 0, n;
            while (t < buf.Length && (n = s.Read(buf, t, buf.Length - t)) > 0) t += n;
            return t;
        }

        private static void TryDelete(string l)
        {
            try { if (File.Exists(l)) File.Delete(l); }
            catch (Exception ex) { Logger.Error(null, "一時ファイルを消せません: " + PathUtil.Plain(l) + "　" + ex.Message); }
        }

        /// <summary>コピー先の「_AutoCopy」フォルダを隠しフォルダにする（ない場合は何もしない）</summary>
        /// <summary>
        /// 同期：共有フォルダで削除されたファイルを、確認のうえこのPCのフォルダからも外す。
        /// 消さずに控えのフォルダ（backupRoot）へ移す（同期のフォルダの中だけ。それ以外は止める）。
        /// </summary>
        public static string MoveOutOfSync(string localPath, string rel, string backupRoot)
        {
            if (!IsInSyncRoot(localPath))
            {
                if (Blocked != null) Blocked(PathUtil.Normalize(localPath));
                throw new SourceProtectedException(localPath);
            }
            string b = PathUtil.Combine(backupRoot, rel);
            string lb = PathUtil.Long(b);
            Directory.CreateDirectory(Path.GetDirectoryName(lb));
            if (File.Exists(lb)) lb = lb + "." + DateTime.Now.Ticks;
            string lf = PathUtil.Long(localPath);
            try { File.Move(lf, lb); }
            catch (IOException)
            {
                // 別のドライブなど：控えにコピーし、中身が同じことを確かめてから元を消す
                File.Copy(lf, lb, false);
                if (!SameBytes(lf, lb)) { TryDelete(lb); throw new IOException("控えを正しく作れなかったため、このPCのファイルは消しませんでした: " + rel); }
                File.SetAttributes(lf, FileAttributes.Normal);
                File.Delete(lf);
            }
            return lb;
        }

        public static void HideMeta(string destRoot)
        {
            try
            {
                string l = PathUtil.Long(PathUtil.Combine(destRoot, AppPaths.DestMetaFolder));
                if (!Directory.Exists(l)) return;
                Guard(l);
                FileAttributes a = File.GetAttributes(l);
                if ((a & FileAttributes.Hidden) == 0) File.SetAttributes(l, a | FileAttributes.Hidden);
            }
            catch (SourceProtectedException) { throw; }
            catch (Exception) { }
        }

        /// <summary>自動コピーのコピー元（またはその中）か（調べるだけ。記録は残さない）</summary>
        public static bool IsProtected(string path)
        {
            string n = PathUtil.Normalize(path);
            string unc = PathUtil.Normalize(NetAccess.ToUncIfMapped(n));
            List<string> roots;
            lock (sync) { roots = protectedRoots; }
            foreach (string r in roots)
                if (PathUtil.IsSameOrInside(n, r) || PathUtil.IsSameOrInside(unc, r)) return true;
            return false;
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

        /// <summary>BOM 付き UTF-8 で書く（Windows 7 のメモ帳でも文字化けしないように）</summary>
        public static void WriteTextBom(string path, string text)
        {
            Guard(path);
            CreateDirectory(Path.GetDirectoryName(PathUtil.Plain(path)));
            File.WriteAllText(PathUtil.Long(path), text, new UTF8Encoding(true));
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
            HideMeta(destRoot);
            return to;
        }

        /// <summary>
        /// ファイルを一時ファイルにコピーしてから置き換える。
        /// コピー元は読み取り専用で開く。コピー先の更新日時はコピー元と同じにする。MD5 も計算して返す。
        /// </summary>
        public static byte[] CopyFile(string src, string dst, DateTime srcMtimeUtc, Throttle throttle, CancellationToken ct)
        {
            return CopyFile(src, dst, srcMtimeUtc, throttle, ct, null);
        }

        /// <summary>onBytes：コピーできたバイト数（進み具合の表示用）</summary>
        public static byte[] CopyFile(string src, string dst, DateTime srcMtimeUtc, Throttle throttle, CancellationToken ct, Action<long> onBytes)
        {
            Guard(dst);
            Guard(dst + PartSuffix);
            string parent = Path.GetDirectoryName(PathUtil.Plain(dst));
            CreateDirectory(parent);
            return CopyVerifiedAndCommit(src, dst, srcMtimeUtc, throttle, ct, onBytes);
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
