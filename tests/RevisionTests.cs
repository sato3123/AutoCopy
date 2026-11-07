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
            Add("T113", "長いメッセージは画面の高さに収まる行数までにする（下のボタンが画面の外に出ない）", MessageFits);
            Add("T114", "表示切れの自動確認が、下のボタンの折り返し・スクロールの下端の欠けを見つける", LayoutCheckDetects);
            Add("T115", "フォルダ内の重複：中身・ファイル名・更新日時・大きさを組み合わせて探す・小さいファイルを除く", FolderDupBasis);
            Add("T116", "重複チェック：小さいファイルの重複（古いコピー・中身が同じ）を探さない設定", JobDupMinSize);
            Add("T117", "「？ 説明」：画面から開く操作説明書の場所（id）がすべてあり、説明書の中のリンクも切れていない", HelpAnchors);
            Add("T118", "ジョブ設定：同期のよく使う組み合わせ・「詳しい設定」を最初から見せるかの判断・大きさの選択・日付の形", JobFormHelpers);
            Add("T119", "除外の件数の表示：数えた「コピーする件数」が、実際にコピーした件数と同じ", ExclusionCountMatchesCopy);
            Add("T120", "エラーのお知らせに「次にすること」を付ける（ネットワーク・容量・権限・使用中・ID/パスワード）", ErrorAdvice);
            Add("T121", "フォルダの重複ファイル探し：残す候補の理由（パスが短い・古い・名前の順）", KeepReason);
            Add("T122", "新しいジョブの保存後の案内：既定は「あとで（無効のまま）」・有効にするには選ぶ必要がある", EnableJobChoice);
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

        /// <summary>リポジトリの中のファイルを探す（テストの実行場所・exe の場所から上へ）</summary>
        private static string FindRepoFile(string name)
        {
            foreach (string start in new string[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
            {
                string d = start;
                for (int i = 0; i < 5 && !string.IsNullOrEmpty(d); i++)
                {
                    string f = Path.Combine(d, name);
                    if (File.Exists(f) || Directory.Exists(f)) return f;
                    d = Path.GetDirectoryName(d.TrimEnd('\\', '/'));
                }
            }
            return null;
        }

        private static void HelpAnchors()
        {
            string manual = FindRepoFile("操作説明書.html");
            True(manual != null, "操作説明書.html が見つかる");
            string html = File.ReadAllText(manual, Encoding.UTF8);
            HashSet<string> ids = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, "id=\"([^\"]+)\"")) ids.Add(m.Groups[1].Value);
            foreach (string a in Help.All) True(ids.Contains(a), "説明書に id=\"" + a + "\" がある");
            // 説明書の中のリンク（#〜）がすべて飛べる
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, "href=\"#([^\"]+)\"")) True(ids.Contains(m.Groups[1].Value), "説明書のリンク #" + m.Groups[1].Value + " の飛び先がある");
            // ソースの Help.〇〇 がすべて Help.All にある（「？ 説明」で開く場所に抜けがない）
            string src = FindRepoFile("src");
            True(src != null, "src フォルダが見つかる");
            HashSet<string> used = new HashSet<string>();
            foreach (string f in Directory.GetFiles(src, "*.cs"))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f, Encoding.UTF8), "(?<![\\w.])Help\\.([A-Z]\\w*)"))
                {
                    string n = m.Groups[1].Value;
                    if (n == "All" || n == "Used" || n == "UsedAnchors") continue;
                    System.Reflection.FieldInfo fi = typeof(Help).GetField(n);
                    True(fi != null, "Help." + n + " がある（" + Path.GetFileName(f) + "）");
                    string v = (string)fi.GetValue(null);
                    True(Array.IndexOf(Help.All, v) >= 0, "Help." + n + " が Help.All にある");
                    used.Add(v);
                }
            True(used.Count >= 14, "多くの画面に「？ 説明」がある（" + used.Count + "か所）");
            foreach (string a in Help.UsedAnchors()) True(a.Length == 0 || ids.Contains(a), "開いた画面の説明の場所 " + a);
        }

        private static void JobFormHelpers()
        {
            bool take, push;
            True(SyncPresets.Values(0, out take, out push) && take && !push, "受け取る（初期値）");
            True(SyncPresets.Values(1, out take, out push) && take && push, "すべてのPCで同じ");
            True(!SyncPresets.Values(SyncPresets.Custom, out take, out push), "自分で選ぶ");
            Eq(0, SyncPresets.Find(true, false), "初期値の組み合わせ");
            Eq(1, SyncPresets.Find(true, true), "送る・受け取る");
            Eq(SyncPresets.Custom, SyncPresets.Find(false, false), "受け取らない → 自分で選ぶ");
            Eq(SyncPresets.Custom, SyncPresets.Find(false, true), "送るだけ → 自分で選ぶ");
            JobConfig d0 = new JobConfig();
            Eq(0, SyncPresets.Find(d0.SyncTakeNew, d0.SyncPushNew), "新しいジョブの初期値は「受け取る」");

            // 初期値のジョブは「詳しい設定」を隠してよい。初期値と違うものが1つでもあれば最初から見せる（隠れた設定に気づかないことがない）
            True(!JobEditForm.HasAdvancedValues(new JobConfig()), "初期値のジョブ");
            Action<string, Action<JobConfig>> adv = delegate(string what, Action<JobConfig> change)
            {
                JobConfig j = new JobConfig(); change(j);
                True(JobEditForm.HasAdvancedValues(j), "詳しい設定を見せる：" + what);
            };
            adv("同時にコピーする数", delegate(JobConfig j) { j.Parallel = 2; });
            adv("転送速度", delegate(JobConfig j) { j.BandwidthKBps = 500; });
            adv("時間帯", delegate(JobConfig j) { j.UseTimeWindow = true; });
            adv("上書きのルール", delegate(JobConfig j) { j.NewerDestPolicy = NewerPolicy.Skip; });
            adv("上書き前に退避", delegate(JobConfig j) { j.BackupBeforeOverwrite = true; });
            adv("名前変更の追従", delegate(JobConfig j) { j.FollowRename = false; });
            adv("再試行", delegate(JobConfig j) { j.RetryCount = 5; });
            adv("例外", delegate(JobConfig j) { j.Exclude.Includes.Add("会議録画\\*.mp4"); });
            adv("対象を限定", delegate(JobConfig j) { j.Exclude.OnlyExtensions.Add(".xlsx"); });
            adv("サイズ", delegate(JobConfig j) { j.Exclude.MaxSizeBytes = 1024; });
            adv("日付", delegate(JobConfig j) { j.Exclude.OlderThan = "2020-01-01"; });
            adv("隠しファイルもコピー", delegate(JobConfig j) { j.Exclude.ExcludeHidden = false; });
            adv("小さいファイルの重複", delegate(JobConfig j) { j.DupMinSizeKB = 100; });
            adv("同期の共有フォルダの確認", delegate(JobConfig j) { j.Sync = true; j.SyncRemoteCacheMinutes = 0; });
            JobConfig nc = new JobConfig(); nc.Exclude.Includes = null; nc.Exclude.OnlyFolders = null; nc.Exclude.OnlyExtensions = null;
            True(!JobEditForm.HasAdvancedValues(nc), "古い設定（一覧がない）でも落ちない");

            // 大きさの選択：よく使う大きさ＋今の値、0 は「すべて」
            System.Windows.Forms.ComboBox cb = Ui.SizeChoice(0, "すべて", "{0} より小さいものは除く");
            Eq(0, Ui.SizeChoiceKB(cb), "初期値は 0（すべて）");
            Eq("すべて", cb.SelectedItem.ToString(), "0 の表示");
            Ui.SetSizeChoice(cb, 100); Eq(100, Ui.SizeChoiceKB(cb), "100 KB を選ぶ");
            True(cb.SelectedItem.ToString().Contains("より小さいものは除く"), "大きさの表示: " + cb.SelectedItem);
            int n0 = cb.Items.Count;
            Ui.SetSizeChoice(cb, 1024); Eq(n0, cb.Items.Count, "一覧にある値は増やさない");
            System.Windows.Forms.ComboBox cb2 = Ui.SizeChoice(300, "すべて", "{0} 未満");
            Eq(300, Ui.SizeChoiceKB(cb2), "一覧にない今の値（300 KB）も残す");
            Ui.SetSizeChoice(cb2, 7); Eq(7, Ui.SizeChoiceKB(cb2), "一覧にない値も選べる");

            // 日付の欄：地域の設定に関係なく yyyy/MM/dd、未設定（MinValue）でも落ちない
            System.Windows.Forms.DateTimePicker dp = Ui.DateBox(new DateTime(2024, 3, 5));
            Eq("yyyy/MM/dd", dp.CustomFormat, "日付の形");
            Eq(System.Windows.Forms.DateTimePickerFormat.Custom, dp.Format, "形を固定");
            Eq(new DateTime(2024, 3, 5), dp.Value.Date, "値");
            Eq(DateTime.Today, Ui.DateBox(DateTime.MinValue).Value.Date, "未設定は今日");
        }

        private static void ExclusionCountMatchesCopy()
        {
            Box b = new Box("T119");
            b.W("a.txt", "a"); b.W("b.tmp", "b"); b.W(@"sub\c.mp4", "c"); b.W(@"sub\d.xlsx", "d"); b.W(@"skip\e.txt", "e"); b.W("~$f.docx", "f");
            b.Job.Exclude.Presets.Add("video");
            b.Job.Exclude.Folders.Add("skip");
            b.Apply();
            int total;
            int inc = ExclusionCounter.Count(b.Src, b.Job.IncludeSubdirs, b.Job.Exclude, CancellationToken.None, out total);
            Eq(6, total, "コピー元の全件数");
            Eq(2, inc, "コピーする件数（a.txt・sub\\d.xlsx）");
            RunResult r = b.Run();
            Eq(inc, r.Copied, "数えた件数と実際にコピーした件数が同じ");
            True(b.DE("a.txt") && b.DE(@"sub\d.xlsx") && !b.DE("b.tmp") && !b.DE(@"sub\c.mp4") && !b.DE(@"skip\e.txt"), "コピーされたもの");
            // 拡張子の一覧（除外に加える候補）：大きい順・拡張子なしも数える
            b.W("big.psd", new string('x', 5000)); b.W("noext", "n");
            List<ExtCount> ec = ExtCount.Of(Scanner.Scan(b.Src, true, null, false, CancellationToken.None));
            Eq(".psd", ec[0].Ext, "大きい順");
            True(ec.Exists(delegate(ExtCount e) { return e.Ext == "" && e.Count == 1; }), "拡張子なし");
            True(ec.Exists(delegate(ExtCount e) { return e.Ext == ".txt" && e.Count == 2; }), ".txt は2件");
            // 中止できる
            CancellationTokenSource cts = new CancellationTokenSource(); cts.Cancel();
            bool canceled = false;
            try { ExclusionCounter.Count(b.Src, true, b.Job.Exclude, cts.Token, out total); } catch (OperationCanceledException) { canceled = true; }
            True(canceled, "中止したら数えない");
        }

        private static void ErrorAdvice()
        {
            Func<string, bool, string> adv = delegate(string t, bool cred) { return ErrorKind.Advice(t, cred) ?? ""; };
            True(adv("【" + ErrorKind.Label(ErrorKind.Network) + "】ネットワークにつながっていないため、\\\\nas に接続できません", false).Contains("Wi-Fi"), "ネットワーク");
            True(adv("\\\\fs02\\設計部 に接続できません（3回再試行しました）", false).Contains("Wi-Fi"), "接続できません");
            True(adv("【" + ErrorKind.Label(ErrorKind.DiskFull) + "】ディスクがいっぱいです", false).Contains("空き容量"), "容量不足");
            True(adv("【" + ErrorKind.Label(ErrorKind.AccessDenied) + "】a.txt", false).Contains("権限"), "アクセス拒否");
            True(adv("【" + ErrorKind.Label(ErrorKind.Locked) + "】a.xlsx", false).Contains("閉じて"), "使用中");
            True(adv("【" + ErrorKind.Label(ErrorKind.TooLong) + "】", false).Contains("短く"), "パスが長すぎる");
            True(adv("コピー元が見つかりません: D:\\data", false).Contains("USBメモリ"), "見つからない");
            True(adv("どこかの原因", true).Contains("パスワード"), "ID・パスワードの誤り");
            True(adv("ユーザー名またはパスワードが正しくありません", false).Contains("パスワード"), "ID・パスワードの文");
            Eq(null, ErrorKind.Advice("よく分からないエラー", false), "分からないときは付けない");
            // 例外の分類から（容量不足：Win32 112）
            string desc = ErrorKind.Describe(new IOException("書けません", unchecked((int)0x80070070)));
            True(desc.StartsWith("【" + ErrorKind.Label(ErrorKind.DiskFull) + "】"), "分類: " + desc);
            string w = ErrorKind.WithAdvice(desc, false);
            True(w.StartsWith(desc) && w.Contains(ErrorKind.AdvicePrefix) && w.Contains("空き容量"), "元の文の後ろに付ける: " + w);
            Eq(w, ErrorKind.WithAdvice(w, false), "2回付けない");
            Eq("よく分からないエラー", ErrorKind.WithAdvice("よく分からないエラー", false), "分からないときはそのまま");
            Eq(null, ErrorKind.WithAdvice(null, false), "null のまま");
            True(!w.Contains("\n"), "一覧で1行に収まる（改行を入れない）");

            // 実際のジョブのエラー（コピー元がない）で、お知らせに「次にすること」が付く
            Box b = new Box("T120");
            b.Job.Source = Path.Combine(b.Root, "ないフォルダ");
            b.Apply();
            JobRuntime rt = AppCore.GetRuntime(b.Job.Id);
            True(AppCore.RunJob(b.Job.Id, true, false), "実行開始");
            True(rt.Idle.Wait(30000), "終わる");
            Notice n = AppCore.RecentNotices().Find(delegate(Notice x) { return x.JobId == b.Job.Id && x.Level == NoticeLevel.Error; });
            True(n != null, "エラーのお知らせ");
            True(n.Text.Contains(ErrorKind.AdvicePrefix), "お知らせに「次にすること」: " + n.Text);
            True(Directory.Exists(b.Src), "元データ（テストの元フォルダ）はそのまま");
        }

        private static void KeepReason()
        {
            Box b = new Box("T121");
            string root = Path.Combine(b.Root, "free");
            DateTime t = DateTime.UtcNow.AddDays(-3);
            Action<string, DateTime> put = delegate(string rel, DateTime m)
            {
                string f = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f, Encoding.ASCII.GetBytes("SAME"));
                File.SetLastWriteTimeUtc(f, m);
            };
            put(@"a.txt", t); put(@"sub\a.txt", t.AddHours(-5));
            FolderDupResult r = FolderDup.Find(root, true, 0, DupBasis.Content, CancellationToken.None, null);
            Eq(1, r.Groups.Count, "1まとまり");
            Eq("a.txt", r.Groups[0].Files[0], "パスが短いものを残す（古くても）");
            Eq("場所（パス）がいちばん短い", r.Groups[0].KeepReason(), "理由：パス");

            string root2 = Path.Combine(b.Root, "free2");
            root = root2;
            put(@"x\b1.txt", t); put(@"x\b2.txt", t.AddHours(-2));
            r = FolderDup.Find(root2, true, 0, DupBasis.Content, CancellationToken.None, null);
            Eq(@"x\b2.txt", r.Groups[0].Files[0], "同じ長さなら古いものを残す");
            Eq("いちばん古い（最初のもの）", r.Groups[0].KeepReason(), "理由：古い");

            string root3 = Path.Combine(b.Root, "free3");
            root = root3;
            put(@"y\c2.txt", t); put(@"y\c1.txt", t);
            r = FolderDup.Find(root3, true, 0, DupBasis.Content, CancellationToken.None, null);
            Eq(@"y\c1.txt", r.Groups[0].Files[0], "同じ長さ・同じ日時なら名前の順");
            Eq("名前の順で先", r.Groups[0].KeepReason(), "理由：名前");
            FolderDupGroup one = new FolderDupGroup(); one.Files.Add("z.txt"); one.Mtimes.Add(0);
            Eq("1件だけ", one.KeepReason(), "1件のとき");
        }

        private static void EnableJobChoice()
        {
            JobConfig j = new JobConfig(); j.Name = "新しいジョブ"; j.Enabled = false;
            using (EnableJobForm f = new EnableJobForm(j))
            {
                Eq(EnableJobForm.Later, f.Choice, "選ばずに閉じたら「あとで（無効のまま）」");
                List<string> texts = new List<string>();
                Stack<System.Windows.Forms.Control> st = new Stack<System.Windows.Forms.Control>(); st.Push(f);
                while (st.Count > 0) { System.Windows.Forms.Control c = st.Pop(); if (c is System.Windows.Forms.Button) texts.Add(c.Text); foreach (System.Windows.Forms.Control k in c.Controls) st.Push(k); }
                True(texts.Exists(delegate(string s) { return s.Contains("テストしてから有効にする"); }), "テストしてから有効にする");
                True(texts.Exists(delegate(string s) { return s.Contains("今すぐ有効にする"); }), "今すぐ有効にする");
                True(texts.Exists(delegate(string s) { return s.Contains("あとで"); }), "あとで");
                True(f.CancelButton != null && ((System.Windows.Forms.Button)f.CancelButton).Text.Contains("あとで"), "Esc は「あとで」");
            }
            True(!j.Enabled, "案内を出しただけでは有効にしない");
        }

        private static void MessageFits()
        {
            List<string> lines = new List<string>();
            lines.Add("できなかったものがあります：");
            for (int i = 0; i < 300; i++) lines.Add(@"\\nas\共有\経理\2026年度\請求書_" + i + ".xlsx：ネットワーク名が見つかりません");
            string all = string.Join("\n", lines.ToArray());
            string c = Ui.ClipLines(all, 25, 120);
            string[] got = c.Split('\n');
            Eq(25, got.Length, "25行まで");
            True(got[24].StartsWith("…ほか ") && got[24].Contains("277行"), "残りの行数を書く: " + got[24]);
            Eq("できなかったものがあります：", got[0], "先頭はそのまま");
            Eq("短い", Ui.ClipLines("短い", 25, 120), "短いものはそのまま");
            string longLine = new string('あ', 500);
            True(Ui.ClipLines(longLine, 25, 120).Length <= 121, "長すぎる1行は切る");
            // 画面の高さから決める行数：小さい画面でも 8行以上、大きい画面でも 40行まで
            int small = Ui.MessageMaxLines(582), normal = Ui.MessageMaxLines(728), big = Ui.MessageMaxLines(2000);
            True(small >= 8 && small <= normal && normal <= big && big <= 40, "行数: " + small + " / " + normal + " / " + big);
            // 1行あたりの高さ × 行数 ＋ 題名・ボタンの分 が画面に収まる
            int lineH = System.Windows.Forms.TextRenderer.MeasureText("あ", System.Drawing.SystemFonts.MessageBoxFont).Height + 2;
            True(normal * lineH + Ui.S(140) <= 728, "1366×768 の画面に収まる: " + normal + "行×" + lineH + "px");
        }

        private static void LayoutCheckDetects()
        {
            // 狭い画面に幅の広いボタンを並べる → 下のボタンの帯が2段に折り返す
            System.Windows.Forms.Form f = new System.Windows.Forms.Form();
            Ui.SetupDialog(f, "確認用", 320, 200);
            f.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            f.Location = new System.Drawing.Point(10, 10);
            System.Windows.Forms.FlowLayoutPanel body = Ui.Body();
            for (int i = 0; i < 30; i++) body.Controls.Add(Ui.L("行 " + i));
            f.Controls.Add(body);
            f.Controls.Add(Ui.Footer(Ui.B("とても長い名前のボタン その1", delegate { }), Ui.B("とても長い名前のボタン その2", delegate { }), Ui.B("とても長い名前のボタン その3", delegate { })));
            try
            {
                f.Show();
                System.Windows.Forms.Application.DoEvents();
                List<string> issues = LayoutCheck.Check(f);
                True(issues.Exists(delegate(string x) { return x.Contains("2段に折り返"); }), "折り返しを見つける: " + string.Join(" / ", issues.ToArray()));
                // スクロールの下端：最後の空白があるので欠けない
                True(!issues.Exists(delegate(string x) { return x.Contains("いちばん下までスクロールしても"); }), "空白があれば最後の行は欠けない");
                // 最後の空白を外すと、下の余白の分だけ欠ける（確認が働くこと）
                foreach (System.Windows.Forms.Control c in body.Controls)
                    if (Equals(c.Tag, Ui.ScrollSpacerTag)) { body.Controls.Remove(c); break; }
                body.PerformLayout();
                System.Windows.Forms.Application.DoEvents();
                List<string> issues2 = LayoutCheck.Check(f);
                True(issues2.Exists(delegate(string x) { return x.Contains("いちばん下までスクロールしても"); }), "空白がないと欠けを見つける: " + string.Join(" / ", issues2.ToArray()));
            }
            finally { f.Close(); f.Dispose(); }
        }

        private static void FolderDupBasis()
        {
            Box b = new Box("T115");
            string root = Path.Combine(b.Root, "free");   // ジョブのコピー元ではない場所（移せる）
            DateTime t = DateTime.UtcNow.AddDays(-3);
            Action<string, string, DateTime> put = delegate(string rel, string text, DateTime m)
            {
                string f = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f, Encoding.ASCII.GetBytes(text));
                File.SetLastWriteTimeUtc(f, m);
            };
            put(@"a\report.docx", "AAA", t);
            put(@"b\report.docx", "BBB", t.AddSeconds(1));      // 名前・日時・大きさは同じ、中身が違う
            put(@"c\report.docx", "AAA", t.AddHours(-1));       // 名前・中身は同じ、日時が違う
            put(@"d\other.docx", "AAA", t);                     // 中身・日時は同じ、名前が違う
            put(@"e\Report.DOCX", "AAAA", t);                   // 名前（大文字小文字違い）・日時は同じ、大きさが違う
            put(@"f\report.docx", "AAA", t.AddSeconds(2));      // すべて同じ（日時は2秒以内）
            put(@"g\report.docx", "AAA", t.AddSeconds(5));      // 日時が5秒違う（2秒を超える）
            Func<int, FolderDupResult> find = delegate(int basis) { return FolderDup.Find(root, true, 0, basis, CancellationToken.None, null); };
            Func<FolderDupResult, string> show = delegate(FolderDupResult r)
            {
                List<string> parts = new List<string>();
                foreach (FolderDupGroup g in r.Groups) { List<string> fs = new List<string>(g.Files); fs.Sort(StringComparer.OrdinalIgnoreCase); parts.Add("{" + string.Join(",", fs.ToArray()).Replace("\\report.docx", "").Replace("\\Report.DOCX", "").Replace("\\other.docx", "") + "}"); }
                parts.Sort(StringComparer.Ordinal);
                return string.Join(" ", parts.ToArray());
            };
            Eq("{a,c,d,f,g}", show(find(DupBasis.Content)), "中身だけ");
            Eq("{a,b,c,e,f,g}", show(find(DupBasis.Name)), "ファイル名だけ（大文字小文字は区別しない）");
            Eq("{a,b,e,f}", show(find(DupBasis.Name | DupBasis.Mtime)), "ファイル名と更新日時（2秒以内）");
            Eq("{a,b,c,f,g}", show(find(DupBasis.Name | DupBasis.Size)), "ファイル名と大きさ");
            Eq("{a,c,f,g}", show(find(DupBasis.Content | DupBasis.Name)), "中身とファイル名");
            Eq("{a,f}", show(find(DupBasis.Content | DupBasis.Name | DupBasis.Mtime)), "中身・ファイル名・更新日時");
            Eq("{a,d,f}", show(find(DupBasis.Content | DupBasis.Mtime)), "中身と更新日時");
            FolderDupResult rc = find(DupBasis.Content);
            True(rc.Groups[0].SameContent, "中身で比べたまとまり");
            FolderDupResult rn = find(DupBasis.Name | DupBasis.Mtime);
            True(!rn.Groups[0].SameContent, "中身を比べていないまとまり");
            Eq(3L + 3 + 4, rn.Groups[0].Wasted(), "空く容量はファイルごとの大きさで数える（先頭を残す）");
            Eq("ファイル名・更新日時", DupBasis.Describe(DupBasis.Name | DupBasis.Mtime), "基準の説明");
            Throws<ArgumentException>(delegate() { find(0); }, "基準なしは使えない");

            // 中身を比べていないまとまり：移すときは、残すファイルと中身が同じものだけ
            FolderDupGroup g1 = rn.Groups[0];
            True(g1.Files[0] == @"a\report.docx", "残す候補は a（パスが短い・古い）: " + g1.Files[0]);
            int moved;
            List<string> errs = FolderDup.MoveAway(root, rn.Groups, new List<string>(new string[] { @"b\report.docx", @"f\report.docx" }), out moved);
            Eq(1, moved, "中身が同じ f だけ移す");
            True(errs.Count == 1 && errs[0].StartsWith(@"b\report.docx") && errs[0].Contains("中身が違う"), "中身が違う b は移さない: " + string.Join(" / ", errs.ToArray()));
            True(File.Exists(Path.Combine(root, @"b\report.docx")) && !File.Exists(Path.Combine(root, @"f\report.docx")), "b は残り、f は移った");

            // 小さいファイルを除く
            FolderDupResult rs = FolderDup.Find(root, true, 4, DupBasis.Name, CancellationToken.None, null);
            Eq(0, rs.Groups.Count, "4バイトより小さいものを除くと、重複なし");
            True(rs.SkippedSmall >= 5, "除いた件数: " + rs.SkippedSmall);
            Eq(4L, rs.MinBytes, "除く大きさを結果に残す");
        }

        private static void JobDupMinSize()
        {
            Box b = new Box("T116"); b.Job.Dedup = DedupMode.Off;
            b.W("small.txt", "小さい");
            b.W("big.bin", new string('x', 4096));
            b.Run();
            b.WD(@"old\small.txt", "小さい", 600);                 // コピー先にだけある古いコピー（小さい）
            b.WD(@"old\big.bin", new string('x', 4096), 600);       // コピー先にだけある古いコピー（4KB）
            Func<string, bool> isOld = delegate(string rel)
            {
                DupResult r = DupChecker.Run(b.Job, b.App, b.State, CancellationToken.None);
                foreach (DupGroup g in r.Groups) foreach (DupEntry e in g.Entries)
                    if (string.Equals(e.Rel, rel, StringComparison.OrdinalIgnoreCase) && e.Status == "old") return true;
                return false;
            };
            b.Job.DupMinSizeKB = 0;
            True(isOld(@"old\small.txt") && isOld(@"old\big.bin"), "0 ならすべて探す");
            b.Job.DupMinSizeKB = 1;
            True(!isOld(@"old\small.txt"), "1KB より小さいものは探さない");
            True(isOld(@"old\big.bin"), "1KB 以上は探す");
            // 前の版の設定（項目なし）は 0 ＝ すべて
            JobConfig old = Json.Deserialize<JobConfig>("{\"Id\":\"x\",\"Name\":\"前の版\"}");
            Eq(0, old.DupMinSizeKB, "前の版の設定はすべて探す");
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
