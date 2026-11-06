using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace AutoCopy
{
    public static class DedupMode
    {
        public const string Off = "off";     // 使わない（すべてコピー）
        public const string Ask = "ask";     // 見つけたら毎回選ぶ（初期値）
        public const string Auto = "auto";   // 自動でテキストにする
    }

    /// <summary>大きいファイルの重複について、あなたが選んだ扱い</summary>
    [DataContract]
    public class DedupDecision
    {
        [DataMember] public string Rel;
        [DataMember] public string Hash;      // 選んだときの中身（変わったら選び直し）
        [DataMember] public string Action;    // text / copy / skip（確認済み、通常コピーを継続）
        [DataMember] public string Decided;
    }

    /// <summary>扱いを選んでもらうのを待っている重複</summary>
    [DataContract]
    public class DedupPendingItem
    {
        [DataMember] public string Rel;       // 2つ目以降のファイル
        [DataMember] public string Target;    // 中身が同じ1つ目（コピーするもの）
        [DataMember] public long Size;
        [DataMember] public long Mtime;       // コピー元の更新日時（UTC ticks）
        [DataMember] public string Hash;
        [DataMember] public string Found;
        [DataMember] public string Origin;    // copy = コピーのとき見つけた / check = 重複チェックで見つけた
        [DataMember] public bool Copied;      // コピー先に普通にコピー済み（テキストにすると容量が空く）
    }

    [DataContract]
    public class DedupBook
    {
        [DataMember] public List<DedupDecision> Decisions = new List<DedupDecision>();
        [DataMember] public List<DedupPendingItem> Pending = new List<DedupPendingItem>();

        [OnDeserialized]
        private void Fix(StreamingContext c)
        {
            if (Decisions == null) Decisions = new List<DedupDecision>();
            if (Pending == null) Pending = new List<DedupPendingItem>();
        }

        public string DecisionFor(string rel, string hash)
        {
            foreach (DedupDecision d in Decisions)
                if (string.Equals(d.Rel, rel, StringComparison.OrdinalIgnoreCase) && d.Hash == hash) return d.Action;
            return null;
        }

        public void Decide(string rel, string hash, string action)
        {
            Decisions.RemoveAll(delegate(DedupDecision d) { return string.Equals(d.Rel, rel, StringComparison.OrdinalIgnoreCase); });
            DedupDecision n = new DedupDecision();
            n.Rel = rel; n.Hash = hash; n.Action = action; n.Decided = Fmt.Now();
            Decisions.Add(n);
            Pending.RemoveAll(delegate(DedupPendingItem p) { return string.Equals(p.Rel, rel, StringComparison.OrdinalIgnoreCase); });
        }

        /// <summary>見つけた場所（origin）ごとに、確認待ちを入れ替える。もう一方で見つけたものは残す。</summary>
        public bool ReplacePending(string origin, List<DedupPendingItem> items)
        {
            string before = Signature();
            Pending.RemoveAll(delegate(DedupPendingItem p) { return p.Origin == origin; });
            foreach (DedupPendingItem i in items)
            {
                DedupPendingItem same = Pending.Find(delegate(DedupPendingItem p) { return string.Equals(p.Rel, i.Rel, StringComparison.OrdinalIgnoreCase); });
                if (same != null) { same.Copied = same.Copied || i.Copied; continue; }
                Pending.Add(i);
            }
            return before != Signature();
        }

        private string Signature()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (DedupPendingItem p in Pending) sb.Append(p.Rel).Append('|').Append(p.Target).Append('|').Append(p.Hash).Append('|').Append(p.Copied).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>ジョブごとの「大きいファイルの重複」の記録（%LOCALAPPDATA%\AutoCopy\state\ジョブID.dedup.json）</summary>
    public static class DedupStore
    {
        private static readonly Dictionary<string, DedupBook> cache = new Dictionary<string, DedupBook>();

        private static string PathFor(string jobId) { return Path.Combine(AppPaths.StateDir, jobId + ".dedup.json"); }

        public static DedupBook Get(string jobId)
        {
            lock (cache)
            {
                DedupBook b;
                if (cache.TryGetValue(jobId, out b)) return b;
                string problem;
                b = Json.LoadSafe<DedupBook>(PathFor(jobId), out problem);
                if (problem != null) Logger.Error(null, "大きいファイルの重複の記録：" + problem);
                if (b == null) b = new DedupBook();
                cache[jobId] = b;
                return b;
            }
        }

        public static void Save(string jobId)
        {
            DedupBook b = Get(jobId);
            lock (b)
            {
                try { Directory.CreateDirectory(AppPaths.StateDir); Json.Save(PathFor(jobId), b); }
                catch (Exception ex) { Logger.Error(null, "大きいファイルの重複の記録を保存できません: " + ex.Message); throw; }
            }
        }

        public static void Forget(string jobId)
        {
            lock (cache) cache.Remove(jobId);
            try { File.Delete(PathFor(jobId) + ".bak"); } catch (Exception) { }
            try { File.Delete(PathFor(jobId)); } catch (Exception) { }
        }

        public static int PendingCount(string jobId)
        {
            DedupBook b = Get(jobId);
            lock (b) return b.Pending.Count;
        }

        /// <summary>テスト用：読み込み済みの記録を捨てる</summary>
        public static void ClearCache() { lock (cache) cache.Clear(); }
    }

    /// <summary>中身が同じ大きいファイルのまとまり（1つ目と、2つ目以降）</summary>
    public class LargeDupGroup
    {
        public string First;
        public List<string> Others = new List<string>();
        public string Hash;
        public long Size;
    }

    public static class LargeDup
    {
        public static string Hex(byte[] h) { return h == null ? null : BitConverter.ToString(h).Replace("-", ""); }

        /// <summary>
        /// コピー元の中で、min バイト以上・中身が同じファイルを探す（同じ大きさのものだけを読む。コピー元は読むだけ）。
        /// 1つ目は、コピー先に普通にコピー済みのものを優先し、なければパスの順で最初のもの。
        /// </summary>
        public static List<LargeDupGroup> Find(string src, string dst, ScanResult scan, Dictionary<string, FileRecord> files,
            long min, HashSet<string> skip, CancellationToken ct, Dictionary<string, byte[]> hashes)
        {
            List<LargeDupGroup> result = new List<LargeDupGroup>();
            Dictionary<long, List<string>> bySize = new Dictionary<long, List<string>>();
            foreach (KeyValuePair<string, ScanItem> kv in scan.Files)
            {
                if (kv.Value.Size < min || kv.Value.Recent || (skip != null && skip.Contains(kv.Key))) continue;
                if (kv.Key.EndsWith(AppPaths.PlaceholderSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                List<string> l;
                if (!bySize.TryGetValue(kv.Value.Size, out l)) { l = new List<string>(); bySize[kv.Value.Size] = l; }
                l.Add(kv.Key);
            }
            foreach (KeyValuePair<long, List<string>> g in bySize)
            {
                if (g.Value.Count < 2) continue;
                Dictionary<string, List<string>> byHash = new Dictionary<string, List<string>>();
                foreach (string rel in g.Value)
                {
                    ct.ThrowIfCancellationRequested();
                    ScanItem it = scan.Files[rel];
                    FileRecord rec;
                    byte[] h = null;
                    if (files != null && files.TryGetValue(rel, out rec) && rec.Hash != null && rec.Size == it.Size && rec.Mtime == it.Mtime) h = rec.Hash;
                    if (h == null) h = CopyEngine.HashFile(PathUtil.Combine(src, rel), ct);
                    if (h == null) continue;
                    if (hashes != null) hashes[rel] = h;
                    string k = Hex(h);
                    List<string> l;
                    if (!byHash.TryGetValue(k, out l)) { l = new List<string>(); byHash[k] = l; }
                    l.Add(rel);
                }
                foreach (KeyValuePair<string, List<string>> same in byHash)
                {
                    if (same.Value.Count < 2) continue;
                    string first = ChooseFirst(dst, same.Value, scan, files);
                    same.Value.Sort(StringComparer.OrdinalIgnoreCase);
                    LargeDupGroup grp = new LargeDupGroup();
                    grp.First = first; grp.Hash = same.Key; grp.Size = g.Key;
                    foreach (string rel in same.Value)
                    {
                        if (string.Equals(rel, first, StringComparison.OrdinalIgnoreCase)) continue;
                        // MD5 が同じでも、念のため中身を1バイトずつ比べてから「重複」とする（一度確かめた組は覚えておく）
                        if (!VerifiedSame(src, rel, first, scan)) { Logger.Error(null, "大きいファイルの重複：" + rel + " と " + first + " は MD5 が同じですが中身が違うため、重複として扱いません"); continue; }
                        grp.Others.Add(rel);
                    }
                    if (grp.Others.Count > 0) result.Add(grp);
                }
            }
            return result;
        }

        private static readonly Dictionary<string, bool> verified = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static bool VerifiedSame(string src, string a, string b, ScanResult scan)
        {
            ScanItem ia, ib;
            scan.Files.TryGetValue(a, out ia); scan.Files.TryGetValue(b, out ib);
            string key = PathUtil.Normalize(src) + "|" + a + "|" + b + "|" + (ia != null ? ia.Size + ":" + ia.Mtime : "") + "|" + (ib != null ? ib.Mtime.ToString() : "");
            lock (verified) { bool v; if (verified.TryGetValue(key, out v)) return v; }
            bool same;
            try { same = SafeFs.SameBytes(PathUtil.Combine(src, a), PathUtil.Combine(src, b)); }
            catch (Exception ex) { Logger.Error(null, "中身を比べられません: " + a + "　" + ex.Message); return false; }
            lock (verified) { if (verified.Count > 100000) verified.Clear(); verified[key] = same; }
            return same;
        }

        /// <summary>
        /// 1つ目（コピーして残すもの）を決める。優先順位：
        /// ① 先にコピーしたもの（コピー先に普通にコピー済みで、初めてコピーした日時が早いもの。日時が不明なものはその次）
        /// ② パス名が短いもの　③ ファイル名が短いもの　④ それでも同じならパスの文字の順
        /// </summary>
        public static string ChooseFirst(string dst, List<string> same, ScanResult scan, Dictionary<string, FileRecord> files)
        {
            string best = null;
            int bestCopied = 0; long bestAt = 0;
            foreach (string rel in same)
            {
                int copied = 0; long at = long.MaxValue;
                if (IsNormalCopy(dst, rel, scan.Files[rel], files))
                {
                    copied = 1;
                    FileRecord rec = files[rel];
                    if (rec.CopiedAt > 0) at = rec.CopiedAt;
                }
                if (best == null) { best = rel; bestCopied = copied; bestAt = at; continue; }
                // c > 0 なら rel の方が1つ目にふさわしい
                int c = copied.CompareTo(bestCopied);                    // コピー済みが先
                if (c == 0 && copied == 1) c = bestAt.CompareTo(at);     // 先にコピーしたもの
                if (c == 0) c = best.Length.CompareTo(rel.Length);       // パス名が短いもの
                if (c == 0) c = PathUtil.FileName(best).Length.CompareTo(PathUtil.FileName(rel).Length);   // ファイル名が短いもの
                if (c == 0) c = string.Compare(best, rel, StringComparison.OrdinalIgnoreCase) > 0 ? 1 : 0;
                if (c > 0) { best = rel; bestCopied = copied; bestAt = at; }
            }
            return best;
        }

        /// <summary>コピー先に、変わっていないファイルが普通にコピーしてあるか</summary>
        public static bool IsNormalCopy(string dst, string rel, ScanItem it, Dictionary<string, FileRecord> files)
        {
            FileRecord rec;
            return files != null && files.TryGetValue(rel, out rec) && rec.PlaceholderOf == null && rec.Size == it.Size && rec.Mtime == it.Mtime
                && File.Exists(PathUtil.Long(PathUtil.Combine(dst, rel)));
        }

        public static DedupPendingItem Pending(string rel, LargeDupGroup g, ScanItem it, string origin, bool copied)
        {
            DedupPendingItem p = new DedupPendingItem();
            p.Rel = rel; p.Target = g.First; p.Size = it.Size; p.Mtime = it.Mtime; p.Hash = g.Hash;
            p.Found = Fmt.Now(); p.Origin = origin; p.Copied = copied;
            return p;
        }

        /// <summary>重複の代わりに置くテキスト</summary>
        public static string PlaceholderText(string src, string dst, string rel, string target, long size, long mtime, string hash)
        {
            string nl = "\r\n";
            string firstDest = PathUtil.Combine(dst, target);
            return "自動コピー：このファイルはコピーしていません（容量を節約するため）" + nl + nl
                + "コピー元のファイル：" + PathUtil.Plain(PathUtil.Combine(src, rel)) + nl
                + "中身が同じファイルの保管場所：" + PathUtil.Plain(firstDest) + nl
                + "（コピー元では：" + PathUtil.Plain(PathUtil.Combine(src, target)) + "）" + nl + nl
                + "サイズ：" + Fmt.Size(size) + "（" + size.ToString("#,0") + " バイト）" + nl
                + "更新日時：" + new DateTime(mtime, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") + nl
                + (hash != null ? "MD5：" + hash + nl : "")
                + "書いた日時：" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + nl + nl
                + "このファイルが必要なときは、上の保管場所のファイルをコピーして、名前を「" + PathUtil.FileName(rel) + "」に変えて使ってください。" + nl
                + "コピー元でどちらかの中身が変わると、次の自動コピーで普通にコピーし直し、このテキストは片付けます。" + nl;
        }

        /// <summary>
        /// 確認待ちの重複1件の扱いを決めて、すぐに反映する。
        /// text：コピー先に普通にコピー済みなら「削除済み」へ移し（元に戻せる）、テキストを置く。copy：普通にコピーする。
        /// 戻り値：できなかった理由（できたら null）
        /// </summary>
        public static string Resolve(JobConfig job, AppSettings app, JobState state, DedupPendingItem item, string action)
        {
            if (action != "text" && action != "copy" && action != "skip") throw new ArgumentException("不明な操作です", "action");
            string src = CopyEngine.ResolvedSource(job, app), dst = CopyEngine.ResolvedDest(job, app);
            DedupBook book = DedupStore.Get(job.Id);
            string s = PathUtil.Combine(src, item.Rel), d = PathUtil.Combine(dst, item.Rel);
            FileInfo fi = new FileInfo(PathUtil.Long(s));
            if (!fi.Exists)
            {
                lock (book) book.Pending.Remove(item);
                DedupStore.Save(job.Id);
                return item.Rel + "：コピー元にもうありません";
            }
            if (action == "skip")
            {
                string hash = Hex(CopyEngine.HashFile(s));
                fi.Refresh();
                if (hash == null || hash != item.Hash || !fi.Exists || fi.Length != item.Size || fi.LastWriteTimeUtc.Ticks != item.Mtime)
                    return item.Rel + "：検索後に内容が変わったため確認済みにしません。再チェックしてください。";
                lock (book)
                {
                    List<DedupDecision> oldDecisions = new List<DedupDecision>(book.Decisions);
                    List<DedupPendingItem> oldPending = new List<DedupPendingItem>(book.Pending);
                    book.Decide(item.Rel, item.Hash, "skip");
                    try { DedupStore.Save(job.Id); }
                    catch { book.Decisions = oldDecisions; book.Pending = oldPending; throw; }
                }
                return null;
            }
            NetAccess.Ensure(dst, job.RetryCount, job.RetryIntervalSeconds, CancellationToken.None, null);
            string ph = d + AppPaths.PlaceholderSuffix;
            if (action == "text")
            {
                string target = PathUtil.Combine(dst, item.Target);
                if (fi.Length != item.Size || fi.LastWriteTimeUtc.Ticks != item.Mtime
                    || !SafeFs.SameBytes(s, PathUtil.Combine(src, item.Target))
                    || !File.Exists(PathUtil.Long(target)) || !SafeFs.SameBytes(s, target))
                    return item.Rel + "：検索後に内容が変わったため処理しません。再チェックしてください。";
                if (File.Exists(PathUtil.Long(d)) && !SafeFs.SameBytes(s, d))
                    return item.Rel + "：コピー先の内容が変わったため処理しません。";
                JobFileScope scope = JobFileScope.Build(job, app, CancellationToken.None);
                if (scope.Protects(d, false)) return item.Rel + "：別ジョブが使用するファイルのためテキストに置換しません。";
                if (!File.Exists(PathUtil.Long(PathUtil.Combine(dst, item.Target))))
                    return item.Rel + "：中身が同じ「" + item.Target + "」がまだコピー先にないため、テキストにできません（コピーの後でもう一度選んでください）";
                if (File.Exists(PathUtil.Long(d)))
                    SafeFs.MoveToTrash(dst, item.Rel, "削除済み", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                SafeFs.WriteTextBom(ph, PlaceholderText(src, dst, item.Rel, item.Target, fi.Length, fi.LastWriteTimeUtc.Ticks, item.Hash));
                FileRecord rec = new FileRecord();
                rec.Size = fi.Length; rec.Mtime = fi.LastWriteTimeUtc.Ticks; rec.CopiedByApp = true; rec.PlaceholderOf = item.Target;
                rec.DestMtime = File.GetLastWriteTimeUtc(PathUtil.Long(ph)).Ticks;
                lock (state) state.Files[item.Rel] = rec;
                Logger.Write(job.Name, "DEDUP", item.Rel + " → " + item.Target + "（選んでテキストにしました" + (item.Copied ? "。コピー済みのファイルは削除済みへ" : "") + "）", fi.Length, -1);
            }
            else
            {
                if (!File.Exists(PathUtil.Long(d)))
                {
                    byte[] hash = SafeFs.CopyFile(s, d, fi.LastWriteTimeUtc, null, CancellationToken.None);
                    FileRecord rec = new FileRecord();
                    rec.CopiedAt = DateTime.UtcNow.Ticks;
                    rec.Size = fi.Length; rec.Mtime = fi.LastWriteTimeUtc.Ticks; rec.Hash = hash; rec.CopiedByApp = true;
                    rec.DestMtime = File.GetLastWriteTimeUtc(PathUtil.Long(d)).Ticks;
                    lock (state) state.Files[item.Rel] = rec;
                    Logger.Write(job.Name, "COPY", item.Rel + "　（重複だが普通にコピーすると選びました）", fi.Length, -1);
                }
                else Logger.Write(job.Name, "INFO", item.Rel + "　重複だが普通にコピーしたまま残すと選びました", fi.Length, -1);
                if (File.Exists(PathUtil.Long(ph))) { try { SafeFs.DeleteFile(ph); } catch (Exception ex) { Logger.Error(job.Name, "重複のテキストを片付けられません: " + ph + "　" + ex.Message); } }
            }
            lock (book) book.Decide(item.Rel, item.Hash, action);
            DedupStore.Save(job.Id);
            return null;
        }

        public static void ResetSkipped(string jobId)
        {
            DedupBook book = DedupStore.Get(jobId);
            lock (book)
            {
                List<DedupDecision> old = new List<DedupDecision>(book.Decisions);
                book.Decisions.RemoveAll(delegate(DedupDecision d) { return d.Action == "skip"; });
                try { DedupStore.Save(jobId); }
                catch { book.Decisions = old; throw; }
            }
        }
    }
}
