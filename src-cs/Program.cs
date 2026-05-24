using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            EnableDpiAwareness();

            CliOptions options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine("FH6SkillPointOcr.exe [--config path] [--dry-run] [--no-overlay] [--mode normal|debug|reset] [--task skill|delete|fullauto|blueprint-test] [--handoff] [--skill-points n] [--skill-points-state-file path] [--safe-stop-file path]");
                return 0;
            }

            try
            {
                Config config = Config.Load(options.ConfigPath);
                if (options.NoOverlay)
                {
                    config.OverlayEnabled = false;
                }

                RunMode mode = ChooseRunMode(options.Mode);
                while (mode == RunMode.ResetSettings)
                {
                    UserSettings.Reset(config);
                    mode = ChooseRunMode(null);
                }

                UserSettings.LoadOrCreate(config);
                AutomationTask task = options.Task.HasValue ? options.Task.Value : GuessTaskFromExecutableName();
                int skillPoints = task == AutomationTask.FullAuto ? AskSkillPointTotal(options) : (task == AutomationTask.SkillPoints ? AskSkillPointTotal(options) : int.MaxValue);
                bool stepDebug = mode == RunMode.Debug;
                bool useFullManufacturerFlow = task == AutomationTask.SkillPoints || (task == AutomationTask.DeleteVehicles && options.ReuseVehicleListState);
                VirtualListLoadMode listLoadMode = ResolveVirtualListLoadMode(task, options);
                if (task == AutomationTask.DeleteVehicles)
                {
                    Console.WriteLine(options.ReuseVehicleListState
                        ? "[STARTUP] 删除车辆：衔接启动，完整复用旧虚拟列表信息。"
                        : "[STARTUP] 删除车辆：独立启动，直接从当前车辆列表 OCR 建表；只有 --handoff 才衔接。");
                }
                Runtime runtime = new Runtime(config, options.DryRun, stepDebug, skillPoints, task, useFullManufacturerFlow, listLoadMode, options.ReuseVehicleListState, options.SafeStopFile, options.SkillPointsStateFile);
                runtime.Run();
                return 0;
            }
            catch (StopRequestedException)
            {
                Console.WriteLine("[EXIT] Space+C");
                return 0;
            }
            catch (CompletedException ex)
            {
                Console.WriteLine("[DONE] " + ex.Message);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                WriteErrorLog(ex);
                return 1;
            }
        }

        private static AutomationTask GuessTaskFromExecutableName()
        {
            string name = Path.GetFileNameWithoutExtension(Application.ExecutablePath) ?? "";
            if (name.IndexOf("FullAuto", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("AutoFlow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AutomationTask.FullAuto;
            }
            if (name.IndexOf("BlueprintCycleTest", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("BlueprintTest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AutomationTask.BlueprintCycleTest;
            }
            if (name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0) return AutomationTask.DeleteVehicles;
            return AutomationTask.SkillPoints;
        }

        private static VirtualListLoadMode ResolveVirtualListLoadMode(AutomationTask task, CliOptions options)
        {
            if (task != AutomationTask.DeleteVehicles) return VirtualListLoadMode.None;
            return options.ReuseVehicleListState ? VirtualListLoadMode.FullState : VirtualListLoadMode.None;
        }

        private static int AskSkillPointTotal(CliOptions options)
        {
            if (options.SkillPoints.HasValue)
            {
                Console.WriteLine("[STARTUP] 使用命令行传入的技术点总数：" + options.SkillPoints.Value);
                return options.SkillPoints.Value;
            }
            if (options.ReuseVehicleListState)
            {
                Console.WriteLine("[STARTUP] 点技能衔接启动：技术点默认 " + FH6AutomationConstants.SkillPoints.Max + "，不读取旧虚拟列表内部状态。");
                return FH6AutomationConstants.SkillPoints.Max;
            }
            while (true)
            {
                Console.Write("请输入当前技术点总数（临时值，关闭程序后不保存，直接回车默认 " + FH6AutomationConstants.SkillPoints.Max + "）：");
                string input = (Console.ReadLine() ?? "").Trim();
                if (input.Length == 0) return FH6AutomationConstants.SkillPoints.Max;
                int value;
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
                {
                    return value;
                }
                Console.WriteLine("输入无效，请输入 0 或正整数。");
            }
        }

        private static RunMode ChooseRunMode(string mode)
        {
            if (mode == "normal") return RunMode.Normal;
            if (mode == "debug") return RunMode.Debug;
            if (mode == "reset") return RunMode.ResetSettings;

            while (true)
            {
                Console.WriteLine("请选择运行模式：");
                Console.WriteLine("1. 正常模式：自动连续运行");
                Console.WriteLine("2. 调试模式：每一步按 · 键继续");
                Console.WriteLine("3. 重设设置：重新输入行列、框选完整可见车辆格子区域");
                Console.Write("输入 1、2 或 3 后回车，直接回车默认正常模式：");
                string choice = (Console.ReadLine() ?? "").Trim();
                if (choice.Length == 0 || choice == "1") return RunMode.Normal;
                if (choice == "2") return RunMode.Debug;
                if (choice == "3") return RunMode.ResetSettings;
                Console.WriteLine("输入无效，请输入 1、2 或 3。");
            }
        }

        private static void WriteErrorLog(Exception ex)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string baseDir = exeDir;
                DirectoryInfo parent = Directory.GetParent(exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent != null && string.Equals(Path.GetFileName(exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "bin", StringComparison.OrdinalIgnoreCase))
                {
                    baseDir = parent.FullName;
                }

                string debugDir = Path.Combine(baseDir, "debug");
                Directory.CreateDirectory(debugDir);
                string body = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\r\n" + ex + "\r\n";
                File.WriteAllText(Path.Combine(debugDir, "last-error.txt"), body, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private enum RunMode
        {
            Normal,
            Debug,
            ResetSettings
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    }

}
