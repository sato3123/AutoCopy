using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoCopy
{
    /// <summary>Windows の資格情報マネージャーにID・パスワードを保存する</summary>
    public static class CredentialStore
    {
        private const string Prefix = "AutoCopy:";

        private static string Target(string server)
        {
            return Prefix + (server ?? "").Trim().TrimStart('\\').ToLowerInvariant();
        }

        public static void Save(string server, string user, string password)
        {
            byte[] blob = Encoding.Unicode.GetBytes(password ?? "");
            Native.CREDENTIAL c = new Native.CREDENTIAL();
            c.Type = Native.CRED_TYPE_GENERIC;
            c.TargetName = Target(server);
            c.UserName = user ?? "";
            c.CredentialBlobSize = blob.Length;
            c.CredentialBlob = Marshal.AllocCoTaskMem(Math.Max(blob.Length, 1));
            c.Persist = Native.CRED_PERSIST_LOCAL_MACHINE;
            c.Comment = "自動コピー";
            try
            {
                Marshal.Copy(blob, 0, c.CredentialBlob, blob.Length);
                if (!Native.CredWrite(ref c, 0))
                    throw new IOException("資格情報を保存できませんでした: " + Native.Win32Message(Marshal.GetLastWin32Error()));
            }
            finally
            {
                // パスワードをメモリに残さない
                Marshal.Copy(new byte[Math.Max(blob.Length, 1)], 0, c.CredentialBlob, Math.Max(blob.Length, 1));
                Array.Clear(blob, 0, blob.Length);
                Marshal.FreeCoTaskMem(c.CredentialBlob);
            }
        }

        public static bool TryLoad(string server, out string user, out string password)
        {
            user = null; password = null;
            IntPtr p;
            if (!Native.CredRead(Target(server), Native.CRED_TYPE_GENERIC, 0, out p)) return false;
            try
            {
                Native.CREDENTIAL c = (Native.CREDENTIAL)Marshal.PtrToStructure(p, typeof(Native.CREDENTIAL));
                user = c.UserName;
                if (c.CredentialBlobSize > 0)
                {
                    byte[] blob = new byte[c.CredentialBlobSize];
                    Marshal.Copy(c.CredentialBlob, blob, 0, blob.Length);
                    password = Encoding.Unicode.GetString(blob);
                }
                else password = "";
                return true;
            }
            finally { Native.CredFree(p); }
        }

        public static void Delete(string server)
        {
            Native.CredDelete(Target(server), Native.CRED_TYPE_GENERIC, 0);
        }
    }

    public class AccessException : Exception
    {
        public bool IsCredentialError;
        public AccessException(string msg, bool credErr) : base(msg) { IsCredentialError = credErr; }
    }

    /// <summary>コピー元・コピー先・ログ保管先へのアクセス確保（接続と再試行）</summary>
    public static class NetAccess
    {
        /// <summary>Z:\xxx のような割り当てドライブを \\server\share\xxx に変換する</summary>
        public static string ToUncIfMapped(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':') return path;
            string unc = Native.GetUncForDrive(path.Substring(0, 2));
            if (unc == null) return path;
            return unc.TrimEnd('\\') + path.Substring(2);
        }

        public static string Describe(string path)
        {
            if (PathUtil.IsUnc(path)) return "ネットワーク";
            try
            {
                string root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root))
                {
                    DriveInfo d = new DriveInfo(root);
                    if (d.DriveType == DriveType.Network) return "ネットワーク";
                    if (d.DriveType == DriveType.Removable) return "外付け（USB）";
                    if (d.IsReady && d.DriveType == DriveType.Fixed) return "PC内";
                }
            }
            catch (Exception) { }
            return "PC内・外付け";
        }

        /// <summary>
        /// フォルダにアクセスできるようにする。ネットワークなら保存した資格情報で接続する。
        /// retries 回まで intervalSec 秒おきに再試行。失敗したら AccessException。
        /// </summary>
        /// <summary>ネットワーク（Wi-Fi・LAN）につながっているか。確認用に AUTOCOPY_TEST_OFFLINE=1 でつながっていない状態を作れる</summary>
        public static bool NetworkUp()
        {
            if (Environment.GetEnvironmentVariable("AUTOCOPY_TEST_OFFLINE") == "1") return false;
            try { return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); }
            catch (Exception) { return true; }   // 調べられないときは、実際に開いてみて判断する
        }

        public static void Ensure(string folder, int retries, int intervalSec, CancellationToken ct, Action<int> onRetry)
        {
            string plain = PathUtil.Plain(folder);
            string lastError = null;
            for (int attempt = 0; attempt <= Math.Max(0, retries); attempt++)
            {
                if (attempt > 0)
                {
                    if (onRetry != null) onRetry(attempt);
                    if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, intervalSec)))) throw new OperationCanceledException();
                }
                bool unc = PathUtil.IsUnc(plain);
                // ネットワークにまったくつながっていない（Wi-Fi・LAN が切れている）ときは、ネットワークのフォルダを使えない。
                // （オフラインファイルなどで「ある」ように見えても、つながっていなければコピーしない）
                if (unc && !NetworkUp())
                {
                    lastError = "【ネットワークの切断】ネットワークにつながっていないため、" + PathUtil.UncShareRoot(plain) + " を使えません（Wi-Fi・LAN が切れている可能性があります）";
                    continue;
                }
                if (ExistsOrParentExists(plain)) return;

                if (unc)
                {
                    string share = PathUtil.UncShareRoot(plain);
                    string server = PathUtil.UncServer(plain);
                    string user, pass;
                    if (share != null && CredentialStore.TryLoad(server, out user, out pass))
                    {
                        Native.NETRESOURCE nr = new Native.NETRESOURCE();
                        nr.dwType = Native.RESOURCETYPE_DISK;
                        nr.lpRemoteName = share;
                        int r = Native.WNetAddConnection2(nr, pass, user, 0);
                        if (Native.IsCredentialError(r))
                            throw new AccessException(share + " に接続できません。ID またはパスワードが違うか、アカウントが使えません（" + Native.Win32Message(r) + "）。", true);
                        if (r == 0 || r == Native.ERROR_SESSION_CREDENTIAL_CONFLICT || r == Native.ERROR_ALREADY_ASSIGNED)
                        {
                            if (ExistsOrParentExists(plain)) return;
                        }
                        lastError = share + " に接続できません（" + Native.Win32Message(r) + "）";
                    }
                    else
                    {
                        lastError = (share ?? plain) + " に接続できません";
                    }
                }
                else
                {
                    string root = Path.GetPathRoot(plain);
                    if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
                        lastError = "ドライブ " + root + " がありません（外付けドライブが外れている可能性があります）";
                    else
                        lastError = plain + " にアクセスできません";
                }
            }
            throw new AccessException(lastError + (retries > 0 ? "（" + retries + "回再試行しました）" : ""), false);
        }

        /// <summary>フォルダ自体か、まだ作られていなければ親フォルダが存在するか</summary>
        private static bool ExistsOrParentExists(string plain)
        {
            try
            {
                if (Directory.Exists(PathUtil.Long(plain))) return true;
                string share = PathUtil.UncShareRoot(plain);
                if (share != null) return Directory.Exists(share);
                string root = Path.GetPathRoot(plain);
                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
            catch (Exception) { return false; }
        }

        /// <summary>接続テスト（画面用）。成功なら null、失敗ならメッセージ。</summary>
        public static string Test(string folder, string user, string password)
        {
            try
            {
                string plain = PathUtil.Plain(folder);
                if (PathUtil.IsUnc(plain) && !string.IsNullOrEmpty(user))
                {
                    Native.NETRESOURCE nr = new Native.NETRESOURCE();
                    nr.dwType = Native.RESOURCETYPE_DISK;
                    nr.lpRemoteName = PathUtil.UncShareRoot(plain);
                    int r = Native.WNetAddConnection2(nr, password, user, 0);
                    if (Native.IsCredentialError(r)) return "ID またはパスワードが違います（" + Native.Win32Message(r) + "）";
                    if (r != 0 && r != Native.ERROR_SESSION_CREDENTIAL_CONFLICT && r != Native.ERROR_ALREADY_ASSIGNED)
                        return Native.Win32Message(r);
                }
                Ensure(folder, 0, 1, CancellationToken.None, null);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
