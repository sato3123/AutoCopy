using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// 確認用：見本のデータで全画面を作り、画像（PNG）に保存する。
    /// AutoCopy.exe /screenshots 出力フォルダ　（環境変数 AUTOCOPY_HOME で作業場所を分けて使う）
    /// </summary>
    public static class ScreenshotMode
    {
        private static string outDir;
        private static readonly List<string> errors = new List<string>();

        public static void Run(string dir)
        {
            outDir = dir;
            Directory.CreateDirectory(outDir);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { errors.Add("画面イベント中の例外: " + e.Exception); };
            try { Prepare(); }
            catch (Exception ex) { errors.Add("準備: " + ex); }

            JobConfig j0 = AppCore.Settings.Jobs[0];
            JobConfig j1 = AppCore.Settings.Jobs[1];

            Capture("01_メイン画面", delegate() { return new MainForm(); }, null);
            Capture("02_ジョブ設定_基本", delegate() { return new JobEditForm(j0.Clone(), false); }, delegate(Form f) { SelectTab(f, 0); });
            Capture("03_ジョブ設定_除外ルール", delegate() { return new JobEditForm(j0.Clone(), false); }, delegate(Form f) { SelectTab(f, 1); });
            Capture("04_ジョブ設定_接続と重複チェック", delegate() { return new JobEditForm(j1.Clone(), false); }, delegate(Form f) { SelectTab(f, 2); });
            Capture("05_ミラーの注意", delegate() { return new MirrorWarnForm(j1); }, null);
            Capture("06_上書きの確認", delegate() { return new ConflictForm(); }, delegate(Form f) { CheckFirst(f); });
            Capture("07_ミラーの保留中の削除", delegate() { return new PendingDeleteForm(j1); }, null);
            Capture("08_重複チェック結果", delegate() { DupForm d = new DupForm(); d.SelectJob(j0.Id); return d; }, null);
            Capture("09_元に戻す", delegate() { return new TrashForm(j0); }, null);
            Capture("10_ログ", delegate() { return new LogForm(); }, null);
            Capture("11_アプリ設定", delegate() { return new SettingsForm(); }, null);
            Capture("12_配布フォルダの作成", delegate() { return new DistCreateForm(); }, null);
            Capture("13_フォルダ名の変更", delegate() { return new FolderNameForm(); }, null);
            Capture("14_ID・パスワード", delegate() { return new CredDialog("nas01", null); }, null);
            string setup = Path.Combine(AppPaths.Local, "sample", AppPaths.SetupFileName);
            Capture("15_初回セットアップ_配布設定", delegate() { return new SetupForm(setup, Distribution.ReadEnvelope(setup), true); }, null);
            Capture("16_初回セットアップ_パスワード入力後", delegate() { return new SetupForm(setup, Distribution.ReadEnvelope(setup), true); },
                delegate(Form f) { UnlockSetup(f, "secret"); });
            Capture("17_初回セットアップ_単独", delegate() { return new SetupForm(null, null, true); }, null);
            Capture("19_PCごとのコピー容量", delegate()
            {
                string note;
                List<LogLine> lines = Logger.Read(DateTime.Today.AddDays(-7), DateTime.Today, true, out note);
                return new StatsForm(lines, DateTime.Today.AddDays(-7).ToString("yyyy/MM/dd") + "〜" + DateTime.Today.ToString("yyyy/MM/dd") + "・すべてのPC");
            }, null);
            string[] tabNames = { null, "20_PCごとのグラフ", "21_日ごとのグラフ", "22_コピー容量_場所別の割合", "23_コピー容量_種類別の割合", "24_コピー容量_大きいファイル" };
            for (int tab = 1; tab < tabNames.Length; tab++)
            {
                int ti = tab;
                Capture(tabNames[ti], delegate()
                {
                    string note;
                    List<LogLine> lines = Logger.Read(DateTime.Today.AddDays(-13), DateTime.Today, true, out note);
                    return new StatsForm(lines, DateTime.Today.AddDays(-13).ToString("yyyy/MM/dd") + "〜" + DateTime.Today.ToString("yyyy/MM/dd") + "・すべてのPC");
                }, delegate(Form f) { ((StatsForm)f).ShowTab(ti); });
            }
            Capture("25_コピー容量_場所別_PCの中", delegate()
            {
                string note;
                List<LogLine> lines = Logger.Read(DateTime.Today.AddDays(-13), DateTime.Today, true, out note);
                return new StatsForm(lines, DateTime.Today.AddDays(-13).ToString("yyyy/MM/dd") + "〜" + DateTime.Today.ToString("yyyy/MM/dd") + "・すべてのPC");
            }, delegate(Form f) { ((StatsForm)f).ShowTab(3); ((StatsForm)f).Places.Open("佐藤_設計\\経理バックアップ"); });
            string[] usageNames = { "26_容量の内訳_フォルダ別", "27_容量の内訳_種類別", "28_容量の内訳_大きいファイル" };
            for (int tab = 0; tab < usageNames.Length; tab++)
            {
                int ti = tab;
                Capture(usageNames[ti], delegate() { return new UsageForm(j0.Id); },
                    delegate(Form f) { ((UsageForm)f).AnalyzeNow(); ((UsageForm)f).ShowTab(ti); });
            }
            Capture("29_容量の内訳_フォルダの中", delegate() { return new UsageForm(j0.Id); },
                delegate(Form f) { ((UsageForm)f).AnalyzeNow(); ((UsageForm)f).Folders.Open("会議録画"); });
            CaptureTrayMenu();

            File.WriteAllText(Path.Combine(outDir, "errors.txt"), errors.Count == 0 ? "なし" : string.Join("\r\n\r\n", errors.ToArray()));
        }

        private static void Prepare()
        {
            string root = Path.Combine(AppPaths.Local, "sample");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string src1 = Path.Combine(root, "経理"), dst1 = Path.Combine(root, "nas\\backup");
            string src2 = Path.Combine(root, "図面"), dst2 = Path.Combine(root, "fs02\\設計部");
            string[] files = { "請求書\\10月.xlsx", "請求書\\10月_控え.pdf", "集計\\月次.xlsx", "設定\\一覧.csv", "見積\\見積書_v2.xlsx", "2025年度\\決算書.xlsx", "2025年度\\附属明細.xlsx" };
            foreach (string f in files) W(Path.Combine(src1, f), "sample " + f, 600);
            for (int i = 0; i < 40; i++) W(Path.Combine(src2, "図面" + i.ToString("00") + ".dwg"), "dwg " + i, 600);

            AppSettings s = new AppSettings();
            s.InstanceId = "sample-instance";
            s.FolderName = "山田_経理";
            s.LogRoot = Path.Combine(root, "logs");
            s.DistName = "経理部"; s.DistVersion = 3; s.DistCreated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            JobConfig a = new JobConfig();
            a.Name = "経理バックアップ"; a.Scope = JobScope.Dist; a.Source = src1; a.Dest = dst1 + "\\{フォルダ名}\\経理";
            a.FollowRename = false;
            a.Exclude.Presets.Add("video"); a.Exclude.Extensions.Add(".log"); a.Exclude.Folders.Add("作業用\\一時"); a.Exclude.Includes.Add("会議録画\\*.mp4");
            a.Exclude.MaxSizeBytes = 500L * 1024 * 1024;
            JobConfig b = new JobConfig();
            b.Name = "図面共有"; b.Scope = JobScope.Dist; b.Source = src2; b.Dest = "\\\\fs02\\設計部\\{フォルダ名}\\図面"; b.Mirror = true; b.IntervalMinutes = 15;
            JobConfig c = new JobConfig();
            c.Name = "作業メモ"; c.Scope = JobScope.Local; c.Source = Path.Combine(root, "メモ"); c.Dest = Path.Combine(root, "usb\\メモ"); c.Enabled = false; c.IntervalMinutes = 60;
            Directory.CreateDirectory(c.Source);
            s.Jobs.Add(a); s.Jobs.Add(b); s.Jobs.Add(c);
            AppCore.Settings = s;
            AppCore.SaveSettings();

            // 1回目のコピー → 名前変更（追従オフで取りこぼし）→ 重複チェック → 上書き確認
            JobState st = AppCore.GetState(a.Id);
            new CopyEngine(a, s, st, false, false, CancellationToken.None).Run();
            File.Move(Path.Combine(src1, "見積\\見積書_v2.xlsx"), Path.Combine(src1, "見積\\見積書_最終.xlsx"));
            Directory.Move(Path.Combine(src1, "2025年度"), Path.Combine(src1, "2025年度_決算"));
            string d1 = CopyEngine.ResolvedDest(a, s);
            W(Path.Combine(d1, "設定\\一覧.csv"), "dest edited", 30);
            W(Path.Combine(src1, "設定\\一覧.csv"), "src edited", 300);
            W(Path.Combine(src1, "集計\\月次.xlsx"), "updated", 300);
            new CopyEngine(a, s, st, false, false, CancellationToken.None).Run();
            AppCore.SaveState(a.Id);
            DupResult dr = DupChecker.Run(a, s, st, CancellationToken.None);
            DupEntry first = null;
            foreach (DupGroup g in dr.Groups) foreach (DupEntry e in g.Entries) if (first == null && e.Recommended && !e.IsFolder) first = e;
            if (first != null)
            {
                // 元に戻す画面用に1件だけ削除済みへ（結果は再チェックで作り直す）
                DupChecker.DeleteSelected(a, s, st, new List<DupEntry>(new DupEntry[] { first }));
                DupChecker.Run(a, s, st, CancellationToken.None);
            }

            JobState st2 = AppCore.GetState(b.Id);
            for (int i = 0; i < 25; i++) { PendingDelete p = new PendingDelete(); p.Rel = "図面" + i.ToString("00") + ".dwg"; p.Size = 1200 * 1024 + i; st2.PendingDeletes.Add(p); }
            st2.LastResult = "error"; st2.LastMessage = "\\\\fs02\\設計部 に接続できません（3回再試行しました）";
            st2.LastRunTicks = DateTime.UtcNow.AddMinutes(-5).Ticks;
            AppCore.SaveState(b.Id);
            JobRuntime rt2 = AppCore.GetRuntime(b.Id);
            rt2.LastResult = st2.LastResult; rt2.LastMessage = st2.LastMessage; rt2.LastRun = DateTime.Now.AddMinutes(-5);

            AppCore.Notify(NoticeLevel.Error, "図面共有：エラー", "\\\\fs02\\設計部 に接続できません（3回再試行しました）", "retry", b.Id);
            AppCore.Notify(NoticeLevel.Warning, "上書きの確認が 1件 あります", "経理バックアップ：コピー先の方が新しいファイルがあるため、上書きせずに待っています。", "conflicts", a.Id);
            AppCore.Notify(NoticeLevel.Info, "重複チェック：削除候補 " + dr.DeleteCandidateCount() + "件", "経理バックアップ：結果を確認してください。", "dup", a.Id);
            Logger.Flush();
            AppCore.PublishStatus();
            // 他のPCのログ（集計画面の見本）
            string t = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string other = "";
            for (int i = 0; i < 12; i++) other += t + "\tPC-SOUMU03\tsuzuki\t鈴木_総務\t経理バックアップ\t" + (i % 3 == 0 ? "OVERWRITE" : "COPY") + "\t総務\\資料" + i + ".docx\t" + (350000 + i * 120000) + "\t0.4\r\n";
            other += t + "\tPC-SOUMU03\tsuzuki\t鈴木_総務\t経理バックアップ\tSUMMARY\t走査 3,120件\t\t2.1\r\n";
            W(Path.Combine(Path.Combine(s.LogRoot, "鈴木_総務"), DateTime.Now.ToString("yyyy-MM-dd") + ".log"), other, 60);
            string[] names = { "鈴木_総務", "田中_営業", "佐藤_設計", "高橋_経理" };
            // 場所・種類ごとに大きさの違うファイル（同じファイルを何度も上書きする例も入れる）
            string[] pool = { "見積\\見積書_{0}.xlsx", "資料\\提案書_{0}.pptx", "資料\\報告書_{0}.pdf", "写真\\現場_{0}.jpg",
                              "図面\\A棟\\平面図_{0}.dwg", "会議録画\\定例会_{0}.mp4", "メール\\outlook.pst" };
            long[] baseSize = { 300000, 2500000, 900000, 3500000, 6000000, 60000000, 150000000 };
            Random rnd = new Random(7);
            for (int dd = 13; dd >= 1; dd--)
            {
                DateTime day = DateTime.Today.AddDays(-dd);
                if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) continue;
                for (int pi = 0; pi < names.Length; pi++)
                {
                    string text = "";
                    int n = 3 + rnd.Next(8);
                    for (int k = 0; k < n; k++)
                    {
                        int kind = rnd.Next(5);
                        if (pi == 2 && k == 0) kind = 4;                 // 佐藤_設計は図面が多い
                        if (pi == 1 && k == 1 && dd % 3 == 0) kind = 5;  // 田中_営業はときどき動画
                        if (pi == 3 && k == 0 && dd % 2 == 0) kind = 6;  // 高橋_経理は PST を何度も上書き
                        string rel = string.Format(pool[kind], dd * 10 + k);
                        long size = baseSize[kind] / 2 + (long)(rnd.NextDouble() * baseSize[kind]);
                        if (kind == 6) size = 180000000 + dd * 1000000;
                        bool over = kind == 6 || k % 4 == 0;
                        text += day.AddHours(9 + k).ToString("yyyy-MM-dd HH:mm:ss") + "\tPC-" + (pi + 1).ToString("00") + "\tuser\t" + names[pi] + "\t経理バックアップ\t"
                            + (over ? "OVERWRITE" : "COPY") + "\t" + rel + "\t" + size + "\t0.5\r\n";
                    }
                    string file = Path.Combine(Path.Combine(s.LogRoot, names[pi]), day.ToString("yyyy-MM-dd") + ".log");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.AppendAllText(file, text);
                }
            }

            // 容量の内訳の見本：コピー先に大きさの違うファイルを置く（中身は空でサイズだけ）
            string[] big = { "会議録画\\2026-09_全体会議.mp4|188000000|40", "会議録画\\2026-10_部会.mp4|96000000|12", "会議録画\\2026-10_研修.mp4|71000000|5",
                             "メール\\outlook_2025.pst|142000000|2", "決算\\2025年度\\決算資料一式.zip|38000000|150", "決算\\2025年度\\試算表.xlsx|2400000|150",
                             "写真\\工事記録\\IMG_0101.jpg|4200000|60", "写真\\工事記録\\IMG_0102.jpg|3900000|60", "写真\\工事記録\\IMG_0103.jpg|4500000|60" };
            foreach (string b0 in big)
            {
                string[] f = b0.Split('|');
                Sized(Path.Combine(d1, f[0]), long.Parse(f[1]), int.Parse(f[2]));
            }
            for (int i = 0; i < 120; i++)
                Sized(Path.Combine(d1, "請求書\\2026\\請求_" + i.ToString("000") + ".pdf"), 180000 + (i * 7919 % 400000), 3 + i % 200);
            for (int i = 0; i < 30; i++)
                Sized(Path.Combine(d1, "写真\\工事記録\\IMG_" + (200 + i) + ".jpg"), 2500000 + i * 61000, 30 + i);

            DistPayload pl = Distribution.BuildPayload(s, "経理部", 3, new List<DistCredential>());
            DistCredential cr = new DistCredential(); cr.Server = "nas01"; cr.User = "nas01\\yamada"; cr.Password = "x";
            pl.Credentials.Add(cr);
            Distribution.Write(Path.Combine(root, AppPaths.SetupFileName), pl, "secret");
        }

        private static void Sized(string path, long size, int ageDays)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write)) fs.SetLength(size);
            File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageDays).AddMinutes(-ageDays * 37 % 600));
        }

        private static void W(string path, string content, int ageSec)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-ageSec));
        }

        private static void Pump(int ms)
        {
            DateTime until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(30); }
        }

        private static void Capture(string name, Func<Form> make, Action<Form> after)
        {
            Form f = null;
            try
            {
                f = make();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(20, 20);
                f.Show();
                Pump(1500);
                if (after != null) { after(f); Pump(1200); }
                Save(f, name);
            }
            catch (Exception ex) { errors.Add(name + ": " + ex); }
            finally
            {
                try { if (f != null) { f.Close(); f.Dispose(); } } catch (Exception) { }
                Pump(200);
            }
        }

        private static void Save(Control c, string name)
        {
            using (Bitmap bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
            // 画面全体の見え方も保存（デスクトップが使える場合）
            try
            {
                Rectangle r = c.RectangleToScreen(c.ClientRectangle);
                if (c is Form) r = ((Form)c).Bounds;
                using (Bitmap scr = new Bitmap(r.Width, r.Height))
                {
                    using (Graphics g = Graphics.FromImage(scr)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    scr.Save(Path.Combine(outDir, name + "_screen.png"), ImageFormat.Png);
                }
            }
            catch (Exception) { }
        }

        private static void SelectTab(Form f, int index)
        {
            foreach (Control c in f.Controls)
            {
                TabControl t = c as TabControl;
                if (t != null) t.SelectedIndex = index;
            }
        }

        private static void CheckFirst(Form f)
        {
            foreach (Control c in f.Controls)
            {
                ListView lv = c as ListView;
                if (lv != null && lv.Items.Count > 0) lv.Items[0].Checked = true;
            }
        }

        private static void UnlockSetup(Form f, string password)
        {
            FieldInfo pass = typeof(SetupForm).GetField("tPass", BindingFlags.NonPublic | BindingFlags.Instance);
            ((TextBox)pass.GetValue(f)).Text = password;
            MethodInfo unlock = typeof(SetupForm).GetMethod("Unlock", BindingFlags.NonPublic | BindingFlags.Instance);
            unlock.Invoke(f, new object[] { true });
        }

        private static void CaptureTrayMenu()
        {
            try
            {
                using (TrayApp app = new TrayApp())
                {
                    FieldInfo mf = typeof(TrayApp).GetField("menu", BindingFlags.NonPublic | BindingFlags.Instance);
                    ContextMenuStrip menu = (ContextMenuStrip)mf.GetValue(app);
                    MethodInfo build = typeof(TrayApp).GetMethod("BuildMenu", BindingFlags.NonPublic | BindingFlags.Instance);
                    build.Invoke(app, null);
                    menu.Show(new Point(200, 200));
                    Pump(800);
                    Save(menu, "18_トレイメニュー");
                    menu.Close();
                }
            }
            catch (Exception ex) { errors.Add("トレイメニュー: " + ex); }
        }
    }
}
