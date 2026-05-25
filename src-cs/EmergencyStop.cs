using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class EmergencyStop
    {
        public static void KillAutomationProcesses(params string[] roots)
        {
            HashSet<string> automationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.SkillPointExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.DeleteVehicleExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.FullAutoExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.BlueprintCycleTestExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.EmergencyStopWatcherExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.UiCacheGuardExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.MinuteLoopExe),
                Path.GetFileNameWithoutExtension(FH6AutomationConstants.Files.BuyLoopExe),
                "FH6BuyPreludeStepDebug"
            };

            List<string> directories = new List<string>();
            foreach (string root in roots)
            {
                AddDirectory(directories, root);
                AddDirectory(directories, Path.Combine(root ?? "", "bin"));
                AddDirectory(directories, Path.Combine(root ?? "", "runtime", "python"));

                string parent = SafeParent(root);
                AddDirectory(directories, parent);
                AddDirectory(directories, Path.Combine(parent ?? "", "bin"));
                AddDirectory(directories, Path.Combine(parent ?? "", "runtime", "python"));
            }

            int currentId = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == currentId || process.HasExited) continue;

                    string path = NormalizePath(SafeProcessPath(process));
                    string name = process.ProcessName;
                    bool isKnownAutomation = automationNames.Contains(name) && IsInsideAny(path, directories);
                    bool isBundledPython = string.Equals(name, "python", StringComparison.OrdinalIgnoreCase) && IsInsideAny(path, directories);

                    if (!isKnownAutomation && !isBundledPython) continue;

                    Console.WriteLine("[EMERGENCY_STOP] kill " + name + " pid=" + process.Id + " path=" + path);
                    process.Kill();
                }
                catch
                {
                }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }

            try
            {
                Console.WriteLine("[EMERGENCY_STOP] kill current pid=" + currentId);
                Process.GetCurrentProcess().Kill();
                Environment.Exit(130);
            }
            catch
            {
                Environment.Exit(130);
            }
        }

        private static void AddDirectory(List<string> directories, string path)
        {
            string normalized = NormalizeDirectory(path);
            if (normalized.Length == 0) return;
            foreach (string existing in directories)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)) return;
            }
            directories.Add(normalized);
        }

        private static string SafeParent(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return "";
                DirectoryInfo parent = Directory.GetParent(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return parent == null ? "" : parent.FullName;
            }
            catch
            {
                return "";
            }
        }

        private static string SafeProcessPath(Process process)
        {
            try
            {
                return process.MainModule == null ? "" : process.MainModule.FileName;
            }
            catch
            {
                return "";
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        }

        private static string NormalizeDirectory(string path)
        {
            string normalized = NormalizePath(path);
            if (normalized.Length == 0) return normalized;
            return normalized + Path.DirectorySeparatorChar;
        }

        private static bool IsInsideAny(string normalizedPath, List<string> normalizedDirectories)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath)) return false;
            foreach (string directory in normalizedDirectories)
            {
                if (normalizedPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
