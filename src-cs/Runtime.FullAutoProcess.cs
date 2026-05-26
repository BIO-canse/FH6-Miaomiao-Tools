using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private Process fullAutoChildProcess;
        private bool fullAutoUserSafeStopRequested;

        private void RunMinuteWLoopProcess(string safeStopFile, string arguments, bool syncSkillPointsAfterExit)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ResolveMinuteWLoopPath();
            psi.WorkingDirectory = config.BaseDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Arguments = arguments;

            FullAutoStageGap("启动刷技术点子程序前预留间隔");
            overlay.HideForCapture(0);
            fullAutoChildProcess = Process.Start(psi);
            overlay.ShowOverlay();
            DateTime minuteLoopEstimateAnchorUtc = DateTime.UtcNow;
            SetStatus(
                "minute loop running",
                MinuteLoopProgressSummary(minuteLoopEstimateAnchorUtc));
            Stopwatch skillPointPoll = Stopwatch.StartNew();

            try
            {
                while (!fullAutoChildProcess.HasExited)
                {
                    PollUiCacheOcrGuards();
                    PollFullAutoHotkeysForChild(safeStopFile);
                    if (syncSkillPointsAfterExit && skillPointPoll.ElapsedMilliseconds >= FH6AutomationConstants.Timing.OneSecondMs)
                    {
                        skillPointPoll.Restart();
                        if (LoadFullAutoSkillPoints("minute loop running", false))
                        {
                            minuteLoopEstimateAnchorUtc = DateTime.UtcNow;
                        }
                        SetStatus("minute loop running", MinuteLoopProgressSummary(minuteLoopEstimateAnchorUtc));
                    }
                    Thread.Sleep(FH6AutomationConstants.Timing.ChildProcessPollMs);
                }

                if (fullAutoUserSafeStopRequested) throw new CompletedException("Space+V 安全结束：子程序已退出，主程序停止。");
                if (fullAutoChildProcess.ExitCode != 0) throw new InvalidOperationException(FH6AutomationConstants.Files.MinuteLoopExe + " 退出码 " + fullAutoChildProcess.ExitCode + ChildLastErrorSummary());
                if (syncSkillPointsAfterExit) LoadFullAutoSkillPoints("minute loop completed");
            }
            catch
            {
                KillChildIfRunning();
                throw;
            }
            finally
            {
                fullAutoChildProcess = null;
                DeleteFileIfExists(safeStopFile);
                overlay.ShowOverlay();
            }

            FullAutoStageGap("刷技术点子程序结束后预留间隔");
        }

        private string MinuteLoopProgressSummary(DateTime estimateAnchorUtc)
        {
            int loops = RemainingMinuteLoopCount();
            int target = FullAutoSkillPointTarget();
            long remainingMs = 0;
            if (loops > 0)
            {
                long totalMs = (long)loops * FH6AutomationConstants.SkillPoints.MinuteLoopEstimatedLoopMs;
                long elapsedMs = Math.Max(0, (long)(DateTime.UtcNow - estimateAnchorUtc).TotalMilliseconds);
                remainingMs = Math.Max(0, totalMs - elapsedMs);
            }

            DateTime finishAt = DateTime.Now.AddMilliseconds(remainingMs);
            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "当前技术点 {0}/{1}；还需 {2} 轮，约 {3}，预计 {4} 刷满；每轮 +{5}",
                remainingSkillPoints,
                target,
                loops,
                FormatDurationCompact(TimeSpan.FromMilliseconds(remainingMs)),
                finishAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                FH6AutomationConstants.SkillPoints.MinuteLoopGain);
            minuteLoopSummary = summary;
            return "刷技术点中；" + summary;
        }

        private int RemainingMinuteLoopCount()
        {
            int missing = FullAutoSkillPointTarget() - remainingSkillPoints;
            if (missing <= 0) return 0;
            return (missing + FH6AutomationConstants.SkillPoints.MinuteLoopGain - 1) / FH6AutomationConstants.SkillPoints.MinuteLoopGain;
        }

        private static string FormatDurationCompact(TimeSpan duration)
        {
            if (duration.TotalSeconds <= 0) return "0秒";
            int totalSeconds = (int)Math.Ceiling(duration.TotalSeconds);
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            if (hours > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}小时{1:00}分{2:00}秒", hours, minutes, seconds);
            }
            if (minutes > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}分{1:00}秒", minutes, seconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}秒", seconds);
        }

        private List<string> BaseChildArgs(string safeStopFileName)
        {
            string safeStopFile = SafeStopPath(safeStopFileName);
            DeleteFileIfExists(safeStopFile);

            List<string> args = new List<string>();
            args.Add("--config");
            args.Add(config.SourcePath);
            args.Add("--mode");
            args.Add("normal");
            args.Add("--safe-stop-file");
            args.Add(safeStopFile);
            return args;
        }

        private void RunChildProcess(string exePath, List<string> args, string label)
        {
            string safeStopFile = ExtractSafeStopFile(args);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.WorkingDirectory = config.BaseDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Arguments = JoinArgs(args);

            SetStatus("run child " + label, Path.GetFileName(exePath) + " " + psi.Arguments);
            FullAutoStageGap("启动子程序 " + label + " 前预留间隔");
            overlay.HideForCapture(0);
            fullAutoChildProcess = Process.Start(psi);

            try
            {
                while (!fullAutoChildProcess.HasExited)
                {
                    PollUiCacheOcrGuards();
                    PollFullAutoHotkeysForChild(safeStopFile);
                    Thread.Sleep(FH6AutomationConstants.Timing.ChildProcessPollMs);
                }

                if (fullAutoUserSafeStopRequested) throw new CompletedException("Space+V 安全结束：子程序已退出，主程序停止。");
                if (fullAutoChildProcess.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(exePath) + " 退出码 " + fullAutoChildProcess.ExitCode + ChildLastErrorSummary());
            }
            catch
            {
                KillChildIfRunning();
                throw;
            }
            finally
            {
                fullAutoChildProcess = null;
                DeleteFileIfExists(safeStopFile);
                overlay.ShowOverlay();
            }

            FullAutoStageGap("子程序 " + label + " 结束后预留间隔");
        }

        private void PollFullAutoHotkeysForChild(string safeStopFile)
        {
            if (input.ShouldStop())
            {
                EmergencyStopAllAutomationProcesses();
                throw new StopRequestedException();
            }

            if (!fullAutoUserSafeStopRequested &&
                IsKeyDown(FH6AutomationConstants.Keys.HotkeyModifierVirtualKey) &&
                IsKeyDown(FH6AutomationConstants.Keys.FullAutoSafeStopVirtualKey))
            {
                fullAutoUserSafeStopRequested = true;
                RequestChildSafeStop(safeStopFile, "Space+V 安全结束：要求当前子程序跑完本轮后退出");
            }
        }

        private void FullAutoSleep(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                PollUiCacheOcrGuards();
                ThrowIfFullAutoSafeStopWithoutChild();
                input.SleepMs(Math.Min(
                    FH6AutomationConstants.Timing.FullAutoSleepSliceMs,
                    Math.Max(1, ms - (int)sw.ElapsedMilliseconds)));
            }
        }

        private string ChildLastErrorSummary()
        {
            try
            {
                string path = Path.Combine(config.ResolvePath(config.DebugDir), "last-error.txt");
                if (!File.Exists(path)) return "";
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Length > 5000) text = text.Substring(0, 5000) + "\r\n...(truncated)";
                return "\r\n\r\n子程序 last-error:\r\n" + text;
            }
            catch (Exception ex)
            {
                return "\r\n\r\n读取子程序 last-error 失败：" + ex.Message;
            }
        }

        private void ThrowIfFullAutoSafeStopWithoutChild()
        {
            if (input.ShouldStop())
            {
                EmergencyStopAllAutomationProcesses();
                throw new StopRequestedException();
            }

            if (IsKeyDown(FH6AutomationConstants.Keys.HotkeyModifierVirtualKey) &&
                IsKeyDown(FH6AutomationConstants.Keys.FullAutoSafeStopVirtualKey))
            {
                fullAutoUserSafeStopRequested = true;
                throw new CompletedException("Space+V 安全结束：当前没有子程序运行，主程序直接停止。");
            }
        }

        private void FullAutoCheckPoint()
        {
            PollUiCacheOcrGuards();
            if (task == AutomationTask.FullAuto) ThrowIfFullAutoSafeStopWithoutChild();
        }

        private void SleepWithFullAutoHotkey(int ms)
        {
            if (task == AutomationTask.FullAuto) FullAutoSleep(ms);
            else input.SleepMs(ms);
        }

        private void FullAutoStageGap(string reason)
        {
            if (task != AutomationTask.FullAuto && task != AutomationTask.BlueprintCycleTest) return;
            SetStage("阶段间隔");
            SetStatus("full auto transition", reason + "，等待 1 秒");
            FullAutoSleep(FH6AutomationConstants.Timing.FullAutoStageGapMs);
        }

        private void RequestChildSafeStop(string safeStopFile, string reason)
        {
            if (string.IsNullOrWhiteSpace(safeStopFile)) return;

            Console.WriteLine("[FULL_AUTO_SAFE_STOP] " + reason);
            Directory.CreateDirectory(Path.GetDirectoryName(safeStopFile));
            File.WriteAllText(safeStopFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Encoding.UTF8);
        }

        private void KillChildIfRunning()
        {
            try
            {
                if (fullAutoChildProcess != null && !fullAutoChildProcess.HasExited) fullAutoChildProcess.Kill();
            }
            catch
            {
            }
        }

        private void EmergencyStopAllAutomationProcesses()
        {
            Console.WriteLine("[EMERGENCY_STOP] Space+C：停止当前运行包内所有自动化进程。");
            KillChildIfRunning();
            EmergencyStop.KillAutomationProcesses(config.BaseDir, AppDomain.CurrentDomain.BaseDirectory);
        }

        private string SafeStopPath(string fileName)
        {
            return Path.Combine(config.BaseDir, "state", fileName);
        }

        private string SkillPointsStatePath()
        {
            return Path.Combine(config.BaseDir, "state", FH6AutomationConstants.Files.SkillPointsState);
        }

        private void PersistFullAutoSkillPoints(string reason)
        {
            try
            {
                string path = SkillPointsStatePath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["schema"] = "fh6_skill_points_state.v1";
                root["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                root["reason"] = reason;
                root["skill_points"] = Math.Max(0, Math.Min(FH6AutomationConstants.SkillPoints.Max, remainingSkillPoints));
                root["super_wheelspins"] = superWheelspinCount;
                root["event_index"] = skillPointEventIndex;
                root["minute_loop_count"] = minuteLoopCompletedCount;
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(root), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FULL_AUTO_SKILL_POINTS] 写入失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.PersistFullAutoSkillPoints", ex);
            }
        }

        private void LoadFullAutoSkillPoints(string reason)
        {
            LoadFullAutoSkillPoints(reason, true);
        }

        private bool LoadFullAutoSkillPoints(string reason, bool updateStatus)
        {
            try
            {
                string path = SkillPointsStatePath();
                if (!File.Exists(path)) return false;

                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(path, Encoding.UTF8));
                object value;
                if (!root.TryGetValue("skill_points", out value)) return false;

                int before = remainingSkillPoints;
                int loaded = Math.Max(0, Math.Min(FH6AutomationConstants.SkillPoints.Max, Convert.ToInt32(value, CultureInfo.InvariantCulture)));
                bool changed = loaded != remainingSkillPoints;
                remainingSkillPoints = loaded;
                superWheelspinCount = ReadInt(root, "super_wheelspins", superWheelspinCount);
                skillPointEventIndex = ReadInt(root, "event_index", skillPointEventIndex);
                minuteLoopCompletedCount = ReadInt(root, "minute_loop_count", minuteLoopCompletedCount);
                if (changed)
                {
                    SetOcrSummary("技术点同步: " + before + " -> " + remainingSkillPoints + "，原因 " + reason);
                }
                if (updateStatus) SetStatus("skill points synced", reason + "，当前技术点 " + remainingSkillPoints);
                return changed;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FULL_AUTO_SKILL_POINTS] 读取失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.LoadFullAutoSkillPoints", ex);
                return false;
            }
        }

        private string ResolveBinPath(string exeName)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
            if (File.Exists(path)) return path;

            path = Path.Combine(config.BaseDir, "bin", exeName);
            if (File.Exists(path)) return path;

            throw new FileNotFoundException("找不到子程序", exeName);
        }

        private string ResolveMinuteWLoopPath()
        {
            return ResolveBinPath(FH6AutomationConstants.Files.MinuteLoopExe);
        }

        private static string ExtractSafeStopFile(List<string> args)
        {
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (args[i] == "--safe-stop-file") return args[i + 1];
            }

            return null;
        }

        private static string JoinArgs(List<string> args)
        {
            return string.Join(" ", args.Select(QuoteArg).ToArray());
        }

        private static string QuoteArg(string arg)
        {
            if (arg == null) return "\"\"";
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        private static void DeleteFileIfExists(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
