using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>
    /// 初回セットアップ（配布設定の取り込み、または配布設定なしの初回起動）。
    /// 新しい版の配布設定を読み込むときにも使う。
    /// </summary>
    public class SetupForm : Form
    {
        public string RestartPath;
        private readonly string setupPath;
        private readonly DistEnvelope env;
        private readonly bool firstRun;
        private DistPayload payload;
        private readonly TextBox tPass, tFolder, tLogRoot;
        private readonly Label lContent, lCheck, lPreview;
        private readonly CheckBox cAuto, cShortcut, cMirrorAgree;
        private readonly Panel mirrorPanel;
        private readonly Label mirrorText;

        public SetupForm(string setupPath, DistEnvelope env, bool firstRun)
        {
            this.setupPath = setupPath;
            this.env = env;
            this.firstRun = firstRun;
            bool dist = env != null;
            Ui.SetupDialog(this, "自動コピー - " + (firstRun ? "初回セットアップ" : "配布設定の読み込み"), 700, dist ? 660 : 520);
            FlowLayoutPanel b = Ui.Body();

            b.Controls.Add(Ui.Heading(firstRun ? "このPCで自動コピーを使い始めます" : "新しい配布設定を読み込みます"));
            b.Controls.Add(Ui.Hint(dist ? "配布された設定を取り込みます。入力が必要なのは下の2つだけです。" : "このPCのフォルダ名を決めてください。コピーの設定は、このあとメイン画面で作ります。"));

            lContent = Ui.Wrap("", 620);
            if (dist)
            {
                b.Controls.Add(Ui.Section("配布設定", Ui.Wrap(env.Name + "　第" + env.Version + "版（" + env.Created + " 作成）", 620), lContent));
                lContent.Text = "内容は配布用パスワードを入れると表示されます。";
                lContent.ForeColor = Ui.Quiet;
            }

            tPass = Ui.T("", 260);
            tPass.UseSystemPasswordChar = true;
            if (dist)
            {
                b.Controls.Add(Ui.Section("1. 配布用パスワード",
                    Ui.Row(tPass, Ui.B("確認", delegate { Unlock(true); })),
                    Ui.Hint("配布担当者から伝えられたパスワードです。")));
            }

            tFolder = Ui.T(string.IsNullOrEmpty(AppCore.Settings.FolderName) ? Environment.MachineName : AppCore.Settings.FolderName, 260);
            tFolder.TextChanged += delegate { UpdatePreview(); lCheck.Text = ""; };
            lCheck = Ui.L("");
            lPreview = Ui.Wrap("", 620);
            lPreview.ForeColor = Ui.Accent;
            b.Controls.Add(Ui.Section(dist ? "2. このPCのフォルダ名" : "このPCのフォルダ名",
                Ui.Row(tFolder, Ui.B("使えるか確認", delegate { CheckFolder(true); }), lCheck),
                Ui.Hint("他のPCとコピー先が重ならないための名前です。自由に決められます（初期値はPC名）。"),
                lPreview));

            tLogRoot = Ui.T(AppCore.Settings.LogRoot, 380);
            if (!dist)
                b.Controls.Add(Ui.Section("ログ保管先（任意）", tLogRoot, Ui.Hint("ネットワークドライブに全PCのログを集める場合に指定します。空欄でもかまいません。")));

            mirrorText = Ui.Wrap("", 600);
            mirrorText.ForeColor = Ui.Warn;
            mirrorText.Font = Ui.Bold;
            cMirrorAgree = Ui.C("内容を理解しました", false);
            mirrorPanel = Ui.Banner(Ui.WarnBack, Ui.Column(mirrorText,
                Ui.Wrap("コピー元で削除したファイルは、コピー先からも削除されます（" + AppCore.Settings.TrashDays + "日間は元に戻せます）。", 600), cMirrorAgree));
            mirrorPanel.Visible = false;
            b.Controls.Add(mirrorPanel);

            cAuto = Ui.C("Windows にサインインしたら自動で起動する", true);
            cShortcut = Ui.C("デスクトップに「" + Shortcuts.ForceSendName + "」のショートカットを作る（ダブルクリックですぐにコピー）", firstRun);
            b.Controls.Add(cAuto);
            b.Controls.Add(cShortcut);

            Button start = Ui.B(firstRun ? "開始" : "読み込む", delegate { Finish(); });
            start.MinimumSize = new Size(Ui.S(88), Ui.S(28));
            Button cancel = Ui.B("キャンセル", delegate { DialogResult = DialogResult.Cancel; Close(); });
            AcceptButton = start;
            CancelButton = cancel;
            Controls.Add(b);
            FlowLayoutPanel foot = Ui.Footer(cancel, start);
            foot.Controls.Add(Ui.HelpButton(Help.Setup));
            Label fh = Ui.Hint("開始すると、最初のコピーを1分後に行います。");
            fh.Margin = new Padding(3, 10, 120, 3);
            if (firstRun) foot.Controls.Add(fh);
            Controls.Add(foot);
            UpdatePreview();
        }

        private bool Unlock(bool showErrors)
        {
            if (env == null) return true;
            if (payload != null) return true;
            string pass = tPass.Text;
            try
            {
                payload = Ui.Busy<DistPayload>(this, "配布設定を確認しています…", delegate() { return Distribution.Decrypt(env, pass); });
                if (Distribution.IsWeakLegacyPassword(pass))
                    Logger.Info(null, "この配布設定のパスワードは短いため（" + Distribution.MinPasswordLength + "文字未満）、配布設定を作り直すことをお勧めします（前の版で作ったものもそのまま使えます）");
            }
            catch (Distribution.WrongPasswordException ex)
            {
                if (showErrors) Ui.Error(this, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                if (showErrors) Ui.Error(this, "配布設定を読めません：" + ex.Message);
                return false;
            }
            List<string> names = new List<string>();
            List<string> mirrors = new List<string>();
            foreach (JobConfig j in payload.Jobs)
            {
                string iv = Fmt.Interval(j.IntervalMinutes);
                names.Add(j.Name + "（" + iv + (j.Mirror ? "・ミラー" : "") + "）");
                if (j.Mirror) mirrors.Add(j.Name);
            }
            lContent.ForeColor = SystemColors.WindowText;
            lContent.Text = "ジョブ：" + (names.Count > 0 ? string.Join("、", names.ToArray()) : "なし") +
                "\nログ保管先：" + (string.IsNullOrEmpty(payload.LogRoot) ? "なし" : payload.LogRoot) +
                "\nID・パスワード：" + (payload.Credentials != null ? payload.Credentials.Count : 0) + "件 含まれています（暗号化済み）";
            if (mirrors.Count > 0)
            {
                mirrorText.Text = "「" + string.Join("」「", mirrors.ToArray()) + "」はミラーです";
                mirrorPanel.Visible = true;
            }
            tPass.Enabled = false;
            UpdatePreview();
            return true;
        }

        private List<string> DestTemplates()
        {
            List<string> l = new List<string>();
            List<JobConfig> jobs = payload != null ? payload.Jobs : AppCore.Settings.Jobs;
            foreach (JobConfig j in jobs) if (PathUtil.HasFolderVariable(j.Dest)) l.Add(j.Dest);
            return l;
        }

        private void UpdatePreview()
        {
            string n = tFolder.Text.Trim();
            List<string> t = DestTemplates();
            if (t.Count == 0) { lPreview.Text = ""; return; }
            List<string> lines = new List<string>();
            foreach (string d in t) lines.Add(PathUtil.Expand(d, n));
            lPreview.Text = "このPCのコピー先：\n" + string.Join("\n", lines.ToArray());
        }

        /// <summary>フォルダ名が使えるか。使えれば null。</summary>
        private string CheckFolder(bool show)
        {
            string n = tFolder.Text.Trim();
            if (!PathUtil.IsValidFolderName(n))
            {
                lCheck.Text = "使えない文字が入っています（\\ / : * ? \" < > |）";
                lCheck.ForeColor = Ui.Err;
                return lCheck.Text;
            }
            if (env != null && !Unlock(show)) return "配布用パスワードを確認してください。";
            List<string> templates = DestTemplates();
            List<DistCredential> creds = payload != null ? payload.Credentials : null;
            string instance = AppCore.Settings.InstanceId;
            string result = Ui.Busy<string>(this, "コピー先を確認しています…", delegate()
            {
                foreach (string t in templates)
                {
                    string d = NetAccess.ToUncIfMapped(PathUtil.Expand(t, n));
                    try
                    {
                        string sv = PathUtil.UncServer(d);
                        if (sv != null && creds != null)
                            foreach (DistCredential c in creds)
                                if (string.Equals(c.Server, sv, StringComparison.OrdinalIgnoreCase)) NetAccess.Test(d, c.User, c.Password);
                        string other = CopyEngine.FindOtherOwner(d, instance);
                        if (other != null) return "このフォルダ名は他のPC（" + other + "）が使っています";
                    }
                    catch (Exception) { }
                }
                return null;
            });
            lCheck.Text = result ?? "使用できます";
            lCheck.ForeColor = result == null ? Ui.Ok : Ui.Err;
            return result;
        }

        private void Finish()
        {
            if (env != null && !Unlock(true)) return;
            string folder = tFolder.Text.Trim();
            string err = CheckFolder(false);
            if (err != null) { Ui.Error(this, err); return; }
            if (mirrorPanel.Visible && !cMirrorAgree.Checked) { Ui.Error(this, "ミラーのジョブについて「内容を理解しました」にチェックを入れてください。"); return; }

            AppSettings s = Json.Deserialize<AppSettings>(Json.Serialize(AppCore.Settings));
            List<string> changedPaths = new List<string>();
            int newDisabled = 0;
            List<JobConfig> reserved = new List<JobConfig>();
            try
            {
                // 動いているジョブが止まるのを待つ（画面が固まらないよう、待っている間は「お待ちください」を出す）
                foreach (JobConfig j in s.Jobs.ToArray()) { string id = j.Id; Ui.Busy<int>(this, "ジョブの停止を待っています…", delegate { AppCore.StopJobAndWait(id); return 0; }); reserved.Add(j); }
                if (payload != null)
                {
                    // 前の版と比べ、コピー元・コピー先が変わったジョブは記録を使わない
                    Dictionary<string, JobConfig> before = new Dictionary<string, JobConfig>();
                    foreach (JobConfig j in s.Jobs) before[j.Id] = j;
                    newDisabled = Distribution.Apply(s, payload);
                    // 更新版の置き場所が配布設定になければ、この配布フォルダを使う（NAS の配布フォルダに新しい exe を置けば全PCが更新される）
                    if (string.IsNullOrEmpty(s.UpdateFolder) && setupPath != null)
                    {
                        string distDir = Path.GetDirectoryName(Path.GetFullPath(setupPath));
                        if (PathUtil.IsUnc(NetAccess.ToUncIfMapped(distDir))) s.UpdateFolder = NetAccess.ToUncIfMapped(distDir);
                    }
                    foreach (JobConfig j in s.Jobs)
                    {
                        JobConfig o;
                        if (before.TryGetValue(j.Id, out o) &&
                            (!string.Equals(o.Source, j.Source, StringComparison.OrdinalIgnoreCase) || !string.Equals(o.Dest, j.Dest, StringComparison.OrdinalIgnoreCase)))
                        {
                            changedPaths.Add(j.Id);
                        }
                    }
                }
                else if (env == null)
                {
                    string lr = tLogRoot.Text.Trim();
                    s.LogRoot = lr.Length == 0 ? null : NetAccess.ToUncIfMapped(lr);
                }
                s.FolderName = folder;
                s.AutoStart = cAuto.Checked;
                AppCore.Settings = s;
                AppCore.SaveSettings();
                foreach (string id in changedPaths) AppCore.ForgetJobState(id);

                // インストール先（%LOCALAPPDATA%\AutoCopy\bin）にコピーする。動いている本体は置き換えない。
                string exe = AppPaths.ExePath;
                bool runningFromBin = string.Equals(PathUtil.Normalize(AppPaths.ExeDir), PathUtil.Normalize(AppPaths.BinDir), StringComparison.OrdinalIgnoreCase);
                bool otherInstanceIsMe = TrayApp.Instance != null; // 動作中のアプリから呼ばれた
                if (!runningFromBin && !otherInstanceIsMe)
                {
                    exe = Installer.InstallToBin();
                    RestartPath = exe;
                }
                else if (!runningFromBin && otherInstanceIsMe)
                {
                    exe = Path.Combine(AppPaths.BinDir, "AutoCopy.exe");
                    if (!File.Exists(exe)) exe = AppPaths.ExePath;
                }
                StartupRegistration.Set(cAuto.Checked, exe);
                if (cShortcut.Checked)
                {
                    try { Shortcuts.CreateOnDesktop(Shortcuts.ForceSendName, "/run", exe); }
                    catch (Exception ex) { Logger.Error(null, "ショートカットを作れませんでした: " + ex.Message); }
                }
                Logger.Info(null, payload != null
                    ? "配布設定を取り込みました：" + payload.Name + " 第" + payload.Version + "版（フォルダ名：" + folder + "）"
                    : "初回セットアップを行いました（フォルダ名：" + folder + "）");
                if (newDisabled > 0)
                {
                    Logger.Info(null, "配布設定で新しく加わったジョブ " + newDisabled + "件 を「無効」で取り込みました");
                    Ui.Info(this, "配布設定で新しく加わったジョブ " + newDisabled + "件 は「無効」で取り込みました。\n"
                        + "メイン画面でコピー元・コピー先などの内容を確かめてから、ジョブの左のチェックを入れて有効にしてください。");
                }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Ui.Error(this, "セットアップできませんでした：" + ex.Message);
            }
            finally { foreach (JobConfig j in reserved) AppCore.EndResolve(j); }
        }
    }
}
