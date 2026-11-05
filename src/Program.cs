using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    internal static class Program
    {
        /// <summary>
        /// 引数:
        ///   (なし)            メイン画面を開く
        ///   /startup          スタートアップから起動（画面は出さない）
        ///   /run              すべてのジョブを今すぐ実行
        ///   /run "ジョブ名"   指定したジョブだけ実行
        ///   /dupcheck         重複チェックを実行
        ///   /checkupdate      更新版があるか確かめる（あれば更新する）
        ///   /updated pid 版   自動更新で起動し直した（前の版が終わるのを待ってから動く）
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            // 長いパス（\\?\ 付き）を Windows 7 でも使えるようにする。パス処理より前に設定する必要がある。
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 確認用：全画面を画像に保存して終了
            if (args.Length > 0 && args[0].Equals("/screenshots", StringComparison.OrdinalIgnoreCase))
            {
                ScreenshotMode.Run(args.Length > 1 ? args[1] : "screenshots");
                return;
            }

#if UPDATEFAIL
            // 確認用：更新の後に起動できない版（見張り役が前の版に戻すことを確かめる）
            if (args.Length > 0 && args[0].Equals("/updated", StringComparison.OrdinalIgnoreCase)) Environment.Exit(3);
#endif
            // 更新の見張り役（前の版の exe から起動される）
            if (args.Length > 0 && args[0].Equals("/updateguard", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(Updater.RunGuard(args));
                return;
            }

            string command = BuildCommand(args);
            if (args.Length >= 2 && args[0].Equals("/updated", StringComparison.OrdinalIgnoreCase))
            {
                int pid;
                if (int.TryParse(args[1], out pid)) Updater.WaitForExit(pid, 60);
                command = "updated|" + (args.Length > 2 ? args[2] : "");
            }
            string setupNextToExe = Path.Combine(AppPaths.ExeDir, AppPaths.SetupFileName);

            if (!SingleInstance.TryAcquire())
            {
                // 配布フォルダから起動された場合は、動いているアプリに取り込みを頼む
                if (File.Exists(setupNextToExe) && args.Length == 0) command = "import|" + setupNextToExe;
                // 既に動いているアプリに指示を渡して終わる
                if (!SingleInstance.SendToRunning(command))
                    MessageBox.Show("自動コピーは既に起動していますが、応答がありません。", "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Logger.Error(null, "画面の処理でエラー: " + e.Exception.Message);
                MessageBox.Show("エラーが発生しました。\n" + e.Exception.Message, "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Logger.Error(null, "予期しないエラー: " + (ex != null ? ex.ToString() : "不明"));
            };
            SafeFs.Blocked += delegate(string p) { Logger.Write(null, "BLOCKED", "コピー元への変更を拒否しました: " + p, -1, -1); };

            bool firstRun = !AppCore.SettingsExist();
            AppCore.LoadSettings();

            // 配布フォルダから起動された：初回セットアップ または 新しい版の取り込み
            string setupFile = setupNextToExe;
            if (File.Exists(setupFile))
            {
                DistEnvelope env = null;
                try { env = Distribution.ReadEnvelope(setupFile); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "自動コピー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                if (env != null && (firstRun || env.Version > AppCore.Settings.DistVersion))
                {
                    using (SetupForm f = new SetupForm(setupFile, env, firstRun))
                    {
                        if (f.ShowDialog() != DialogResult.OK && firstRun) return;
                        if (f.RestartPath != null)
                        {
                            // インストール先から起動し直す
                            SingleInstance.Release();
                            System.Diagnostics.Process.Start(f.RestartPath);
                            return;
                        }
                    }
                    command = "show";
                }
            }
            else if (firstRun)
            {
                using (SetupForm f = new SetupForm(null, null, true))
                {
                    if (f.ShowDialog() != DialogResult.OK) return;
                }
                command = "show";
            }

            // 配布フォルダ（USB など）から起動されたが、インストール済みの本体がある：そちらで動かす
            string binExe = Path.Combine(AppPaths.BinDir, "AutoCopy.exe");
            bool runningFromBin = string.Equals(PathUtil.Normalize(AppPaths.ExeDir), PathUtil.Normalize(AppPaths.BinDir), StringComparison.OrdinalIgnoreCase);
            if (!runningFromBin && File.Exists(setupFile) && File.Exists(binExe))
            {
                SingleInstance.Release();
                System.Diagnostics.Process.Start(binExe);
                return;
            }

            Logger.Info(null, command == "startup" ? "サインインにより起動しました。自動コピーを開始します"
                : command.StartsWith("updated|") ? "新しい版（" + Updater.CurrentVersion + "）で起動し直しました" : "起動しました");
            if (!AppCore.Settings.ShortcutRenamed)
            {
                Shortcuts.RenameOld(AppCore.Settings);
                try { AppCore.SaveSettings(); } catch (Exception) { }
            }
            AppCore.Start();
            // 前回の更新に失敗して前の版に戻した：知らせる
            try
            {
                if (File.Exists(Updater.RollbackFile))
                {
                    string msg = File.ReadAllText(Updater.RollbackFile);
                    File.Delete(Updater.RollbackFile);
                    Logger.Write(null, "UPDATE", msg, -1, -1);
                    AppCore.Notify(NoticeLevel.Warning, "更新に失敗したため、前の版に戻しました", msg, "log", null);
                }
            }
            catch (Exception ex) { Logger.Error(null, "更新の記録を読めません: " + ex.Message); }
            using (TrayApp app = new TrayApp())
            {
                app.HandleCommand(command);
                if (command.StartsWith("updated|"))
                {
                    // 新しい版として問題なく動き始めたら、見張り役に知らせる
                    EventHandler idle = null;
                    idle = delegate { Application.Idle -= idle; Updater.MarkStarted(); };
                    Application.Idle += idle;
                }
                Application.Run(app);
            }
            AppCore.Stop();
            Logger.Flush();
        }

        private static string BuildCommand(string[] args)
        {
            if (args.Length == 0) return "show";
            string a = args[0].Trim().ToLowerInvariant().TrimStart('/', '-');
            if (a == "startup") return "startup";
            if (a == "dupcheck") return "dupcheck";
            if (a == "checkupdate") return "checkupdate";
            if (a == "run") return args.Length > 1 ? "run|" + args[1] : "run";
            return "show";
        }
    }
}
