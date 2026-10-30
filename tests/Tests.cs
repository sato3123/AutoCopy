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
                Distribution.Apply(target, back);
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
