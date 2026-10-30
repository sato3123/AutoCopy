using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AutoCopy
{
    public static class Json
    {
        public static string Serialize<T>(T obj)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
                    ser.WriteObject(writer, obj);
                    writer.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Deserialize<T>(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
                return (T)ser.ReadObject(ms);
            }
        }

        public static T Load<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            return Deserialize<T>(text);
        }

        /// <summary>一時ファイルに書いてから置き換える（途中で落ちても壊れない）</summary>
        public static void Save<T>(string path, T obj)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(obj), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }

    public static class AppPaths
    {
        public const string AppFolder = "AutoCopy";
        public const string DestMetaFolder = "_AutoCopy";

        /// <summary>テスト用：環境変数 AUTOCOPY_HOME があれば、設定・状態・ログをすべてそこに置く</summary>
        private static string Override
        {
            get
            {
                string h = Environment.GetEnvironmentVariable("AUTOCOPY_HOME");
                return string.IsNullOrEmpty(h) ? null : h;
            }
        }

        public static string Roaming
        {
            get { return Override != null ? Path.Combine(Override, "roaming") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder); }
        }

        public static string Local
        {
            get { return Override != null ? Path.Combine(Override, "local") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder); }
        }

        public static string SettingsFile { get { return Path.Combine(Roaming, "settings.json"); } }
        public static string StateDir { get { return Path.Combine(Local, "state"); } }
        public static string LogDir { get { return Path.Combine(Local, "logs"); } }
        public static string OutboxDir { get { return Path.Combine(Local, "outbox"); } }
        public static string DupDir { get { return Path.Combine(Local, "dup"); } }
        public static string BinDir { get { return Path.Combine(Local, "bin"); } }
        public static string ExePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        public static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }
        public const string SetupFileName = "AutoCopy.setup";
        public const string ManualFileName = "操作説明書.html";
    }

    public static class PathUtil
    {
        /// <summary>{フォルダ名} と %環境変数% と %DOCUMENTS% を展開する</summary>
        public static string Expand(string path, string folderName)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path;
            if (!string.IsNullOrEmpty(folderName)) p = p.Replace("{フォルダ名}", folderName);
            p = ReplaceIgnoreCase(p, "%DOCUMENTS%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            p = ReplaceIgnoreCase(p, "%DESKTOP%", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            p = Environment.ExpandEnvironmentVariables(p);
            return p.Trim();
        }

        public static bool HasFolderVariable(string path)
        {
            return path != null && path.Contains("{フォルダ名}");
        }

        private static string ReplaceIgnoreCase(string s, string find, string repl)
        {
            int i = s.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                s = s.Substring(0, i) + repl + s.Substring(i + find.Length);
                i = s.IndexOf(find, i + repl.Length, StringComparison.OrdinalIgnoreCase);
            }
            return s;
        }

        /// <summary>表示用の通常パスに戻す</summary>
        public static string Plain(string p)
        {
            if (p == null) return null;
            if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + p.Substring(8);
            if (p.StartsWith(@"\\?\")) return p.Substring(4);
            return p;
        }

        /// <summary>長いパスでも扱えるよう \\?\ を付ける（Windows 7 対策）</summary>
        public static string Long(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.StartsWith(@"\\?\")) return p;
            string full = p;
            try { full = Path.GetFullPath(p); } catch (Exception) { }
            if (full.StartsWith(@"\\")) return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }

        /// <summary>比較用：通常パス・末尾\なし・フルパス</summary>
        public static string Normalize(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string s = Plain(p.Trim());
            try { s = Path.GetFullPath(s); } catch (Exception) { }
            s = s.TrimEnd('\\', '/');
            if (s.Length == 2 && s[1] == ':') s = s + "\\";
            return s;
        }

        public static string Combine(string root, string rel)
        {
            if (string.IsNullOrEmpty(rel)) return root;
            return root.TrimEnd('\\') + "\\" + rel;
        }

        public static string ParentRel(string rel)
        {
            int i = rel.LastIndexOf('\\');
            return i < 0 ? "" : rel.Substring(0, i);
        }

        public static string FileName(string rel)
        {
            int i = rel.LastIndexOf('\\');
            return i < 0 ? rel : rel.Substring(i + 1);
        }

        /// <summary>a が b と同じか、b の中にあるか</summary>
        public static bool IsSameOrInside(string a, string b)
        {
            string na = Normalize(a), nb = Normalize(b);
            if (na.Length == 0 || nb.Length == 0) return false;
            if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = nb.EndsWith("\\") ? nb : nb + "\\";
            return na.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>相対パス a が b と同じか、b の中にあるか</summary>
        public static bool IsSameOrInsideRel(string a, string b)
        {
            if (string.IsNullOrEmpty(b)) return true;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            return a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnc(string p)
        {
            string s = Plain(p);
            return s != null && s.StartsWith(@"\\");
        }

        /// <summary>\\server\share を返す</summary>
        public static string UncShareRoot(string p)
        {
            string s = Plain(p);
            if (s == null || !s.StartsWith(@"\\")) return null;
            string[] parts = s.Substring(2).Split('\\');
            if (parts.Length < 2) return null;
            return @"\\" + parts[0] + "\\" + parts[1];
        }

        public static string UncServer(string p)
        {
            string s = Plain(p);
            if (s == null || !s.StartsWith(@"\\")) return null;
            string[] parts = s.Substring(2).Split('\\');
            return parts.Length > 0 ? parts[0] : null;
        }

        public static readonly char[] InvalidNameChars = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        public static bool IsValidFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Trim().Length == 0) return false;
            if (name.IndexOfAny(InvalidNameChars) >= 0) return false;
            if (name.EndsWith(".") || name.EndsWith(" ")) return false;
            return true;
        }
    }

    public static class Fmt
    {
        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            double gb = mb / 1024.0;
            return gb.ToString("0.##") + " GB";
        }

        public static string LocalTime(long utcTicks)
        {
            if (utcTicks <= 0) return "";
            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        }

        public static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static string Count(int n)
        {
            return n.ToString("#,0");
        }
    }

    public static class TimeUtil
    {
        public static bool TryParseHm(string s, out TimeSpan ts)
        {
            ts = TimeSpan.Zero;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Trim().Split(':');
            if (parts.Length != 2) return false;
            int h, m;
            if (!int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            ts = new TimeSpan(h, m, 0);
            return true;
        }

        public static bool InWindow(JobConfig job, DateTime now)
        {
            if (!job.UseTimeWindow) return true;
            TimeSpan a, b;
            if (!TryParseHm(job.WindowStart, out a) || !TryParseHm(job.WindowEnd, out b)) return true;
            TimeSpan t = now.TimeOfDay;
            if (a <= b) return t >= a && t <= b;
            return t >= a || t <= b; // 日をまたぐ
        }
    }

    /// <summary>アイコンをプログラムで描く（画像ファイル不要）</summary>
    public static class IconFactory
    {
        public static readonly Color Blue = Color.FromArgb(0, 95, 184);
        public static readonly Color Green = Color.FromArgb(15, 123, 15);
        public static readonly Color Orange = Color.FromArgb(194, 122, 0);
        public static readonly Color Red = Color.FromArgb(196, 43, 28);
        public static readonly Color Gray = Color.FromArgb(120, 120, 120);

        private static Dictionary<string, Icon> cache = new Dictionary<string, Icon>();

        public static Icon Get(Color main, Color? badge)
        {
            string key = main.ToArgb() + "_" + (badge.HasValue ? badge.Value.ToArgb().ToString() : "none");
            Icon ic;
            if (cache.TryGetValue(key, out ic)) return ic;
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Pen pen = new Pen(main, 3f))
                    {
                        g.DrawRectangle(pen, 3, 9, 17, 20);
                        g.DrawLine(pen, 10, 3, 26, 3);
                        g.DrawLine(pen, 27, 3, 27, 21);
                    }
                    if (badge.HasValue)
                    {
                        using (SolidBrush b = new SolidBrush(badge.Value))
                        {
                            g.FillEllipse(Brushes.White, 17, 17, 15, 15);
                            g.FillEllipse(b, 19, 19, 11, 11);
                        }
                    }
                }
                IntPtr h = bmp.GetHicon();
                ic = Icon.FromHandle(h);
            }
            cache[key] = ic;
            return ic;
        }

        public static Icon App { get { return Get(Blue, null); } }
    }
}
