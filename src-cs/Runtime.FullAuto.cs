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
        private Process fullAutoChildProcess;
        private bool fullAutoUserSafeStopRequested;

        private void RunFullAutoLoop()
        {
            SetStage("A. 大世界自动进入车库标准位");
            SetStatus("full auto startup", "大世界自动进入车库标准位");
            EnterGarageStandardPosition();
            RunSkillPointChild();

            while (true)
            {
                RunDeleteChildHandoff();
                PrepareDriveSearchAfterDeleteChild();
                FindDriveCarAndEnterBlueprint();
                RunMinuteWLoopUntilSkillPointsFull();
                RunPostMinuteReturnSequence();
                FullAutoStageGap("返回流程结束，准备进入车库标准位");
                EnterGarageStandardPosition();
                RunSkillPointChild();
            }
        }

        private void RunBlueprintCycleTest()
        {
            SetStage("测试: 找蓝图刷一次回车库标准位");
            SetStatus("blueprint cycle test", "只执行找蓝图、刷技术点 1 轮、退出并回到车库标准位");
            SetOcrSummary("测试脚本: 不执行自动点技术点、自动删车、自动拿常用车");

            EnterCreativeCenterFavoriteBlueprint();
            RunMinuteWLoopOnceForTest();
            RunPostMinuteReturnSequence();
            FullAutoStageGap("刷技术点退出流程结束，准备进入车库标准位");
            EnterGarageStandardPosition();
            throw new CompletedException("测试完成：已执行找蓝图、刷技术点 1 轮，并回到车库标准位。");
        }

        private void RunSkillPointChild()
        {
            SetStage("子程序: 自动点技能点");
            SetStatus("full auto child", "衔接调用自动点技能点，本轮重新 OCR 扫表，使用总控技术点计数 " + remainingSkillPoints);
            List<string> args = BaseChildArgs(FH6AutomationConstants.Files.SkillSafeStop);
            args.Add("--task");
            args.Add("skill");
            args.Add("--handoff");
            args.Add("--skill-points");
            args.Add(remainingSkillPoints.ToString(CultureInfo.InvariantCulture));
            args.Add("--skill-points-state-file");
            args.Add(SkillPointsStatePath());
            RunChildProcess(ResolveBinPath(FH6AutomationConstants.Files.SkillPointExe), args, "skill");
            LoadFullAutoSkillPoints("skill child completed");
        }

        private void RunFullAutoStartupPreflight()
        {
            SetOcrSummary("总控启动前置: 制造商定位使用滚动到底 + OCR 点击斯巴鲁，无需录制路径");
        }

        private void RunDeleteChildHandoff()
        {
            SetStage("子程序: 自动删车");
            SetStatus("full auto child", "衔接调用自动删车");
            List<string> args = BaseChildArgs(FH6AutomationConstants.Files.DeleteSafeStop);
            args.Add("--task");
            args.Add("delete");
            args.Add("--handoff");
            RunChildProcess(ResolveBinPath(FH6AutomationConstants.Files.DeleteVehicleExe), args, "delete");
            ReloadVehicleListStateFromHandoff("delete child completed");
        }

        private void PrepareDriveSearchAfterDeleteChild()
        {
            SetStage("找状态 5 前置复位");
            SetStatus("drive search handoff", "自动删车结束后 Esc -> 等待 0.5 秒，再进入找自用车流程");
            input.Tap("ESC");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
        }

        private void RunMinuteWLoopUntilSkillPointsFull()
        {
            SetStage("子程序: 1 分钟刷技术点");
            SetStatus("minute loop", "启动 1 分钟刷技术点循环，每轮 +" + FH6AutomationConstants.SkillPoints.MinuteLoopGain + "，到 " + FH6AutomationConstants.SkillPoints.Max + " 后安全退出");
            string safeStopFile = SafeStopPath(FH6AutomationConstants.Files.MinuteSafeStop);
            DeleteFileIfExists(safeStopFile);
            PersistFullAutoSkillPoints("before_minute_loop");
            string arguments = "--handoff --safe-stop-file " + QuoteArg(safeStopFile) + " --skill-points-state-file " + QuoteArg(SkillPointsStatePath());
            RunMinuteWLoopProcess(safeStopFile, arguments, true);
        }

        private void RunMinuteWLoopProcess(string safeStopFile, string arguments, bool syncSkillPointsAfterExit)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ResolveMinuteWLoopPath();
            psi.WorkingDirectory = config.BaseDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Arguments = arguments;

            FullAutoStageGap("启动 1 分钟刷技术点子程序前预留间隔");
            overlay.HideForCapture(0);
            fullAutoChildProcess = Process.Start(psi);

            try
            {
                while (!fullAutoChildProcess.HasExited)
                {
                    PollFullAutoHotkeysForChild(safeStopFile);
                    Thread.Sleep(FH6AutomationConstants.Timing.ChildProcessPollMs);
                }
                if (fullAutoUserSafeStopRequested) throw new CompletedException("Space+V 安全结束：子程序已退出，主程序停止。");
                if (fullAutoChildProcess.ExitCode != 0) throw new InvalidOperationException(FH6AutomationConstants.Files.MinuteLoopExe + " 退出码 " + fullAutoChildProcess.ExitCode);
                if (syncSkillPointsAfterExit) LoadFullAutoSkillPoints("minute loop completed");
            }
            finally
            {
                fullAutoChildProcess = null;
                DeleteFileIfExists(safeStopFile);
                overlay.ShowOverlay();
            }
            FullAutoStageGap("1 分钟刷技术点子程序结束后预留间隔");
        }

        private void RunPostMinuteReturnSequence()
        {
            SetStage("E. 刷技术点结束后的返回流程");
            SetStatus("post minute return", "Down x" + FH6AutomationConstants.Flow.PostMinuteDownCount + ", Enter, 1 秒, Enter, 20 秒");
            MoveMouseToScreenBottomRight("idle before post minute return");
            for (int i = 0; i < FH6AutomationConstants.Flow.PostMinuteDownCount; i++) input.Tap("DOWN");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.OneSecondMs);
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.TwentySecondsMs);
        }

        private void FindDriveCarAndEnterBlueprint()
        {
            SetStage("找状态 5 开蓝图车辆");
            if (!grid.Locked) BuildGrid();
            ReopenSubaruListFromVehicleListForDriveSearch();
            ResetDeleteSelectionToFirstCell();
            CellKey target = FindDriveVehicleCell();
            MoveDeleteSelectionToCell(target, FH6AutomationConstants.Timing.HalfSecondMs, false, "开蓝图车");

            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.FiveSecondsMs);
            input.Tap("ESC");
            FullAutoSleep(FH6AutomationConstants.Timing.TenSecondsMs);

            RunPreCreativeCenterBuySetup();
            EnterCreativeCenterFavoriteBlueprint();
        }

        private void EnterCreativeCenterFavoriteBlueprint()
        {
            SetStage("D. 进入创意中心收藏蓝图");
            FindTextAndClick(config.CreativeCenterText, "创意中心");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.OneSecondMs);
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.OneSecondMs);
            FindTextAndClick(config.LatestHotText, "最新最热");
            FindTextAndClick(config.MyFavoritesText, "我的收藏");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.FiveSecondsMs);
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.FiveSecondsMs);
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.TwentySecondsMs);
        }

        private void RunMinuteWLoopOnceForTest()
        {
            remainingSkillPoints = FH6AutomationConstants.SkillPoints.Max - FH6AutomationConstants.SkillPoints.MinuteLoopGain;
            PersistFullAutoSkillPoints("blueprint_cycle_test_before_minute_loop_once");
            SetStatus("minute loop test", "运行 1 分钟刷技术点脚本 1 轮，计数从 " + remainingSkillPoints + " 到 " + FH6AutomationConstants.SkillPoints.Max);
            RunMinuteWLoopUntilSkillPointsFull();
        }

        private void ReopenSubaruListFromVehicleListForDriveSearch()
        {
            SetStatus("reopen Subaru list", "Enter -> 0.5 秒 -> Backspace -> 0.5 秒 -> 滚动到底 -> OCR 点击斯巴鲁");
            ClearOcrFields();
            DebugGate("reopen Subaru list", "按 Enter 进入车辆列表");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            DebugGate("reopen Subaru list", "按 Backspace 打开制造商页面");
            input.Tap("BACKSPACE");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            SelectSubaruManufacturer();
            FullAutoSleep(config.AfterClickMs);
            vehicleList.ResetView();
            UpdateOverlay(null, null, null, null);
        }

        private void RunPreCreativeCenterBuySetup()
        {
            SetStage("C. 买车前置流程");
            int buyRounds = CalculateVehicleBuyRounds();
            SetStatus("pre creative center buy setup", "进入买车脚本前置流程，本次补车 " + buyRounds + " 辆");
            MoveMouseToScreenBottomRight("idle before pre creative center buy setup");
            input.Tap("ESC");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("LEFT");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("RIGHT");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("DOWN");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("BACKSPACE");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            FindSubaruManufacturerByOcr("买车斯巴鲁制造商", false);
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("DOWN");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            MoveMouseToScreenBottomRight("idle after entering Subaru buy page");
            RunVehicleBuyScriptRounds(buyRounds);
            FullAutoStageGap("自动买车脚本结束，准备返回菜单");
            for (int i = 0; i < FH6AutomationConstants.Flow.PreCreativeExitEscCount; i++)
            {
                input.Tap("ESC");
                FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            }
        }

        private int CalculateVehicleBuyRounds()
        {
            int existingValidNew = vehicleList.CountValidNewVehicles();
            int target = FH6AutomationConstants.Flow.BuyTargetValidNewCount;
            int rounds = Math.Max(0, target - existingValidNew);
            SetOcrSummary(string.Format(
                CultureInfo.InvariantCulture,
                "买车补充: 当前状态3={0}, 目标={1}, 本次买={2}",
                existingValidNew,
                target,
                rounds));
            return rounds;
        }

        private void RunVehicleBuyScriptRounds(int rounds)
        {
            SetStage("子程序: 自动买车");
            if (rounds <= 0)
            {
                SetStatus("full auto child", "当前状态 3 已达到 " + FH6AutomationConstants.Flow.BuyTargetValidNewCount + "，跳过自动买车脚本");
                return;
            }
            SetStatus("full auto child", "运行自动买车脚本 " + rounds + " 轮");
            string safeStopFile = SafeStopPath(FH6AutomationConstants.Files.BuySafeStop);
            DeleteFileIfExists(safeStopFile);
            List<string> args = new List<string>();
            args.Add("--rounds");
            args.Add(rounds.ToString(CultureInfo.InvariantCulture));
            args.Add("--startup-delay-ms");
            args.Add("0");
            args.Add("--safe-stop-file");
            args.Add(safeStopFile);
            RunChildProcess(ResolveBinPath(FH6AutomationConstants.Files.BuyLoopExe), args, "buy");
        }

        private CellKey FindDriveVehicleCell()
        {
            SetStatus("find drive vehicle", "按虚拟表规划下一步：选择、滚动、OCR 或默认第一格");
            OcrSnapshot last = null;
            for (int i = 0; i < config.MaxFindNewScrolls; i++)
            {
                DriveSearchDecision decision = PlanDriveVehicleSearch();
                if (decision.Kind == DriveSearchActionKind.Select)
                {
                    PrepareDriveTarget(decision.Target, decision.Reason);
                    return decision.Target;
                }

                if (decision.Kind == DriveSearchActionKind.UseDefault)
                {
                    return UseDefaultDriveVehicleAfterSearchEnds(decision.Reason);
                }

                if (decision.Kind == DriveSearchActionKind.Scroll)
                {
                    SetOcrSummary("虚拟列表: " + decision.Reason);
                    ScrollVehicleListDown(decision.ScrollTicks, "find drive vehicle");
                    continue;
                }

                DebugGate("find drive vehicle", "OCR 车型和 900，第 " + (i + 1) + " 次：" + decision.Reason);
                last = ReadVehicleGridScreen();
                RecordVisibleDriveGridFromOcr(last, i);
            }

            return UseDefaultDriveVehicleAfterSearchEnds("超过最大查找次数");
        }

        private CellKey UseDefaultDriveVehicleAfterSearchEnds(string reason)
        {
            if (vehicleList.CurrentOffset > 0)
            {
                ResetToSubaruListStartForDriveSearch();
            }

            SetOcrSummary(reason + "，没有找到状态 5，默认第一列第一行就是可用车辆");
            lastTargetSummary = "开蓝图车: 默认 col=0, row=0";
            UpdateOverlay(null, null, null, null, new CellKey(0, 0));
            return new CellKey(0, 0);
        }

        private void ResetToSubaruListStartForDriveSearch()
        {
            SetStatus("reset Subaru list for drive", "Esc -> 0.5 秒 -> Enter -> 0.5 秒 -> Backspace -> 0.5 秒 -> 滚动到底 -> OCR 点击斯巴鲁");
            input.Tap("ESC");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            ReopenSubaruListFromVehicleListForDriveSearch();
        }

        private CellKey? RecordVisibleDriveGridFromOcr(OcrSnapshot snapshot, int scrollIndex)
        {
            VehicleGridObservation observation = BuildVehicleGridObservation(snapshot, scrollIndex);
            ApplyVehicleGridObservation(observation);
            CellKey? chosen = LeftTopCell(observation.DriveCells);
            SetOcrSummary(FullObservationSummary(observation, ", 找5滚动=" + scrollIndex));
            lastTargetSummary = chosen.HasValue ? string.Format("开蓝图车: col={0}, row={1}", chosen.Value.Col, chosen.Value.Row) : "开蓝图车: 未找到状态 5";
            UpdateOverlay(observation.TargetCells, observation.ValidNewCells, observation.InvalidNewCells, observation.DeletableCells, observation.DriveCells, chosen);
            if (!chosen.HasValue) WriteOcrDump(snapshot, "drive-current");
            return chosen;
        }

        private void FindTextAndClick(string text, string label)
        {
            FindTextAndClick(text, label, true);
        }

        private void FindTextAndClick(string text, string label, bool moveToIdleAfterClick)
        {
            string cacheKey = UiClickCacheKey("text", label, text);
            if (TryClickCachedUiPoint(cacheKey, label, moveToIdleAfterClick))
            {
                return;
            }

            OcrSnapshot last = null;
            for (int i = 0; i < FH6AutomationConstants.Ocr.UiFindAttempts; i++)
            {
                DebugGate("find text " + label, "OCR 找 " + text + " 并点击，第 " + (i + 1) + " 次");
                WaitBeforeUiOcrCapture("before OCR " + label);
                last = ReadScreen();
                List<OcrMatch> matches = FindConfiguredCjkTextMatches(last, text);
                SetOcrFields(new OcrFieldGroup(label, matches));
                SetOcrSummary("OCR: " + text + "=" + matches.Count);
                if (matches.Count > 0)
                {
                    OcrMatch chosen = ChooseUiTextMatch(matches, text);
                    Point center = chosen.RectCenter();
                    RememberUiClickPoint(cacheKey, label, center);
                    input.MoveTo(center.X, center.Y);
                    input.Click();
                    if (moveToIdleAfterClick) MoveMouseToScreenBottomRight("idle after clicking " + label);
                    return;
                }
                WriteOcrDump(last, "find-text-" + SanitizeDebugLabel(label));
                FullAutoSleep(FH6AutomationConstants.Timing.UiFindRetryDelayMs);
            }

            Fail(last, "text-not-found-" + label);
        }

        private string UiClickCacheKey(string category, string label, string text)
        {
            return category + "|" + label + "|" + text;
        }

        private bool TryClickCachedUiPoint(string cacheKey, string label, bool moveToIdleAfterClick)
        {
            Point point;
            if (!uiClickCache.TryGetValue(cacheKey, out point)) return false;

            DebugGate("ui click cache " + label, "复用 UI 坐标 " + label + " (" + point.X + "," + point.Y + ")");
            SetOcrSummary("UI坐标缓存: " + label + " -> " + point.X + "," + point.Y + "，等待 0.5 秒后点击");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.MoveTo(point.X, point.Y);
            input.Click();
            if (moveToIdleAfterClick) MoveMouseToScreenBottomRight("idle after clicking cached " + label);
            return true;
        }

        private void RememberUiClickPoint(string cacheKey, string label, Point point)
        {
            uiClickCache[cacheKey] = point;
            SetOcrSummary("UI坐标缓存: 已记录 " + label + " -> " + point.X + "," + point.Y);
        }

        private void WaitBeforeUiOcrCapture(string reason)
        {
            SetOcrSummary("UI OCR 等待画面稳定 1 秒: " + reason);
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.UiOcrStableWaitMs);
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
                    PollFullAutoHotkeysForChild(safeStopFile);
                    Thread.Sleep(FH6AutomationConstants.Timing.ChildProcessPollMs);
                }

                if (fullAutoUserSafeStopRequested) throw new CompletedException("Space+V 安全结束：子程序已退出，主程序停止。");
                if (fullAutoChildProcess.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(exePath) + " 退出码 " + fullAutoChildProcess.ExitCode);
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
                KillChildIfRunning();
                throw new StopRequestedException();
            }
            if (!fullAutoUserSafeStopRequested && IsKeyDown(FH6AutomationConstants.Keys.HotkeyModifierVirtualKey) && IsKeyDown(FH6AutomationConstants.Keys.FullAutoSafeStopVirtualKey))
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
                ThrowIfFullAutoSafeStopWithoutChild();
                input.SleepMs(Math.Min(FH6AutomationConstants.Timing.FullAutoSleepSliceMs, Math.Max(1, ms - (int)sw.ElapsedMilliseconds)));
            }
        }

        private void ThrowIfFullAutoSafeStopWithoutChild()
        {
            if (input.ShouldStop()) throw new StopRequestedException();
            if (IsKeyDown(FH6AutomationConstants.Keys.HotkeyModifierVirtualKey) && IsKeyDown(FH6AutomationConstants.Keys.FullAutoSafeStopVirtualKey))
            {
                fullAutoUserSafeStopRequested = true;
                throw new CompletedException("Space+V 安全结束：当前没有子程序运行，主程序直接停止。");
            }
        }

        private void FullAutoCheckPoint()
        {
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
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(root), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FULL_AUTO_SKILL_POINTS] 写入失败：" + ex.Message);
            }
        }

        private void LoadFullAutoSkillPoints(string reason)
        {
            try
            {
                string path = SkillPointsStatePath();
                if (!File.Exists(path)) return;
                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                object value;
                if (!root.TryGetValue("skill_points", out value)) return;
                remainingSkillPoints = Math.Max(0, Math.Min(FH6AutomationConstants.SkillPoints.Max, Convert.ToInt32(value, CultureInfo.InvariantCulture)));
                SetStatus("skill points synced", reason + "，当前技术点 " + remainingSkillPoints);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FULL_AUTO_SKILL_POINTS] 读取失败：" + ex.Message);
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
            string path = ResolveBinPath(FH6AutomationConstants.Files.MinuteLoopExe);
            return path;
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

        private static string SanitizeDebugLabel(string label)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in label)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }
            return sb.Length == 0 ? "text" : sb.ToString();
        }

    }
}
