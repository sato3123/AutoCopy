using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AutoCopy.Tests
{
    /// <summary>
    /// 自動コピーの自動テスト。コピー処理・名前変更・ミラー・除外・元データ保護・重複チェック・配布設定・ログなど。
    /// どのテストでも、実行の前後でコピー元が1バイトも変わっていないことを自動で確かめる。
    /// </summary>
    internal static class TestMain
    {
        private static string baseDir;
        private static readonly List<string[]> results = new List<string[]>();

        private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            Console.OutputEncoding = Encoding.UTF8;
            baseDir = Path.Combine(Path.GetTempPath(), "autocopy-tests");
            if (Directory.Exists(baseDir)) Directory.Delete(PathUtil.Long(baseDir), true);
            Directory.CreateDirectory(baseDir);
            Environment.SetEnvironmentVariable("AUTOCOPY_HOME", Path.Combine(baseDir, "_home"));

            Add("T01", "新規コピー（サブフォルダ含む・更新日時の引き継ぎ）", T01_NewCopy);
            Add("T02", "変更がなければ何もコピーしない", T02_NoChange);
            Add("T03", "更新されたファイルだけ上書きする", T03_Update);
            Add("T04", "コピー先が新しい→確認待ち（上書きしない）", T04_ConflictAsk);
            Add("T05", "コピー先が新しい→設定「上書きしない」", T05_ConflictSkip);
            Add("T06", "コピー先が新しい→設定「常に上書き」", T06_ConflictOverwrite);
            Add("T07", "上書き確認の解決（上書き・今回はスキップ・今後も上書きしない）", T07_ResolveConflict);
            Add("T08", "ファイル名の変更→コピー先も名前変更（再コピーなし）", T08_RenameFile);
            Add("T09", "フォルダ名の変更→フォルダごと名前変更（再コピーなし）", T09_RenameFolder);
            Add("T10", "別フォルダへの移動→コピー先も移動", T10_MoveFile);
            Add("T11", "名前変更と内容変更が同時→名前変更後に上書き", T11_RenameAndModify);
            Add("T12", "名前変更への追従オフ→通常コピー（旧名は残る）", T12_FollowOff);
            Add("T13", "ファイルIDが使えない場合の名前変更判定（サイズ・日時・内容）", T13_RenameFallback);
            Add("T14", "除外ルール（種類・名前・サイズ・0バイト・フォルダ・隠し・日付・例外）", T14_Exclude);
            Add("T15", "追加・更新のみ：コピー元で削除してもコピー先は残す", T15_NoMirrorKeep);
            Add("T16", "ミラー：コピー元で削除→削除済みへ移動・空フォルダ片付け", T16_MirrorDelete);
            Add("T17", "ミラー：コピー元が空なら削除しない", T17_MirrorEmptySource);
            Add("T18", "ミラー：大量削除は保留→承認で削除／取りやめで残す", T18_MirrorBulk);
            Add("T19", "ミラー：人が置いたファイル・コピー先で更新されたファイルは削除しない", T19_MirrorSafety);
            Add("T20", "ミラー：除外対象になったファイルは削除しない", T20_MirrorExcluded);
            Add("T21", "元データ保護（コピー元への書き込み・移動・削除を拒否、重なる設定を拒否）", T21_SourceProtection);
            Add("T22", "上書き前の退避", T22_BackupBeforeOverwrite);
            Add("T23", "テスト実行（ドライラン）は何も変えない", T23_DryRun);
            Add("T24", "保存直後（5秒以内）のファイルは次回に回す", T24_RecentDeferred);
            Add("T25", "使用中（ロック中）のファイルは次回コピー", T25_LockedFile);
            Add("T26", "長いパス（260文字超）", T26_LongPath);
            Add("T27", "フォルダ名の重複（他のPCが使用中）を止める", T27_OwnerConflict);
            Add("T28", "{フォルダ名} の展開とコピー先の自動作成", T28_FolderVariable);
            Add("T29", "全体を再確認：コピー先で消えたファイルを戻す", T29_FullVerify);
            Add("T30", "重複チェック：名前変更の取りこぼし→削除→元に戻す", T30_DupRename);
            Add("T31", "重複チェック：フォルダ名変更の取りこぼし（フォルダ単位）", T31_DupFolder);
            Add("T32", "重複チェック：中身が同じファイル（情報のみ）", T32_DupSame);
            Add("T33", "配布設定：暗号化・パスワード誤り・改ざん検知・取り込み", T33_Distribution);
            Add("T34", "状態ファイルの保存と読み込み", T34_StateStore);
            Add("T35", "ログ：このPCとログ保管先への送信・状態ファイル・全PC読み込み", T35_Logger);
            Add("T36", "削除済みの保存期間切れを片付ける", T36_TrashPurge);
            Add("T37", "接続できないときの再試行（回数・間隔・ログ）", T37_Retry);
            Add("T38", "パスの展開・判定", T38_PathUtil);
            Add("T39", "スタートアップ登録・ショートカット作成", T39_StartupShortcut);
            Add("T40", "ネットワーク共有：ID・パスワード・接続テスト・コピー", T40_NetworkShare);
            Add("T41", "アプリの実行管理（RunJob・状態・通知）", T41_AppCoreRun);
            Add("T42", "性能：2000ファイルの初回と2回目", T42_Performance);
            Add("T43", "ログから各PCのコピー容量を集計（PC別・ジョブ別・合計）", T43_CopyStats);
            Add("T44", "ログのコピー実績の内訳（場所別・種類別・大きいファイル・同じフォルダ名のPCを分ける）", T44_CopyUsage);
            Add("T45", "フォルダの容量の内訳（フォルダ別・種類別・大きいファイル・読むだけ）", T45_DiskUsage);
            Add("T46", "容量の内訳を途中で中止できる（途中経過の通知・中止後もフォルダは変わらない）", T46_DiskUsageCancel);
            Add("T47", "空のフォルダの削除（コピー先のみ・コピー元にあるものは残す・削除直前の再確認・元に戻す）", T47_EmptyFolders);
            Add("T48", "コピー先の _AutoCopy フォルダは隠しフォルダ", T48_HiddenMeta);
            Add("T49", "大きいファイルの重複は1つだけコピーし、2つ目以降は保管場所を書いたテキストを置く", T49_Dedup);
            Add("T50", "重複のテキスト：中身が変わったら普通にコピー・ミラーで1つ目が消えたら付け替え・重複チェックで孤立にしない", T50_DedupChanges);
            Add("T51", "複数PCの同期：双方向の取り込み・前の版の控え・削除は伝えない・持ち主の確認をしない", T51_Sync);
            Add("T52", "同期：両方で変えたら確認待ち・共有の方を使う・このPCの方を使う・コピー元の削除は今までどおり拒否", T52_SyncConflict);
            Add("T53", "確認が必要なものの一覧（上書きの確認・ミラーの保留・未確認の重複チェック・エラー）", T53_Attention);
            Add("T54", "自動更新：版の比較・置き場所の確認・署名のない版の扱い", T54_UpdaterBasics);
            Add("T55", "実行結果の予測：コピー・上書き・確認待ち・名前変更・除外・ミラー削除を一覧にし、何も変えない", T55_Preview);
            Add("T56", "作業状況：件数・容量・進み具合・コピー中のファイル", T56_Progress);
            Add("T57", "大きいファイルの重複の扱い（使わない・毎回選ぶ・自動）と前の版の設定の引き継ぎ", T57_DedupChoice);
            Add("T58", "大きいファイルの重複：見つけたら毎回選ぶ（コピー時・重複チェック）・テキストにする・普通にコピー", T58_DedupAsk);
            Add("T59", "大きいファイルの重複：1つ目は「先にコピーしたもの → パス名が短いもの → ファイル名が短いもの」の順で決める", T59_DedupFirst);
            Add("T60", "同期：一方で削除されたファイルは削除候補として示し、確認してから削除（控え・削除済みへ）／残すと戻す", T60_SyncDeleteCandidates);
            Add("T61", "重複チェック：名前変更が大量でも速く終わり、同じフォルダのものは1つのまとまりにする", T61_DupManyRenames);
            Add("T62", "お知らせ：未対応・対応済み（自動・手動）・同じお知らせはまとめる・保存される", T62_NoticeStates);
            Add("T63", "同期：共有フォルダにだけあるファイルは、初期値ではこのPCにコピーしない（設定で取り込める）", T63_SyncTakeNew);
            Add("T64", "実行の予定：重複チェックの毎月・毎年・ログオン時、止まっている間に過ぎた予定は起動後に実行", T64_Schedule);
            Add("T65", "フォルダ内の重複ファイル検索：コピーと関係なく探す・1つ残す候補・移す（戻せる）・コピー元は移さない", T65_FolderDup);
            Add("T66", "条件で選ぶ：名前・拡張子・大きさ・更新日時・種類、重複で残す1つの決め方", T66_SelectCriteria);

            int ng = 0;
            foreach (TestCase t in cases)
            {
                Stopwatch sw = Stopwatch.StartNew();
                string status, detail = "";
                try
                {
                    t.Body();
                    status = "OK";
                    detail = Note;
                }
                catch (SkipException ex) { status = "SKIP"; detail = ex.Message; }
                catch (Exception ex)
                {
                    status = "NG";
                    detail = ex.GetType().Name + ": " + ex.Message;
                    Console.WriteLine(ex.ToString());
                    ng++;
                }
                Note = "";
                sw.Stop();
                Console.WriteLine("[{0}] {1} {2} ({3} ms) {4}", status, t.Id, t.Name, sw.ElapsedMilliseconds, detail);
                results.Add(new string[] { t.Id, t.Name, status, sw.ElapsedMilliseconds.ToString(), detail });
            }

            StringBuilder md = new StringBuilder();
            md.AppendLine("| No | テスト | 結果 | 時間(ms) | メモ |");
            md.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (string[] r in results)
                md.AppendLine("| " + r[0] + " | " + r[1] + " | " + r[2] + " | " + r[3] + " | " + r[4].Replace("|", "/") + " |");
            string outFile = args.Length > 0 ? args[0] : "test-results.md";
            File.WriteAllText(outFile, md.ToString(), new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("結果: " + (cases.Count - ng) + " / " + cases.Count + " 成功（NG " + ng + "）");
            return ng;
        }

        // ------------------------------------------------------------------
        private class TestCase { public string Id, Name; public Action Body; }
        private static readonly List<TestCase> cases = new List<TestCase>();
        public static string Note = "";
        private static void Add(string id, string name, Action body) { TestCase t = new TestCase(); t.Id = id; t.Name = name; t.Body = body; cases.Add(t); }

        private class SkipException : Exception { public SkipException(string m) : base(m) { } }

        private static void True(bool c, string what) { if (!c) throw new Exception("期待どおりではありません: " + what); }
        private static void Eq<X>(X expected, X actual, string what)
        {
            if (!object.Equals(expected, actual)) throw new Exception(what + "：期待 " + expected + "、実際 " + actual);
        }
        private static void Throws<E>(Action a, string what) where E : Exception
        {
            try { a(); }
            catch (E) { return; }
            catch (Exception ex) { throw new Exception(what + "：別の例外 " + ex.GetType().Name + " " + ex.Message); }
            throw new Exception(what + "：例外が起きませんでした");
        }

        /// <summary>テスト1件分の作業場所（コピー元・コピー先・ジョブ・状態）</summary>
        private class Box
        {
            public string Root, Src, Dst;
            public JobConfig Job;
            public AppSettings App;
            public JobState State;

            public Box(string name)
            {
                Root = Path.Combine(baseDir, name);
                Src = Path.Combine(Root, "src");
                Dst = Path.Combine(Root, "dst");
                Directory.CreateDirectory(Src);
                Directory.CreateDirectory(Dst);
                App = new AppSettings();
                App.InstanceId = "test-instance";
                App.FolderName = "PC1";
                App.TrashDays = 30;
                Job = new JobConfig();
                Job.Name = name;
                Job.Source = Src;
                Job.Dest = Dst;
                Job.RetryCount = 0;
                App.Jobs.Add(Job);
                Apply();
            }

            public void Apply()
            {
                AppCore.Settings = App;
                SafeFs.SetProtectedSources(new string[] { CopyEngine.ResolvedSource(Job, App) });
                Logger.Configure(App);
                State = AppCore.GetState(Job.Id);
            }

            public RunResult Run() { return Run(false); }

            public RunResult Run(bool full)
            {
                Apply();
                Dictionary<string, string> before = Snapshot(Src);
                CopyEngine e = new CopyEngine(Job, App, State, false, false, CancellationToken.None);
                e.FullVerify = full;
                RunResult r;
                try { r = e.Run(); }
                finally
                {
                    Dictionary<string, string> after = Snapshot(Src);
                    CompareSnapshots(before, after);
                }
                AppCore.SaveState(Job.Id);
                return r;
            }

            public RunResult Dry(bool forceMirror)
            {
                Apply();
                Dictionary<string, string> before = Snapshot(Src);
                RunResult r = new CopyEngine(Job, App, State, true, forceMirror, CancellationToken.None).Run();
                CompareSnapshots(before, Snapshot(Src));
                return r;
            }

            public string S(string rel) { return Path.Combine(Src, rel); }
            public string D(string rel) { return Path.Combine(Dst, rel); }

            public void W(string rel, string content) { WriteFile(S(rel), content, 120); }
            public void W(string rel, string content, int ageSec) { WriteFile(S(rel), content, ageSec); }
            public void WD(string rel, string content, int ageSec) { WriteFile(D(rel), content, ageSec); }
            public string RD(string rel) { return File.ReadAllText(PathUtil.Long(D(rel))); }
            public bool DE(string rel) { return File.Exists(PathUtil.Long(D(rel))); }
            public int TrashCount(string kind)
            {
                string t = Path.Combine(Path.Combine(Dst, AppPaths.DestMetaFolder), kind);
                return Directory.Exists(t) ? Directory.GetFiles(t, "*", SearchOption.AllDirectories).Length : 0;
            }
            public int DestFileCount()
            {
                int n = 0;
                foreach (string f in Directory.GetFiles(PathUtil.Long(Dst), "*", SearchOption.AllDirectories))
                    if (f.IndexOf("\\" + AppPaths.DestMetaFolder + "\\", StringComparison.OrdinalIgnoreCase) < 0) n++;
                return n;
            }
        }

        private static void WriteFile(string path, string content, int ageSec)
        {
            string l = PathUtil.Long(path);
            Directory.CreateDirectory(Path.GetDirectoryName(l));
            if (File.Exists(l)) File.SetAttributes(l, FileAttributes.Normal);
            File.WriteAllText(l, content, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(l, DateTime.UtcNow.AddSeconds(-ageSec));
        }

        private static void Age(string path, int ageSec)
        {
            File.SetLastWriteTimeUtc(PathUtil.Long(path), DateTime.UtcNow.AddSeconds(-ageSec));
        }

        /// <summary>コピー元の状態（パス・サイズ・更新日時・属性・中身のハッシュ）</summary>
        private static Dictionary<string, string> Snapshot(string root)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string l = PathUtil.Long(root);
            if (!Directory.Exists(l)) return d;
            foreach (string dir in Directory.GetDirectories(l, "*", SearchOption.AllDirectories))
                d["D:" + dir.Substring(l.Length)] = "dir";
            foreach (string f in Directory.GetFiles(l, "*", SearchOption.AllDirectories))
            {
                FileInfo fi = new FileInfo(f);
                string hash;
                try
                {
                    using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (MD5 md5 = MD5.Create()) hash = BitConverter.ToString(md5.ComputeHash(fs));
                }
                catch (IOException) { hash = "(locked)"; }
                d["F:" + f.Substring(l.Length)] = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + fi.Attributes + "|" + hash;
            }
            return d;
        }

        private static void CompareSnapshots(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            foreach (KeyValuePair<string, string> kv in a)
            {
                string v;
                if (!b.TryGetValue(kv.Key, out v)) throw new Exception("【元データ保護違反】コピー元から消えました: " + kv.Key);
                if (v != kv.Value && !kv.Value.EndsWith("(locked)") && !v.EndsWith("(locked)"))
                    throw new Exception("【元データ保護違反】コピー元が変わりました: " + kv.Key + " " + kv.Value + " → " + v);
            }
            foreach (string k in b.Keys)
                if (!a.ContainsKey(k)) throw new Exception("【元データ保護違反】コピー元に増えました: " + k);
        }

        private static string LogText()
        {
            string f = Path.Combine(AppPaths.LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            return File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8) : "";
        }

        // ================== テスト本体 ==================

        private static void T01_NewCopy()
        {
            Box b = new Box("T01");
            b.W("a.txt", "A");
            b.W("sub\\b.txt", "B");
            b.W("sub\\deep\\c.txt", "C");
            RunResult r = b.Run();
            Eq(3, r.Copied, "コピー件数");
            Eq("A", b.RD("a.txt"), "a.txt の中身");
            Eq("C", b.RD("sub\\deep\\c.txt"), "c.txt の中身");
            TimeSpan diff = File.GetLastWriteTimeUtc(b.D("a.txt")) - File.GetLastWriteTimeUtc(b.S("a.txt"));
            True(Math.Abs(diff.TotalSeconds) < 2, "更新日時がコピー元と同じ");
            Eq(3, b.State.Files.Count, "記録件数");
            True(LogText().Contains("\tCOPY\tsub\\deep\\c.txt"), "ログに COPY");
        }

        private static void T02_NoChange()
        {
            Box b = new Box("T02");
            b.W("a.txt", "A"); b.W("b.txt", "B");
            b.Run();
            DateTime c1 = File.GetLastWriteTimeUtc(b.D("a.txt"));
            RunResult r = b.Run();
            Eq(0, r.Copied + r.Overwritten, "2回目のコピー件数");
            Eq(c1, File.GetLastWriteTimeUtc(b.D("a.txt")), "コピー先が触られていない");
        }

        private static void T03_Update()
        {
            Box b = new Box("T03");
            b.W("a.txt", "A"); b.W("b.txt", "B");
            b.Run();
            b.W("a.txt", "A2-updated", 60);
            RunResult r = b.Run();
            Eq(1, r.Overwritten, "上書き件数");
            Eq(0, r.Copied, "新規件数");
            Eq("A2-updated", b.RD("a.txt"), "中身");
        }

        private static Box ConflictBox(string name, string policy)
        {
            Box b = new Box(name);
            b.Job.NewerDestPolicy = policy;
            b.W("a.txt", "A");
            b.Run();
            b.WD("a.txt", "DEST-EDITED", 10);   // コピー先で編集（新しい）
            b.W("a.txt", "SRC-NEW", 60);         // コピー元も変更（コピー先より古い）
            return b;
        }

        private static void T04_ConflictAsk()
        {
            Box b = ConflictBox("T04", NewerPolicy.Ask);
            RunResult r = b.Run();
            Eq(1, r.Conflicts, "確認待ち件数");
            Eq(0, r.Overwritten, "上書き件数");
            Eq("DEST-EDITED", b.RD("a.txt"), "コピー先は変わらない");
            Eq(1, b.State.Conflicts.Count, "状態の確認待ち");
            r = b.Run();
            Eq(1, r.Conflicts, "次の実行でも確認待ちのまま（重複しない）");
            Eq(1, b.State.Conflicts.Count, "確認待ちは1件のまま");
        }

        private static void T05_ConflictSkip()
        {
            Box b = ConflictBox("T05", NewerPolicy.Skip);
            RunResult r = b.Run();
            Eq(0, r.Conflicts, "確認待ち");
            Eq("DEST-EDITED", b.RD("a.txt"), "上書きしない");
            r = b.Run();
            Eq(0, r.Overwritten + r.Conflicts, "2回目も何もしない");
        }

        private static void T06_ConflictOverwrite()
        {
            Box b = ConflictBox("T06", NewerPolicy.Overwrite);
            RunResult r = b.Run();
            Eq(1, r.Overwritten, "上書き");
            Eq("SRC-NEW", b.RD("a.txt"), "中身");
        }

        private static void T07_ResolveConflict()
        {
            // 上書き（退避あり）
            Box b = ConflictBox("T07a", NewerPolicy.Ask);
            b.Run();
            ConflictItem c = b.State.Conflicts[0];
            Eq(null, AppCore.ResolveConflict(b.Job, c, AppCore.ActOverwrite, true), "上書きの結果");
            Eq("SRC-NEW", b.RD("a.txt"), "上書き後の中身");
            Eq(1, b.TrashCount("退避"), "退避されたファイル");
            Eq(0, b.State.Conflicts.Count, "確認待ちが消える");
            Eq(0, b.Run().Overwritten, "次回は何もしない");

            // 今回はスキップ
            Box b2 = ConflictBox("T07b", NewerPolicy.Ask);
            b2.Run();
            AppCore.ResolveConflict(b2.Job, b2.State.Conflicts[0], AppCore.ActSkipOnce, false);
            RunResult r = b2.Run();
            Eq(0, r.Conflicts, "スキップ後は聞かない");
            Eq("DEST-EDITED", b2.RD("a.txt"), "コピー先はそのまま");
            b2.W("a.txt", "SRC-NEWER", 5 + 0);
            Age(b2.S("a.txt"), 6);
            File.SetLastWriteTimeUtc(b2.D("a.txt"), DateTime.UtcNow.AddSeconds(-30));
            r = b2.Run();
            Eq(1, r.Overwritten, "コピー元がまた変わったら通常どおり上書き（コピー先が古くなったため）");

            // 今後も上書きしない
            Box b3 = ConflictBox("T07c", NewerPolicy.Ask);
            b3.Run();
            AppCore.ResolveConflict(b3.Job, b3.State.Conflicts[0], AppCore.ActSkipForever, false);
            b3.W("a.txt", "SRC-NEWER", 6);
            File.SetLastWriteTimeUtc(b3.D("a.txt"), DateTime.UtcNow.AddSeconds(-300));
            r = b3.Run();
            Eq(0, r.Overwritten + r.Conflicts, "今後も上書きしない");
            Eq("DEST-EDITED", b3.RD("a.txt"), "コピー先はそのまま");
        }

        private static void T08_RenameFile()
        {
            Box b = new Box("T08");
            b.W("x.txt", "content-x");
            b.W("other.txt", "o");
            b.Run();
            File.Move(b.S("x.txt"), b.S("y.txt"));
            RunResult r = b.Run();
            Eq(1, r.Renamed, "名前変更件数");
            Eq(0, r.Copied + r.Overwritten, "コピー件数");
            True(!b.DE("x.txt") && b.DE("y.txt"), "コピー先も名前が変わる");
            Eq("content-x", b.RD("y.txt"), "中身");
            True(LogText().Contains("\tRENAME\tx.txt → y.txt"), "ログに RENAME");
        }

        private static void T09_RenameFolder()
        {
            Box b = new Box("T09");
            b.W("f1\\a.txt", "a"); b.W("f1\\b.txt", "bb"); b.W("f1\\s\\c.txt", "ccc");
            b.W("keep.txt", "k");
            b.Run();
            Directory.Move(b.S("f1"), b.S("f2"));
            RunResult r = b.Run();
            Eq(3, r.Renamed, "名前変更件数");
            Eq(0, r.Copied, "コピー件数");
            True(!Directory.Exists(b.D("f1")), "旧フォルダがない");
            Eq("ccc", b.RD("f2\\s\\c.txt"), "新フォルダの中身");
            True(LogText().Contains("f1 → f2（フォルダ・3ファイル）"), "フォルダ1回の名前変更としてログ");
        }

        private static void T10_MoveFile()
        {
            Box b = new Box("T10");
            b.W("a\\m.txt", "move-me");
            Directory.CreateDirectory(b.S("b"));
            b.Run();
            File.Move(b.S("a\\m.txt"), b.S("b\\m.txt"));
            RunResult r = b.Run();
            Eq(1, r.Renamed, "移動件数");
            Eq(0, r.Copied, "コピー件数");
            True(!b.DE("a\\m.txt") && b.DE("b\\m.txt"), "コピー先でも移動");
        }

        private static void T11_RenameAndModify()
        {
            Box b = new Box("T11");
            b.W("x.txt", "old");
            b.Run();
            File.Move(b.S("x.txt"), b.S("y.txt"));
            b.W("y.txt", "new-content-longer", 60);
            RunResult r = b.Run();
            Eq(1, r.Renamed, "名前変更");
            Eq(1, r.Overwritten, "上書き");
            Eq(0, r.Copied, "新規コピー");
            Eq("new-content-longer", b.RD("y.txt"), "中身");
            True(!b.DE("x.txt"), "旧名がない");
        }

        private static void T12_FollowOff()
        {
            Box b = new Box("T12");
            b.Job.FollowRename = false;
            b.W("x.txt", "x");
            b.Run();
            File.Move(b.S("x.txt"), b.S("y.txt"));
            RunResult r = b.Run();
            Eq(0, r.Renamed, "名前変更しない");
            Eq(1, r.Copied, "新しい名前でコピー");
            True(b.DE("x.txt") && b.DE("y.txt"), "旧名も残る（追加・更新のみ）");
        }

        private static void T13_RenameFallback()
        {
            Box b = new Box("T13");
            b.W("x.bin", "fallback-content-0123456789");
            b.Run();
            // 別ファイルとして作り直す（ファイルIDが変わる）が、サイズ・更新日時・中身は同じ
            DateTime mt = File.GetLastWriteTimeUtc(b.S("x.bin"));
            File.Copy(b.S("x.bin"), b.S("y.bin"));
            File.SetLastWriteTimeUtc(b.S("y.bin"), mt);
            File.Delete(b.S("x.bin"));
            RunResult r = b.Run();
            Eq(1, r.Renamed, "サイズ・日時・内容で名前変更と判定");
            Eq(0, r.Copied, "コピーしない");
            True(!b.DE("x.bin") && b.DE("y.bin"), "コピー先も名前変更");
        }

        private static void T14_Exclude()
        {
            Box b = new Box("T14");
            ExcludeRules x = ExcludeRules.CreateDefault();
            x.Presets.Add("video");
            x.Extensions.Add(".log");
            x.NamePatterns.Add("*_old.*");
            x.MaxSizeBytes = 1024 * 1024;
            x.ExcludeZeroByte = true;
            x.OlderThan = "2020-01-01";
            x.Folders.Add("node_modules");
            x.Folders.Add("work\\tmp");
            x.Includes.Add("video\\keep.mp4");
            b.Job.Exclude = x;

            b.W("keep.txt", "ok");
            b.W("a.tmp", "t"); b.W("b.log", "l"); b.W("~$doc.docx", "o"); b.W("report_old.xlsx", "o");
            b.W("Thumbs.db", "t");
            WriteFile(b.S("big.bin"), new string('x', 2 * 1024 * 1024), 120);
            WriteFile(b.S("zero.txt"), "", 120);
            b.W("node_modules\\lib\\x.js", "x");
            b.W("work\\tmp\\y.txt", "y");
            b.W("work\\keep2.txt", "k2");
            b.W("hidden.txt", "h");
            File.SetAttributes(b.S("hidden.txt"), FileAttributes.Hidden);
            b.W("old.txt", "o");
            File.SetLastWriteTimeUtc(b.S("old.txt"), new DateTime(2019, 5, 1, 0, 0, 0, DateTimeKind.Utc));
            b.W("video\\keep.mp4", "v"); b.W("video\\other.mp4", "v");

            RunResult r = b.Run();
            Eq(3, r.Copied, "コピー件数（keep.txt・work\\keep2.txt・video\\keep.mp4）");
            True(b.DE("keep.txt") && b.DE("work\\keep2.txt") && b.DE("video\\keep.mp4"), "対象はコピー");
            foreach (string n in new string[] { "a.tmp", "b.log", "~$doc.docx", "report_old.xlsx", "Thumbs.db", "big.bin", "zero.txt",
                "node_modules\\lib\\x.js", "work\\tmp\\y.txt", "hidden.txt", "old.txt", "video\\other.mp4" })
                True(!b.DE(n), "除外: " + n);
            Note = "除外 " + r.Excluded + "件";
        }

        private static void T15_NoMirrorKeep()
        {
            Box b = new Box("T15");
            b.W("a.txt", "a"); b.W("b.txt", "b");
            b.Run();
            File.Delete(b.S("b.txt"));
            RunResult r = b.Run();
            Eq(0, r.Deleted, "削除件数");
            True(b.DE("b.txt"), "コピー先に残る");
        }

        private static void T16_MirrorDelete()
        {
            Box b = new Box("T16");
            b.Job.Mirror = true;
            b.W("a.txt", "a"); b.W("b.txt", "b"); b.W("sub\\c.txt", "c");
            for (int i = 0; i < 30; i++) b.W("pad\\p" + i + ".txt", "p" + i);  // 割合の確認に当たらないよう件数を増やす
            b.Run();
            File.Delete(b.S("b.txt"));
            Directory.Delete(b.S("sub"), true);
            RunResult r = b.Run();
            Eq(2, r.Deleted, "削除件数");
            True(!b.DE("b.txt") && !b.DE("sub\\c.txt"), "コピー先から消える");
            True(!Directory.Exists(b.D("sub")), "空になったフォルダも片付く");
            Eq(2, b.TrashCount("削除済み"), "削除済みに移動（元に戻せる）");
            True(b.DE("a.txt"), "ほかは残る");
        }

        private static void T17_MirrorEmptySource()
        {
            Box b = new Box("T17");
            b.Job.Mirror = true;
            b.W("a.txt", "a");
            b.Run();
            File.Delete(b.S("a.txt"));
            RunResult r = b.Run();
            True(r.Failed, "エラー扱い");
            Eq(0, r.Deleted, "削除しない");
            True(b.DE("a.txt"), "コピー先に残る");
        }

        private static void T18_MirrorBulk()
        {
            Box b = new Box("T18");
            b.Job.Mirror = true;
            for (int i = 0; i < 20; i++) b.W("f" + i + ".txt", "x" + i);
            b.Run();
            for (int i = 0; i < 12; i++) File.Delete(b.S("f" + i + ".txt"));
            RunResult r = b.Run();
            Eq(12, r.PendingDeletes, "保留件数");
            Eq(0, r.Deleted, "まだ削除しない");
            Eq(20, b.DestFileCount(), "コピー先はそのまま");
            Eq(null, AppCore.ResolvePendingDeletes(b.Job, true), "承認");
            Eq(8, b.DestFileCount(), "承認後に削除");
            Eq(12, b.TrashCount("削除済み"), "削除済みへ");

            Box b2 = new Box("T18b");
            b2.Job.Mirror = true;
            for (int i = 0; i < 20; i++) b2.W("f" + i + ".txt", "x" + i);
            b2.Run();
            for (int i = 0; i < 12; i++) File.Delete(b2.S("f" + i + ".txt"));
            b2.Run();
            Eq(null, AppCore.ResolvePendingDeletes(b2.Job, false), "取りやめ");
            Eq(20, b2.DestFileCount(), "取りやめたら残る");
            r = b2.Run();
            Eq(0, r.PendingDeletes + r.Deleted, "取りやめた分は再度聞かない");
        }

        private static void T19_MirrorSafety()
        {
            Box b = new Box("T19");
            b.Job.Mirror = true;
            for (int i = 0; i < 30; i++) b.W("pad\\p" + i + ".txt", "p");
            b.W("m.txt", "m");
            b.Run();
            b.WD("manual.txt", "人が置いた", 100);
            b.WD("m.txt", "コピー先で更新", 10);
            File.Delete(b.S("m.txt"));
            RunResult r = b.Run();
            Eq(0, r.Deleted, "削除件数");
            True(b.DE("manual.txt"), "人が置いたファイルは残る");
            True(b.DE("m.txt"), "コピー先で更新されたファイルは残る");
        }

        private static void T20_MirrorExcluded()
        {
            Box b = new Box("T20");
            b.Job.Mirror = true;
            for (int i = 0; i < 30; i++) b.W("pad\\p" + i + ".txt", "p");
            b.W("keep.log", "log");
            b.Run();
            b.Job.Exclude.Extensions.Add(".log");
            RunResult r = b.Run();
            Eq(0, r.Deleted, "削除件数");
            True(b.DE("keep.log"), "除外になったファイルは消さない");
        }

        private static void T21_SourceProtection()
        {
            Box b = new Box("T21");
            b.W("a.txt", "a");
            b.Apply();
            Throws<SourceProtectedException>(delegate { SafeFs.WriteText(b.S("x.txt"), "x"); }, "コピー元への書き込み");
            Throws<SourceProtectedException>(delegate { SafeFs.DeleteFile(b.S("a.txt")); }, "コピー元の削除");
            Throws<SourceProtectedException>(delegate { SafeFs.Move(b.S("a.txt"), b.D("a.txt")); }, "コピー元からの移動");
            Throws<SourceProtectedException>(delegate { SafeFs.Move(b.D("z.txt"), b.S("z.txt")); }, "コピー元への移動");
            Throws<SourceProtectedException>(delegate { SafeFs.CreateDirectory(b.S("newdir")); }, "コピー元にフォルダ作成");
            Throws<SourceProtectedException>(delegate { SafeFs.DeleteDirectoryTree(b.Src); }, "コピー元フォルダの削除");
            Throws<SourceProtectedException>(delegate { SafeFs.MoveToTrash(b.Src, "a.txt", "削除済み", "x"); }, "コピー元をごみ箱へ");
            True(File.Exists(b.S("a.txt")), "コピー元のファイルは残っている");
            True(!File.Exists(b.S("x.txt")) && !Directory.Exists(b.S("newdir")), "コピー元に何も増えていない");

            JobConfig j = b.Job.Clone();
            j.Dest = Path.Combine(b.Src, "backup");
            True(CopyEngine.Validate(j, b.App, b.App.Jobs) != null, "コピー先がコピー元の中 → 拒否");
            j.Dest = b.Src;
            True(CopyEngine.Validate(j, b.App, b.App.Jobs) != null, "同じフォルダ → 拒否");
            j.Source = Path.Combine(b.Dst, "inner"); j.Dest = b.Dst;
            True(CopyEngine.Validate(j, b.App, b.App.Jobs) != null, "コピー元がコピー先の中 → 拒否");
            JobConfig other = new JobConfig();
            other.Source = Path.Combine(b.Root, "x"); other.Dest = b.Src;
            True(CopyEngine.Validate(other, b.App, b.App.Jobs) != null, "別ジョブのコピー元をコピー先にする → 拒否");
        }

        private static void T22_BackupBeforeOverwrite()
        {
            Box b = new Box("T22");
            b.Job.BackupBeforeOverwrite = true;
            b.W("a.txt", "v1");
            b.Run();
            b.W("a.txt", "v2", 60);
            RunResult r = b.Run();
            Eq(1, r.Overwritten, "上書き");
            Eq("v2", b.RD("a.txt"), "新しい中身");
            Eq(1, b.TrashCount("退避"), "古いものを退避");
        }

        private static void T23_DryRun()
        {
            Box b = new Box("T23");
            b.W("a.txt", "a"); b.W("b.txt", "b"); b.W("s\\c.txt", "c");
            RunResult r = b.Dry(false);
            Eq(3, r.PlannedCopyCount, "コピー予定件数");
            Eq(0, b.DestFileCount(), "コピー先は空のまま");
            Eq(0, b.State.Files.Count, "記録も変わらない");
            True(!Directory.Exists(Path.Combine(b.Dst, AppPaths.DestMetaFolder)), "管理フォルダも作らない");
        }

        private static void T24_RecentDeferred()
        {
            Box b = new Box("T24");
            b.W("new.txt", "n", 0);
            RunResult r = b.Run();
            Eq(1, r.Deferred, "次回に回す");
            True(!b.DE("new.txt"), "まだコピーしない");
            Age(b.S("new.txt"), 60);
            r = b.Run();
            Eq(1, r.Copied, "次の実行でコピー");
        }

        private static void T25_LockedFile()
        {
            Box b = new Box("T25");
            b.W("lock.txt", "locked");
            RunResult r;
            using (FileStream fs = new FileStream(b.S("lock.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                r = b.Run();
            }
            Eq(1, r.Locked, "使用中");
            True(!b.DE("lock.txt"), "コピーされていない");
            True(!File.Exists(b.D("lock.txt.ckpart")), "一時ファイルが残らない");
            r = b.Run();
            Eq(1, r.Copied, "次回コピー");
        }

        private static void T26_LongPath()
        {
            Box b = new Box("T26");
            string rel = "";
            while (rel.Length < 300) rel = Path.Combine(rel, "very_long_folder_name_" + rel.Length);
            rel = Path.Combine(rel, "file.txt");
            b.W(rel, "long");
            RunResult r = b.Run();
            Eq(1, r.Copied, "コピー件数");
            Eq("long", b.RD(rel), "中身");
            Note = "パスの長さ " + b.D(rel).Length + "文字";
        }

        private static void T27_OwnerConflict()
        {
            Box b = new Box("T27");
            b.W("a.txt", "a");
            OwnerInfo o = new OwnerInfo();
            o.InstanceId = "someone-else"; o.ComputerName = "OTHER-PC"; o.FolderName = "PC1";
            WriteFile(Path.Combine(Path.Combine(b.Dst, AppPaths.DestMetaFolder), "owner.json"), Json.Serialize(o), 100);
            Throws<FolderConflictException>(delegate { b.Run(); }, "他のPCが使用中");
            True(!b.DE("a.txt"), "コピーしない");
            Eq("OTHER-PC", CopyEngine.FindOtherOwner(b.Dst, b.App.InstanceId), "使用中のPC名");
            o.ComputerName = Environment.MachineName;
            WriteFile(Path.Combine(Path.Combine(b.Dst, AppPaths.DestMetaFolder), "owner.json"), Json.Serialize(o), 100);
            Eq(1, b.Run().Copied, "同じPCで入れ直した場合は引き継ぐ");
        }

        private static void T28_FolderVariable()
        {
            Box b = new Box("T28");
            b.App.FolderName = "山田_経理";
            b.Job.Dest = Path.Combine(b.Root, "nas") + "\\{フォルダ名}\\経理";
            b.W("a.txt", "a");
            RunResult r = b.Run();
            Eq(1, r.Copied, "コピー");
            True(File.Exists(Path.Combine(b.Root, "nas\\山田_経理\\経理\\a.txt")), "フォルダ名が入った場所にコピー");
            b.App.FolderName = "";
            True(CopyEngine.Validate(b.Job, b.App, b.App.Jobs) != null, "フォルダ名が未設定なら止める");
        }

        private static void T29_FullVerify()
        {
            Box b = new Box("T29");
            b.W("a.txt", "a"); b.W("b.txt", "b");
            b.Run();
            File.Delete(b.D("a.txt"));
            Eq(0, b.Run().Copied, "通常の実行ではコピー先を見に行かない（速さ優先）");
            Eq(1, b.Run(true).Copied, "全体を再確認で戻る");
            True(b.DE("a.txt"), "戻った");
        }

        private static void T30_DupRename()
        {
            Box b = new Box("T30");
            b.Job.FollowRename = false;
            b.W("x.txt", "duplicate-content-123");
            b.W("other.txt", "other");
            b.Run();
            File.Move(b.S("x.txt"), b.S("y.txt"));
            b.Run();
            DupResult d = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            DupGroup g = d.Groups.Find(delegate(DupGroup x) { return x.Kind == "rename"; });
            True(g != null, "名前変更の取りこぼしを検出");
            DupEntry old = g.Entries.Find(delegate(DupEntry e) { return e.Rel == "x.txt"; });
            DupEntry cur = g.Entries.Find(delegate(DupEntry e) { return e.Rel == "y.txt"; });
            True(old != null && old.Recommended && old.CanDelete, "古い名前を削除推奨");
            True(cur != null && !cur.CanDelete, "今の名前は削除できない");
            int n = DupChecker.DeleteSelected(b.Job, b.App, b.State, new List<DupEntry>(new DupEntry[] { old }));
            Eq(1, n, "削除件数");
            True(!b.DE("x.txt") && b.DE("y.txt"), "古い名前だけ消える");
            List<TrashStore.Batch> batches = TrashStore.List(b.Dst);
            Eq(1, batches.Count, "削除済みの束");
            Eq(1, TrashStore.Restore(b.Job.Name, b.Dst, batches[0]), "元に戻す件数");
            True(b.DE("x.txt"), "元に戻った");
        }

        private static void T31_DupFolder()
        {
            Box b = new Box("T31");
            b.Job.FollowRename = false;
            b.W("f1\\a.txt", "aaa-1"); b.W("f1\\b.txt", "bbb-22"); b.W("f1\\c.txt", "ccc-333");
            b.Run();
            Directory.Move(b.S("f1"), b.S("f2"));
            b.Run();
            DupResult d = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            DupGroup g = d.Groups.Find(delegate(DupGroup x) { return x.Kind == "folder"; });
            True(g != null, "フォルダ単位で検出");
            True(g.Entries[0].Rel == "f1" && g.Entries[0].Recommended && g.Entries[0].IsFolder, "旧フォルダを削除推奨");
            Eq(3, g.Entries[0].FileCount, "ファイル数");
            DupChecker.DeleteSelected(b.Job, b.App, b.State, new List<DupEntry>(new DupEntry[] { g.Entries[0] }));
            True(!Directory.Exists(b.D("f1")) && Directory.Exists(b.D("f2")), "旧フォルダだけ消える");
        }

        private static void T32_DupSame()
        {
            Box b = new Box("T32");
            b.W("p\\same.txt", "same-content");
            b.W("q\\same2.txt", "same-content");
            b.Run();
            DupResult d = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            DupGroup g = d.Groups.Find(delegate(DupGroup x) { return x.Kind == "same"; });
            True(g != null && g.Entries.Count == 2, "中身が同じものを検出");
            True(!g.Entries[0].CanDelete && !g.Entries[1].CanDelete, "情報のみ（削除不可）");
            Eq(0, d.DeleteCandidateCount(), "削除候補なし");
        }

        private static void T33_Distribution()
        {
            Box b = new Box("T33");
            b.Job.Scope = JobScope.Dist;
            b.Job.Dest = "\\\\nas01\\backup\\{フォルダ名}\\経理";
            JobConfig local = new JobConfig(); local.Name = "このPCだけ"; local.Scope = JobScope.Local; local.Source = "C:\\x"; local.Dest = "D:\\y";
            b.App.Jobs.Add(local);
            b.App.LogRoot = "\\\\nas01\\自動コピー\\logs";
            List<DistCredential> creds = new List<DistCredential>();
            DistCredential c = new DistCredential(); c.Server = "actest-server-xyz"; c.User = "nas01\\yamada"; c.Password = "p@ss-日本語";
            creds.Add(c);
            DistPayload p = Distribution.BuildPayload(b.App, "経理部", 3, creds);
            Eq(1, p.Jobs.Count, "配布するジョブはこのPCだけのものを含まない");
            string file = Path.Combine(b.Root, "AutoCopy.setup");
            Distribution.Write(file, p, "secret-pass");
            string raw = File.ReadAllText(file);
            True(!raw.Contains("p@ss") && !raw.Contains("yamada") && !raw.Contains("nas01"), "ファイルに平文が含まれない");

            DistEnvelope env = Distribution.ReadEnvelope(file);
            Eq(3, env.Version, "版");
            Throws<Distribution.WrongPasswordException>(delegate { Distribution.Decrypt(env, "wrong"); }, "パスワード誤り");
            DistPayload back = Distribution.Decrypt(env, "secret-pass");
            Eq("p@ss-日本語", back.Credentials[0].Password, "パスワードの復号");
            Eq(b.Job.Dest, back.Jobs[0].Dest, "ジョブの復号");

            DistEnvelope bad = Distribution.ReadEnvelope(file);
            char[] data = bad.Data.ToCharArray();
            data[10] = data[10] == 'A' ? 'B' : 'A';
            bad.Data = new string(data);
            Throws<Distribution.WrongPasswordException>(delegate { Distribution.Decrypt(bad, "secret-pass"); }, "改ざん検知");

            AppSettings target = new AppSettings();
            target.FolderName = "鈴木_総務";
            target.InstanceId = "pc2";
            JobConfig mine = new JobConfig(); mine.Name = "自分用"; mine.Scope = JobScope.Local; mine.Source = "C:\\a"; mine.Dest = "D:\\b";
            target.Jobs.Add(mine);
            try
            {
                int added = Distribution.Apply(target, back);
                Eq(1, added, "新しく加わったジョブの数");
                True(!target.Jobs.Find(delegate(JobConfig j) { return j.Scope == JobScope.Dist; }).Enabled, "新しく加わった配布のジョブは無効で取り込む");
                True(target.Jobs.Find(delegate(JobConfig j) { return j.Name == "自分用"; }).Enabled, "このPCのジョブの有効/無効は変えない");
                // 有効にしてから次の版を取り込んでも、このPCで決めた有効/無効を引き継ぐ
                target.Jobs.Find(delegate(JobConfig j) { return j.Scope == JobScope.Dist; }).Enabled = true;
                DistPayload again = Distribution.Decrypt(Distribution.ReadEnvelope(file), "secret-pass");
                Eq(0, Distribution.Apply(target, again), "2回目は新しいジョブなし");
                True(target.Jobs.Find(delegate(JobConfig j) { return j.Scope == JobScope.Dist; }).Enabled, "有効にしたジョブは有効のまま");
                Eq("鈴木_総務", target.FolderName, "フォルダ名は残る");
                Eq(2, target.Jobs.Count, "配布ジョブ＋自分のジョブ");
                True(target.Jobs.Exists(delegate(JobConfig j) { return j.Name == "自分用"; }), "このPCだけのジョブは残る");
                Eq(3, target.DistVersion, "版を記録");
                Eq(b.App.LogRoot, target.LogRoot, "ログ保管先");
                string u, pw;
                True(CredentialStore.TryLoad("actest-server-xyz", out u, out pw), "資格情報マネージャーに保存");
                Eq("nas01\\yamada", u, "ユーザーID");
                Eq("p@ss-日本語", pw, "パスワード");
            }
            finally { CredentialStore.Delete("actest-server-xyz"); }
            string u2, p2;
            True(!CredentialStore.TryLoad("actest-server-xyz", out u2, out p2), "削除できる");
        }

        private static void T34_StateStore()
        {
            string id = "state-test-" + Guid.NewGuid().ToString("N");
            JobState s = JobState.Load(id);
            FileRecord r = new FileRecord();
            r.Size = 123; r.Mtime = 456; r.Volume = 7; r.FileIndex = 890; r.DestMtime = 457; r.CopiedByApp = true;
            r.Hash = new byte[16]; r.Hash[3] = 9;
            s.Files["日本語\\ファイル.txt"] = r;
            s.SkipForever.Add("skip.txt");
            ConflictItem c = new ConflictItem(); c.Rel = "c.txt"; c.SrcSize = 1; c.DstSize = 2; c.Reason = "newer"; c.Detected = "now";
            s.Conflicts.Add(c);
            PendingDelete p = new PendingDelete(); p.Rel = "p.txt"; p.Size = 5;
            s.PendingDeletes.Add(p);
            s.LastResult = "ok"; s.LastMessage = "メッセージ";
            s.Save();
            JobState t = JobState.Load(id);
            FileRecord q = t.Files["日本語\\ファイル.txt"];
            True(q.Size == 123 && q.Mtime == 456 && q.Volume == 7 && q.FileIndex == 890 && q.DestMtime == 457 && q.CopiedByApp && q.Hash[3] == 9, "ファイルの記録");
            True(t.SkipForever.Contains("SKIP.TXT"), "今後も上書きしない（大文字小文字を区別しない）");
            Eq("newer", t.Conflicts[0].Reason, "確認待ち");
            Eq(5L, t.PendingDeletes[0].Size, "保留削除");
            Eq("メッセージ", t.LastMessage, "結果");
            t.Delete();
        }

        private static void T35_Logger()
        {
            Box b = new Box("T35");
            b.App.LogRoot = Path.Combine(b.Root, "netlogs");
            b.App.FolderName = "PC-LOG";
            b.Apply();
            Logger.Write(b.Job.Name, "INFO", "ログ保管先テスト", -1, -1);
            True(Logger.Flush(), "送信成功");
            string f = Path.Combine(Path.Combine(b.App.LogRoot, "PC-LOG"), DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            True(File.Exists(f) && File.ReadAllText(f, Encoding.UTF8).Contains("ログ保管先テスト"), "保管先\\フォルダ名\\日付.log に記録");
            True(Directory.GetFiles(AppPaths.OutboxDir).Length == 0, "送信済みは outbox に残らない");
            AppCore.PublishStatus();
            True(File.Exists(Path.Combine(Path.Combine(b.App.LogRoot, "PC-LOG"), "status.json")), "状態ファイル");
            string note;
            List<LogLine> lines = Logger.Read(DateTime.Today, DateTime.Today, true, out note);
            True(lines.Exists(delegate(LogLine l) { return l.Detail == "ログ保管先テスト" && l.Folder == "PC-LOG"; }), "全PCの読み込み");
            Eq(1, Logger.ReadAllStatus().Count, "全PCの状態");

            // 保管先に届かない間は溜めておく
            b.App.LogRoot = "Q:\\no-such-drive\\logs";
            b.Apply();
            Logger.Write(b.Job.Name, "INFO", "溜めるテスト", -1, -1);
            True(!Logger.Flush(), "送れない");
            True(Directory.GetFiles(AppPaths.OutboxDir).Length > 0, "outbox に残る");
            b.App.LogRoot = Path.Combine(b.Root, "netlogs");
            b.Apply();
            True(Logger.Flush(), "つながったら送る");
            True(File.ReadAllText(f, Encoding.UTF8).Contains("溜めるテスト"), "あとから届く");
        }

        private static void T36_TrashPurge()
        {
            Box b = new Box("T36");
            b.W("a.txt", "a");
            WriteFile(Path.Combine(b.Dst, "_AutoCopy\\削除済み\\20200101-000000\\old.txt"), "old", 100);
            WriteFile(Path.Combine(b.Dst, "_AutoCopy\\削除済み\\" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "\\new.txt"), "new", 100);
            b.Run();
            True(!Directory.Exists(Path.Combine(b.Dst, "_AutoCopy\\削除済み\\20200101-000000")), "保存期間切れを削除");
            Eq(1, b.TrashCount("削除済み"), "期間内のものは残る");
        }

        private static void T37_Retry()
        {
            Box b = new Box("T37");
            b.W("a.txt", "a");
            b.Job.Dest = "Q:\\no-such-drive\\backup";
            b.Job.RetryCount = 2;
            b.Job.RetryIntervalSeconds = 1;
            Stopwatch sw = Stopwatch.StartNew();
            AccessException ex = null;
            try { b.Run(); } catch (AccessException e) { ex = e; }
            sw.Stop();
            True(ex != null, "接続できないエラー");
            True(ex.Message.Contains("2回再試行"), "メッセージに再試行回数: " + ex.Message);
            True(sw.Elapsed.TotalSeconds >= 1.9, "間隔どおり待つ（" + sw.Elapsed.TotalSeconds.ToString("0.0") + "秒）");
            True(LogText().Contains("コピー先への接続を再試行（2 / 2回目）"), "ログに RETRY");
            Note = ex.Message;
        }

        private static void T38_PathUtil()
        {
            Eq("\\\\nas\\b\\山田\\x", PathUtil.Expand("\\\\nas\\b\\{フォルダ名}\\x", "山田"), "{フォルダ名}");
            True(PathUtil.Expand("C:\\%USERNAME%\\x", "a").Contains(Environment.UserName), "%USERNAME%");
            True(PathUtil.IsSameOrInside("C:\\a\\b", "C:\\a"), "中にある");
            True(PathUtil.IsSameOrInside("c:\\A\\", "C:\\a"), "同じ（大文字小文字・末尾\\）");
            True(!PathUtil.IsSameOrInside("C:\\ab", "C:\\a"), "名前が前方一致しても別フォルダ");
            Eq("\\\\server\\share\\x", PathUtil.Plain(PathUtil.Long("\\\\server\\share\\x")), "UNC の長いパス表記");
            Eq("\\\\server\\share", PathUtil.UncShareRoot("\\\\server\\share\\a\\b"), "共有のルート");
            True(PathUtil.IsValidFolderName("山田_経理") && !PathUtil.IsValidFolderName("a/b") && !PathUtil.IsValidFolderName(""), "フォルダ名の検査");
            ExcludeMatcher m = new ExcludeMatcher(ExcludeRules.CreateDefault());
            True(m.ExcludeFile("x\\~$book.xlsx", "~$book.xlsx", 10, DateTime.UtcNow, FileAttributes.Normal), "Office 一時ファイル");
            True(!m.ExcludeFile("x\\book.xlsx", "book.xlsx", 10, DateTime.UtcNow, FileAttributes.Normal), "通常ファイル");
        }

        private static void T39_StartupShortcut()
        {
            if (Environment.GetEnvironmentVariable("CI") != "true") throw new SkipException("CI でのみ実行（実PCのレジストリ・デスクトップを変更しないため）");
            StartupRegistration.Set(true, "C:\\Test\\AutoCopy.exe");
            True(StartupRegistration.IsSet(), "スタートアップに登録");
            StartupRegistration.Set(false, "C:\\Test\\AutoCopy.exe");
            True(!StartupRegistration.IsSet(), "登録解除");
            string lnk = Shortcuts.CreateOnDesktop("自動コピー（テスト）", "/run \"経理\"", "C:\\Test\\AutoCopy.exe");
            True(File.Exists(lnk), "ショートカット作成: " + lnk);
            File.Delete(lnk);
        }

        private static void T40_NetworkShare()
        {
            string share = Environment.GetEnvironmentVariable("AC_SHARE");      // \\127.0.0.1\acshare
            string local = Environment.GetEnvironmentVariable("AC_SHARE_LOCAL"); // C:\acshare
            string user = Environment.GetEnvironmentVariable("AC_USER");
            string pass = Environment.GetEnvironmentVariable("AC_PASS");
            if (string.IsNullOrEmpty(share)) throw new SkipException("共有フォルダが用意されていません（CI でのみ実行）");
            string server = PathUtil.UncServer(share);
            string err = NetAccess.Test(share, user, "wrong-password");
            True(err != null && err.Contains("ID"), "パスワード誤りを検出: " + err);
            Eq(null, NetAccess.Test(share, user, pass), "正しいID・パスワードで接続");
            CredentialStore.Save(server, user, pass);
            try
            {
                Box b = new Box("T40");
                b.App.FolderName = "PC-NET";
                b.Job.Dest = share + "\\{フォルダ名}\\data";
                b.Job.RetryCount = 1;
                b.Job.RetryIntervalSeconds = 1;
                b.W("a.txt", "network");
                b.W("s\\b.txt", "network-b");
                RunResult r = b.Run();
                Eq(2, r.Copied, "ネットワークへコピー");
                True(File.Exists(Path.Combine(local, "PC-NET\\data\\s\\b.txt")), "共有フォルダの中に作られる");
                File.Move(b.S("a.txt"), b.S("a2.txt"));
                Eq(1, b.Run().Renamed, "ネットワーク上でも名前変更");
                Note = "共有 " + share;
            }
            finally { CredentialStore.Delete(server); }
        }

        private static void T41_AppCoreRun()
        {
            Box b = new Box("T41");
            b.W("a.txt", "a");
            b.W("b.txt", "b");
            b.Job.NewerDestPolicy = NewerPolicy.Ask;
            b.Apply();
            True(AppCore.RunJob(b.Job.Id, true, false), "実行開始");
            True(!AppCore.RunJob(b.Job.Id, true, false), "実行中は二重に始めない");
            JobRuntime rt = AppCore.GetRuntime(b.Job.Id);
            for (int i = 0; i < 300 && rt.Running; i++) Thread.Sleep(100);
            True(!rt.Running, "終わる");
            Eq("ok", rt.LastResult, "結果: " + rt.LastMessage);
            True(b.DE("a.txt") && b.DE("b.txt"), "コピーされた");
            True(rt.NextRun > DateTime.Now.AddMinutes(25), "次回は間隔（30分）後");

            AppCore.Pause(null, 30);
            True(AppCore.IsPaused(b.Job.Id), "一時停止");
            AppCore.Resume(null);
            True(!AppCore.IsPaused(b.Job.Id), "再開");

            b.WD("a.txt", "DEST", 10);
            b.W("a.txt", "SRC2", 60);
            AppCore.RunJob(b.Job.Id, true, false);
            for (int i = 0; i < 300 && rt.Running; i++) Thread.Sleep(100);
            Eq(1, AppCore.TotalConflicts(), "確認待ちの合計");
            True(AppCore.RecentNotices().Exists(delegate(Notice n) { return n.Action == "conflicts"; }), "上書き確認の通知");
        }

        private static void T43_CopyStats()
        {
            // 2台分のログをログ保管先に用意し、全PCを読み込んで集計する
            Box b = new Box("T43");
            b.App.LogRoot = Path.Combine(b.Root, "netlogs");
            b.App.FolderName = "山田_経理";
            b.Apply();
            string day = DateTime.Now.ToString("yyyy-MM-dd");
            string t = DateTime.Now.ToString("yyyy-MM-dd") + " 10:00:00";
            string[] pcA = {
                t + "\tPC-A\tyamada\t山田_経理\t経理\tCOPY\ta.xlsx\t1000\t0.1",
                t + "\tPC-A\tyamada\t山田_経理\t経理\tOVERWRITE\tb.xlsx\t2000\t0.1",
                t + "\tPC-A\tyamada\t山田_経理\t図面\tCOPY\tc.dwg\t5000\t0.1",
                t + "\tPC-A\tyamada\t山田_経理\t経理\tRENAME\tx → y\t3000\t",
                t + "\tPC-A\tyamada\t山田_経理\t経理\tSUMMARY\t走査\t8000\t1.0",
                t + "\tPC-A\tyamada\t山田_経理\t経理\tERROR\t使用中\t10\t" };
            string[] pcB = {
                t + "\tPC-B\tsuzuki\t鈴木_総務\t経理\tCOPY\td.docx\t700\t0.1",
                t + "\tPC-B\tsuzuki\t鈴木_総務\t経理\tDELETE\te.docx\t300\t" };
            WriteFile(Path.Combine(b.App.LogRoot, "山田_経理\\" + day + ".log"), string.Join("\r\n", pcA) + "\r\n", 10);
            WriteFile(Path.Combine(b.App.LogRoot, "鈴木_総務\\" + day + ".log"), string.Join("\r\n", pcB) + "\r\n", 10);
            string note;
            List<LogLine> lines = Logger.Read(DateTime.Today, DateTime.Today, true, out note);
            lines = lines.FindAll(delegate(LogLine l) { return l.Pc == "PC-A" || l.Pc == "PC-B"; });

            List<CopyStat> s = LogStats.Aggregate(lines, false);
            Eq(3, s.Count, "PC 2台＋合計");
            CopyStat a = s.Find(delegate(CopyStat x) { return x.Pc == "PC-A"; });
            CopyStat bb = s.Find(delegate(CopyStat x) { return x.Pc == "PC-B"; });
            CopyStat total = s[s.Count - 1];
            Eq(8000L, a.TotalBytes, "PC-A のコピー容量（新規＋上書き。名前変更・集計行・エラーは含まない）");
            Eq(6000L, a.CopiedBytes, "PC-A の新規");
            Eq(2000L, a.OverwrittenBytes, "PC-A の上書き");
            Eq(3, a.TotalFiles, "PC-A のファイル数");
            Eq(1, a.Renamed, "PC-A の名前変更");
            Eq(1, a.Errors, "PC-A のエラー");
            Eq(1, a.Runs, "PC-A の実行回数");
            Eq("山田_経理", a.Folder, "フォルダ名");
            Eq(700L, bb.TotalBytes, "PC-B のコピー容量");
            Eq(1, bb.Deleted, "PC-B の削除");
            Eq(8700L, total.TotalBytes, "合計");
            True(s[0].Pc == "PC-A", "容量の多い順");

            List<CopyStat> j = LogStats.Aggregate(lines, true);
            CopyStat aKeiri = j.Find(delegate(CopyStat x) { return x.Pc == "PC-A" && x.Job == "経理"; });
            CopyStat aZumen = j.Find(delegate(CopyStat x) { return x.Pc == "PC-A" && x.Job == "図面"; });
            Eq(3000L, aKeiri.TotalBytes, "PC-A・経理");
            Eq(5000L, aZumen.TotalBytes, "PC-A・図面");
            Eq(8700L, j[j.Count - 1].TotalBytes, "ジョブ別でも合計は同じ");

            // 日ごとの集計（グラフ用）
            List<LogLine> two = new List<LogLine>(lines);
            LogLine y = LogLine.Parse(DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") + " 09:00:00\tPC-A\tyamada\t山田_経理\t経理\tCOPY\tz.xlsx\t400\t0.1");
            two.Add(y);
            SortedDictionary<DateTime, Dictionary<string, long>> daily = LogStats.Daily(two);
            Eq(2, daily.Count, "日数");
            Eq(400L, daily[DateTime.Today.AddDays(-1)]["山田_経理"], "前日の山田_経理");
            Eq(8000L, daily[DateTime.Today]["山田_経理"], "当日の山田_経理（新規＋上書き）");
            Eq(700L, daily[DateTime.Today]["鈴木_総務"], "当日の鈴木_総務");
        }

        private static void T44_CopyUsage()
        {
            string t = DateTime.Now.ToString("yyyy-MM-dd") + " 10:00:00";
            string y = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") + " 10:00:00";
            string[] raw = {
                y + "\tPC-A\tu\t総務\t経理\tCOPY\t会議\\録画.mp4\t5000\t0.1",
                t + "\tPC-A\tu\t総務\t経理\tOVERWRITE\t会議\\録画.mp4　（確認して上書き）\t6000\t0.1",
                t + "\tPC-A\tu\t総務\t経理\tCOPY\t請求\\a.xlsx\t100\t0.1",
                t + "\tPC-A\tu\t総務\t経理\tRENAME\tx → y\t9999\t",
                t + "\tPC-B\tu\t総務\t経理\tCOPY\tb.pdf\t300\t0.1",
                t + "\tPC-C\tu\t設計\t図面\tCOPY\tA棟\\1F\\平面.dwg\t2000\t0.1" };
            List<LogLine> lines = new List<LogLine>();
            foreach (string r in raw) lines.Add(LogLine.Parse(r));

            // 同じフォルダ名「総務」を2台が使っている → 日ごとのグラフで別々にする
            SortedDictionary<DateTime, Dictionary<string, long>> daily = LogStats.Daily(lines);
            Dictionary<string, long> today = daily[DateTime.Today];
            Eq(6100L, today["総務（PC-A）"], "総務（PC-A）の当日");
            Eq(300L, today["総務（PC-B）"], "総務（PC-B）の当日");
            Eq(2000L, today["設計"], "フォルダ名が重ならないPCはそのまま");
            True(!today.ContainsKey("総務"), "まとめて数えない");

            UsageTree u = LogStats.CopyUsage(lines);
            Eq(13400L, u.Total, "合計（名前変更は含まない）");
            Eq(4, u.FileCount, "ファイル数（同じファイルは1件）");
            UsageNode a = u.Find("総務（PC-A）");
            True(a != null, "PC-A の場所がある");
            Eq(11100L, a.Size, "PC-A の容量");
            UsageNode kaigi = u.Find("総務（PC-A）\\経理\\会議");
            True(kaigi != null, "PC＼ジョブ＼フォルダ");
            Eq(1, kaigi.Top.Count, "注記を外して同じファイルとして数える");
            UsageFile mp4 = kaigi.Top[0];
            Eq("録画.mp4", mp4.Name, "ファイル名");
            Eq(11000L, mp4.Size, "コピー容量は2回分");
            Eq(2, mp4.Count, "回数");
            Eq(6000L, mp4.LastSize, "1回のサイズ（最後）");
            Eq("総務（PC-A）\\経理\\会議", mp4.Dir, "場所");
            True(u.Find("設計\\図面\\A棟\\1F") != null, "深いフォルダ");
            Eq("録画.mp4", u.Biggest[0].Name, "大きい順の1位");
            Eq("平面.dwg", u.Biggest[1].Name, "大きい順の2位");
            List<ExtStat> ext = u.ExtList();
            Eq(".mp4", ext[0].Ext, "種類別の1位");
            Eq(11000L, ext[0].Size, "種類別の容量");
            Eq(".mp4（動画）", UsageTree.ExtLabel(".mp4"), "種類の表示名");

            // 割合の項目：子の合計が親の容量と一致する（帯が100%になる）
            List<ShareItem> items = UsageItems.Items(u, u.Root, true);
            long sum = 0; foreach (ShareItem it in items) sum += it.Bytes;
            Eq(u.Total, sum, "割合の項目の合計＝全体");
            Note = "場所 " + u.Root.Children.Count + "件／ファイル " + u.FileCount + "件";
        }

        private static void T45_DiskUsage()
        {
            Box b = new Box("T45");
            WriteFile(b.D("動画\\大.mp4"), new string('x', 50000), 100);
            WriteFile(b.D("動画\\小.mp4"), new string('x', 1000), 100);
            WriteFile(b.D("請求\\2026\\a.xlsx"), new string('x', 3000), 100);
            WriteFile(b.D("直下.txt"), new string('x', 10), 100);
            Directory.CreateDirectory(b.D("空のフォルダ"));
            for (int i = 0; i < 200; i++) WriteFile(b.D("細かい\\f" + i + ".csv"), new string('y', 10 + i), 100);
            WriteFile(b.D("_AutoCopy\\削除済み\\x\\old.zip"), new string('z', 7000), 100);
            long expectSmall = 0; for (int i = 0; i < 200; i++) expectSmall += 10 + i;

            Dictionary<string, string> before = Snapshot(b.Dst);
            UsageTree t = DiskUsage.Scan(b.Dst, CancellationToken.None);
            CompareSnapshots(before, Snapshot(b.Dst));   // 読むだけで何も変えない

            Eq(50000L + 1000 + 3000 + 10 + 7000 + expectSmall, t.Total, "合計");
            Eq(205, t.FileCount, "ファイル数");
            Eq(51000L, t.Find("動画").Size, "フォルダ別");
            Eq(3000L, t.Find("請求").Size, "サブフォルダも含む");
            True(t.Find("空のフォルダ") != null, "空のフォルダも出す");
            Eq(7000L, t.Find("_AutoCopy").Size, "削除済み・退避も容量に数える");
            Eq("大.mp4", t.Biggest[0].Name, "大きいファイルの1位");
            Eq(Path.Combine(b.Dst, "動画\\大.mp4"), t.Biggest[0].FullPath, "実際のパス");
            Eq("動画", t.Biggest[0].Dir, "場所（調べたフォルダからの位置）");
            Eq(".mp4", t.ExtList()[0].Ext, "種類別の1位");
            Eq(10L, t.Root.DirectSize, "直下のファイル");

            // 1フォルダに多いときは上位だけ残し、残りは「ほかのファイル」にまとめる
            UsageNode many = t.Find("細かい");
            Eq(UsageNode.TopPerFolder, many.Top.Count, "上位だけ残す");
            List<ShareItem> items = UsageItems.Items(t, many, false);
            True(items.Count <= UsageItems.MaxRows, "行数の上限");
            long sum = 0; foreach (ShareItem it in items) sum += it.Bytes;
            Eq(many.Size, sum, "割合の項目の合計＝フォルダの容量");
            List<ShareItem> rootItems = UsageItems.Items(t, t.Root, false);
            True(rootItems[0].Label == "動画\\" && rootItems[0].Data != null, "容量の多い順・フォルダはクリックで開ける");

            Throws<DirectoryNotFoundException>(delegate { DiskUsage.Scan(Path.Combine(b.Root, "ない"), CancellationToken.None); }, "ないフォルダ");
        }

        private static void T46_DiskUsageCancel()
        {
            Box b = new Box("T46");
            for (int i = 0; i < 3000; i++) WriteFile(b.D("d" + (i % 30) + "\\f" + i + ".txt"), "x" + i, 100);
            Dictionary<string, string> before = Snapshot(b.Dst);

            // 始める前に中止済み → すぐ止まる
            using (CancellationTokenSource pre = new CancellationTokenSource())
            {
                pre.Cancel();
                Throws<OperationCanceledException>(delegate { DiskUsage.Scan(b.Dst, pre.Token, null); }, "中止済みなら始めない");
            }

            // 途中経過が届き、途中で中止すると止まる
            int reports = 0, filesAtStop = 0;
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Stopwatch sw = Stopwatch.StartNew();
                Throws<OperationCanceledException>(delegate
                {
                    DiskUsage.Scan(b.Dst, cts.Token, delegate(int files, long bytes, string dir)
                    {
                        reports++;
                        // 最初の途中経過（調べ始め）で中止する。途中経過は0.2秒ごとなので、ここで止めないと小さいフォルダは終わってしまう
                        if (!cts.IsCancellationRequested) { filesAtStop = files; cts.Cancel(); }
                    });
                }, "途中で中止");
                True(sw.ElapsedMilliseconds < 10000, "すぐ止まる: " + sw.ElapsedMilliseconds + "ms");
            }
            True(reports >= 1, "途中経過が届く");
            True(filesAtStop < 3000, "途中で止まった（" + filesAtStop + "件の時点）");

            // 中止しなければ最後まで数える
            int last = 0;
            UsageTree t = DiskUsage.Scan(b.Dst, CancellationToken.None, delegate(int files, long bytes, string dir) { last = files; });
            Eq(3000, t.FileCount, "最後まで数える");
            CompareSnapshots(before, Snapshot(b.Dst));
            Note = "中止時点 " + filesAtStop + "件／途中経過 " + reports + "回";
        }

        private static void T47_EmptyFolders()
        {
            Box b = new Box("T47");
            b.W("data\\a.txt", "aaa");
            Directory.CreateDirectory(b.S("keep\\empty"));          // コピー元にもある空のフォルダ
            Directory.CreateDirectory(b.S("src_only_empty"));
            b.Run();
            Dictionary<string, string> srcBefore = Snapshot(b.Src);

            Directory.CreateDirectory(b.D("old\\sub\\deeper"));   // 入れ子の空フォルダ → いちばん上の old だけ
            Directory.CreateDirectory(b.D("data\\emptyInDest"));
            Directory.CreateDirectory(b.D("keep\\empty"));          // コピー元にもある → 残す
            Directory.CreateDirectory(b.D("hid"));
            File.SetAttributes(b.D("hid"), FileAttributes.Directory | FileAttributes.Hidden);   // 隠しフォルダ → 対象外
            WriteFile(b.D("ckp\\x.ckpart"), "partial", 10);          // コピー作業中の一時ファイル → 対象外
            Directory.CreateDirectory(b.D("later"));                  // チェックの後でファイルが入る
            Directory.CreateDirectory(b.D("_AutoCopy\\空"));        // アプリのフォルダ → 対象外

            DupResult d = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            DupGroup g = d.Groups.Find(delegate(DupGroup x) { return x.Kind == "empty"; });
            True(g != null, "空のフォルダのグループがある");
            List<string> rels = new List<string>();
            foreach (DupEntry e in g.Entries) rels.Add(e.Rel);
            rels.Sort(StringComparer.OrdinalIgnoreCase);
            Eq("data\\emptyInDest|later|old", string.Join("|", rels.ToArray()), "候補（入れ子は上だけ・コピー元にあるもの・隠し・作業中・アプリのフォルダは除く）");
            foreach (DupEntry e in g.Entries) True(e.Recommended && e.CanDelete && e.IsFolder, "削除を推奨");

            // 入れなければ探さない
            b.Job.DupShowEmptyFolders = false;
            DupResult off = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            True(!off.Groups.Exists(delegate(DupGroup x) { return x.Kind == "empty"; }), "設定でオフにできる");
            b.Job.DupShowEmptyFolders = true;

            // チェック後にファイルが入ったフォルダは削除しない
            WriteFile(b.D("later\\new.txt"), "new", 10);
            int n = DupChecker.DeleteSelected(b.Job, b.App, b.State, g.Entries);
            Eq(2, n, "削除した件数");
            True(!Directory.Exists(b.D("old")), "old を削除");
            True(!Directory.Exists(b.D("data\\emptyInDest")), "data\\emptyInDest を削除");
            True(File.Exists(b.D("data\\a.txt")), "同じフォルダのファイルは残る");
            True(File.Exists(b.D("later\\new.txt")), "空でなくなったフォルダは残す");
            True(Directory.Exists(b.D("keep\\empty")) && Directory.Exists(b.D("hid")) && File.Exists(b.D("ckp\\x.ckpart")), "対象外は残る");
            CompareSnapshots(srcBefore, Snapshot(b.Src));   // コピー元は一切変わらない（空のフォルダも）

            // 削除済みに入り、元に戻せる
            List<TrashStore.Batch> batches = TrashStore.List(b.Dst);
            Eq(1, batches.Count, "削除済みの記録");
            Eq(0, batches[0].FileCount, "ファイルは0件");
            Eq(2, batches[0].EmptyFolderCount, "空のフォルダ2件（old\\sub\\deeper と data\\emptyInDest）");
            Eq(2, TrashStore.Restore(b.Job.Name, b.Dst, batches[0]), "元に戻す件数");
            True(Directory.Exists(b.D("old\\sub\\deeper")), "入れ子ごと戻る");
            True(Directory.Exists(b.D("data\\emptyInDest")), "戻る");

            // 2回目のチェック：同じ結果（元に戻したので）、ミラーでない普通のジョブでもコピー元は空フォルダごと残っている
            DupResult again = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            DupGroup g2 = again.Groups.Find(delegate(DupGroup x) { return x.Kind == "empty"; });
            Eq(2, g2.Entries.Count, "元に戻した2件がまた候補になる");
            CompareSnapshots(srcBefore, Snapshot(b.Src));
        }

        private static void T48_HiddenMeta()
        {
            Box b = new Box("T48");
            b.W("a.txt", "a");
            b.Run();
            string meta = b.D("_AutoCopy");
            True(Directory.Exists(meta), "_AutoCopy がある（持ち主の記録）");
            True((File.GetAttributes(meta) & FileAttributes.Hidden) != 0, "隠しフォルダ");
            // 隠しを外されても、次の実行でまた隠す
            File.SetAttributes(meta, FileAttributes.Directory);
            b.W("b.txt", "b");
            b.Run();
            True((File.GetAttributes(meta) & FileAttributes.Hidden) != 0, "次の実行でまた隠す");
        }

        private static void WriteBig(string path, int mb, byte seed, int ageSec)
        {
            string l = PathUtil.Long(path);
            Directory.CreateDirectory(Path.GetDirectoryName(l));
            byte[] buf = new byte[1024 * 1024];
            using (FileStream fs = new FileStream(l, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < mb; i++)
                {
                    for (int k = 0; k < buf.Length; k += 4096) buf[k] = (byte)(seed + i + k / 4096);
                    fs.Write(buf, 0, buf.Length);
                }
            }
            File.SetLastWriteTimeUtc(l, DateTime.UtcNow.AddSeconds(-ageSec));
        }

        private static void T49_Dedup()
        {
            Box b = new Box("T49");
            b.Job.DedupLarge = true;   // 使うと選んだジョブ
            WriteBig(b.S("a\\動画.mp4"), 12, 1, 300);
            WriteBig(b.S("z\\コピー\\動画_コピー.mp4"), 12, 1, 200);   // 中身が同じ
            WriteBig(b.S("m\\別の動画.mp4"), 12, 9, 300);              // 大きさは同じで中身が違う
            WriteBig(b.S("小1.bin"), 1, 5, 300);                        // 10MB未満の重複は普通にコピー
            WriteBig(b.S("小2.bin"), 1, 5, 300);
            RunResult r = b.Run();
            Eq(1, r.Deduped, "重複で省略した件数");
            Eq(12L * 1024 * 1024, r.DedupBytes, "省略した容量");
            Eq(4, r.Copied, "普通にコピーした件数");
            True(File.Exists(b.D("a\\動画.mp4")), "1つ目はコピー");
            True(!File.Exists(b.D("z\\コピー\\動画_コピー.mp4")), "2つ目はコピーしない");
            string ph = b.D("z\\コピー\\動画_コピー.mp4" + AppPaths.PlaceholderSuffix);
            True(File.Exists(ph), "代わりにテキストを置く");
            string text = File.ReadAllText(ph, Encoding.UTF8);
            True(text.Contains(b.D("a\\動画.mp4")), "1つ目の保管場所が書いてある");
            True(text.Contains(b.S("z\\コピー\\動画_コピー.mp4")), "コピー元のファイルも書いてある");
            byte[] head = new byte[3];
            using (FileStream fs = File.OpenRead(ph)) fs.Read(head, 0, 3);
            True(head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF, "BOM 付き（Windows 7 のメモ帳でも読める）");
            True(File.Exists(b.D("m\\別の動画.mp4")) && File.Exists(b.D("小1.bin")) && File.Exists(b.D("小2.bin")), "中身が違うもの・小さいものは普通にコピー");

            RunResult r2 = b.Run();
            Eq(0, r2.Copied + r2.Overwritten + r2.Deduped, "2回目は何もしない");

            // オフにすると、次から普通にコピーする
            b.Job.DedupLarge = false;
            RunResult r3 = b.Run();
            Eq(1, r3.Copied, "オフにすると普通にコピー");
            True(File.Exists(b.D("z\\コピー\\動画_コピー.mp4")) && !File.Exists(ph), "テキストは片付ける");
            Note = "省略 " + Fmt.Size(r.DedupBytes);
        }

        private static void T50_DedupChanges()
        {
            // 中身が変わったら普通にコピーし、テキストを片付ける
            Box b = new Box("T50");
            b.Job.DedupLarge = true;
            WriteBig(b.S("a.bin"), 11, 3, 300);
            WriteBig(b.S("b.bin"), 11, 3, 300);
            WriteBig(b.S("c.bin"), 11, 3, 300);
            RunResult r = b.Run();
            Eq(2, r.Deduped, "2つ目・3つ目を省略");
            True(File.Exists(b.D("a.bin")) && File.Exists(b.D("b.bin" + AppPaths.PlaceholderSuffix)) && File.Exists(b.D("c.bin" + AppPaths.PlaceholderSuffix)), "a だけコピー");
            WriteBig(b.S("b.bin"), 11, 7, 100);   // b の中身が変わる
            RunResult r2 = b.Run();
            Eq(1, r2.Copied, "b を普通にコピー");
            True(File.Exists(b.D("b.bin")) && !File.Exists(b.D("b.bin" + AppPaths.PlaceholderSuffix)), "b のテキストを片付ける");
            True(File.Exists(b.D("c.bin" + AppPaths.PlaceholderSuffix)), "c はテキストのまま");

            // 重複チェック：テキストは孤立ファイルにしない
            b.Job.DupShowOrphans = true;
            DupResult d = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            foreach (DupGroup g in d.Groups)
                foreach (DupEntry e in g.Entries)
                    True(!e.Rel.EndsWith(AppPaths.PlaceholderSuffix), "テキストは重複チェックの候補にならない: " + e.Rel);

            // ミラー：1つ目（a）をコピー元で消すと、c を普通にコピーし、a はコピー先から削除済みへ
            Box m = new Box("T50m");
            m.Job.Mirror = true; m.Job.DedupLarge = true;
            WriteBig(m.S("a.bin"), 11, 4, 300);
            WriteBig(m.S("c.bin"), 11, 4, 300);
            m.Run();
            True(File.Exists(m.D("c.bin" + AppPaths.PlaceholderSuffix)), "c はテキスト");
            File.Delete(m.S("a.bin"));
            RunResult rm = m.Run();
            True(File.Exists(m.D("c.bin")), "c を普通にコピー");
            True(!File.Exists(m.D("c.bin" + AppPaths.PlaceholderSuffix)), "c のテキストを片付ける");
            True(!File.Exists(m.D("a.bin")), "a はミラーで削除済みへ");
            Eq(1, rm.Deleted, "ミラー削除 1件");

            // ミラー：テキストを置いたファイルをコピー元で消すと、テキストを削除済みへ
            Box m2 = new Box("T50n");
            m2.Job.Mirror = true; m2.Job.DedupLarge = true;
            WriteBig(m2.S("a.bin"), 11, 6, 300);
            WriteBig(m2.S("c.bin"), 11, 6, 300);
            m2.Run();
            File.Delete(m2.S("c.bin"));
            RunResult rm2 = m2.Run();
            Eq(1, rm2.Deleted, "テキストを削除");
            True(!File.Exists(m2.D("c.bin" + AppPaths.PlaceholderSuffix)) && File.Exists(m2.D("a.bin")), "テキストだけ片付け、a は残る");
        }

        /// <summary>同期のテスト用：2台のPC（このPCのフォルダ A・B）と共有フォルダ S</summary>
        private class SyncPair
        {
            public string Root, A, B, S;
            public AppSettings App;
            public JobConfig JA, JB;
            public SyncPair(string name)
            {
                Root = Path.Combine(baseDir, name);
                A = Path.Combine(Root, "PC1"); B = Path.Combine(Root, "PC2"); S = Path.Combine(Root, "共有");
                foreach (string d in new string[] { A, B, S }) Directory.CreateDirectory(d);
                App = new AppSettings();
                App.InstanceId = "test-instance";
                App.FolderName = "PC1";
                App.TrashDays = 30;
                JA = new JobConfig(); JA.Name = name + "A"; JA.Source = A; JA.Dest = S; JA.Sync = true; JA.RetryCount = 0; JA.SyncTakeNew = true; JA.SyncPushNew = true;
                JB = new JobConfig(); JB.Name = name + "B"; JB.Source = B; JB.Dest = S; JB.Sync = true; JB.RetryCount = 0; JB.SyncTakeNew = true; JB.SyncPushNew = true;
                App.Jobs.Add(JA); App.Jobs.Add(JB);
                AppCore.Settings = App;
                SafeFs.SetProtectedSources(new string[] { A, B });
                SafeFs.SetSyncRoots(new string[] { A, B });
                Logger.Configure(App);
            }
            public RunResult Run(JobConfig j, string instance)
            {
                App.InstanceId = instance;   // 別のPCとして動かす
                AppCore.Settings = App;
                RunResult r = new CopyEngine(j, App, AppCore.GetState(j.Id), false, false, CancellationToken.None).Run();
                AppCore.SaveState(j.Id);
                return r;
            }
        }

        private static string Read(string p) { return File.ReadAllText(PathUtil.Long(p), Encoding.UTF8); }

        private static void T51_Sync()
        {
            SyncPair p = new SyncPair("T51");
            WriteFile(Path.Combine(p.A, "a.txt"), "PC1が作った", 600);
            WriteFile(Path.Combine(p.B, "資料\\b.txt"), "PC2が作った", 600);
            RunResult r1 = p.Run(p.JA, "pc1");
            Eq(1, r1.Copied, "PC1：共有へ a");
            RunResult r2 = p.Run(p.JB, "pc2");
            Eq(1, r2.Copied, "PC2：共有へ b");
            Eq(1, r2.Downloaded, "PC2：a を取り込み");
            RunResult r3 = p.Run(p.JA, "pc1");
            Eq(1, r3.Downloaded, "PC1：b を取り込み");
            Eq("PC2が作った", Read(Path.Combine(p.A, "資料\\b.txt")), "PC1 に b");
            Eq("PC1が作った", Read(Path.Combine(p.B, "a.txt")), "PC2 に a");
            True(!File.Exists(Path.Combine(p.S, "_AutoCopy\\owner.json")), "同期では持ち主の記録を作らない（複数PCで使える）");

            RunResult r4 = p.Run(p.JA, "pc1");
            Eq(0, r4.Copied + r4.Overwritten + r4.Downloaded + r4.Conflicts, "変更がなければ何もしない");

            // PC2 で a を更新 → 共有へ → PC1 に取り込み（PC1 の前の版は控えに）
            WriteFile(Path.Combine(p.B, "a.txt"), "PC2が更新", 100);
            RunResult r5 = p.Run(p.JB, "pc2");
            Eq(1, r5.Overwritten, "PC2：共有の a を更新");
            RunResult r6 = p.Run(p.JA, "pc1");
            Eq(1, r6.Downloaded, "PC1：新しい a を取り込み");
            Eq("PC2が更新", Read(Path.Combine(p.A, "a.txt")), "PC1 の a が新しい版");
            string[] backups = Directory.GetFiles(Path.Combine(AppPaths.SyncBackupDir, p.JA.Id), "a.txt", SearchOption.AllDirectories);
            Eq(1, backups.Length, "前の版の控え");
            Eq("PC1が作った", Read(backups[0]), "控えの中身は前の版");

            // PC1 で b を削除 → 共有・PC2 からは消さない、PC1 にも取り込み直さない
            File.Delete(Path.Combine(p.A, "資料\\b.txt"));
            RunResult r7 = p.Run(p.JA, "pc1");
            Eq(0, r7.Downloaded, "消したものは取り込み直さない");
            True(!File.Exists(Path.Combine(p.A, "資料\\b.txt")), "PC1 では消えたまま");
            True(File.Exists(Path.Combine(p.S, "資料\\b.txt")), "共有には残る（削除は伝えない）");
            p.Run(p.JB, "pc2");
            True(File.Exists(Path.Combine(p.B, "資料\\b.txt")), "PC2 にも残る");
            // ただし、他のPCがその後で更新したら取り込む
            WriteFile(Path.Combine(p.B, "資料\\b.txt"), "PC2がb更新", 50);
            p.Run(p.JB, "pc2");
            RunResult r8 = p.Run(p.JA, "pc1");
            Eq(1, r8.Downloaded, "更新されたら取り込む");
            Eq("PC2がb更新", Read(Path.Combine(p.A, "資料\\b.txt")), "新しい版");
        }

        private static void T52_SyncConflict()
        {
            SyncPair p = new SyncPair("T52");
            WriteFile(Path.Combine(p.A, "c.txt"), "最初の版", 600);
            p.Run(p.JA, "pc1");
            p.Run(p.JB, "pc2");
            Eq("最初の版", Read(Path.Combine(p.B, "c.txt")), "PC2 に c");

            WriteFile(Path.Combine(p.A, "c.txt"), "PC1の変更", 80);
            WriteFile(Path.Combine(p.B, "c.txt"), "PC2の変更", 70);
            RunResult r1 = p.Run(p.JA, "pc1");
            Eq(1, r1.Overwritten, "PC1 の変更を共有へ");
            RunResult r2 = p.Run(p.JB, "pc2");
            Eq(1, r2.Conflicts, "PC2：両方で変わったので確認待ち");
            Eq(0, r2.Downloaded + r2.Overwritten, "勝手に上書きしない");
            Eq("PC2の変更", Read(Path.Combine(p.B, "c.txt")), "PC2 の版はそのまま");
            Eq("PC1の変更", Read(Path.Combine(p.S, "c.txt")), "共有の版もそのまま");
            JobState stB = AppCore.GetState(p.JB.Id);
            ConflictItem c;
            lock (stB) { c = stB.Conflicts[0]; }
            Eq("both", c.Reason, "理由");

            // 共有の方を使う → PC2 は PC1 の版になり、PC2 の版は控えに
            Eq<string>(null, AppCore.ResolveConflict(p.JB, c, AppCore.ActTakeShared, true), "共有の方を使う");
            Eq("PC1の変更", Read(Path.Combine(p.B, "c.txt")), "PC2 が共有の版になる");
            string[] bk = Directory.GetFiles(Path.Combine(AppPaths.SyncBackupDir, p.JB.Id), "c.txt", SearchOption.AllDirectories);
            True(bk.Length >= 1 && Read(bk[bk.Length - 1]) == "PC2の変更", "PC2 の版は控えに残る");
            RunResult r3 = p.Run(p.JB, "pc2");
            Eq(0, r3.Conflicts + r3.Downloaded + r3.Overwritten, "解決後は何もしない");

            // もう一度ぶつけて、このPCの方を使う
            WriteFile(Path.Combine(p.A, "c.txt"), "PC1の再変更", 40);
            WriteFile(Path.Combine(p.B, "c.txt"), "PC2の再変更!", 30);
            p.Run(p.JA, "pc1");
            RunResult r4 = p.Run(p.JB, "pc2");
            Eq(1, r4.Conflicts, "また確認待ち");
            lock (stB) { c = stB.Conflicts[0]; }
            Eq<string>(null, AppCore.ResolveConflict(p.JB, c, AppCore.ActOverwrite, true), "このPCの方を使う");
            Eq("PC2の再変更!", Read(Path.Combine(p.S, "c.txt")), "共有が PC2 の版になる");
            RunResult r5 = p.Run(p.JA, "pc1");
            Eq(1, r5.Downloaded, "PC1 に取り込む");
            Eq("PC2の再変更!", Read(Path.Combine(p.A, "c.txt")), "PC1 も PC2 の版");

            // 初回：同じ名前で中身が違うファイルが両方にある → 確認待ち（勝手に上書きしない）
            WriteFile(Path.Combine(p.B, "初回.txt"), "PC2の初回", 500);
            WriteFile(Path.Combine(p.S, "初回.txt"), "共有の初回です", 400);
            RunResult r6 = p.Run(p.JB, "pc2");
            lock (stB) { True(stB.Conflicts.Exists(delegate(ConflictItem x) { return x.Rel == "初回.txt" && x.Reason == "first"; }), "初回で中身が違う → 確認待ち"); }
            Eq("PC2の初回", Read(Path.Combine(p.B, "初回.txt")), "そのまま");

            // 同期でも、このPCのフォルダの削除・移動は今までどおり拒否する
            Throws<SourceProtectedException>(delegate { SafeFs.DeleteFile(Path.Combine(p.A, "c.txt")); }, "削除は拒否");
            Throws<SourceProtectedException>(delegate { SafeFs.Move(Path.Combine(p.A, "c.txt"), Path.Combine(p.A, "x.txt")); }, "移動は拒否");
            // 同期でないジョブのコピー元へは取り込みもできない
            Box nb = new Box("T52n");
            nb.W("x.txt", "x");
            SafeFs.SetSyncRoots(new string[] { p.A, p.B });
            Throws<SourceProtectedException>(delegate { SafeFs.CopyIntoSync(Path.Combine(p.S, "c.txt"), nb.S("y.txt"), "y.txt", DateTime.UtcNow, null, CancellationToken.None); }, "同期でないコピー元への取り込みは拒否");
            True(File.Exists(Path.Combine(p.A, "c.txt")), "PC1 の c は残っている");
        }

        private static void T53_Attention()
        {
            Box b = new Box("T53");
            b.W("a.txt", "a");
            b.Run();
            Eq(0, AppCore.AttentionItems().Count, "最初は何もない");

            ConflictItem c = new ConflictItem(); c.Rel = "a.txt"; c.Reason = "newer";
            lock (b.State) { b.State.Conflicts.Add(c); }
            PendingDelete p = new PendingDelete(); p.Rel = "x.txt"; p.Size = 1;
            lock (b.State) { b.State.PendingDeletes.Add(p); }
            List<AppCore.AttentionItem> items = AppCore.AttentionItems();
            True(items.Exists(delegate(AppCore.AttentionItem x) { return x.Action == "conflicts" && x.Text.Contains("1件"); }), "上書きの確認");
            True(items.Exists(delegate(AppCore.AttentionItem x) { return x.Action == "pending" && x.JobId == b.Job.Id; }), "ミラーの保留");

            // 重複チェック：削除候補があり、まだ見ていない結果
            Directory.CreateDirectory(b.D("空"));
            DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            items = AppCore.AttentionItems();
            True(items.Exists(delegate(AppCore.AttentionItem x) { return x.Action == "dup" && x.JobId == b.Job.Id; }), "未確認の重複チェック結果");
            // 結果の画面で見たら、確認が必要なものから外れる
            DupResult r = DupChecker.LoadResult(b.Job.Id);
            r.Seen = true;
            Json.Save(DupChecker.ResultPath(b.Job.Id), r);
            System.Threading.Thread.Sleep(20);
            File.SetLastWriteTimeUtc(DupChecker.ResultPath(b.Job.Id), DateTime.UtcNow);
            items = AppCore.AttentionItems();
            True(!items.Exists(delegate(AppCore.AttentionItem x) { return x.Action == "dup"; }), "見た結果は外れる");

            lock (b.State) { b.State.Conflicts.Clear(); b.State.PendingDeletes.Clear(); }
            Eq(0, AppCore.AttentionItems().Count, "確認が済めば0件");
        }

        private static void T54_UpdaterBasics()
        {
            Box b = new Box("T54");
            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            True(Updater.VersionOf(self) != null, "版を読める");
            Eq(Updater.CurrentVersion, Updater.VersionOf(self), "今の版");
            string sig; bool unsigned;
            string err = Updater.CheckSignature(self, out sig, out unsigned);
            True(err != null && unsigned, "署名のない exe は「署名なし」と判定: " + err);
            Version v; string signer;
            True(Updater.Acceptable(self, out v, out signer).StartsWith("新しい版ではありません"), "同じ版には更新しない");

            string reason;
            b.App.UpdateFolder = null;
            True(Updater.Find(b.App, out reason) == null && reason.Contains("設定されていません"), "置き場所が未設定");
            b.App.UpdateFolder = Path.Combine(b.Root, "upd");
            Directory.CreateDirectory(b.App.UpdateFolder);
            True(Updater.Find(b.App, out reason) == null && reason.Contains("AutoCopy.exe がありません"), "置き場所に exe がない: " + reason);
            File.Copy(self, Path.Combine(b.App.UpdateFolder, "AutoCopy.exe"));
            True(Updater.Find(b.App, out reason) == null && reason.StartsWith("新しい版ではありません"), "同じ版なら更新しない: " + reason);
            // 中身が壊れたファイル（版を読めない）
            File.WriteAllText(Path.Combine(b.App.UpdateFolder, "AutoCopy.exe"), "not an exe");
            True(Updater.Find(b.App, out reason) == null, "壊れたファイルでは更新しない: " + reason);
            Note = "今の版 " + Updater.CurrentVersion;
        }

        private static void T55_Preview()
        {
            Box b = new Box("T55");
            b.Job.Mirror = true;
            b.W("keep.txt", "keep");
            b.W("old\\a.txt", "aaa");
            b.W("upd.txt", "v1", 600);
            b.W("conf.txt", "v1", 600);
            b.W("gone.txt", "gone");
            b.Run();
            // 変更：新規・上書き（コピー先が古い）・確認待ち（コピー先が新しい）・名前変更・除外・コピー元で削除
            b.W("new.txt", "new");
            b.W("upd.txt", "v2", 100);
            WriteFile(b.D("conf.txt"), "dest edited", 10);
            b.W("conf.txt", "v2", 200);
            Directory.Move(b.S("old"), b.S("renamed"));
            b.W("x.tmp", "temp");
            b.Job.Exclude.Extensions.Add(".tmp");
            File.Delete(b.S("gone.txt"));

            Dictionary<string, string> srcBefore = Snapshot(b.Src), dstBefore = Snapshot(b.Dst);
            int recBefore; lock (b.State) recBefore = b.State.Files.Count;
            JobProgress pr = new JobProgress();
            RunResult r = AppCore.DryRun(b.Job, false, CancellationToken.None, pr);
            CompareSnapshots(srcBefore, Snapshot(b.Src));
            CompareSnapshots(dstBefore, Snapshot(b.Dst));     // コピー先も変えない
            lock (b.State) { Eq(recBefore, b.State.Files.Count, "記録も変えない"); True(b.State.Files.ContainsKey("old\\a.txt"), "名前変更前の記録のまま"); }

            Func<string, string, bool> has = delegate(string kind, string rel)
            {
                return r.Plan.Exists(delegate(PlanItem p) { return p.Kind == kind && p.Rel == rel; });
            };
            True(has(PlanKind.Copy, "new.txt"), "新規コピー");
            True(has(PlanKind.Overwrite, "upd.txt"), "上書き（コピー先が古い）");
            True(has(PlanKind.Conflict, "conf.txt"), "確認待ち（コピー先が新しい）");
            True(r.Plan.Exists(delegate(PlanItem p) { return p.Kind == PlanKind.Rename && p.Note.Contains("old"); }), "名前変更");
            True(has(PlanKind.Excluded, "x.tmp"), "除外");
            True(has(PlanKind.Delete, "gone.txt"), "ミラー削除");
            True(!r.Plan.Exists(delegate(PlanItem p) { return p.Rel == "keep.txt"; }), "変わっていないものは出さない");
            True(r.Unchanged >= 1, "変更なしの件数");
            PlanItem c = r.Plan.Find(delegate(PlanItem p) { return p.Kind == PlanKind.Conflict; });
            True(c.DstSize > 0 && c.DstMtime > 0 && c.Note.Length > 0, "コピー先のサイズ・日時・説明");
            True(pr.Scanned >= 5, "調べた件数を作業状況に出す");

            // 予測どおりに実行される
            RunResult real = b.Run();
            Eq(1, real.Copied, "実行：新規");
            Eq(1, real.Overwritten, "実行：上書き");
            Eq(1, real.Conflicts, "実行：確認待ち");
            Eq(1, real.Deleted, "実行：削除");
            Eq(0, real.Plan.Count, "実際の実行では一覧を作らない（メモリ節約）");
            Note = "予測 " + r.Plan.Count + "件";
        }

        private static void T56_Progress()
        {
            Box b = new Box("T56");
            for (int i = 0; i < 4; i++) WriteBig(b.S("f" + i + ".bin"), 2, (byte)i, 300);
            b.W("small.txt", "s");
            b.Job.BandwidthKBps = 4096;    // ゆっくりコピーして途中の様子を見る
            b.Job.Parallel = 2;
            b.Apply();
            CopyEngine e = new CopyEngine(b.Job, b.App, b.State, false, false, CancellationToken.None);
            JobProgress p = e.Progress;
            int maxActive = 0; long maxDone = 0; bool sawPercent = false; string sawFile = null;
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                Stopwatch lim = Stopwatch.StartNew();
                while (p.Finished == DateTime.MinValue && lim.ElapsedMilliseconds < 120000)
                {
                    JobProgress s2 = p.Snapshot();
                    maxActive = Math.Max(maxActive, s2.ActiveList.Count);
                    if (s2.ActiveList.Count > 0 && sawFile == null) sawFile = s2.ActiveList[0].Rel + " " + s2.ActiveList[0].Size;
                    if (s2.Percent > 0 && s2.Percent < 100) sawPercent = true;
                    maxDone = Math.Max(maxDone, s2.DoneBytes);
                    System.Threading.Thread.Sleep(50);
                }
            });
            t.Start();
            RunResult r = e.Run();
            t.Join();
            JobProgress f = p.Snapshot();
            Eq(5, f.TotalFiles, "全体の件数");
            Eq(5, f.DoneFiles, "終わった件数");
            Eq(8L * 1024 * 1024 + 1, f.TotalBytes, "全体の容量");
            Eq(f.TotalBytes, f.DoneBytes, "終わった容量");
            Eq(100.0, f.Percent, "100%");
            Eq("完了", f.Phase, "最後は完了");
            Eq(0, f.ActiveList.Count, "コピー中のファイルは残らない");
            True(maxActive >= 1 && sawFile != null, "コピー中のファイル名とサイズが見える: " + sawFile);
            True(sawPercent, "途中の進み具合（0〜100%）が見える");
            True(f.Speed > 0, "速さ");
            Eq(5, r.Copied, "コピー件数");
            Note = "同時に最大 " + maxActive + "件・" + sawFile;
        }

        private static void T57_DedupChoice()
        {
            Eq(DedupMode.Ask, new JobConfig().Dedup, "新しいジョブは「見つけたら毎回選ぶ」");
            Eq(DedupMode.Auto, Json.Deserialize<JobConfig>("{\"Name\":\"v17\",\"DedupEnabled\":true}").Dedup, "v1.7 で「使う」にしていたジョブは「自動」");
            Eq(DedupMode.Ask, Json.Deserialize<JobConfig>("{\"Name\":\"v17\",\"DedupEnabled\":false}").Dedup, "v1.7 で「使わない」（初期値）だったジョブは「毎回選ぶ」");
            Eq(DedupMode.Ask, Json.Deserialize<JobConfig>("{\"Name\":\"古い\"}").Dedup, "設定がなければ「毎回選ぶ」");
            JobConfig off = Json.Deserialize<JobConfig>("{\"Name\":\"新\",\"DedupMode\":\"off\",\"DedupMinMB\":20}");
            Eq(DedupMode.Off, off.Dedup, "使わない");
            Eq(20, off.DedupMinMB, "大きさ");
            string saved = Json.Serialize(off);
            True(saved.Contains("DedupMode") && !saved.Contains("DedupEnabled"), "保存の名前: " + saved.Substring(0, Math.Min(80, saved.Length)));
            Eq(DedupMode.Off, Json.Deserialize<JobConfig>(saved).Dedup, "保存して読み直しても同じ");
            // 使わないジョブでは重複しても普通にコピー
            Box b = new Box("T57");
            b.Job.Dedup = DedupMode.Off;
            WriteBig(b.S("a.bin"), 11, 2, 300);
            WriteBig(b.S("b.bin"), 11, 2, 300);
            RunResult r = b.Run();
            Eq(0, r.Deduped + r.DedupAsked, "省略も確認もしない");
            Eq(2, r.Copied, "両方コピー");
            Eq(0, DedupStore.PendingCount(b.Job.Id), "確認待ちなし");
        }

        private static void T58_DedupAsk()
        {
            // 見つけたら毎回選ぶ（初期値）：選ぶまでコピーせずに待ち、確認が必要なものとして知らせる
            Box b = new Box("T58");
            Eq(DedupMode.Ask, b.Job.Dedup, "初期値");
            WriteBig(b.S("動画\\元.mp4"), 11, 3, 300);
            WriteBig(b.S("動画\\写し.mp4"), 11, 3, 300);
            WriteBig(b.S("資料\\大.zip"), 11, 5, 300);
            WriteBig(b.S("資料\\大_コピー.zip"), 11, 5, 300);
            RunResult r = b.Run();
            Eq(2, r.Copied, "1つ目だけコピー");
            Eq(2, r.DedupAsked, "2つ目は扱いを選ぶまで待つ");
            True(r.DedupNew, "新しく見つかった");
            True(!File.Exists(b.D("動画\\写し.mp4")) && !File.Exists(b.D("動画\\写し.mp4" + AppPaths.PlaceholderSuffix)), "まだコピーもテキストもしない");
            DedupBook book = DedupStore.Get(b.Job.Id);
            Eq(2, book.Pending.Count, "確認待ち");
            True(AppCore.AttentionItems().Exists(delegate(AppCore.AttentionItem x) { return x.Text.Contains("大きいファイルの重複 2件"); }), "確認が必要なものとして知らせる");
            RunResult r2 = b.Run();
            Eq(2, r2.DedupAsked, "2回目も待つ");
            True(!r2.DedupNew, "同じものは新しく知らせない");

            // 予測でも「扱いを選ぶ」と出る
            RunResult pv = AppCore.DryRun(b.Job, false);
            Eq(2, pv.Plan.FindAll(delegate(PlanItem p) { return p.Kind == PlanKind.DedupAsk; }).Count, "予測：扱いを選ぶ");

            // テキストにする
            DedupPendingItem v = book.Pending.Find(delegate(DedupPendingItem x) { return x.Rel == "動画\\写し.mp4"; });
            Eq("動画\\元.mp4", v.Target, "1つ目");
            Eq<string>(null, LargeDup.Resolve(b.Job, b.App, b.State, v, "text"), "テキストにする");
            True(File.Exists(b.D("動画\\写し.mp4" + AppPaths.PlaceholderSuffix)) && !File.Exists(b.D("動画\\写し.mp4")), "テキストを置いた");
            // 普通にコピーする
            DedupPendingItem z = book.Pending.Find(delegate(DedupPendingItem x) { return x.Rel == "資料\\大_コピー.zip"; });
            Eq<string>(null, LargeDup.Resolve(b.Job, b.App, b.State, z, "copy"), "普通にコピー");
            True(File.Exists(b.D("資料\\大_コピー.zip")), "コピーした");
            Eq(0, book.Pending.Count, "確認待ちは0件");
            b.Apply(); AppCore.SaveState(b.Job.Id);
            RunResult r3 = b.Run();
            Eq(0, r3.Copied + r3.Overwritten + r3.Deduped + r3.DedupAsked, "選んだ後は何もしない");

            // 重複チェックの画面：普通にコピー済みの重複も選べる（テキストにすると削除済みへ移して容量を空ける）
            Box c = new Box("T58c");
            c.Job.Dedup = DedupMode.Off;
            WriteBig(c.S("a.mov"), 11, 7, 300);
            WriteBig(c.S("b.mov"), 11, 7, 300);
            c.Run();
            True(File.Exists(c.D("a.mov")) && File.Exists(c.D("b.mov")), "両方コピー済み");
            Dictionary<string, string> srcBefore = Snapshot(c.Src);
            c.Job.Dedup = DedupMode.Ask;
            DupResult d = DupChecker.Run(c.Job, c.App, c.State, CancellationToken.None);
            Eq(1, d.LargePending, "重複チェックで見つける");
            DedupPendingItem w = DedupStore.Get(c.Job.Id).Pending[0];
            True(w.Copied && w.Origin == "check", "コピー済みとして出す");
            Eq<string>(null, LargeDup.Resolve(c.Job, c.App, c.State, w, "text"), "テキストにする");
            True(!File.Exists(c.D(w.Rel)) && File.Exists(c.D(w.Rel + AppPaths.PlaceholderSuffix)), "コピー先はテキストに");
            List<TrashStore.Batch> tb = TrashStore.List(c.Dst);
            True(tb.Count == 1 && tb[0].FileCount == 1, "コピー済みのものは削除済みへ（元に戻せる）");
            CompareSnapshots(srcBefore, Snapshot(c.Src));   // コピー元は変えない
            AppCore.SaveState(c.Job.Id);
            RunResult rc = c.Run();
            Eq(0, rc.Copied + rc.DedupAsked, "次のコピーでも戻さない");
            DupResult d2 = DupChecker.Run(c.Job, c.App, c.State, CancellationToken.None);
            Eq(0, d2.LargePending, "選んだものは出さない");
        }

        private static string FirstOf(Box b)
        {
            DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            DedupBook book = DedupStore.Get(b.Job.Id);
            lock (book) return book.Pending.Count > 0 ? book.Pending[0].Target : null;
        }

        private static void T59_DedupFirst()
        {
            // ② パス名が短いもの（まだどれもコピーしていない）
            Box a = new Box("T59a");
            a.Job.Dedup = DedupMode.Ask;
            WriteBig(a.S("とても長いフォルダ名\\x.bin"), 11, 1, 300);
            WriteBig(a.S("短\\長いファイル名です.bin"), 11, 1, 300);
            WriteBig(a.S("aa\\b.bin"), 11, 1, 300);
            RunResult ra = a.Run();
            True(File.Exists(a.D("aa\\b.bin")), "パス名がいちばん短いものをコピー");
            Eq(1, ra.Copied, "1つだけコピー");
            Eq("aa\\b.bin", FirstOf(a), "1つ目はパス名が短いもの");

            // ③ パス名の長さが同じなら、ファイル名が短いもの
            Box n = new Box("T59n");
            WriteBig(n.S("xy\\ab.bin"), 11, 2, 300);    // パス 9文字・ファイル名 6文字
            WriteBig(n.S("x\\abc.bin"), 11, 2, 300);    // パス 9文字・ファイル名 7文字
            n.Run();
            True(File.Exists(n.D("xy\\ab.bin")) && !File.Exists(n.D("x\\abc.bin")), "ファイル名が短いものをコピー");

            // ① 先にコピーしたもの（パス名が長くても）
            Box c = new Box("T59c");
            WriteBig(c.S("とても長いフォルダ名\\先にあった.bin"), 11, 3, 300);
            c.Run();                                         // 1つしかないので普通にコピー
            System.Threading.Thread.Sleep(30);
            WriteBig(c.S("z.bin"), 11, 3, 300);              // 後から同じ中身・短いパス
            RunResult rc = c.Run();
            Eq(0, rc.Copied, "後から来た短いパスはコピーしない");
            Eq(1, rc.DedupAsked, "扱いを選ぶ");
            Eq("とても長いフォルダ名\\先にあった.bin", FirstOf(c), "1つ目は先にコピーしたもの");

            // ① 両方コピー済みなら、初めてコピーした日時が早いもの（パス名が長くても）
            Box d = new Box("T59d");
            d.Job.Dedup = DedupMode.Off;
            WriteBig(d.S("長い長いフォルダ\\最初.bin"), 11, 4, 300);
            d.Run();
            System.Threading.Thread.Sleep(30);
            WriteBig(d.S("q.bin"), 11, 4, 300);
            d.Run();                                         // 使わない設定なので両方コピー
            True(File.Exists(d.D("q.bin")) && File.Exists(d.D("長い長いフォルダ\\最初.bin")), "両方コピー済み");
            // 上書きしても「初めてコピーした日時」は変わらない
            long first; lock (d.State) first = d.State.Files["長い長いフォルダ\\最初.bin"].CopiedAt;
            True(first > 0, "初めてコピーした日時を記録");
            d.Job.Dedup = DedupMode.Ask;
            Eq("長い長いフォルダ\\最初.bin", FirstOf(d), "1つ目は先にコピーしたもの");
            DedupPendingItem p = DedupStore.Get(d.Job.Id).Pending[0];
            Eq("q.bin", p.Rel, "選ぶのは後からコピーした方");
            True(p.Copied, "コピー済み");

            // 記録が保存・読み直しで消えない
            AppCore.SaveState(d.Job.Id);
            JobState re = JobState.Load(d.Job.Id);
            Eq(first, re.Files["長い長いフォルダ\\最初.bin"].CopiedAt, "保存して読み直しても同じ");
        }

        private static void T60_SyncDeleteCandidates()
        {
            SyncPair p = new SyncPair("T60");
            WriteFile(Path.Combine(p.A, "d.txt"), "消すファイル", 600);
            WriteFile(Path.Combine(p.A, "e.txt"), "残すファイル", 600);
            WriteFile(Path.Combine(p.A, "f.txt"), "変わるファイル", 600);
            p.Run(p.JA, "pc1");
            p.Run(p.JB, "pc2");
            True(File.Exists(Path.Combine(p.B, "d.txt")), "PC2 に d");

            // PC1 で d・e を削除 → すぐには消さず、共有フォルダから消すかの候補にする
            File.Delete(Path.Combine(p.A, "d.txt"));
            File.Delete(Path.Combine(p.A, "e.txt"));
            RunResult r1 = p.Run(p.JA, "pc1");
            Eq(2, r1.SyncDeleteCandidates, "削除候補 2件");
            Eq(2, r1.SyncDeleteNew, "新しく見つかった 2件");
            True(File.Exists(Path.Combine(p.S, "d.txt")) && File.Exists(Path.Combine(p.S, "e.txt")), "確認するまで共有フォルダには残る");
            List<SyncDeleteItem> items = SyncDeleteStore.GetItems(p.JA.Id);
            Eq(2, items.Count, "保存された候補");
            True(items.TrueForAll(delegate(SyncDeleteItem x) { return x.Side == SyncDeleteItem.SideShared; }), "このPCで消したもの");
            RunResult r1b = p.Run(p.JA, "pc1");
            Eq(0, r1b.SyncDeleteNew, "2回目は新しい候補なし（見つけた日時も引き継ぐ）");
            List<AppCore.AttentionItem> att = AppCore.AttentionItems();
            True(att.Exists(delegate(AppCore.AttentionItem x) { return x.Action == "synccand" && x.JobId == p.JA.Id; }), "確認が必要なものに出る");

            // 予測でも「削除の確認」と出る（何も変えない）
            RunResult pv = AppCore.DryRun(p.JA, false);
            True(pv.Plan.Exists(delegate(PlanItem x) { return x.Kind == PlanKind.SyncDelete && x.Rel == "d.txt"; }), "予測に削除の確認");

            // d は削除する、e は残す
            SyncDeleteItem d = items.Find(delegate(SyncDeleteItem x) { return x.Rel == "d.txt"; });
            SyncDeleteItem e = items.Find(delegate(SyncDeleteItem x) { return x.Rel == "e.txt"; });
            Eq(0, AppCore.ResolveSyncDeletes(p.JA, new List<SyncDeleteItem>(new SyncDeleteItem[] { d }), true).Count, "削除できる");
            True(!File.Exists(Path.Combine(p.S, "d.txt")), "共有フォルダから外れた");
            True(Directory.GetFiles(Path.Combine(p.S, "_AutoCopy\\削除済み"), "d.txt", SearchOption.AllDirectories).Length == 1, "共有フォルダの「削除済み」に移った（戻せる）");
            Eq(0, AppCore.ResolveSyncDeletes(p.JA, new List<SyncDeleteItem>(new SyncDeleteItem[] { e }), false).Count, "残すを選べる");
            Eq(0, SyncDeleteStore.Count(p.JA.Id), "候補はなくなる");
            Eq("残すファイル", Read(Path.Combine(p.A, "e.txt")), "残すにした e は、すぐにこのPCへ戻す（取り込まない設定でも戻る）");
            RunResult r2 = p.Run(p.JA, "pc1");
            Eq(0, r2.Downloaded + r2.Copied, "戻した e はその後は何もしない");
            Eq(0, r2.SyncDeleteCandidates, "d は候補に出ない");

            // PC2：共有フォルダで消えた d → PC2 からも消すかの候補
            RunResult r3 = p.Run(p.JB, "pc2");
            Eq(1, r3.SyncDeleteCandidates, "PC2 の候補");
            SyncDeleteItem dB = SyncDeleteStore.GetItems(p.JB.Id)[0];
            Eq(SyncDeleteItem.SideLocal, dB.Side, "共有フォルダで消えたもの");
            True(File.Exists(Path.Combine(p.B, "d.txt")), "確認するまで PC2 には残る");
            Eq(0, AppCore.ResolveSyncDeletes(p.JB, new List<SyncDeleteItem>(new SyncDeleteItem[] { dB }), true).Count, "PC2 から外す");
            True(!File.Exists(Path.Combine(p.B, "d.txt")), "PC2 から外れた");
            string[] bk = Directory.GetFiles(Path.Combine(AppPaths.SyncBackupDir, p.JB.Id), "d.txt", SearchOption.AllDirectories);
            True(bk.Length == 1 && Read(bk[0]) == "消すファイル", "PC2 の分は控えに残る（消さない）");
            RunResult r4 = p.Run(p.JB, "pc2");
            Eq(0, r4.SyncDeleteCandidates + r4.Copied + r4.Downloaded, "その後は何もしない");

            // 見つけた後で変更されたものは消さない
            File.Delete(Path.Combine(p.S, "f.txt"));
            RunResult r5 = p.Run(p.JA, "pc1");
            Eq(1, r5.SyncDeleteCandidates, "f の候補");
            SyncDeleteItem f = SyncDeleteStore.GetItems(p.JA.Id)[0];
            WriteFile(Path.Combine(p.A, "f.txt"), "変わるファイル（変更した）", 90);
            List<string> errs = AppCore.ResolveSyncDeletes(p.JA, new List<SyncDeleteItem>(new SyncDeleteItem[] { f }), true);
            Eq(1, errs.Count, "変更されたものは消さない");
            True(File.Exists(Path.Combine(p.A, "f.txt")), "f は残る");
            RunResult r6 = p.Run(p.JA, "pc1");
            Eq(1, r6.Copied, "変更された f は共有フォルダへコピーし直す");

            // 同期でないフォルダへは、削除の手続きでも書き込めない
            Throws<SourceProtectedException>(delegate { SafeFs.MoveOutOfSync(Path.Combine(baseDir, "T60x\\x.txt"), "x.txt", Path.Combine(baseDir, "T60bk")); }, "同期のフォルダ以外は拒否");
        }

        private static void T61_DupManyRenames()
        {
            Box b = new Box("T61");
            b.Job.FollowRename = false;   // 名前変更の追従をしない＝重複チェックで見つける
            b.Job.DupShowOrphans = true;
            const int dirs = 30, per = 100;
            for (int d = 0; d < dirs; d++)
                for (int i = 0; i < per; i++) b.W("d" + d.ToString("00") + "\\file" + i.ToString("000") + ".txt", "中身 " + d + "-" + i + new string('x', (d * per + i) % 97), 600);
            b.Run();
            for (int d = 0; d < dirs; d++)
                for (int i = 0; i < per; i++)
                {
                    string rel = "d" + d.ToString("00") + "\\file" + i.ToString("000") + ".txt";
                    File.Move(b.S(rel), b.S("d" + d.ToString("00") + "\\renamed" + i.ToString("000") + ".txt"));
                }
            b.Run();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            DupResult r = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            sw.Stop();
            int renameGroups = 0, oldEntries = 0;
            foreach (DupGroup g in r.Groups)
                if (g.Kind == "rename") { renameGroups++; foreach (DupEntry e in g.Entries) if (e.Status == "old") oldEntries++; }
            Eq(dirs * per, oldEntries, "古い名前 " + (dirs * per) + "件をすべて見つける");
            Eq(dirs, renameGroups, "フォルダごとに1つのまとまり（1件ずつのまとまりにしない）");
            True(sw.Elapsed.TotalSeconds < 60, "時間 " + sw.Elapsed.TotalSeconds.ToString("0.0") + "秒");
            Console.WriteLine("    重複チェック " + (dirs * per) + "件の名前変更：" + sw.Elapsed.TotalSeconds.ToString("0.0") + "秒・まとまり " + r.Groups.Count + "件");
            DupGroup one = r.Groups.Find(delegate(DupGroup g) { return g.Kind == "rename"; });
            True(one.Title.Contains(per + "件"), "見出しに件数: " + one.Title);
            Eq(per * 2, one.Entries.Count, "古い名前と今の名前");
        }

        private static void T62_NoticeStates()
        {
            Box b = new Box("T62");
            b.W("a.txt", "a");
            b.Run();
            string id = b.Job.Id;
            Predicate<Notice> mine = delegate(Notice n) { return n.JobId == id; };

            // 上書きの確認：確認待ちが残っている間は未対応、なくなれば自動で対応済み
            ConflictItem c = new ConflictItem(); c.Rel = "a.txt"; c.Reason = "newer";
            lock (b.State) { b.State.Conflicts.Add(c); }
            AppCore.Notify(NoticeLevel.Warning, "上書きの確認が 1件 あります", "T62", "conflicts", id);
            AppCore.RefreshNoticeStates();
            Notice nc = AppCore.RecentNotices().Find(delegate(Notice n) { return n.JobId == id && n.Action == "conflicts"; });
            True(nc != null && !nc.Handled && nc.StateText == "未対応", "確認待ちがある間は未対応");
            lock (b.State) { b.State.Conflicts.Clear(); }
            AppCore.RefreshNoticeStates();
            True(nc.Handled && nc.HandledNote == NoticeNotes.Seen && nc.StateText == "対応済み", "確認が済めば自動で対応済み");

            // ただのお知らせは最初から「対応不要」
            AppCore.Notify(NoticeLevel.Ok, "T62：コピーしました", "1件", "log", id);
            Notice ok = AppCore.RecentNotices().Find(delegate(Notice n) { return n.JobId == id && n.Level == NoticeLevel.Ok; });
            True(ok.Handled && ok.StateText == "対応不要", "対応不要");

            // エラー：開いて確認したら対応済み、同じエラーが続いても1件にまとめて点滅させない
            JobRuntime rt = AppCore.GetRuntime(id);
            rt.LastResult = "error"; rt.LastMessage = "接続できません"; rt.LastRun = DateTime.Now;
            AppCore.Notify(NoticeLevel.Error, b.Job.Name + "：エラー", "接続できません", "retry", id);
            True(AppCore.AttentionItems().Exists(delegate(AppCore.AttentionItem x) { return x.IsError && x.JobId == id; }), "未対応のエラーは確認が必要");
            AppCore.AckErrors(id);
            True(!AppCore.AttentionItems().Exists(delegate(AppCore.AttentionItem x) { return x.IsError && x.JobId == id; }), "確認したら外れる");
            AppCore.Notify(NoticeLevel.Error, b.Job.Name + "：エラー", "接続できません", "retry", id);
            List<Notice> errs = AppCore.RecentNotices().FindAll(delegate(Notice n) { return n.JobId == id && n.Level == NoticeLevel.Error; });
            Eq(1, errs.Count, "同じエラーは1件にまとめる");
            Eq(2, errs[0].Count, "回数");
            True(errs[0].Handled, "確認済みのまま（また点滅しない）");

            // 別のエラー → その後正常に実行できたら自動で対応済み
            AppCore.Notify(NoticeLevel.Error, b.Job.Name + "：エラー", "別の原因", "retry", id);
            Notice e2 = AppCore.RecentNotices().Find(delegate(Notice n) { return n.JobId == id && n.Text == "別の原因"; });
            True(!e2.Handled, "新しいエラーは未対応");
            System.Threading.Thread.Sleep(20);
            rt.LastResult = "ok"; rt.LastRun = DateTime.Now;
            AppCore.RefreshNoticeStates();
            True(e2.Handled && e2.HandledNote == NoticeNotes.Recovered, "その後正常に実行できた");

            // 手で未対応に戻す → 自動では対応済みにしない／手で対応済みにする
            AppCore.SetNoticeHandled(new Notice[] { nc }, false);
            AppCore.RefreshNoticeStates();
            True(!nc.Handled, "手で戻したものは未対応のまま");
            AppCore.SetNoticeHandled(new Notice[] { nc }, true);
            True(nc.Handled && nc.HandledNote == NoticeNotes.Manual, "手で対応済み");

            // 保存される
            True(File.Exists(AppCore.NoticePath), "お知らせを保存");
            NoticeBook saved = Json.Load<NoticeBook>(AppCore.NoticePath);
            True(saved.Items.Exists(delegate(Notice n) { return n.Id == nc.Id && n.Handled; }), "対応済みの状態も保存");
            int before = AppCore.RecentNotices().FindAll(mine).Count;
            int removed = AppCore.ClearHandledNotices();
            True(removed >= 3 && AppCore.RecentNotices().FindAll(mine).Count < before, "対応済みのものを消せる");
        }

        private static void T63_SyncTakeNew()
        {
            JobConfig def = Json.Deserialize<JobConfig>("{\"Name\":\"v19\",\"Sync\":true}");
            True(def.SyncTakeNew && new JobConfig().SyncTakeNew, "共有フォルダにだけあるファイルは、初期値で「取り込む」（前の版の設定を読んだときも）");
            True(Json.Deserialize<JobConfig>("{\"Name\":\"v112\",\"Sync\":true,\"SyncTakeNew\":false}").SyncTakeNew, "1.10〜1.12 の「取り込まない」（初期値）は「取り込む」に戻す");
            True(!def.SyncPushNew && !new JobConfig().SyncPushNew, "このPCにだけあるファイルも、初期値は「共有フォルダにコピーしない」");
            True(new JobConfig().RunAtStartup && def.RunAtStartup, "ログオン時にも実行する が初期値");

            SyncPair p = new SyncPair("T63");
            p.JA.SyncTakeNew = false;   // PC1 は「コピーしない」
            WriteFile(Path.Combine(p.A, "共通.txt"), "両方にある", 600);
            p.Run(p.JA, "pc1");
            p.Run(p.JB, "pc2");
            True(File.Exists(Path.Combine(p.B, "共通.txt")), "取り込む設定の PC2 には入る");

            WriteFile(Path.Combine(p.B, "PC2だけ.txt"), "PC2が追加", 600);
            p.Run(p.JB, "pc2");
            True(File.Exists(Path.Combine(p.S, "PC2だけ.txt")), "共有フォルダへはコピーする");
            RunResult r1 = p.Run(p.JA, "pc1");
            Eq(0, r1.Downloaded, "共有フォルダにだけあるファイルはコピーしない");
            Eq(1, r1.SyncNotTaken, "取り込まなかった件数");
            True(!File.Exists(Path.Combine(p.A, "PC2だけ.txt")), "PC1 には入らない");
            Eq(0, r1.SyncDeleteCandidates, "削除の候補にもしない");
            RunResult pv = AppCore.DryRun(p.JA, false);
            True(pv.Plan.Exists(delegate(PlanItem x) { return x.Kind == PlanKind.SyncNotTaken && x.Rel == "PC2だけ.txt"; }), "予測に「取り込まない」");

            // 両方にあるファイルの更新は、これまでどおり取り込む
            WriteFile(Path.Combine(p.B, "共通.txt"), "PC2が更新した", 100);
            p.Run(p.JB, "pc2");
            RunResult r2 = p.Run(p.JA, "pc1");
            Eq(1, r2.Downloaded, "両方にあるファイルの更新は取り込む");
            Eq("PC2が更新した", Read(Path.Combine(p.A, "共通.txt")), "新しい版");

            // 設定で「取り込む」にすると入る
            p.JA.SyncTakeNew = true;
            RunResult r3 = p.Run(p.JA, "pc1");
            Eq(1, r3.Downloaded, "取り込む設定にすると入る");
            Eq("PC2が追加", Read(Path.Combine(p.A, "PC2だけ.txt")), "中身");

            // このPCにだけあるファイル：初期値では共有フォルダへコピーしない（設定でコピーできる）
            p.JA.SyncPushNew = false;
            WriteFile(Path.Combine(p.A, "PC1だけ.txt"), "PC1が追加", 600);
            RunResult r4 = p.Run(p.JA, "pc1");
            Eq(0, r4.Copied, "このPCにだけあるファイルは共有フォルダへコピーしない");
            Eq(1, r4.SyncNotPushed, "送らなかった件数");
            True(!File.Exists(Path.Combine(p.S, "PC1だけ.txt")), "共有フォルダにはない");
            Eq(0, r4.SyncDeleteCandidates, "削除の候補にもしない");
            RunResult pv2 = AppCore.DryRun(p.JA, false);
            True(pv2.Plan.Exists(delegate(PlanItem x) { return x.Kind == PlanKind.SyncNotPushed && x.Rel == "PC1だけ.txt"; }), "予測に「共有へ送らない」");
            WriteFile(Path.Combine(p.A, "共通.txt"), "PC1が更新した", 50);
            RunResult r5 = p.Run(p.JA, "pc1");
            Eq(1, r5.Overwritten, "両方にあるファイルの更新は共有フォルダへ伝える");
            p.JA.SyncPushNew = true;
            RunResult r6 = p.Run(p.JA, "pc1");
            Eq(1, r6.Copied, "コピーする設定にすると共有フォルダへ");
            Eq("PC1が追加", Read(Path.Combine(p.S, "PC1だけ.txt")), "中身");

            // 共有フォルダで消えたファイルを「残す」：送らない設定でも、その場で共有フォルダへ戻す
            p.JA.SyncPushNew = false;
            File.Delete(Path.Combine(p.S, "PC1だけ.txt"));
            RunResult r7 = p.Run(p.JA, "pc1");
            Eq(1, r7.SyncDeleteCandidates, "共有フォルダで消えた → 削除の確認");
            SyncDeleteItem keep = SyncDeleteStore.GetItems(p.JA.Id).Find(delegate(SyncDeleteItem x) { return x.Rel == "PC1だけ.txt"; });
            Eq(0, AppCore.ResolveSyncDeletes(p.JA, new List<SyncDeleteItem>(new SyncDeleteItem[] { keep }), false).Count, "残す");
            Eq("PC1が追加", Read(Path.Combine(p.S, "PC1だけ.txt")), "すぐに共有フォルダへ戻った");
            RunResult r8 = p.Run(p.JA, "pc1");
            Eq(0, r8.Copied + r8.SyncDeleteCandidates + r8.SyncNotPushed, "その後は何もしない");
        }

        private static void T64_Schedule()
        {
            JobConfig j = new JobConfig();
            j.DupTime = "12:00";
            DateTime now = new DateTime(2026, 11, 5, 9, 0, 0);   // 木曜

            j.DupSchedule = DupScheduleKind.Daily;
            Eq(new DateTime(2026, 11, 4, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎日：今日の時刻前なら昨日");
            j.DupSchedule = DupScheduleKind.Weekly; j.DupWeekday = 1;
            Eq(new DateTime(2026, 11, 2, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎週月曜");
            j.DupSchedule = DupScheduleKind.Monthly; j.DupDay = 10;
            Eq(new DateTime(2026, 10, 10, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎月10日：今月はまだ → 先月");
            j.DupDay = 5;
            Eq(new DateTime(2026, 10, 5, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎月5日：今日の12時前 → 先月");
            Eq(new DateTime(2026, 11, 5, 12, 0, 0), AppCore.PrevDupTime(j, now.AddHours(4)), "毎月5日：今日の12時を過ぎたら今日");
            j.DupDay = DupScheduleKind.LastDay;
            Eq(new DateTime(2026, 10, 31, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎月末日");
            Eq(new DateTime(2026, 2, 28, 12, 0, 0), AppCore.PrevDupTime(j, new DateTime(2026, 3, 15)), "2月の末日");
            j.DupSchedule = DupScheduleKind.Yearly; j.DupMonth = 4; j.DupDay = 1;
            Eq(new DateTime(2026, 4, 1, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎年4月1日");
            j.DupMonth = 12;
            Eq(new DateTime(2025, 12, 1, 12, 0, 0), AppCore.PrevDupTime(j, now), "毎年12月1日：今年はまだ → 去年");

            // 止まっている間に過ぎた予定は、起動後に実行する（取りこぼさない）
            Box b = new Box("T64");
            b.Job.DupCheckEnabled = true;
            b.Job.DupSchedule = DupScheduleKind.Monthly; b.Job.DupDay = 1; b.Job.DupTime = "12:00";
            JobRuntime rt = new JobRuntime();
            DateTime t = new DateTime(2026, 11, 20, 8, 0, 0);
            lock (b.State) b.State.LastDupCheckTicks = new DateTime(2026, 10, 1, 12, 5, 0).ToUniversalTime().Ticks;   // 10/1 に実行済み、11/1 は止まっていた
            string note;
            True(AppCore.DupDue(b.Job, rt, t, out note), "11/1 の予定を過ぎていて未実行 → 起動後に実行");
            True(note != null && note.Contains("11/1"), "取りこぼしの記録: " + note);
            lock (b.State) b.State.LastDupCheckTicks = new DateTime(2026, 11, 20, 8, 1, 0).ToUniversalTime().Ticks;
            True(!AppCore.DupDue(b.Job, rt, t.AddMinutes(5), out note), "実行した後は次の予定（12/1）まで待つ");
            True(AppCore.DupDue(b.Job, rt, new DateTime(2026, 12, 1, 12, 0, 0), out note), "12/1 12:00 に実行");
            rt.StartGate = new DateTime(2026, 12, 1, 12, 1, 0);
            True(!AppCore.DupDue(b.Job, rt, new DateTime(2026, 12, 1, 12, 0, 30), out note), "起動直後の待ち時間の間は実行しない");

            // ログオン時（アプリの起動時）：起動するたびに1回
            b.Job.DupSchedule = DupScheduleKind.Startup;
            JobRuntime rt2 = new JobRuntime();
            True(AppCore.DupDue(b.Job, rt2, DateTime.Now, out note), "起動したら実行");
            rt2.DupStartupDone = true;
            True(!AppCore.DupDue(b.Job, rt2, DateTime.Now, out note), "同じ起動の間は1回だけ");
            b.Job.DupSchedule = DupScheduleKind.Manual;
            True(!AppCore.DupDue(b.Job, new JobRuntime(), DateTime.Now, out note), "手動のみは自動で実行しない");

            // コピー：ログオン時にも実行する（初期値）／しない場合は、予定を過ぎていたときだけ起動後に実行
            DateTime gate = new DateTime(2026, 11, 5, 9, 1, 0);
            JobConfig c = new JobConfig(); c.IntervalMinutes = 240;
            string cu;
            Eq(gate, AppCore.FirstRunAfterStart(c, gate.AddHours(-1), gate, true, out cu), "ログオン時にも実行：起動後すぐ");
            c.RunAtStartup = false;
            Eq(gate.AddHours(-1).AddMinutes(240), AppCore.FirstRunAfterStart(c, gate.AddHours(-1), gate, true, out cu), "予定前：前回＋間隔まで待つ");
            True(cu == null, "取りこぼしではない");
            Eq(gate, AppCore.FirstRunAfterStart(c, gate.AddHours(-9), gate, true, out cu), "止まっている間に予定が過ぎた：起動後すぐ");
            True(cu != null, "取りこぼしの記録");
            Eq(gate, AppCore.FirstRunAfterStart(c, DateTime.MinValue, gate, true, out cu), "一度も実行していない：起動後すぐ");
            c.StartupOnly = true;
            Eq(gate, AppCore.FirstRunAfterStart(c, gate.AddHours(-1), gate, true, out cu), "ログオン時のみ：起動後すぐ（予定の時刻を待たない）");
            True(!Json.Deserialize<JobConfig>("{\"Name\":\"v110\"}").StartupOnly, "ログオン時のみ は初期値ではない（前の版の設定も）");
        }

        private static void T65_FolderDup()
        {
            string root = Path.Combine(baseDir, "T65");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            WriteFile(Path.Combine(root, "a.txt"), "同じ中身です", 600);
            WriteFile(Path.Combine(root, "sub\\a のコピー.txt"), "同じ中身です", 300);
            WriteFile(Path.Combine(root, "sub\\深い\\a.txt"), "同じ中身です", 100);
            WriteFile(Path.Combine(root, "b.txt"), "同じ大きさ違う", 600);   // 大きさは同じでも中身が違う
            WriteFile(Path.Combine(root, "c.txt"), "同じ大きさ違ウ", 600);
            string big = new string('x', 300000);
            WriteFile(Path.Combine(root, "大\\1.bin"), big + "1", 600);
            WriteFile(Path.Combine(root, "大\\2.bin"), big + "1", 600);
            WriteFile(Path.Combine(root, "大\\3.bin"), big + "2", 600);   // 先頭と末尾の一部だけ違う
            Dictionary<string, string> before = Snapshot(root);

            FolderDupResult r = FolderDup.Find(root, true, 0, CancellationToken.None, null);
            Eq(2, r.Groups.Count, "中身が同じまとまり 2つ（大きさが同じだけのものは入れない）");
            FolderDupGroup g = r.Groups.Find(delegate(FolderDupGroup x) { return x.Files.Count == 3; });
            True(g != null, "3つの同じファイル");
            Eq("a.txt", g.Files[0], "残す候補はパスが短いもの");
            True(r.Groups.Exists(delegate(FolderDupGroup x) { return x.Files.Count == 2 && x.Files.Contains("大\\1.bin") && x.Files.Contains("大\\2.bin"); }), "大きいファイルは全体で比べる");
            Eq(3, r.WastedFiles, "1つずつ残すと消せる件数");
            Eq(before.Count, Snapshot(root).Count, "探すだけでは何も変えない");
            foreach (KeyValuePair<string, string> kv in before) Eq(kv.Value, Snapshot(root)[kv.Key], "中身も変えない " + kv.Key);

            FolderDupResult r2 = FolderDup.Find(root, false, 0, CancellationToken.None, null);
            Eq(0, r2.Groups.Count, "サブフォルダを含めない");
            FolderDupResult r3 = FolderDup.Find(root, true, 100 * 1024, CancellationToken.None, null);
            Eq(1, r3.Groups.Count, "大きさの下限");

            // 移す：消さずに「_AutoCopy\重複で削除」へ
            SafeFs.SetProtectedSources(new string[0]);
            int moved;
            List<string> errs = FolderDup.MoveAway(root, new List<string>(new string[] { "sub\\a のコピー.txt" }), out moved);
            Eq(0, errs.Count, "移せる");
            Eq(1, moved, "1件");
            True(!File.Exists(Path.Combine(root, "sub\\a のコピー.txt")), "元の場所からなくなる");
            True(Directory.GetFiles(FolderDup.TrashFolder(root), "a のコピー.txt", SearchOption.AllDirectories).Length == 1, "重複で削除のフォルダに残る（戻せる）");
            FolderDupResult r4 = FolderDup.Find(root, true, 0, CancellationToken.None, null);
            FolderDupGroup g4 = r4.Groups.Find(delegate(FolderDupGroup x) { return x.Files.Contains("a.txt"); });
            Eq(2, g4.Files.Count, "移したもの（_AutoCopy の中）は次の検索に出ない");

            // 自動コピーのコピー元のファイルは移さない
            SafeFs.SetProtectedSources(new string[] { Path.Combine(root, "大") });
            errs = FolderDup.MoveAway(root, new List<string>(new string[] { "大\\2.bin" }), out moved);
            Eq(1, errs.Count, "コピー元のファイルは移さない");
            True(File.Exists(Path.Combine(root, "大\\2.bin")), "コピー元のファイルは残る");
            True(SafeFs.IsProtected(Path.Combine(root, "大\\x")) && !SafeFs.IsProtected(Path.Combine(root, "sub")), "コピー元かどうか");
            SafeFs.SetProtectedSources(new string[0]);
        }

        private static void T66_SelectCriteria()
        {
            SelectRow a = new SelectRow(); a.Path = "営業\\提案書.pptx"; a.Size = 3 * 1024 * 1024; a.Mtime = new DateTime(2025, 4, 1, 9, 0, 0).ToUniversalTime().Ticks; a.Tags.Add("重複");
            SelectRow b = new SelectRow(); b.Path = "個人\\山田\\提案書.pptx"; b.Size = 3 * 1024 * 1024; b.Mtime = new DateTime(2026, 10, 1, 9, 0, 0).ToUniversalTime().Ticks; b.Tags.Add("残す候補");
            SelectRow c = new SelectRow(); c.Path = "一時\\作業.tmp"; c.Size = 2000; c.Mtime = new DateTime(2026, 11, 1).ToUniversalTime().Ticks; c.Tags.Add("重複");
            SelectCriteria k = new SelectCriteria();
            True(k.IsEmpty && k.Matches(a) && k.Matches(c), "条件なしはすべて");
            k.Words = SelectCriteria.Split("個人; 一時");
            True(!k.Matches(a) && k.Matches(b) && k.Matches(c), "名前・場所に含む（どれか1つ）");
            k.WordsNot = true;
            True(k.Matches(a) && !k.Matches(b), "含まない");
            k = new SelectCriteria(); k.Exts.Add(".TMP");
            True(k.Matches(c) && !k.Matches(a), "拡張子（大文字小文字は区別しない）");
            k = new SelectCriteria(); k.MinSize = 1024 * 1024;
            True(k.Matches(a) && !k.Matches(c), "大きさ以上");
            k = new SelectCriteria(); k.Before = new DateTime(2026, 1, 1);
            True(k.Matches(a) && !k.Matches(b), "更新日時より前");
            k = new SelectCriteria(); k.After = new DateTime(2026, 10, 15);
            True(k.Matches(c) && !k.Matches(b), "更新日時以降");
            k = new SelectCriteria(); k.Tags = new HashSet<string>(new string[] { "残す候補" });
            True(k.Matches(b) && !k.Matches(a), "種類");

            List<SelectRow> rows = new List<SelectRow>(new SelectRow[] { b, a });
            Eq(1, KeepRule.Pick(rows, KeepRule.ShortPath, null), "パスが短いものを残す");
            Eq(0, KeepRule.Pick(rows, KeepRule.Newest, null), "新しいものを残す");
            Eq(1, KeepRule.Pick(rows, KeepRule.Oldest, null), "古いものを残す");
            SelectCriteria m = new SelectCriteria(); m.Words.Add("個人");
            Eq(0, KeepRule.Pick(rows, KeepRule.Matching, m), "条件に合うもの（場所）を残す");
        }

        private static void T42_Performance()
        {
            Box b = new Box("T42");
            for (int i = 0; i < 2000; i++) b.W("d" + (i % 20) + "\\f" + i + ".txt", "content " + i);
            Stopwatch sw = Stopwatch.StartNew();
            RunResult r1 = b.Run();
            long t1 = sw.ElapsedMilliseconds;
            sw.Restart();
            RunResult r2 = b.Run();
            long t2 = sw.ElapsedMilliseconds;
            Eq(2000, r1.Copied, "初回コピー件数");
            Eq(0, r2.Copied + r2.Overwritten, "2回目は0件");
            True(t2 < 5000, "2回目（変更なし）は5秒以内: " + t2 + "ms");
            Note = "初回 " + t1 + "ms／2回目 " + t2 + "ms";
        }
    }
}
