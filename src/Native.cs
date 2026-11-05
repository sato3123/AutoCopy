using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AutoCopy
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        public const int SW_RESTORE = 9, SW_SHOW = 5;

        /// <summary>タスクバーのボタンを、画面を開くまで点滅させる</summary>
        public static void Flash(IntPtr hwnd)
        {
            try
            {
                FLASHWINFO f = new FLASHWINFO();
                f.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
                f.hwnd = hwnd;
                f.dwFlags = 3 | 12;   // FLASHW_ALL | FLASHW_TIMERNOFG
                f.uCount = 0; f.dwTimeout = 0;
                FlashWindowEx(ref f);
            }
            catch (Exception) { }
        }

        // ---------- ファイルID（名前変更の検出用。読み取り属性のみで開く） ----------
        public const uint FILE_READ_ATTRIBUTES = 0x80;
        public const uint FILE_SHARE_ALL = 0x7;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        /// <summary>ファイルIDを取得する。失敗したら false。ファイルの中身・日時は一切変更しない。</summary>
        public static bool TryGetFileId(string path, out uint volume, out ulong index)
        {
            volume = 0; index = 0;
            using (SafeFileHandle h = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero,
                OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (h.IsInvalid) return false;
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(h, out info)) return false;
                volume = info.VolumeSerialNumber;
                index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
                return index != 0;
            }
        }

        // ---------- 資格情報マネージャー ----------
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        public static extern bool CredWrite(ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        public static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
        public static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        public static extern void CredFree(IntPtr buffer);

        // ---------- ネットワーク接続 ----------
        public const int RESOURCETYPE_DISK = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetAddConnection2W")]
        public static extern int WNetAddConnection2(NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetGetConnectionW")]
        public static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        public const int ERROR_SUCCESS = 0;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_BAD_NETPATH = 53;
        public const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
        public const int ERROR_LOGON_FAILURE = 1326;
        public const int ERROR_BAD_USERNAME = 2202;
        public const int ERROR_ALREADY_ASSIGNED = 85;
        public const int ERROR_INVALID_PASSWORD = 86;

        /// <summary>ID・パスワードの誤りやアカウントの問題（再試行しても直らないもの）</summary>
        public static bool IsCredentialError(int code)
        {
            return code == ERROR_INVALID_PASSWORD || code == ERROR_LOGON_FAILURE || code == ERROR_BAD_USERNAME
                || code == 1327 /* アカウントの制限 */ || code == 1328 /* ログオン時間の制限 */ || code == 1330 /* パスワード期限切れ */
                || code == 1331 /* アカウント無効 */ || code == 1907 /* パスワード変更が必要 */ || code == 1909 /* ロックアウト */;
        }

        /// <summary>割り当てドライブ（Z: など）を \\server\share に変換。ネットワークでなければ null。</summary>
        public static string GetUncForDrive(string driveLetterColon)
        {
            StringBuilder sb = new StringBuilder(512);
            int len = sb.Capacity;
            int r = WNetGetConnection(driveLetterColon, sb, ref len);
            if (r == 0) return sb.ToString();
            return null;
        }

        public static string Win32Message(int code)
        {
            return new System.ComponentModel.Win32Exception(code).Message + " (エラー " + code + ")";
        }
    }
}
