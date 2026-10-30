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
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exePath });
                lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { arguments });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(exePath) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "自動コピー" });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exePath + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(lnk);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            return path;
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
