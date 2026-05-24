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
        private readonly InputController input;
        private readonly ScreenCapture capture;
        private readonly OcrReader ocr;
        private readonly GridGeometry grid;
        private readonly VirtualVehicleList vehicleList;
        private readonly OverlayRenderer overlay;
        private readonly string debugDir;
        private readonly string debugScreenshotDir;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private int failures;
        private int loopCount;
        private int debugStepCount;
        private int debugScreenshotCounter;
        private int remainingSkillPoints;
        private readonly int firstRunSkillPoints;
        private bool deleteSelectionKnown;
        private CellKey deleteSelection = new CellKey(0, 0);
        private readonly Dictionary<string, Point> uiClickCache = new Dictionary<string, Point>();
        private string status = "init";
        private string bigStage = "启动";
        private string nextAction = "等待启动";
        private string lastOcrSummary = "OCR: -";
        private string lastTargetSummary = "目标格: -";
        private List<OcrFieldView> lastOcrFields = new List<OcrFieldView>();
        private bool subaruListBoundaryReached;
        private string subaruListBoundaryReason = "";

        public Runtime(Config config, bool dryRun, bool stepDebug, int initialSkillPoints, AutomationTask task, bool useFullManufacturerFlow, VirtualListLoadMode virtualListLoadMode, bool handoffStart, string safeStopFile, string skillPointsStateFile)
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
            debugScreenshotDir = Path.Combine(debugDir, "screenshots");
            if (stepDebug) ResetDebugScreenshots();
            ocr = new OcrReader(config, stepDebug ? debugScreenshotDir : null);
            string virtualListLog = stepDebug ? Path.Combine(debugDir, "virtual-list-edits-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log") : null;
            string virtualListPath = config.ResolvePath(config.VirtualListPath);
            vehicleList = new VirtualVehicleList(config.GridRows, virtualListLog, virtualListPath, virtualListLoadMode);
            overlay = new OverlayRenderer(config.OverlayEnabled);
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
                return;
            }

            if (stepDebug)
            {
                Console.Write("[STARTUP] 准备工作已完成。调试模式按 Enter 后进入单步流程：");
                Console.ReadLine();
                SetStatus("startup wait skipped", "调试模式跳过开局等待");
                return;
            }

            Console.Write("[STARTUP] 准备工作已完成。请切到目标窗口，按 Enter 后等待 10 秒开始自动流程：");
            Console.ReadLine();
            DebugGate("startup wait", "等待 10 秒后进入主循环");
            input.SleepMs(config.StartupDelayMs);
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
            MoveSelectionToCell(target);
            SetStage("执行点技能点固定序列");
            RunFixedSequence();
            MarkVehicleCellProcessed(target);
            bool completionBoundaryReached = IsCompletionBoundaryReached();
            DeductSkillPointsForCompletedVehicle();
            if (completionBoundaryReached)
            {
                SetStatus("completed", "本轮技术点已点完，脚本停止");
                throw new CompletedException("本轮完成后没有状态 3，且车辆列表完成边界已确认。");
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
            remainingSkillPoints = Math.Max(0, remainingSkillPoints - config.SkillPointsPerVehicle);
            PersistSkillPointsState("deduct_completed_vehicle");
            SetStatus("skill points updated", "本轮完成，剩余技术点 " + remainingSkillPoints);
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
                string json = new JavaScriptSerializer().Serialize(root);
                File.WriteAllText(skillPointsStateFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SKILL_POINTS_STATE] 写入失败：" + ex.Message);
            }
        }
    }
}
