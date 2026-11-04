using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AutoCopy
{
    /// <summary>アプリ全体の設定（%APPDATA%\AutoCopy\settings.json）</summary>
    [DataContract]
    public class AppSettings
    {
        [DataMember] public int FormatVersion;
        [DataMember] public string InstanceId;        // このPCの識別子（配布しない）
        [DataMember] public string FolderName;        // このPCのフォルダ名（配布しない）
        [DataMember] public string LogRoot;           // ネットワークのログ保管先
        [DataMember] public int LocalLogDays;
        [DataMember] public int NetLogDays;
        [DataMember] public int TrashDays;
        [DataMember] public bool AutoStart;
        [DataMember] public bool NotifyErrors;
        [DataMember] public bool NotifyConfirm;
        [DataMember] public bool NotifyDone;
        [DataMember] public string UpdateFolder;      // 更新版の AutoCopy.exe を置く場所（NAS の配布フォルダなど）
        [DataMember] public bool AutoUpdate;          // 置き場所に新しい版があれば自動で更新する
        [DataMember] public bool AttentionBlink;      // 確認が必要なときはアイコンを点滅させる
        [DataMember] public bool AttentionPopup;      // 確認が必要なときはメッセージ画面を出す
        [DataMember] public int AttentionRemindMinutes; // 確認が済むまで、メッセージ画面を出し直す間隔
        [DataMember] public bool ShortcutRenamed;     // 旧名のショートカットを「強制データ送信」に変えた
        [DataMember] public int DistVersion;          // 取り込んだ配布設定の版
        [DataMember] public string DistName;
        [DataMember] public string DistCreated;
        [DataMember] public List<JobConfig> Jobs;

        public AppSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        private void SetDefaults()
        {
            FormatVersion = 1;
            LocalLogDays = 90;
            NetLogDays = 365;
            TrashDays = 30;
            AutoStart = true;
            NotifyErrors = true;
            NotifyConfirm = true;
            NotifyDone = false;
            AutoUpdate = true;
            AttentionBlink = true;
            AttentionPopup = true;
            AttentionRemindMinutes = 120;
            Jobs = new List<JobConfig>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext c)
        {
            if (Jobs == null) Jobs = new List<JobConfig>();
            if (AttentionRemindMinutes <= 0) AttentionRemindMinutes = 120;
        }
    }

    public static class NewerPolicy
    {
        public const string Ask = "ask";
        public const string Skip = "skip";
        public const string Overwrite = "overwrite";
    }

    public static class JobScope
    {
        public const string Dist = "dist";
        public const string Local = "local";
    }

    [DataContract]
    public class JobConfig
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public bool Enabled;
        [DataMember] public string Scope;
        [DataMember] public string Source;
        [DataMember] public bool IncludeSubdirs;
        [DataMember] public string Dest;
        [DataMember] public int IntervalMinutes;
        [DataMember] public bool UseTimeWindow;
        [DataMember] public string WindowStart;
        [DataMember] public string WindowEnd;
        [DataMember] public bool UseStartupDelay;
        [DataMember] public int StartupDelayMinutes;
        [DataMember] public string NewerDestPolicy;
        [DataMember] public int ToleranceSeconds;
        [DataMember] public bool BackupBeforeOverwrite;
        [DataMember] public bool FollowRename;
        [DataMember] public int Parallel;             // 0 = 自動
        [DataMember] public int BandwidthKBps;        // 0 = 制限なし
        [DataMember] public int RetryCount;
        [DataMember] public int RetryIntervalSeconds;
        [DataMember] public bool Mirror;
        [DataMember] public int MirrorMaxCount;
        [DataMember] public int MirrorMaxPercent;
        [DataMember] public ExcludeRules Exclude;
        [DataMember] public bool DupCheckEnabled;
        [DataMember] public string DupSchedule;       // daily / weekly / manual
        [DataMember] public string DupTime;
        [DataMember] public int DupWeekday;           // 0=日 .. 6=土
        [DataMember] public bool DupShowSameContent;
        [DataMember] public bool DupShowOrphans;
        [DataMember] public bool DupShowEmptyFolders;
        [DataMember] public bool Sync;                // 複数PCで同期（双方向・削除は伝えない）
        [DataMember] public bool DedupLarge;          // 大きいファイルの重複は1つだけコピーし、残りは場所を書いたテキストにする
        [DataMember] public int DedupMinMB;

        public JobConfig() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext c)
        {
            if (Exclude == null) Exclude = ExcludeRules.CreateDefault();
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(Scope)) Scope = JobScope.Local;
            if (string.IsNullOrEmpty(NewerDestPolicy)) NewerDestPolicy = NewerPolicy.Ask;
            if (string.IsNullOrEmpty(DupSchedule)) DupSchedule = "daily";
            if (DedupMinMB <= 0) DedupMinMB = 10;
            if (Sync) Mirror = false;
        }

        /// <summary>方式の表示名</summary>
        public string ModeLabel { get { return Sync ? "同期（複数PC）" : Mirror ? "ミラー" : "追加・更新"; } }
        /// <summary>大きいファイルの重複を省略するか（同期では使わない：他のPCにテキストが届いてしまうため）</summary>
        public bool DedupOn { get { return DedupLarge && !Sync; } }
        public long DedupMinBytes { get { return Math.Max(1, DedupMinMB) * 1024L * 1024; } }

        private void SetDefaults()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "新しいジョブ";
            Enabled = true;
            Scope = JobScope.Local;
            Source = "";
            IncludeSubdirs = true;
            Dest = "";
            IntervalMinutes = 30;
            UseTimeWindow = false;
            WindowStart = "08:00";
            WindowEnd = "20:00";
            UseStartupDelay = true;
            StartupDelayMinutes = 1;
            NewerDestPolicy = NewerPolicy.Ask;
            ToleranceSeconds = 2;
            BackupBeforeOverwrite = false;
            FollowRename = true;
            Parallel = 0;
            BandwidthKBps = 0;
            RetryCount = 3;
            RetryIntervalSeconds = 30;
            Mirror = false;
            MirrorMaxCount = 100;
            MirrorMaxPercent = 10;
            Exclude = ExcludeRules.CreateDefault();
            DupCheckEnabled = true;
            DupSchedule = "daily";
            DupTime = "12:00";
            DupWeekday = 1;
            DupShowSameContent = true;
            DupShowOrphans = false;
            DupShowEmptyFolders = true;
            Sync = false;
            DedupLarge = true;
            DedupMinMB = 10;
        }

        public JobConfig Clone()
        {
            return Json.Deserialize<JobConfig>(Json.Serialize(this));
        }
    }

    [DataContract]
    public class ExcludeRules
    {
        [DataMember] public List<string> Presets;       // tmp, video, music, archive, exe
        [DataMember] public List<string> Extensions;    // ".log" 形式
        [DataMember] public List<string> NamePatterns;  // "~$*" 形式
        [DataMember] public long MaxSizeBytes;          // 0 = 使わない
        [DataMember] public bool ExcludeZeroByte;
        [DataMember] public string OlderThan;           // "yyyy-MM-dd" 空なら使わない
        [DataMember] public bool ExcludeHidden;
        [DataMember] public bool ExcludeSystem;
        [DataMember] public List<string> Folders;       // フォルダ名 または 相対パス
        [DataMember] public List<string> Includes;      // 例外（必ずコピー）"会議録画\*.mp4"

        public ExcludeRules()
        {
            Presets = new List<string>();
            Extensions = new List<string>();
            NamePatterns = new List<string>();
            Folders = new List<string>();
            Includes = new List<string>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext c)
        {
            if (Presets == null) Presets = new List<string>();
            if (Extensions == null) Extensions = new List<string>();
            if (NamePatterns == null) NamePatterns = new List<string>();
            if (Folders == null) Folders = new List<string>();
            if (Includes == null) Includes = new List<string>();
        }

        public static ExcludeRules CreateDefault()
        {
            ExcludeRules r = new ExcludeRules();
            r.Presets.Add("tmp");
            r.NamePatterns.Add("Thumbs.db");
            r.NamePatterns.Add("desktop.ini");
            r.NamePatterns.Add("~$*");
            r.NamePatterns.Add("*.ckpart");
            r.ExcludeHidden = true;
            r.ExcludeSystem = true;
            return r;
        }
    }

    /// <summary>配布用設定ファイルの中身（暗号化される）</summary>
    [DataContract]
    public class DistPayload
    {
        [DataMember] public string Name;
        [DataMember] public int Version;
        [DataMember] public string Created;
        [DataMember] public string LogRoot;
        [DataMember] public string UpdateFolder;
        [DataMember] public int LocalLogDays;
        [DataMember] public int NetLogDays;
        [DataMember] public int TrashDays;
        [DataMember] public List<JobConfig> Jobs;
        [DataMember] public List<DistCredential> Credentials;
    }

    [DataContract]
    public class DistCredential
    {
        [DataMember] public string Server;
        [DataMember] public string User;
        [DataMember] public string Password;
    }

    /// <summary>配布用設定ファイルの外側（平文部分）</summary>
    [DataContract]
    public class DistEnvelope
    {
        [DataMember] public string Format;
        [DataMember] public string Name;
        [DataMember] public int Version;
        [DataMember] public string Created;
        [DataMember] public int Iterations;
        [DataMember] public string Salt;
        [DataMember] public string Iv;
        [DataMember] public string Data;
        [DataMember] public string Mac;
    }

    /// <summary>コピー先フォルダに置く利用者の記録（干渉防止）</summary>
    [DataContract]
    public class OwnerInfo
    {
        [DataMember] public string InstanceId;
        [DataMember] public string ComputerName;
        [DataMember] public string FolderName;
        [DataMember] public string Created;
    }

    /// <summary>ログ保管先に置く各PCの状態</summary>
    [DataContract]
    public class PcStatus
    {
        [DataMember] public string ComputerName;
        [DataMember] public string FolderName;
        [DataMember] public string UserName;
        [DataMember] public string Updated;
        [DataMember] public List<JobStatusInfo> Jobs;
    }

    [DataContract]
    public class JobStatusInfo
    {
        [DataMember] public string Name;
        [DataMember] public string LastRun;
        [DataMember] public string Result;   // ok / error / running
        [DataMember] public string Message;
    }

    /// <summary>上書き確認待ち</summary>
    [DataContract]
    public class ConflictItem
    {
        [DataMember] public string Rel;
        [DataMember] public long SrcSize;
        [DataMember] public long SrcMtime;   // UTC ticks
        [DataMember] public long DstSize;
        [DataMember] public long DstMtime;
        [DataMember] public string Reason;   // newer / sizediff / both（同期：両方で変更） / first（同期：初回で中身が違う）
        [DataMember] public string Detected;
    }

    /// <summary>ミラーの大量削除で保留中のもの</summary>
    [DataContract]
    public class PendingDelete
    {
        [DataMember] public string Rel;
        [DataMember] public long Size;
    }
}
