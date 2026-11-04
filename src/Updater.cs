using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace AutoCopy
{
    /// <summary>
    /// 自動更新。更新版の置き場所（NAS の配布フォルダなど）に新しい AutoCopy.exe があれば、
    /// 手元の exe を入れ替えて起動し直す。
    /// ・版（ファイルバージョン）が新しいときだけ更新する（古い版・同じ版には戻さない）
    /// ・今の exe にデジタル署名があれば、更新版も「同じ証明書」で署名され、改ざんされていないものだけを使う
    /// ・コピー中・画面を開いている間は待つ
    /// ・入れ替える前の exe は AutoCopy.exe.old として残す（元に戻すとき用）
    /// </summary>
    public static class Updater
    {
        public class Candidate
        {
            public string Path;
            public Version Version;
            public string Signer;      // 署名の証明書の拇印（なし = null）
        }

        public static Version CurrentVersion
        {
            get { return VersionOf(AppPaths.ExePath) ?? new Version(0, 0); }
        }

        public static Version VersionOf(string path)
        {
            try
            {
                FileVersionInfo fv = FileVersionInfo.GetVersionInfo(path);
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch (Exception) { return null; }
        }

        // ---------------- デジタル署名 ----------------

        public const uint TRUST_E_NOSIGNATURE = 0x800B0100, CERT_E_UNTRUSTEDROOT = 0x800B0109, CERT_E_CHAINING = 0x800B010A,
            TRUST_E_BAD_DIGEST = 0x80096010, TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004, CERT_E_EXPIRED = 0x800B0101;

        /// <summary>
        /// 署名を調べる。戻り値：null = 署名があり中身も改ざんされていない（証明書が PC に登録されていなくてもよい）、
        /// それ以外 = 理由。signer に証明書の拇印。署名がないときは unsigned = true。
        /// </summary>
        public static string CheckSignature(string path, out string signer, out bool unsigned)
        {
            signer = null; unsigned = false;
            uint r = WinVerify(path);
            if (r == TRUST_E_NOSIGNATURE || r == 0x800B0003 /* TRUST_E_SUBJECT_FORM_UNKNOWN */) { unsigned = true; return "デジタル署名がありません"; }
            // 0 = 信頼できる発行元、UNTRUSTEDROOT/CHAINING = 自己署名など（中身の確認は済んでいる）
            if (r != 0 && r != CERT_E_UNTRUSTEDROOT && r != CERT_E_CHAINING && r != CERT_E_EXPIRED)
                return r == TRUST_E_BAD_DIGEST ? "署名のあとで中身が変えられています（改ざんの可能性）"
                    : "デジタル署名を確認できません（0x" + r.ToString("X8") + "）";
            try
            {
                X509Certificate c = X509Certificate.CreateFromSignedFile(path);
                signer = new X509Certificate2(c).Thumbprint;
            }
            catch (Exception ex) { return "署名の証明書を読めません: " + ex.Message; }
            return null;
        }

        /// <summary>今動いている exe の署名の拇印（署名なし = null）</summary>
        public static string CurrentSigner()
        {
            string s; bool u;
            return CheckSignature(AppPaths.ExePath, out s, out u) == null ? s : null;
        }

        /// <summary>更新版として使ってよいか。だめなら理由。</summary>
        public static string Acceptable(string path, out Version ver, out string signer)
        {
            signer = null;
            ver = VersionOf(path);
            if (ver == null) return "版を読めません";
            if (ver <= CurrentVersion) return "新しい版ではありません（" + ver + "）";
            bool unsigned;
            string err = CheckSignature(path, out signer, out unsigned);
            string mine = CurrentSigner();
            if (mine != null)
            {
                // 署名付きで動いているときは、同じ証明書で署名された更新版だけを使う
                if (err != null) return err;
                if (!string.Equals(signer, mine, StringComparison.OrdinalIgnoreCase))
                    return "今の版と違う証明書で署名されています（拇印 " + signer + "）";
            }
            else if (err != null && !unsigned) return err;   // 署名なしで動いているとき：壊れた署名は使わない
            return null;
        }

        // ---------------- 確認と更新 ----------------

        /// <summary>置き場所に使える更新版があれば返す。なければ null（reason に理由）。</summary>
        public static Candidate Find(AppSettings s, out string reason)
        {
            reason = null;
            string folder = s.UpdateFolder;
            if (string.IsNullOrEmpty(folder)) { reason = "更新版の置き場所が設定されていません"; return null; }
            folder = NetAccess.ToUncIfMapped(folder.Trim());
            if (string.Equals(PathUtil.Normalize(folder), PathUtil.Normalize(AppPaths.ExeDir), StringComparison.OrdinalIgnoreCase))
            {
                reason = "置き場所から直接起動しているため更新しません"; return null;
            }
            try { NetAccess.Ensure(folder, 0, 1, CancellationToken.None, null); }
            catch (Exception ex) { reason = "置き場所に接続できません: " + ex.Message; return null; }
            string exe = Path.Combine(folder, "AutoCopy.exe");
            if (!File.Exists(exe)) { reason = "置き場所に AutoCopy.exe がありません"; return null; }
            Version v; string signer;
            string why = Acceptable(exe, out v, out signer);
            if (why != null) { reason = why; return null; }
            Candidate c = new Candidate();
            c.Path = exe; c.Version = v; c.Signer = signer;
            return c;
        }

        /// <summary>
        /// exe を入れ替える（起動し直しは呼び出し側）。戻り値：新しい exe のパス。
        /// 置き場所からいったん手元にコピーし、コピーしたものをもう一度確かめてから入れ替える。
        /// </summary>
        public static string Install(Candidate c)
        {
            string target = AppPaths.ExePath;
            string dir = AppPaths.ExeDir;
            SafeFs.Guard(target);   // コピー元フォルダの中で動いている場合などは入れ替えない
            string tmp = target + ".new";
            if (File.Exists(tmp)) File.Delete(tmp);
            File.Copy(c.Path, tmp, false);
            Version v; string signer;
            string why = Acceptable(tmp, out v, out signer);
            if (why != null) { try { File.Delete(tmp); } catch (Exception) { } throw new InvalidOperationException("更新版を使えません：" + why); }

            string old = target + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(target, old);              // 動いている exe でも名前は変えられる
            try { File.Move(tmp, target); }
            catch
            {
                try { File.Move(old, target); } catch (Exception) { }
                throw;
            }
            // 説明書・設定ファイル・証明書も新しいものがあれば入れ替える（なくても更新は続ける）
            string from = Path.GetDirectoryName(c.Path);
            foreach (string name in new string[] { "AutoCopy.exe.config", AppPaths.ManualFileName, AppPaths.CertFileName, AppPaths.CertInstallerName })
            {
                try
                {
                    string f = Path.Combine(from, name);
                    if (File.Exists(f)) File.Copy(f, Path.Combine(dir, name), true);
                }
                catch (Exception) { }
            }
            return target;
        }

        /// <summary>起動し直した後：前の版の exe が終わるのを待つ</summary>
        public static void WaitForExit(int pid, int seconds)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid)) p.WaitForExit(seconds * 1000);
            }
            catch (Exception) { }
        }

        // ---------------- WinVerifyTrust ----------------

        private static uint WinVerify(string path)
        {
            WINTRUST_FILE_INFO fi = new WINTRUST_FILE_INFO();
            fi.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            fi.pcwszFilePath = path;
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(fi, pFile, false);
            WINTRUST_DATA d = new WINTRUST_DATA();
            d.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            d.dwUIChoice = 2;            // WTD_UI_NONE
            d.fdwRevocationChecks = 0;   // WTD_REVOKE_NONE（ネットワークに出ない）
            d.dwUnionChoice = 1;         // WTD_CHOICE_FILE
            d.pFile = pFile;
            d.dwStateAction = 0;
            d.dwProvFlags = 0x00000080;  // WTD_CACHE_ONLY_URL_RETRIEVAL
            Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
            try
            {
                return (uint)WinVerifyTrust(IntPtr.Zero, ref action, ref d);
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;   // Windows 7 の形（Windows 8 以降もこの大きさで使える）
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    }
}
