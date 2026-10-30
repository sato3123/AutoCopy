using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace AutoCopy
{
    public static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AutoCopy";

        public static void Set(bool enabled)
        {
            Set(enabled, AppPaths.ExePath);
        }

        public static void Set(bool enabled, string exePath)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, "\"" + exePath + "\" /startup");
                else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
            }
        }

        public static bool IsSet()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return k != null && k.GetValue(ValueName) != null;
            }
        }
    }

    public static class Shortcuts
    {
        /// <summary>デスクトップにショートカットを作る（WScript.Shell を使用。Windows 7〜11 共通）</summary>
        public static string CreateOnDesktop(string title, string arguments)
        {
            return CreateOnDesktop(title, arguments, AppPaths.ExePath);
        }

        public static string CreateOnDesktop(string title, string arguments, string exePath)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string safe = title;
            foreach (char c in PathUtil.InvalidNameChars) safe = safe.Replace(c, '_');
            string path = Path.Combine(desktop, safe + ".lnk");
            // Windows 標準の IShellLinkW を使う（日本語などの Unicode のファイル名でも作れる。Windows 7〜11 共通）
            object obj = new ShellLinkObject();
            try
            {
                IShellLinkW link = (IShellLinkW)obj;
                link.SetPath(exePath);
                link.SetArguments(arguments ?? "");
                link.SetWorkingDirectory(Path.GetDirectoryName(exePath));
                link.SetDescription("自動コピー");
                link.SetIconLocation(exePath, 0);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)obj).Save(path, true);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(obj);
            }
            return path;
        }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkObject { }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        [System.Runtime.InteropServices.Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszFile);
        }
    }

    public static class Installer
    {
        /// <summary>配布フォルダ（USB等）から起動された場合、%LOCALAPPDATA%\AutoCopy\bin にコピーする。コピー後の exe パス。</summary>
        public static string InstallToBin()
        {
            string bin = AppPaths.BinDir;
            if (string.Equals(PathUtil.Normalize(AppPaths.ExeDir), PathUtil.Normalize(bin), StringComparison.OrdinalIgnoreCase))
                return AppPaths.ExePath;
            Directory.CreateDirectory(bin);
            string target = Path.Combine(bin, "AutoCopy.exe");
            File.Copy(AppPaths.ExePath, target, true);
            string cfg = AppPaths.ExePath + ".config";
            if (File.Exists(cfg)) File.Copy(cfg, target + ".config", true);
            string manual = Path.Combine(AppPaths.ExeDir, AppPaths.ManualFileName);
            if (File.Exists(manual)) File.Copy(manual, Path.Combine(bin, AppPaths.ManualFileName), true);
            // Windows 7 用の .NET Framework インストーラーがあれば、配布フォルダ作成用に一緒に置いておく
            foreach (string f in Directory.GetFiles(AppPaths.ExeDir, "ndp48*.exe"))
            {
                try { File.Copy(f, Path.Combine(bin, Path.GetFileName(f)), true); } catch (Exception) { }
            }
            return target;
        }
    }

    /// <summary>二重起動の防止と、2つ目の起動からの指示の受け渡し</summary>
    public static class SingleInstance
    {
        private static Mutex mutex;
        private static string PipeName { get { return "AutoCopy_" + Environment.UserDomainName + "_" + Environment.UserName; } }

        public static bool TryAcquire()
        {
            bool created;
            mutex = new Mutex(true, @"Local\AutoCopy_" + Environment.UserName, out created);
            return created;
        }

        public static void Release()
        {
            try { if (mutex != null) { mutex.ReleaseMutex(); mutex.Dispose(); mutex = null; } } catch (Exception) { }
        }

        public static bool SendToRunning(string command)
        {
            try
            {
                using (NamedPipeClientStream c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(3000);
                    byte[] b = Encoding.UTF8.GetBytes(command);
                    c.Write(b, 0, b.Length);
                    c.Flush();
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void StartServer(Action<string> onCommand)
        {
            Thread t = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        using (NamedPipeServerStream s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1))
                        {
                            s.WaitForConnection();
                            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            {
                                string cmd = r.ReadToEnd();
                                if (!string.IsNullOrEmpty(cmd)) onCommand(cmd.Trim());
                            }
                        }
                    }
                    catch (Exception) { Thread.Sleep(1000); }
                }
            });
            t.IsBackground = true;
            t.Name = "AutoCopyPipe";
            t.Start();
        }
    }
}
