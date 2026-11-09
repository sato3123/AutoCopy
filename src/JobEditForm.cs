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
        private CheckBox cEnabled, cSub, cWindow, cDelay, cFollow, cBackup, cRunAtStartup;
        private RadioButton rSyncNoTake, rSyncTake, rSyncNoPush, rSyncPush, rInterval, rStartupOnly;
        private RadioButton rDedupOff, rDedupAsk, rDedupOn;
        private CheckBox cAlwaysRename;
        private Label lSyncNote, lDedupNote;
        private RadioButton rDist, rLocal, rAppend, rMirror, rSync, rAsk, rSkip, rOver;
        private NumericUpDown nSyncCache;
        private ComboBox cbDupMin, cbSyncPreset;
        private CheckBox cAdvanced, cParAuto, cBwLimit, cSyncCache;
        private FlowLayoutPanel syncChoices;
        private Label lExclCount;
        private readonly List<Control> advanced = new List<Control>();   // 「詳しい設定」のときだけ見せるもの
        public readonly ReasonTips Tips = new ReasonTips();
        private ExclusionCounter counter;
        private NumericUpDown nInterval, nDelay, nTol, nPar, nBw, nMirrorCount, nMirrorPct, nDedup;
        private ComboBox cbUnit;
        private Label lPreview;
        // 除外
        private readonly Dictionary<string, CheckBox> presetBoxes = new Dictionary<string, CheckBox>();
        private TextBox tExt, tPat, tInc, tOnlyFolders, tOnlyExt;
        private CheckBox cMax, cZero, cOlder, cHidden, cSystem;
        private NumericUpDown nMax;
        private ComboBox cbMaxUnit;
        private DateTimePicker dOlder;
        private ListBox lbFolders;
        // 接続・重複
        private ComboBox cbServer;
        private TextBox tUser, tPass, tDupTime;
        private CheckBox cSaveCred, cDup, cSame, cOrphan, cEmpty;
        private Label lServerNote, lConn;
        private NumericUpDown nRetry, nRetryInt;
        private RadioButton rStartupDup, rDaily, rWeekly, rMonthly, rYearly, rManual;
        private ComboBox cbWeekday, cbDupDay, cbDupMonth;
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

            Button ok = Btn.MakePrimary(Ui.B("設定を保存", delegate { if (Collect()) { DialogResult = DialogResult.OK; Close(); } }));
            ok.MinimumSize = new Size(Ui.S(88), Ui.S(28));
            Button cancel = Ui.B("キャンセル", delegate { DialogResult = DialogResult.Cancel; Close(); });
            Button test = Ui.B("テスト（コピーせずに結果を確認）", delegate { RunTest(); });
            FlowLayoutPanel foot = Ui.Footer(cancel, ok);
            test.Margin = new Padding(3, 3, Ui.S(60), 3);
            foot.Controls.Add(test);
            foot.Controls.Add(Ui.HelpButton(Help.Jobs));
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            // 上の帯：詳しい設定の表示・はじめての設定の順番
            cAdvanced = Ui.C("詳しい設定も表示する（時間帯・同時にコピーする数・上書きのルール・除外の細かい条件 など）", false);
            cAdvanced.CheckedChanged += delegate { ApplyAdvanced(); if (!loading) { AppCore.Settings.ShowAdvancedJobSettings = cAdvanced.Checked; } };
            FlowLayoutPanel topBar = Ui.Column(cAdvanced);
            if (isNew)
            {
                Label nl = Ui.L("はじめての設定：「基本」タブの ①コピー元 → ②コピー先 → ③コピーの方式 → ④実行のタイミング を決め、下の「テスト」で結果を確かめてから「設定を保存」を押してください。"
                    + "新しいジョブは「無効」で作られ、保存した後に有効にするかを尋ねます。");
                Ui.FitToForm(nl, 760);
                Panel nb = Ui.Banner(Ui.InfoBack, nl);
                topBar.Controls.Add(nb);
            }
            topBar.Dock = DockStyle.Top;
            topBar.Padding = new Padding(Ui.S(8), Ui.S(4), Ui.S(8), 0);
            Controls.Add(topBar);
            Controls.Add(foot);
            LoadValues();
            loading = false;
            WireEnabled();
            SetReasons();
            UpdateEnabled();
            UpdatePreview();
            ApplyAdvanced();
            counter = new ExclusionCounter(this, lExclCount, delegate { return BuildSnapshot(); });
            FormClosed += delegate { counter.Dispose(); };
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedIndex == 1) counter.Request(); };
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
            b.Controls.Add(Ui.Row(Ui.L("① コピー元フォルダ", 130), tSrc, Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, tSrc.Text, "コピー元フォルダを選んでください"); if (f != null) tSrc.Text = f; })));
            cSub = Ui.C("サブフォルダも含める", true);
            b.Controls.Add(Ui.Row(Ui.L("", 130), cSub));

            tDst = Ui.T("", 440);
            tDst.TextChanged += delegate { UpdatePreview(); };
            b.Controls.Add(Ui.Row(Ui.L("② コピー先フォルダ", 130), tDst, Ui.B("参照…", delegate { string f = Ui.BrowseFolder(this, PathUtil.Expand(tDst.Text, AppCore.Settings.FolderName), "コピー先フォルダを選んでください"); if (f != null) tDst.Text = f; })));
            lPreview = Ui.Hint("");
            Ui.FitToForm(lPreview, 600);
            Button ins = Ui.B("{フォルダ名} を末尾に入れる", delegate
            {
                string t = tDst.Text.TrimEnd('\\');
                if (!PathUtil.HasFolderVariable(t)) tDst.Text = t + "\\{フォルダ名}";
            });
            b.Controls.Add(Ui.Row(Ui.L("", 130), Ui.Column(lPreview,
                Ui.Hint("使える変数：{フォルダ名}（各PCの使用者が決める名前）　%COMPUTERNAME%　%USERNAME%　%DOCUMENTS%"),
                Ui.Hint("PC内のフォルダ・外付けドライブ・ネットワークドライブのどれでも指定できます。"), ins)));

            // 3つの方式は同じ入れ物に入れる（ラジオボタンは同じ入れ物の中でだけ1つを選ぶため）
            rAppend = Ui.R("追加・更新のみ（コピー先から削除しない）", true);
            rMirror = Ui.R("ミラー（コピー元で削除したらコピー先も削除）", false);
            rMirror.ForeColor = Ui.Warn;
            rMirror.CheckedChanged += OnMirrorChanged;
            nMirrorCount = Ui.N(1, 1000000, 100, 80);
            nMirrorPct = Ui.N(1, 100, 10, 56);
            FlowLayoutPanel mirrorLimit = Ui.Row(Ui.L("　　"), Ui.Hint("一度に"), nMirrorCount, Ui.Hint("件、またはコピー先の"), nMirrorPct, Ui.Hint("% を超える削除は確認する"));
            rSync = Ui.R("複数PCで同期（例外）", false);
            rSync.ForeColor = Ui.Warn;
            rSync.CheckedChanged += OnSyncChanged;
            lSyncNote = Ui.Wrap("コピー先を共有フォルダにして、複数のPCで同期します。他のPCが更新したファイルは、このPCのコピー元フォルダにも取り込みます。削除は「削除の確認」で選んでから伝えます。両方で変えたファイルは「上書きの確認」で選びます。取り込みで置き換えたこのPCの古い版は控えに残ります。", 580);
            lSyncNote.ForeColor = Ui.Quiet;
            lSyncNote.Margin = new Padding(Ui.S(22), 0, 3, 4);
            // 同期：共有フォルダにだけある（このPCにない）ファイルの扱い（同じ入れ物の中で1つを選ぶ）
            rSyncTake = Ui.R("このPCに取り込む（初期値。他のPCが追加したファイルも入ってくる）", true);
            rSyncNoTake = Ui.R("このPCにはコピーしない", false);
            FlowLayoutPanel syncNew = Ui.Column(Ui.Hint("共有フォルダにだけあって、このPCにないファイル："), rSyncTake, rSyncNoTake);
            syncNew.Margin = new Padding(Ui.S(22), 0, 3, 4);
            // このPCにだけある（共有フォルダにない）ファイルの扱い（別の入れ物＝別の選択）
            rSyncNoPush = Ui.R("共有フォルダにはコピーしない（初期値）", true);
            rSyncPush = Ui.R("共有フォルダへコピーする（このPCで追加したファイルも他のPCへ）", false);
            FlowLayoutPanel syncPush = Ui.Column(Ui.Hint("このPCにだけあって、共有フォルダにないファイル："), rSyncNoPush, rSyncPush);
            syncPush.Margin = new Padding(Ui.S(22), 0, 3, 4);
            // 共有フォルダの詳しい確認を省略する時間（複数PCで同期するときの待ち時間を減らす）
            // よく使う組み合わせ（選ぶと下の2つが決まる。下を自分で変えると「自分で選ぶ」になる）
            cbSyncPreset = Ui.Combo(360, SyncPresets.Labels);
            cbSyncPreset.SelectedIndexChanged += delegate
            {
                if (loading || cbSyncPreset.SelectedIndex < 0) return;
                bool take, push;
                if (SyncPresets.Values(cbSyncPreset.SelectedIndex, out take, out push))
                {
                    rSyncTake.Checked = take; rSyncNoTake.Checked = !take;
                    rSyncPush.Checked = push; rSyncNoPush.Checked = !push;
                }
                ApplyAdvanced();
            };
            EventHandler syncRadio = delegate { if (!loading) SelectSyncPreset(); };
            foreach (RadioButton r in new RadioButton[] { rSyncTake, rSyncNoTake, rSyncPush, rSyncNoPush }) r.CheckedChanged += syncRadio;
            FlowLayoutPanel syncPresetRow = Ui.Row(Ui.Hint("同期のしかた："), cbSyncPreset);
            syncPresetRow.Margin = new Padding(Ui.S(22), 0, 3, 4);
            syncChoices = Ui.Column(syncNew, syncPush);
            syncChoices.Margin = new Padding(0);
            cSyncCache = Ui.C("共有フォルダの詳しい確認を省略する（前回から", true);
            nSyncCache = Ui.N(1, 1440, 30, 72);
            Label lCacheNote = Ui.Wrap("共有フォルダの中を全部調べるのは時間がかかるため、前回の詳しい確認からこの時間以内で、どのPCも自動コピーで共有フォルダを変えていなければ、前回の確認の結果を使います。チェックを外すと毎回詳しく確認します。自動コピー以外の方法で共有フォルダを直接変えた場合は、この時間がたってから反映されます。", 540);
            lCacheNote.ForeColor = Ui.Quiet;
            FlowLayoutPanel syncCache = Ui.Column(Ui.Row(cSyncCache, nSyncCache, Ui.Hint("分以内）")), lCacheNote);
            syncCache.Margin = new Padding(Ui.S(22), 0, 3, 4);
            advanced.Add(syncCache);
            FlowLayoutPanel modes = Ui.Column(rAppend, rMirror, mirrorLimit, rSync, lSyncNote, syncPresetRow, syncChoices, syncCache);
            b.Controls.Add(Ui.Row(Ui.L("③ コピーの方式", 130), modes));

            // 実行のタイミング：一定の間隔で（＋ログオン時）／ログオン時のみ
            nInterval = Ui.N(1, 1440, 30, 72);
            cbUnit = Ui.Combo(80, "分", "時間");
            rInterval = Ui.R("一定の間隔で", true);
            rStartupOnly = Ui.R("ログオン時（アプリの起動時）のみ", false);
            // 別々の入れ物に入るため、片方を選んだらもう片方を外す
            rInterval.CheckedChanged += delegate { if (rInterval.Checked) rStartupOnly.Checked = false; };
            rStartupOnly.CheckedChanged += delegate { if (rStartupOnly.Checked) rInterval.Checked = false; };
            cRunAtStartup = Ui.C("ログオン時（アプリの起動時）にも実行する", true);
            cRunAtStartup.Margin = new Padding(Ui.S(22), 3, 3, 3);
            Label lStartupNote = Ui.Wrap("チェックしない場合も、PCが止まっている間に予定の時刻（前回＋実行間隔）が過ぎていれば、起動後に実行します。", 540);
            lStartupNote.ForeColor = Ui.Quiet;
            lStartupNote.Margin = new Padding(Ui.S(40), 0, 3, 4);
            Label lOnlyNote = Ui.Wrap("Windows にサインインしてアプリが起動したときに1回だけ実行します（下の「起動後の初回」の待ち時間の後）。その後は、次にログオンするまで自動では実行しません（手動の実行・強制データ送信はいつでもできます）。", 540);
            lOnlyNote.ForeColor = Ui.Quiet;
            lOnlyNote.Margin = new Padding(Ui.S(22), 0, 3, 4);
            b.Controls.Add(Ui.Row(Ui.L("④ 実行のタイミング", 130), Ui.Column(
                Ui.Row(rInterval, nInterval, cbUnit, Ui.Hint("ごと（1分〜24時間）")),
                cRunAtStartup, lStartupNote, rStartupOnly, lOnlyNote)));

            cWindow = Ui.C("時間帯を限定する", false);
            tStart = Ui.T("08:00", 60);
            tEnd = Ui.T("20:00", 60);
            Adv(b, Ui.Row(Ui.L("実行する時間帯", 130), cWindow, tStart, Ui.L("〜"), tEnd));

            cDelay = Ui.C("PC起動後", true);
            nDelay = Ui.N(0, 120, 1, 56);
            Adv(b, Ui.Row(Ui.L("起動後の初回", 130), cDelay, nDelay, Ui.L("分待ってから実行する")));


            cFollow = Ui.C("名前やフォルダ名を変えたら、コピー先も同じ名前に変える（再コピーしない）", true);
            rAsk = Ui.R("毎回確認する（推奨）", true);
            rSkip = Ui.R("上書きしない", false);
            rOver = Ui.R("常に上書き", false);
            nTol = Ui.N(0, 3600, 2, 56);
            cBackup = Ui.C("上書きする前に、コピー先の古いファイルを「_AutoCopy\\退避」に残す", false);
            Adv(b, Ui.Section("名前変更と上書きのルール",
                cFollow,
                Ui.Row(Ui.L("コピー先が古いとき", 160), Ui.L("自動で上書きする")),
                Ui.Row(Ui.L("コピー先が新しいとき", 160), Ui.Row(rAsk, rSkip, rOver)),
                Ui.Row(Ui.L("同じとみなす日時の差", 160), nTol, Ui.L("秒以内")),
                cBackup));

            rDedupOff = Ui.R("使わない（重複していてもすべてコピーする）", false);
            rDedupAsk = Ui.R("見つけたら毎回選ぶ（初期値）：選ぶまで2つ目以降はコピーせずに待ち、お知らせする", true);
            rDedupOn = Ui.R("自動でテキストにする：2つ目以降はコピーせず、1つ目の保管場所を書いたテキストを置く", false);
            rDedupOn.CheckedChanged += delegate { UpdateEnabled(); };
            nDedup = Ui.N(1, 1000000, 10, 72);
            lDedupNote = Ui.Wrap("「テキスト」は「〇〇" + AppPaths.PlaceholderSuffix + "」で、1つ目の保管場所を書きます。中身はファイル全体で比べます（同じ大きさのものだけ読みます）。毎回選ぶ場合は、コピー先の整理（重複チェック）の画面で「テキストにする」か「普通にコピー」を選びます。普通にコピー済みの重複も、その画面でテキストにできます。", 600);
            lDedupNote.ForeColor = Ui.Quiet;
            Adv(b, Ui.Section("大きいファイルの重複をテキストに置き換える（コピー元の中で中身が同じ大きいファイル・容量の節約）",
                Ui.Column(rDedupOff, rDedupAsk, rDedupOn),
                Ui.Row(Ui.L("　　対象の大きさ", 160), nDedup, Ui.L("MB 以上")),
                lDedupNote));

            cParAuto = Ui.C("自動（おすすめ：ネットワークは 2、PC内は 4）", true);
            nPar = Ui.N(1, 8, 2, 56);
            Adv(b, Ui.Row(Ui.L("同時にコピーする数", 130), cParAuto, nPar, Ui.Hint("個")));
            cBwLimit = Ui.C("転送速度を制限する", false);
            nBw = Ui.N(1, 1000000, 1000, 90);
            Adv(b, Ui.Row(Ui.L("転送速度の上限", 130), cBwLimit, nBw, Ui.Hint("KB/秒（1000 KB/秒 ≒ 1 MB/秒）")));
            p.Controls.Add(b);
            return p;
        }

        /// <summary>「詳しい設定」のときだけ見せる部品として加える</summary>
        private Control Adv(FlowLayoutPanel b, Control c)
        {
            b.Controls.Add(c);
            advanced.Add(c);
            return c;
        }

        /// <summary>詳しい設定の表示・非表示（隠しても設定の内容はそのまま使う）</summary>
        private void ApplyAdvanced()
        {
            if (cAdvanced == null) return;
            bool adv = cAdvanced.Checked;
            SuspendLayout();
            foreach (Control c in advanced) c.Visible = adv;
            // 同期の細かい選択：詳しい設定のとき、または「自分で選ぶ」のときに見せる
            if (syncChoices != null) syncChoices.Visible = adv || cbSyncPreset.SelectedIndex == SyncPresets.Custom;
            ResumeLayout(true);
        }

        /// <summary>このジョブに、初期値と違う「詳しい設定」があるか（あれば最初から見せる）</summary>
        public static bool HasAdvancedValues(JobConfig j)
        {
            JobConfig d = new JobConfig();
            ExcludeRules x = j.Exclude ?? ExcludeRules.CreateDefault();
            return j.UseTimeWindow || !j.UseStartupDelay || j.StartupDelayMinutes != d.StartupDelayMinutes || j.Parallel > 0 || j.BandwidthKBps > 0
                || j.NewerDestPolicy != NewerPolicy.Ask || j.BackupBeforeOverwrite || !j.FollowRename || j.ToleranceSeconds != d.ToleranceSeconds
                || j.Dedup != d.Dedup || j.DedupMinMB != d.DedupMinMB
                || j.RetryCount != d.RetryCount || j.RetryIntervalSeconds != d.RetryIntervalSeconds
                || (j.Sync && j.SyncRemoteCacheMinutes != d.SyncRemoteCacheMinutes)
                || x.MaxSizeBytes > 0 || x.ExcludeZeroByte || !string.IsNullOrEmpty(x.OlderThan) || !x.ExcludeHidden || !x.ExcludeSystem
                || (x.Includes != null && x.Includes.Count > 0) || (x.OnlyFolders != null && x.OnlyFolders.Count > 0) || (x.OnlyExtensions != null && x.OnlyExtensions.Count > 0)
                || j.DupShowOrphans != d.DupShowOrphans || j.DupShowSameContent != d.DupShowSameContent || j.DupShowEmptyFolders != d.DupShowEmptyFolders || j.DupMinSizeKB > 0;
        }

        /// <summary>同期の2つの選択に合う「同期のしかた」を選ぶ（合うものがなければ「自分で選ぶ」）</summary>
        private void SelectSyncPreset()
        {
            int k = SyncPresets.Find(rSyncTake.Checked, rSyncPush.Checked);
            bool keep = loading;
            loading = true;
            cbSyncPreset.SelectedIndex = k;
            loading = keep;
            ApplyAdvanced();
        }

        /// <summary>灰色（操作できない）の項目に、マウスを重ねたときに出す理由</summary>
        private void SetReasons()
        {
            Func<string> notSync = delegate { return "「③ コピーの方式」で「複数PCで同期」を選んだときだけ使います"; };
            Func<string> dupOff = delegate { return rSync.Checked ? "「複数PCで同期」では、コピー先の整理（重複チェック）は行いません" : "「コピー先の整理（重複チェック）を行う」にチェックを入れると使えます"; };
            Tips.Set(nMirrorCount, "「ミラー」を選んだときだけ使います");
            Tips.Set(nMirrorPct, "「ミラー」を選んだときだけ使います");
            foreach (Control c in new Control[] { rSyncTake, rSyncNoTake, rSyncPush, rSyncNoPush, cbSyncPreset, cSyncCache }) Tips.Set(c, notSync);
            Tips.Set(nSyncCache, delegate { return !rSync.Checked ? notSync() : "「共有フォルダの詳しい確認を省略する」にチェックを入れると使えます"; });
            Tips.Set(tStart, "「時間帯を限定する」にチェックを入れると使えます");
            Tips.Set(tEnd, "「時間帯を限定する」にチェックを入れると使えます");
            Tips.Set(nDelay, "「PC起動後」にチェックを入れると使えます");
            foreach (Control c in new Control[] { nInterval, cbUnit, cRunAtStartup }) Tips.Set(c, "「ログオン時（アプリの起動時）のみ」を選んでいるため使いません");
            Tips.Set(cBackup, "「コピー先が新しいとき」で「上書きしない」を選んでいるため使いません");
            foreach (Control c in new Control[] { rDedupOff, rDedupAsk, rDedupOn }) Tips.Set(c, "「複数PCで同期」では使えません（他のPCのファイルがあるため）");
            Tips.Set(nDedup, delegate { return rSync.Checked ? "「複数PCで同期」では使えません" : "「使わない」を選んでいるため使いません"; });
            Tips.Set(nPar, "「自動」のチェックを外すと、数を決められます");
            Tips.Set(nBw, "「転送速度を制限する」にチェックを入れると使えます");
            Tips.Set(nMax, "左のチェックを入れると、大きさで除外できます");
            Tips.Set(cbMaxUnit, "左のチェックを入れると、大きさで除外できます");
            Tips.Set(dOlder, "左のチェックを入れると、日付で除外できます");
            Tips.Set(cDup, "「複数PCで同期」では、コピー先の整理（重複チェック）は行いません");
            foreach (Control c in new Control[] { rStartupDup, rDaily, rWeekly, rMonthly, rYearly, rManual, cEmpty, cSame, cOrphan, cbDupMin }) Tips.Set(c, dupOff);
            Tips.Set(cbWeekday, delegate { return !(cDup.Checked && !rSync.Checked) ? dupOff() : "「毎週」を選んだときだけ使います"; });
            Tips.Set(cbDupDay, delegate { return !(cDup.Checked && !rSync.Checked) ? dupOff() : "「毎月」「毎年」を選んだときだけ使います"; });
            Tips.Set(cbDupMonth, delegate { return !(cDup.Checked && !rSync.Checked) ? dupOff() : "「毎年」を選んだときだけ使います"; });
            Tips.Set(tDupTime, delegate { return !(cDup.Checked && !rSync.Checked) ? dupOff() : "「ログオン時」「手動のみ」では時刻を使いません"; });
            Tips.Set(cAlwaysRename, "いつも探すため、外せません（コピー先に古い名前のファイルが残らないようにするため）");
            foreach (Control c in new Control[] { cbServer, cSaveCred, tUser, tPass }) Tips.Set(c, "コピー元・コピー先がネットワークのフォルダではないため、入力は不要です");
            Tips.Set(nRetryInt, "再試行の回数が 0 のため使いません");
        }

        /// <summary>確認用：今、灰色なのに理由が出ない入力欄（あれば名前）</summary>
        public List<string> MissingReasons()
        {
            List<string> bad = new List<string>();
            Stack<Control> st = new Stack<Control>();
            st.Push(this);
            while (st.Count > 0)
            {
                Control c = st.Pop();
                if (!(c is NumericUpDown || c is ComboBox || c is DateTimePicker)) foreach (Control ch in c.Controls) st.Push(ch);
                bool input = c is CheckBox || c is RadioButton || c is NumericUpDown || c is TextBox || c is ComboBox || c is DateTimePicker || c is Button || c is ListBox;
                if (!input || c.Enabled || c.Parent == null || !c.Parent.Enabled) continue;
                string r = Tips.ReasonFor(c);
                if (string.IsNullOrEmpty(r)) bad.Add(c.GetType().Name + "「" + c.Text + "」");
            }
            return bad;
        }

        private void OnSyncChanged(object sender, EventArgs e)
        {
            UpdateSyncControls();
            if (loading || !rSync.Checked) return;
            string msg = "「複数PCで同期」は例外的な使い方です。\n\n"
                + "・コピー先は、同期するすべてのPCから見える共有フォルダにしてください（{フォルダ名} は使えません）。\n"
                + "・他のPCが入れた・更新したファイルが、このPCのコピー元フォルダに入ってきます。\n"
                + "・削除は「削除の確認」で選んでから伝えます。両方で変えたファイルは確認画面で選びます。\n"
                + "・共有フォルダにだけあるファイルは、初期値ではこのPCに取り込みます。このPCにだけあるファイルは、初期値では共有フォルダにコピーしません（どちらも下で選べます）。\n"
                + "・このPCの古い版は、置き換える前に控え（" + AppPaths.SyncBackupDir + "）に残します。\n"
                + "・コピー先の整理（重複チェック）と、大きいファイルの重複のテキストへの置き換えは、同期では行いません。\n\n同期にしますか？";
            if (!Ui.Confirm(this, msg))
            {
                loading = true;
                rAppend.Checked = true;
                loading = false;
                UpdateSyncControls();
            }
        }

        private void UpdateSyncControls() { UpdateEnabled(); }

        /// <summary>上位の設定が変わったら、下位の項目の操作できる・できないを直す</summary>
        private void WireEnabled()
        {
            EventHandler h = delegate { UpdateEnabled(); };
            foreach (CheckBox c in new CheckBox[] { cWindow, cDelay, cMax, cOlder, cDup, cParAuto, cBwLimit, cSyncCache }) c.CheckedChanged += h;
            foreach (RadioButton r in new RadioButton[] { rInterval, rStartupOnly, rAppend, rMirror, rSync, rAsk, rSkip, rOver, rStartupDup, rDaily, rWeekly, rMonthly, rYearly, rManual, rDedupOff, rDedupAsk, rDedupOn }) r.CheckedChanged += h;
            nRetry.ValueChanged += h;
        }

        /// <summary>
        /// 上の設定で決まる「変えられない項目」を操作できないようにする（設定を変えるたびに呼ぶ）。
        /// </summary>
        public void UpdateEnabled()
        {
            if (rDedupOn == null || cDup == null || cMax == null) return;   // 画面を作っている途中
            bool sync = rSync.Checked, mirror = rMirror.Checked;
            // コピーの方式
            nMirrorCount.Enabled = nMirrorPct.Enabled = mirror;
            lSyncNote.Enabled = sync;
            rSyncNoTake.Enabled = rSyncTake.Enabled = sync;
            rSyncNoPush.Enabled = rSyncPush.Enabled = sync;
            cbSyncPreset.Enabled = cSyncCache.Enabled = sync;
            nSyncCache.Enabled = sync && cSyncCache.Checked;
            nPar.Enabled = !cParAuto.Checked;
            nBw.Enabled = cBwLimit.Checked;
            // 実行する時間帯・起動後の待ち
            tStart.Enabled = tEnd.Enabled = cWindow.Checked;
            nDelay.Enabled = cDelay.Checked;
            // 実行のタイミング：ログオン時のみなら、間隔と「ログオン時にも」は使わない
            nInterval.Enabled = cbUnit.Enabled = cRunAtStartup.Enabled = rInterval.Checked;
            // 上書きのルール：常に上書き・確認のときだけ「退避」に意味がある（上書きしないなら不要）
            cBackup.Enabled = !rSkip.Checked;
            // 大きいファイルの重複：同期では使えない。使うときだけ大きさを決められる
            rDedupOn.Enabled = rDedupAsk.Enabled = rDedupOff.Enabled = !sync;
            nDedup.Enabled = !rDedupOff.Checked && !sync;
            lDedupNote.Enabled = nDedup.Enabled;
            // 除外ルール
            nMax.Enabled = cbMaxUnit.Enabled = cMax.Checked;
            dOlder.Enabled = cOlder.Checked;
            // 重複チェック：同期では行わない。行うときだけ日時と探すものを決められる
            cDup.Enabled = !sync;
            bool dup = cDup.Checked && !sync;
            rStartupDup.Enabled = rDaily.Enabled = rWeekly.Enabled = rMonthly.Enabled = rYearly.Enabled = rManual.Enabled = dup;
            cbWeekday.Enabled = dup && rWeekly.Checked;
            cbDupDay.Enabled = dup && (rMonthly.Checked || rYearly.Checked);
            cbDupMonth.Enabled = dup && rYearly.Checked;
            tDupTime.Enabled = dup && !rManual.Checked && !rStartupDup.Checked;
            cEmpty.Enabled = cSame.Enabled = cOrphan.Enabled = cbDupMin.Enabled = dup;
            cAlwaysRename.Enabled = false;   // 常に探す（変えられない）
            // ネットワークの接続：ネットワークのフォルダがないとき、保存しないときは入力できない
            bool net = cbServer.Items.Count > 0;
            cbServer.Enabled = net;
            cSaveCred.Enabled = net;
            tUser.Enabled = tPass.Enabled = net;
            nRetryInt.Enabled = nRetry.Value > 0;
        }

        /// <summary>
        /// 確認用：上の設定を切り替えたとき、下の項目が操作できる・できないが正しく変わるか（問題があれば説明を返す）
        /// </summary>
        /// <summary>SelfTestEnabled で確かめる項目の数（画面の確認の報告用）</summary>
        public static int SelfTestCount;

        public List<string> SelfTestEnabled()
        {
            SelfTestCount = 0;
            List<string> bad = new List<string>();
            bool saveLoading = loading;
            loading = true;   // 確認画面（ミラーの注意など）を出さない
            try
            {
                Action<bool, string> expect = delegate(bool ok, string what) { SelfTestCount++; if (!ok) bad.Add(what); };
                rAppend.Checked = true; UpdateEnabled();
                expect(!nMirrorCount.Enabled && !nMirrorPct.Enabled, "追加・更新のときミラーの件数が変えられる");
                rMirror.Checked = true; UpdateEnabled();
                expect(nMirrorCount.Enabled && nMirrorPct.Enabled, "ミラーのとき件数を変えられない");
                expect(!rAppend.Checked && !rSync.Checked, "ミラーを選んでも他の方式の選択が外れない");
                rSync.Checked = true; UpdateEnabled();
                expect(!rMirror.Checked && !rAppend.Checked, "同期を選んでも他の方式の選択が外れない");
                expect(!rDedupOn.Enabled && !nDedup.Enabled, "同期のとき重複の省略を選べる");
                expect(!cDup.Enabled && !cbWeekday.Enabled && !tDupTime.Enabled, "同期のとき重複チェックを変えられる");
                expect(rSyncTake.Enabled && rSyncNoTake.Enabled, "同期なのに共有フォルダにだけあるファイルの扱いを選べない");
                expect(rSyncPush.Enabled && rSyncNoPush.Enabled, "同期なのにこのPCにだけあるファイルの扱いを選べない");
                cSyncCache.Checked = false; UpdateEnabled();
                expect(cSyncCache.Enabled && !nSyncCache.Enabled, "省略しないのに省略する時間を変えられる");
                cSyncCache.Checked = true; UpdateEnabled();
                expect(nSyncCache.Enabled, "省略するのに時間を変えられない");
                // 同期のしかた（よく使う組み合わせ）を選ぶと、2つの選択が決まる。自分で変えると「自分で選ぶ」になる
                loading = false;
                for (int k = 0; k < SyncPresets.Custom; k++)
                {
                    cbSyncPreset.SelectedIndex = k;
                    bool tk, ps; SyncPresets.Values(k, out tk, out ps);
                    expect(rSyncTake.Checked == tk && rSyncPush.Checked == ps, "同期のしかた「" + SyncPresets.Labels[k] + "」を選んでも、取り込む・送るの選択が合わない");
                }
                cbSyncPreset.SelectedIndex = 0;
                bool push0 = rSyncPush.Checked;
                if (push0) rSyncNoPush.Checked = true; else rSyncPush.Checked = true;
                expect(cbSyncPreset.SelectedIndex == SyncPresets.Find(rSyncTake.Checked, rSyncPush.Checked), "取り込む・送るを変えても、同期のしかたが合わない");
                loading = true;
                cbSyncPreset.SelectedIndex = 0; rSyncTake.Checked = true; rSyncNoPush.Checked = true;
                bool takeBefore = rSyncTake.Checked;
                rSyncPush.Checked = true;
                expect(rSyncTake.Checked == takeBefore && rSyncNoTake.Checked != takeBefore && rSync.Checked, "共有へコピーするを選ぶと、取り込みや方式の選択が変わる（同じ入れ物に入っている）");
                rSyncNoPush.Checked = true;
                rSyncTake.Checked = true;
                expect(rSync.Checked, "取り込むを選ぶと方式の選択が外れる（同じ入れ物に入っている）");
                rSyncNoTake.Checked = true;
                rAppend.Checked = true; UpdateEnabled();
                expect(!rSync.Checked && !rMirror.Checked, "追加・更新を選んでも他の方式の選択が外れない");
                expect(!rSyncTake.Enabled && !rSyncNoTake.Enabled && !rSyncPush.Enabled && !rSyncNoPush.Enabled, "同期でないのに、共有フォルダ／このPCにだけあるファイルの扱いを選べる");
                expect(!nSyncCache.Enabled && !cSyncCache.Enabled && !cbSyncPreset.Enabled, "同期でないのに、共有フォルダの確認を省略する時間・同期のしかたを変えられる");
                cParAuto.Checked = true; UpdateEnabled();
                expect(!nPar.Enabled, "同時にコピーする数が自動なのに数を変えられる");
                cParAuto.Checked = false; UpdateEnabled();
                expect(nPar.Enabled, "自動でないのに数を変えられない");
                cBwLimit.Checked = false; UpdateEnabled();
                expect(!nBw.Enabled, "転送速度を制限しないのに上限を変えられる");
                cBwLimit.Checked = true; UpdateEnabled();
                expect(nBw.Enabled, "転送速度を制限するのに上限を変えられない");
                rDedupOff.Checked = true; UpdateEnabled();
                expect(rDedupOn.Enabled && !nDedup.Enabled, "重複の省略を使わないのに大きさを変えられる");
                expect(!rDedupAsk.Checked && !rDedupOn.Checked, "使わないを選んでも他の選択が外れない");
                rDedupAsk.Checked = true; UpdateEnabled();
                expect(nDedup.Enabled && !rDedupOff.Checked, "毎回選ぶのに大きさを変えられない");
                rDedupOn.Checked = true; UpdateEnabled();
                expect(nDedup.Enabled && !rDedupAsk.Checked, "自動にしたのに大きさを変えられない");
                cWindow.Checked = false; UpdateEnabled();
                expect(!tStart.Enabled && !tEnd.Enabled, "時間帯を限定しないのに時刻を変えられる");
                cWindow.Checked = true; UpdateEnabled();
                expect(tStart.Enabled && tEnd.Enabled, "時間帯を限定するのに時刻を変えられない");
                rStartupOnly.Checked = true; UpdateEnabled();
                expect(!rInterval.Checked && !nInterval.Enabled && !cbUnit.Enabled && !cRunAtStartup.Enabled, "ログオン時のみなのに間隔を変えられる（または一定の間隔の選択が外れない）");
                rInterval.Checked = true; UpdateEnabled();
                expect(!rStartupOnly.Checked && nInterval.Enabled && cRunAtStartup.Enabled, "一定の間隔なのに間隔を変えられない（またはログオン時のみの選択が外れない）");
                cDelay.Checked = false; UpdateEnabled();
                expect(!nDelay.Enabled, "起動後に待たないのに分を変えられる");
                rSkip.Checked = true; UpdateEnabled();
                expect(!cBackup.Enabled, "上書きしないのに退避を選べる");
                rAsk.Checked = true; UpdateEnabled();
                expect(cBackup.Enabled, "確認するのに退避を選べない");
                cMax.Checked = false; UpdateEnabled();
                expect(!nMax.Enabled && !cbMaxUnit.Enabled, "サイズで除外しないのに大きさを変えられる");
                cOlder.Checked = false; UpdateEnabled();
                expect(!dOlder.Enabled, "日付で除外しないのに日付を変えられる");
                cDup.Checked = false; UpdateEnabled();
                expect(!rDaily.Enabled && !tDupTime.Enabled && !cEmpty.Enabled && !cSame.Enabled && !cOrphan.Enabled && !cbDupMin.Enabled, "重複チェックをしないのに日時・探すものを変えられる");
                cDup.Checked = true; rManual.Checked = true; UpdateEnabled();
                expect(!tDupTime.Enabled && !cbWeekday.Enabled && !cbDupDay.Enabled && !cbDupMonth.Enabled, "手動のみなのに時刻・曜日・日・月を変えられる");
                rStartupDup.Checked = true; UpdateEnabled();
                expect(!tDupTime.Enabled && !cbWeekday.Enabled && !cbDupDay.Enabled && !cbDupMonth.Enabled, "ログオン時なのに時刻・曜日・日・月を変えられる");
                rDaily.Checked = true; UpdateEnabled();
                expect(tDupTime.Enabled && !cbWeekday.Enabled && !cbDupDay.Enabled, "毎日なのに曜日・日を変えられる（または時刻を変えられない）");
                rWeekly.Checked = true; UpdateEnabled();
                expect(tDupTime.Enabled && cbWeekday.Enabled && !cbDupDay.Enabled, "毎週なのに曜日・時刻を変えられない（または日を変えられる）");
                rMonthly.Checked = true; UpdateEnabled();
                expect(tDupTime.Enabled && cbDupDay.Enabled && !cbDupMonth.Enabled && !cbWeekday.Enabled, "毎月なのに日・時刻を変えられない（または月・曜日を変えられる）");
                rYearly.Checked = true; UpdateEnabled();
                expect(tDupTime.Enabled && cbDupDay.Enabled && cbDupMonth.Enabled, "毎年なのに月・日・時刻を変えられない");
                expect(!cAlwaysRename.Enabled, "常に探す項目のチェックを外せる");
                nRetry.Value = 0; UpdateEnabled();
                expect(!nRetryInt.Enabled, "再試行しないのに間隔を変えられる");
                // 灰色の項目には、どれも理由が出る（いろいろな組み合わせで確かめる）
                foreach (Action set in new Action[] {
                    delegate { rSync.Checked = true; cSyncCache.Checked = false; },
                    delegate { rAppend.Checked = true; cDup.Checked = false; rDedupOff.Checked = true; cMax.Checked = false; cOlder.Checked = false; cWindow.Checked = false; cDelay.Checked = false; rStartupOnly.Checked = true; rSkip.Checked = true; cParAuto.Checked = true; cBwLimit.Checked = false; nRetry.Value = 0; },
                    delegate { rMirror.Checked = true; cDup.Checked = true; rManual.Checked = true; rInterval.Checked = true; },
                    delegate { rAppend.Checked = true; cDup.Checked = true; rDaily.Checked = true; } })
                {
                    set(); UpdateEnabled();
                    List<string> missing = MissingReasons();
                    expect(missing.Count == 0, "灰色なのに理由が出ない：" + string.Join("、", missing.ToArray()));
                }
            }
            finally { loading = saveLoading; }
            return bad;
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
            if (!d.StartsWith(@"\\") && CloudFiles.IsCloudFolder(d))
                lPreview.Text += "\nOneDrive（クラウド同期フォルダ）です：" + (AppCore.Settings.CloudFreeLocal
                    ? "コピーしたファイルは、アップロード後にこのPCから空けます（「クラウドだけ」にする。アプリの設定で変えられます）。"
                    : "コピーしたファイルはこのPCにも残ります（アプリの設定で「このPCに残さない」にできます）。");
            if (cbServer != null) RefreshServers();
        }

        // ================= 除外ルール =================

        private TabPage BuildExclude()
        {
            TabPage p = new TabPage("対象・除外");
            FlowLayoutPanel b = Ui.Body();
            // この設定で、コピー元のうち何件をコピー・除外するか（入力するたびに数え直す）
            lExclCount = Ui.L("");
            lExclCount.Font = Ui.Bold;
            lExclCount.ForeColor = Ui.Accent;
            Ui.FitToForm(lExclCount, 700);
            b.Controls.Add(lExclCount);
            tOnlyFolders = Ui.T("", 460);
            tOnlyExt = Ui.T("", 460);
            Adv(b, Ui.Section("対象を限定する（空欄ならすべて）",
                Ui.Column(Ui.L("コピー元からの相対フォルダ（配下を含む）"), tOnlyFolders,
                    Ui.B("対象フォルダを選んで追加…", delegate {
                        string src = PathUtil.Expand(tSrc.Text, AppCore.Settings.FolderName);
                        string f = Ui.BrowseFolder(this, src, "コピー対象のフォルダを選択");
                        if (f == null) return;
                        string root = PathUtil.Normalize(src).TrimEnd('\\');
                        if (!PathUtil.IsSameOrInside(f, src) || string.Equals(PathUtil.Normalize(f), root, StringComparison.OrdinalIgnoreCase)) { Ui.Info(this, "コピー元の中のフォルダを選んでください。"); return; }
                        string rel = PathUtil.Normalize(f).Substring(root.Length + 1);
                        tOnlyFolders.Text += (tOnlyFolders.Text.Trim().Length == 0 ? "" : "; ") + rel;
                    })),
                Ui.Column(Ui.L("対象の拡張子（例：.pdf; .xlsx）"), tOnlyExt),
                Ui.Hint("両方指定した場合は、指定フォルダ内の指定拡張子だけが対象です。対象外の既存コピーは削除しません。")));
            b.Controls.Add(Ui.Hint("次の条件のどれかに当てはまるファイルはコピーしません。"));

            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.AutoSize = true;
            Ui.FitToForm(presets, 700);
            presets.WrapContents = true;
            foreach (string k in ExcludePresets.Keys)
            {
                CheckBox c = Ui.C(ExcludePresets.Label(k), false);
                presetBoxes[k] = c;
                presets.Controls.Add(c);
            }
            tExt = Ui.T("", 460);
            tPat = Ui.T("", 460);
            Button pickExt = Ui.B("コピー元にある種類から選ぶ…", delegate { PickExtensions(); });
            Btn.SetIcon(pickExt, "add", Ui.Ok);
            b.Controls.Add(Ui.Section("ファイルの種類", presets,
                Ui.Row(Ui.L("追加の拡張子", 110), Ui.Column(tExt, pickExt, Ui.Hint("例：.log; .psd　（「;」で区切る。上のボタンで、コピー元にある種類を件数・大きさを見ながら選べます）"))),
                Ui.Row(Ui.L("ファイル名", 110), Ui.Column(tPat, Ui.Hint("* と ? が使えます（例：~$*　→「~$」で始まる名前）")))));

            cMax = Ui.C("", false);
            nMax = Ui.N(1, 1000000, 500, 72);
            cbMaxUnit = Ui.Combo(56, "MB", "GB");
            cZero = Ui.C("0 バイトのファイル", false);
            cOlder = Ui.C("", false);
            dOlder = Ui.DateBox(new DateTime(2020, 1, 1));
            cHidden = Ui.C("隠しファイル", true);
            cSystem = Ui.C("システムファイル", true);
            Adv(b, Ui.Section("サイズ・日付・属性",
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
                    Ui.B("一覧から外す", delegate { if (lbFolders.SelectedIndex >= 0) { lbFolders.Items.RemoveAt(lbFolders.SelectedIndex); if (counter != null) counter.Request(); } })))));

            tInc = Ui.T("", 460);
            Button pickInc = Ui.B("ファイルを選んで追加…", delegate { PickInclude(); });
            Btn.SetIcon(pickInc, "add", Ui.Ok);
            Adv(b, Ui.Section("例外（必ずコピー）", Ui.Row(tInc, pickInc), Ui.Hint("例：会議録画\\*.mp4　（除外の条件に当てはまっても、これに当てはまるファイルはコピーします）")));

            // 「テスト」のボタンは下の帯の1つだけにする（同じボタンが2つあると迷うため）
            b.Controls.Add(Ui.Hint("除外されるファイルの一覧は、下の「テスト（コピーせずに結果を確認）」で開く結果の画面の「除外」で確認できます。"));
            EventHandler recount = delegate { if (!loading && counter != null) counter.Request(); };
            foreach (Control c in new Control[] { tExt, tPat, tInc, tOnlyFolders, tOnlyExt }) c.TextChanged += recount;
            foreach (CheckBox c in new CheckBox[] { cMax, cZero, cOlder, cHidden, cSystem }) c.CheckedChanged += recount;
            foreach (CheckBox c in presetBoxes.Values) c.CheckedChanged += recount;
            nMax.ValueChanged += recount; cbMaxUnit.SelectedIndexChanged += recount; dOlder.ValueChanged += recount;
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
            if (counter != null) counter.Request();
        }

        /// <summary>コピー元にあるファイルの種類（拡張子）を、件数・大きさを見ながら選んで「追加の拡張子」に足す</summary>
        private void PickExtensions()
        {
            JobConfig j = BuildSnapshot();
            string src = CopyEngine.ResolvedSource(j, AppCore.Settings);
            if (string.IsNullOrEmpty(src) || !Directory.Exists(PathUtil.Long(src))) { Ui.Info(this, "先に「基本」タブで、① コピー元フォルダを指定してください。"); return; }
            bool stopped;
            List<ExtCount> list = Ui.BusyCancel<List<ExtCount>>(this, "コピー元にあるファイルの種類を数えています…", delegate(System.Threading.CancellationToken ct, BusyProgress bp)
            {
                return ExtCount.Of(Scanner.Scan(src, j.IncludeSubdirs, null, true, ct, delegate(int n) { bp.Text = Fmt.Count(n) + "件"; }));
            }, out stopped);
            if (stopped || list == null) return;
            if (list.Count == 0) { Ui.Info(this, "コピー元にファイルがありません。"); return; }
            List<string> already = SplitList(tExt.Text);
            using (Form f = new Form())
            {
                Ui.SetupDialog(f, "除外する種類を選ぶ", 560, 480);
                CheckedListBox cl = new CheckedListBox();
                cl.CheckOnClick = true; cl.Dock = DockStyle.Fill; cl.Font = Ui.Base; cl.IntegralHeight = false;
                foreach (ExtCount e in list) cl.Items.Add(e, already.Exists(delegate(string x) { return string.Equals(x, e.Ext, StringComparison.OrdinalIgnoreCase); }));
                Label head = Ui.L("チェックした種類はコピーしません（大きい順）。");
                head.Dock = DockStyle.Top; head.Padding = new Padding(Ui.S(8));
                Button ok = Btn.MakePrimary(Ui.B("追加", delegate { f.DialogResult = DialogResult.OK; f.Close(); }));
                f.Controls.Add(cl); f.Controls.Add(head);
                f.Controls.Add(Ui.Footer(Ui.B("キャンセル", delegate { f.Close(); }), ok));
                if (f.ShowDialog(this) != DialogResult.OK) return;
                foreach (object o in cl.CheckedItems)
                {
                    string ext = ((ExtCount)o).Ext;
                    if (ext.Length > 1 && !already.Exists(delegate(string x) { return string.Equals(x, ext, StringComparison.OrdinalIgnoreCase); })) already.Add(ext);
                }
                tExt.Text = string.Join("; ", already.ToArray());
            }
        }

        /// <summary>コピー元のファイルを選んで「例外（必ずコピー）」に足す</summary>
        private void PickInclude()
        {
            string src = CopyEngine.ResolvedSource(BuildSnapshot(), AppCore.Settings);
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "必ずコピーするファイルを選んでください（コピー元の中）";
                try { if (!string.IsNullOrEmpty(src) && Directory.Exists(src)) d.InitialDirectory = src; } catch (Exception) { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrEmpty(src) || !PathUtil.IsSameOrInside(d.FileName, src)) { Ui.Info(this, "コピー元の中のファイルを選んでください。"); return; }
                string rel = PathUtil.Normalize(d.FileName).Substring(PathUtil.Normalize(src).TrimEnd('\\').Length + 1);
                tInc.Text += (tInc.Text.Trim().Length == 0 ? "" : "; ") + rel;
            }
        }

        private void AddFolderByName()
        {
            string name = InputBox.Show(this, "除外するフォルダ名", "フォルダ名（例：node_modules）または コピー元からの相対パス", "");
            if (!string.IsNullOrEmpty(name) && name.Trim().Length > 0) { lbFolders.Items.Add(name.Trim()); if (counter != null) counter.Request(); }
        }

        // ================= 接続・重複チェック =================

        private TabPage BuildConnect()
        {
            TabPage p = new TabPage("接続・コピー先の整理");
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
            Adv(b, Ui.Section("接続できないとき",
                Ui.Row(Ui.L("再試行", 110), nRetry, Ui.L("回、"), nRetryInt, Ui.L("秒おきに試す")),
                Ui.Wrap("すべて失敗したらエラーを通知し、次の実行時刻にもう一度試します。ID・パスワードの誤りのときは再試行しません。", 640)));

            cDup = Ui.C("コピー先の整理（重複チェック）を行う：古い名前のコピー・空のフォルダなどを探して知らせる", true);
            rStartupDup = Ui.R("ログオン時（起動時）", false);
            rDaily = Ui.R("毎日", true);
            rWeekly = Ui.R("毎週", false);
            rMonthly = Ui.R("毎月", false);
            rYearly = Ui.R("毎年", false);
            cbWeekday = Ui.Combo(64, "日曜", "月曜", "火曜", "水曜", "木曜", "金曜", "土曜");
            List<string> days = new List<string>();
            for (int d = 1; d <= 28; d++) days.Add(d + "日");
            days.Add("末日");
            cbDupDay = Ui.Combo(64, days.ToArray());
            List<string> months = new List<string>();
            for (int m = 1; m <= 12; m++) months.Add(m + "月");
            cbDupMonth = Ui.Combo(64, months.ToArray());
            rManual = Ui.R("手動のみ", false);
            tDupTime = Ui.T("12:00", 60);
            cSame = Ui.C("中身が同じファイル（情報として表示のみ）", true);
            cOrphan = Ui.C("コピー元にないファイル（孤立ファイル）も表示する", false);
            cEmpty = Ui.C("空のフォルダ（コピー元にないもの）", true);
            cbDupMin = Ui.SizeChoice(0, "小さいファイルも探す（すべて）", "{0} より小さいファイルは探さない");
            FlowLayoutPanel findWhat = Ui.Row(Ui.L("探すもの", 110), Ui.Column(cAlwaysRename = Ui.C("ファイル名・フォルダ名の変更や移動の取りこぼし（常に探します）", true), cEmpty, cSame, cOrphan));
            FlowLayoutPanel smallRow = Ui.Row(Ui.L("小さいファイル", 110), cbDupMin, Ui.Hint("（古いコピー・中身が同じファイルを探すとき）"));
            advanced.Add(findWhat); advanced.Add(smallRow);
            b.Controls.Add(Ui.Section("コピー先の整理（重複チェック）",
                cDup,
                Ui.Row(Ui.L("実行", 110), Ui.WrapRow(rStartupDup, rDaily, rWeekly, rMonthly, rYearly, rManual)),
                Ui.Row(Ui.L("", 110), Ui.WrapRow(Ui.L("曜日"), cbWeekday, Ui.L("月"), cbDupMonth, Ui.L("日"), cbDupDay, Ui.L("時刻"), tDupTime)),
                Ui.Row(Ui.L("", 110), Ui.Wrap("PCが止まっていて予定の日時に実行できなかったときは、その後にアプリが起動したときに実行します。", 520)),
                findWhat,
                smallRow,
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
            UpdateEnabled();
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
            rSyncTake.Checked = j.SyncTakeNew;
            rSyncNoTake.Checked = !j.SyncTakeNew;
            rSyncPush.Checked = j.SyncPushNew;
            cSyncCache.Checked = j.SyncRemoteCacheMinutes > 0;
            nSyncCache.Value = Clamp(nSyncCache, j.SyncRemoteCacheMinutes > 0 ? j.SyncRemoteCacheMinutes : 30);
            rSyncNoPush.Checked = !j.SyncPushNew;
            cRunAtStartup.Checked = j.RunAtStartup;
            rStartupOnly.Checked = j.StartupOnly;
            rInterval.Checked = !j.StartupOnly;
            rDedupOff.Checked = j.Dedup == DedupMode.Off;
            rDedupAsk.Checked = j.Dedup == DedupMode.Ask;
            rDedupOn.Checked = j.Dedup == DedupMode.Auto;
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
            cParAuto.Checked = j.Parallel <= 0;
            nPar.Value = Clamp(nPar, j.Parallel > 0 ? j.Parallel : 2);
            cBwLimit.Checked = j.BandwidthKBps > 0;
            nBw.Value = Clamp(nBw, j.BandwidthKBps > 0 ? j.BandwidthKBps : 1000);

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
            tOnlyFolders.Text = string.Join("; ", x.OnlyFolders.ToArray());
            tOnlyExt.Text = string.Join("; ", x.OnlyExtensions.ToArray());

            nRetry.Value = Clamp(nRetry, j.RetryCount);
            nRetryInt.Value = Clamp(nRetryInt, j.RetryIntervalSeconds);
            cDup.Checked = j.DupCheckEnabled;
            rStartupDup.Checked = j.DupSchedule == DupScheduleKind.Startup;
            rDaily.Checked = j.DupSchedule == DupScheduleKind.Daily;
            rWeekly.Checked = j.DupSchedule == DupScheduleKind.Weekly;
            rMonthly.Checked = j.DupSchedule == DupScheduleKind.Monthly;
            rYearly.Checked = j.DupSchedule == DupScheduleKind.Yearly;
            rManual.Checked = j.DupSchedule == DupScheduleKind.Manual;
            cbWeekday.SelectedIndex = Math.Max(0, Math.Min(6, j.DupWeekday));
            cbDupDay.SelectedIndex = j.DupDay == DupScheduleKind.LastDay ? 28 : Math.Max(0, Math.Min(27, j.DupDay - 1));
            cbDupMonth.SelectedIndex = Math.Max(0, Math.Min(11, j.DupMonth - 1));
            tDupTime.Text = j.DupTime;
            cSame.Checked = j.DupShowSameContent;
            Ui.SetSizeChoice(cbDupMin, Math.Max(0, j.DupMinSizeKB));
            cOrphan.Checked = j.DupShowOrphans;
            cEmpty.Checked = j.DupShowEmptyFolders;
            cbSyncPreset.SelectedIndex = SyncPresets.Find(j.SyncTakeNew, j.SyncPushNew);
            // 詳しい設定：前に表示していた・このジョブに初期値と違う詳しい設定がある・同期の細かい選択をしている → 見せる
            cAdvanced.Checked = AppCore.Settings.ShowAdvancedJobSettings || HasAdvancedValues(j);
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
            j.SyncTakeNew = rSync.Checked && rSyncTake.Checked;
            j.SyncPushNew = rSync.Checked && rSyncPush.Checked;
            j.SyncRemoteCacheMinutes = cSyncCache.Checked ? (int)nSyncCache.Value : 0;
            j.RunAtStartup = cRunAtStartup.Checked;
            j.StartupOnly = rStartupOnly.Checked;
            j.Dedup = rDedupOff.Checked ? DedupMode.Off : rDedupOn.Checked ? DedupMode.Auto : DedupMode.Ask;
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
            j.Parallel = cParAuto.Checked ? 0 : (int)nPar.Value;
            j.BandwidthKBps = cBwLimit.Checked ? (int)nBw.Value : 0;

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
            x.OnlyFolders = SplitList(tOnlyFolders.Text);
            x.OnlyExtensions = SplitList(tOnlyExt.Text);
            j.Exclude = x;

            j.RetryCount = (int)nRetry.Value;
            j.RetryIntervalSeconds = (int)nRetryInt.Value;
            j.DupCheckEnabled = cDup.Checked;
            j.DupSchedule = rManual.Checked ? DupScheduleKind.Manual : rStartupDup.Checked ? DupScheduleKind.Startup : rWeekly.Checked ? DupScheduleKind.Weekly
                : rMonthly.Checked ? DupScheduleKind.Monthly : rYearly.Checked ? DupScheduleKind.Yearly : DupScheduleKind.Daily;
            j.DupWeekday = cbWeekday.SelectedIndex;
            j.DupDay = cbDupDay.SelectedIndex == 28 ? DupScheduleKind.LastDay : cbDupDay.SelectedIndex + 1;
            j.DupMonth = cbDupMonth.SelectedIndex + 1;
            j.DupTime = tDupTime.Text.Trim();
            j.DupShowSameContent = cSame.Checked;
            j.DupMinSizeKB = Ui.SizeChoiceKB(cbDupMin);
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
            if (j.DupCheckEnabled && j.DupSchedule != DupScheduleKind.Manual && j.DupSchedule != DupScheduleKind.Startup && !TimeUtil.TryParseHm(j.DupTime, out ts))
            { Ui.Error(this, "コピー先の整理（重複チェック）の時刻は 12:00 の形で入力してください。"); return false; }
            string v = CopyEngine.Validate(j, AppCore.Settings, AppCore.Settings.Jobs);
            if (v != null) { Ui.Error(this, v); return false; }

            if (!isNew && (!string.Equals(job.Source, j.Source, StringComparison.OrdinalIgnoreCase) || !string.Equals(job.Dest, j.Dest, StringComparison.OrdinalIgnoreCase)))
            {
                if (!Ui.Confirm(this, "コピー元またはコピー先を変えると、前回までの記録は使いません。次回はすべてのファイルをコピー先と照合します（同じファイルはコピーしません）。よろしいですか？"))
                    return false;
            }
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

            Result = j;
            return true;
        }

        /// <summary>今の画面の設定で、コピーせずに実行結果を確かめる（保存していない変更も反映する）</summary>
        private void RunTest()
        {
            JobConfig j = BuildSnapshot();
            string v = CopyEngine.Validate(j, AppCore.Settings, AppCore.Settings.Jobs);
            if (v != null) { Ui.Error(this, v); return; }
            SaveServerEdits();
            foreach (string path in new string[] { j.Source, j.Dest })
            {
                string server = PathUtil.UncServer(path); string[] credential;
                if (server == null || !credEdits.TryGetValue(server, out credential)) continue;
                string error = NetAccess.Test(path, credential[0], credential[1]);
                if (error != null) { Ui.Error(this, error); return; }
            }
            using (PreviewForm f = new PreviewForm(j, false)) f.ShowDialog(this);
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

    /// <summary>同期のしかた（よく使う組み合わせ）</summary>
    public static class SyncPresets
    {
        public static readonly string[] Labels = {
            "共有フォルダのファイルを受け取る（初期値）",
            "すべてのPCで同じにする（追加したファイルも送る）",
            "自分で選ぶ（下の2つ）" };
        public const int Custom = 2;

        public static bool Values(int index, out bool take, out bool push)
        {
            take = true; push = false;
            if (index == 0) return true;
            if (index == 1) { push = true; return true; }
            return false;
        }

        public static int Find(bool take, bool push)
        {
            for (int i = 0; i < Custom; i++) { bool t, p; Values(i, out t, out p); if (t == take && p == push) return i; }
            return Custom;
        }
    }

    /// <summary>拡張子ごとの件数・大きさ</summary>
    public class ExtCount
    {
        public string Ext; public int Count; public long Bytes;
        public override string ToString() { return (Ext.Length > 0 ? Ext : "（拡張子なし）") + "　" + Fmt.Count(Count) + "件・" + Fmt.Size(Bytes); }

        public static List<ExtCount> Of(ScanResult sc)
        {
            Dictionary<string, ExtCount> d = new Dictionary<string, ExtCount>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ScanItem> kv in sc.Files)
            {
                string e = Path.GetExtension(kv.Key).ToLowerInvariant();
                ExtCount c;
                if (!d.TryGetValue(e, out c)) { c = new ExtCount(); c.Ext = e; d[e] = c; }
                c.Count++; c.Bytes += kv.Value.Size;
            }
            List<ExtCount> l = new List<ExtCount>(d.Values);
            l.Sort(delegate(ExtCount a, ExtCount b) { return b.Bytes.CompareTo(a.Bytes); });
            return l;
        }
    }

    /// <summary>
    /// 除外ルールを変えるたびに、コピー元のうち何件をコピー・除外するかを数えて表示する（別のスレッドで。30秒で打ち切る）。
    /// コピー元の全件数は、コピー元が変わったときだけ数え直す。
    /// </summary>
    public sealed class ExclusionCounter : IDisposable
    {
        public static int TimeoutSeconds = 30;
        private readonly Control owner;
        private readonly Label label;
        private readonly Func<JobConfig> snapshot;
        private readonly Timer timer = new Timer();
        private System.Threading.CancellationTokenSource cts;
        private string cachedKey;
        private int cachedTotal = -1;
        private bool disposed;

        public ExclusionCounter(Control owner, Label label, Func<JobConfig> snapshot)
        {
            this.owner = owner; this.label = label; this.snapshot = snapshot;
            timer.Interval = 700;
            timer.Tick += delegate { timer.Stop(); Start(); };
        }

        /// <summary>少し待ってから数え直す（続けて入力しているときは最後の入力の後に1回）</summary>
        public void Request()
        {
            if (disposed || label == null) return;
            timer.Stop();
            timer.Start();
        }

        /// <summary>数える（確認用にも使う）：コピーする件数を返し、total にコピー元の全件数</summary>
        public static int Count(string src, bool sub, ExcludeRules rules, System.Threading.CancellationToken ct, out int total)
        {
            // コピーするときと同じ調べ方（CopyEngine と同じ：_AutoCopy の扱いも同じ）
            total = Scanner.Scan(src, sub, null, false, ct).Files.Count;
            return Scanner.Scan(src, sub, new ExcludeMatcher(rules), false, ct).Files.Count;
        }

        private void Start()
        {
            if (disposed) return;
            JobConfig j = snapshot();
            string src = CopyEngine.ResolvedSource(j, AppCore.Settings);
            if (string.IsNullOrEmpty(src) || !Directory.Exists(PathUtil.Long(src))) { label.Text = "「基本」タブで ① コピー元フォルダを指定すると、コピーする件数をここに表示します。"; return; }
            if (cts != null) cts.Cancel();
            System.Threading.CancellationTokenSource my = new System.Threading.CancellationTokenSource();
            cts = my;
            my.CancelAfter(TimeoutSeconds * 1000);
            label.Text = "この設定でコピーする件数を数えています…";
            string key = src + "|" + j.IncludeSubdirs;
            int knownTotal = key == cachedKey ? cachedTotal : -1;
            bool sub = j.IncludeSubdirs;
            ExcludeRules rules = j.Exclude;
            System.Threading.Tasks.Task.Factory.StartNew(delegate()
            {
                string text;
                try
                {
                    int total = knownTotal;
                    if (total < 0) total = Scanner.Scan(src, sub, null, false, my.Token).Files.Count;
                    int inc = Scanner.Scan(src, sub, new ExcludeMatcher(rules), false, my.Token).Files.Count;
                    cachedKey = key; cachedTotal = total;
                    text = "この設定で：コピーする " + Fmt.Count(inc) + "件・除外 " + Fmt.Count(Math.Max(0, total - inc)) + "件（コピー元 " + Fmt.Count(total) + "件）";
                }
                catch (OperationCanceledException)
                {
                    if (my != cts) return;   // 次の数え直しに替わった
                    text = "ファイルが多いため数えきれませんでした（「テスト」で確認できます）";
                }
                catch (Exception ex) { text = "コピー元を読めないため数えられません：" + ex.Message; }
                if (my != cts || disposed) return;
                try { owner.BeginInvoke(new Action(delegate() { if (!disposed && my == cts) label.Text = text; })); } catch (Exception) { }
            });
        }

        public void Dispose()
        {
            disposed = true;
            timer.Stop(); timer.Dispose();
            if (cts != null) cts.Cancel();
        }
    }
}
