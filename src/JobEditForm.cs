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
            test.Margin = new Padding(3, 3, 200, 3);
            foot.Controls.Add(test);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            if (isNew)
            {
                Label nl = Ui.L("新しいジョブは「無効」で作られます（自動では実行されません）。「テスト」で結果を確かめてから、メイン画面のジョブの左のチェックを入れて有効にしてください。");
                Ui.FitToForm(nl, 760);
                Panel nb = Ui.Banner(Ui.InfoBack, nl);
                nb.Dock = DockStyle.Top;
                Controls.Add(nb);
            }
            Controls.Add(foot);
            LoadValues();
            loading = false;
            WireEnabled();
            UpdateEnabled();
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
            nSyncCache = Ui.N(0, 1440, 30, 72);
            Label lCacheNote = Ui.Wrap("共有フォルダの中を全部調べるのは時間がかかるため、前回の詳しい確認からこの時間以内で、どのPCも自動コピーで共有フォルダを変えていなければ、前回の確認の結果を使います（0 = 毎回詳しく確認）。自動コピー以外の方法で共有フォルダを直接変えた場合は、この時間がたってから反映されます。", 540);
            lCacheNote.ForeColor = Ui.Quiet;
            FlowLayoutPanel syncCache = Ui.Column(Ui.Row(Ui.Hint("共有フォルダの詳しい確認を、前回から"), nSyncCache, Ui.Hint("分以内なら省略する")), lCacheNote);
            syncCache.Margin = new Padding(Ui.S(22), 0, 3, 4);
            FlowLayoutPanel modes = Ui.Column(rAppend, rMirror, mirrorLimit, rSync, lSyncNote, syncNew, syncPush, syncCache);
            b.Controls.Add(Ui.Row(Ui.L("コピーの方式", 130), modes));

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
            b.Controls.Add(Ui.Row(Ui.L("実行のタイミング", 130), Ui.Column(
                Ui.Row(rInterval, nInterval, cbUnit, Ui.Hint("ごと（1分〜24時間）")),
                cRunAtStartup, lStartupNote, rStartupOnly, lOnlyNote)));

            cWindow = Ui.C("時間帯を限定する", false);
            tStart = Ui.T("08:00", 60);
            tEnd = Ui.T("20:00", 60);
            b.Controls.Add(Ui.Row(Ui.L("実行する時間帯", 130), cWindow, tStart, Ui.L("〜"), tEnd));

            cDelay = Ui.C("PC起動後", true);
            nDelay = Ui.N(0, 120, 1, 56);
            b.Controls.Add(Ui.Row(Ui.L("起動後の初回", 130), cDelay, nDelay, Ui.L("分待ってから実行する")));


            cFollow = Ui.C("名前やフォルダ名を変えたら、コピー先も同じ名前に変える（再コピーしない）", true);
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

            rDedupOff = Ui.R("使わない（重複していてもすべてコピーする）", false);
            rDedupAsk = Ui.R("見つけたら毎回選ぶ（初期値）：選ぶまで2つ目以降はコピーせずに待ち、お知らせする", true);
            rDedupOn = Ui.R("自動でテキストにする：2つ目以降はコピーせず、1つ目の保管場所を書いたテキストを置く", false);
            rDedupOn.CheckedChanged += delegate { UpdateEnabled(); };
            nDedup = Ui.N(1, 1000000, 10, 72);
            lDedupNote = Ui.Wrap("「テキスト」は「〇〇" + AppPaths.PlaceholderSuffix + "」で、1つ目の保管場所を書きます。中身はファイル全体で比べます（同じ大きさのものだけ読みます）。毎回選ぶ場合は、重複チェックの画面で「テキストにする」か「普通にコピー」を選びます。普通にコピー済みの重複も、重複チェックの画面でテキストにできます。", 600);
            lDedupNote.ForeColor = Ui.Quiet;
            b.Controls.Add(Ui.Section("大きいファイルの重複（コピー元の中で中身が同じファイル）",
                Ui.Column(rDedupOff, rDedupAsk, rDedupOn),
                Ui.Row(Ui.L("　　対象の大きさ", 160), nDedup, Ui.L("MB 以上")),
                lDedupNote));

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
                + "・削除は「削除の確認」で選んでから伝えます。両方で変えたファイルは確認画面で選びます。\n"
                + "・共有フォルダにだけあるファイルは、初期値ではこのPCに取り込みます。このPCにだけあるファイルは、初期値では共有フォルダにコピーしません（どちらも下で選べます）。\n"
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

        private void UpdateSyncControls() { UpdateEnabled(); }

        /// <summary>上位の設定が変わったら、下位の項目の操作できる・できないを直す</summary>
        private void WireEnabled()
        {
            EventHandler h = delegate { UpdateEnabled(); };
            foreach (CheckBox c in new CheckBox[] { cWindow, cDelay, cMax, cOlder, cDup }) c.CheckedChanged += h;
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
            nSyncCache.Enabled = sync;
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
            cEmpty.Enabled = cSame.Enabled = cOrphan.Enabled = dup;
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
                expect(!nSyncCache.Enabled, "同期でないのに、共有フォルダの確認を省略する時間を変えられる");
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
                expect(!rDaily.Enabled && !tDupTime.Enabled && !cEmpty.Enabled && !cSame.Enabled && !cOrphan.Enabled, "重複チェックをしないのに日時・探すものを変えられる");
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
            if (cbServer != null) RefreshServers();
        }

        // ================= 除外ルール =================

        private TabPage BuildExclude()
        {
            TabPage p = new TabPage("対象・除外");
            FlowLayoutPanel b = Ui.Body();
            tOnlyFolders = Ui.T("", 460);
            tOnlyExt = Ui.T("", 460);
            b.Controls.Add(Ui.Section("対象を限定する（空欄ならすべて）",
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
            b.Controls.Add(Ui.Section("ファイルの種類", presets,
                Ui.Row(Ui.L("追加の拡張子", 110), Ui.Column(tExt, Ui.Hint("例：.log; .psd"))),
                Ui.Row(Ui.L("ファイル名", 110), Ui.Column(tPat, Ui.Hint("* と ? が使えます")))));

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
                    Ui.B("一覧から外す", delegate { if (lbFolders.SelectedIndex >= 0) lbFolders.Items.RemoveAt(lbFolders.SelectedIndex); })))));

            tInc = Ui.T("", 460);
            b.Controls.Add(Ui.Section("例外（必ずコピー）", Ui.Row(tInc, Ui.Hint("例：会議録画\\*.mp4"))));

            // 「テスト」のボタンは下の帯の1つだけにする（同じボタンが2つあると迷うため）
            b.Controls.Add(Ui.Hint("除外されるファイルは、下の「テスト（コピーせずに結果を確認）」で開く結果の画面の「除外」で確認できます。"));
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
            b.Controls.Add(Ui.Section("重複チェック",
                cDup,
                Ui.Row(Ui.L("実行", 110), Ui.WrapRow(rStartupDup, rDaily, rWeekly, rMonthly, rYearly, rManual)),
                Ui.Row(Ui.L("", 110), Ui.WrapRow(Ui.L("曜日"), cbWeekday, Ui.L("月"), cbDupMonth, Ui.L("日"), cbDupDay, Ui.L("時刻"), tDupTime)),
                Ui.Row(Ui.L("", 110), Ui.Wrap("PCが止まっていて予定の日時に実行できなかったときは、その後にアプリが起動したときに実行します。", 520)),
                Ui.Row(Ui.L("探すもの", 110), Ui.Column(cAlwaysRename = Ui.C("ファイル名・フォルダ名の変更や移動の取りこぼし（常に探します）", true), cEmpty, cSame, cOrphan)),
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
            nSyncCache.Value = Clamp(nSyncCache, j.SyncRemoteCacheMinutes);
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
            j.SyncTakeNew = rSync.Checked && rSyncTake.Checked;
            j.SyncPushNew = rSync.Checked && rSyncPush.Checked;
            j.SyncRemoteCacheMinutes = (int)nSyncCache.Value;
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
            { Ui.Error(this, "重複チェックの時刻は 12:00 の形で入力してください。"); return false; }
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
}
