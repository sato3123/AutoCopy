using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AutoCopy
{
    /// <summary>コピー済みファイル1件の記録</summary>
    public class FileRecord
    {
        public long Size;
        public long Mtime;        // コピー元の更新日時（UTC ticks）
        public uint Volume;       // ファイルID（ボリューム）
        public ulong FileIndex;   // ファイルID（0 = 不明）
        public long DestMtime;    // コピー先の更新日時（UTC ticks）
        public bool CopiedByApp;  // このジョブがコピーした（ミラーで削除してよい）
        public byte[] Hash;       // MD5（null = 不明）
        public string PlaceholderOf; // 重複のためコピーせず、場所を書いたテキストを置いた：中身が同じコピー元ファイル（null = 普通にコピー）
        public long CopiedAt;        // 初めてコピー先にコピーした日時（UTC ticks。0 = 不明）。上書きしても変えない

        /// <summary>前の記録から「初めてコピーした日時」を引き継ぐ（なければ今）</summary>
        public static long FirstCopied(FileRecord old)
        {
            return old != null && old.CopiedAt > 0 ? old.CopiedAt : DateTime.UtcNow.Ticks;
        }

        public FileRecord Clone()
        {
            FileRecord r = (FileRecord)MemberwiseClone();
            return r;
        }
    }

    /// <summary>
    /// ジョブごとの状態（%LOCALAPPDATA%\AutoCopy\state\ジョブID.dat）
    /// SQLite 等の外部DLLを使わず、単一の exe で動くよう独自の小さなバイナリ形式で保存する。
    /// </summary>
    public class JobState
    {
        public Dictionary<string, FileRecord> Files = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SkipForever = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<ConflictItem> Conflicts = new List<ConflictItem>();
        public List<PendingDelete> PendingDeletes = new List<PendingDelete>();
        public long LastRunTicks;
        public long LastDupCheckTicks;
        public long LastTrashPurgeTicks;
        public string LastResult = "";
        public string LastMessage = "";

        private const int Magic = 0x31534341; // "ACS1"
        private string path;

        public static string PathFor(string jobId)
        {
            return Path.Combine(AppPaths.StateDir, jobId + ".dat");
        }

        /// <summary>読み込みで問題があったとき、その説明（なし = null）。呼び出し側が知らせる</summary>
        public string LoadProblem;

        public const int FormatVersion = 3;

        /// <summary>
        /// 状態ファイルを読む。壊れていたら、壊れたファイルを「.corrupt-日時」として残し、前回保存した内容（.bak）から戻す。
        /// どちらも読めないときは空から始める（記録がないので、ミラーの削除・同期の削除の候補は出ない＝消す方向には動かない）。
        /// </summary>
        public static JobState Load(string jobId)
        {
            string path = PathFor(jobId);
            JobState s = new JobState();
            s.path = path;
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return s;
            string err = null;
            if (File.Exists(path))
            {
                try { return ReadFrom(path, s); }
                catch (Exception ex)
                {
                    err = ex.Message;
                    try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch (Exception) { }
                }
            }
            JobState b = new JobState();
            b.path = path;
            if (File.Exists(path + ".bak"))
            {
                try
                {
                    ReadFrom(path + ".bak", b);
                    b.LoadProblem = "状態ファイル" + (err != null ? "が壊れていた（" + err + "）" : "がなかった") + "ため、前回保存した内容から戻しました";
                    Logger.Error(null, b.LoadProblem + ": " + path);
                    return b;
                }
                catch (Exception ex2) { err = (err ?? "") + "／前回保存した内容も読めません（" + ex2.Message + "）"; }
            }
            JobState empty = new JobState();
            empty.path = path;
            empty.LoadProblem = "状態ファイルを読めなかったため、記録を作り直します（" + err + "）。次の実行では全体を確かめ直します。記録がないため、ミラーの削除・同期の削除の候補は出しません";
            Logger.Error(null, empty.LoadProblem + ": " + path);
            return empty;
        }

        private static JobState ReadFrom(string file, JobState s)
        {
            using (BinaryReader r = new BinaryReader(File.OpenRead(file), Encoding.UTF8))
            {
                if (r.ReadInt32() != Magic) throw new InvalidDataException("状態ファイルの形式が違います");
                int ver = r.ReadInt32();
                if (ver < 1 || ver > FormatVersion) throw new InvalidDataException("対応していない形式の版です（" + ver + "）");
                s.LastRunTicks = r.ReadInt64();
                s.LastDupCheckTicks = r.ReadInt64();
                s.LastTrashPurgeTicks = r.ReadInt64();
                s.LastResult = r.ReadString();
                s.LastMessage = r.ReadString();
                int n = r.ReadInt32();
                if (n < 0) throw new InvalidDataException("件数が正しくありません");
                for (int i = 0; i < n; i++)
                {
                    string rel = r.ReadString();
                    FileRecord f = new FileRecord();
                    f.Size = r.ReadInt64();
                    f.Mtime = r.ReadInt64();
                    f.Volume = r.ReadUInt32();
                    f.FileIndex = r.ReadUInt64();
                    f.DestMtime = r.ReadInt64();
                    byte flags = r.ReadByte();
                    f.CopiedByApp = (flags & 1) != 0;
                    if ((flags & 2) != 0) f.Hash = r.ReadBytes(16);
                    if ((flags & 4) != 0) f.PlaceholderOf = r.ReadString();
                    if ((flags & 8) != 0) f.CopiedAt = r.ReadInt64();
                    s.Files[rel] = f;
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) s.SkipForever.Add(r.ReadString());
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    ConflictItem c = new ConflictItem();
                    c.Rel = r.ReadString();
                    c.SrcSize = r.ReadInt64(); c.SrcMtime = r.ReadInt64();
                    c.DstSize = r.ReadInt64(); c.DstMtime = r.ReadInt64();
                    c.Reason = r.ReadString(); c.Detected = r.ReadString();
                    s.Conflicts.Add(c);
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    PendingDelete d = new PendingDelete();
                    d.Rel = r.ReadString();
                    d.Size = r.ReadInt64();
                    s.PendingDeletes.Add(d);
                }
                // 最後まで読めたことを確かめる（途中で切れたファイルを正しいものとして扱わない）
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("ファイルの終わりに余分なデータがあります");
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.StateDir);
            string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
            using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (BinaryWriter w = new BinaryWriter(fs, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(LastRunTicks);
                w.Write(LastDupCheckTicks);
                w.Write(LastTrashPurgeTicks);
                w.Write(LastResult ?? "");
                w.Write(LastMessage ?? "");
                w.Write(Files.Count);
                foreach (KeyValuePair<string, FileRecord> kv in Files)
                {
                    FileRecord f = kv.Value;
                    w.Write(kv.Key);
                    w.Write(f.Size);
                    w.Write(f.Mtime);
                    w.Write(f.Volume);
                    w.Write(f.FileIndex);
                    w.Write(f.DestMtime);
                    byte flags = (byte)((f.CopiedByApp ? 1 : 0) | (f.Hash != null && f.Hash.Length == 16 ? 2 : 0) | (f.PlaceholderOf != null ? 4 : 0) | (f.CopiedAt > 0 ? 8 : 0));
                    w.Write(flags);
                    if ((flags & 2) != 0) w.Write(f.Hash);
                    if ((flags & 4) != 0) w.Write(f.PlaceholderOf);
                    if ((flags & 8) != 0) w.Write(f.CopiedAt);
                }
                w.Write(SkipForever.Count);
                foreach (string s in SkipForever) w.Write(s);
                w.Write(Conflicts.Count);
                foreach (ConflictItem c in Conflicts)
                {
                    w.Write(c.Rel ?? ""); w.Write(c.SrcSize); w.Write(c.SrcMtime);
                    w.Write(c.DstSize); w.Write(c.DstMtime);
                    w.Write(c.Reason ?? ""); w.Write(c.Detected ?? "");
                }
                w.Write(PendingDeletes.Count);
                foreach (PendingDelete d in PendingDeletes)
                {
                    w.Write(d.Rel ?? ""); w.Write(d.Size);
                }
                w.Flush();
                fs.Flush(true);   // ディスクに確定させてから置き換える
            }
            // 前回の内容は .bak として残す（壊れたときに戻せるように）
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
            else File.Move(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
            LoadProblem = null;
        }

        public void Delete()
        {
            try { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
            catch (Exception ex) { Logger.Error(null, "状態ファイルを消せません: " + ex.Message); }
        }
    }
}
