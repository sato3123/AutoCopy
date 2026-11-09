using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoCopy
{
    /// <summary>
    /// OneDrive などのクラウド同期フォルダ（Windows の Cloud Files：「ファイル オンデマンド」）への対応。
    ///
    /// ・OneDrive のフォルダ・ファイルは「リパースポイント」（クラウド用の印 IO_REPARSE_TAG_CLOUD_*）になっている。
    ///   ジャンクション（ループのおそれ）と区別して、クラウド用のフォルダは中まで調べる。
    /// ・「クラウドだけにある」ファイル（オンラインのみ）は、中身を読むとダウンロードされてこのPCの容量を使う。
    ///   中身を比べる処理（重複の確認など）では、読まずに済ませる。
    /// ・コピー先がクラウド同期フォルダのときは、コピーしたファイルに「このPCに残さない」（UNPINNED）の印を付ける。
    ///   OneDrive はアップロードが終わると、このPCからファイルの中身を空ける（クラウドだけにする）。
    /// ・このPCの空きが少ないときは、OneDrive のアップロード（空き）を待ってからコピーする。待っても空かなければ止める。
    /// </summary>
    public static class CloudFiles
    {
        public const int FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
        public const int FILE_ATTRIBUTE_PINNED = 0x80000;
        public const int FILE_ATTRIBUTE_UNPINNED = 0x100000;
        public const int FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;
        public const uint IO_REPARSE_TAG_CLOUD = 0x9000001A;          // CLOUD_1〜F は 0x9000x01A
        public const uint IO_REPARSE_TAG_ONEDRIVE = 0x80000021;
        public const uint IO_REPARSE_TAG_FILE_PLACEHOLDER = 0x80000015;
        public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;   // ジャンクション
        public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

        /// <summary>中身がこのPCになく、読むとダウンロードされるファイル（オンラインのみ）か</summary>
        public static bool IsCloudOnly(FileAttributes a)
        {
            int v = (int)a;
            return (v & (FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS | FILE_ATTRIBUTE_RECALL_ON_OPEN)) != 0 || (a & FileAttributes.Offline) != 0;
        }

        /// <summary>クラウド同期（OneDrive など）の印か</summary>
        public static bool IsCloudTag(uint tag)
        {
            return (tag & 0xFFFF0FFF) == IO_REPARSE_TAG_CLOUD || tag == IO_REPARSE_TAG_ONEDRIVE || tag == IO_REPARSE_TAG_FILE_PLACEHOLDER;
        }

        /// <summary>
        /// 中を調べないフォルダ（ジャンクション・シンボリックリンクなど：ループ・別の場所の二重のおそれ）か。
        /// クラウド同期のフォルダ（OneDrive）は中を調べる。印が読めないときは今まで通り調べない（安全側）。
        /// </summary>
        public static bool SkipLinkDir(string fullPath, FileAttributes a)
        {
            if ((a & FileAttributes.ReparsePoint) == 0) return false;
            uint tag = ReparseTag(fullPath);
            return !IsCloudTag(tag);
        }

        /// <summary>リパースポイントの印（読めない・リパースポイントでない = 0）。ファイルを開かないのでダウンロードもしない</summary>
        public static uint ReparseTag(string path)
        {
            try
            {
                WIN32_FIND_DATAW d;
                IntPtr h = FindFirstFileW(PathUtil.Long(PathUtil.Plain(path).TrimEnd('\\')), out d);
                if (h == INVALID_HANDLE_VALUE) return 0;
                FindClose(h);
                return (d.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? d.dwReserved0 : 0;
            }
            catch (Exception) { return 0; }
        }

        // ---------------- コピー先がクラウド同期フォルダか ----------------

        private static readonly Dictionary<string, bool> folderCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// クラウド同期フォルダ（OneDrive など）の中か。
        /// Windows の Cloud Files に登録された同期フォルダの中（Windows 10 1709 以降）、または OneDrive のフォルダ（環境変数）の中。
        /// </summary>
        public static bool IsCloudFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string key = PathUtil.Plain(folder).TrimEnd('\\');
            lock (folderCache)
            {
                bool v;
                if (folderCache.TryGetValue(key, out v)) return v;
            }
            bool r = InSyncRoot(key) || UnderOneDriveEnv(key);
            lock (folderCache)
            {
                if (folderCache.Count > 5000) folderCache.Clear();
                folderCache[key] = r;
            }
            return r;
        }

        public static void ClearCache() { lock (folderCache) folderCache.Clear(); }

        private static bool cfMissing;   // cldapi.dll がない（Windows 7・8.1 など）

        private static bool InSyncRoot(string folder)
        {
            if (cfMissing) return false;
            // 近い親から：まだないフォルダ（これから作る）でも、親が同期フォルダなら同期フォルダ
            string p = folder;
            for (int i = 0; i < 64 && !string.IsNullOrEmpty(p); i++)
            {
                if (Directory.Exists(PathUtil.Long(p))) break;
                p = Path.GetDirectoryName(p);
            }
            if (string.IsNullOrEmpty(p) || p.StartsWith(@"\\")) return false;   // ネットワークの場所は対象外
            IntPtr buf = Marshal.AllocHGlobal(4096);
            try
            {
                uint ret;
                int hr = CfGetSyncRootInfoByPath(p, 0 /* CF_SYNC_ROOT_INFO_BASIC */, buf, 4096, out ret);
                return hr == 0 || hr == unchecked((int)0x800700EA) /* ERROR_MORE_DATA */;
            }
            catch (DllNotFoundException) { cfMissing = true; return false; }
            catch (EntryPointNotFoundException) { cfMissing = true; return false; }
            catch (Exception) { return false; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>OneDrive のフォルダ（環境変数 OneDrive・OneDriveCommercial・OneDriveConsumer）の中か</summary>
        public static bool UnderOneDriveEnv(string folder)
        {
            foreach (string name in new string[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                string root = Environment.GetEnvironmentVariable(name);
                if (IsUnder(folder, root)) return true;
            }
            return false;
        }

        public static bool IsUnder(string folder, string root)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(folder)) return false;
            string f = PathUtil.Plain(folder).TrimEnd('\\') + "\\", r = PathUtil.Plain(root).TrimEnd('\\') + "\\";
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------- このPCに残さない（アップロード後に空ける） ----------------

        private static int freeFailLogged;

        /// <summary>
        /// コピーしたファイルに「このPCに残さない」の印を付ける（OneDrive はアップロード後に中身を空ける）。
        /// 「常にこのデバイスに保持する」にしてある（PINNED）ときは、利用者の選んだ通りにして変えない。
        /// 戻り値：印を付けた = true。ファイルの中身・日時は変えない。
        /// </summary>
        public static bool FreeLocalCopy(string path)
        {
            try
            {
                string l = PathUtil.Long(path);
                uint a = GetFileAttributesW(l);
                if (a == INVALID_FILE_ATTRIBUTES) return false;
                if ((a & FILE_ATTRIBUTE_PINNED) != 0) return false;        // 利用者が「常に保持」にしている
                if ((a & FILE_ATTRIBUTE_UNPINNED) != 0) return true;       // すでに印がある
                uint keep = a & Settable;
                if (!SetFileAttributesW(l, keep | (uint)FILE_ATTRIBUTE_UNPINNED))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (Interlocked.Exchange(ref freeFailLogged, 1) == 0)
                        Logger.Error(null, "OneDrive：コピーしたファイルに「このPCに残さない」の印を付けられません（コピーはできています）: " + PathUtil.Plain(path) + "　" + new Win32Exception(err).Message);
                    return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // SetFileAttributes で指定できる属性（リパースポイント・圧縮などは指定できない）
        private const uint Settable = 0x1 | 0x2 | 0x4 | 0x20 | 0x80 | 0x100 | 0x1000 | 0x2000 | FILE_ATTRIBUTE_PINNED;

        // ---------------- このPCの空き ----------------

        /// <summary>空き容量（バイト。分からない = -1）</summary>
        public static long FreeBytes(string folder)
        {
            try
            {
                ulong free, total, totalFree;
                string p = PathUtil.Plain(folder);
                for (int i = 0; i < 64 && !string.IsNullOrEmpty(p) && !Directory.Exists(PathUtil.Long(p)); i++) p = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(p)) return -1;
                if (!GetDiskFreeSpaceExW(p.TrimEnd('\\') + "\\", out free, out total, out totalFree)) return -1;
                return (long)free;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>テスト用：空き容量の求め方を差し替える（null = 本物）</summary>
        public static Func<string, long> FreeBytesForTest;

        public const long DefaultReserveBytes = 2048L * 1024 * 1024;

        /// <summary>設定：コピー先がクラウド同期フォルダなら、コピーしたファイルをこのPCに残さない</summary>
        public static bool FreeLocalEnabled = true;
        /// <summary>設定：このPCに残しておく空き（これを下回りそうならアップロードを待つ）</summary>
        public static long ReserveBytes = DefaultReserveBytes;
        public static TimeSpan MaxWait = TimeSpan.FromMinutes(30), Poll = TimeSpan.FromSeconds(15);

        /// <summary>
        /// コピー先がクラウド同期フォルダ：このPCの空きが「ファイルの大きさ＋残しておく分」より少なければ、
        /// OneDrive のアップロードで空くのを待つ（最長 maxWait）。待っても空かなければ容量不足として止める（コピー先は変えない）。
        /// 戻り値：待った時間。
        /// </summary>
        public static TimeSpan WaitForLocalSpace(string folder, long need, long reserve, TimeSpan maxWait, TimeSpan poll, CancellationToken ct)
        {
            DateTime start = DateTime.UtcNow;
            bool logged = false;
            while (true)
            {
                long free = FreeBytesForTest != null ? FreeBytesForTest(folder) : FreeBytes(folder);
                if (free < 0 || free >= need + reserve) return DateTime.UtcNow - start;   // 分からないときは待たない（コピーで容量不足なら今まで通りエラー）
                TimeSpan waited = DateTime.UtcNow - start;
                if (waited >= maxWait)
                    throw new IOException("OneDrive（クラウド同期フォルダ）のアップロードを " + (int)maxWait.TotalMinutes + "分 待ちましたが、このPCの空き容量が足りません（空き "
                        + Fmt.Size(free) + "・必要 " + Fmt.Size(need + reserve) + "）。OneDrive がアップロードできているか（サインイン・ネットワーク・一時停止）を確かめてください: " + PathUtil.Plain(folder),
                        unchecked((int)0x80070070) /* ERROR_DISK_FULL：容量不足として止める */);
                if (!logged)
                {
                    logged = true;
                    Logger.Info(null, "OneDrive：このPCの空きが少ないため、アップロードで空くのを待っています（空き " + Fmt.Size(free) + "・必要 " + Fmt.Size(need + reserve) + "）");
                }
                TimeSpan rest = maxWait - waited;
                if (ct.WaitHandle.WaitOne(rest < poll ? rest : poll)) ct.ThrowIfCancellationRequested();
            }
        }

        // ---------------- Windows ----------------

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributesW(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceExW(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
        private static extern int CfGetSyncRootInfoByPath(string FilePath, int InfoClass, IntPtr InfoBuffer, uint InfoBufferLength, out uint ReturnedLength);
    }
}
