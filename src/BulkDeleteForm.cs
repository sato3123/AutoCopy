using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AutoCopy
{
    /// <summary>ファイルの一括削除の条件（すべて満たすものが対象）</summary>
    public class BulkDeleteCriteria
    {
        public List<string> Extensions = new List<string>();     // ".tmp" の形。空 = 条件にしない
        public List<string> NamePatterns = new List<string>();   // * ? を使える。空 = 条件にしない
        public long MinBytes = -1, MaxBytes = -1;                // 以上・以下（-1 = 条件にしない）
        public long OlderThanUtc = -1;                           // 更新日時がこれより前（-1 = 条件にしない）
        public bool IncludeHidden;                               // 隠しファイルも（システムファイルは常に対象外）

        public bool HasCondition
        {
            get { return Extensions.Count > 0 || NamePatterns.Count > 0 || MinBytes >= 0 || MaxBytes >= 0 || OlderThanUtc >= 0; }
        }

        private List<Regex> nameRx;

        /// <summary>「.tmp .bak」「tmp, bak」などから拡張子の一覧</summary>
        public static List<string> ParseExtensions(string s)
        {
            List<string> r = new List<string>();
            foreach (string p in (s ?? "").Split(new char[] { ' ', '　', ',', '、', ';', '；', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = p.Trim().Trim('*');
                if (e.Length == 0) continue;
                if (!e.StartsWith(".")) e = "." + e;
                e = e.ToLowerInvariant();
                if (!r.Contains(e)) r.Add(e);
            }
            return r;
        }

        /// <summary>「*~*.xlsx; Thumbs.db」などから名前の型の一覧（; 、 改行で区切る。空白は名前の一部）</summary>
        public static List<string> ParseNames(string s)
        {
            List<string> r = new List<string>();
            foreach (string p in (s ?? "").Split(new char[] { ';', '；', '、', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = p.Trim();
                if (n.Length > 0 && !r.Contains(n)) r.Add(n);
            }
            return r;
        }

        public static Regex Wildcard(string pattern)
        {
            return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public bool Matches(string rel, long size, long mtimeUtc, FileAttributes attr)
        {
            if ((attr & FileAttributes.System) != 0) return false;
            if (!IncludeHidden && (attr & FileAttributes.Hidden) != 0) return false;
            string name = PathUtil.FileName(rel);
            if (Extensions.Count > 0)
            {
                string ext = Path.GetExtension(name).ToLowerInvariant();
                if (!Extensions.Contains(ext)) return false;
            }
            if (NamePatterns.Count > 0)
            {
                if (nameRx == null) { nameRx = new List<Regex>(); foreach (string p in NamePatterns) nameRx.Add(Wildcard(p)); }
                bool any = false;
                foreach (Regex x in nameRx) if (x.IsMatch(name)) { any = true; break; }
                if (!any) return false;
            }
            if (MinBytes >= 0 && size < MinBytes) return false;
            if (MaxBytes >= 0 && size > MaxBytes) return false;
            if (OlderThanUtc >= 0 && mtimeUtc >= OlderThanUtc) return false;
            return true;
        }

        public string Describe()
        {
            List<string> p = new List<string>();
            if (Extensions.Count > 0) p.Add("拡張子 " + string.Join(" ", Extensions.ToArray()));
            if (NamePatterns.Count > 0) p.Add("名前 " + string.Join("; ", NamePatterns.ToArray()));
            if (MinBytes >= 0) p.Add(Fmt.Size(MinBytes) + " 以上");
            if (MaxBytes >= 0) p.Add(Fmt.Size(MaxBytes) + " 以下");
            if (OlderThanUtc >= 0) p.Add("更新日時が " + new DateTime(OlderThanUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd") + " より前");
            if (IncludeHidden) p.Add("隠しファイルも");
            return string.Join("・", p.ToArray());
        }
    }

    public class BulkDeleteItem
    {
        public string Rel;
        public long Size;
        public long Mtime;   // UTC ticks（探したときの値：削除の直前に変わっていないか確かめる）
    }

    public class BulkDeleteResult
    {
        public int Deleted;
        public long DeletedBytes;
        public List<string> Changed = new List<string>();   // 探した後に変わった・なくなったため削除しなかった
        public List<string> Errors = new List<string>();
        public bool Canceled;
    }

    /// <summary>
    /// ジョブとは別の「ファイルの一括削除」。指定したフォルダの中から条件に合うファイルを探し、
    /// 利用者が一覧で確かめてチェックしたものだけを削除する（ごみ箱に移せる場所では、ごみ箱へ）。
    /// </summary>
    public static class BulkDelete
    {
        public const string LogJobName = "ファイルの一括削除";
        public const int MaxListed = 50000;

        /// <summary>このフォルダで使えない理由（使える = null）。ドライブのいちばん上・Windows のフォルダ・このアプリのフォルダなど</summary>
        public static string CheckFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder.Trim().Length == 0) return "フォルダを指定してください。";
            string f;
            try { f = Path.GetFullPath(PathUtil.Plain(folder.Trim())).TrimEnd('\\'); }
            catch (Exception) { return "フォルダの指定が正しくありません。"; }
            if (!Directory.Exists(PathUtil.Long(f))) return "フォルダが見つかりません：" + f;
            string root = Path.GetPathRoot(f + "\\").TrimEnd('\\');
            if (string.Equals(f, root, StringComparison.OrdinalIgnoreCase)) return "ドライブ全体（" + f + "\\）は指定できません。その中のフォルダを指定してください。";
            if (f.StartsWith(@"\\") && f.Substring(2).Split('\\').Length <= 2) return "共有フォルダのいちばん上（" + f + "）は指定できません。その中のフォルダを指定してください。";
            List<string> deny = new List<string>();
            foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
            {
                try { string p = Environment.GetFolderPath(sf); if (!string.IsNullOrEmpty(p)) deny.Add(p.TrimEnd('\\')); } catch (Exception) { }
            }
            string temp = null;
            try { temp = Path.GetTempPath().TrimEnd('\\'); } catch (Exception) { }
            bool inTemp = temp != null && CloudFiles.IsUnder(f, temp) && !string.Equals(f, temp, StringComparison.OrdinalIgnoreCase);   // 一時ファイルのフォルダの中は使える
            foreach (string d in deny)
                if (CloudFiles.IsUnder(f, d) && !inTemp) return "Windows・アプリのフォルダ（" + d + "）の中は指定できません。";
            foreach (string mine in new string[] { AppPaths.Local, AppPaths.Roaming, AppPaths.ExeDir })
                if (CloudFiles.IsUnder(f, mine) || CloudFiles.IsUnder(mine, f)) return "このアプリのフォルダ（" + mine + "）を含む場所は指定できません。";
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
                if (string.Equals(f, profile, StringComparison.OrdinalIgnoreCase)) return "ユーザーのフォルダ全体（" + f + "）は指定できません。その中のフォルダ（ドキュメントなど）を指定してください。";
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>ごみ箱に移せる場所か（このPCの固定ドライブ）。ネットワーク・USBメモリなどは、ごみ箱に入らず完全に削除になる</summary>
        public static bool CanRecycle(string folder)
        {
            try
            {
                string f = PathUtil.Plain(folder);
                if (f.StartsWith(@"\\")) return false;
                DriveInfo d = new DriveInfo(Path.GetPathRoot(f));
                return d.DriveType == DriveType.Fixed;
            }
            catch (Exception) { return false; }
        }

        /// <summary>ジョブのコピー元・コピー先に関係するときの注意（なし = 空）</summary>
        public static List<string> JobNotes(string folder)
        {
            List<string> notes = new List<string>();
            foreach (JobConfig j in AppCore.Settings.Jobs.ToArray())
            {
                string src = null, dst = null;
                try { src = CopyEngine.ResolvedSource(j, AppCore.Settings); } catch (Exception) { }
                try { dst = CopyEngine.ResolvedDest(j, AppCore.Settings); } catch (Exception) { }
                bool inSrc = src != null && (CloudFiles.IsUnder(folder, src) || CloudFiles.IsUnder(src, folder));
                bool inDst = dst != null && (CloudFiles.IsUnder(folder, dst) || CloudFiles.IsUnder(dst, folder));
                if (inSrc)
                    notes.Add("ジョブ「" + j.Name + "」のコピー元です。" + (j.Mirror ? "ミラーのため、次のコピーでコピー先からも削除されます（コピー先の「_AutoCopy\\削除済み」に残ります）。"
                        : j.Sync ? "同期のため、削除は他のPCにも伝わることがあります（確認の画面が出ます）。" : "コピー先にあるファイルはそのまま残ります。"));
                if (inDst)
                    notes.Add("ジョブ「" + j.Name + "」のコピー先です。コピー元に残っているファイルは、次のコピーでまたコピーされます。");
            }
            return notes;
        }

        /// <summary>条件に合うファイルを探す（削除はしない）。_AutoCopy（このアプリの記録）の中は対象外</summary>
        public static List<BulkDeleteItem> Find(string folder, bool sub, BulkDeleteCriteria c, CancellationToken ct, out int scanned, out int failedDirs)
        {
            ScanResult sc = Scanner.Scan(folder, sub, null, true, ct);
            scanned = sc.Files.Count;
            failedDirs = sc.FailedDirs.Count;
            List<BulkDeleteItem> r = new List<BulkDeleteItem>();
            foreach (KeyValuePair<string, ScanItem> kv in sc.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (kv.Key.EndsWith(".ckpart", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith(".cklock", StringComparison.OrdinalIgnoreCase)) continue;   // コピー中の一時ファイル
                if (!c.Matches(kv.Key, kv.Value.Size, kv.Value.Mtime, kv.Value.Attr)) continue;
                BulkDeleteItem it = new BulkDeleteItem();
                it.Rel = kv.Key; it.Size = kv.Value.Size; it.Mtime = kv.Value.Mtime;
                r.Add(it);
            }
            r.Sort(delegate(BulkDeleteItem a, BulkDeleteItem b) { return string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase); });
            return r;
        }

        /// <summary>探したときから変わっていないか（なくなった・大きさや更新日時が違う → 削除しない）</summary>
        public static bool Unchanged(string full, BulkDeleteItem it)
        {
            try
            {
                FileInfo fi = new FileInfo(PathUtil.Long(full));
                if (!fi.Exists) return false;
                if ((fi.Attributes & FileAttributes.System) != 0) return false;
                return fi.Length == it.Size && Math.Abs(fi.LastWriteTimeUtc.Ticks - it.Mtime) < TimeSpan.TicksPerSecond * 2;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 削除する。recycle = true：ごみ箱へ（入らない大きさのときは Windows が確かめる）。false：完全に削除。
        /// 1件ずつ、探したときから変わっていないか確かめ、変わっていれば削除しない。
        /// </summary>
        public static BulkDeleteResult Delete(string folder, List<BulkDeleteItem> items, bool recycle, CancellationToken ct, BusyProgress progress)
        {
            BulkDeleteResult res = new BulkDeleteResult();
            string mode = recycle ? "ごみ箱へ" : "完全に削除";
            List<KeyValuePair<string, BulkDeleteItem>> batch = new List<KeyValuePair<string, BulkDeleteItem>>();
            int done = 0;
            Action flush = delegate
            {
                if (batch.Count == 0) return;
                List<string> paths = new List<string>();
                foreach (KeyValuePair<string, BulkDeleteItem> b in batch) paths.Add(b.Key);
                bool aborted;
                int rc = RecycleFiles(paths, out aborted);
                foreach (KeyValuePair<string, BulkDeleteItem> b in batch)
                {
                    if (!File.Exists(PathUtil.Long(b.Key)))
                    {
                        res.Deleted++; res.DeletedBytes += b.Value.Size;
                        Logger.Write(LogJobName, "DELETE", b.Value.Rel + "（" + mode + "・" + folder + "）", b.Value.Size, -1);
                    }
                    else res.Errors.Add(b.Value.Rel + "：削除できませんでした" + (aborted ? "（取り消しました）" : rc != 0 ? "（コード " + rc + "）" : ""));
                }
                batch.Clear();
                if (aborted) res.Canceled = true;
            };
            foreach (BulkDeleteItem it in items)
            {
                if (ct.IsCancellationRequested || res.Canceled) { res.Canceled = true; break; }
                done++;
                if (progress != null && (done & 31) == 0) progress.Text = "削除しています（" + Fmt.Count(done) + " / " + Fmt.Count(items.Count) + "件）";
                string full = PathUtil.Combine(folder, it.Rel);
                if (!Unchanged(full, it)) { res.Changed.Add(it.Rel); continue; }
                if (recycle)
                {
                    if (PathUtil.Plain(full).Length >= 259) { res.Errors.Add(it.Rel + "：場所（パス）が長すぎるため、ごみ箱に移せません（削除していません）"); continue; }
                    batch.Add(new KeyValuePair<string, BulkDeleteItem>(PathUtil.Plain(full), it));
                    if (batch.Count >= 200) flush();
                }
                else
                {
                    try
                    {
                        File.Delete(PathUtil.Long(full));
                        res.Deleted++; res.DeletedBytes += it.Size;
                        Logger.Write(LogJobName, "DELETE", it.Rel + "（" + mode + "・" + folder + "）", it.Size, -1);
                    }
                    catch (Exception ex) { res.Errors.Add(it.Rel + "：" + ex.Message); }
                }
            }
            if (!res.Canceled) flush();
            return res;
        }

        // ---------- ごみ箱（SHFileOperation）。STA のスレッドで呼ぶ ----------

        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT64
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
            public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        private struct SHFILEOPSTRUCT32
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
            public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")] private static extern int SHFileOperation64(ref SHFILEOPSTRUCT64 op);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")] private static extern int SHFileOperation32(ref SHFILEOPSTRUCT32 op);

        /// <summary>ごみ箱へ移す（入らない大きさなどで完全に削除になるときは、Windows が確かめる画面を出す）</summary>
        public static int RecycleFiles(List<string> paths, out bool aborted)
        {
            string from = string.Join("\0", paths.ToArray()) + "\0\0";
            ushort flags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING);
            int rc = -1;
            bool ab = false;
            Thread t = new Thread(delegate()
            {
                try
                {
                    if (IntPtr.Size == 8)
                    {
                        SHFILEOPSTRUCT64 op = new SHFILEOPSTRUCT64(); op.wFunc = FO_DELETE; op.pFrom = from; op.fFlags = flags;
                        rc = SHFileOperation64(ref op); ab = op.fAnyOperationsAborted;
                    }
                    else
                    {
                        SHFILEOPSTRUCT32 op = new SHFILEOPSTRUCT32(); op.wFunc = FO_DELETE; op.pFrom = from; op.fFlags = flags;
                        rc = SHFileOperation32(ref op); ab = op.fAnyOperationsAborted;
                    }
                }
                catch (Exception) { rc = -1; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            aborted = ab;
            return rc;
        }
    }

    /// <summary>ファイルの一括削除の画面</summary>
    public class BulkDeleteForm : Form
    {
        private readonly TextBox tFolder, tExt, tName;
        private readonly CheckBox cSub, cMin, cMax, cOlder, cHidden;
        private readonly NumericUpDown nMin, nMax;
        private readonly ComboBox cbMinU, cbMaxU;
        private readonly DateTimePicker dOlder;
        private readonly ListView lv;
        private readonly Label lSummary;
        private readonly Button bDelete;
        private readonly ReasonTips reasons = new ReasonTips();
        private string foundFolder;        // 探したときのフォルダ（条件を変えたら null）
        private int foundTotal, foundScanned;
        private bool stale = true, filling;
        private static readonly string[] Units = { "KB", "MB", "GB" };

        public BulkDeleteForm(string folder)
        {
            Ui.SetupForm(this, "ファイルの一括削除", 1000, 680);
            tFolder = Ui.T(folder ?? "", 440);
            cSub = Ui.C("サブフォルダの中も", true);
            tExt = Ui.T("", 220);
            tName = Ui.T("", 260);
            cMin = Ui.C("", false); nMin = Ui.N(0, 999999, 1, 80); cbMinU = Ui.Combo(64, Units); cbMinU.SelectedIndex = 1;
            cMax = Ui.C("", false); nMax = Ui.N(0, 999999, 100, 80); cbMaxU = Ui.Combo(64, Units); cbMaxU.SelectedIndex = 0;
            cOlder = Ui.C("", false); dOlder = Ui.DateBox(DateTime.Today.AddYears(-1));
            cHidden = Ui.C("隠しファイルも対象にする（システムファイルは対象にしません）", false);
            Button search = Btn.MakePrimary(Ui.B("探す（まだ削除しません）", delegate { Search(); }));

            FlowLayoutPanel top = Ui.Column(
                Ui.Heading("ファイルの一括削除（条件に合うファイルを探して、確かめてから削除）"),
                Ui.Hint("ジョブとは関係なく、指定したフォルダの中のファイルを削除します。「探す」で一覧を出し、チェックしたものだけを削除します。このPCのドライブでは「ごみ箱」に移すので、元に戻せます。"),
                Ui.WrapRow(Ui.L("フォルダ", 90), tFolder, Ui.B("参照…", delegate { string p = Ui.BrowseFolder(this, tFolder.Text, "ファイルを削除するフォルダを選んでください"); if (p != null) tFolder.Text = p; }), cSub),
                Ui.WrapRow(Ui.L("拡張子", 90), tExt, Ui.Hint("例：.tmp .bak（空白・カンマで区切る。空欄 = 条件にしない）")),
                Ui.WrapRow(Ui.L("ファイル名", 90), tName, Ui.Hint("例：*~*.xlsx; Thumbs.db（* = 何文字でも、? = 1文字。; で区切る。空欄 = 条件にしない）")),
                Ui.WrapRow(Ui.L("大きさ", 90), cMin, nMin, cbMinU, Ui.L("以上"), Ui.L("　"), cMax, nMax, cbMaxU, Ui.L("以下")),
                Ui.WrapRow(Ui.L("更新日時", 90), cOlder, dOlder, Ui.L("より前（この日を含まない）")),
                Ui.WrapRow(Ui.L("", 90), cHidden),
                Ui.WrapRow(Ui.L("", 90), search, Ui.Hint("条件はすべて満たすものが対象です。少なくとも1つ指定してください。")));
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(12, 10, 12, 6);

            lv = Ui.List("ファイル（フォルダの中の場所）|460", "サイズ|90", "更新日時|140", "種類|80");
            lv.CheckBoxes = true;
            lv.Dock = DockStyle.Fill;
            Ui.AddLocationMenu(lv, delegate(ListViewItem x)
            {
                BulkDeleteItem it = x.Tag as BulkDeleteItem;
                return it == null || foundFolder == null ? null : new string[] { PathUtil.Combine(foundFolder, it.Rel) };
            });
            bool pendingSum = false;
            lv.ItemChecked += delegate
            {
                if (filling || pendingSum || !IsHandleCreated) return;
                pendingSum = true;
                try { BeginInvoke(new Action(delegate() { pendingSum = false; UpdateSummary(); })); } catch (Exception) { pendingSum = false; }
            };
            Panel checkBar = Ui.CheckAllBar(lv, "すべてにチェック／すべて外す", null);

            lSummary = Ui.L("条件を決めて「探す」を押してください。");
            lSummary.Padding = new Padding(12, 6, 12, 6);
            lSummary.Dock = DockStyle.Bottom;
            lSummary.AutoSize = true;

            bDelete = Btn.MakeDanger(Ui.B("チェックしたファイルを削除…", delegate { DeleteChecked(); }));
            reasons.Set(bDelete, delegate { return DeleteReason(); });

            Controls.Add(lv);
            Controls.Add(checkBar);
            Controls.Add(top);
            Controls.Add(lSummary);
            Controls.Add(Ui.WithHelp(Ui.Footer(Ui.B("閉じる", delegate { Close(); }), bDelete), Help.BulkDelete));

            // 条件を変えたら、前に探した一覧は使わない（違う条件で削除しないように）
            EventHandler changed = delegate { MarkStale(); };
            foreach (Control c in new Control[] { tFolder, tExt, tName, cSub, cMin, cMax, cOlder, cHidden, nMin, nMax, cbMinU, cbMaxU, dOlder })
            {
                if (c is CheckBox) ((CheckBox)c).CheckedChanged += changed;
                else if (c is NumericUpDown) ((NumericUpDown)c).ValueChanged += changed;
                else if (c is ComboBox) ((ComboBox)c).SelectedIndexChanged += changed;
                else if (c is DateTimePicker) ((DateTimePicker)c).ValueChanged += changed;
                else c.TextChanged += changed;
            }
            EventHandler en = delegate { nMin.Enabled = cbMinU.Enabled = cMin.Checked; nMax.Enabled = cbMaxU.Enabled = cMax.Checked; dOlder.Enabled = cOlder.Checked; };
            cMin.CheckedChanged += en; cMax.CheckedChanged += en; cOlder.CheckedChanged += en;
            en(null, EventArgs.Empty);
            reasons.Set(nMin, "左のチェックを入れると使います");
            reasons.Set(nMax, "左のチェックを入れると使います");
            reasons.Set(dOlder, "左のチェックを入れると使います");
            UpdateSummary();
        }

        private static long ToBytes(decimal v, int unit) { return (long)v * (unit == 0 ? 1024L : unit == 1 ? 1024L * 1024 : 1024L * 1024 * 1024); }

        /// <summary>画面の条件（テスト用に公開）</summary>
        public BulkDeleteCriteria Criteria()
        {
            BulkDeleteCriteria c = new BulkDeleteCriteria();
            c.Extensions = BulkDeleteCriteria.ParseExtensions(tExt.Text);
            c.NamePatterns = BulkDeleteCriteria.ParseNames(tName.Text);
            if (cMin.Checked) c.MinBytes = ToBytes(nMin.Value, cbMinU.SelectedIndex);
            if (cMax.Checked) c.MaxBytes = ToBytes(nMax.Value, cbMaxU.SelectedIndex);
            if (cOlder.Checked) c.OlderThanUtc = dOlder.Value.Date.ToUniversalTime().Ticks;
            c.IncludeHidden = cHidden.Checked;
            return c;
        }

        /// <summary>テスト用：画面の入力を決める</summary>
        public void SetInputs(string folder, string ext, string names)
        {
            tFolder.Text = folder; tExt.Text = ext ?? ""; tName.Text = names ?? "";
        }

        public ListView List { get { return lv; } }
        public Button DeleteButton { get { return bDelete; } }

        private void MarkStale()
        {
            if (stale && lv.Items.Count == 0) return;
            stale = true;
            foundFolder = null;
            lv.Items.Clear();
            UpdateSummary();
        }

        public string DeleteReason()
        {
            if (stale || foundFolder == null) return "先に「探す」で、削除するファイルの一覧を出してください（条件を変えたときも、もう一度）";
            if (lv.CheckedItems.Count == 0) return "削除するファイルにチェックを入れてください";
            return null;
        }

        private void UpdateSummary()
        {
            if (stale || foundFolder == null)
            {
                lSummary.Text = lv.Items.Count == 0 && foundScanned == 0 ? "条件を決めて「探す」を押してください。" : "条件を変えました。もう一度「探す」を押してください。";
                lSummary.ForeColor = Ui.Quiet;
            }
            else
            {
                int n = 0; long b = 0;
                foreach (ListViewItem it in lv.CheckedItems) { n++; b += ((BulkDeleteItem)it.Tag).Size; }
                lSummary.Text = "条件に合うファイル：" + Fmt.Count(foundTotal) + "件（" + Fmt.Count(foundScanned) + "件の中から）"
                    + (foundTotal > lv.Items.Count ? "　多いため、はじめの " + Fmt.Count(lv.Items.Count) + "件だけ表示しています（表示したものだけ削除できます）" : "")
                    + "　｜　チェックしたもの：" + Fmt.Count(n) + "件・" + Fmt.Size(b);
                lSummary.ForeColor = n > 0 ? Ui.Err : SystemColors.ControlText;
            }
            bDelete.Enabled = DeleteReason() == null;
        }

        /// <summary>条件に合うファイルを探して一覧に出す（削除はしない）</summary>
        public void Search()
        {
            string folder = tFolder.Text.Trim();
            string why = BulkDelete.CheckFolder(folder);
            if (why != null) { Ui.Info(this, why); return; }
            folder = Path.GetFullPath(PathUtil.Plain(folder)).TrimEnd('\\');
            BulkDeleteCriteria c = Criteria();
            if (!c.HasCondition) { Ui.Info(this, "条件（拡張子・ファイル名・大きさ・更新日時）を、少なくとも1つ指定してください。\n（フォルダの中のファイルをすべて削除することはできません）"); return; }
            if (c.MinBytes >= 0 && c.MaxBytes >= 0 && c.MinBytes > c.MaxBytes) { Ui.Info(this, "大きさの「以上」が「以下」より大きくなっています。"); return; }
            bool sub = cSub.Checked;
            int scanned = 0, failed = 0;
            bool stopped;
            List<BulkDeleteItem> found = Ui.BusyCancel<List<BulkDeleteItem>>(this, "条件に合うファイルを探しています（削除はしません）…", delegate(CancellationToken ct, BusyProgress p)
            {
                return BulkDelete.Find(folder, sub, c, ct, out scanned, out failed);
            }, out stopped);
            if (stopped || found == null) return;
            Fill(folder, found, scanned);
            if (failed > 0) Ui.Info(this, "読めなかったフォルダが " + failed + "個ありました（その中は探していません）。");
        }

        /// <summary>一覧に出す（テストからも使う）</summary>
        public void Fill(string folder, List<BulkDeleteItem> found, int scanned)
        {
            filling = true;
            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                List<ListViewItem> add = new List<ListViewItem>();
                foreach (BulkDeleteItem it in found)
                {
                    if (add.Count >= BulkDelete.MaxListed) break;
                    ListViewItem x = new ListViewItem(it.Rel);
                    x.SubItems.Add(Fmt.Size(it.Size)); x.SubItems[1].Tag = it.Size;
                    x.SubItems.Add(Fmt.LocalTime(it.Mtime)); x.SubItems[2].Tag = it.Mtime;
                    string ext = Path.GetExtension(it.Rel);
                    x.SubItems.Add(ext.Length > 0 ? ext.ToLowerInvariant() : "（なし）");
                    x.Checked = true;
                    x.Tag = it;
                    add.Add(x);
                }
                lv.Items.AddRange(add.ToArray());
            }
            finally { lv.EndUpdate(); filling = false; }
            foundFolder = folder;
            foundTotal = found.Count;
            foundScanned = scanned;
            stale = false;
            UpdateSummary();
        }

        private void DeleteChecked()
        {
            string why = DeleteReason();
            if (why != null) { Ui.Info(this, why); return; }
            List<BulkDeleteItem> items = new List<BulkDeleteItem>();
            long bytes = 0;
            foreach (ListViewItem x in lv.CheckedItems) { BulkDeleteItem it = (BulkDeleteItem)x.Tag; items.Add(it); bytes += it.Size; }
            string folder = foundFolder;
            bool recycle = BulkDelete.CanRecycle(folder);
            StringBuilder sb = new StringBuilder();
            sb.Append("チェックした " + Fmt.Count(items.Count) + "件（" + Fmt.Size(bytes) + "）を削除します。\n\n");
            sb.Append("フォルダ：" + folder + "\n条件：" + Criteria().Describe() + "\n");
            for (int i = 0; i < items.Count && i < 5; i++) sb.Append("・" + items[i].Rel + "\n");
            if (items.Count > 5) sb.Append("　ほか " + Fmt.Count(items.Count - 5) + "件\n");
            sb.Append("\n");
            sb.Append(recycle ? "削除したファイルは「ごみ箱」に移します（ごみ箱から元に戻せます）。\nごみ箱に入らない大きさのファイルがあるときは、Windows が確かめます。"
                              : "【注意】この場所（ネットワーク・USBメモリなど）は、ごみ箱に入らず完全に削除されます。元に戻せません。");
            foreach (string n in BulkDelete.JobNotes(folder)) sb.Append("\n\n" + n);
            sb.Append("\n\nよろしいですか？");
            if (!Ui.ConfirmDanger(this, sb.ToString())) return;
            if (!recycle && !Ui.ConfirmDanger(this, "本当に完全に削除しますか？（" + Fmt.Count(items.Count) + "件・" + Fmt.Size(bytes) + "・元に戻せません）")) return;

            bool stopped;
            BulkDeleteResult r = Ui.BusyCancel<BulkDeleteResult>(this, "削除しています…", delegate(CancellationToken ct, BusyProgress p)
            {
                return BulkDelete.Delete(folder, items, recycle, ct, p);
            }, out stopped);
            if (r == null) r = new BulkDeleteResult();
            // 削除したものを一覧から消す（残ったものは残す）
            filling = true;
            lv.BeginUpdate();
            try
            {
                for (int i = lv.Items.Count - 1; i >= 0; i--)
                {
                    BulkDeleteItem it = (BulkDeleteItem)lv.Items[i].Tag;
                    if (!File.Exists(PathUtil.Long(PathUtil.Combine(folder, it.Rel)))) lv.Items.RemoveAt(i);
                }
            }
            finally { lv.EndUpdate(); filling = false; }
            foundTotal = lv.Items.Count;
            UpdateSummary();
            string msg = Fmt.Count(r.Deleted) + "件（" + Fmt.Size(r.DeletedBytes) + "）を" + (recycle ? "ごみ箱に移しました。" : "削除しました。")
                + (stopped || r.Canceled ? "\n途中で中止しました。" : "")
                + (r.Changed.Count > 0 ? "\n探した後に変わった・なくなったため削除しなかったもの：" + r.Changed.Count + "件" : "")
                + (r.Errors.Count > 0 ? "\n削除できなかったもの：" + r.Errors.Count + "件\n" + string.Join("\n", r.Errors.GetRange(0, Math.Min(10, r.Errors.Count)).ToArray()) : "");
            if (r.Errors.Count > 0) Ui.Error(this, msg); else Ui.Info(this, msg);
        }
    }
}
