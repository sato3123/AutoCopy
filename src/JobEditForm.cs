using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>ジョブ設定（基本／除外ルール／接続・重複チェック）</summary>
    public class JobEditForm : Form
    {
        public JobConfig Result;
        private readonly JobConfig job;
        private readonly bool isNew;
        private bool loading = true;

        // 基本
        private TextBox tName, tSrc, tDst, tStart, tEnd;
        private CheckBox cEnabled, cSub, cWindow, cDelay, cFollow, cBackup, cDedup;
        private RadioButton rDist, rLocal, rAppend, rMirror, rSync, rAsk, rSkip, rOver;
        private NumericUpDown nInterval, nDelay, nTol, nPar, nBw, nMirrorCount, nMirrorPct, nDedup;
        private ComboBox cbUnit;
        private Label lPreview;
        // 除外
        private readonly Dictionary<string, CheckBox> presetBoxes = new Dictionary<string, CheckBox>();
        private TextBox tExt, tPat, tInc;
        private CheckBox cMax, cZero, cOlder, cHidden, cSystem;
        private NumericUpDown nMax;
        private ComboBox cbMaxUnit;
        private DateTimePicker dOlder;
        private ListBox lbFolders;
        private Label lTest;
        private List<string> lastExcluded = new List<string>();
        // 接続・重複
        private ComboBox cbServer;
        private TextBox tUser, tPass, tDupTime;
        private CheckBox cSaveCred, cDup, cSame, cOrphan, cEmpty;
        private Label lServerNote, lConn;
        private NumericUpDown nRetry, nRetryInt;
        private RadioButton rDaily, rWeekly, rManual;
        private ComboBox cbWeekday;
        private readonly Dictionary<string, string[]> credEdits = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private string currentServer;

        public JobEditForm(JobConfig j, bool isNew)
        {
            job = j;
            this.isNew = isNew;
            Ui.SetupForm(this, "ジョブ設定 - " + j.Name, 800, 660);
            MinimizeBox = false;

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(14, 5);
            tabs.TabPages.Add(BuildBasic());
            tabs.TabPages.Add(BuildExclude());
            tabs.TabPages.Add(BuildConnect());

            Button ok = Ui.B("OK", delegate { if (Collect()) { DialogResult = DialogResult.OK; Close(); } });
            ok.MinimumSize = new Size(Ui.S(88), Ui.S(28));
            Button cancel = Ui.B("キャンセル", delegate { DialogResult = DialogResult.Cancel; Close(); });
            Button test = Ui.B("テスト（コピーせずに件数を確認）", delegate { RunTest(); });
            FlowLayoutPanel foot = Ui.Footer(cancel, ok);
            test.Margin = new Padding(3, 3, 200, 3);
            foot.Controls.Add(test);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(foot);
            LoadValues();
            loading = false;
            UpdateSyncControls();
            UpdatePreview();
        }

        // ================= 基本 =================

        private TabPage BuildBasic()
        {
            TabPage p = new TabPage("基本");
            FlowLayoutPanel b = Ui.Body();
            tName = Ui.T("", 260);
            cEnabled = Ui.C("このジョブを有効にする", true);
            b.Controls.Add(Ui.Row(Ui.L("ジョブ名", 130), tName, cEnabled));

            rDist = Ui.R("配布設定に含める（全台で使う）", false);
            rLocal = Ui.R("このPCだけ", true);
            b.Controls.Add(Ui.Row(Ui.L("保存場所", 130), Ui.Row(rDist, rLocal)));

            tSrc = Ui.T("", 440);
            tSrc.TextChanged += delegate { UpdatePreview(); };
            b.Controls.Add(Ui.Row(Ui.L("コピー元フォルダ", 130), tSrc, Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, tSrc.Text, "コピー元フォルダを選んでください"); if (f != null) tSrc.Text = f; })));
            cSub = Ui.C("サブフォルダも含める", true);
            b.Controls.Add(Ui.Row(Ui.L("", 130), cSub));

            tDst = Ui.T("", 440);
            tDst.TextChanged += delegate { UpdatePreview(); };
            b.Controls.Add(Ui.Row(Ui.L("コピー先フォルダ", 130), tDst, Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, PathUtil.Expand(tDst.Text, AppCore.Settings.FolderName), "コピー先フォルダを選んでください"); if (f != null) tDst.Text = f; })));
            lPreview = Ui.Hint("");
            lPreview.MaximumSize = new Size(Ui.S(600), 0);
            Button ins = Ui.B("{フォルダ名} を末尾に入れる", delegate
            {
                string t = tDst.Text.TrimEnd('\\');
                if (!PathUtil.HasFolderVariable(t)) tDst.Text = t + "\\{フォルダ名}";
            });
            b.Controls.Add(Ui.Row(Ui.L("", 130), Ui.Column(lPreview,
                Ui.Hint("使える変数：{フォルダ名}（各PCの使用者が決める名前）　%COMPUTERNAME%　%USERNAME%　%DOCUMENTS%"),
                Ui.Hint("PC内のフォルダ・外付けドライブ・ネットワークドライブのどれでも指定できます。"), ins)));

            rAppend = Ui.R("追加・更新のみ（コピー先から削除しない）", true);
            rMirror = Ui.R("ミラー（コピー元で削除したらコピー先も削除）", false);
            rMirror.ForeColor = Ui.Warn;
            rMirror.CheckedChanged += OnMirrorChanged;
            b.Controls.Add(Ui.Row(Ui.L("コピーの方式", 130), Ui.Row(rAppend, rMirror)));
            nMirrorCount = Ui.N(1, 1000000, 100, 80);
            nMirrorPct = Ui.N(1, 100, 10, 56);
            b.Controls.Add(Ui.Row(Ui.L("", 130), Ui.Hint("ミラーで一度に"), nMirrorCount, Ui.Hint("件、またはコピー先の"), nMirrorPct, Ui.Hint("% を超える削除は確認する")));
            rSync = Ui.R("複数PCで同期（例外：コピー先を共有フォルダにして、複数のPCで双方向に同期する）", false);
            rSync.ForeColor = Ui.Warn;
            rSync.CheckedChanged += OnSyncChanged;
            b.Controls.Add(Ui.Row(Ui.L("", 130), rSync));
            b.Controls.Add(Ui.Row(Ui.L("", 130), Ui.Wrap("同期では、他のPCが共有フォルダに入れた・更新したファイルを、このPCのコピー元フォルダにも取り込みます。削除はどちらにも伝えません。両方で変えたファイルは「上書きの確認」で選びます。取り込みで置き換えたこのPCの古い版は控えに残ります。", 600)));

            nInterval = Ui.N(1, 1440, 30, 72);
            cbUnit = Ui.Combo(80, "分", "時間");
            b.Controls.Add(Ui.Row(Ui.L("実行間隔", 130), nInterval, cbUnit, Ui.Hint("1分〜24時間")));

            cWindow = Ui.C("時間帯を限定する", false);
            tStart = Ui.T("08:00", 60);
            tEnd = Ui.T("20:00", 60);
            b.Controls.Add(Ui.Row(Ui.L("実行する時間帯", 130), cWindow, tStart, Ui.L("〜"), tEnd));

            cDelay = Ui.C("PC起動後", true);
            nDelay = Ui.N(0, 120, 1, 56);
            b.Controls.Add(Ui.Row(Ui.L("起動後の初回", 130), cDelay, nDelay, Ui.L("分待ってから実行する")));

            cFollow = Ui.C("コピー元で名前やフォルダ名を変えたら、コピー先も同じ名前に変える（再コピーしない・確認なし）", true);
            rAsk = Ui.R("毎回確認する（推奨）", true);
            rSkip = Ui.R("上書きしない", false);
            rOver = Ui.R("常に上書き", false);
            nTol = Ui.N(0, 3600, 2, 56);
            cBackup = Ui.C("上書きする前に、コピー先の古いファイルを「_AutoCopy\\退避」に残す", false);
            b.Controls.Add(Ui.Section("名前変更と上書きのルール",
                cFollow,
                Ui.Row(Ui.L("コピー先が古いとき", 160), Ui.L("自動で上書きする")),
                Ui.Row(Ui.L("コピー先が新しいとき", 160), Ui.Row(rAsk, rSkip, rOver)),
                Ui.Row(Ui.L("同じとみなす日時の差", 160), nTol, Ui.L("秒以内")),
                cBackup));

            cDedup = Ui.C("コピー元の中で中身が同じ大きいファイルは、1つだけコピーし、2つ目以降は保管場所を書いたテキスト（〇〇" + AppPaths.PlaceholderSuffix + "）を置く", true);
            nDedup = Ui.N(1, 1000000, 10, 72);
            b.Controls.Add(Ui.Section("大きいファイルの重複",
                cDedup,
                Ui.Row(Ui.L("対象の大きさ", 160), nDedup, Ui.L("MB 以上")),
                Ui.Wrap("中身はファイル全体で比べます（同じ大きさのものだけ読みます）。すでに普通にコピー済みのファイルはそのまま残します。同期のジョブでは使いません。", 600)));

            nPar = Ui.N(0, 8, 0, 56);
            b.Controls.Add(Ui.Row(Ui.L("同時にコピーする数", 130), nPar, Ui.Hint("0 = 自動（ネットワークは 2、PC内は 4）")));
            nBw = Ui.N(0, 1000000, 0, 90);
            b.Controls.Add(Ui.Row(Ui.L("転送速度の上限", 130), nBw, Ui.Hint("KB/秒（0 = 制限なし）")));
            p.Controls.Add(b);
            return p;
        }

        private void OnSyncChanged(object sender, EventArgs e)
        {
            UpdateSyncControls();
            if (loading || !rSync.Checked) return;
            string msg = "「複数PCで同期」は例外的な使い方です。\n\n"
                + "・コピー先は、同期するすべてのPCから見える共有フォルダにしてください（{フォルダ名} は使えません）。\n"
                + "・他のPCが入れた・更新したファイルが、このPCのコピー元フォルダに入ってきます。\n"
                + "・削除はどちらにも伝えません。両方で変えたファイルは確認画面で選びます。\n"
                + "・このPCの古い版は、置き換える前に控え（" + AppPaths.SyncBackupDir + "）に残します。\n"
                + "・重複チェックと大きいファイルの重複の省略は、同期では行いません。\n\n同期にしますか？";
            if (!Ui.Confirm(this, msg))
            {
                loading = true;
                rAppend.Checked = true;
                loading = false;
                UpdateSyncControls();
            }
        }

        private void UpdateSyncControls()
        {
            if (cDedup == null) return;
            bool sync = rSync != null && rSync.Checked;
            cDedup.Enabled = !sync; nDedup.Enabled = !sync;
            if (cDup != null) cDup.Enabled = !sync;
        }

        private void OnMirrorChanged(object sender, EventArgs e)
        {
            if (loading || !rMirror.Checked) return;
            JobConfig snap = BuildSnapshot();
            using (MirrorWarnForm f = new MirrorWarnForm(snap))
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                {
                    loading = true;
                    rAppend.Checked = true;
                    loading = false;
                }
            }
        }

        private void UpdatePreview()
        {
            if (loading || lPreview == null) return;
            string folder = AppCore.Settings.FolderName;
            string d = NetAccess.ToUncIfMapped(PathUtil.Expand(tDst.Text, folder));
            if (d.Length == 0) { lPreview.Text = ""; return; }
            lPreview.Text = "このPCでは " + d + "（種類：" + NetAccess.Describe(d) + "・自動判定）";
            if (cbServer != null) RefreshServers();
        }

        // ================= 除外ルール =================

        private TabPage BuildExclude()
        {
            TabPage p = new TabPage("除外ルール");
            FlowLayoutPanel b = Ui.Body();
            b.Controls.Add(Ui.Hint("次の条件のどれかに当てはまるファイルはコピーしません。"));

            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.AutoSize = true;
            presets.MaximumSize = new Size(Ui.S(700), 0);
            presets.WrapContents = true;
            foreach (string k in ExcludePresets.Keys)
            {
                CheckBox c = Ui.C(ExcludePresets.Label(k), false);
                presetBoxes[k] = c;
                presets.Controls.Add(c);
            }
            tExt = Ui.T("", 460);
            tPat = Ui.T("", 460);
            b.Controls.Add(Ui.Section("ファイルの種類", presets,
                Ui.Row(Ui.L("追加の拡張子", 110), tExt, Ui.Hint("例：.log; .psd")),
                Ui.Row(Ui.L("ファイル名", 110), tPat, Ui.Hint("* と ? が使えます"))));

            cMax = Ui.C("", false);
            nMax = Ui.N(1, 1000000, 500, 72);
            cbMaxUnit = Ui.Combo(56, "MB", "GB");
            cZero = Ui.C("0 バイトのファイル", false);
            cOlder = Ui.C("", false);
            dOlder = new DateTimePicker();
            dOlder.Format = DateTimePickerFormat.Short;
            dOlder.Width = Ui.S(120);
            dOlder.Value = new DateTime(2020, 1, 1);
            cHidden = Ui.C("隠しファイル", true);
            cSystem = Ui.C("システムファイル", true);
            b.Controls.Add(Ui.Section("サイズ・日付・属性",
                Ui.Row(cMax, nMax, cbMaxUnit, Ui.L("より大きい")),
                cZero,
                Ui.Row(cOlder, dOlder, Ui.L("より前に更新")),
                Ui.Row(cHidden, cSystem)));

            lbFolders = new ListBox();
            lbFolders.Size = new Size(Ui.S(380), Ui.S(100));
            b.Controls.Add(Ui.Section("フォルダ（名前だけ書くと全階層が対象。相対パスも可）",
                Ui.Row(lbFolders, Ui.Column(
                    Ui.B("フォルダを選んで追加…", delegate { AddFolderByBrowse(); }),
                    Ui.B("名前で追加…", delegate { AddFolderByName(); }),
                    Ui.B("削除", delegate { if (lbFolders.SelectedIndex >= 0) lbFolders.Items.RemoveAt(lbFolders.SelectedIndex); })))));

            tInc = Ui.T("", 460);
            b.Controls.Add(Ui.Section("例外（必ずコピー）", Ui.Row(tInc, Ui.Hint("例：会議録画\\*.mp4"))));

            lTest = Ui.L("");
            b.Controls.Add(Ui.Row(Ui.B("テスト（コピーせずに件数を確認）", delegate { RunTest(); }), lTest,
                Ui.B("除外されるファイルを表示", delegate
                {
                    using (TextListForm f = new TextListForm("除外されるファイル", "除外されるファイル（最大5000件）", lastExcluded)) f.ShowDialog(this);
                })));
            p.Controls.Add(b);
            return p;
        }

        private void AddFolderByBrowse()
        {
            string src = PathUtil.Expand(tSrc.Text, AppCore.Settings.FolderName);
            string f = Ui.BrowseFolder(this, src, "除外するフォルダを選んでください（コピー元の中）");
            if (f == null) return;
            if (PathUtil.IsSameOrInside(f, src) && !string.Equals(PathUtil.Normalize(f), PathUtil.Normalize(src), StringComparison.OrdinalIgnoreCase))
                lbFolders.Items.Add(PathUtil.Normalize(f).Substring(PathUtil.Normalize(src).TrimEnd('\\').Length + 1));
            else
                Ui.Info(this, "コピー元の中のフォルダを選んでください。");
        }

        private void AddFolderByName()
        {
            string name = InputBox.Show(this, "除外するフォルダ名", "フォルダ名（例：node_modules）または コピー元からの相対パス", "");
            if (!string.IsNullOrEmpty(name) && name.Trim().Length > 0) lbFolders.Items.Add(name.Trim());
        }

        // ================= 接続・重複チェック =================

        private TabPage BuildConnect()
        {
            TabPage p = new TabPage("接続・重複チェック");
            FlowLayoutPanel b = Ui.Body();
            cbServer = Ui.Combo(260);
            cbServer.SelectedIndexChanged += delegate { SwitchServer(); };
            tUser = Ui.T("", 280);
            tPass = Ui.T("", 280);
            tPass.UseSystemPasswordChar = true;
            Button show = Ui.B("表示", delegate { tPass.UseSystemPasswordChar = !tPass.UseSystemPasswordChar; });
            cSaveCred = Ui.C("ID・パスワードを保存する（Windows の資格情報マネージャーに暗号化して保存）", true);
            lConn = Ui.L("");
            lServerNote = Ui.Hint("");
            b.Controls.Add(Ui.Section("ネットワークドライブの接続",
                Ui.Row(Ui.L("接続先", 110), cbServer),
                lServerNote,
                Ui.Row(Ui.L("ユーザーID", 110), tUser, Ui.Hint("例：nas01\\yamada")),
                Ui.Row(Ui.L("パスワード", 110), tPass, show),
                cSaveCred,
                Ui.Row(Ui.L("", 110), Ui.B("接続テスト", delegate { TestConnection(); }), lConn)));

            nRetry = Ui.N(0, 99, 3, 56);
            nRetryInt = Ui.N(5, 3600, 30, 72);
            b.Controls.Add(Ui.Section("接続できないとき",
                Ui.Row(Ui.L("再試行", 110), nRetry, Ui.L("回、"), nRetryInt, Ui.L("秒おきに試す")),
                Ui.Wrap("すべて失敗したらエラーを通知し、次の実行時刻にもう一度試します。ID・パスワードの誤りのときは再試行しません。", 640)));

            cDup = Ui.C("重複チェックを行う", true);
            rDaily = Ui.R("毎日", true);
            rWeekly = Ui.R("毎週", false);
            cbWeekday = Ui.Combo(64, "日曜", "月曜", "火曜", "水曜", "木曜", "金曜", "土曜");
            rManual = Ui.R("手動のみ", false);
            tDupTime = Ui.T("12:00", 60);
            cSame = Ui.C("中身が同じファイル（情報として表示のみ）", true);
            cOrphan = Ui.C("コピー元にないファイル（孤立ファイル）も表示する", false);
            cEmpty = Ui.C("空のフォルダ（コピー元にないもの）", true);
            b.Controls.Add(Ui.Section("重複チェック",
                cDup,
                Ui.Row(Ui.L("実行", 110), rDaily, rWeekly, cbWeekday, rManual, Ui.L("時刻"), tDupTime),
                Ui.Row(Ui.L("探すもの", 110), Ui.Column(Ui.C("ファイル名・フォルダ名の変更や移動の取りこぼし（常に探します）", true), cEmpty, cSame, cOrphan)),
                Ui.Wrap("削除したファイルは「コピー先\\_AutoCopy\\削除済み」に移し、アプリ設定の保存日数が過ぎたら完全に削除します。", 640)));
            p.Controls.Add(b);
            return p;
        }

        private void RefreshServers()
        {
            List<string> servers = new List<string>();
            foreach (string path in new string[] { tSrc.Text, tDst.Text })
            {
                string sv = PathUtil.UncServer(NetAccess.ToUncIfMapped(PathUtil.Expand(path, AppCore.Settings.FolderName)));
                if (sv != null && !servers.Exists(delegate(string x) { return string.Equals(x, sv, StringComparison.OrdinalIgnoreCase); })) servers.Add(sv);
            }
            bool same = servers.Count == cbServer.Items.Count;
            if (same) for (int i = 0; i < servers.Count; i++) if (!string.Equals((string)cbServer.Items[i], servers[i], StringComparison.OrdinalIgnoreCase)) same = false;
            if (same) return;
            SaveServerEdits();
            cbServer.Items.Clear();
            foreach (string s in servers) cbServer.Items.Add(s);
            bool any = servers.Count > 0;
            tUser.Enabled = tPass.Enabled = cSaveCred.Enabled = cbServer.Enabled = any;
            lServerNote.Text = any ? "" : "コピー元・コピー先ともネットワークではないため、設定は不要です。";
            currentServer = null;
            if (any) cbServer.SelectedIndex = 0;
            else { tUser.Text = ""; tPass.Text = ""; }
        }

        private void SaveServerEdits()
        {
            if (currentServer != null) credEdits[currentServer] = new string[] { tUser.Text, tPass.Text };
        }

        private void SwitchServer()
        {
            SaveServerEdits();
            currentServer = cbServer.SelectedItem as string;
            if (currentServer == null) return;
            string[] e;
            if (credEdits.TryGetValue(currentServer, out e)) { tUser.Text = e[0]; tPass.Text = e[1]; return; }
            string u, pw;
            if (CredentialStore.TryLoad(currentServer, out u, out pw)) { tUser.Text = u; tPass.Text = pw; }
            else { tUser.Text = ""; tPass.Text = ""; }
        }

        private void TestConnection()
        {
            string target = NetAccess.ToUncIfMapped(PathUtil.Expand(tDst.Text, AppCore.Settings.FolderName));
            if (currentServer != null && !string.Equals(PathUtil.UncServer(target), currentServer, StringComparison.OrdinalIgnoreCase))
                target = NetAccess.ToUncIfMapped(PathUtil.Expand(tSrc.Text, AppCore.Settings.FolderName));
            string user = tUser.Text, pass = tPass.Text;
            lConn.Text = "確認中…";
            lConn.ForeColor = Ui.Quiet;
            string err = Ui.Busy<string>(this, "接続を確認しています…", delegate() { return NetAccess.Test(target, user, pass); });
            if (err == null) { lConn.Text = "接続できました"; lConn.ForeColor = Ui.Ok; }
            else { lConn.Text = err; lConn.ForeColor = Ui.Err; }
        }

        // ================= 値の読み書き =================

        private void LoadValues()
        {
            JobConfig j = job;
            tName.Text = j.Name;
            cEnabled.Checked = j.Enabled;
            rDist.Checked = j.Scope == JobScope.Dist;
            rLocal.Checked = j.Scope != JobScope.Dist;
            tSrc.Text = j.Source;
            cSub.Checked = j.IncludeSubdirs;
            tDst.Text = j.Dest;
            rMirror.Checked = j.Mirror && !j.Sync;
            rSync.Checked = j.Sync;
            rAppend.Checked = !j.Mirror && !j.Sync;
            cDedup.Checked = j.DedupLarge;
            nDedup.Value = Clamp(nDedup, j.DedupMinMB);
            nMirrorCount.Value = Clamp(nMirrorCount, j.MirrorMaxCount);
            nMirrorPct.Value = Clamp(nMirrorPct, j.MirrorMaxPercent);
            if (j.IntervalMinutes >= 60 && j.IntervalMinutes % 60 == 0) { cbUnit.SelectedIndex = 1; nInterval.Value = Clamp(nInterval, j.IntervalMinutes / 60); }
            else { cbUnit.SelectedIndex = 0; nInterval.Value = Clamp(nInterval, j.IntervalMinutes); }
            cWindow.Checked = j.UseTimeWindow;
            tStart.Text = j.WindowStart;
            tEnd.Text = j.WindowEnd;
            cDelay.Checked = j.UseStartupDelay;
            nDelay.Value = Clamp(nDelay, j.StartupDelayMinutes);
            cFollow.Checked = j.FollowRename;
            rAsk.Checked = j.NewerDestPolicy == NewerPolicy.Ask;
            rSkip.Checked = j.NewerDestPolicy == NewerPolicy.Skip;
            rOver.Checked = j.NewerDestPolicy == NewerPolicy.Overwrite;
            nTol.Value = Clamp(nTol, j.ToleranceSeconds);
            cBackup.Checked = j.BackupBeforeOverwrite;
            nPar.Value = Clamp(nPar, j.Parallel);
            nBw.Value = Clamp(nBw, j.BandwidthKBps);

            ExcludeRules x = j.Exclude ?? ExcludeRules.CreateDefault();
            foreach (KeyValuePair<string, CheckBox> kv in presetBoxes) kv.Value.Checked = x.Presets.Contains(kv.Key);
            tExt.Text = string.Join("; ", x.Extensions.ToArray());
            tPat.Text = string.Join("; ", x.NamePatterns.ToArray());
            if (x.MaxSizeBytes > 0)
            {
                cMax.Checked = true;
                if (x.MaxSizeBytes % (1024L * 1024 * 1024) == 0) { cbMaxUnit.SelectedIndex = 1; nMax.Value = Clamp(nMax, (int)(x.MaxSizeBytes / (1024L * 1024 * 1024))); }
                else { cbMaxUnit.SelectedIndex = 0; nMax.Value = Clamp(nMax, (int)Math.Max(1, x.MaxSizeBytes / (1024L * 1024))); }
            }
            cZero.Checked = x.ExcludeZeroByte;
            DateTime od;
            if (!string.IsNullOrEmpty(x.OlderThan) && DateTime.TryParse(x.OlderThan, out od)) { cOlder.Checked = true; dOlder.Value = od; }
            cHidden.Checked = x.ExcludeHidden;
            cSystem.Checked = x.ExcludeSystem;
            foreach (string f in x.Folders) lbFolders.Items.Add(f);
            tInc.Text = string.Join("; ", x.Includes.ToArray());

            nRetry.Value = Clamp(nRetry, j.RetryCount);
            nRetryInt.Value = Clamp(nRetryInt, j.RetryIntervalSeconds);
            cDup.Checked = j.DupCheckEnabled;
            rDaily.Checked = j.DupSchedule == "daily";
            rWeekly.Checked = j.DupSchedule == "weekly";
            rManual.Checked = j.DupSchedule == "manual";
            cbWeekday.SelectedIndex = Math.Max(0, Math.Min(6, j.DupWeekday));
            tDupTime.Text = j.DupTime;
            cSame.Checked = j.DupShowSameContent;
            cOrphan.Checked = j.DupShowOrphans;
            cEmpty.Checked = j.DupShowEmptyFolders;
            loading = false;
            RefreshServers();
            loading = true;
        }

        private static decimal Clamp(NumericUpDown n, int v)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        private static List<string> SplitList(string s)
        {
            List<string> l = new List<string>();
            foreach (string p in (s ?? "").Split(new char[] { ';', ',', '、' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = p.Trim();
                if (t.Length > 0) l.Add(t);
            }
            return l;
        }

        /// <summary>画面の値から JobConfig を作る（検証なし）</summary>
        private JobConfig BuildSnapshot()
        {
            JobConfig j = job.Clone();
            j.Name = tName.Text.Trim();
            j.Enabled = cEnabled.Checked;
            j.Scope = rDist.Checked ? JobScope.Dist : JobScope.Local;
            j.Source = ToUncIfNetworkDrive(tSrc.Text.Trim());
            j.IncludeSubdirs = cSub.Checked;
            j.Dest = ToUncIfNetworkDrive(tDst.Text.Trim());
            j.Mirror = rMirror.Checked;
            j.Sync = rSync.Checked;
            j.DedupLarge = cDedup.Checked;
            j.DedupMinMB = (int)nDedup.Value;
            j.MirrorMaxCount = (int)nMirrorCount.Value;
            j.MirrorMaxPercent = (int)nMirrorPct.Value;
            int iv = (int)nInterval.Value * (cbUnit.SelectedIndex == 1 ? 60 : 1);
            j.IntervalMinutes = Math.Max(1, Math.Min(1440, iv));
            j.UseTimeWindow = cWindow.Checked;
            j.WindowStart = tStart.Text.Trim();
            j.WindowEnd = tEnd.Text.Trim();
            j.UseStartupDelay = cDelay.Checked;
            j.StartupDelayMinutes = (int)nDelay.Value;
            j.FollowRename = cFollow.Checked;
            j.NewerDestPolicy = rOver.Checked ? NewerPolicy.Overwrite : rSkip.Checked ? NewerPolicy.Skip : NewerPolicy.Ask;
            j.ToleranceSeconds = (int)nTol.Value;
            j.BackupBeforeOverwrite = cBackup.Checked;
            j.Parallel = (int)nPar.Value;
            j.BandwidthKBps = (int)nBw.Value;

            ExcludeRules x = new ExcludeRules();
            foreach (KeyValuePair<string, CheckBox> kv in presetBoxes) if (kv.Value.Checked) x.Presets.Add(kv.Key);
            x.Extensions = SplitList(tExt.Text);
            x.NamePatterns = SplitList(tPat.Text);
            x.MaxSizeBytes = cMax.Checked ? (long)nMax.Value * (cbMaxUnit.SelectedIndex == 1 ? 1024L * 1024 * 1024 : 1024L * 1024) : 0;
            x.ExcludeZeroByte = cZero.Checked;
            x.OlderThan = cOlder.Checked ? dOlder.Value.ToString("yyyy-MM-dd") : "";
            x.ExcludeHidden = cHidden.Checked;
            x.ExcludeSystem = cSystem.Checked;
            foreach (object o in lbFolders.Items) x.Folders.Add((string)o);
            x.Includes = SplitList(tInc.Text);
            j.Exclude = x;

            j.RetryCount = (int)nRetry.Value;
            j.RetryIntervalSeconds = (int)nRetryInt.Value;
            j.DupCheckEnabled = cDup.Checked;
            j.DupSchedule = rManual.Checked ? "manual" : rWeekly.Checked ? "weekly" : "daily";
            j.DupWeekday = cbWeekday.SelectedIndex;
            j.DupTime = tDupTime.Text.Trim();
            j.DupShowSameContent = cSame.Checked;
            j.DupShowOrphans = cOrphan.Checked;
            j.DupShowEmptyFolders = cEmpty.Checked;
            return j;
        }

        /// <summary>割り当てドライブ（Z: など）で入力されたら \\server\share に直して保存する（起動直後の未接続対策）</summary>
        private static string ToUncIfNetworkDrive(string p)
        {
            if (p.Length >= 2 && p[1] == ':' && !p.Contains("{"))
            {
                string u = NetAccess.ToUncIfMapped(p);
                return u;
            }
            return p;
        }

        private bool Collect()
        {
            JobConfig j = BuildSnapshot();
            if (j.Name.Length == 0) { Ui.Error(this, "ジョブ名を入力してください。"); return false; }
            foreach (JobConfig o in AppCore.Settings.Jobs)
                if (o.Id != j.Id && string.Equals(o.Name, j.Name, StringComparison.OrdinalIgnoreCase))
                { Ui.Error(this, "同じ名前のジョブがあります。別の名前にしてください。"); return false; }
            TimeSpan ts;
            if (j.UseTimeWindow && (!TimeUtil.TryParseHm(j.WindowStart, out ts) || !TimeUtil.TryParseHm(j.WindowEnd, out ts)))
            { Ui.Error(this, "時間帯は 08:00 の形で入力してください。"); return false; }
            if (j.DupCheckEnabled && j.DupSchedule != "manual" && !TimeUtil.TryParseHm(j.DupTime, out ts))
            { Ui.Error(this, "重複チェックの時刻は 12:00 の形で入力してください。"); return false; }
            string v = CopyEngine.Validate(j, AppCore.Settings, AppCore.Settings.Jobs);
            if (v != null) { Ui.Error(this, v); return false; }

            // 資格情報
            SaveServerEdits();
            try
            {
                foreach (KeyValuePair<string, string[]> kv in credEdits)
                {
                    if (!cSaveCred.Checked) { CredentialStore.Delete(kv.Key); continue; }
                    if (string.IsNullOrEmpty(kv.Value[0])) continue;
                    CredentialStore.Save(kv.Key, kv.Value[0], kv.Value[1]);
                }
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); return false; }

            if (!isNew && (!string.Equals(job.Source, j.Source, StringComparison.OrdinalIgnoreCase) || !string.Equals(job.Dest, j.Dest, StringComparison.OrdinalIgnoreCase)))
            {
                if (!Ui.Confirm(this, "コピー元またはコピー先を変えると、前回までの記録は使いません。次回はすべてのファイルをコピー先と照合します（同じファイルはコピーしません）。よろしいですか？"))
                    return false;
            }
            Result = j;
            return true;
        }

        private void RunTest()
        {
            JobConfig j = BuildSnapshot();
            string v = CopyEngine.Validate(j, AppCore.Settings, AppCore.Settings.Jobs);
            if (v != null) { Ui.Error(this, v); return; }
            SaveServerEdits();
            foreach (KeyValuePair<string, string[]> kv in credEdits)
                if (!string.IsNullOrEmpty(kv.Value[0])) { try { CredentialStore.Save(kv.Key, kv.Value[0], kv.Value[1]); } catch (Exception) { } }
            try
            {
                RunResult r = Ui.Busy<RunResult>(this, "ファイルを数えています（コピーはしません）…", delegate() { return AppCore.DryRun(j, false); });
                lastExcluded = r.ExcludedSamples;
                string msg = "コピー対象 " + Fmt.Count(r.Scanned) + "件・除外 " + Fmt.Count(r.Excluded) + "件（" + Fmt.Size(r.ExcludedBytes) + "）";
                lTest.Text = msg;
                string detail = msg + "\n\n次の実行で：\n・コピー " + Fmt.Count(r.PlannedCopyCount) + "件（" + Fmt.Size(r.CopyBytesPlanned) + "）\n・名前変更 " + r.Renamed + "件\n・上書き確認 " + r.Conflicts + "件";
                if (j.Mirror) detail += "\n・ミラー削除 " + r.Deleted + "件（" + Fmt.Size(r.DeleteBytes) + "）";
                Ui.Info(this, detail);
            }
            catch (Exception ex) { Ui.Error(this, "テストできませんでした：" + ex.Message); }
        }
    }

    /// <summary>1行入力</summary>
    public static class InputBox
    {
        public static string Show(IWin32Window owner, string title, string prompt, string value)
        {
            using (Form f = new Form())
            {
                Ui.SetupDialog(f, title, 420, 130);
                Label l = Ui.L(prompt);
                l.Location = new Point(Ui.S(12), Ui.S(12));
                TextBox t = Ui.T(value, 390);
                t.Location = new Point(Ui.S(14), Ui.S(40));
                Button ok = Ui.B("OK", null);
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(Ui.S(240), Ui.S(80));
                Button cancel = Ui.B("キャンセル", null);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(Ui.S(320), Ui.S(80));
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }
    }
}
