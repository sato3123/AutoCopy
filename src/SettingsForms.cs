using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>アプリ設定</summary>
    public class SettingsForm : Form
    {
        private readonly Label lFolder, lDist;
        private readonly TextBox tLogRoot, tUpdate;
        private readonly CheckBox cAuto, cErr, cConf, cDone, cBlink, cPopup, cAutoUpdate, cAutoClose;
        private readonly NumericUpDown nLocal, nNet, nTrash, nRemind, nCloseSec;
        private readonly ComboBox cbShortcut;
        private Button bLogCred, bCheckUpdate;

        public SettingsForm()
        {
            AppSettings s = AppCore.Settings;
            Ui.SetupForm(this, "アプリ設定", 760, 660);
            FlowLayoutPanel b = Ui.Body();

            lFolder = Ui.L(s.FolderName);
            lFolder.Font = Ui.Bold;
            tLogRoot = Ui.T(s.LogRoot, 300);
            lDist = Ui.Hint("");
            b.Controls.Add(Ui.Section("複数台で使う",
                Ui.Row(Ui.L("このPCのフォルダ名", 140), lFolder, Ui.B("変更…", delegate { ChangeFolderName(); })),
                Ui.Row(Ui.L("ログ保管先", 140), tLogRoot,
                    Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, tLogRoot.Text, "ログ保管先（ネットワークドライブ）を選んでください"); if (f != null) tLogRoot.Text = NetAccess.ToUncIfMapped(f); }),
                    bLogCred = Ui.B("ID・パスワード…", delegate { string sv = PathUtil.UncServer(tLogRoot.Text); if (sv == null) { Ui.Info(this, "ネットワークのフォルダ（\\\\サーバー\\共有\\…）を指定してください。"); return; } using (CredDialog d = new CredDialog(sv, tLogRoot.Text)) d.ShowDialog(this); })),
                Ui.Hint("各PCは「ログ保管先\\フォルダ名」にログを書きます。空欄ならこのPCの中だけに残します。"),
                lDist,
                Ui.Row(Ui.B("配布フォルダを作成…", delegate { using (DistCreateForm f = new DistCreateForm()) f.ShowDialog(this); UpdateDist(); }),
                    Ui.B("配布設定を読み込む…", delegate { ImportDist(); }))));

            cAuto = Ui.C("Windows にサインインしたら自動で起動する", s.AutoStart);
            b.Controls.Add(Ui.Section("起動",
                cAuto,
                Ui.Hint("一時停止中でも、PC を再起動すると自動コピーは必ず再開します。Windows 7〜11 で動作します。")));

            cErr = Ui.C("エラーを通知する", s.NotifyErrors);
            cConf = Ui.C("上書き確認・重複チェック・ミラーの保留を通知する", s.NotifyConfirm);
            cDone = Ui.C("コピーが正常に終わったときも通知する", s.NotifyDone);
            cBlink = Ui.C("アイコンを点滅させる（確認が済むまで）", s.AttentionBlink);
            cPopup = Ui.C("画面右下に「確認が必要です」のメッセージを出す", s.AttentionPopup);
            nRemind = Ui.N(5, 1440, s.AttentionRemindMinutes, 64);
            cAutoClose = Ui.C("メッセージを自動で閉じる", s.AttentionAutoClose);
            nCloseSec = Ui.N(5, 3600, s.AttentionCloseSeconds, 72);
            b.Controls.Add(Ui.Section("通知", cErr, cConf, cDone,
                Ui.L("確認が必要なとき（上書きの確認・ミラーの保留・重複チェックの結果・エラー）"),
                cBlink, cPopup,
                Ui.Row(Ui.L("　", 140), cAutoClose, nCloseSec, Ui.L("秒たったら（マウスを重ねている間は閉じない）")),
                Ui.Row(Ui.L("　確認が済むまで", 140), nRemind, Ui.L("分ごとにメッセージを出し直す")),
                Ui.Hint("自動で閉じても、確認が済むまでアイコンの点滅は続きます。画面右下の Windows の通知（吹き出し）は、Windows が数秒で自動的に閉じます。")));

            tUpdate = Ui.T(s.UpdateFolder, 300);
            cAutoUpdate = Ui.C("新しい版があれば自動で更新する（1時間ごとに確認。コピー中・画面を開いている間は待ちます）", s.AutoUpdate);
            b.Controls.Add(Ui.Section("自動更新",
                Ui.Row(Ui.L("更新版の置き場所", 140), tUpdate,
                    Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, tUpdate.Text, "新しい AutoCopy.exe を置くフォルダ（NAS の配布フォルダなど）を選んでください"); if (f != null) tUpdate.Text = NetAccess.ToUncIfMapped(f); })),
                cAutoUpdate,
                Ui.Hint("置き場所の AutoCopy.exe が今の版（" + Updater.CurrentVersion + "）より新しく、今と同じ証明書でデジタル署名されているときだけ更新します。"),
                Ui.Row(bCheckUpdate = Ui.B("今すぐ確認", delegate { if (Save() && TrayApp.Instance != null) TrayApp.Instance.CheckUpdate(true); }))));

            nLocal = Ui.N(1, 3650, s.LocalLogDays, 72);
            nNet = Ui.N(1, 3650, s.NetLogDays, 72);
            nTrash = Ui.N(1, 3650, s.TrashDays, 72);
            b.Controls.Add(Ui.Section("保存期間",
                Ui.Row(Ui.L("ログ（このPC）", 180), nLocal, Ui.L("日")),
                Ui.Row(Ui.L("ログ（保管先）", 180), nNet, Ui.L("日")),
                Ui.Row(Ui.L("削除済み・退避ファイル", 180), nTrash, Ui.L("日"))));

            cbShortcut = Ui.Combo(320, "強制データ送信（すべてのジョブを今すぐ実行）");
            foreach (JobConfig j in s.Jobs) cbShortcut.Items.Add(j.Name + " を今すぐ実行");
            cbShortcut.Items.Add("重複チェックを実行");
            b.Controls.Add(Ui.Section("デスクトップのショートカット",
                Ui.Row(cbShortcut, Ui.B("作成", delegate { MakeShortcut(); })),
                Ui.Hint("ダブルクリックすると、動いている自動コピーに実行を指示します。")));

            Button ok = Ui.B("OK", delegate { if (Save()) Close(); });
            ok.MinimumSize = new Size(Ui.S(88), Ui.S(28));
            Controls.Add(b);
            Controls.Add(Ui.Footer(Ui.B("キャンセル", delegate { Close(); }), ok, Ui.B("操作説明書を開く", delegate { Ui.OpenManual(); })));
            EventHandler h = delegate { UpdateEnabled(); };
            cPopup.CheckedChanged += h;
            cAutoClose.CheckedChanged += h;
            tUpdate.TextChanged += h;
            tLogRoot.TextChanged += h;
            UpdateEnabled();
            UpdateDist();
        }

        private void UpdateDist()
        {
            AppSettings s = AppCore.Settings;
            lDist.Text = s.DistVersion > 0
                ? "配布設定：" + (s.DistName ?? "") + "　第" + s.DistVersion + "版（" + (s.DistCreated ?? "") + " 作成）　このPC：" + Environment.MachineName
                : "配布設定：なし　このPC：" + Environment.MachineName;
            lFolder.Text = s.FolderName;
        }

        private void ChangeFolderName()
        {
            using (FolderNameForm f = new FolderNameForm()) f.ShowDialog(this);
            UpdateDist();
        }

        private void ImportDist()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "配布用設定|" + AppPaths.SetupFileName + ";*.setup";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DistEnvelope env = Distribution.ReadEnvelope(d.FileName);
                    if (env.Version < AppCore.Settings.DistVersion &&
                        !Ui.Confirm(this, "いまの設定（第" + AppCore.Settings.DistVersion + "版）より古い版（第" + env.Version + "版）です。それでも読み込みますか？")) return;
                    using (SetupForm f = new SetupForm(d.FileName, env, false)) f.ShowDialog(this);
                    UpdateDist();
                }
                catch (Exception ex) { Ui.Error(this, ex.Message); }
            }
        }

        private void MakeShortcut()
        {
            int i = cbShortcut.SelectedIndex;
            List<JobConfig> jobs = AppCore.Settings.Jobs;
            try
            {
                string path;
                if (i <= 0) path = Shortcuts.CreateOnDesktop(Shortcuts.ForceSendName, "/run");
                else if (i <= jobs.Count) path = Shortcuts.CreateOnDesktop(Shortcuts.ForceSendName + "（" + jobs[i - 1].Name + "）", "/run \"" + jobs[i - 1].Name + "\"");
                else path = Shortcuts.CreateOnDesktop("自動コピー（重複チェック）", "/dupcheck");
                Ui.Info(this, "デスクトップにショートカットを作りました：\n" + Path.GetFileName(path));
            }
            catch (Exception ex) { Ui.Error(this, "作れませんでした：" + ex.Message); }
        }

        /// <summary>上の設定で決まる「変えられない項目」を操作できないようにする</summary>
        public void UpdateEnabled()
        {
            nRemind.Enabled = cPopup.Checked;
            cAutoClose.Enabled = cPopup.Checked;
            nCloseSec.Enabled = cPopup.Checked && cAutoClose.Checked;
            bool up = tUpdate.Text.Trim().Length > 0;
            cAutoUpdate.Enabled = bCheckUpdate.Enabled = up;
            bLogCred.Enabled = PathUtil.IsUnc(NetAccess.ToUncIfMapped(tLogRoot.Text.Trim()));
        }

        /// <summary>確認用：上の設定を切り替えたときの、下の項目の操作できる・できない</summary>
        public List<string> SelfTestEnabled()
        {
            List<string> bad = new List<string>();
            string u = tUpdate.Text, l = tLogRoot.Text; bool pop = cPopup.Checked;
            bool ac = cAutoClose.Checked;
            cPopup.Checked = false; if (nRemind.Enabled) bad.Add("メッセージを出さないのに間隔を変えられる");
            if (cAutoClose.Enabled || nCloseSec.Enabled) bad.Add("メッセージを出さないのに自動で閉じる設定を変えられる");
            cPopup.Checked = true; if (!nRemind.Enabled) bad.Add("メッセージを出すのに間隔を変えられない");
            cAutoClose.Checked = false; if (nCloseSec.Enabled) bad.Add("自動で閉じないのに秒数を変えられる");
            cAutoClose.Checked = true; if (!nCloseSec.Enabled) bad.Add("自動で閉じるのに秒数を変えられない");
            cAutoClose.Checked = ac;
            tUpdate.Text = ""; if (cAutoUpdate.Enabled || bCheckUpdate.Enabled) bad.Add("置き場所がないのに自動更新を選べる");
            tUpdate.Text = "\\\\nas01\\配布"; if (!cAutoUpdate.Enabled) bad.Add("置き場所があるのに自動更新を選べない");
            tLogRoot.Text = "C:\\logs"; if (bLogCred.Enabled) bad.Add("ネットワークでないログ保管先なのに ID・パスワードを押せる");
            tLogRoot.Text = "\\\\nas01\\logs"; if (!bLogCred.Enabled) bad.Add("ネットワークのログ保管先なのに ID・パスワードを押せない");
            tUpdate.Text = u; tLogRoot.Text = l; cPopup.Checked = pop;
            return bad;
        }

        private bool Save()
        {
            AppSettings s = AppCore.Settings;
            string root = tLogRoot.Text.Trim();
            s.LogRoot = root.Length == 0 ? null : NetAccess.ToUncIfMapped(root);
            s.AutoStart = cAuto.Checked;
            s.NotifyErrors = cErr.Checked;
            s.NotifyConfirm = cConf.Checked;
            s.NotifyDone = cDone.Checked;
            s.AttentionBlink = cBlink.Checked;
            s.AttentionPopup = cPopup.Checked;
            s.AttentionRemindMinutes = (int)nRemind.Value;
            s.AttentionAutoClose = cAutoClose.Checked;
            s.AttentionCloseSeconds = (int)nCloseSec.Value;
            string up = tUpdate.Text.Trim();
            s.UpdateFolder = up.Length == 0 ? null : NetAccess.ToUncIfMapped(up);
            s.AutoUpdate = cAutoUpdate.Checked;
            s.LocalLogDays = (int)nLocal.Value;
            s.NetLogDays = (int)nNet.Value;
            s.TrashDays = (int)nTrash.Value;
            try
            {
                StartupRegistration.Set(s.AutoStart);
                AppCore.SaveSettings();
                return true;
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); return false; }
        }
    }

    /// <summary>サーバーごとの ID・パスワード</summary>
    public class CredDialog : Form
    {
        public CredDialog(string server, string testPath)
        {
            Ui.SetupDialog(this, "ID・パスワード - " + server, 480, 220);
            string u, p;
            CredentialStore.TryLoad(server, out u, out p);
            TextBox tU = Ui.T(u, 260), tP = Ui.T(p, 260);
            tP.UseSystemPasswordChar = true;
            Label res = Ui.L("");
            FlowLayoutPanel b = Ui.Body();
            b.Controls.Add(Ui.Row(Ui.L("接続先", 100), Ui.L("\\\\" + server)));
            b.Controls.Add(Ui.Row(Ui.L("ユーザーID", 100), tU));
            b.Controls.Add(Ui.Row(Ui.L("パスワード", 100), tP));
            b.Controls.Add(Ui.Row(Ui.L("", 100), Ui.B("接続テスト", delegate
            {
                string user = tU.Text, pass = tP.Text;
                string err = Ui.Busy<string>(this, "接続を確認しています…", delegate() { return NetAccess.Test(testPath ?? ("\\\\" + server), user, pass); });
                res.Text = err == null ? "接続できました" : err;
                res.ForeColor = err == null ? Ui.Ok : Ui.Err;
            }), res));
            Button ok = Ui.B("保存", delegate
            {
                try { CredentialStore.Save(server, tU.Text, tP.Text); DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { Ui.Error(this, ex.Message); }
            });
            Controls.Add(b);
            Controls.Add(Ui.Footer(Ui.B("キャンセル", delegate { Close(); }), ok));
        }
    }

    /// <summary>このPCのフォルダ名を変える（コピー先のフォルダ名も合わせて変える）</summary>
    public class FolderNameForm : Form
    {
        private readonly TextBox tName;
        private readonly Label lCheck;

        public FolderNameForm()
        {
            Ui.SetupDialog(this, "このPCのフォルダ名", 560, 260);
            tName = Ui.T(AppCore.Settings.FolderName, 260);
            lCheck = Ui.L("");
            FlowLayoutPanel b = Ui.Body();
            b.Controls.Add(Ui.Wrap("他のPCとコピー先が重ならないための名前です。変えると、コピー先とログ保管先の「今のフォルダ名」のフォルダも新しい名前に変えます（再コピーはしません）。", 500));
            b.Controls.Add(Ui.Row(Ui.L("新しいフォルダ名", 120), tName, Ui.B("使えるか確認", delegate { Check(); })));
            b.Controls.Add(Ui.Row(Ui.L("", 120), lCheck));
            Controls.Add(b);
            Controls.Add(Ui.Footer(Ui.B("キャンセル", delegate { Close(); }), Ui.B("変更する", delegate { Apply(); })));
        }

        private class FolderMove { public string From, To; }

        private List<FolderMove> PlanMoves(string oldName, string newName)
        {
            List<FolderMove> list = new List<FolderMove>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JobConfig j in AppCore.Settings.Jobs)
            {
                string t = j.Dest ?? "";
                int idx = t.IndexOf("{フォルダ名}");
                if (idx < 0) continue;
                int end = t.IndexOf('\\', idx);
                string prefix = end < 0 ? t : t.Substring(0, end);
                FolderMove m = new FolderMove();
                m.From = NetAccess.ToUncIfMapped(PathUtil.Expand(prefix, oldName));
                m.To = NetAccess.ToUncIfMapped(PathUtil.Expand(prefix, newName));
                if (seen.Add(m.From)) list.Add(m);
            }
            if (!string.IsNullOrEmpty(AppCore.Settings.LogRoot))
            {
                FolderMove m = new FolderMove();
                m.From = PathUtil.Combine(AppCore.Settings.LogRoot, oldName);
                m.To = PathUtil.Combine(AppCore.Settings.LogRoot, newName);
                if (seen.Add(m.From)) list.Add(m);
            }
            return list;
        }

        private string Check()
        {
            string n = tName.Text.Trim();
            if (!PathUtil.IsValidFolderName(n)) { lCheck.Text = "使えない文字が入っています（\\ / : * ? \" < > |）"; lCheck.ForeColor = Ui.Err; return lCheck.Text; }
            if (string.Equals(n, AppCore.Settings.FolderName, StringComparison.OrdinalIgnoreCase)) { lCheck.Text = "今と同じ名前です"; lCheck.ForeColor = Ui.Quiet; return lCheck.Text; }
            List<FolderMove> moves = PlanMoves(AppCore.Settings.FolderName, n);
            string err = Ui.Busy<string>(this, "確認しています…", delegate()
            {
                foreach (FolderMove m in moves)
                {
                    try
                    {
                        NetAccess.Ensure(m.To, 0, 1, CancellationToken.None, null);
                        if (Directory.Exists(PathUtil.Long(m.To)))
                        {
                            string other = CopyEngine.FindOtherOwner(m.To, AppCore.Settings.InstanceId);
                            return other != null ? "この名前は他のPC（" + other + "）が使っています" : "同じ名前のフォルダが既にあります: " + m.To;
                        }
                    }
                    catch (Exception ex) { return "確認できませんでした: " + ex.Message; }
                }
                return null;
            });
            lCheck.Text = err ?? "使用できます";
            lCheck.ForeColor = err == null ? Ui.Ok : Ui.Err;
            return err;
        }

        private void Apply()
        {
            string err = Check();
            if (err != null) return;
            string oldName = AppCore.Settings.FolderName, newName = tName.Text.Trim();
            List<FolderMove> moves = PlanMoves(oldName, newName);
            if (!Ui.Confirm(this, "フォルダ名を「" + oldName + "」から「" + newName + "」に変えます。\nコピー先・ログ保管先のフォルダも名前を変えます。よろしいですか？")) return;
            foreach (JobConfig j in AppCore.Settings.Jobs) AppCore.CancelJob(j.Id);
            List<string> errors = new List<string>();
            foreach (FolderMove m in moves)
            {
                try
                {
                    if (Directory.Exists(PathUtil.Long(m.From)) && !Directory.Exists(PathUtil.Long(m.To)))
                    {
                        SafeFs.Move(m.From, m.To);
                        Logger.Write(null, "RENAME", "フォルダ名の変更：" + m.From + " → " + m.To, -1, -1);
                    }
                }
                catch (Exception ex) { errors.Add(m.From + "：" + ex.Message); }
            }
            AppCore.Settings.FolderName = newName;
            AppCore.SaveSettings();
            if (errors.Count > 0) Ui.Error(this, "名前を変えられなかったフォルダがあります（次回のコピーで新しいフォルダに作り直します）：\n" + string.Join("\n", errors.ToArray()));
            else Ui.Info(this, "フォルダ名を変えました。");
            Close();
        }
    }

    /// <summary>配布フォルダの作成</summary>
    public class DistCreateForm : Form
    {
        private readonly TextBox tName, tPass, tPass2, tOut;
        private readonly NumericUpDown nVer;
        private readonly ListView lvCred;
        private readonly List<string> servers;

        public DistCreateForm()
        {
            AppSettings s = AppCore.Settings;
            Ui.SetupDialog(this, "配布フォルダを作成", 740, 660);
            FlowLayoutPanel b = Ui.Body();
            b.Controls.Add(Ui.Wrap("このPCの設定のうち「配布設定に含める」ジョブと、ログ保管先・保存期間・ID とパスワードを配布用のファイルにします。各PCのフォルダ名・このPCだけのジョブは含めません。", 660));

            tName = Ui.T(s.DistName ?? "", 240);
            nVer = Ui.N(1, 100000, s.DistVersion + 1, 80);
            b.Controls.Add(Ui.Row(Ui.L("配布設定の名前", 130), tName, Ui.Hint("例：経理部")));
            b.Controls.Add(Ui.Row(Ui.L("版", 130), nVer, Ui.Hint("取り込み済みより新しい版だけが自動で取り込まれます")));

            List<string> names = new List<string>();
            int noVar = 0;
            foreach (JobConfig j in s.Jobs)
            {
                if (j.Scope != JobScope.Dist) continue;
                names.Add(j.Name + (j.Mirror ? "（ミラー）" : ""));
                if (!PathUtil.HasFolderVariable(j.Dest)) noVar++;
            }
            Label jl = Ui.Wrap(names.Count == 0 ? "配布設定に含めるジョブがありません。ジョブ設定の「保存場所」で「配布設定に含める」を選んでください。" : "含めるジョブ：" + string.Join("、", names.ToArray()), 640);
            if (names.Count == 0) jl.ForeColor = Ui.Err;
            Label warn = Ui.Wrap(noVar > 0 ? "注意：コピー先に {フォルダ名} が入っていないジョブが " + noVar + "件 あります。複数のPCが同じコピー先に書き込み、互いのファイルを上書きするおそれがあります。" : "", 640);
            warn.ForeColor = Ui.Warn;
            b.Controls.Add(Ui.Section("ジョブ", jl, warn));

            tPass = Ui.T("", 220); tPass.UseSystemPasswordChar = true;
            tPass2 = Ui.T("", 220); tPass2.UseSystemPasswordChar = true;
            b.Controls.Add(Ui.Section("配布用パスワード（各PCの初回セットアップで1回入力します）",
                Ui.Row(Ui.L("パスワード", 130), tPass, Ui.Hint("4文字以上")),
                Ui.Row(Ui.L("もう一度", 130), tPass2)));

            servers = Distribution.ServersUsed(s, true);
            lvCred = Ui.List("サーバー|200", "ユーザーID|200", "状態|160");
            lvCred.Size = new Size(Ui.S(600), Ui.S(110));
            Button setCred = Ui.B("ID・パスワードを設定…", delegate
            {
                if (lvCred.SelectedItems.Count == 0) { Ui.Info(this, "サーバーを選んでください。"); return; }
                string sv = (string)lvCred.SelectedItems[0].Tag;
                using (CredDialog d = new CredDialog(sv, null)) d.ShowDialog(this);
                FillCreds();
            });
            b.Controls.Add(Ui.Section("配布設定に含める ID・パスワード（暗号化して含めます）", lvCred, setCred));

            tOut = Ui.T(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "自動コピー配布"), 440);
            b.Controls.Add(Ui.Row(Ui.L("作成する場所", 130), tOut, Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, tOut.Text, "配布フォルダを作る場所（USBメモリなど）"); if (f != null) tOut.Text = Path.Combine(f, "自動コピー配布"); })));
            b.Controls.Add(Ui.Hint("Windows 7 のPCに配る場合は、ndp48-x86-x64-allos-jpn.exe を AutoCopy.exe と同じフォルダに置いておくと同梱されます。"));

            Button create = Ui.B("作成", delegate { Create(); });
            create.MinimumSize = new Size(Ui.S(88), Ui.S(28));
            Controls.Add(b);
            Controls.Add(Ui.Footer(Ui.B("キャンセル", delegate { Close(); }), create));
            FillCreds();
        }

        private void FillCreds()
        {
            lvCred.Items.Clear();
            foreach (string sv in servers)
            {
                string u, p;
                bool ok = CredentialStore.TryLoad(sv, out u, out p);
                ListViewItem it = new ListViewItem("\\\\" + sv);
                it.SubItems.Add(ok ? u : "");
                it.SubItems.Add(ok ? "含めます" : "未設定（含めません）");
                it.ForeColor = ok ? SystemColors.WindowText : Ui.Warn;
                it.Tag = sv;
                lvCred.Items.Add(it);
            }
            if (servers.Count == 0)
            {
                ListViewItem it = new ListViewItem("（ネットワークのフォルダはありません）");
                lvCred.Items.Add(it);
            }
        }

        private void Create()
        {
            AppSettings s = AppCore.Settings;
            string name = tName.Text.Trim();
            if (name.Length == 0) { Ui.Error(this, "配布設定の名前を入力してください。"); return; }
            if (tPass.Text.Length < 4) { Ui.Error(this, "配布用パスワードは4文字以上にしてください。"); return; }
            if (tPass.Text != tPass2.Text) { Ui.Error(this, "パスワードが一致しません。"); return; }
            bool anyDist = false;
            foreach (JobConfig j in s.Jobs) if (j.Scope == JobScope.Dist) anyDist = true;
            if (!anyDist && !Ui.Confirm(this, "ジョブが入っていません（ログ保管先などの設定だけを配ります）。作成しますか？")) return;
            List<DistCredential> creds = new List<DistCredential>();
            foreach (string sv in servers)
            {
                string u, p;
                if (!CredentialStore.TryLoad(sv, out u, out p)) continue;
                DistCredential c = new DistCredential();
                c.Server = sv; c.User = u; c.Password = p;
                creds.Add(c);
            }
            int ver = (int)nVer.Value;
            DistPayload payload = Distribution.BuildPayload(s, name, ver, creds);
            string folder = tOut.Text.Trim();
            string pass = tPass.Text;
            // 更新版の置き場所が決まっていなければ、NAS に作る配布フォルダをそのまま使う
            if (string.IsNullOrEmpty(payload.UpdateFolder))
            {
                string unc = NetAccess.ToUncIfMapped(folder);
                if (PathUtil.IsUnc(unc)) payload.UpdateFolder = unc;
            }
            try
            {
                List<string> notes = Ui.Busy<List<string>>(this, "配布フォルダを作っています…", delegate() { return Distribution.CreateFolder(folder, payload, pass); });
                s.DistName = name;
                s.DistVersion = ver;
                s.DistCreated = payload.Created;
                AppCore.SaveSettings();
                Logger.Info(null, "配布フォルダを作成しました：" + folder + "（" + name + " 第" + ver + "版）");
                string msg = "配布フォルダを作成しました：\n" + folder + "\n\nこのフォルダを各PCにコピーし、AutoCopy.exe を起動してください。\n配布用パスワードは、配布フォルダとは別の方法で伝えてください。";
                if (notes.Count > 0) msg += "\n\n" + string.Join("\n", notes.ToArray());
                Ui.Info(this, msg);
                Ui.OpenFolder(folder);
                Close();
            }
            catch (Exception ex) { Ui.Error(this, "作成できませんでした：" + ex.Message); }
        }
    }
}
