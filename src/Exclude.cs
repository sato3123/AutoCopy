using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AutoCopy
{
    public static class ExcludePresets
    {
        public static readonly string[] Keys = new string[] { "tmp", "video", "music", "archive", "exe" };

        public static string Label(string key)
        {
            switch (key)
            {
                case "tmp": return "一時ファイル（.tmp .bak）";
                case "video": return "動画（.mp4 .mov .avi .wmv .mkv）";
                case "music": return "音楽（.mp3 .wav .wma .m4a .flac）";
                case "archive": return "圧縮（.zip .7z .rar .lzh）";
                case "exe": return "実行ファイル（.exe .msi）";
            }
            return key;
        }

        public static string[] Extensions(string key)
        {
            switch (key)
            {
                case "tmp": return new string[] { ".tmp", ".bak" };
                case "video": return new string[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".mpg", ".m4v" };
                case "music": return new string[] { ".mp3", ".wav", ".wma", ".m4a", ".flac", ".aac" };
                case "archive": return new string[] { ".zip", ".7z", ".rar", ".lzh", ".cab", ".gz" };
                case "exe": return new string[] { ".exe", ".msi" };
            }
            return new string[0];
        }
    }

    /// <summary>除外ルールの判定（どれかに当てはまれば除外。例外は必ずコピー）</summary>
    public class ExcludeMatcher
    {
        private readonly HashSet<string> exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Regex> names = new List<Regex>();
        private readonly List<string> folderNames = new List<string>();
        private readonly List<string> folderPaths = new List<string>();
        private readonly List<Regex> includes = new List<Regex>();
        private readonly List<string> includeDirPrefixes = new List<string>();
        private readonly long maxSize;
        private readonly List<string> onlyFolders = new List<string>();
        private readonly HashSet<string> onlyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool zero, hidden, system;
        private readonly DateTime olderThanUtc;
        private readonly bool useOlder;

        public ExcludeMatcher(ExcludeRules r)
        {
            if (r == null) r = new ExcludeRules();
            foreach (string f in r.OnlyFolders ?? new List<string>()) { string x = (f ?? "").Trim().Replace('/', '\\').Trim('\\'); if (x.Length > 0) onlyFolders.Add(x); }
            foreach (string e in r.OnlyExtensions ?? new List<string>()) { string x = (e ?? "").Trim(); if (x.Length > 0) onlyExtensions.Add(x.StartsWith(".") ? x : "." + x); }
            foreach (string p in r.Presets)
                foreach (string e in ExcludePresets.Extensions(p)) exts.Add(e);
            foreach (string e in r.Extensions)
            {
                string x = (e ?? "").Trim();
                if (x.Length == 0) continue;
                if (!x.StartsWith(".")) x = "." + x;
                exts.Add(x);
            }
            foreach (string n in r.NamePatterns)
            {
                string x = (n ?? "").Trim();
                if (x.Length > 0) names.Add(Wildcard(x));
            }
            foreach (string f in r.Folders)
            {
                string x = (f ?? "").Trim().Trim('\\');
                if (x.Length == 0) continue;
                if (x.Contains("\\")) folderPaths.Add(x); else folderNames.Add(x);
            }
            foreach (string inc in r.Includes)
            {
                string x = (inc ?? "").Trim().Trim('\\');
                if (x.Length == 0) continue;
                includes.Add(Wildcard(x));
                string dir = PathUtil.ParentRel(x);
                if (dir.Length > 0 && dir.IndexOfAny(new char[] { '*', '?' }) < 0) includeDirPrefixes.Add(dir);
            }
            maxSize = r.MaxSizeBytes;
            zero = r.ExcludeZeroByte;
            hidden = r.ExcludeHidden;
            system = r.ExcludeSystem;
            DateTime d;
            if (!string.IsNullOrEmpty(r.OlderThan) &&
                DateTime.TryParseExact(r.OlderThan, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d))
            {
                useOlder = true;
                olderThanUtc = d.ToUniversalTime();
            }
        }

        public static Regex Wildcard(string pattern)
        {
            string re = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return new Regex(re, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public bool InScope(string rel)
        {
            if (onlyFolders.Count > 0 && !onlyFolders.Exists(delegate(string f) { return PathUtil.IsSameOrInsideRel(rel, f); })) return false;
            return onlyExtensions.Count == 0 || onlyExtensions.Contains(Path.GetExtension(rel));
        }

        private bool IsIncluded(string rel)
        {
            string name = PathUtil.FileName(rel);
            foreach (Regex rx in includes)
                if (rx.IsMatch(rel) || rx.IsMatch(name)) return true;
            return false;
        }

        public bool ExcludeDir(string rel, string name, FileAttributes a)
        {
            if (onlyFolders.Count > 0 && !onlyFolders.Exists(delegate(string f) { return PathUtil.IsSameOrInsideRel(rel, f) || PathUtil.IsSameOrInsideRel(f, rel); })) return true;
            bool ex = false, byAttr = false;
            if (hidden && (a & FileAttributes.Hidden) != 0) ex = byAttr = true;
            if (system && (a & FileAttributes.System) != 0) ex = byAttr = true;
            if (!ex)
            {
                foreach (string f in folderNames)
                    if (string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) { ex = true; break; }
            }
            if (!ex)
            {
                foreach (string f in folderPaths)
                    if (string.Equals(f, rel, StringComparison.OrdinalIgnoreCase)) { ex = true; break; }
            }
            if (!ex) return false;
            // 名前・パスで除外したフォルダ：例外（必ずコピー）が中のファイルに当てはまるかもしれないので中を見る
            // （中のファイルは ExcludeFile でフォルダの除外を当てはめる）。
            // 隠し・システムのフォルダ（System Volume Information など）は中を見ない：
            // 中のファイルに隠しの印がなくてもコピーしてしまう・読めずに毎回「一部失敗」になるため
            if (!byAttr && includes.Count > 0) return false;
            // 例外の対象フォルダを含むなら、中を見に行く
            foreach (string p in includeDirPrefixes)
            {
                if (PathUtil.IsSameOrInsideRel(p, rel)) return false;
            }
            return true;
        }

        public bool ExcludeFile(string rel, string name, long size, DateTime mtimeUtc, FileAttributes a)
        {
            if (!InScope(rel)) return true;
            if (IsIncluded(rel)) return false;
            if (hidden && (a & FileAttributes.Hidden) != 0) return true;
            if (system && (a & FileAttributes.System) != 0) return true;
            string ext = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(ext) && exts.Contains(ext)) return true;
            foreach (Regex rx in names) if (rx.IsMatch(name)) return true;
            if (maxSize > 0 && size > maxSize) return true;
            if (zero && size == 0) return true;
            if (useOlder && mtimeUtc < olderThanUtc) return true;
            // フォルダ（相対パス指定）配下のファイル
            foreach (string f in folderPaths)
                if (PathUtil.IsSameOrInsideRel(rel, f)) return true;
            foreach (string f in folderNames)
            {
                string[] segs = rel.Split('\\');
                for (int i = 0; i < segs.Length - 1; i++)
                    if (string.Equals(segs[i], f, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>相対パスだけで判定（ミラー削除時：今は除外対象になったファイルを消さないため）</summary>
        public bool ExcludeByPathOnly(string rel)
        {
            if (!InScope(rel)) return true;
            if (IsIncluded(rel)) return false;
            string name = PathUtil.FileName(rel);
            string ext = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(ext) && exts.Contains(ext)) return true;
            foreach (Regex rx in names) if (rx.IsMatch(name)) return true;
            foreach (string f in folderPaths)
                if (PathUtil.IsSameOrInsideRel(rel, f)) return true;
            string[] segs = rel.Split('\\');
            foreach (string f in folderNames)
                for (int i = 0; i < segs.Length - 1; i++)
                    if (string.Equals(segs[i], f, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
