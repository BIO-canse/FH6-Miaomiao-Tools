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
    internal sealed partial class Runtime
    {
        private readonly Config config;
        private readonly AutomationTask task;
        private readonly bool useFullManufacturerFlow;
        private readonly VirtualListLoadMode virtualListLoadMode;
        private readonly bool handoffStart;
        private readonly bool stepDebug;
        private readonly string skillPointsStateFile;
        private readonly string skillPointsLogFile;
        private readonly InputController input;
        private readonly ScreenCapture capture;
        private readonly OcrReader ocr;
        private readonly GridGeometry grid;
        private readonly VirtualVehicleList vehicleList;
        private readonly OverlayRenderer overlay;
        private readonly string debugDir;
        private readonly string debugScreenshotDir;
        private readonly string uiClickCachePath;
        private readonly DateTime startedAtLocal = DateTime.Now;
        private int failures;
        private int loopCount;
        private int fullAutoCycleCount;
        private int debugStepCount;
        private int debugScreenshotCounter;
        private int remainingSkillPoints;
        private int superWheelspinCount;
        private int skillPointEventIndex;
        private int minuteLoopCompletedCount;
        private readonly int firstRunSkillPoints;
        private bool deleteSelectionKnown;
        private CellKey deleteSelection = new CellKey(0, 0);
        private readonly Dictionary<string, Point> uiClickCache = new Dictionary<string, Point>();
        private string status = "init";
        private string bigStage = "启动";
        private string nextAction = "等待启动";
        private string actionSequence = "-";
        private string minuteLoopSummary = "-";
        private string lastOcrSummary = "OCR: -";
        private string lastTargetSummary = "目标格: -";
        private List<OcrFieldView> lastOcrFields = new List<OcrFieldView>();
        private bool subaruListBoundaryReached;
        private string subaruListBoundaryReason = "";

        public Runtime(Config config, bool dryRun, bool stepDebug, int initialSkillPoints, AutomationTask task, bool useFullManufacturerFlow, VirtualListLoadMode virtualListLoadMode, bool handoffStart, string safeStopFile, string skillPointsStateFile, string skillPointsLogFile)
        {
            this.config = config;
            this.task = task;
            this.useFullManufacturerFlow = useFullManufacturerFlow;
            this.virtualListLoadMode = virtualListLoadMode;
            this.handoffStart = handoffStart;
            this.stepDebug = stepDebug;
            this.skillPointsStateFile = string.IsNullOrWhiteSpace(skillPointsStateFile) ? null : Path.GetFullPath(skillPointsStateFile);
            remainingSkillPoints = initialSkillPoints;
            firstRunSkillPoints = initialSkillPoints;
            input = new InputController(config.TapMs, config.RepeatIntervalMs, dryRun, safeStopFile);
            capture = new ScreenCapture(config.MonitorIndex);
            grid = new GridGeometry(config);
            debugDir = config.ResolvePath(config.DebugDir);
            Directory.CreateDirectory(debugDir);
            this.skillPointsLogFile = ResolveSkillPointsLogFile(skillPointsLogFile);
            debugScreenshotDir = Path.Combine(debugDir, "screenshots");
            uiClickCachePath = Path.Combine(config.BaseDir, "state", FH6AutomationShared.FH6AutomationConstants.Files.UiClickCache);
            LoadSharedUiClickCacheIfAllowed();
            if (stepDebug) ResetDebugScreenshots();
            ocr = new OcrReader(config, stepDebug ? debugScreenshotDir : null);
            string virtualListLog = Path.Combine(debugDir, "virtual-list-edits-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
            string virtualListPath = config.ResolvePath(config.VirtualListPath);
            vehicleList = new VirtualVehicleList(config.GridRows, virtualListLog, virtualListPath, virtualListLoadMode);
            overlay = new OverlayRenderer(config.OverlayEnabled);
            LoadSkillPointCountersFromState();
            PersistSkillPointsState("init");
        }

        public void Run()
        {
            overlay.Start();
            try
            {
                if (task == AutomationTask.FullAuto)
                {
                    RunFullAutoStartupPreflight();
                }

                WaitForAutomationStart();
                if (task == AutomationTask.FullAuto)
                {
                    RunFullAutoLoop();
                    return;
                }
                if (task == AutomationTask.BlueprintCycleTest)
                {
                    RunBlueprintCycleTest();
                    return;
                }
                if (task == AutomationTask.DeleteVehicles)
                {
                    PrepareDeleteVehicleRun();
                }

                while (true)
                {
                    if (task == AutomationTask.DeleteVehicles) DeleteVehicleLoopOnce();
                    else MainLoopOnce();
                    StopAfterLoopIfRequested();
                }
            }
            finally
            {
                KillUiCacheOcrGuards();
                overlay.Stop();
                ocr.Dispose();
            }
        }

        private void WaitForAutomationStart()
        {
            if (handoffStart)
            {
                Console.WriteLine("[STARTUP] 衔接启动：跳过启动确认和 10 秒等待。");
                SetStatus("startup wait skipped", "衔接启动子程序跳过启动确认");
                EnableWindowBindingForAutomation("handoff startup");
                return;
            }

            if (stepDebug)
            {
                Console.Write("[STARTUP] 准备工作已完成。调试模式按 Enter 后进入单步流程：");
                Console.ReadLine();
                SetStatus("startup wait skipped", "调试模式跳过开局等待");
                EnableWindowBindingForAutomation("debug startup");
                return;
            }

            Console.Write("[STARTUP] 准备工作已完成。请切到目标窗口，按 Enter 后等待 10 秒开始自动流程：");
            Console.ReadLine();
            DebugGate("startup wait", "等待 10 秒后进入主循环");
            input.SleepMs(config.StartupDelayMs);
            EnableWindowBindingForAutomation("normal startup after delay");
        }

        private void MainLoopOnce()
        {
            if (remainingSkillPoints < config.SkillPointsPerVehicle)
            {
                SetStatus("completed", "技术点不足，脚本停止");
                throw new CompletedException("当前技术点 " + remainingSkillPoints + "，不足 " + config.SkillPointsPerVehicle + "，已停止。");
            }

            loopCount++;
            SetStage("自动点技术点");
            SetStatus("loop " + loopCount, "按 Enter，打开制造商页面");
            OpenSubaruList();
            EnterVehicleListAtCachedOffset();
            if (!grid.Locked) BuildGrid();
            SetStage("查找待点技能点车辆");
            CellKey target = FindValidNewCell();
            CellKey selectedTarget = MoveSelectionToCell(target);
            SetStage("执行点技能点固定序列");
            RunFixedSequence();
            MarkVehicleCellProcessed(selectedTarget);
            bool completionReached = UseTableOnlyVehicleSearch()
                ? !vehicleList.HasPendingValidNew
                : IsCompletionBoundaryReached();
            DeductSkillPointsForCompletedVehicle();
            if (completionReached)
            {
                SetStatus("completed", "本轮技术点已点完，脚本停止");
                throw new CompletedException(UseTableOnlyVehicleSearch()
                    ? "定表虚拟列表内没有状态 3，本轮可点技术点车辆已处理完。"
                    : "本轮完成后没有状态 3，且车辆列表完成边界已确认。");
            }
        }

        private void PrepareDeleteVehicleRun()
        {
            SetStatus("delete startup", DeleteStartupSummary());
            if (useFullManufacturerFlow)
            {
                OpenSubaruList();
                EnterVehicleListAtCachedOffset();
            }
            else
            {
                SetOcrSummary("独立删车: 不打开制造商页面，直接从当前车辆列表 OCR 建表");
                UpdateOverlay(null, null, null, null);
            }
            ResetDeleteSelectionToFirstCell();
        }

        private string DeleteStartupSummary()
        {
            string nav = useFullManufacturerFlow ? "衔接启动：自动打开制造商并进入斯巴鲁" : "独立启动：当前应已在斯巴鲁车辆列表";
            string listMode = virtualListLoadMode == VirtualListLoadMode.FullState
                ? "读取旧虚拟列表状态"
                : "只复用格子坐标/行列数，重新 OCR 建表";
            return nav + "；" + listMode;
        }

        private void StopAfterLoopIfRequested()
        {
            if (!input.SafeStopRequested) return;
            SetStatus("safe stop", "Space+V 安全结束：本轮已完成并复位");
            throw new CompletedException("Space+V 安全结束：已跑完当前轮并复位。");
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private void DeductSkillPointsForCompletedVehicle()
        {
            int before = remainingSkillPoints;
            int superBefore = superWheelspinCount;
            remainingSkillPoints = Math.Max(0, remainingSkillPoints - config.SkillPointsPerVehicle);
            superWheelspinCount++;
            AppendSkillPointEvent("deduct_completed_vehicle", before, remainingSkillPoints, remainingSkillPoints - before, superBefore, superWheelspinCount);
            PersistSkillPointsState("deduct_completed_vehicle");
            SetStatus("skill points updated", "点完 1 辆车：技术点 " + before + " -> " + remainingSkillPoints + "，超级抽奖 +1，总计 " + superWheelspinCount);
        }

        private void PersistSkillPointsState(string reason)
        {
            if (string.IsNullOrWhiteSpace(skillPointsStateFile)) return;
            try
            {
                string directory = Path.GetDirectoryName(skillPointsStateFile);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["schema"] = "fh6_skill_points_state.v1";
                root["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                root["reason"] = reason;
                root["skill_points"] = remainingSkillPoints;
                root["super_wheelspins"] = superWheelspinCount;
                root["event_index"] = skillPointEventIndex;
                root["minute_loop_count"] = minuteLoopCompletedCount;
                string json = new JavaScriptSerializer().Serialize(root);
                File.WriteAllText(skillPointsStateFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SKILL_POINTS_STATE] 写入失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.PersistSkillPointsState", ex);
            }
        }

        private string ResolveSkillPointsLogFile(string requestedPath)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath)) return Path.GetFullPath(requestedPath);
            string fileName = "skill-points-events-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";
            return Path.Combine(debugDir, fileName);
        }

        private void LoadSkillPointCountersFromState()
        {
            if (string.IsNullOrWhiteSpace(skillPointsStateFile) || !File.Exists(skillPointsStateFile)) return;
            try
            {
                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(skillPointsStateFile, Encoding.UTF8));
                superWheelspinCount = ReadInt(root, "super_wheelspins", superWheelspinCount);
                skillPointEventIndex = ReadInt(root, "event_index", skillPointEventIndex);
                minuteLoopCompletedCount = ReadInt(root, "minute_loop_count", minuteLoopCompletedCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SKILL_POINTS_STATE] 计数读取失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.LoadSkillPointCountersFromState", ex);
            }
        }

        private static int ReadInt(Dictionary<string, object> root, string key, int fallback)
        {
            object value;
            if (!root.TryGetValue(key, out value) || value == null) return fallback;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private void AppendSkillPointEvent(string reason, int before, int after, int delta, int superBefore, int superAfter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(skillPointsLogFile)) return;
                string directory = Path.GetDirectoryName(skillPointsLogFile);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                skillPointEventIndex++;
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff}\tevent={1}\ttask={2}\tcycle={3}\tloop={4}\treason={5}\tskill={6}->{7}\tdelta={8}\tsuper={9}->{10}",
                    DateTime.Now,
                    skillPointEventIndex,
                    task,
                    fullAutoCycleCount,
                    loopCount,
                    reason,
                    before,
                    after,
                    delta,
                    superBefore,
                    superAfter);
                File.AppendAllText(skillPointsLogFile, line + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine("[SKILL_POINTS_EVENT] " + line);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SKILL_POINTS_EVENT] 写入失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.AppendSkillPointEvent", ex);
            }
        }
    }
}
