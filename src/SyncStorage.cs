using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AutoCopy
{
    /// <summary>同期：削除の確認待ち1件</summary>
    [DataContract]
    public class SyncDeleteItem
    {
        public const string SideLocal = "local";     // 共有フォルダで消えた → このPCから消すか
        public const string SideShared = "shared";   // このPCで消した → 共有フォルダから消すか
        [DataMember] public string Rel;
        [DataMember] public string Side;
        [DataMember] public long Size;
        [DataMember] public long Mtime;
        [DataMember] public string Detected;

        public string SideText { get { return Side == SideLocal ? "共有フォルダで削除 → このPCからも消す" : "このPCで削除 → 共有フォルダからも消す"; } }
    }

    [DataContract]
    public class SyncDeleteBook
    {
        [DataMember] public List<SyncDeleteItem> Items = new List<SyncDeleteItem>();
    }

    /// <summary>同期の削除の確認待ちを、ジョブごとに保存する（state\&lt;id&gt;.syncdel.json）</summary>
    public static class SyncDeleteStore
    {
        private static readonly Dictionary<string, SyncDeleteBook> cache = new Dictionary<string, SyncDeleteBook>();

        public static string PathFor(string jobId) { return Path.Combine(AppPaths.StateDir, jobId + ".syncdel.json"); }

        public static SyncDeleteBook Get(string jobId)
        {
            lock (cache)
            {
                SyncDeleteBook b;
                if (cache.TryGetValue(jobId, out b)) return b;
                string problem;
                b = Json.LoadSafe<SyncDeleteBook>(PathFor(jobId), out problem);
                if (problem != null) Logger.Error(null, "同期の削除の確認：" + problem + "（候補は次の実行で見つけ直します。削除はしません）");
                if (b == null) b = new SyncDeleteBook();
                if (b.Items == null) b.Items = new List<SyncDeleteItem>();
                cache[jobId] = b;
                return b;
            }
        }

        public static List<SyncDeleteItem> GetItems(string jobId)
        {
            SyncDeleteBook b = Get(jobId);
            lock (b) return new List<SyncDeleteItem>(b.Items);
        }

        public static int Count(string jobId)
        {
            SyncDeleteBook b = Get(jobId);
            lock (b) return b.Items.Count;
        }

        public static void Save(string jobId)
        {
            SyncDeleteBook b = Get(jobId);
            // 保存できなければ例外にする（握りつぶさない）：実行はエラーとして知らせ、確認の画面ではエラーを表示する
            lock (b)
            {
                try { Directory.CreateDirectory(AppPaths.StateDir); Json.Save(PathFor(jobId), b); }
                catch (Exception ex)
                {
                    Logger.Error(null, "同期の削除の確認を保存できません: " + ex.Message);
                    throw new IOException("同期の削除の確認を保存できませんでした（" + ex.Message + "）。削除・取り込みは行っていません。", ex);
                }
            }
        }

        /// <summary>今回の判定の結果で置き換える（見つけた日時は前のものを引き継ぐ）。新しく見つかった件数を返す。</summary>
        public static int Replace(string jobId, List<SyncDeleteItem> now)
        {
            SyncDeleteBook b = Get(jobId);
            int fresh = 0;
            lock (b)
            {
                Dictionary<string, SyncDeleteItem> old = new Dictionary<string, SyncDeleteItem>(StringComparer.OrdinalIgnoreCase);
                foreach (SyncDeleteItem i in b.Items) old[i.Side + "|" + i.Rel] = i;
                List<SyncDeleteItem> list = new List<SyncDeleteItem>();
                foreach (SyncDeleteItem i in now)
                {
                    SyncDeleteItem o;
                    if (old.TryGetValue(i.Side + "|" + i.Rel, out o)) i.Detected = o.Detected; else fresh++;
                    list.Add(i);
                }
                bool same = list.Count == b.Items.Count && fresh == 0;
                if (same) { b.Items = list; return 0; }
                // 変更と保存をひとまとまりにする（途中で他の変更が入らない）。保存できなければ元の内容に戻す
                List<SyncDeleteItem> before = b.Items;
                b.Items = list;
                try { Save(jobId); }
                catch { b.Items = before; throw; }
            }
            return fresh;
        }

        public static void Remove(string jobId, IEnumerable<SyncDeleteItem> items)
        {
            SyncDeleteBook b = Get(jobId);
            lock (b)
            {
                HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SyncDeleteItem i in items) keys.Add(i.Side + "|" + i.Rel);
                List<SyncDeleteItem> before = b.Items;
                List<SyncDeleteItem> after = new List<SyncDeleteItem>(before);
                if (after.RemoveAll(delegate(SyncDeleteItem x) { return keys.Contains(x.Side + "|" + x.Rel); }) == 0) return;
                b.Items = after;
                try { Save(jobId); }
                catch { b.Items = before; throw; }   // 保存できなければ元の内容に戻す（画面と保存の内容をそろえる）
            }
        }

        public static void Forget(string jobId)
        {
            lock (cache) cache.Remove(jobId);
            try { File.Delete(PathFor(jobId)); File.Delete(PathFor(jobId) + ".bak"); }
            catch (Exception ex) { Logger.Error(null, "同期の削除の確認のファイルを消せません: " + ex.Message); }
        }
    }

    /// <summary>
    /// 同期：共有フォルダの一覧（前回の詳しい確認の結果）を覚えておき、時間のかかる確認を省略できるようにする。
    /// 共有フォルダの「_AutoCopy\sync-stamp.txt」（変更の印）は、どのPCでも共有フォルダを変えたら新しくする。
    /// 印が前回と同じで、前回の詳しい確認から決めた時間以内なら、前回の結果を使う。
    /// （自動コピー以外の方法で共有フォルダを直接変えた場合は、決めた時間がたってから反映される）
    /// </summary>
    public static class SyncRemoteCache
    {
        public class Entry
        {
            public string Root;
            public ScanResult Remote;
            public DateTime FullScanAt;
            public string Stamp;
            public string Rules;   // 調べたときの除外・対象の設定（変わったら使わない）

            public ScanResult CopyRemote()
            {
                ScanResult r = new ScanResult();
                lock (this)
                {
                    foreach (KeyValuePair<string, ScanItem> kv in Remote.Files)
                    {
                        ScanItem it = new ScanItem(); it.Size = kv.Value.Size; it.Mtime = kv.Value.Mtime; it.Attr = kv.Value.Attr; it.Recent = false;
                        r.Files[kv.Key] = it;
                    }
                    foreach (string d in Remote.Dirs) r.Dirs.Add(d);
                }
                return r;
            }
        }

        private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        public const string StampName = "sync-stamp.txt";

        private static string StampPath(string dst) { return PathUtil.Combine(PathUtil.Combine(dst, AppPaths.DestMetaFolder), StampName); }

        /// <summary>共有フォルダの変更の印（ない = ""、読めない = null）</summary>
        public static string ReadStamp(string dst)
        {
            try
            {
                string p = PathUtil.Long(StampPath(dst));
                return File.Exists(p) ? File.ReadAllText(p).Trim() : "";
            }
            catch (Exception ex) { Logger.Error(null, "共有フォルダの変更の印を読めません（毎回詳しく確認します）: " + ex.Message); return null; }
        }

        /// <summary>共有フォルダを変えたので、印を新しくする（他のPCが次に詳しく確認するように）</summary>
        public static string TouchStamp(string dst)
        {
            string v = Guid.NewGuid().ToString("N") + "|" + Environment.MachineName + "|" + DateTime.UtcNow.ToString("o");
            try { SafeFs.WriteText(StampPath(dst), v); SafeFs.HideMeta(dst); return v; }
            catch (Exception ex) { Logger.Error(null, "共有フォルダの変更の印を書けません: " + ex.Message); return null; }
        }

        /// <summary>一覧の内容を左右する設定（除外・対象・サブフォルダ）。これが変わったら前回の一覧は使えない</summary>
        public static string RulesOf(JobConfig job)
        {
            return (job.IncludeSubdirs ? "1|" : "0|") + Json.Serialize(job.Exclude ?? new ExcludeRules());
        }

        public static Entry Get(string jobId, string dst)
        {
            return Get(jobId, dst, null);
        }

        public static Entry Get(string jobId, string dst, string rules)
        {
            lock (cache)
            {
                Entry e;
                if (cache.TryGetValue(jobId, out e) && string.Equals(e.Root, dst, StringComparison.OrdinalIgnoreCase)
                    && (rules == null || string.Equals(e.Rules, rules, StringComparison.Ordinal))) return e;
                return null;
            }
        }

        public static void Put(string jobId, string dst, ScanResult remote, string stamp)
        {
            Put(jobId, dst, remote, stamp, null);
        }

        public static void Put(string jobId, string dst, ScanResult remote, string stamp, string rules)
        {
            Entry e = new Entry();
            e.Root = dst; e.Remote = remote; e.FullScanAt = DateTime.Now; e.Stamp = stamp; e.Rules = rules;
            lock (cache) cache[jobId] = e;
        }

        /// <summary>自分で共有フォルダへコピーしたものを、覚えている一覧に反映する（印も自分が書いた新しいものにする）</summary>
        public static void Update(string jobId, List<KeyValuePair<string, ScanItem>> items, string newStamp)
        {
            Entry e;
            lock (cache) { if (!cache.TryGetValue(jobId, out e)) return; }
            if (newStamp == null) { Invalidate(jobId); return; }
            lock (e)
            {
                foreach (KeyValuePair<string, ScanItem> kv in items) e.Remote.Files[kv.Key] = kv.Value;
                e.Stamp = newStamp;
            }
        }

        public static void Invalidate(string jobId)
        {
            lock (cache) cache.Remove(jobId);
        }
    }
}
