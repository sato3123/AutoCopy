using System;
using System.Collections.Generic;

namespace AutoCopy
{
    /// <summary>コピー中のファイル1件</summary>
    public class ActiveFile
    {
        public string Rel;
        public long Size;
        public long Done;
        public string Kind;      // コピー / 上書き / 取り込み
        public DateTime Started;
    }

    /// <summary>
    /// 1つのジョブの作業状況（コピーの処理側が書き、画面が読む）。すべて lock (this) で読み書きする。
    /// </summary>
    public class JobProgress
    {
        public string Phase = "";         // 今していること
        public int Scanned;               // 調べたファイル数
        public int TotalFiles;            // 今回コピー・上書き・取り込みするファイル数
        public int DoneFiles;             // そのうち終わった数（エラー・使用中も含む）
        public int ErrorFiles;
        public long TotalBytes;
        public long DoneBytes;            // 終わったファイル＋コピー中のファイルの済んだ分
        public DateTime Started = DateTime.Now;
        public DateTime CopyStarted = DateTime.MinValue;
        public DateTime Finished = DateTime.MinValue;
        public string LastFile = "";
        public readonly List<ActiveFile> Active = new List<ActiveFile>();
        private long finishedBytes;

        public void SetPhase(string p) { lock (this) Phase = p; }

        public void SetPlan(int files, long bytes)
        {
            lock (this) { TotalFiles = files; TotalBytes = bytes; CopyStarted = DateTime.Now; }
        }

        public void AddPlan(int files, long bytes)
        {
            lock (this) { TotalFiles += files; TotalBytes += bytes; if (CopyStarted == DateTime.MinValue) CopyStarted = DateTime.Now; }
        }

        public ActiveFile Begin(string rel, long size, string kind)
        {
            ActiveFile a = new ActiveFile();
            a.Rel = rel; a.Size = size; a.Kind = kind; a.Started = DateTime.Now;
            lock (this) { Active.Add(a); }
            return a;
        }

        public void Bytes(ActiveFile a, long done)
        {
            lock (this) { a.Done = done; Recalc(); }
        }

        public void End(ActiveFile a, bool ok)
        {
            lock (this)
            {
                Active.Remove(a);
                DoneFiles++;
                if (!ok) ErrorFiles++;
                finishedBytes += a.Size;
                LastFile = a.Rel;
                Recalc();
            }
        }

        private void Recalc()
        {
            long b = finishedBytes;
            foreach (ActiveFile x in Active) b += Math.Min(x.Done, x.Size);
            DoneBytes = b;
        }

        /// <summary>画面に出すための写し</summary>
        public JobProgress Snapshot()
        {
            lock (this)
            {
                JobProgress s = (JobProgress)MemberwiseClone();
                List<ActiveFile> list = new List<ActiveFile>();
                foreach (ActiveFile a in Active)
                {
                    ActiveFile c = new ActiveFile();
                    c.Rel = a.Rel; c.Size = a.Size; c.Done = a.Done; c.Kind = a.Kind; c.Started = a.Started;
                    list.Add(c);
                }
                s.activeCopy = list;
                return s;
            }
        }

        private List<ActiveFile> activeCopy;
        public List<ActiveFile> ActiveList { get { return activeCopy ?? Active; } }

        /// <summary>進み具合（0〜100）。容量で計算し、容量がなければ件数で</summary>
        public double Percent
        {
            get
            {
                if (TotalBytes > 0) return Math.Min(100, DoneBytes * 100.0 / TotalBytes);
                if (TotalFiles > 0) return Math.Min(100, DoneFiles * 100.0 / TotalFiles);
                return Finished != DateTime.MinValue ? 100 : 0;
            }
        }

        /// <summary>転送の速さ（バイト/秒）</summary>
        public double Speed
        {
            get
            {
                if (CopyStarted == DateTime.MinValue) return 0;
                double sec = ((Finished != DateTime.MinValue ? Finished : DateTime.Now) - CopyStarted).TotalSeconds;
                return sec < 1 ? 0 : DoneBytes / sec;
            }
        }

        /// <summary>残り時間の目安（不明 = 負の値）</summary>
        public double RemainingSeconds
        {
            get
            {
                double sp = Speed;
                if (sp <= 0 || TotalBytes <= 0) return -1;
                return Math.Max(0, (TotalBytes - DoneBytes) / sp);
            }
        }

        public static string Duration(double sec)
        {
            if (sec < 0) return "計算中";
            if (sec < 60) return Math.Ceiling(sec) + "秒";
            if (sec < 3600) return Math.Floor(sec / 60) + "分" + ((int)sec % 60).ToString("00") + "秒";
            return Math.Floor(sec / 3600) + "時間" + ((int)(sec / 60) % 60).ToString("00") + "分";
        }

        /// <summary>一覧などに出す短い説明</summary>
        public string Short()
        {
            if (TotalFiles > 0 && CopyStarted != DateTime.MinValue)
                return Phase + " " + Percent.ToString("0") + "%（" + Fmt.Count(DoneFiles) + " / " + Fmt.Count(TotalFiles) + "件）";
            if (Scanned > 0) return Phase + "（" + Fmt.Count(Scanned) + "件）";
            return Phase;
        }
    }
}
