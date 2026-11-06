using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AutoCopy
{
    // Destination paths backed by another configured job must not be treated as orphans.
    public sealed class JobFileScope
    {
        private readonly HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> blockedRoots = new List<string>();

        public static JobFileScope Build(JobConfig job, AppSettings app, CancellationToken ct)
        {
            JobFileScope result = new JobFileScope();
            string dst = CopyEngine.ResolvedDest(job, app);
            foreach (JobConfig other in app.Jobs.ToArray())
            {
                if (other.Id == job.Id) continue;
                string od = CopyEngine.ResolvedDest(other, app);
                if (!PathUtil.IsSameOrInside(dst, od) && !PathUtil.IsSameOrInside(od, dst)) continue;
                try
                {
                    string os = CopyEngine.ResolvedSource(other, app);
                    FileSafety.RequireReadableDirectory(os);
                    ScanResult scan = Scanner.Scan(os, other.IncludeSubdirs, new ExcludeMatcher(other.Exclude), false, ct);
                    foreach (string rel in scan.Files.Keys) result.files.Add(PathUtil.Normalize(PathUtil.Combine(od, rel)));
                    foreach (string rel in scan.FailedDirs) result.blockedRoots.Add(PathUtil.Normalize(PathUtil.Combine(od, rel)));
                    // Reparse-point directories are deliberately not traversed; protect their mapped destination.
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result.blockedRoots.Add(PathUtil.Normalize(od));
                    Logger.Error(job.Name, "別ジョブ「" + other.Name + "」のコピー元を確認できないため、そのコピー先を整理対象から外します: " + ex.Message);
                }
            }
            return result;
        }

        public bool Protects(string path, bool folder)
        {
            string p = PathUtil.Normalize(path);
            foreach (string root in blockedRoots)
                if (PathUtil.IsSameOrInside(p, root) || (folder && PathUtil.IsSameOrInside(root, p))) return true;
            if (files.Contains(p)) return true;
            if (folder) foreach (string f in files) if (PathUtil.IsSameOrInside(f, p)) return true;
            return false;
        }
    }

    public static class FileSafety
    {
        public static void RequireReadableDirectory(string root)
        {
            if (PathUtil.IsUnc(root) && !NetAccess.NetworkUp()) throw new IOException("コピー元に接続できないため処理を停止します: " + root);
            using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(PathUtil.Long(root)).GetEnumerator()) entries.MoveNext();
        }

        public static bool Missing(string root, string rel)
        {
            RequireReadableDirectory(root);
            try { File.GetAttributes(PathUtil.Long(PathUtil.Combine(root, rel))); return false; }
            catch (FileNotFoundException) { RequireReadableDirectory(root); return true; }
            catch (DirectoryNotFoundException) { RequireReadableDirectory(root); return true; }
        }
    }
}
