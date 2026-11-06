using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoCopy
{
    public class LogLine
    {
        public DateTime Time;
        public string Pc, User, Folder, Job, Type, Detail;
        public long Size = -1;
        public double Seconds = -1;

        public static LogLine Parse(string line)
        {
            string[] f = line.Split('\t');
            if (f.Length < 7) return null;
            LogLine l = new LogLine();
            if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out l.Time)) return null;
            l.Pc = f[1]; l.User = f[2]; l.Folder = f[3]; l.Job = f[4]; l.Type = f[5]; l.Detail = f[6];
            if (f.Length > 7 && f[7].Length > 0) long.TryParse(f[7], out l.Size);
            if (f.Length > 8 && f[8].Length > 0) double.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out l.Seconds);
            return l;
        }
    }

    /// <summary>PC（またはPC×ジョブ）ごとの集計1行</summary>
    public class CopyStat
    {
        public string Folder, Pc, Job;
        public int Copied, Overwritten, Renamed, Deleted, Errors, Runs;
        public long CopiedBytes, OverwrittenBytes, DeletedBytes;
        public DateTime First = DateTime.MaxValue, Last = DateTime.MinValue;
        public long TotalBytes { get { return CopiedBytes + OverwrittenBytes; } }
        public int TotalFiles { get { return Copied + Overwritten; } }
    }

    /// <summary>ログから、各PCがコピーした容量などを集計する</summary>
    public static class LogStats
    {
        /// <summary>
        /// COPY（新規）と OVERWRITE（上書き）の行のサイズを足す。SUMMARY は二重に数えないよう使わない。
        /// byJob = true なら PC×ジョブごと。最後の行（Job = null）は全体の合計。
        /// </summary>
        public static List<CopyStat> Aggregate(IEnumerable<LogLine> lines, bool byJob)
        {
            Dictionary<string, CopyStat> map = new Dictionary<string, CopyStat>(StringComparer.OrdinalIgnoreCase);
            CopyStat total = new CopyStat();
            total.Folder = "合計";
            foreach (LogLine l in lines)
            {
                string key = (l.Folder ?? "") + "|" + (l.Pc ?? "") + (byJob ? "|" + (l.Job ?? "") : "");
                CopyStat s;
                if (!map.TryGetValue(key, out s))
                {
                    s = new CopyStat();
                    s.Folder = l.Folder; s.Pc = l.Pc; s.Job = byJob ? l.Job : null;
                    map[key] = s;
                }
                long size = l.Size > 0 ? l.Size : 0;
                foreach (CopyStat t in new CopyStat[] { s, total })
                {
                    switch (l.Type)
                    {
                        case "COPY": t.Copied++; t.CopiedBytes += size; break;
                        case "OVERWRITE": t.Overwritten++; t.OverwrittenBytes += size; break;
                        case "RENAME": t.Renamed++; break;
                        case "DELETE": t.Deleted++; t.DeletedBytes += size; break;
                        case "ERROR": t.Errors++; break;
                        case "SUMMARY": t.Runs++; break;
                    }
                    if (l.Time < t.First) t.First = l.Time;
                    if (l.Time > t.Last) t.Last = l.Time;
                }
            }
            List<CopyStat> list = new List<CopyStat>(map.Values);
            list.Sort(delegate(CopyStat a, CopyStat b)
            {
                int c = b.TotalBytes.CompareTo(a.TotalBytes);
                return c != 0 ? c : string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
            });
            list.Add(total);
            return list;
        }

        /// <summary>
        /// 日ごと・PCごとのコピー容量（新規＋上書き）。日付は古い順。
        /// 表示名はフォルダ名。同じフォルダ名を複数のPCが使っている場合は「フォルダ名（PC名）」で分ける。
        /// </summary>
        public static SortedDictionary<DateTime, Dictionary<string, long>> Daily(IEnumerable<LogLine> lines)
        {
            Dictionary<string, string> labels = Labels(lines);
            SortedDictionary<DateTime, Dictionary<string, long>> map = new SortedDictionary<DateTime, Dictionary<string, long>>();
            foreach (LogLine l in lines)
            {
                if (l.Type != "COPY" && l.Type != "OVERWRITE") continue;
                DateTime d = l.Time.Date;
                Dictionary<string, long> day;
                if (!map.TryGetValue(d, out day)) { day = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); map[d] = day; }
                string pc = labels[Key(l)];
                long v;
                day.TryGetValue(pc, out v);
                day[pc] = v + (l.Size > 0 ? l.Size : 0);
            }
            return map;
        }

        private static string Key(LogLine l) { return (l.Folder ?? "") + "|" + (l.Pc ?? ""); }

        /// <summary>フォルダ名|PC名 → 表示名。フォルダ名が他のPCと重なる場合だけ PC名を付ける。</summary>
        public static Dictionary<string, string> Labels(IEnumerable<LogLine> lines)
        {
            Dictionary<string, HashSet<string>> pcsOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (LogLine l in lines)
            {
                string f = PcLabel(l.Folder, l.Pc);
                HashSet<string> set;
                if (!pcsOf.TryGetValue(f, out set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); pcsOf[f] = set; }
                set.Add(l.Pc ?? "");
            }
            Dictionary<string, string> res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LogLine l in lines)
            {
                string k = Key(l);
                if (res.ContainsKey(k)) continue;
                string f = PcLabel(l.Folder, l.Pc);
                res[k] = pcsOf[f].Count > 1 && !string.IsNullOrEmpty(l.Pc) && !string.IsNullOrEmpty(l.Folder) ? f + "（" + l.Pc + "）" : f;
            }
            return res;
        }

        /// <summary>ログの種類の日本語名（画面・CSVで「COPY（コピー）」のように併記する）</summary>
        public static string TypeJa(string type)
        {
            switch (type ?? "")
            {
                case "COPY": return "コピー";
                case "OVERWRITE": return "上書き";
                case "RENAME": return "名前の変更";
                case "DELETE": return "削除";
                case "RESTORE": return "元に戻す";
                case "PURGE": return "古い退避を整理";
                case "CONFLICT": return "上書き確認待ち";
                case "HOLD": return "削除を保留";
                case "SKIP": return "スキップ";
                case "SYNCIN": return "同期：取り込み";
                case "SYNCDEL": return "同期：削除";
                case "DEDUP": return "大きい重複の置き換え";
                case "DUPCHECK": return "重複チェック";
                case "SUMMARY": return "実行結果のまとめ";
                case "ERROR": return "エラー";
                case "BLOCKED": return "安全のため中止";
                case "RETRY": return "再試行";
                case "UPDATE": return "プログラムの更新";
                case "INFO": return "お知らせ";
                case "HANDLED": return "対応状況";
                default: return "";
            }
        }

        /// <summary>「COPY（コピー）」の形</summary>
        public static string TypeLabel(string type)
        {
            string ja = TypeJa(type);
            return ja.Length == 0 ? (type ?? "") : (type ?? "") + "（" + ja + "）";
        }

        public static string PcLabel(string folder, string pc)
        {
            if (string.IsNullOrEmpty(folder)) return pc ?? "";
            return folder;
        }

        /// <summary>COPY / OVERWRITE 行の内容からファイルの相対パスを取り出す（「（確認して上書き）」などの注記を外す）</summary>
        public static string RelOf(LogLine l)
        {
            string d = (l.Detail ?? "").Trim();
            int i = d.IndexOf("　（", StringComparison.Ordinal);
            if (i > 0 && d.EndsWith("）")) d = d.Substring(0, i);
            return d.Trim().TrimStart('\\');
        }

        /// <summary>
        /// ログのコピー実績から容量の内訳を作る。場所は「PC（フォルダ名）＼ジョブ＼フォルダ…」。
        /// 同じファイルを何度もコピーした場合は、コピーした容量を足し、回数を数える。
        /// </summary>
        public static UsageTree CopyUsage(IEnumerable<LogLine> lines)
        {
            Dictionary<string, string> labels = Labels(lines);
            Dictionary<string, UsageFile> files = new Dictionary<string, UsageFile>(StringComparer.OrdinalIgnoreCase);
            List<string[]> dirsOf = new List<string[]>();
            List<UsageFile> order = new List<UsageFile>();
            foreach (LogLine l in lines)
            {
                if (l.Type != "COPY" && l.Type != "OVERWRITE") continue;
                string rel = RelOf(l);
                if (rel.Length == 0) continue;
                string pc = labels[Key(l)];
                string job = string.IsNullOrEmpty(l.Job) ? "（ジョブなし）" : l.Job;
                string k = pc + "\\" + job + "\\" + rel;
                long size = l.Size > 0 ? l.Size : 0;
                UsageFile f;
                if (!files.TryGetValue(k, out f))
                {
                    f = new UsageFile();
                    int cut = rel.LastIndexOf('\\');
                    f.Name = cut >= 0 ? rel.Substring(cut + 1) : rel;
                    string relDir = cut >= 0 ? rel.Substring(0, cut) : "";
                    f.Dir = pc + "\\" + job + (relDir.Length > 0 ? "\\" + relDir : "");
                    f.Pc = pc; f.Job = job;
                    files[k] = f;
                    order.Add(f);
                    List<string> segs = new List<string>();
                    segs.Add(pc); segs.Add(job);
                    if (relDir.Length > 0) segs.AddRange(relDir.Split('\\'));
                    dirsOf.Add(segs.ToArray());
                }
                f.Size += size;
                f.LastSize = size;
                f.Count++;
                if (l.Time > f.Time) f.Time = l.Time;
            }
            UsageTree t = new UsageTree("ログのコピー実績");
            for (int i = 0; i < order.Count; i++) t.Add(dirsOf[i], order[i]);
            t.Finish();
            return t;
        }
    }

    /// <summary>
    /// ログ。このPC内（%LOCALAPPDATA%\AutoCopy\logs）に必ず残し、
    /// ネットワークのログ保管先（保管先\フォルダ名\日付.log）にも送る。送れない間は outbox に溜める。
    /// </summary>
    public static class Logger
    {
        private static readonly object sync = new object();
        private static readonly object sendSync = new object();
        private static string logRoot;
        private static string folderName;
        public static event Action<LogLine> LineWritten;
        public static DateTime LastSendOk = DateTime.MinValue;
        public static string LastSendError;

        public static void Configure(AppSettings s)
        {
            lock (sync)
            {
                logRoot = string.IsNullOrEmpty(s.LogRoot) ? null : s.LogRoot.Trim();
                folderName = string.IsNullOrEmpty(s.FolderName) ? Environment.MachineName : s.FolderName;
            }
        }

        public static string PcFolderName
        {
            get { lock (sync) { return folderName ?? Environment.MachineName; } }
        }

        private static string Clean(string s)
        {
            if (s == null) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        public static string LastWriteError;

        public static void Write(string job, string type, string detail, long size, double seconds)
        {
            DateTime now = DateTime.Now;
            string line = string.Join("\t", new string[] {
                now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Environment.MachineName, Environment.UserName, Clean(PcFolderName), Clean(job ?? "（アプリ）"), type, Clean(detail),
                size >= 0 ? size.ToString(CultureInfo.InvariantCulture) : "",
                seconds >= 0 ? seconds.ToString("0.0", CultureInfo.InvariantCulture) : "" }) + "\r\n";
            string day = now.ToString("yyyy-MM-dd");
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    File.AppendAllText(Path.Combine(AppPaths.LogDir, day + ".log"), line, new UTF8Encoding(false));
                    if (logRoot != null)
                    {
                        Directory.CreateDirectory(AppPaths.OutboxDir);
                        File.AppendAllText(Path.Combine(AppPaths.OutboxDir, day + ".log"), line, new UTF8Encoding(false));
                    }
                }
                catch (Exception ex) { LastWriteError = ex.Message; }
            }
            Action<LogLine> h = LineWritten;
            if (h != null)
            {
                LogLine l = LogLine.Parse(line.TrimEnd('\r', '\n'));
                if (l != null) { try { h(l); } catch (Exception) { } }
            }
        }

        public static void Info(string job, string msg) { Write(job, "INFO", msg, -1, -1); }
        public static void Error(string job, string msg) { Write(job, "ERROR", msg, -1, -1); }

        public static string NetworkFolderForThisPc()
        {
            string root;
            lock (sync) { root = logRoot; }
            if (root == null) return null;
            return PathUtil.Combine(root, PcFolderName);
        }

        /// <summary>outbox の分をネットワークへ送る。成功なら true（送るものがない場合も true）。</summary>
        public static bool Flush()
        {
            string target = NetworkFolderForThisPc();
            if (target == null) return true;
            lock (sendSync)
            {
                try
                {
                    if (!Directory.Exists(AppPaths.OutboxDir)) return true;
                    // 送信中の名前に変えてから送る（その間の書き込みは新しい outbox ファイルへ）
                    lock (sync)
                    {
                        foreach (string f in Directory.GetFiles(AppPaths.OutboxDir, "*.log"))
                        {
                            string sending = f + "." + Guid.NewGuid().ToString("N") + ".sending";
                            File.Move(f, sending);
                        }
                    }
                    string[] list = Directory.GetFiles(AppPaths.OutboxDir, "*.sending");
                    if (list.Length == 0) return true;
                    Array.Sort(list, StringComparer.Ordinal);
                    NetAccess.Ensure(target, 0, 1, CancellationToken.None, null);
                    foreach (string f in list)
                    {
                        string day = Path.GetFileName(f).Substring(0, 10);
                        LogDelivery.AppendOnce(f, PathUtil.Combine(target, day + ".log"), CancellationToken.None);
                        File.Delete(f);
                    }
                    LastSendOk = DateTime.Now;
                    LastSendError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    LastSendError = ex.Message;
                    return false;
                }
            }
        }

        /// <summary>まだ送れていないログのうち一番古い日（なければ null）</summary>
        public static DateTime? OldestUnsent()
        {
            try
            {
                if (!Directory.Exists(AppPaths.OutboxDir)) return null;
                DateTime? oldest = null;
                foreach (string f in Directory.GetFiles(AppPaths.OutboxDir))
                {
                    DateTime t = File.GetCreationTime(f);
                    if (!oldest.HasValue || t < oldest.Value) oldest = t;
                }
                return oldest;
            }
            catch (Exception) { return null; }
        }

        public static void WriteStatus(PcStatus st)
        {
            string target = NetworkFolderForThisPc();
            if (target == null) return;
            try
            {
                NetAccess.Ensure(target, 0, 1, CancellationToken.None, null);
                SafeFs.WriteText(PathUtil.Combine(target, "status.json"), Json.Serialize(st));
            }
            catch (Exception) { }
        }

        public static void Purge(AppSettings s)
        {
            PurgeDir(AppPaths.LogDir, s.LocalLogDays);
            string net = NetworkFolderForThisPc();
            if (net != null)
            {
                try
                {
                    if (Directory.Exists(PathUtil.Long(net))) PurgeDir(net, s.NetLogDays);
                }
                catch (Exception) { }
            }
        }

        private static void PurgeDir(string dir, int days)
        {
            try
            {
                string l = PathUtil.Long(dir);
                if (!Directory.Exists(l)) return;
                DateTime limit = DateTime.Today.AddDays(-Math.Max(1, days));
                foreach (string f in Directory.GetFiles(l, "????-??-??.log"))
                {
                    DateTime d;
                    if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                        && d < limit)
                        SafeFs.DeleteFile(f);
                }
            }
            catch (Exception) { }
        }

        /// <summary>ログを読む。allPcs なら保管先の全PC分。</summary>
        public static List<LogLine> Read(DateTime from, DateTime to, bool allPcs, out string note)
        {
            return Read(from, to, allPcs, out note, CancellationToken.None);
        }

        public static List<LogLine> Read(DateTime from, DateTime to, bool allPcs, out string note, CancellationToken ct)
        {
            note = null;
            List<LogLine> list = new List<LogLine>();
            List<string> dirs = new List<string>();
            string root;
            lock (sync) { root = logRoot; }
            if (allPcs && root != null)
            {
                try
                {
                    NetAccess.Ensure(root, 0, 1, CancellationToken.None, null);
                    foreach (string d in Directory.GetDirectories(PathUtil.Long(root))) dirs.Add(d);
                    Flush();
                }
                catch (Exception ex)
                {
                    note = "ログ保管先を読めません（" + ex.Message + "）。このPCのログを表示しています。";
                    dirs.Clear();
                }
            }
            if (dirs.Count == 0) { dirs.Add(AppPaths.LogDir); if (allPcs && root == null) note = "ログ保管先が未設定のため、このPCのログを表示しています。"; }
            for (DateTime day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                foreach (string d in dirs)
                {
                    string f = Path.Combine(d, day.ToString("yyyy-MM-dd") + ".log");
                    if (!File.Exists(f)) continue;
                    try
                    {
                        using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                            {
                                ct.ThrowIfCancellationRequested();
                                LogLine l = LogLine.Parse(line);
                                if (l != null) list.Add(l);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { }
                }
            }
            list.Sort(delegate(LogLine a, LogLine b) { return b.Time.CompareTo(a.Time); });
            return list;
        }

        /// <summary>保管先の全PCの状態</summary>
        public static List<PcStatus> ReadAllStatus()
        {
            List<PcStatus> list = new List<PcStatus>();
            string root;
            lock (sync) { root = logRoot; }
            if (root == null) return list;
            try
            {
                foreach (string d in Directory.GetDirectories(PathUtil.Long(root)))
                {
                    string f = Path.Combine(d, "status.json");
                    if (!File.Exists(f)) continue;
                    try { PcStatus st = Json.Deserialize<PcStatus>(File.ReadAllText(f, Encoding.UTF8)); if (st != null) list.Add(st); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}
