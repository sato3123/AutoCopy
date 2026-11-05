using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Collections.Generic;

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
        /// 署名を調べる。戻り値：null = 署名があり、中身が改ざんされておらず、証明書も使える状態、それ以外 = 理由。
        /// signer に証明書の拇印。署名がないときは unsigned = true。
        /// chainTrusted：PC が信頼している証明書（ルートまでたどれる）か。false なら、自己署名など PC に登録されていない証明書
        /// （その場合、更新に使ってよいのは、拇印が決まっている証明書＝ Acceptable で確かめる だけ）。
        /// 期限切れ・まだ有効でない・コード署名用でない証明書は、どんな場合も使わない。
        /// </summary>
        public static string CheckSignature(string path, out string signer, out bool unsigned)
        {
            bool trusted;
            return CheckSignature(path, out signer, out unsigned, out trusted);
        }

        public static string CheckSignature(string path, out string signer, out bool unsigned, out bool chainTrusted)
        {
            signer = null; unsigned = false; chainTrusted = false;
            uint r = WinVerify(path);
            if (r == TRUST_E_NOSIGNATURE || r == 0x800B0003 /* TRUST_E_SUBJECT_FORM_UNKNOWN */) { unsigned = true; return "デジタル署名がありません"; }
            if (r == TRUST_E_BAD_DIGEST) return "署名のあとで中身が変えられています（改ざんの可能性）";
            if (r == CERT_E_EXPIRED) return "署名の証明書の有効期限が切れています";
            // 0 = 信頼できる発行元。UNTRUSTEDROOT = 自己署名で PC に登録されていない（中身の確認は済んでいる）
            if (r != 0 && r != CERT_E_UNTRUSTEDROOT)
                return "デジタル署名を確認できません（0x" + r.ToString("X8") + "）";
            X509Certificate2 cert;
            try { cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)); }
            catch (Exception ex) { return "署名の証明書を読めません: " + ex.Message; }
            string bad = CertProblem(cert, DateTime.Now);
            if (bad != null) return bad;
            if (r == CERT_E_UNTRUSTEDROOT)
            {
                // 登録されていない証明書は、証明書そのものが署名した（自己署名）ものだけ。途中の証明書が欠けているもの（CHAINING）は使わない
                if (!string.Equals(cert.Subject, cert.Issuer, StringComparison.Ordinal)) return "署名の証明書を発行元までたどれません";
            }
            chainTrusted = r == 0;
            signer = cert.Thumbprint;
            return null;
        }

        /// <summary>証明書が今、コードの署名に使える状態か（期限・用途）。だめなら理由。</summary>
        public static string CertProblem(X509Certificate2 cert, DateTime now)
        {
            if (cert == null) return "署名の証明書がありません";
            if (now > cert.NotAfter) return "署名の証明書の有効期限が切れています（" + cert.NotAfter.ToString("yyyy/MM/dd") + " まで）";
            if (now < cert.NotBefore) return "署名の証明書がまだ有効になっていません（" + cert.NotBefore.ToString("yyyy/MM/dd") + " から）";
            foreach (X509Extension e in cert.Extensions)
            {
                X509EnhancedKeyUsageExtension eku = e as X509EnhancedKeyUsageExtension;
                if (eku == null) continue;
                bool code = false;
                foreach (System.Security.Cryptography.Oid o in eku.EnhancedKeyUsages) if (o.Value == "1.3.6.1.5.5.7.3.3") code = true;
                if (!code) return "コード署名用の証明書ではありません";
            }
            return null;
        }

        /// <summary>今動いている exe の署名の拇印（署名なし・使えない署名 = null）</summary>
        public static string CurrentSigner()
        {
            string s; bool u;
            return CheckSignature(AppPaths.ExePath, out s, out u) == null ? s : null;
        }

        /// <summary>正式に配布している「自動コピー」の署名の証明書の拇印</summary>
        public const string OfficialThumbprint = "E8F609B1AEE069E00891E639CBB03B3CEECB9AC7";

        /// <summary>
        /// 更新に使ってよい証明書の拇印（正式な証明書）。
        /// 証明書を作り直すとき：新しい拇印をここに足した版を、今の証明書で署名して配る → 以後は新しい証明書で署名した版も使える。
        /// </summary>
        public static readonly string[] TrustedThumbprints = { OfficialThumbprint };

        public static bool IsTrustedThumbprint(string t)
        {
            foreach (string x in TrustedThumbprints) if (string.Equals(x, t, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// 更新版として使ってよいか。だめなら理由。
        /// ・署名がない版・署名の後で中身が変えられた版・証明書が期限切れなどの版は、どんな場合も使わない
        /// ・今の版が署名付きなら、同じ証明書か、正式な証明書（TrustedThumbprints：証明書の作り直しに対応）で署名された版だけ
        /// ・今の版に署名がない（自分でビルドした など）なら、正式な「自動コピー」の証明書で署名された版だけ
        /// ・前に更新に失敗した版（起動できなかった版）は使わない
        /// </summary>
        public static string Acceptable(string path, out Version ver, out string signer)
        {
            signer = null;
            ver = VersionOf(path);
            if (ver == null) return "版を読めません";
            if (ver <= CurrentVersion) return "新しい版ではありません（" + ver + "）";
            string blocked = BlockedVersion();
            if (blocked != null && blocked == ver.ToString()) return "前にこの版（" + ver + "）への更新に失敗して元に戻したため、使いません（もっと新しい版を置いてください）";
            bool unsigned, trusted;
            string err = CheckSignature(path, out signer, out unsigned, out trusted);
            if (unsigned) return "デジタル署名がない版には更新しません（" + ver + "）";
            if (err != null) return err;
            return SignerAllowed(signer, CurrentSigner());
        }

        /// <summary>署名した証明書（拇印）が更新に使えるか。mine = 今の版の署名（なし = null）</summary>
        public static string SignerAllowed(string signer, string mine)
        {
            if (IsTrustedThumbprint(signer)) return null;   // 正式な証明書（作り直した新しい証明書も含む）
            if (mine != null)
            {
                // 署名付きで動いているときは、同じ証明書で署名された更新版だけを使う
                if (!string.Equals(signer, mine, StringComparison.OrdinalIgnoreCase))
                    return "今の版と違う証明書で署名されています（拇印 " + signer + "）";
                return null;
            }
            return "正式な「自動コピー」の証明書で署名されていない版には更新しません（拇印 " + signer + "）";
        }

        // ---------------- 更新の失敗から戻す ----------------

        public static string BlockedFile { get { return Path.Combine(AppPaths.Local, "update-blocked.txt"); } }
        public static string RollbackFile { get { return Path.Combine(AppPaths.Local, "update-rollback.txt"); } }
        private static string MarkerFile(int pid) { return Path.Combine(AppPaths.Local, "update-ok-" + pid + ".txt"); }

        public static string BlockedVersion()
        {
            try { return File.Exists(BlockedFile) ? File.ReadAllText(BlockedFile).Trim() : null; }
            catch (Exception ex) { Logger.Error(null, "更新の記録を読めません: " + ex.Message); return null; }
        }

        /// <summary>新しい版が問題なく起動できたことを、見張り役に知らせる</summary>
        public static void MarkStarted()
        {
            try { Directory.CreateDirectory(AppPaths.Local); File.WriteAllText(MarkerFile(Process.GetCurrentProcess().Id), CurrentVersion.ToString()); }
            catch (Exception ex) { Logger.Error(null, "更新の確認の印を書けません: " + ex.Message); }
        }

        /// <summary>入れ替える関連ファイル（説明書・設定・証明書）</summary>
        public static readonly string[] Related = { "AutoCopy.exe.config", AppPaths.ManualFileName, AppPaths.CertFileName, AppPaths.CertInstallerName };

        /// <summary>
        /// 前の版に戻す（新しい版を起動できなかったとき）。exe と関連ファイルを .old から戻し、失敗した版は使わないよう記録する。
        /// </summary>
        public static string Rollback(string exePath, string failedVersion, string oldVersion, string reason)
        {
            string old = exePath + ".old";
            if (!File.Exists(old)) return "前の版（" + old + "）がないため戻せません";
            try
            {
                string failed = exePath + ".failed";
                if (File.Exists(failed)) File.Delete(failed);
                if (File.Exists(exePath)) File.Move(exePath, failed);
                File.Move(old, exePath);
                string dir = Path.GetDirectoryName(exePath);
                foreach (string name in Related)
                {
                    string f = Path.Combine(dir, name), fo = f + ".old";
                    if (!File.Exists(fo)) continue;
                    try { if (File.Exists(f)) File.Delete(f); File.Move(fo, f); }
                    catch (Exception ex) { reason += "（" + name + " を戻せません: " + ex.Message + "）"; }
                }
                Directory.CreateDirectory(AppPaths.Local);
                if (!string.IsNullOrEmpty(failedVersion)) File.WriteAllText(BlockedFile, failedVersion);
                File.WriteAllText(RollbackFile, "新しい版（" + failedVersion + "）" + reason + "ため、前の版（" + oldVersion + "）に戻しました");
                return null;
            }
            catch (Exception ex) { return "前の版に戻せませんでした: " + ex.Message; }
        }

        /// <summary>
        /// 見張り役（前の版の exe から起動される）：新しい版が起動して「起動できた」印を書くまで待つ。
        /// 印が書かれないまま終わった・時間内に起動できなかったときは、前の版に戻して起動し直す。
        /// </summary>
        public static int RunGuard(string[] args)
        {
            // /updateguard 新しい版のpid exeのパス 前の版 新しい版 前の版のpid
            int pid;
            if (args.Length < 5 || !int.TryParse(args[1], out pid)) return 2;
            string exe = args[2], oldVer = args[3], newVer = args[4];
            int oldPid;
            if (args.Length > 5 && int.TryParse(args[5], out oldPid)) WaitForExit(oldPid, 60);   // 前の版が終わるのを待つ
            DateTime limit = DateTime.Now.AddSeconds(GuardSeconds);
            Process p = null;
            try { p = Process.GetProcessById(pid); } catch (Exception) { }
            while (DateTime.Now < limit)
            {
                if (File.Exists(MarkerFile(pid))) { try { File.Delete(MarkerFile(pid)); } catch (Exception) { } return 0; }   // 起動できた
                if (p == null || p.HasExited) break;   // 印を書かずに終わった
                Thread.Sleep(500);
            }
            if (File.Exists(MarkerFile(pid))) { try { File.Delete(MarkerFile(pid)); } catch (Exception) { } return 0; }
            try { if (p != null && !p.HasExited) { p.Kill(); p.WaitForExit(10000); } } catch (Exception) { }
            string err = Rollback(exe, newVer, oldVer, p == null || p.HasExited ? "が起動できなかった" : "が時間内に起動しなかった");
            if (err != null) { try { File.WriteAllText(RollbackFile, err); } catch (Exception) { } return 1; }
            try { Process.Start(exe, "/startup"); } catch (Exception) { }
            return 1;
        }

        public static int GuardSeconds = 180;

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

            // 説明書・設定ファイル・証明書：先に手元へ「.new」としてコピーしておく（exe と食い違わないよう、そろってから入れ替える）
            string from = Path.GetDirectoryName(c.Path);
            List<string> staged = new List<string>();
            foreach (string name in Related)
            {
                string f = Path.Combine(from, name);
                if (!File.Exists(f)) continue;
                string to = Path.Combine(dir, name) + ".new";
                try { File.Copy(f, to, true); staged.Add(name); }
                catch (Exception ex)
                {
                    foreach (string n in staged) { try { File.Delete(Path.Combine(dir, n) + ".new"); } catch (Exception) { } }
                    try { File.Delete(tmp); } catch (Exception) { }
                    throw new IOException("更新版の " + name + " をコピーできないため、更新を止めました: " + ex.Message, ex);
                }
            }

            string old = target + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(target, old);              // 動いている exe でも名前は変えられる
            try { File.Move(tmp, target); }
            catch
            {
                try { File.Move(old, target); } catch (Exception rex) { Logger.Error(null, "更新に失敗し、前の版を戻せませんでした: " + rex.Message); }
                throw;
            }
            foreach (string name in staged)
            {
                string f = Path.Combine(dir, name);
                try
                {
                    if (File.Exists(f + ".old")) File.Delete(f + ".old");
                    if (File.Exists(f)) File.Move(f, f + ".old");
                    File.Move(f + ".new", f);
                }
                catch (Exception ex) { Logger.Error(null, "更新：" + name + " を入れ替えられませんでした（前のものを使います）: " + ex.Message); }
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
