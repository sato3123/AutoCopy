using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoCopy.Tests
{
    internal static partial class TestMain
    {
        private static void RegisterRevisionTests()
        {
            Add("T85", "共有コピー先・親子コピー先：別ジョブの元ファイルを重複整理しない", SharedDestinations);
            Add("T86", "対象フォルダと拡張子の限定・保存互換・対象外の既存コピーを維持", TargetScope);
            Add("T87", "除外フォルダ内でもワイルドカードの例外を適用", IncludeDescendants);
            Add("T88", "フォルダ重複の確認後に増えたファイルを保護", StaleFolder);
            Add("T89", "大容量重複の確認後に変わったファイルを保護", StaleLarge);
            Add("T90", "確認操作中は重複チェック・同じ場所の別ジョブを実行しない", ResolveGate);
            Add("T91", "停止完了を待ち、ジョブを再開できる", DrainJob);
            Add("T92", "設定ファイル破損時にバックアップ復元", RecoverSettings);
            Add("T93", "関連ファイル更新失敗で本体も元に戻す", UpdateTransaction);
            Add("T94", "ミラー・重複削除の失敗を保持し成功のみ報告", DeleteFailures);
            Add("T95", "コピー再試行後の更新日時とサイズが実データに一致", RetryMetadata);
            Add("T96", "単一巨大フォルダ走査とハッシュ計算の中止", CancelWork);
            Add("T97", "復元名が複数衝突しても上書きせず復元", RestoreNames);
            Add("T98", "ログ送信成功後の再送で二重記録しない", LogRetry);
            Add("T99", "設定保存失敗で使用中の設定を戻す", SettingsTransaction);
            Add("T100", "予約名と配布設定の異常値を拒否", InputValidation);
            Add("T101", "別ジョブの元が読めない場合は共有先を保護", UnavailableOtherSource);
            Add("T102", "ミラー削除承認時に元が切断されていれば保留を維持", OfflineApproval);
            Add("T103", "ネットワーク用一覧キャッシュの正確性と取得回数", InventoryTest);
            Add("T104", "実行間隔の変更を次回実行予定に反映", Reschedule);
            Add("T105", "大容量重複の確認済み：永続化・通常コピー・内容変更・解除", SkipReviewedLarge);
            Add("T106", "重複チェック：除外しているだけでコピー元にあるファイルを「古いコピー」にしない", DupKeepsExcluded);
            Add("T107", "例外ルールがあっても、隠し・システムのフォルダには入らない（名前で除外したフォルダには入る）", HiddenDirWithIncludes);
            Add("T108", "同期：除外・対象を変えたら共有フォルダの一覧を使い直さない（削除の候補にしない）", SyncCacheRules);
            Add("T109", "別ジョブが同期なら、その共有フォルダはまるごと整理の対象にしない", SyncSharedProtected);
            Add("T110", "ログ送信：途中で終了しても二重・途中までにしない・保管先のファイルを書き直さない", LogResume);
            Add("T111", "コピー先一覧：上限を超えたらファイルごとに確かめる", InventoryLimit);
            Add("T112", "終了時の停止待ちは上限まで（止まらない処理があっても終了できる）", StopTimeout);
        }

        private static void SkipReviewedLarge()
        {
            Box b = new Box("T105"); b.Job.Dedup = DedupMode.Ask; b.Job.DedupMinMB = 1;
            b.W("a.bin", new string('A', 1048576)); b.W("b.bin", new string('A', 1048576));
            b.Run();
            DedupBook book = DedupStore.Get(b.Job.Id);
            Eq(1, book.Pending.Count, "最初は確認待ち");
            DedupPendingItem p = book.Pending[0];
            True(!b.DE(p.Rel), "未コピーの確認待ち");
            Eq(null, LargeDup.Resolve(b.Job, b.App, b.State, p, "skip"), "確認済みにする");
            True(!b.DE(p.Rel), "確認操作ではファイルを作らない");
            DedupStore.ClearCache(); book = DedupStore.Get(b.Job.Id);
            Eq("skip", book.DecisionFor(p.Rel, p.Hash), "再起動後も記録を保持");
            b.Run(); True(b.DE(p.Rel), "次回は通常コピー");
            DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            Eq(0, book.Pending.Count, "確認済みを再通知しない");
            LargeDup.ResetSkipped(b.Job.Id);
            DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            Eq(1, book.Pending.Count, "解除すると再確認");
            p = book.Pending[0];
            Eq(null, LargeDup.Resolve(b.Job, b.App, b.State, p, "skip"), "コピー済みも確認可能");
            b.W("a.bin", new string('B', 1048577)); b.W("b.bin", new string('B', 1048577));
            DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            Eq(1, book.Pending.Count, "内容変更後は再確認");
            p = book.Pending[0];
            File.WriteAllText(b.S(p.Rel), new string('C', 1048577)); File.SetLastWriteTimeUtc(b.S(p.Rel), new DateTime(p.Mtime, DateTimeKind.Utc));
            True(LargeDup.Resolve(b.Job, b.App, b.State, p, "skip") != null, "同サイズ・同日時の変更も確認時に拒否");
            Eq(1, book.Pending.Count, "古い確認では待機項目を消さない");
        }

        private static void SharedDestinations()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                Box b = new Box("T85-" + mode);
                b.Job.Dedup = DedupMode.Off; b.Job.FollowRename = false;
                b.W("current.txt", "same"); b.Run();
                string otherSource = Path.Combine(b.Root, "other");
                string rel = mode == 1 ? @"child\old.txt" : "old.txt";
                string od = mode == 1 ? b.D("child") : mode == 2 ? b.Root : b.Dst;
                string otherRel = mode == 2 ? @"dst\old.txt" : "old.txt";
                WriteFile(Path.Combine(otherSource, otherRel), "same", 600);
                b.WD(rel, "same", 600);
                JobConfig other = new JobConfig(); other.Source = otherSource; other.Dest = od; other.Exclude = new ExcludeRules();
                b.App.Jobs.Add(other);
                DupResult r = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
                foreach (DupGroup g in r.Groups) foreach (DupEntry e in g.Entries)
                    True(!e.CanDelete || e.Rel != rel, "別ジョブのファイルを削除候補にしない: " + mode);
                True(b.DE(rel), "コピー先のファイルを維持");
                True(JobFileScope.Build(b.Job, b.App, CancellationToken.None).Protects(b.D(rel), false), "対応するファイルを保護");
            }
        }

        private static void TargetScope()
        {
            Box b = new Box("T86"); b.Job.Dedup = DedupMode.Off; b.Job.FollowRename = false;
            b.W(@"docs\a.pdf", "A"); b.W(@"docs\b.txt", "B"); b.W(@"other\a.pdf", "C");
            b.Run();
            b.Job.Exclude.OnlyFolders.Add("docs"); b.Job.Exclude.OnlyExtensions.Add(".pdf"); b.Job.Mirror = true;
            JobConfig round = b.Job.Clone();
            Eq("docs", round.Exclude.OnlyFolders[0], "保存・読み込み");
            b.W(@"docs\new.pdf", "D"); b.W(@"other\new.pdf", "E");
            b.Run();
            True(b.DE(@"docs\new.pdf") && !b.DE(@"other\new.pdf"), "対象だけ追加");
            True(b.DE(@"docs\b.txt") && b.DE(@"other\a.pdf"), "対象外の既存コピーを維持");
            ExcludeRules legacy = Json.Deserialize<ExcludeRules>("{}");
            True(new ExcludeMatcher(legacy).InScope("any.txt"), "旧設定では全対象");
        }

        private static void IncludeDescendants()
        {
            Box b = new Box("T87"); b.Job.Dedup = DedupMode.Off;
            b.Job.Exclude.Folders.Add("hidden"); b.Job.Exclude.Includes.Add("*.txt");
            b.W(@"hidden\sub\keep.txt", "keep"); b.W(@"hidden\sub\skip.bin", "skip");
            b.Run();
            True(b.DE(@"hidden\sub\keep.txt"), "除外フォルダ内でも例外をコピー");
            True(!b.DE(@"hidden\sub\skip.bin"), "例外以外は除外");
        }

        private static void StaleFolder()
        {
            Box b = new Box("T88");
            b.WD(@"old\a.txt", "A", 600); b.WD(@"new\a.txt", "A", 600);
            DupEntry old = new DupEntry(); old.Rel = "old"; old.Status = "old"; old.IsFolder = true; old.CanDelete = true;
            DupEntry current = new DupEntry(); current.Rel = "new"; current.Status = "current"; current.IsFolder = true;
            DupGroup g = new DupGroup(); g.Entries.Add(old); g.Entries.Add(current);
            DupResult result = new DupResult(); result.Groups.Add(g);
            b.WD(@"old\unique.txt", "unique", 600);
            List<string> skipped = new List<string>();
            Eq(0, DupChecker.VerifyBeforeDelete(b.Job, b.App, result, new List<DupEntry> { old }, skipped).Count, "追加されたファイルを保護");
            Eq(1, skipped.Count, "保留理由");
        }

        private static void StaleLarge()
        {
            Box b = new Box("T89"); b.W("first.bin", "AAAA"); b.W("second.bin", "AAAA"); b.WD("first.bin", "AAAA", 120); b.WD("second.bin", "AAAA", 120);
            DedupPendingItem p = new DedupPendingItem(); p.Rel = "second.bin"; p.Target = "first.bin"; p.Size = new FileInfo(b.S(p.Rel)).Length; p.Mtime = File.GetLastWriteTimeUtc(b.S(p.Rel)).Ticks;
            File.WriteAllText(b.S(p.Rel), "BBBB"); File.SetLastWriteTimeUtc(b.S(p.Rel), new DateTime(p.Mtime, DateTimeKind.Utc));
            True(LargeDup.Resolve(b.Job, b.App, b.State, p, "text") != null, "同じサイズ・日時でも内容変更を検出");
            Eq("AAAA", b.RD(p.Rel), "既存コピーを移さない");
        }

        private static void ResolveGate()
        {
            Box b = new Box("T90"); b.W("a.txt", "a");
            Eq<string>(null, AppCore.BeginResolve(b.Job), "確認処理を開始");
            try
            {
                True(!AppCore.RunJob(b.Job.Id, true, false), "コピー不可");
                True(!AppCore.RunDup(b.Job.Id), "重複チェック不可");
                True(AppCore.BeginResolve(b.Job) != null, "二重承認不可");
                JobConfig other = b.Job.Clone(); other.Id = Guid.NewGuid().ToString("N"); other.Name = "other"; b.App.Jobs.Add(other);
                True(!AppCore.RunDup(other.Id), "重なるジョブも不可");
            }
            finally { AppCore.EndResolve(b.Job); }
        }

        private static void DrainJob()
        {
            Box b = new Box("T91"); b.Job.BandwidthKBps = 64; b.Job.Dedup = DedupMode.Off;
            WriteBig(b.S("big.bin"), 2, 7, 120);
            True(AppCore.RunJob(b.Job.Id, true, false), "実行開始");
            AppCore.StopJobAndWait(b.Job.Id);
            JobRuntime rt = AppCore.GetRuntime(b.Job.Id);
            True(!rt.Running && rt.Idle.IsSet && rt.Resolving, "停止完了・再開禁止");
            AppCore.EndResolve(b.Job); b.Job.BandwidthKBps = 0;
            True(AppCore.RunJob(b.Job.Id, true, false), "再実行");
            True(rt.Idle.Wait(30000), "完了"); True(b.DE("big.bin"), "コピー成功");
        }

        private static void RecoverSettings()
        {
            Box b = new Box("T92"); b.App.FolderName = "recover-me"; AppCore.SaveSettings(); AppCore.SaveSettings();
            File.WriteAllText(AppPaths.SettingsFile, "{broken");
            AppCore.LoadSettings();
            Eq("recover-me", AppCore.Settings.FolderName, "バックアップから復元");
            Eq(b.Job.Id, AppCore.Settings.Jobs[0].Id, "ジョブを維持");
        }

        private static void UpdateTransaction()
        {
            string root = Path.Combine(baseDir, "T93"); Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "app.exe"), config = Path.Combine(root, "app.config");
            File.WriteAllText(exe, "old"); File.WriteAllText(exe + ".new", "new"); File.WriteAllText(config, "old config");
            Throws<IOException>(delegate { Updater.ReplaceStagedFiles(new string[] { exe, config }); }, "2件目の失敗");
            Eq("old", File.ReadAllText(exe), "本体を復元"); Eq("old config", File.ReadAllText(config), "設定も維持");
        }

        private static void DeleteFailures()
        {
            Box b = new Box("T94"); b.Job.Mirror = true; b.Job.Dedup = DedupMode.Off; b.Job.FollowRename = false;
            b.W("a.txt", "A"); b.W("keep.txt", "K"); b.Run(); File.Delete(b.S("a.txt"));
            using (FileStream locked = new FileStream(b.D("a.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                RunResult r = b.Run(); True(r.Errors > 0 && r.Outcome == JobOutcome.Partial, "削除失敗は部分失敗");
                DupEntry e = new DupEntry(); e.Rel = "a.txt"; e.CanDelete = true; e.Status = "orphan";
                List<DupEntry> done = new List<DupEntry>(); List<string> errors = new List<string>();
                Eq(0, DupChecker.DeleteSelected(b.Job, b.App, b.State, new List<DupEntry> { e }, done, errors), "削除失敗");
                Eq(0, done.Count, "成功一覧から除外"); Eq(1, errors.Count, "理由を返す");
            }
        }

        private static void RetryMetadata()
        {
            Box b = new Box("T95"); b.W("a.txt", "old");
            int calls = 0; long size = 0, stamp = 0;
            DateTime original = File.GetLastWriteTimeUtc(b.S("a.txt"));
            SafeFs.CopyFile(b.S("a.txt"), b.D("a.txt"), original, null, CancellationToken.None,
                delegate(long n) { if (calls++ == 0) { File.WriteAllText(b.S("a.txt"), "new-longer"); File.SetLastWriteTimeUtc(b.S("a.txt"), original.AddSeconds(30)); } }, null,
                delegate(long n, long t) { size = n; stamp = t; });
            Eq(new FileInfo(b.S("a.txt")).Length, size, "実際のサイズ");
            Eq(File.GetLastWriteTimeUtc(b.S("a.txt")).Ticks, stamp, "実際の日時");
            Eq(stamp, File.GetLastWriteTimeUtc(b.D("a.txt")).Ticks, "コピー先の日時");
        }

        private static void CancelWork()
        {
            Box b = new Box("T96"); for (int i = 0; i < 510; i++) b.W(i + ".txt", "a");
            CancellationTokenSource ct = new CancellationTokenSource();
            Throws<OperationCanceledException>(delegate { Scanner.Scan(b.Src, true, null, false, ct.Token, delegate(int n) { ct.Cancel(); }); }, "同じフォルダ内でも中止");
            Throws<OperationCanceledException>(delegate { CopyEngine.HashFile(b.S("0.txt"), ct.Token); }, "ハッシュ中止");
        }

        private static void RestoreNames()
        {
            Box b = new Box("T97"); b.WD("a.txt", "original", 600); b.WD("a (復元).txt", "existing", 600);
            string batch = Path.Combine(b.Dst, @"_AutoCopy\削除済み\20261106-120000"); Directory.CreateDirectory(batch);
            WriteFile(Path.Combine(batch, "a.txt"), "restore", 600);
            TrashStore.Batch item = new TrashStore.Batch(); item.Path = batch; item.Kind = "削除済み"; item.Name = "20261106-120000";
            Eq(1, TrashStore.Restore(b.Job.Name, b.Dst, item), "復元件数");
            Eq("original", b.RD("a.txt"), "元のファイル保護"); Eq("existing", b.RD("a (復元).txt"), "既存の復元も保護");
            Eq("restore", b.RD("a (復元 2).txt"), "連番で復元");
        }

        private static void LogRetry()
        {
            Box b = new Box("T98"); string source = Path.Combine(b.Root, "unique.sending"), target = b.D("day.log");
            File.WriteAllText(source, "payload\r\n");
            LogDelivery.AppendOnce(source, target, CancellationToken.None);
            string once = File.ReadAllText(target);
            LogDelivery.AppendOnce(source, target, CancellationToken.None);
            Eq(once, File.ReadAllText(target), "再送でも同じ内容");
        }

        private static void SettingsTransaction()
        {
            Box b = new Box("T99"); b.App.FolderName = "before"; AppCore.SaveSettings();
            using (FileStream locked = new FileStream(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                b.App.FolderName = "after";
                Throws<IOException>(delegate { AppCore.SaveSettings(); }, "設定の保存失敗");
                Eq("before", AppCore.Settings.FolderName, "使用中設定を戻す");
            }
        }

        private static void InputValidation()
        {
            foreach (string name in new string[] { "CON", "nul.txt", "COM1", "LPT9.log", "x\u0001" }) True(!PathUtil.IsValidFolderName(name), "予約・制御文字: " + name);
            True(PathUtil.IsValidFolderName("会議資料"), "通常名");
            DistEnvelope envelope = new DistEnvelope(); envelope.Iterations = int.MaxValue;
            Throws<InvalidDataException>(delegate { Distribution.Decrypt(envelope, "password"); }, "過大な処理回数");
        }

        private static void UnavailableOtherSource()
        {
            Box b = new Box("T101"); JobConfig other = new JobConfig(); other.Source = Path.Combine(b.Root, "missing"); other.Dest = b.D("nested"); b.App.Jobs.Add(other);
            JobFileScope scope = JobFileScope.Build(b.Job, b.App, CancellationToken.None);
            True(scope.Protects(b.D(@"nested\a.txt"), false), "未接続の別ジョブ領域は保護");
            True(scope.Protects(b.Dst, true), "親フォルダごとの整理も禁止");
        }

        private static void OfflineApproval()
        {
            Box b = new Box("T102"); b.Job.Mirror = true; b.Job.Dedup = DedupMode.Off; b.W("a.txt", "a"); b.Run();
            PendingDelete pending = new PendingDelete(); pending.Rel = "a.txt"; b.State.PendingDeletes.Add(pending);
            Directory.Move(b.Src, b.Src + "-offline");
            string reason = AppCore.ResolvePendingDeletes(b.Job, true);
            True(reason != null, "接続できなければ失敗"); True(b.DE("a.txt"), "コピー先を維持"); Eq(1, b.State.PendingDeletes.Count, "確認待ちを維持");
        }

        private static void InventoryTest()
        {
            Box b = new Box("T103"); for (int i = 0; i < 200; i++) b.WD(i + ".txt", "sample", 600);
            DestinationInventory inventory = new DestinationInventory(); Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) { DestEntry f = inventory.Get(b.D(i + ".txt")); True(f != null && f.Length == 6, "一覧の内容"); }
            True(inventory.Get(b.D("missing")) == null, "存在しないファイル");
            Eq(1, inventory.EnumeratedDirectories, "200件を1回の一覧取得で判定");
            Note = "200件のメタデータ照会: " + sw.ElapsedMilliseconds + "ms（ローカル測定）";
        }

        private static void Reschedule()
        {
            Box b = new Box("T104"); AppCore.SaveSettings();
            JobRuntime rt = AppCore.GetRuntime(b.Job.Id); rt.LastRun = DateTime.Now.AddMinutes(-2); rt.NextRun = DateTime.Now.AddHours(1);
            b.Job.IntervalMinutes = 5; AppCore.SaveSettings();
            True(Math.Abs((rt.NextRun - rt.LastRun.AddMinutes(5)).TotalSeconds) < 1, "次回予定を再計算");
        }
            private static void DupKeepsExcluded()
        {
            Box b = new Box("T106"); b.Job.Dedup = DedupMode.Off;
            b.W("keep.dat", "同じ内容"); b.W("a.txt", "a");
            b.Run();
            True(b.DE("keep.dat"), "最初はコピー");
            b.Job.Exclude.Extensions.Add(".dat");   // 後から除外（コピー元にはまだある）
            b.WD(@"old\keep.dat", "同じ内容", 600);  // コピー先にだけある同じ中身（本当に古いコピー）
            DupResult r = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
            bool keepIsOld = false, oldIsOld = false;
            foreach (DupGroup g in r.Groups)
                foreach (DupEntry e in g.Entries)
                {
                    if (string.Equals(e.Rel, "keep.dat", StringComparison.OrdinalIgnoreCase) && e.Status == "old") keepIsOld = true;
                    if (string.Equals(e.Rel, @"old\keep.dat", StringComparison.OrdinalIgnoreCase) && e.Status == "old") oldIsOld = true;
                }
            True(!keepIsOld, "コピー元にある（除外しているだけの）ファイルのコピーは古いコピーにしない");
            True(oldIsOld, "コピー先にだけあるものは古いコピー");
        }

        private static void HiddenDirWithIncludes()
        {
            ExcludeRules r = new ExcludeRules();
            r.ExcludeHidden = true; r.ExcludeSystem = true; r.Folders.Add("tmp"); r.Includes.Add("*.txt");
            ExcludeMatcher m = new ExcludeMatcher(r);
            True(m.ExcludeDir("秘密", "秘密", FileAttributes.Directory | FileAttributes.Hidden), "隠しフォルダには入らない");
            True(m.ExcludeDir("System Volume Information", "System Volume Information", FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System), "システムのフォルダには入らない");
            True(!m.ExcludeDir("tmp", "tmp", FileAttributes.Directory), "名前で除外したフォルダは、例外のために中を見る");
            True(m.ExcludeFile(@"tmp\x.bin", "x.bin", 1, DateTime.UtcNow, FileAttributes.Normal), "中の例外でないファイルは除外");
            True(!m.ExcludeFile(@"tmp\x.txt", "x.txt", 1, DateTime.UtcNow, FileAttributes.Normal), "中の例外はコピー");
            // 実際の走査でも：隠しフォルダの中の（隠しでない）ファイルはコピーしない
            Box b = new Box("T107"); b.Job.Dedup = DedupMode.Off;
            b.Job.Exclude.ExcludeHidden = true; b.Job.Exclude.Includes.Add("*.txt");
            b.W(@"h\a.txt", "a"); b.W("top.txt", "t");
            File.SetAttributes(b.S("h"), FileAttributes.Directory | FileAttributes.Hidden);
            b.Run();
            True(b.DE("top.txt") && !b.DE(@"h\a.txt"), "隠しフォルダの中はコピーしない");
        }

        private static void SyncCacheRules()
        {
            SyncPair p = new SyncPair("T108");
            p.JA.SyncRemoteCacheMinutes = 30;
            WriteFile(Path.Combine(p.A, "a.txt"), "a", 600);
            WriteFile(Path.Combine(p.A, "b.log"), "b", 600);
            RunResult r1 = p.Run(p.JA, "pc1");
            Eq(2, r1.Copied, "共有へ2件");
            True(p.Run(p.JA, "pc1").RemoteScanSkipped, "設定が同じなら省略");
            p.JA.Exclude.Extensions.Add(".log");   // 後から除外
            RunResult r3 = p.Run(p.JA, "pc1");
            True(!r3.RemoteScanSkipped, "除外を変えたら詳しく確認し直す");
            Eq(0, SyncDeleteStore.Count(p.JA.Id), "除外しただけのファイルを削除の候補にしない");
            True(File.Exists(Path.Combine(p.S, "b.log")), "共有フォルダはそのまま");
            p.JA.Exclude.OnlyFolders.Add("docs");
            True(!p.Run(p.JA, "pc1").RemoteScanSkipped, "対象を変えたときも確認し直す");
            Eq(0, SyncDeleteStore.Count(p.JA.Id), "対象外にしただけのファイルも候補にしない");
            SyncRemoteCache.Invalidate(p.JA.Id); SyncRemoteCache.Invalidate(p.JB.Id);
        }

        private static void SyncSharedProtected()
        {
            Box b = new Box("T109");
            JobConfig sync = new JobConfig(); sync.Name = "同期"; sync.Sync = true;
            sync.Source = Path.Combine(b.Root, "local"); Directory.CreateDirectory(sync.Source);
            sync.Dest = b.D("shared");
            b.App.Jobs.Add(sync);
            JobFileScope scope = JobFileScope.Build(b.Job, b.App, CancellationToken.None);
            True(scope.Protects(b.D(@"shared\他のPCのファイル.txt"), false), "共有フォルダの中は保護");
            True(scope.Protects(b.Dst, true), "親フォルダごとの整理もしない");
            True(!scope.Protects(b.D("自分のファイル.txt"), false), "それ以外は今までどおり");
            b.App.Jobs.Remove(sync);
        }

        private static void LogResume()
        {
            Box b = new Box("T110");
            string target = b.D("day.log");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "前の行\r\n");
            string batch = Path.Combine(b.Root, "1.sending");
            File.WriteAllText(batch, "送る行1\r\n送る行2\r\n");
            LogDelivery.AppendOnce(batch, target, CancellationToken.None);
            Eq("前の行\r\n送る行1\r\n送る行2\r\n", File.ReadAllText(target), "書き足した");
            // 送れたが、手元のファイルを消す前に終了 → 次の送信で二重にしない
            LogDelivery.AppendOnce(batch, target, CancellationToken.None);
            Eq("前の行\r\n送る行1\r\n送る行2\r\n", File.ReadAllText(target), "再送でも二重にしない");
            LogDelivery.Done(batch);
            True(!File.Exists(batch) && !File.Exists(batch + LogDelivery.PosSuffix), "片付けた");
            // 書き足しの途中で終了（途中までの行が残った）→ 取り消してから書き直す
            string batch2 = Path.Combine(b.Root, "2.sending");
            File.WriteAllText(batch2, "次の行\r\n");
            long len = new FileInfo(target).Length;
            File.WriteAllText(batch2 + LogDelivery.PosSuffix, len.ToString());
            using (FileStream fs = new FileStream(target, FileMode.Append)) { byte[] half = Encoding.UTF8.GetBytes("次の"); fs.Write(half, 0, half.Length); }
            LogDelivery.AppendOnce(batch2, target, CancellationToken.None);
            Eq("前の行\r\n送る行1\r\n送る行2\r\n次の行\r\n", File.ReadAllText(target), "途中までの分を取り消して書き直す");
            LogDelivery.Done(batch2);
            // 片付けの途中で終了した記録を消す
            File.WriteAllText(Path.Combine(b.Root, "3.sending" + LogDelivery.PosSuffix), "0");
            LogDelivery.CleanOrphans(b.Root);
            True(!File.Exists(Path.Combine(b.Root, "3.sending" + LogDelivery.PosSuffix)), "送信待ちのない記録を消す");
        }

        private static void InventoryLimit()
        {
            Box b = new Box("T111");
            for (int i = 0; i < 5; i++) b.WD(i + ".txt", "abc", 600);
            int keep = DestinationInventory.MaxEntries;
            try
            {
                DestinationInventory.MaxEntries = 0;
                DestinationInventory inv = new DestinationInventory();
                DestEntry e = inv.Get(b.D("3.txt"));
                True(e != null && e.Exists && e.Length == 3, "上限を超えたらファイルごとに確かめる");
                True(inv.Get(b.D("none.txt")) == null, "ないファイル");
                Eq(0, inv.EnumeratedDirectories, "一覧は取らない");
                DestinationInventory.MaxEntries = 1000;
                DestinationInventory inv2 = new DestinationInventory();
                inv2.Get(b.D("0.txt"));
                Eq(5, inv2.CachedEntries, "持っている件数");
            }
            finally { DestinationInventory.MaxEntries = keep; }
        }

        private static void StopTimeout()
        {
            Box b = new Box("T112");
            JobRuntime rt = AppCore.GetRuntime(b.Job.Id);
            rt.Idle.Reset();   // 止まらない処理（ネットワークの応答待ちなど）のつもり
            int keep = AppCore.StopWaitSeconds;
            AppCore.StopWaitSeconds = 1;
            Stopwatch sw = Stopwatch.StartNew();
            bool all;
            try { all = AppCore.Stop(); }
            finally
            {
                rt.Idle.Set();
                AppCore.StopWaitSeconds = keep;
                // 以降のテストのために「終了中」を戻す
                typeof(AppCore).GetField("stopping", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, false);
            }
            True(!all, "止まらないものがあったと返す");
            True(sw.ElapsedMilliseconds < 10000, "上限で待つのをやめる: " + sw.ElapsedMilliseconds + "ms");
        }
    }
}
