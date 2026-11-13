using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
            Add("T123", "右クリック「エクスプローラーで保存先を開く」：開く場所の決め方（ファイル → コピー元 → 近いフォルダ）と、一覧への付け方", OpenLocation);
            Add("T124", "日本時間（UTC+9）でも保存できる：未設定（MinValue）の日時を JSON に保存しても「UTC への変換時…範囲外」にならない・値と比較は変わらない", JsonDatesSafe);
            Add("T125", "更新に失敗した版の記録を手で消して、もう一度更新できる（記録がないときも安全）", RetryBlockedUpdate);
            Add("T126", "OneDrive：クラウド用の印・「クラウドだけ」の判定・OneDrive のフォルダの判定・空きを待つ・ジャンクションは今まで通り調べない", CloudBasics);
            Add("T127", "OneDrive と同じしくみ（Windows の Cloud Files）の同期フォルダで：中まで調べる・クラウドだけのファイルを読まない（ダウンロードしない）・コピーしたファイルはこのPCに残さない", CloudSyncRoot);
            Add("T128", "画面の表示：並列数の説明が実際の動き（自動＝最大4件）と同じ・上書きの確認で「共有フォルダの方を使う」は同期のものだけ・同期の行は橙・重複の「大きさ」の表示・操作の対象の表示", UiConsistency);
            Add("T129", "画面の整理：ツールバーはまとまりの区切りで折り返す・枠の幅を揃える・Esc で閉じる・操作の対象の短い表示", UiRefine);
            Add("T130", "上書きの確認：件数が多くても画面が開く（3万件）・チェックの変化はまとめて調べる", ConflictManyItems);
            Add("T131", "上書きの確認：どちらが新しいか・それぞれ新しい方の扱い・新しい方にまとめてチェック", ConflictNewer);
            Add("T132", "ファイルの一括削除：条件・使えないフォルダ・探しても削除しない・チェックしたものだけごみ箱へ・探した後に変わったものは削除しない", BulkDeleteTool);
            Add("T133", "触っていないのに「日付違い・サイズ同じ」にならない：コピー先が日時を残さない・コピー元の日時が少しずれる・記録がない（中身が同じならコピーも確認もしない。中身が違えば今まで通り）", TimeOnlyDifference);
            Add("T134", "ジョブのグループ：同じグループのジョブは同時に実行しない（待って、終わったら実行）・違うグループ・グループなしは今まで通り", JobGroups);
            Add("T135", "お知らせ画面：処理中は更新しない（終わったら1回だけ）・件数が減っただけでは出し直さない・増えたら出す", AttentionOnlyAtEnd);
            Add("T136", "OneDrive：コピー先の大きさが少しだけ違い、コピー先の方が新しいときは無視（コピー元が変わった・新しい・違いが大きい・設定なし・OneDrive でない・同期は今まで通り）", CloudSmallSizeDiff);
            Add("T137", "OneDrive：空きが少ないときに待つ時間を設定で選べる（待たない・分・時間の表示・前の版の設定は30分）", CloudWaitSetting);
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
            Eq("パスが最短", r.Groups[0].KeepReason(), "理由：パス");

            string root2 = Path.Combine(b.Root, "free2");
            root = root2;
            put(@"x\b1.txt", t); put(@"x\b2.txt", t.AddHours(-2));
            r = FolderDup.Find(root2, true, 0, DupBasis.Content, CancellationToken.None, null);
            Eq(@"x\b2.txt", r.Groups[0].Files[0], "同じ長さなら古いものを残す");
            Eq("いちばん古い", r.Groups[0].KeepReason(), "理由：古い");

            string root3 = Path.Combine(b.Root, "free3");
            root = root3;
            put(@"y\c2.txt", t); put(@"y\c1.txt", t);
            r = FolderDup.Find(root3, true, 0, DupBasis.Content, CancellationToken.None, null);
            Eq(@"y\c1.txt", r.Groups[0].Files[0], "同じ長さ・同じ日時なら名前の順");
            Eq("名前の順", r.Groups[0].KeepReason(), "理由：名前");
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

        private static void OpenLocation()
        {
            Box b = new Box("T123");
            b.W(@"営業\見積.xlsx", "x");
            b.Apply();
            string inSrc = Path.Combine(b.Src, @"営業\見積.xlsx");
            bool exact;
            Eq(inSrc, Ui.LocateInExplorer(new string[] { inSrc }, out exact), "あるファイル");
            True(exact, "ファイルを選んだ状態で開く");
            string gone = Path.Combine(b.Dst, @"営業\見積.xlsx");
            Eq(inSrc, Ui.LocateInExplorer(new string[] { gone, inSrc }, out exact), "1つ目がなければ次の候補");
            True(exact, "次の候補も選んだ状態");
            string deep = Path.Combine(b.Src, @"営業\消えた\さらに\a.txt");
            Eq(Path.Combine(b.Src, "営業"), Ui.LocateInExplorer(new string[] { deep }, out exact), "消えていたら残っているいちばん近いフォルダ");
            True(!exact, "フォルダを開くだけ");
            Eq(Path.Combine(b.Src, "営業"), Ui.LocateInExplorer(new string[] { Path.Combine(b.Src, "営業") + "\\" }, out exact), "フォルダ（末尾の \\ 付き）");
            True(exact, "フォルダも選んだ状態");
            Eq(inSrc, Ui.LocateInExplorer(new string[] { @"\\?\" + inSrc }, out exact), "長いパスの印付きでも普通のパスで開く");
            Eq(null, Ui.LocateInExplorer(null, out exact), "候補なし");
            Eq(null, Ui.LocateInExplorer(new string[] { "", null }, out exact), "空の候補");
            Eq(null, Ui.LocateInExplorer(new string[] { @"Q:\ないドライブ\a.txt" }, out exact), "どこも残っていない");
            True(File.Exists(inSrc), "開く場所を調べてもファイルは変えない");

            // コピー先の整理の一覧：コピー先のパス → （大きいファイルの重複は）コピー元
            JobConfig j = b.Job;
            System.Windows.Forms.ListViewItem it = new System.Windows.Forms.ListViewItem("削除");
            DupEntry e = new DupEntry(); e.Rel = @"営業\見積 - コピー.xlsx"; it.Tag = e;
            string[] p = DupForm.DupPaths(j, it);
            Eq(1, p.Length, "重複チェックの行"); Eq(Path.Combine(CopyEngine.ResolvedDest(j, AppCore.Settings), e.Rel), p[0], "コピー先のパス");
            DedupPendingItem dp = new DedupPendingItem(); dp.Rel = @"営業\見積.xlsx"; dp.Target = "x"; it.Tag = dp;
            p = DupForm.DupPaths(j, it);
            Eq(2, p.Length, "大きいファイルの重複の行");
            Eq(Path.Combine(CopyEngine.ResolvedSource(j, AppCore.Settings), dp.Rel), p[1], "コピー先になければコピー元");
            System.Windows.Forms.ListViewItem first = new System.Windows.Forms.ListViewItem("1つ目"); first.SubItems.Add(@"営業\元.xlsx"); first.Tag = "first";
            True(DupForm.DupPaths(j, first)[0].EndsWith(@"営業\元.xlsx"), "1つ目の行");
            Eq(null, DupForm.DupPaths(null, it), "ジョブなし");
            it.Tag = null; Eq(null, DupForm.DupPaths(j, it), "見出しなどの行");

            // 一覧に付ける：既にある右クリックの項目は残し、区切りの後に足す
            System.Windows.Forms.ListView lv = new System.Windows.Forms.ListView();
            Ui.AddLocationMenu(lv, delegate(System.Windows.Forms.ListViewItem x) { return null; });
            True(lv.ContextMenuStrip != null && lv.ContextMenuStrip.Items[0].Text == Ui.LocationMenuText, "右クリックの項目");
            Eq(2, lv.ContextMenuStrip.Items.Count, "開く・パスをコピー");
            System.Windows.Forms.ListView lv2 = new System.Windows.Forms.ListView();
            lv2.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(); lv2.ContextMenuStrip.Items.Add("前からある項目");
            Ui.AddLocationMenu(lv2, delegate(System.Windows.Forms.ListViewItem x) { return null; });
            Eq(4, lv2.ContextMenuStrip.Items.Count, "前からある項目＋区切り＋2つ");
            Eq("前からある項目", lv2.ContextMenuStrip.Items[0].Text, "前からある項目は残す");

            // 各画面の一覧に付いている（重複・削除の候補を出す画面）
            foreach (string f in new string[] { "Dialogs.cs", "FolderDupForm.cs" })
            {
                string s = File.ReadAllText(Path.Combine(FindRepoFile("src"), f), Encoding.UTF8);
                int n = System.Text.RegularExpressions.Regex.Matches(s, "Ui\\.AddLocationMenu\\(").Count;
                True(n >= (f == "Dialogs.cs" ? 4 : 1), f + " の一覧に付いている（" + n + "か所）");
            }
        }

        [System.Runtime.Serialization.DataContract] public struct T124S { [System.Runtime.Serialization.DataMember] public DateTime When; [System.Runtime.Serialization.DataMember] public int N; }
        [System.Runtime.Serialization.DataContract] public class T124Inner { [System.Runtime.Serialization.DataMember] public DateTime When; [System.Runtime.Serialization.DataMember] public DateTime? Maybe; }
        [System.Runtime.Serialization.DataContract]
        public class T124Box
        {
            [System.Runtime.Serialization.DataMember] public DateTime A, B, Now;
            [System.Runtime.Serialization.DataMember] public DateTime? N1, N2;
            [System.Runtime.Serialization.DataMember] public List<DateTime> L = new List<DateTime>();
            [System.Runtime.Serialization.DataMember] public Dictionary<string, DateTime> D = new Dictionary<string, DateTime>();
            [System.Runtime.Serialization.DataMember] public List<T124S> LS = new List<T124S>();
            [System.Runtime.Serialization.DataMember] public T124S S;
            [System.Runtime.Serialization.DataMember] public T124Inner Child = new T124Inner();
            [System.Runtime.Serialization.DataMember] public T124Inner[] Arr = new T124Inner[] { new T124Inner(), null };
            [System.Runtime.Serialization.DataMember] public string Text = "そのまま";
        }

        private static void JsonDatesSafe()
        {
            TimeSpan off = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            Console.WriteLine("  このPCの時差: " + off + "（" + TimeZoneInfo.Local.Id + "）");
            DateTime now = new DateTime(2026, 11, 9, 19, 52, 0, DateTimeKind.Local);
            T124Box b = new T124Box();
            b.A = DateTime.MinValue; b.B = DateTime.MaxValue; b.Now = now;
            b.N1 = DateTime.MinValue; b.N2 = null;
            b.L.Add(DateTime.MinValue); b.L.Add(now); b.L.Add(DateTime.MaxValue);
            b.D["min"] = DateTime.MinValue; b.D["now"] = now;
            T124S s; s.When = DateTime.MinValue; s.N = 7; b.LS.Add(s); b.S = s;
            b.Child.When = DateTime.MinValue; b.Child.Maybe = DateTime.MinValue;
            b.Arr[0].When = DateTime.MaxValue;

            // 前の版の保存のしかたのままだと、東（日本など）では MinValue、西では MaxValue で失敗する（このPCで再現するか）
            bool rawFails = false;
            try { new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(DateTime)).WriteObject(new MemoryStream(), off > TimeSpan.Zero ? DateTime.MinValue : DateTime.MaxValue); }
            catch (System.Runtime.Serialization.SerializationException) { rawFails = true; }
            True(rawFails == (off != TimeSpan.Zero), "時差があれば、直さないと失敗する（時差 " + off + "・失敗 " + rawFails + "）");

            string json = Json.Serialize(b);   // 失敗しない
            T124Box r = Json.Deserialize<T124Box>(json);
            Eq(DateTime.MinValue, r.A, "未設定（MinValue）のまま");
            True(r.A == DateTime.MinValue, "== DateTime.MinValue の判定も今まで通り");
            Eq(DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond, r.B.Ticks / TimeSpan.TicksPerMillisecond, "MaxValue のまま（ミリ秒まで）");
            Eq(now, r.Now, "普通の日時はそのまま");
            Eq(DateTimeKind.Local, b.Now.Kind, "普通の日時の種類は変えない");
            Eq(DateTime.MinValue, r.N1.Value, "DateTime? の MinValue");
            True(!r.N2.HasValue, "null のまま");
            Eq(3, r.L.Count, "一覧の件数"); Eq(DateTime.MinValue, r.L[0], "一覧の中"); Eq(now, r.L[1], "一覧の中（普通）");
            Eq(DateTime.MinValue, r.D["min"], "辞書の中"); Eq(now, r.D["now"], "辞書の中（普通）");
            Eq(DateTime.MinValue, r.LS[0].When, "構造体の一覧の中"); Eq(7, r.LS[0].N, "構造体の他の値");
            Eq(DateTime.MinValue, r.S.When, "構造体の中");
            Eq(DateTime.MinValue, r.Child.When, "入れ子の中"); Eq(DateTime.MinValue, r.Child.Maybe.Value, "入れ子の DateTime?");
            Eq(DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond, r.Arr[0].When.Ticks / TimeSpan.TicksPerMillisecond, "配列の中");
            True(r.Arr[1] == null, "配列の null");
            Eq("そのまま", r.Text, "文字");
            True(b.A == DateTime.MinValue && b.L[0] == DateTime.MinValue, "元の値も比べた結果は同じ");

            // アプリが実際に保存するもの：お知らせ（対応日時が未設定）・設定・ジョブ
            Box bx = new Box("T124");
            Notice n = new Notice(); n.Time = DateTime.Now; n.Title = "テスト"; n.Text = "未対応";   // First・HandledAt は未設定
            NoticeBook nb = new NoticeBook(); nb.Items = new List<Notice>(); nb.Items.Add(n);
            string np = Path.Combine(bx.Root, "notices.json");
            Json.Save(np, nb);
            string problem;
            NoticeBook nr = Json.LoadSafe<NoticeBook>(np, out problem);
            True(nr != null && problem == null, "お知らせを保存・読み込みできる");
            Eq(DateTime.MinValue, nr.Items[0].HandledAt, "対応日時は未設定のまま");
            Eq(n.Time.ToString("yyyy/MM/dd HH:mm:ss"), nr.Items[0].Time.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"), "お知らせの日時");
            AppSettings st = new AppSettings(); if (st.Jobs == null) st.Jobs = new List<JobConfig>(); st.Jobs.Add(bx.Job);
            Json.Save(Path.Combine(bx.Root, "settings.json"), st);
            AppSettings sr = Json.LoadSafe<AppSettings>(Path.Combine(bx.Root, "settings.json"), out problem);
            True(sr != null && sr.Jobs.Count == 1 && sr.Jobs[0].Id == bx.Job.Id, "設定を保存・読み込みできる");
            True(bx.Job.Clone().Id == bx.Job.Id, "ジョブの複製（JSON を通す）");

            // 前の版のファイル（日本時間で保存された日時）もそのまま読める
            string old = "{\"A\":\"\\/Date(1762685520000+0900)\\/\",\"B\":\"\\/Date(1762685520000)\\/\"}";
            T124Box o = Json.Deserialize<T124Box>(old);
            Eq(new DateTime(2025, 11, 9, 10, 52, 0, DateTimeKind.Utc), o.A.ToUniversalTime(), "前の版の形（時差付き）");
            Eq(new DateTime(2025, 11, 9, 10, 52, 0, DateTimeKind.Utc), o.B.ToUniversalTime(), "前の版の形（UTC）");

            // 大きな一覧でも時間がかからない
            List<T124Inner> big = new List<T124Inner>();
            for (int i = 0; i < 100000; i++) { T124Inner x = new T124Inner(); x.When = i % 2 == 0 ? DateTime.MinValue : now; big.Add(x); }
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            JsonDates.MakeSafe(big);
            True(sw.ElapsedMilliseconds < 5000, "10万件: " + sw.ElapsedMilliseconds + "ms");
            Json.Serialize(big);
        }

        private static void RetryBlockedUpdate()
        {
            string keep = File.Exists(Updater.BlockedFile) ? File.ReadAllText(Updater.BlockedFile) : null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Updater.BlockedFile));
                File.WriteAllText(Updater.BlockedFile, "100.0.0.0");
                Eq("100.0.0.0", Updater.BlockedVersion(), "失敗した版の記録");
                Eq(null, Updater.ClearBlocked(), "記録を消す");
                Eq(null, Updater.BlockedVersion(), "記録がなくなる（次の確認でもう一度更新する）");
                True(!File.Exists(Updater.BlockedFile), "記録のファイルがない");
                Eq(null, Updater.ClearBlocked(), "記録がないときに消しても失敗しない");
                True(Updater.BlockedReasonPrefix.Length > 0, "理由の文の頭（画面で「もう一度試す」を出す判断に使う）");
            }
            finally
            {
                if (keep != null) File.WriteAllText(Updater.BlockedFile, keep); else if (File.Exists(Updater.BlockedFile)) File.Delete(Updater.BlockedFile);
            }
        }

        private static void CloudBasics()
        {
            True(CloudFiles.IsCloudTag(0x9000001A) && CloudFiles.IsCloudTag(0x9000401A) && CloudFiles.IsCloudTag(0x9000F01A), "クラウド用の印（CLOUD・CLOUD_4・CLOUD_F）");
            True(CloudFiles.IsCloudTag(CloudFiles.IO_REPARSE_TAG_ONEDRIVE), "OneDrive の印");
            True(!CloudFiles.IsCloudTag(CloudFiles.IO_REPARSE_TAG_MOUNT_POINT) && !CloudFiles.IsCloudTag(CloudFiles.IO_REPARSE_TAG_SYMLINK) && !CloudFiles.IsCloudTag(0), "ジャンクション・シンボリックリンクは違う");
            True(CloudFiles.IsCloudOnly((FileAttributes)CloudFiles.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS), "クラウドだけ（読むとダウンロード）");
            True(CloudFiles.IsCloudOnly((FileAttributes)CloudFiles.FILE_ATTRIBUTE_RECALL_ON_OPEN), "クラウドだけ（開くとダウンロード）");
            True(CloudFiles.IsCloudOnly(FileAttributes.Offline), "オフラインの印");
            True(!CloudFiles.IsCloudOnly(FileAttributes.Archive | FileAttributes.ReparsePoint | (FileAttributes)CloudFiles.FILE_ATTRIBUTE_PINNED), "このPCにあるファイル（常に保持）");

            Box b = new Box("T126");
            // ジャンクションは今まで通り中を調べない（ループ・二重のおそれ）
            Directory.CreateDirectory(Path.Combine(b.Src, "本物"));
            b.W(@"本物\a.txt", "a");
            string jn = Path.Combine(b.Src, "ジャンクション");
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + jn + "\" \"" + Path.Combine(b.Src, "本物") + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            System.Diagnostics.Process.Start(psi).WaitForExit();
            True(Directory.Exists(jn), "ジャンクションを作れた");
            Eq(CloudFiles.IO_REPARSE_TAG_MOUNT_POINT, CloudFiles.ReparseTag(jn), "ジャンクションの印");
            True(CloudFiles.SkipLinkDir(jn, File.GetAttributes(jn)), "ジャンクションは調べない");
            True(!CloudFiles.SkipLinkDir(Path.Combine(b.Src, "本物"), File.GetAttributes(Path.Combine(b.Src, "本物"))), "普通のフォルダは調べる");
            ScanResult sc = Scanner.Scan(b.Src, true, null, false, CancellationToken.None);
            True(sc.Files.ContainsKey(@"本物\a.txt") && !sc.Files.ContainsKey(@"ジャンクション\a.txt"), "ジャンクションの中は数えない");

            // OneDrive のフォルダ（環境変数）の中か
            string od = Path.Combine(b.Root, "OneDrive - 会社");
            Directory.CreateDirectory(od);
            string keepEnv = Environment.GetEnvironmentVariable("OneDriveCommercial");
            try
            {
                Environment.SetEnvironmentVariable("OneDriveCommercial", od);
                CloudFiles.ClearCache();
                True(CloudFiles.IsCloudFolder(Path.Combine(od, @"バックアップ\まだない")), "OneDrive の中（まだないフォルダも）");
                True(!CloudFiles.IsCloudFolder(b.Dst), "OneDrive の外");
                True(!CloudFiles.IsCloudFolder(od + "2"), "名前が似ているだけの別のフォルダ");
                // OneDrive へのコピー：コピーはでき、中身は同じ（印を付けられない普通のフォルダでもコピーは失敗しない）
                b.W("doc.txt", "OneDrive へ");
                byte[] h = SafeFs.CopyFile(Path.Combine(b.Src, "doc.txt"), Path.Combine(od, @"バックアップ\doc.txt"), File.GetLastWriteTimeUtc(Path.Combine(b.Src, "doc.txt")), null, CancellationToken.None);
                True(h != null && File.ReadAllText(Path.Combine(od, @"バックアップ\doc.txt")) == "OneDrive へ", "OneDrive のフォルダへコピーできる");
            }
            finally { Environment.SetEnvironmentVariable("OneDriveCommercial", keepEnv); CloudFiles.ClearCache(); }

            // このPCの空きを待つ
            long[] free = { 100 };
            CloudFiles.FreeBytesForTest = delegate(string f) { return free[0]; };
            try
            {
                TimeSpan w = CloudFiles.WaitForLocalSpace(od, 10, 50, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100), CancellationToken.None);
                True(w < TimeSpan.FromSeconds(1), "空きが足りていれば待たない");
                bool full = false;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                try { CloudFiles.WaitForLocalSpace(od, 80, 50, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), CancellationToken.None); }
                catch (IOException ex) { full = ErrorKind.Classify(ex) == ErrorKind.DiskFull; True(ex.Message.Contains("OneDrive"), "理由: " + ex.Message); }
                True(full, "待っても空かなければ容量不足として止める");
                True(sw.ElapsedMilliseconds >= 900 && sw.ElapsedMilliseconds < 5000, "決めた時間だけ待つ: " + sw.ElapsedMilliseconds + "ms");
                // 待っている間に空いたら続ける（アップロードで空いた）
                System.Threading.Timer t = new System.Threading.Timer(delegate { free[0] = 1000; }, null, 400, System.Threading.Timeout.Infinite);
                w = CloudFiles.WaitForLocalSpace(od, 80, 50, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), CancellationToken.None);
                True(w >= TimeSpan.FromMilliseconds(300) && w < TimeSpan.FromSeconds(5), "空いたら続ける: " + w.TotalMilliseconds + "ms");
                t.Dispose();
                free[0] = 0;
                CancellationTokenSource cts = new CancellationTokenSource(300);
                bool canceled = false;
                try { CloudFiles.WaitForLocalSpace(od, 80, 50, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), cts.Token); } catch (OperationCanceledException) { canceled = true; }
                True(canceled, "中止したら待つのをやめる");
                free[0] = -1;
                True(CloudFiles.WaitForLocalSpace(od, 80, 50, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), CancellationToken.None) < TimeSpan.FromSeconds(1), "空きが分からないときは待たない");
            }
            finally { CloudFiles.FreeBytesForTest = null; }
            True(CloudFiles.FreeBytes(b.Root) > 0, "本物の空き容量を読める");

            // 前の版の設定（項目がない）でも、初期値は「このPCに残さない」・2 GB
            AppSettings old = Json.Deserialize<AppSettings>("{\"FormatVersion\":1}");
            True(old.CloudFreeLocal, "初期値：このPCに残さない");
            Eq(2048, old.CloudReserveMB, "初期値：2 GB 残す");
        }

        // ---- Windows の Cloud Files（OneDrive と同じしくみ）を使った試験用の同期フォルダ ----
        [StructLayout(LayoutKind.Sequential)]
        private struct CF_SYNC_REGISTRATION
        {
            public uint StructSize;
            public IntPtr ProviderName, ProviderVersion, SyncRootIdentity;
            public uint SyncRootIdentityLength;
            public IntPtr FileIdentity;
            public uint FileIdentityLength;
            public Guid ProviderId;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct CF_SYNC_POLICIES
        {
            public uint StructSize;
            public ushort HydrationPrimary, HydrationModifier, PopulationPrimary, PopulationModifier;
            public uint InSync, HardLink, PlaceholderManagement;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct CF_PLACEHOLDER_CREATE_INFO
        {
            public IntPtr RelativeFileName;
            public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
            public uint FileAttributes;
            public long FileSize;
            public IntPtr FileIdentity;
            public uint FileIdentityLength;
            public uint Flags;
            public int Result;
            public long CreateUsn;
        }
        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)] private static extern int CfRegisterSyncRoot(string path, ref CF_SYNC_REGISTRATION reg, ref CF_SYNC_POLICIES pol, uint flags);
        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)] private static extern int CfUnregisterSyncRoot(string path);
        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)] private static extern int CfCreatePlaceholders(string baseDir, [In, Out] CF_PLACEHOLDER_CREATE_INFO[] arr, uint count, uint flags, out uint processed);

        private static int MakePlaceholder(string baseDir, string name, bool dir, long size, DateTime mtimeUtc, List<IntPtr> mem)
        {
            return MakePlaceholder(baseDir, name, dir, size, mtimeUtc, mem, true);
        }

        private static int MakePlaceholder(string baseDir, string name, bool dir, long size, DateTime mtimeUtc, List<IntPtr> mem, bool fullDir)
        {
            CF_PLACEHOLDER_CREATE_INFO[] a = new CF_PLACEHOLDER_CREATE_INFO[1];
            IntPtr n = Marshal.StringToHGlobalUni(name); mem.Add(n);
            byte[] id = Encoding.UTF8.GetBytes("id:" + name);
            IntPtr ip = Marshal.AllocHGlobal(id.Length); Marshal.Copy(id, 0, ip, id.Length); mem.Add(ip);
            long ft = mtimeUtc.ToFileTimeUtc();
            a[0].RelativeFileName = n;
            a[0].CreationTime = a[0].LastAccessTime = a[0].LastWriteTime = a[0].ChangeTime = ft;
            a[0].FileAttributes = dir ? 0x10u : 0x80u;
            a[0].FileSize = dir ? 0 : size;
            a[0].FileIdentity = ip; a[0].FileIdentityLength = (uint)id.Length;
            a[0].Flags = 0x2 /* MARK_IN_SYNC */ | (dir && fullDir ? 0x1u /* DISABLE_ON_DEMAND_POPULATION */ : 0);
            uint done;
            int hr = CfCreatePlaceholders(baseDir, a, 1, 0, out done);
            return hr != 0 ? hr : a[0].Result;
        }

        private static void CloudSyncRoot()
        {
            Box b = new Box("T127");
            string root = Path.Combine(b.Root, "雲の同期フォルダ");
            Directory.CreateDirectory(root);
            List<IntPtr> mem = new List<IntPtr>();
            CF_SYNC_REGISTRATION reg = new CF_SYNC_REGISTRATION();
            reg.StructSize = (uint)Marshal.SizeOf(typeof(CF_SYNC_REGISTRATION));
            reg.ProviderName = Marshal.StringToHGlobalUni("AutoCopyTest"); mem.Add(reg.ProviderName);
            reg.ProviderVersion = Marshal.StringToHGlobalUni("1.0"); mem.Add(reg.ProviderVersion);
            reg.ProviderId = Guid.NewGuid();
            CF_SYNC_POLICIES pol = new CF_SYNC_POLICIES();
            pol.StructSize = (uint)Marshal.SizeOf(typeof(CF_SYNC_POLICIES));
            pol.HydrationPrimary = 2;    // FULL
            pol.PopulationPrimary = 3;   // ALWAYS_FULL（一覧は最初から全部ある：同期の相手に頼まない）
            int hr;
            try { hr = CfRegisterSyncRoot(root, ref reg, ref pol, 0); }
            catch (DllNotFoundException) { hr = -1; }
            Console.WriteLine("  Cloud Files の同期フォルダの登録: 0x" + hr.ToString("X8"));
            True(hr == 0, "Cloud Files の同期フォルダを作れる（この結果が NG なら、この環境では Cloud Files を試せていない）: 0x" + hr.ToString("X8"));
            try
            {
                DateTime mt = new DateTime(2025, 4, 1, 3, 0, 0, DateTimeKind.Utc);
                long size = 5 * 1024 * 1024;
                Eq(0, MakePlaceholder(root, "雲フォルダ", true, 0, mt, mem), "クラウドのフォルダ（プレースホルダー）を作る");
                string cdir = Path.Combine(root, "雲フォルダ");
                Eq(0, MakePlaceholder(cdir, "a.bin", false, size, mt, mem), "クラウドだけのファイル a.bin");
                Eq(0, MakePlaceholder(cdir, "b.bin", false, size, mt.AddDays(1), mem), "クラウドだけのファイル b.bin（同じ大きさ）");
                int reparseDirs = 0;
                foreach (string dpath in new string[] { root, cdir })
                {
                    uint tg = CloudFiles.ReparseTag(dpath);
                    FileAttributes da = File.GetAttributes(dpath);
                    Console.WriteLine("  フォルダ " + Path.GetFileName(dpath) + " の印: 0x" + tg.ToString("X8") + "・属性: " + da);
                    if ((da & FileAttributes.ReparsePoint) != 0)
                    {
                        reparseDirs++;
                        True(CloudFiles.IsCloudTag(tg), "リパースポイントのクラウドのフォルダはクラウド用の印: 0x" + tg.ToString("X8"));
                        True(!CloudFiles.SkipLinkDir(dpath, da), "クラウドのフォルダは中を調べる（前の版は調べなかった）");
                    }
                }
                Console.WriteLine("  リパースポイントのフォルダ: " + reparseDirs + "個（0 のときは、フォルダの印の扱いをこの環境では試せていない）");
                uint ftag = CloudFiles.ReparseTag(Path.Combine(cdir, "a.bin"));
                Console.WriteLine("  ファイル a.bin の印: 0x" + ftag.ToString("X8") + "・属性: " + File.GetAttributes(Path.Combine(cdir, "a.bin")));
                True(CloudFiles.IsCloudFolder(root) && CloudFiles.IsCloudFolder(Path.Combine(root, @"新しい\フォルダ")), "同期フォルダの中と分かる（まだないフォルダも）");
                True(!CloudFiles.IsCloudFolder(b.Dst), "普通のフォルダは同期フォルダではない");

                // 中まで調べる
                ScanResult sc = Scanner.Scan(root, true, null, false, CancellationToken.None);
                True(sc.Files.ContainsKey(@"雲フォルダ\a.bin") && sc.Files.ContainsKey(@"雲フォルダ\b.bin"), "クラウドのフォルダの中のファイルを数える: " + string.Join(",", new List<string>(sc.Files.Keys).ToArray()));
                ScanItem ai = sc.Files[@"雲フォルダ\a.bin"];
                Eq(size, ai.Size, "大きさ（中身はなくても本当の大きさ）");
                True(CloudFiles.IsCloudOnly(ai.Attr), "クラウドだけのファイルと分かる: " + ai.Attr);
                // 中身はこのPCにない（同期の相手がいないので、読もうとすると失敗する＝ダウンロードが起きたら分かる）
                bool readFails = false;
                try { using (FileStream fs = new FileStream(Path.Combine(cdir, "a.bin"), FileMode.Open, FileAccess.Read)) fs.ReadByte(); }
                catch (Exception ex) { readFails = true; Console.WriteLine("  クラウドだけのファイルを読むと: " + ex.Message); }
                True(readFails, "中身はこのPCにない（読むとダウンロードが必要）");

                // ジョブ：コピー元にも a.bin（同じ大きさ・日時）と新しいファイル。コピー先は同期フォルダ
                Directory.CreateDirectory(Path.Combine(b.Src, "雲フォルダ"));
                string sa = Path.Combine(b.Src, @"雲フォルダ\a.bin");
                using (FileStream fs = new FileStream(sa, FileMode.Create)) fs.SetLength(size);
                File.SetLastWriteTimeUtc(sa, mt);
                b.W("新しい.txt", "このPCで作ったファイル");
                b.Job.Dest = root;
                b.Apply();
                RunResult r = b.Run();
                Eq(0, r.Errors, "エラーなし");
                Eq(1, r.Copied, "新しいファイルだけコピー（クラウドだけの a.bin は同じなので読まない・上書きしない）");
                string nd = Path.Combine(root, "新しい.txt");
                True(File.Exists(nd) && File.ReadAllText(nd) == "このPCで作ったファイル", "同期フォルダへコピーできる");
                int na = (int)File.GetAttributes(nd);
                Console.WriteLine("  コピーしたファイルの属性: 0x" + na.ToString("X"));
                True((na & CloudFiles.FILE_ATTRIBUTE_UNPINNED) != 0, "コピーしたファイルに「このPCに残さない」の印（アップロード後に空く）: 0x" + na.ToString("X"));
                True(CloudFiles.IsCloudOnly(File.GetAttributes(Path.Combine(cdir, "a.bin"))), "a.bin はクラウドだけのまま（ダウンロードしていない）");

                // コピー先の整理（重複チェック）：コピー先の b.bin（コピー元にない・a.bin と同じ大きさ）を読まない
                DupResult d = DupChecker.Run(b.Job, AppCore.Settings, b.State, CancellationToken.None);
                True(d != null, "コピー先の整理ができる");
                True(d.CloudSkipped >= 1, "クラウドだけのファイルは中身を調べない: " + d.CloudSkipped + "件");
                True(CloudFiles.IsCloudOnly(File.GetAttributes(Path.Combine(cdir, "b.bin"))), "b.bin はクラウドだけのまま");

                // フォルダの重複ファイル探し：読まない
                FolderDupResult fr = FolderDup.Find(root, true, 0, DupBasis.Content, CancellationToken.None, null);
                Eq(2, fr.CloudSkipped, "クラウドだけの2件は中身を比べない");
                Eq(0, fr.Errors.Count, "読めないエラーにしない: " + string.Join(" / ", fr.Errors.ToArray()));
                FolderDupResult fn = FolderDup.Find(root, true, 0, DupBasis.Name | DupBasis.Size, CancellationToken.None, null);
                Eq(0, fn.CloudSkipped, "中身を基準にしないときは、そもそも読まない");

                // 使用量（容量の内訳）もクラウドのフォルダの中を数える
                True(CloudFiles.IsCloudOnly(File.GetAttributes(Path.Combine(cdir, "a.bin"))) && CloudFiles.IsCloudOnly(File.GetAttributes(Path.Combine(cdir, "b.bin"))), "最後までダウンロードしていない");
            }
            finally
            {
                try { CfUnregisterSyncRoot(root); } catch (Exception) { }
            }

            // OneDrive と同じ「一覧を後から受け取る」同期フォルダ：フォルダがリパースポイント（クラウド用の印）になるか、なれば中を調べるか
            string root2 = Path.Combine(b.Root, "雲の同期フォルダ2");
            Directory.CreateDirectory(root2);
            CF_SYNC_POLICIES pol2 = pol;
            pol2.PopulationPrimary = 0;   // PARTIAL（OneDrive と同じ）
            reg.ProviderId = Guid.NewGuid();
            int hr2 = CfRegisterSyncRoot(root2, ref reg, ref pol2, 0);
            Console.WriteLine("  同期フォルダ2（一覧を後から受け取る）の登録: 0x" + hr2.ToString("X8"));
            try
            {
                if (hr2 == 0)
                {
                    DateTime mt2 = new DateTime(2025, 4, 1, 3, 0, 0, DateTimeKind.Utc);
                    int h1 = MakePlaceholder(root2, "雲フォルダ", true, 0, mt2, mem, true);
                    string d2 = Path.Combine(root2, "雲フォルダ");
                    int h2 = h1 == 0 ? MakePlaceholder(d2, "c.txt", false, 1234, mt2, mem) : h1;
                    Console.WriteLine("  同期フォルダ2：フォルダ 0x" + h1.ToString("X8") + "・ファイル 0x" + h2.ToString("X8"));
                    if (h1 == 0)
                    {
                        FileAttributes da = File.GetAttributes(d2);
                        uint tg = CloudFiles.ReparseTag(d2);
                        Console.WriteLine("  同期フォルダ2 のフォルダの印: 0x" + tg.ToString("X8") + "・属性: " + da);
                        ScanResult s2;
                        try { s2 = Scanner.Scan(root2, true, null, false, CancellationToken.None); }
                        catch (Exception ex) { Console.WriteLine("  同期フォルダ2 を調べられません（同期の相手がいないため）: " + ex.Message); s2 = null; }
                        if (s2 != null) Console.WriteLine("  同期フォルダ2 を調べた結果: ファイル " + string.Join(",", new List<string>(s2.Files.Keys).ToArray()) + "・読めなかったフォルダ " + string.Join(",", s2.FailedDirs.ToArray()));
                        if (s2 != null && (da & FileAttributes.ReparsePoint) != 0)
                        {
                            True(CloudFiles.IsCloudTag(tg), "同期フォルダ2：フォルダはクラウド用の印: 0x" + tg.ToString("X8"));
                            True(!CloudFiles.SkipLinkDir(d2, da), "同期フォルダ2：クラウドのフォルダは中を調べる（前の版は黙って飛ばしていた）");
                            True(s2.Files.ContainsKey(@"雲フォルダ\c.txt") || s2.FailedDirs.Contains("雲フォルダ"), "同期フォルダ2：中のファイルを数える（読めなければ「読めなかった」として扱い、黙って飛ばさない）");
                        }
                    }
                }
            }
            finally
            {
                try { CfUnregisterSyncRoot(root2); } catch (Exception) { }
                foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            }
        }

        private static List<System.Windows.Forms.Control> AllControls(System.Windows.Forms.Control root)
        {
            List<System.Windows.Forms.Control> l = new List<System.Windows.Forms.Control>();
            Stack<System.Windows.Forms.Control> st = new Stack<System.Windows.Forms.Control>(); st.Push(root);
            while (st.Count > 0) { System.Windows.Forms.Control c = st.Pop(); l.Add(c); foreach (System.Windows.Forms.Control k in c.Controls) st.Push(k); }
            return l;
        }

        private static void UiConsistency()
        {
            // 1) 並列数：自動のときの説明と、実際に使う数（CopyEngine：0 = 4件）が同じ
            string eng = File.ReadAllText(Path.Combine(FindRepoFile("src"), "CopyEngine.cs"), Encoding.UTF8);
            True(eng.Contains("int degree = job.Parallel > 0 ? job.Parallel : 4;"), "自動のときは同時に4件（コピーの処理）");
            using (JobEditForm jf = new JobEditForm(new JobConfig(), true))
            {
                bool found = false;
                foreach (System.Windows.Forms.Control c in AllControls(jf))
                    if (c is System.Windows.Forms.CheckBox && c.Text.StartsWith("自動（")) { found = true; True(c.Text.Contains("最大 4 件") && !c.Text.Contains("2"), "並列数の説明: " + c.Text); }
                True(found, "並列数の「自動」の欄がある");
            }

            // 2) 3) 上書きの確認：同期の行は橙、「共有フォルダの方を使う」は同期のものだけにチェックしたときに押せる
            Box b = new Box("T128");
            b.Job.Sync = true; b.Job.Name = "同期のジョブ";
            JobConfig normal = b.Job.Clone(); normal.Id = Guid.NewGuid().ToString("N"); normal.Name = "普通のジョブ"; normal.Sync = false;
            b.App.Jobs.Add(normal);
            b.Apply();
            ConflictItem c1 = new ConflictItem(); c1.Rel = "同期.txt"; c1.Reason = "both";
            ConflictItem c2 = new ConflictItem(); c2.Rel = "普通.txt"; c2.Reason = "newer";
            JobState s1 = AppCore.GetState(b.Job.Id), s2 = AppCore.GetState(normal.Id);
            lock (s1) s1.Conflicts.Add(c1);
            lock (s2) s2.Conflicts.Add(c2);
            try
            {
                using (ConflictForm cf = new ConflictForm())
                {
                    System.Windows.Forms.ListView lv = null;
                    System.Windows.Forms.Button ts = null;
                    foreach (System.Windows.Forms.Control c in AllControls(cf))
                    {
                        if (c is System.Windows.Forms.ListView && lv == null) lv = (System.Windows.Forms.ListView)c;
                        if (c is System.Windows.Forms.Button && c.Text.StartsWith("共有フォルダの方を使う")) ts = (System.Windows.Forms.Button)c;
                    }
                    True(lv != null && ts != null, "一覧とボタンがある");
                    IntPtr hf = cf.Handle, hl = lv.Handle;   // チェックの変化を受け取れるように、画面を作っておく（表示はしない）
                    System.Windows.Forms.ListViewItem syncRow = null, normRow = null;
                    foreach (System.Windows.Forms.ListViewItem it in lv.Items) { if (it.Text == "同期.txt") syncRow = it; if (it.Text == "普通.txt") normRow = it; }
                    True(syncRow != null && normRow != null, "2件とも出る");
                    Eq(Ui.Warn, syncRow.ForeColor, "同期のジョブの行は橙のまま（前は直後に黒で上書きしていた）");
                    Eq(System.Drawing.SystemColors.WindowText, normRow.ForeColor, "普通のジョブの行は黒");
                    True(!ts.Enabled && cf.TakeSharedReason() != null, "何もチェックしていないときは押せない: " + cf.TakeSharedReason());
                    syncRow.Checked = true;
                    System.Windows.Forms.Application.DoEvents();   // 押せる・押せないは、まとめて後で調べる
                    True(ts.Enabled && cf.TakeSharedReason() == null, "同期のものだけチェック：押せる");
                    normRow.Checked = true;
                    System.Windows.Forms.Application.DoEvents();
                    True(!ts.Enabled && cf.TakeSharedReason().Contains("普通のジョブ"), "普通のジョブも混ざる：押せない・理由にジョブ名: " + cf.TakeSharedReason());
                    syncRow.Checked = false;
                    System.Windows.Forms.Application.DoEvents();
                    True(!ts.Enabled, "普通のジョブだけ：押せない");
                }
            }
            finally { lock (s1) s1.Conflicts.Clear(); lock (s2) s2.Conflicts.Clear(); }

            // 5) フォルダの重複ファイル探し：「中身」のときは「大きさ」をチェックした状態で灰色（含まれる）。やめたら前の選択に戻す。検索の条件は前と同じ
            using (FolderDupForm fd = new FolderDupForm(null))
            {
                IntPtr hfd = fd.Handle;
                True(fd.ContentBox.Checked && !fd.SizeBox.Enabled && fd.SizeBox.Checked, "中身：大きさは「含まれる」表示");
                Eq(DupBasis.Content, fd.Basis, "条件は中身だけ（大きさは中身に含まれる・前と同じ）");
                fd.ContentBox.Checked = false;
                True(fd.SizeBox.Enabled && !fd.SizeBox.Checked, "中身をやめたら、前の選択（チェックなし）に戻す");
                fd.SizeBox.Checked = true;
                fd.ContentBox.Checked = true;
                True(!fd.SizeBox.Enabled && fd.SizeBox.Checked, "中身にしたら灰色・チェック");
                fd.ContentBox.Checked = false;
                True(fd.SizeBox.Enabled && fd.SizeBox.Checked, "中身をやめたら、前の選択（チェックあり）に戻す");
                fd.Basis = DupBasis.Name | DupBasis.Size;
                Eq(DupBasis.Name | DupBasis.Size, fd.Basis, "名前＋大きさ");
                fd.Basis = DupBasis.Content | DupBasis.Mtime;
                Eq(DupBasis.Content | DupBasis.Mtime, fd.Basis, "中身＋日時");
                True(fd.SizeBox.Checked && !fd.SizeBox.Enabled, "中身のときは「含まれる」表示");
            }

            // 6) 7) 操作の対象の表示
            True(MainForm.TargetText(0, null, 0).Contains("ジョブがありません"), "ジョブなし");
            True(MainForm.TargetText(0, null, 3).Contains("選んでいません"), "選んでいない");
            string one = MainForm.TargetText(0, "経理", 1);
            True(one.Contains("「経理」") && one.Contains("選んだジョブを実行"), "1件だけ：編集などはそのジョブ・実行は選んでから: " + one);
            Eq("操作の対象：「経理」", MainForm.TargetText(1, "経理", 3), "1件選んだ");
            True(MainForm.TargetText(2, "経理", 3).Contains("2件") && MainForm.TargetText(2, "経理", 3).Contains("「経理」"), "複数選んだ");
        }

        private static void UiRefine()
        {
            // ツールバー：まとまりの中では折り返さない（「ジョブを追加・編集・複製・削除」は同じまとまり）
            using (MainForm mf = new MainForm())
            {
                System.Windows.Forms.Control add = null;
                foreach (System.Windows.Forms.Control c in AllControls(mf)) if (c is System.Windows.Forms.Button && c.Text == "ジョブを追加") add = c;
                True(add != null, "「ジョブを追加」がある");
                System.Windows.Forms.FlowLayoutPanel g = add.Parent as System.Windows.Forms.FlowLayoutPanel;
                True(g != null && !g.WrapContents, "まとまりの中では折り返さない");
                List<string> names = new List<string>();
                foreach (System.Windows.Forms.Control c in g.Controls) if (c is System.Windows.Forms.Button) names.Add(c.Text);
                Eq("ジョブを追加,編集,複製,削除", string.Join(",", names.ToArray()), "ジョブの管理のまとまり");
                System.Windows.Forms.FlowLayoutPanel bar = g.Parent as System.Windows.Forms.FlowLayoutPanel;
                True(bar != null && bar.WrapContents, "まとまりの区切りで折り返す");
                int buttons = 0;
                foreach (System.Windows.Forms.Control c in AllControls(bar)) if (c is System.Windows.Forms.Button) buttons++;
                Eq(17, buttons, "ボタンの数は前と同じ（省いたもの・増やしたものはない）");
                Eq(null, Ui.AutoCancel(mf), "メイン画面は Esc で閉じない（閉じるボタンがない）");
            }

            // 枠の幅を揃える
            System.Windows.Forms.FlowLayoutPanel body = Ui.Body();
            body.Size = new System.Drawing.Size(Ui.S(600), Ui.S(400));
            System.Windows.Forms.GroupBox g1 = Ui.Section("短い", Ui.L("あ"));
            System.Windows.Forms.GroupBox g2 = Ui.Section("長い", Ui.L(new string('あ', 20)));
            body.Controls.Add(g1); body.Controls.Add(g2);
            Ui.EvenSections(body);
            body.PerformLayout();
            True(g1.Width == g2.Width && g1.Width > Ui.S(400), "枠の幅が揃う: " + g1.Width + " / " + g2.Width);
            True(g1.Right <= body.ClientSize.Width, "横にはみ出さない");

            // Esc：「閉じる」「キャンセル」「やめる」に割り当てる（実行・削除などには割り当てない）
            using (FolderDupForm fd = new FolderDupForm(null))
            {
                System.Windows.Forms.Button cb = Ui.AutoCancel(fd);
                True(cb != null && cb.Text.Contains("閉じる"), "フォルダの重複ファイル探し：Esc = 閉じる");
                Eq(System.Windows.Forms.DialogResult.None, cb.DialogResult, "押したときの動きは前と同じ");
            }
            using (SettingsForm sf = new SettingsForm())
            {
                System.Windows.Forms.Button cb = Ui.AutoCancel(sf);
                True(cb != null && cb.Text.Contains("キャンセル"), "アプリ設定：Esc = キャンセル（保存しない）");
            }
            True(Ui.IsCloseText("閉じる（後で決める）") && Ui.IsCloseText("キャンセル") && Ui.IsCloseText("やめる"), "閉じる系の名前");
            True(!Ui.IsCloseText("削除する") && !Ui.IsCloseText("ミラーにする") && !Ui.IsCloseText("この内容で今すぐ実行") && !Ui.IsCloseText(null), "実行・削除には割り当てない");

            // 操作の対象：意味は同じで「｜」で区切った短い表示
            string multi = MainForm.TargetText(3, "経理", 5);
            True(multi.StartsWith("3件 選択｜") && multi.Contains("実行・中止：3件") && multi.Contains("先頭の「経理」"), "複数: " + multi);
            True(MainForm.TargetText(0, "経理", 1).Length < 90, "1件のときの説明が短い");
        }

        private static System.Windows.Forms.ListView FirstList(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control c in AllControls(root)) if (c is System.Windows.Forms.ListView) return (System.Windows.Forms.ListView)c;
            return null;
        }

        private static void ConflictManyItems()
        {
            Box b = new Box("T130");
            b.Apply();
            JobState st = AppCore.GetState(b.Job.Id);
            const int N = 30000;
            long now = DateTime.UtcNow.Ticks;
            lock (st)
            {
                for (int i = 0; i < N; i++)
                {
                    ConflictItem c = new ConflictItem(); c.Rel = @"フォルダ" + (i % 50) + @"\ファイル" + i + ".xlsx"; c.Reason = "newer";
                    c.SrcMtime = now - TimeSpan.TicksPerHour * (i % 2 == 0 ? 2 : 0); c.DstMtime = now - TimeSpan.TicksPerHour; c.SrcSize = 100; c.DstSize = 200;
                    st.Conflicts.Add(c);
                }
            }
            try
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                using (ConflictForm cf = new ConflictForm())
                {
                    System.Windows.Forms.ListView lv = FirstList(cf);
                    IntPtr h = cf.Handle, hl = lv.Handle;   // 画面を作る（一覧に行を入れる。前はここで1行ごとに全件を読んでいた）
                    System.Windows.Forms.Application.DoEvents();
                    long open = sw.ElapsedMilliseconds;
                    Console.WriteLine("  3万件の上書きの確認を開くまで: " + open + "ms");
                    Eq(N, lv.Items.Count, "3万件とも出る");
                    True(open < 30000, "30秒以内に開く: " + open + "ms");
                    sw.Restart();
                    int n = cf.CheckNewer(ConflictForm.NewerSource);
                    System.Windows.Forms.Application.DoEvents();
                    Eq(N / 2, n, "コピー元が新しい半分にチェック");
                    Eq(N / 2, lv.CheckedItems.Count, "チェックの数");
                    True(sw.ElapsedMilliseconds < 30000, "まとめてチェックしても固まらない: " + sw.ElapsedMilliseconds + "ms");
                    True(cf.Text.Contains(Fmt.Count(N).Replace(",", "")) || cf.Text.Contains(N.ToString()), "題名に件数: " + cf.Text);
                }
            }
            finally { lock (st) st.Conflicts.Clear(); }
        }

        private static void ConflictNewer()
        {
            JobConfig j = new JobConfig(); j.ToleranceSeconds = 2;
            ConflictItem c = new ConflictItem();
            long t = new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc).Ticks;
            c.SrcMtime = t + TimeSpan.TicksPerMinute; c.DstMtime = t;
            Eq(ConflictForm.NewerSource, ConflictForm.WhichNewer(j, c), "コピー元が新しい");
            Eq(AppCore.ActOverwrite, ConflictForm.NewerAction(j, c), "→ 上書きする");
            c.SrcMtime = t; c.DstMtime = t + TimeSpan.TicksPerMinute;
            Eq(ConflictForm.NewerDest, ConflictForm.WhichNewer(j, c), "コピー先が新しい");
            Eq(AppCore.ActSkipOnce, ConflictForm.NewerAction(j, c), "→ コピー先を残す（今回はスキップ）");
            j.Sync = true;
            Eq(AppCore.ActTakeShared, ConflictForm.NewerAction(j, c), "同期で共有フォルダが新しい → 共有フォルダの方を使う");
            c.SrcMtime = t + TimeSpan.TicksPerMinute; c.DstMtime = t;
            Eq(AppCore.ActOverwrite, ConflictForm.NewerAction(j, c), "同期でこのPCが新しい → 上書きする（このPCの方を使う）");
            c.SrcMtime = t + TimeSpan.TicksPerSecond; c.DstMtime = t;
            Eq(ConflictForm.NewerUnknown, ConflictForm.WhichNewer(j, c), "差が許容範囲（2秒）以内は決めない");
            Eq(null, ConflictForm.NewerAction(j, c), "→ 何もしない");
            c.SrcMtime = t; c.DstMtime = t;
            Eq(null, ConflictForm.NewerAction(j, c), "同じ日時（サイズ違い）は何もしない");

            // 画面：新しい方にまとめてチェック（ほかのチェックは外す）
            Box b = new Box("T131");
            b.Apply();
            JobState st = AppCore.GetState(b.Job.Id);
            ConflictItem a1 = new ConflictItem(); a1.Rel = "元が新しい.txt"; a1.Reason = "newer"; a1.SrcMtime = t + TimeSpan.TicksPerHour; a1.DstMtime = t;
            ConflictItem a2 = new ConflictItem(); a2.Rel = "先が新しい.txt"; a2.Reason = "newer"; a2.SrcMtime = t; a2.DstMtime = t + TimeSpan.TicksPerHour;
            ConflictItem a3 = new ConflictItem(); a3.Rel = "同じ.txt"; a3.Reason = "samedate"; a3.SrcMtime = t; a3.DstMtime = t;
            lock (st) { st.Conflicts.Add(a1); st.Conflicts.Add(a2); st.Conflicts.Add(a3); }
            try
            {
                using (ConflictForm cf = new ConflictForm())
                {
                    System.Windows.Forms.ListView lv = FirstList(cf);
                    IntPtr h = cf.Handle, hl = lv.Handle;
                    Eq(1, cf.CheckNewer(ConflictForm.NewerSource), "コピー元が新しいもの");
                    Eq("元が新しい.txt", lv.CheckedItems[0].Text, "チェックした行");
                    Eq(1, cf.CheckNewer(ConflictForm.NewerDest), "コピー先が新しいもの");
                    Eq(1, lv.CheckedItems.Count, "前のチェックは外す");
                    Eq("先が新しい.txt", lv.CheckedItems[0].Text, "チェックした行（コピー先）");
                    bool hasButton = false, hasMenu = false;
                    foreach (System.Windows.Forms.Control x in AllControls(cf))
                    {
                        if (x is System.Windows.Forms.Button && x.Text.StartsWith("それぞれ新しい方を使う")) hasButton = true;
                        if (x is System.Windows.Forms.Button && x.Text.StartsWith("新しい方で選ぶ")) hasMenu = true;
                    }
                    True(hasButton && hasMenu, "「新しい方で選ぶ ▾」と「それぞれ新しい方を使う…」がある");
                }
            }
            finally { lock (st) st.Conflicts.Clear(); }
        }

        private static void BulkDeleteTool()
        {
            // 条件
            Eq(".tmp,.bak,.log", string.Join(",", BulkDeleteCriteria.ParseExtensions(" .tmp, bak　*.LOG ").ToArray()), "拡張子の書き方");
            Eq("*~*.xlsx|Thumbs.db|新しい フォルダ*", string.Join("|", BulkDeleteCriteria.ParseNames("*~*.xlsx; Thumbs.db\n新しい フォルダ*").ToArray()), "名前の書き方（空白は名前の一部）");
            BulkDeleteCriteria c = new BulkDeleteCriteria();
            True(!c.HasCondition, "条件なし");
            c.Extensions.Add(".tmp");
            True(c.HasCondition, "条件あり");
            True(c.Matches(@"a\b.TMP", 10, 0, FileAttributes.Archive), "拡張子（大文字小文字は区別しない）");
            True(!c.Matches(@"a\b.tmpx", 10, 0, FileAttributes.Archive), "違う拡張子");
            True(!c.Matches(@"b.tmp", 10, 0, FileAttributes.System), "システムファイルは対象外");
            True(!c.Matches(@"b.tmp", 10, 0, FileAttributes.Hidden), "隠しファイルは初期値では対象外");
            c.IncludeHidden = true;
            True(c.Matches(@"b.tmp", 10, 0, FileAttributes.Hidden), "隠しファイルも対象にする");
            BulkDeleteCriteria n = new BulkDeleteCriteria(); n.NamePatterns.Add("*~*.xlsx"); n.NamePatterns.Add("Thumbs.db");
            True(n.Matches(@"x\見積~1.xlsx", 1, 0, 0) && n.Matches("thumbs.db", 1, 0, 0) && !n.Matches("見積.xlsx", 1, 0, 0), "名前（* と ;）");
            BulkDeleteCriteria s = new BulkDeleteCriteria(); s.MinBytes = 100; s.MaxBytes = 200;
            True(s.Matches("a", 100, 0, 0) && s.Matches("a", 200, 0, 0) && !s.Matches("a", 99, 0, 0) && !s.Matches("a", 201, 0, 0), "大きさ（以上・以下）");
            long day = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            BulkDeleteCriteria o = new BulkDeleteCriteria(); o.OlderThanUtc = day;
            True(o.Matches("a", 1, day - 1, 0) && !o.Matches("a", 1, day, 0), "更新日時がこの日より前");

            // 使えないフォルダ
            True(BulkDelete.CheckFolder(Path.GetPathRoot(Path.GetTempPath())) != null, "ドライブ全体は使えない");
            True(BulkDelete.CheckFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) != null, "Windows のフォルダは使えない");
            True(BulkDelete.CheckFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")) != null, "Windows の中も使えない");
            True(BulkDelete.CheckFolder(AppPaths.Local) != null, "このアプリのフォルダは使えない");
            True(BulkDelete.CheckFolder("") != null && BulkDelete.CheckFolder(@"Q:\ないフォルダ") != null, "空欄・ないフォルダ");
            True(BulkDelete.CheckFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) != null, "ユーザーのフォルダ全体は使えない");

            Box b = new Box("T132");
            string root = Path.Combine(b.Root, "片付け");
            Eq(null, BulkDelete.CheckFolder(b.Root), "普通のフォルダは使える: " + BulkDelete.CheckFolder(b.Root));
            Action<string, int> put = delegate(string rel, int size)
            {
                string f = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f, new byte[size]);
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-1));
            };
            put("a.tmp", 10); put(@"sub\b.tmp", 20); put(@"sub\keep.xlsx", 30); put(@"sub\deep\c.TMP", 40); put("d.bak", 50);
            put(@"_AutoCopy\記録.tmp", 5);   // このアプリの記録は対象外
            File.SetAttributes(Path.Combine(root, "d.bak"), FileAttributes.Hidden);
            BulkDeleteCriteria q = new BulkDeleteCriteria(); q.Extensions = BulkDeleteCriteria.ParseExtensions(".tmp .bak");
            int scanned, failed;
            List<BulkDeleteItem> found = BulkDelete.Find(root, true, q, CancellationToken.None, out scanned, out failed);
            Eq("a.tmp,sub\\b.tmp,sub\\deep\\c.TMP", string.Join(",", found.ConvertAll(delegate(BulkDeleteItem x) { return x.Rel; }).ToArray()), "見つかったもの（隠し・_AutoCopy は除く）");
            True(File.Exists(Path.Combine(root, "a.tmp")), "探しただけでは削除しない");
            List<BulkDeleteItem> top = BulkDelete.Find(root, false, q, CancellationToken.None, out scanned, out failed);
            Eq(1, top.Count, "サブフォルダを含めないとき");

            // 削除：チェックしたものだけ、ごみ箱へ。探した後に変わったものは削除しない
            True(BulkDelete.CanRecycle(root) || root.StartsWith(@"\\"), "このPCのドライブはごみ箱に移せる: " + root);
            List<BulkDeleteItem> sel = new List<BulkDeleteItem>(found);
            sel.RemoveAll(delegate(BulkDeleteItem x) { return x.Rel == @"sub\b.tmp"; });   // b.tmp はチェックを外した
            File.WriteAllBytes(Path.Combine(root, @"sub\deep\c.TMP"), new byte[99]);   // 探した後に変わった
            BulkDeleteResult r = BulkDelete.Delete(root, sel, BulkDelete.CanRecycle(root), CancellationToken.None, null);
            Eq(1, r.Deleted, "削除した数: " + string.Join(" / ", r.Errors.ToArray()));
            Eq(1, r.Changed.Count, "変わったものは削除しない");
            True(!File.Exists(Path.Combine(root, "a.tmp")), "チェックしたものは削除した");
            True(File.Exists(Path.Combine(root, @"sub\b.tmp")), "チェックを外したものは残る");
            True(File.Exists(Path.Combine(root, @"sub\deep\c.TMP")), "探した後に変わったものは残る");
            True(File.Exists(Path.Combine(root, @"sub\keep.xlsx")) && File.Exists(Path.Combine(root, "d.bak")) && File.Exists(Path.Combine(root, @"_AutoCopy\記録.tmp")), "条件に合わないもの・隠し・記録は残る");

            // 完全に削除（ネットワークなどごみ箱がない場所の扱い）
            List<BulkDeleteItem> again = BulkDelete.Find(root, true, q, CancellationToken.None, out scanned, out failed);
            BulkDeleteResult r2 = BulkDelete.Delete(root, again, false, CancellationToken.None, null);
            Eq(2, r2.Deleted, "完全に削除: " + string.Join(" / ", r2.Errors.ToArray()));
            True(!File.Exists(Path.Combine(root, @"sub\b.tmp")) && File.Exists(Path.Combine(root, @"sub\keep.xlsx")), "対象だけ削除");

            // 画面：条件を変えたら一覧を消して、もう一度探すまで削除できない
            using (BulkDeleteForm f = new BulkDeleteForm(root))
            {
                IntPtr h = f.Handle;
                True(f.DeleteReason() != null && !f.DeleteButton.Enabled, "探す前は削除できない");
                put("e.tmp", 5);
                f.SetInputs(root, ".tmp", "");
                f.Fill(root, BulkDelete.Find(root, true, f.Criteria(), CancellationToken.None, out scanned, out failed), scanned);
                Eq(1, f.List.Items.Count, "探した一覧");
                True(f.List.Items[0].Checked, "はじめは全部にチェック");
                True(f.DeleteReason() == null && f.DeleteButton.Enabled, "探した後は削除できる");
                f.SetInputs(root, ".bak", "");
                Eq(0, f.List.Items.Count, "条件を変えたら一覧を消す");
                True(f.DeleteReason() != null && !f.DeleteButton.Enabled, "もう一度探すまで削除できない");
                True(File.Exists(Path.Combine(root, "e.tmp")), "画面の操作だけでは削除しない");
            }
        }

        private static void TimeOnlyDifference()
        {
            Box b = new Box("T133");
            b.W("見積.xlsx", "中身A-12345");
            b.W(@"sub\表.csv", "1,2,3");
            RunResult r = b.Run();
            Eq(2, r.Copied, "はじめのコピー");
            string sa = Path.Combine(b.Src, "見積.xlsx"), da = Path.Combine(b.Dst, "見積.xlsx");
            FileRecord rec;
            lock (b.State) rec = b.State.Files["見積.xlsx"];
            True(rec.Hash != null, "コピーしたときの MD5 を記録している");

            // 1) コピー先（NAS など）が日時を残さなかった：コピー先の日時がコピーした時刻になっている（記録もその時刻）
            DateTime late = File.GetLastWriteTimeUtc(da).AddHours(3);
            File.SetLastWriteTimeUtc(da, late);
            lock (b.State) b.State.Files["見積.xlsx"].DestMtime = late.Ticks;
            // コピー元の日時が少しだけずれた（中身は同じ：クラウドの同期・ファイルの仕組みの細かさなど）
            File.SetLastWriteTimeUtc(sa, File.GetLastWriteTimeUtc(sa).AddSeconds(5));
            r = b.Run();
            Eq(0, r.Conflicts, "「コピー先が新しい」の確認にしない");
            Eq(0, r.Copied + r.Overwritten, "コピーしない");
            Eq(1, r.TimeOnlySame, "日時だけ違い中身が同じ（記録の MD5 と比べた）");
            r = b.Run();
            Eq(0, r.Conflicts + r.Copied + r.Overwritten + r.TimeOnlySame, "次の実行では何もない（記録を直した）");

            // 2) 記録がない（前の版・記録が消えた）：コピー先の日時が違う・大きさは同じ・中身は同じ
            string sc = Path.Combine(b.Src, @"sub\表.csv"), dc = Path.Combine(b.Dst, @"sub\表.csv");
            lock (b.State) b.State.Files.Remove(@"sub\表.csv");
            File.SetLastWriteTimeUtc(dc, File.GetLastWriteTimeUtc(sc).AddHours(9));   // 例：時差の分ずれた
            r = b.Run();
            Eq(0, r.Conflicts, "記録がなくても、中身が同じなら確認にしない");
            Eq(1, r.TimeOnlySame, "コピー元とコピー先を比べて同じ");
            Eq("1,2,3", File.ReadAllText(dc), "コピー先はそのまま");

            // 3) 中身が違う（同じ大きさ）でコピー先が新しい・記録なし → 今まで通り確認にする（勝手に上書きしない）
            File.WriteAllText(dc, "9,9,9");
            File.SetLastWriteTimeUtc(dc, File.GetLastWriteTimeUtc(sc).AddHours(9));
            lock (b.State) b.State.Files.Remove(@"sub\表.csv");
            r = b.Run();
            Eq(1, r.Conflicts, "中身が違えば今まで通り「コピー先が新しい」の確認");
            Eq("9,9,9", File.ReadAllText(dc), "コピー先は上書きしない");

            // 4) コピー先は前回コピーしたまま（日時を残さなかった）で、コピー元をその後に直した（同じ大きさ） → 普通の更新として上書き
            File.WriteAllText(sa, "中身B-67890");
            File.SetLastWriteTimeUtc(sa, DateTime.UtcNow.AddMinutes(-1));
            lock (b.State) rec = b.State.Files["見積.xlsx"];
            True(File.GetLastWriteTimeUtc(da).Ticks > File.GetLastWriteTimeUtc(sa).Ticks, "コピー先の日時の方が新しく見える");
            r = b.Run();
            Predicate<ConflictItem> mitsu = delegate(ConflictItem x) { return x.Rel == "見積.xlsx"; };
            bool cf4;
            lock (b.State) cf4 = b.State.Conflicts.Exists(mitsu);   // 3) の「表.csv」の確認は残っている
            True(!cf4, "見かけだけの「コピー先が新しい」で確認にしない");
            Eq(1, r.Overwritten, "コピー元の変更を上書きでコピー");
            Eq("中身B-67890", File.ReadAllText(da), "コピー先が新しい中身になる");

            // 5) コピー先も誰かが直した（日時が記録と違う）・同じ大きさ・中身が違う → 確認にする
            File.WriteAllText(da, "中身C-00000");
            File.SetLastWriteTimeUtc(da, DateTime.UtcNow.AddMinutes(5));
            File.WriteAllText(sa, "中身D-11111");
            File.SetLastWriteTimeUtc(sa, DateTime.UtcNow.AddMinutes(-2));
            r = b.Run();
            bool cf5;
            lock (b.State) cf5 = b.State.Conflicts.Exists(mitsu);
            True(cf5, "コピー先も直されていれば確認");
            Eq("中身C-00000", File.ReadAllText(da), "コピー先の変更は消さない");
        }

        private static void JobGroups()
        {
            JobConfig x = new JobConfig(), y = new JobConfig();
            True(!AppCore.SameGroup(x, y), "グループなし同士は同じグループではない");
            x.Group = " 経理 "; y.Group = "経理";
            True(AppCore.SameGroup(x, y), "前後の空白は区別しない");
            y.Group = "KEIRI"; x.Group = "keiri";
            True(AppCore.SameGroup(x, y), "大文字・小文字は区別しない");
            y.Group = "総務";
            True(!AppCore.SameGroup(x, y), "違うグループ");
            x.Group = "   ";
            Eq(null, x.GroupKey, "空白だけはグループなし");

            // 3つのジョブ（フォルダはすべて別）：A と B は同じグループ、C は別のグループ
            Box b = new Box("T134");
            b.Job.Name = "A"; b.Job.Group = "夜間";
            b.W("a.txt", "a");
            Func<string, string, JobConfig> mk = delegate(string name, string group)
            {
                JobConfig j = b.Job.Clone(); j.Id = Guid.NewGuid().ToString("N"); j.Name = name; j.Group = group;
                j.Source = Path.Combine(b.Root, name + "-src"); j.Dest = Path.Combine(b.Root, name + "-dst");
                Directory.CreateDirectory(j.Source); File.WriteAllText(Path.Combine(j.Source, name + ".txt"), name);
                File.SetLastWriteTimeUtc(Path.Combine(j.Source, name + ".txt"), DateTime.UtcNow.AddMinutes(-5));
                b.App.Jobs.Add(j);
                return j;
            };
            JobConfig jb = mk("B", "夜間"), jc = mk("C", "昼間");
            b.Apply();
            List<string> gn = JobEditForm.GroupNames(null);
            True(gn.Count == 2 && gn.Contains("昼間") && gn.Contains("夜間"), "使っているグループ名（選べるように・重複なし）: " + string.Join(",", gn.ToArray()));
            JobRuntime ra = AppCore.GetRuntime(b.Job.Id), rb = AppCore.GetRuntime(jb.Id), rc = AppCore.GetRuntime(jc.Id);
            lock (ra) ra.Running = true;   // A が実行中
            try
            {
                Eq(b.Job.Name, AppCore.OverlappingRunning(jb) != null ? AppCore.OverlappingRunning(jb).Name : null, "B は A（同じグループ）が終わるのを待つ");
                Eq(null, AppCore.OverlappingRunning(jc), "C（違うグループ）は待たない");
                True(AppCore.RunJob(jb.Id, true, false), "手動の実行は受け付ける（待ってから実行）");
                True(!rb.Running && rb.QueuedManual && rb.WaitingFor == "A", "B は待機中: " + rb.WaitingFor);
                True(!File.Exists(Path.Combine(jb.Dest, "B.txt")), "A が動いている間は B はコピーしない");
                True(AppCore.RunJob(jc.Id, true, false), "C は実行する");
                True(rc.Idle.Wait(30000), "C が終わる");
                True(File.Exists(Path.Combine(jc.Dest, "C.txt")), "違うグループの C は、A の実行中でもコピーした");
            }
            finally { lock (ra) ra.Running = false; }
            // A が終わったら、待っていた B を実行する（実行の予定の確認で、待っていた手動の実行を始める）
            AppCore.RunJob(jb.Id, true, false);   // A が終わったので B を実行（予定の確認がすでに始めていれば、それを待つ）
            True(rb.Idle.Wait(30000), "B が終わる");
            True(File.Exists(Path.Combine(jb.Dest, "B.txt")), "B をコピーした");
            True(!rb.QueuedManual && rb.WaitingFor == null, "待機中の印は消える");

            // 保存の互換：前の版の設定（Group がない）はグループなし
            JobConfig old = Json.Deserialize<JobConfig>("{\"Name\":\"前の版\"}");
            Eq(null, old.GroupKey, "前の版のジョブはグループなし");
            JobConfig rt2 = Json.Deserialize<JobConfig>(Json.Serialize(jb));
            Eq("夜間", rt2.GroupKey, "グループ名を保存・読み込みできる");
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

        private static void AttentionOnlyAtEnd()
        {
            Func<string, string, string, bool, AppCore.AttentionItem> it = delegate(string text, string action, string job, bool err)
            {
                AppCore.AttentionItem a = new AppCore.AttentionItem(); a.Text = text; a.Action = action; a.JobId = job; a.IsError = err; return a;
            };
            Func<int, List<AppCore.AttentionItem>> dup = delegate(int n)
            {
                return new List<AppCore.AttentionItem> { it("ジョブ2：大きいファイルの重複 " + n.ToString("#,0") + "件（テキストへの置き換え）の扱いを選んでください", "dup", "j2", false) };
            };
            Dictionary<string, long> prev = new Dictionary<string, long>(), next = new Dictionary<string, long>();
            True(AppCore.HasNewAttention(prev, dup(1200), next), "初めて出たものは新しい");
            Eq(1200L, next[AppCore.AttentionKind(dup(1200)[0])], "件数（桁区切りも読む）");
            // 1件ずつ処理して件数が減っていく：毎回は出し直さない
            for (int n = 1199; n >= 1190; n--)
            {
                prev = next; next = new Dictionary<string, long>();
                True(!AppCore.HasNewAttention(prev, dup(n), next), "件数が減っただけでは新しくない: " + n);
            }
            prev = next; next = new Dictionary<string, long>();
            True(!AppCore.HasNewAttention(prev, dup(1190), next), "同じなら新しくない");
            prev = next; next = new Dictionary<string, long>();
            True(AppCore.HasNewAttention(prev, dup(1195), next), "件数が増えたら新しい（また見つかった）");
            prev = next; next = new Dictionary<string, long>();
            List<AppCore.AttentionItem> two = dup(1195);
            two.Add(it("上書きの確認が 3件 あります", "conflicts", null, false));
            True(AppCore.HasNewAttention(prev, two, next), "別の種類が増えたら新しい");
            prev = next; next = new Dictionary<string, long>();
            True(!AppCore.HasNewAttention(prev, dup(1195), next), "片方が片付いただけでは新しくない");
            // エラーは文が変わっても同じ種類
            Dictionary<string, long> p2 = new Dictionary<string, long>(), n2 = new Dictionary<string, long>();
            AppCore.HasNewAttention(p2, new List<AppCore.AttentionItem> { it("A：エラー　12件失敗", "error", "a", true) }, n2);
            True(!AppCore.HasNewAttention(n2, new List<AppCore.AttentionItem> { it("A：エラー　15件失敗", "error", "a", true) }, new Dictionary<string, long>()), "エラーの文の違いでは出し直さない（前と同じ）");
            // ジョブ名の数字は件数として扱わない（種類も件数もジョブごとに分かれる）
            Dictionary<string, long> p3 = new Dictionary<string, long>(), n3 = new Dictionary<string, long>();
            AppCore.HasNewAttention(p3, new List<AppCore.AttentionItem> { it("ジョブ1：同期で削除されたファイル 5件 の扱いを選んでください", "synccand", "j1", false) }, n3);
            True(AppCore.HasNewAttention(n3, new List<AppCore.AttentionItem> { it("ジョブ1：同期で削除されたファイル 5件 の扱いを選んでください", "synccand", "j1", false),
                it("ジョブ9：同期で削除されたファイル 1件 の扱いを選んでください", "synccand", "j9", false) }, new Dictionary<string, long>()), "別のジョブは別の種類");

            // 処理中（重複の削除など）の間は AnyResolving が true、終わったら false
            Box b = new Box("T135");
            True(!AppCore.AnyResolving(), "ふだんは処理中ではない");
            Eq(null, AppCore.BeginResolve(b.Job), "処理を始める");
            try { True(AppCore.AnyResolving(), "処理中（お知らせ画面は更新しない）"); }
            finally { AppCore.EndResolve(b.Job); }
            True(!AppCore.AnyResolving(), "終わったら更新する");
        }

        private static void CloudSmallSizeDiff()
        {
            Box b = new Box("T136");
            string[] names = { "a.xlsx", "b.xlsx", "c.xlsx", "d.xlsx", "e.xlsx", "f.xlsx" };
            foreach (string n in names) b.W(n, new string('x', 20000));
            Eq(6, b.Run().Copied, "はじめのコピー");
            string oldEnv = Environment.GetEnvironmentVariable("OneDrive");
            try
            {
                Environment.SetEnvironmentVariable("OneDrive", b.Dst);   // コピー先を OneDrive のフォルダとして扱う
                CloudFiles.ClearCache();
                True(CopyEngine.CloudSizeTolerance(b.Job, b.App, b.Dst) == 16 * 1024, "初期値は 16 KB まで無視");
                // OneDrive がコピー先に情報を書き足した：大きさが少し増え、コピー先の日時が新しくなる
                Action<string, int> grow = delegate(string n, int add)
                {
                    string d = Path.Combine(b.Dst, n);
                    DateTime t = File.GetLastWriteTimeUtc(d);
                    File.AppendAllText(d, new string('m', add));
                    File.SetLastWriteTimeUtc(d, t.AddMinutes(3));
                };
                Func<string, bool> conflict = delegate(string n) { bool v; lock (b.State) v = b.State.Conflicts.Exists(delegate(ConflictItem c) { return c.Rel == n; }); return v; };

                // 1) 記録がない（前の版・記録が消えた）
                grow("a.xlsx", 1500);
                lock (b.State) b.State.Files.Remove("a.xlsx");
                RunResult r = b.Run();
                Eq(1, r.CloudSizeIgnored, "記録がなくても無視");
                True(!conflict("a.xlsx"), "確認にしない");
                Eq(21500L, new FileInfo(Path.Combine(b.Dst, "a.xlsx")).Length, "コピー先はそのまま（上書きしない）");
                r = b.Run();
                Eq(0, r.CloudSizeIgnored + r.Conflicts + r.Copied + r.Overwritten, "次の実行では何もない（記録を直した）");

                // 2) 記録あり・全部確かめて実行
                grow("b.xlsx", 3000);
                r = b.Run(true);
                True(!conflict("b.xlsx"), "全部確かめても確認にしない");
                Eq(23000L, new FileInfo(Path.Combine(b.Dst, "b.xlsx")).Length, "コピー先はそのまま");

                // 3) コピー元を直した（記録と違う）のに、コピー先の方が新しい → 今まで通り確認
                grow("c.xlsx", 1000);
                b.W("c.xlsx", new string('y', 20100), 3600);
                b.Run();
                True(conflict("c.xlsx"), "コピー元が変わったときは無視しない（確認）");

                // 4) コピー元の方が新しい → 今まで通りコピー
                string dd = Path.Combine(b.Dst, "d.xlsx");
                File.AppendAllText(dd, "mm");
                File.SetLastWriteTimeUtc(dd, File.GetLastWriteTimeUtc(Path.Combine(b.Src, "d.xlsx")).AddMinutes(-10));
                lock (b.State) b.State.Files.Remove("d.xlsx");
                r = b.Run();
                Eq(20000L, new FileInfo(dd).Length, "コピー元の方が新しければ上書き");

                // 5) 違いが大きい（20 KB）→ 確認
                grow("e.xlsx", 20 * 1024);
                lock (b.State) b.State.Files.Remove("e.xlsx");
                b.Run();
                True(conflict("e.xlsx"), "違いが大きいときは確認");

                // 6) 設定「無視しない」→ 確認
                b.App.CloudSizeToleranceKB = 0;
                True(CopyEngine.CloudSizeTolerance(b.Job, b.App, b.Dst) == 0, "設定：無視しない");
                grow("f.xlsx", 100);
                lock (b.State) b.State.Files.Remove("f.xlsx");
                b.Run();
                True(conflict("f.xlsx"), "無視しない設定では確認");
                b.App.CloudSizeToleranceKB = 16;

                // 7) 同期のジョブには使わない
                JobConfig sj = new JobConfig(); sj.Sync = true;
                True(CopyEngine.CloudSizeTolerance(sj, b.App, b.Dst) == 0, "同期のジョブには使わない");
            }
            finally
            {
                Environment.SetEnvironmentVariable("OneDrive", oldEnv);
                CloudFiles.ClearCache();
            }
            // 8) OneDrive でないフォルダには使わない
            True(CopyEngine.CloudSizeTolerance(b.Job, b.App, b.Dst) == 0, "OneDrive でなければ使わない");
            AppSettings old = Json.Deserialize<AppSettings>("{\"FormatVersion\":1}");
            Eq(16, old.CloudSizeToleranceKB, "前の版の設定：16 KB まで無視");
        }

        private static void CloudWaitSetting()
        {
            AppSettings old = Json.Deserialize<AppSettings>("{\"FormatVersion\":1}");
            Eq(30, old.CloudWaitMinutes, "前の版の設定は30分（今まで通り）");
            AppSettings z = Json.Deserialize<AppSettings>("{\"FormatVersion\":1,\"CloudWaitMinutes\":0}");
            Eq(0, z.CloudWaitMinutes, "「待たない」を保存できる");
            Eq(TimeSpan.FromHours(2), CloudFiles.WaitSpan(120), "2時間");
            Eq(TimeSpan.Zero, CloudFiles.WaitSpan(-5), "負の値は待たない");
            Eq("待たない", CloudFiles.WaitText(0), "表示");
            Eq("10分", CloudFiles.WaitText(10), "表示");
            Eq("1時間30分", CloudFiles.WaitText(90), "表示");
            Eq("4時間", CloudFiles.WaitText(240), "表示");
            Box b = new Box("T137");
            string od = b.Dst;
            CloudFiles.FreeBytesForTest = delegate(string f) { return 10; };
            try
            {
                DateTime t0 = DateTime.UtcNow;
                string msg = null;
                try { CloudFiles.WaitForLocalSpace(od, 80, 50, CloudFiles.WaitSpan(0), TimeSpan.FromMilliseconds(100), CancellationToken.None); }
                catch (IOException ex) { msg = ex.Message; }
                True(msg != null && msg.Contains("待たない"), "待たない設定ではすぐ止める: " + msg);
                True(DateTime.UtcNow - t0 < TimeSpan.FromSeconds(2), "待たずに止める");
                msg = null;
                try { CloudFiles.WaitForLocalSpace(od, 80, 50, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), CancellationToken.None); }
                catch (IOException ex) { msg = ex.Message; }
                True(msg != null && msg.Contains("待ちましたが"), "時間まで待ってから止める: " + msg);
            }
            finally { CloudFiles.FreeBytesForTest = null; }
            foreach (int m in CloudFiles.WaitChoices) True(m >= 0, "選択肢");
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
