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

        public static JobState Load(string jobId)
        {
            JobState s = new JobState();
            s.path = PathFor(jobId);
            if (!File.Exists(s.path)) return s;
            try
            {
                using (BinaryReader r = new BinaryReader(File.OpenRead(s.path), Encoding.UTF8))
                {
                    if (r.ReadInt32() != Magic) return s;
                    int ver = r.ReadInt32();
                    s.LastRunTicks = r.ReadInt64();
                    s.LastDupCheckTicks = r.ReadInt64();
                    s.LastTrashPurgeTicks = r.ReadInt64();
                    s.LastResult = r.ReadString();
                    s.LastMessage = r.ReadString();
                    int n = r.ReadInt32();
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
                }
            }
            catch (Exception ex)
            {
                // 壊れていた場合は退避して空から始める（コピー先は消さないので安全）
                try { File.Copy(s.path, s.path + ".broken", true); } catch (Exception) { }
                Logger.Error(null, "状態ファイルを読めなかったため作り直します: " + ex.Message);
                JobState empty = new JobState();
                empty.path = s.path;
                return empty;
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.StateDir);
            string tmp = path + ".tmp";
            using (BinaryWriter w = new BinaryWriter(File.Create(tmp), Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(2);
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
                    byte flags = (byte)((f.CopiedByApp ? 1 : 0) | (f.Hash != null && f.Hash.Length == 16 ? 2 : 0) | (f.PlaceholderOf != null ? 4 : 0));
                    w.Write(flags);
                    if ((flags & 2) != 0) w.Write(f.Hash);
                    if ((flags & 4) != 0) w.Write(f.PlaceholderOf);
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
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public void Delete()
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
