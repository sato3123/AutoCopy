using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AutoCopy
{
    /// <summary>
    /// 配布用設定ファイル（AutoCopy.setup）。
    /// 中身（ジョブ・ログ保管先・ID とパスワード）は配布用パスワードから作った鍵で AES-256 暗号化し、改ざん検知（HMAC-SHA256）を付ける。
    /// </summary>
    public static class Distribution
    {
        private const string FormatName = "AutoCopy.setup/1";
        private const int Iterations = 100000;

        public class WrongPasswordException : Exception
        {
            public WrongPasswordException() : base("配布用パスワードが違います。") { }
        }

        public static DistEnvelope ReadEnvelope(string path)
        {
            DistEnvelope env = Json.Load<DistEnvelope>(path);
            if (env == null || env.Format != FormatName) throw new InvalidDataException("配布用設定ファイルではありません: " + path);
            return env;
        }

        /// <summary>配布用パスワードの最短の長さ（v1.15 から 12文字。前の版で作った短いパスワードの配布設定も、そのまま読める）</summary>
        public const int MinPasswordLength = 12;

        /// <summary>配布用パスワードとして使えるか。だめなら理由。</summary>
        public static string PasswordProblem(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
                return "配布用パスワードは" + MinPasswordLength + "文字以上にしてください（ID・パスワードを含むため、推測されにくい長さが必要です）。";
            bool allSame = true;
            foreach (char c in password) if (c != password[0]) allSame = false;
            if (allSame) return "配布用パスワードに、同じ文字のくり返しは使えません。";
            return null;
        }

        /// <summary>短いパスワード（前の版の基準）で作られた配布設定か：作り直しを勧める</summary>
        public static bool IsWeakLegacyPassword(string password)
        {
            return password != null && password.Length < MinPasswordLength;
        }

        public static void Write(string path, DistPayload payload, string password)
        {
            string bad = PasswordProblem(password);
            if (bad != null) throw new ArgumentException(bad);
            WriteCore(path, payload, password);
        }

        /// <summary>暗号化して書く（長さの確認なし：テストで前の版の配布設定を作るため）</summary>
        internal static void WriteCore(string path, DistPayload payload, string password)
        {
            byte[] salt = Random(16), iv = Random(16);
            byte[] encKey, macKey;
            DeriveKeys(password, salt, Iterations, out encKey, out macKey);
            byte[] plain = Encoding.UTF8.GetBytes(Json.Serialize(payload));
            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform t = aes.CreateEncryptor()) cipher = t.TransformFinalBlock(plain, 0, plain.Length);
            }
            DistEnvelope env = new DistEnvelope();
            env.Format = FormatName;
            env.Name = payload.Name;
            env.Version = payload.Version;
            env.Created = payload.Created;
            env.Iterations = Iterations;
            env.Salt = Convert.ToBase64String(salt);
            env.Iv = Convert.ToBase64String(iv);
            env.Data = Convert.ToBase64String(cipher);
            env.Mac = Convert.ToBase64String(Mac(macKey, env));
            Json.Save(path, env);
        }

        public static DistPayload Decrypt(DistEnvelope env, string password)
        {
            byte[] salt = Convert.FromBase64String(env.Salt), iv = Convert.FromBase64String(env.Iv);
            byte[] encKey, macKey;
            DeriveKeys(password ?? "", salt, env.Iterations > 0 ? env.Iterations : Iterations, out encKey, out macKey);
            byte[] mac = Mac(macKey, env);
            byte[] given = Convert.FromBase64String(env.Mac ?? "");
            if (!FixedEquals(mac, given)) throw new WrongPasswordException();
            byte[] cipher = Convert.FromBase64String(env.Data);
            byte[] plain;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform t = aes.CreateDecryptor()) plain = t.TransformFinalBlock(cipher, 0, cipher.Length);
            }
            return Json.Deserialize<DistPayload>(Encoding.UTF8.GetString(plain));
        }

        private static void DeriveKeys(string password, byte[] salt, int iterations, out byte[] encKey, out byte[] macKey)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] k = kdf.GetBytes(64);
                encKey = new byte[32]; macKey = new byte[32];
                Buffer.BlockCopy(k, 0, encKey, 0, 32);
                Buffer.BlockCopy(k, 32, macKey, 0, 32);
            }
        }

        private static byte[] Mac(byte[] key, DistEnvelope env)
        {
            string text = env.Format + "|" + env.Version + "|" + env.Created + "|" + env.Salt + "|" + env.Iv + "|" + env.Data;
            using (HMACSHA256 h = new HMACSHA256(key)) return h.ComputeHash(Encoding.UTF8.GetBytes(text));
        }

        private static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        private static byte[] Random(int n)
        {
            byte[] b = new byte[n];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return b;
        }

        /// <summary>このPCの設定から配布用の中身を作る（このPCだけのジョブ・フォルダ名は含めない）</summary>
        public static DistPayload BuildPayload(AppSettings s, string name, int version, List<DistCredential> creds)
        {
            DistPayload p = new DistPayload();
            p.Name = name;
            p.Version = version;
            p.Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            p.LogRoot = s.LogRoot;
            p.UpdateFolder = s.UpdateFolder;
            p.LocalLogDays = s.LocalLogDays;
            p.NetLogDays = s.NetLogDays;
            p.TrashDays = s.TrashDays;
            p.Jobs = new List<JobConfig>();
            foreach (JobConfig j in s.Jobs) if (j.Scope == JobScope.Dist) p.Jobs.Add(j.Clone());
            p.Credentials = creds ?? new List<DistCredential>();
            return p;
        }

        /// <summary>ジョブとログ保管先で使うサーバー名の一覧</summary>
        public static List<string> ServersUsed(AppSettings s, bool distOnly)
        {
            List<string> list = new List<string>();
            List<string> paths = new List<string>();
            foreach (JobConfig j in s.Jobs)
            {
                if (distOnly && j.Scope != JobScope.Dist) continue;
                paths.Add(NetAccess.ToUncIfMapped(j.Source));
                paths.Add(NetAccess.ToUncIfMapped(j.Dest));
            }
            if (!string.IsNullOrEmpty(s.LogRoot)) paths.Add(s.LogRoot);
            if (!string.IsNullOrEmpty(s.UpdateFolder)) paths.Add(s.UpdateFolder);
            foreach (string p in paths)
            {
                string sv = PathUtil.UncServer(p);
                if (sv == null) continue;
                bool dup = false;
                foreach (string x in list) if (string.Equals(x, sv, StringComparison.OrdinalIgnoreCase)) dup = true;
                if (!dup) list.Add(sv);
            }
            return list;
        }

        /// <summary>配布用の中身をこのPCの設定に取り込む。フォルダ名・このPCだけのジョブは残す。</summary>
        /// <summary>
        /// 新しく加わったジョブは「無効」で取り込む（内容を確かめてから有効にしてもらう）。
        /// 取り込み済みのジョブは、このPCで決めた有効/無効をそのまま使う。新しく加わった（無効にした）ジョブの数を返す。
        /// </summary>
        public static int Apply(AppSettings s, DistPayload p)
        {
            Dictionary<string, bool> wasEnabled = new Dictionary<string, bool>();
            foreach (JobConfig j in s.Jobs) wasEnabled[j.Id] = j.Enabled;
            int added = 0;
            s.LogRoot = p.LogRoot;
            if (!string.IsNullOrEmpty(p.UpdateFolder)) s.UpdateFolder = p.UpdateFolder;
            if (p.LocalLogDays > 0) s.LocalLogDays = p.LocalLogDays;
            if (p.NetLogDays > 0) s.NetLogDays = p.NetLogDays;
            if (p.TrashDays > 0) s.TrashDays = p.TrashDays;
            List<JobConfig> keep = new List<JobConfig>();
            foreach (JobConfig j in s.Jobs) if (j.Scope != JobScope.Dist) keep.Add(j);
            List<JobConfig> merged = new List<JobConfig>();
            if (p.Jobs != null)
            {
                foreach (JobConfig j in p.Jobs)
                {
                    j.Scope = JobScope.Dist;
                    bool en;
                    if (wasEnabled.TryGetValue(j.Id, out en)) j.Enabled = en;
                    else { j.Enabled = false; added++; }
                    merged.Add(j); // ID が同じなら状態（前回の記録）はそのまま引き継がれる
                }
            }
            merged.AddRange(keep);
            s.Jobs = merged;
            s.DistVersion = p.Version;
            s.DistName = p.Name;
            s.DistCreated = p.Created;
            if (p.Credentials != null)
                foreach (DistCredential c in p.Credentials)
                    CredentialStore.Save(c.Server, c.User, c.Password);
            return added;
        }

        /// <summary>配布フォルダを作る</summary>
        public static List<string> CreateFolder(string folder, DistPayload payload, string password)
        {
            List<string> notes = new List<string>();
            Directory.CreateDirectory(folder);
            string exe = AppPaths.ExePath;
            File.Copy(exe, Path.Combine(folder, "AutoCopy.exe"), true);
            if (File.Exists(exe + ".config")) File.Copy(exe + ".config", Path.Combine(folder, "AutoCopy.exe.config"), true);
            string manual = Path.Combine(AppPaths.ExeDir, AppPaths.ManualFileName);
            if (File.Exists(manual)) File.Copy(manual, Path.Combine(folder, AppPaths.ManualFileName), true);
            else notes.Add("操作説明書.html が見つからないため同梱していません。");
            bool net = false;
            foreach (string f in Directory.GetFiles(AppPaths.ExeDir, "ndp48*.exe"))
            {
                File.Copy(f, Path.Combine(folder, Path.GetFileName(f)), true);
                net = true;
            }
            // 署名の証明書と、その登録用のバッチ（あれば一緒に配る）
            foreach (string name in new string[] { AppPaths.CertFileName, AppPaths.CertInstallerName })
            {
                string f = Path.Combine(AppPaths.ExeDir, name);
                if (File.Exists(f)) File.Copy(f, Path.Combine(folder, name), true);
            }
            if (!net) notes.Add(".NET Framework 4.8 のインストーラー（ndp48-x86-x64-allos-jpn.exe）が AutoCopy.exe と同じフォルダにないため同梱していません。Windows 7 のPCに配る場合は、配布フォルダに入れてください。");
            Write(Path.Combine(folder, AppPaths.SetupFileName), payload, password);
            File.WriteAllText(Path.Combine(folder, "はじめにお読みください.txt"), ReadmeText(payload), new UTF8Encoding(true));
            return notes;
        }

        private static string ReadmeText(DistPayload p)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("自動コピー　配布フォルダ");
            sb.AppendLine("配布設定：" + p.Name + "　第" + p.Version + "版（" + p.Created + " 作成）");
            sb.AppendLine();
            sb.AppendLine("【使い方】");
            sb.AppendLine("1. このフォルダごと、使うPCにコピーします（USBメモリなど）。");
            sb.AppendLine("2. AutoCopy.exe をダブルクリックします。");
            sb.AppendLine("3. 初回セットアップ画面で「配布用パスワード」と「このPCのフォルダ名」を入力し、［開始］を押します。");
            sb.AppendLine("   配布用パスワードは配布担当者から聞いてください。");
            sb.AppendLine("4. セットアップが終わったら、このフォルダは削除してかまいません。");
            sb.AppendLine();
            sb.AppendLine("【Windows 7 の場合】");
            sb.AppendLine("AutoCopy.exe が起動しないときは、先に .NET Framework 4.8 をインストールしてください。");
            sb.AppendLine("このフォルダに ndp48-x86-x64-allos-jpn.exe があればそれを実行します。");
            sb.AppendLine("ない場合は Microsoft のサイトから「.NET Framework 4.8」を入手してください（Windows 7 SP1 が必要です）。");
            sb.AppendLine();
            sb.AppendLine("詳しくは 操作説明書.html をご覧ください。");
            return sb.ToString();
        }
    }
}
