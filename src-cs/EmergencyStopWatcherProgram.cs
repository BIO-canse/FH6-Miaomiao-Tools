using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class EmergencyStopWatcherLauncher
    {
        private const string ActiveEnv = "FH6_EMERGENCY_STOP_WATCHER_ACTIVE";

        public static void Start(string baseDir)
        {
            if (string.Equals(Environment.GetEnvironmentVariable(ActiveEnv), "1", StringComparison.Ordinal)) return;

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string watcherPath = Path.Combine(exeDir, FH6AutomationConstants.Files.EmergencyStopWatcherExe);
            if (!File.Exists(watcherPath))
            {
                Console.WriteLine("[EMERGENCY_WATCHER] 未找到独立急停进程：" + watcherPath);
                return;
            }

            List<string> args = new List<string>();
            args.Add("--owner");
            args.Add(Process.GetCurrentProcess().Id.ToString());
            AddRootArg(args, baseDir);
            AddRootArg(args, exeDir);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = watcherPath;
            psi.WorkingDirectory = string.IsNullOrWhiteSpace(baseDir) ? exeDir : baseDir;
            psi.Arguments = JoinArgs(args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.EnvironmentVariables[ActiveEnv] = "1";

            Process.Start(psi);
            Environment.SetEnvironmentVariable(ActiveEnv, "1");
            Console.WriteLine("[EMERGENCY_WATCHER] 已启动独立急停进程：" + watcherPath);
        }

        private static void AddRootArg(List<string> args, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            args.Add("--root");
            args.Add(root);
        }

        private static string JoinArgs(List<string> args)
        {
            List<string> quoted = new List<string>();
            foreach (string arg in args)
            {
                quoted.Add("\"" + (arg ?? "").Replace("\"", "\\\"") + "\"");
            }
            return string.Join(" ", quoted.ToArray());
        }
    }

    internal static class EmergencyStopWatcherProgram
    {
        private static int Main(string[] args)
        {
            FH6FailureLog.InstallGlobalHandlers("FH6EmergencyStopWatcher");
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                FH6FailureLog.Write("FH6EmergencyStopWatcher", ex);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            int ownerPid = ParseOwnerPid(args);
            List<string> roots = ParseRoots(args);
            if (roots.Count == 0) roots.Add(AppDomain.CurrentDomain.BaseDirectory);

            while (true)
            {
                if (HotkeyDown())
                {
                    Console.WriteLine("[EMERGENCY_WATCHER] Space+C detected.");
                    EmergencyStop.KillAutomationProcesses(roots.ToArray());
                    return 130;
                }

                if (ownerPid > 0 && !ProcessExists(ownerPid)) return 0;
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
        }

        private static int ParseOwnerPid(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "--owner", StringComparison.OrdinalIgnoreCase)) continue;
                int pid;
                if (int.TryParse(args[i + 1], out pid)) return pid;
            }
            return 0;
        }

        private static List<string> ParseRoots(string[] args)
        {
            List<string> roots = new List<string>();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "--root", StringComparison.OrdinalIgnoreCase)) continue;
                string root = args[i + 1];
                if (string.IsNullOrWhiteSpace(root)) continue;
                roots.Add(root);
            }
            return roots;
        }

        private static bool ProcessExists(int pid)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                try { return !process.HasExited; }
                finally { process.Dispose(); }
            }
            catch
            {
                return false;
            }
        }

        private static bool HotkeyDown()
        {
            return (GetAsyncKeyState(FH6AutomationConstants.Keys.HotkeyModifierVirtualKey) & 0x8000) != 0 &&
                   (GetAsyncKeyState(FH6AutomationConstants.Keys.ExitVirtualKey) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
