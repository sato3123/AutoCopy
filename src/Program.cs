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

            string command = BuildCommand(args);
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

            Logger.Info(null, command == "startup" ? "サインインにより起動しました。自動コピーを開始します" : "起動しました");
            AppCore.Start();
            using (TrayApp app = new TrayApp())
            {
                app.HandleCommand(command);
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
            if (a == "run") return args.Length > 1 ? "run|" + args[1] : "run";
            return "show";
        }
    }
}
