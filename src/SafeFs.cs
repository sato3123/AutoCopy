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
            NotFound = "notfound", TooLong = "toolong", Changed = "changed", DestChanged = "destchanged", Other = "other";

        public static string Classify(Exception ex)
        {
            if (ex == null) return Other;
            if (ex is SourceChangedException) return Changed;
            if (ex is DestChangedException) return DestChanged;
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
                case DestChanged: return "コピー先が変更された";
            }
            return "その他";
        }

        /// <summary>ジョブを止めるべき原因か（続けても同じ失敗をくり返す）</summary>
        public static bool IsFatal(string kind) { return kind == Network || kind == DiskFull; }

        public const string AdvicePrefix = "次にすること：";

        /// <summary>
        /// エラーの文から、利用者が次にすることを案内する（分からないときは null）。
        /// 「【ネットワークの切断】」などの分類の印と、IDやパスワードの誤りを見て決める。
        /// </summary>
        public static string Advice(string text, bool credError)
        {
            string t = text ?? "";
            if (credError || t.Contains("パスワード") || t.Contains("ユーザー名") || t.Contains("資格情報") || t.Contains("ログオン失敗"))
                return AdvicePrefix + "ジョブの設定の「接続・コピー先の整理」で、共有フォルダのIDとパスワードが正しいか確かめてください。";
            if (t.Contains("【" + Label(Network) + "】") || t.Contains("接続できません") || t.Contains("ネットワーク"))
                return AdvicePrefix + "ネットワーク（Wi-Fi・LANケーブル）と、コピー先のPC・NASの電源を確かめてください。つながれば、次の実行で続きからコピーします。";
            if (t.Contains("【" + Label(DiskFull) + "】") || t.Contains("空き容量") || t.Contains("ディスクに十分な空き領域"))
                return AdvicePrefix + "コピー先の空き容量を増やしてください（メイン画面の「容量の内訳」で大きいフォルダを確かめられます）。空けば次の実行で続きからコピーします。";
            if (t.Contains("【" + Label(AccessDenied) + "】") || t.Contains("アクセスが拒否"))
                return AdvicePrefix + "そのフォルダに書き込む権限があるか、読み取り専用になっていないかを確かめてください（会社のPCは管理者に相談してください）。";
            if (t.Contains("【" + Label(Locked) + "】") || t.Contains("別のプロセスが使用中"))
                return AdvicePrefix + "そのファイルを開いているアプリ（Excel など）を閉じてください。次の実行でコピーし直します。";
            if (t.Contains("【" + Label(TooLong) + "】"))
                return AdvicePrefix + "フォルダの階層を浅くするか、フォルダ名・ファイル名を短くしてください。";
            if (t.Contains("【" + Label(NotFound) + "】") || t.Contains("見つかりません"))
                return AdvicePrefix + "コピー元・コピー先のフォルダがあるか（名前を変えていないか、USBメモリ・外付けディスクがつながっているか）を確かめてください。";
            return null;
        }

        /// <summary>エラーの文の後ろに「次にすること」を付ける（付けられないとき・付いているときはそのまま）</summary>
        public static string WithAdvice(string text, bool credError)
        {
            if (text == null || text.Contains(AdvicePrefix)) return text;
            string a = Advice(text, credError);
            return a == null ? text : text + "　" + a;
        }

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

    /// <summary>確かめた後にコピー先が変わった（別のPC・アプリ・ジョブが書いた）：上書きせず、次回に判断し直す</summary>
    public class DestChangedException : IOException
    {
        public DestChangedException(string path, string why) : base("確認した後にコピー先が変わったため、上書きしませんでした（" + why + "）。次回、確かめ直します: " + PathUtil.Plain(path)) { }
    }

    /// <summary>
    /// 置き換える直前に確かめる、コピー先の状態（予定を立てたときの状態）。
    /// MustBeAbsent：新しくコピーする（コピー先にまだない）はず。Size ≥ 0：上書きする元のファイルの大きさ・更新日時。
    /// </summary>
    public class DestExpect
    {
        public bool MustBeAbsent;
        public long Size = -1, Mtime;
        public static DestExpect Absent() { DestExpect e = new DestExpect(); e.MustBeAbsent = true; return e; }
        public static DestExpect Same(long size, long mtime) { DestExpect e = new DestExpect(); e.Size = size; e.Mtime = mtime; return e; }

        /// <summary>今のコピー先が予定どおりか。違えば理由。</summary>
        public string Check(string ld)
        {
            FileInfo fi = new FileInfo(ld);
            if (MustBeAbsent) return fi.Exists ? "同じ名前のファイルができていた" : null;
            if (Size < 0) return null;
            if (!fi.Exists) return null;   // 消えていた：上書きの元がない（新しく置くだけ）
            if (fi.Length != Size || Math.Abs(fi.LastWriteTimeUtc.Ticks - Mtime) > TimeSpan.TicksPerSecond * 2) return "大きさか更新日時が変わっていた";
            return null;
        }
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
            return CopyIntoSync(from, toLocal, rel, mtimeUtc, backupRoot, ct, onBytes, null);
        }

        public static byte[] CopyIntoSync(string from, string toLocal, string rel, DateTime mtimeUtc, string backupRoot, CancellationToken ct, Action<long> onBytes, DestExpect expect, Action<long, long> onCommitted = null)
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
            return CopyVerifiedAndCommit(from, toLocal, mtimeUtc, null, ct, onBytes, expect, onCommitted);
        }

        /// <summary>一時ファイルの名前（同じファイルへ同時に書いてもぶつからないよう、毎回違う名前にする）</summary>
        public static string TempName(string dst)
        {
            return dst + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + PartSuffix;
        }

        public const string PartSuffix = ".ckpart", AsideSuffix = ".ckold", LockSuffix = ".cklock";

        /// <summary>この時間以内に更新されたファイル・書き込み中のファイルは、コピーの後で中身（MD5）を確かめ直す</summary>
        public static TimeSpan RecentForHashCheck = TimeSpan.FromHours(1);

        /// <summary>
        /// コピーの本体（CopyFile・CopyIntoSync 共通）：
        /// 1) 一時ファイル（毎回違う名前）に書き、ディスクに書き込みを確定させる（電源断・強制終了でも、元のファイルは変わらない）
        /// 2) コピー中にコピー元が変わったら（大きさ・更新日時）一時ファイルを捨てて、少し待ってやり直す（3回まで）
        /// 3) 中止されたら、置き換える直前でも止める（コピー先は元のまま）
        /// 4) 置き換えは元のファイルを失わない方法で行う（失敗したら元のファイルに戻す）
        /// </summary>
        private static byte[] CopyVerifiedAndCommit(string src, string dst, DateTime srcMtimeUtc, Throttle throttle, CancellationToken ct, Action<long> onBytes, DestExpect expect, Action<long, long> onCommitted = null)
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
                    bool changed = !after.Exists || after.Length != bs || after.LastWriteTimeUtc.Ticks != bm || copied != bs;
                    // 大きさ・更新日時が同じでも中身が変わることがある（書き込み中・更新日時を戻すアプリ）：
                    // 更新されたばかり・他のアプリが書き込みのために開いている ときは、中身を読み直して確かめる
                    if (!changed && NeedsHashCheck(ls, bm))
                    {
                        byte[] again = HashOf(ls, ct);
                        if (!SameHash(hash, again)) changed = true;
                    }
                    if (changed)
                    {
                        TryDelete(lt);
                        if (attempt >= 3) throw new SourceChangedException(src);
                        if (ct.WaitHandle.WaitOne(700 * attempt)) ct.ThrowIfCancellationRequested();
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();   // 中止：置き換える前に止める（コピー先は元のまま）
                    File.SetLastWriteTimeUtc(lt, new DateTime(bm, DateTimeKind.Utc));
                    using (DestLock.Acquire(ld, ct))   // 同じコピー先へ同時に書かない（このPCの他のジョブ・他のPC）
                    {
                        if (expect != null)
                        {
                            string why = expect.Check(ld);
                            if (why != null) throw new DestChangedException(dst, why);
                        }
                        ct.ThrowIfCancellationRequested();
                        Commit(lt, ld);
                    }
                    try { File.SetLastWriteTimeUtc(ld, new DateTime(bm, DateTimeKind.Utc)); } catch (Exception ex) { Logger.Error(null, "更新日時を設定できません: " + PathUtil.Plain(dst) + "　" + ex.Message); }
                    if (onCommitted != null) onCommitted(copied, bm);
                    return hash;
                }
                catch
                {
                    TryDelete(lt);   // 自分で作った一時ファイルだけ消す
                    throw;
                }
            }
        }

        /// <summary>コピーの後で中身を確かめ直すか：更新されたばかり、または他のアプリが書き込みのために開いている</summary>
        public static bool NeedsHashCheck(string ls, long mtimeTicks)
        {
            if (DateTime.UtcNow.Ticks - mtimeTicks < RecentForHashCheck.Ticks) return true;
            try
            {
                // 書き込みを許さずに開けなければ、誰かが書き込みのために開いている
                using (new FileStream(ls, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1)) { }
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static byte[] HashOf(string ls, CancellationToken ct)
        {
            byte[] buf = new byte[1024 * 1024];
            using (MD5 md5 = MD5.Create())
            using (FileStream fin = new FileStream(ls, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buf.Length, FileOptions.SequentialScan))
            {
                int n;
                while ((n = fin.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    md5.TransformBlock(buf, 0, n, null, 0);
                }
                md5.TransformFinalBlock(buf, 0, 0);
                return md5.Hash;
            }
        }

        private static bool SameHash(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
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
            if (!File.Exists(ld)) { File.Move(lt, ld); return; }   // 新しく置く：すでにあれば例外（上書きしない）
            FileAttributes a = File.GetAttributes(ld);
            bool ro = (a & FileAttributes.ReadOnly) != 0;
            if (ro) File.SetAttributes(ld, a & ~FileAttributes.ReadOnly);
            string aside = ld + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + AsideSuffix;
            // 強制終了・電源断に備えて、置き換えの途中であることを記録する（次の起動で元に戻す）
            string journal = CommitJournal.Begin(ld, aside, lt);
            bool replaced = false;
            try
            {
                try
                {
                    File.Replace(lt, ld, aside, true);
                    replaced = true;
                    TryDelete(aside);
                    return;
                }
                catch (Exception ex)
                {
                    // Replace の途中で失敗：元のファイルが控えの名前になっていれば戻す
                    if (!File.Exists(ld) && File.Exists(aside))
                    {
                        try { File.Move(aside, ld); }
                        catch (Exception ex2) { journal = null; throw new IOException("上書きに失敗し、元のファイルを " + PathUtil.Plain(aside) + " に残しました（次の起動で戻します）: " + ex2.Message, ex); }
                    }
                    if (!File.Exists(lt)) throw;   // 一時ファイルがない：これ以上はしない（元のファイルはそのまま）
                }
                // Replace が使えない場所（一部の NAS など）：元を横へ移してから入れ替える
                File.Move(ld, aside);   // 失敗したら例外（元のファイルはそのまま）
                try { File.Move(lt, ld); }
                catch
                {
                    try { File.Move(aside, ld); }
                    catch (Exception ex3) { journal = null; Logger.Error(null, "上書きに失敗しました。元のファイルは " + PathUtil.Plain(aside) + " に残っています（次の起動で戻します）: " + ex3.Message); }
                    throw;
                }
                replaced = true;
                TryDelete(aside);
            }
            finally
            {
                // 失敗して元のファイルが残っている：読み取り専用などの属性を元に戻す
                if (!replaced && ro)
                {
                    try { if (File.Exists(ld)) File.SetAttributes(ld, File.GetAttributes(ld) | FileAttributes.ReadOnly); }
                    catch (Exception ex4) { Logger.Error(null, "読み取り専用の属性を戻せません: " + PathUtil.Plain(ld) + "　" + ex4.Message); }
                }
                CommitJournal.End(journal);   // 元に戻せなかったとき（journal = null）は記録を残し、次の起動で戻す
            }
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
            return CopyFile(src, dst, srcMtimeUtc, throttle, ct, onBytes, null);
        }

        /// <summary>expect：置き換える直前に確かめるコピー先の状態（違えば DestChangedException。上書きしない）</summary>
        public static byte[] CopyFile(string src, string dst, DateTime srcMtimeUtc, Throttle throttle, CancellationToken ct, Action<long> onBytes, DestExpect expect, Action<long, long> onCommitted = null)
        {
            Guard(dst);
            Guard(dst + PartSuffix);
            string parent = Path.GetDirectoryName(PathUtil.Plain(dst));
            CreateDirectory(parent);
            // コピー先が OneDrive などのクラウド同期フォルダ：このPCの空きが少なければアップロードで空くのを待ち、
            // コピーしたファイルには「このPCに残さない」の印を付ける（アップロード後は中身がクラウドだけになる）
            bool cloud = CloudFiles.FreeLocalEnabled && CloudFiles.IsCloudFolder(parent);
            if (cloud)
            {
                long need = 0;
                try { need = new FileInfo(PathUtil.Long(src)).Length; } catch (Exception) { }
                CloudFiles.WaitForLocalSpace(parent, need, CloudFiles.ReserveBytes, CloudFiles.MaxWait, CloudFiles.Poll, ct);
            }
            byte[] hash = CopyVerifiedAndCommit(src, dst, srcMtimeUtc, throttle, ct, onBytes, expect, onCommitted);
            if (cloud) CloudFiles.FreeLocalCopy(dst);
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

    /// <summary>
    /// 同じコピー先のファイルへ、同時に書かない（置き換えの直前〜直後だけ）。
    /// ・このPCの中：パスごとのロック（ジョブ・確認の画面の操作どうし）
    /// ・他のアプリ・他のPC：コピー先に「〜.cklock」を他から開けない形で作る（閉じると自動で消える。強制終了しても OS が閉じる）
    /// 待っても空かなければ「使用中」として次回に回す（上書きしない）。
    /// </summary>
    public static class DestLock
    {
        public static int WaitSeconds = 60;

        private class Holder { public int Refs; }
        private static readonly Dictionary<string, Holder> held = new Dictionary<string, Holder>(StringComparer.OrdinalIgnoreCase);

        private class Releaser : IDisposable
        {
            public string Key; public Holder H; public FileStream LockFile;
            public void Dispose()
            {
                if (LockFile != null) { try { LockFile.Dispose(); } catch (Exception ex) { Logger.Error(null, "ロックのファイルを閉じられません: " + ex.Message); } LockFile = null; }
                if (H == null) return;
                lock (held)
                {
                    Monitor.Exit(H);
                    if (--H.Refs == 0) held.Remove(Key);
                }
                H = null;
            }
        }

        public static IDisposable Acquire(string ld, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string key = PathUtil.Plain(ld).TrimEnd('\\');
            Holder h;
            lock (held)
            {
                if (!held.TryGetValue(key, out h)) { h = new Holder(); held[key] = h; }
                h.Refs++;
            }
            DateTime limit = DateTime.UtcNow.AddSeconds(WaitSeconds);
            bool entered = false;
            try
            {
                while (!(entered = Monitor.TryEnter(h, 200)))
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > limit) throw Busy(ld);
                }
                Releaser r = new Releaser(); r.Key = key; r.H = h;
                try { r.LockFile = OpenLockFile(ld, limit, ct); }
                catch { r.Dispose(); entered = false; h = null; throw; }
                return r;
            }
            catch
            {
                if (h != null)
                {
                    lock (held)
                    {
                        if (entered) Monitor.Exit(h);
                        if (--h.Refs == 0) held.Remove(key);
                    }
                }
                throw;
            }
        }

        private static IOException Busy(string ld)
        {
            return new IOException("同じファイルに別の処理（他のジョブ・他のPC）が書き込み中のため、上書きしませんでした: " + PathUtil.Plain(ld), unchecked((int)0x80070020));
        }

        /// <summary>他のアプリ・他のPC と取り合うためのファイル（上書きのときだけ。新しく置くときは名前がぶつかれば失敗するので不要）</summary>
        private static FileStream OpenLockFile(string ld, DateTime limit, CancellationToken ct)
        {
            if (!File.Exists(ld)) return null;
            string lf = ld + SafeFs.LockSuffix;
            while (true)
            {
                try { return new FileStream(lf, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
                catch (IOException ex)
                {
                    int code = ErrorKind.Win32Code(ex);
                    if (code != 32 && code != 33) return null;   // ロックのファイルを作れない場所（読み取り専用の共有など）：このPCの中のロックだけで続ける
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > limit) throw Busy(ld);
                    if (ct.WaitHandle.WaitOne(300)) ct.ThrowIfCancellationRequested();
                }
                catch (UnauthorizedAccessException) { return null; }
            }
        }
    }

    /// <summary>
    /// 上書き（置き換え）の途中で強制終了・電源断があったときのための記録（local\commit-journal\*.txt）。
    /// 置き換えの前に「コピー先・元のファイルの控えの名前・一時ファイル」を書き、終わったら消す。
    /// 次の起動（とジョブの実行の前）に残っている記録を調べ、コピー先がなくなっていれば控えから元に戻す。
    /// </summary>
    public static class CommitJournal
    {
        public static string Dir { get { return Path.Combine(AppPaths.Local, "commit-journal"); } }
        private static readonly object recoverLock = new object();
        private static long myStart = -1;

        private static long MyStart()
        {
            if (myStart < 0) { try { myStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks; } catch (Exception) { myStart = 0; } }
            return myStart;
        }

        public static string Begin(string ld, string aside, string lt)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string f = Path.Combine(Dir, Guid.NewGuid().ToString("N") + ".txt");
                byte[] b = Encoding.UTF8.GetBytes(string.Join("\n", new string[] { ld, aside, lt, Process.GetCurrentProcess().Id.ToString(), MyStart().ToString(), DateTime.UtcNow.Ticks.ToString() }));
                using (FileStream fs = new FileStream(f, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(b, 0, b.Length);
                    fs.Flush(true);
                }
                return f;
            }
            catch (Exception ex)
            {
                Logger.Error(null, "上書きの途中の記録を書けません（強制終了したときに自動で戻せない場合があります）: " + ex.Message);
                return null;
            }
        }

        public static void End(string journal)
        {
            if (journal == null) return;
            try { File.Delete(journal); }
            catch (Exception ex) { Logger.Error(null, "上書きの途中の記録を消せません: " + ex.Message); }
        }

        /// <summary>中断された置き換えを元に戻す。戻した件数を返す。</summary>
        public static int Recover()
        {
            int restored = 0;
            lock (recoverLock)
            {
                string[] files;
                try { if (!Directory.Exists(Dir)) return 0; files = Directory.GetFiles(Dir, "*.txt"); }
                catch (Exception ex) { Logger.Error(null, "上書きの途中の記録を読めません: " + ex.Message); return 0; }
                foreach (string f in files)
                {
                    try
                    {
                        string[] v = File.ReadAllText(f, Encoding.UTF8).Split('\n');
                        if (v.Length < 5) { Logger.Error(null, "上書きの途中の記録が壊れています（消します）: " + f); File.Delete(f); continue; }
                        string ld = v[0], aside = v[1], lt = v[2];
                        int pid; long start;
                        int.TryParse(v[3], out pid); long.TryParse(v[4], out start);
                        if (StillRunning(pid, start)) continue;   // 動いている処理の記録（今まさに置き換え中）
                        string parent = Path.GetDirectoryName(ld);
                        if (!Directory.Exists(parent))
                        {
                            // NAS につながっていない など：次の機会に（30日たったらあきらめる）
                            long at; if (v.Length > 5 && long.TryParse(v[5], out at) && DateTime.UtcNow.Ticks - at > TimeSpan.TicksPerDay * 30) { Logger.Error(null, "上書きの途中の記録を、30日つながらなかったため消しました: " + PathUtil.Plain(ld)); File.Delete(f); }
                            continue;
                        }
                        bool hasDst = File.Exists(ld), hasAside = File.Exists(aside);
                        if (hasAside && !hasDst)
                        {
                            File.Move(aside, ld);
                            restored++;
                            Logger.Info(null, "上書きの途中で終了していたため、元のファイルに戻しました: " + PathUtil.Plain(ld));
                        }
                        else if (hasAside && hasDst)
                        {
                            File.Delete(aside);   // 置き換えは終わっていた：元のファイルの控えを片付ける
                        }
                        if (File.Exists(lt)) File.Delete(lt);   // 使われなかった一時ファイル
                        File.Delete(f);
                    }
                    catch (Exception ex) { Logger.Error(null, "上書きの途中で終了したファイルを戻せません（次の起動でもう一度試します）: " + ex.Message); }
                }
            }
            return restored;
        }

        private static bool StillRunning(int pid, long start)
        {
            if (pid <= 0) return false;
            if (pid == Process.GetCurrentProcess().Id && start == MyStart()) return true;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) return false;
                    return start != 0 && p.StartTime.ToUniversalTime().Ticks == start;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
