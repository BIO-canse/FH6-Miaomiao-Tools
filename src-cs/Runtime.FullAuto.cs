using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private void RunFullAutoLoop()
        {
            fullAutoCycleCount = 1;
            SetStage("A. 大世界自动进入车库标准位");
            SetStatus("full auto startup", "大世界自动进入车库标准位");
            EnterGarageStandardPosition();
            FullAutoStageGap("车库标准位已就绪，准备进入定表阶段");
            BuildInitialVehicleTableFromGarageStandardPosition();
            FullAutoStageGap("定表完成并回到车库标准位，准备运行自动点技能点");
            RunSkillPointChild();

            while (true)
            {
                RunDeleteChildHandoff();
                PrepareDriveSearchAfterDeleteChild();
                FindDriveCarAndEnterBlueprint();
                RunMinuteWLoopUntilSkillPointsFull();
                RunPostMinuteReturnSequence();
                SetStatus("full auto cycle completed", "第 " + fullAutoCycleCount + " 轮完成，准备进入下一轮");
                fullAutoCycleCount++;
                FullAutoStageGap("返回流程结束，准备进入车库标准位");
                EnterGarageStandardPosition();
                FullAutoStageGap("车库标准位已就绪，准备运行自动点技能点");
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
            SetStatus("full auto child", "衔接调用自动点技能点，直接使用定表虚拟列表，使用总控技术点计数 " + remainingSkillPoints);
            List<string> args = BaseChildArgs(FH6AutomationConstants.Files.SkillSafeStop);
            args.Add("--task");
            args.Add("skill");
            args.Add("--handoff");
            args.Add("--skill-points");
            args.Add(remainingSkillPoints.ToString(CultureInfo.InvariantCulture));
            args.Add("--skill-points-state-file");
            args.Add(SkillPointsStatePath());
            args.Add("--skill-points-log-file");
            args.Add(skillPointsLogFile);
            RunChildProcess(ResolveBinPath(FH6AutomationConstants.Files.SkillPointExe), args, "skill");
            LoadFullAutoSkillPoints("skill child completed");
        }

        private void RunFullAutoStartupPreflight()
        {
            ClearSharedUiClickCache("full auto startup");
            SetOcrSummary("总控启动前置: 当前 CR " + remainingCredits + "；买车每辆 " + FH6AutomationConstants.Credits.VehiclePrice + "；第一轮先定表，后续直接读写虚拟列表");
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
            SetStage("找开蓝图车辆前置复位");
            SetStatus("drive search handoff", "自动删车结束后 Esc -> 等待 0.5 秒，再进入找 900 分开蓝图车辆流程");
            input.Tap("ESC");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
        }

        private void RunMinuteWLoopUntilSkillPointsFull()
        {
            SetStage("子程序: 刷技术点循环");
            SetStatus("minute loop", "启动刷技术点循环，每轮 +" + FH6AutomationConstants.SkillPoints.MinuteLoopGain + "，到 " + FH6AutomationConstants.SkillPoints.Max + " 后安全退出");
            string safeStopFile = SafeStopPath(FH6AutomationConstants.Files.MinuteSafeStop);
            DeleteFileIfExists(safeStopFile);
            PersistFullAutoSkillPoints("before_minute_loop");
            string arguments = "--handoff --safe-stop-file " + QuoteArg(safeStopFile) + " --skill-points-state-file " + QuoteArg(SkillPointsStatePath()) + " --skill-points-log-file " + QuoteArg(skillPointsLogFile);
            RunMinuteWLoopProcess(safeStopFile, arguments, true);
        }

        private void RunPostMinuteReturnSequence()
        {
            minuteLoopSummary = "-";
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
            SetStage("找 900 分开蓝图车辆");
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
            SetStatus("minute loop test", "运行刷技术点脚本 1 轮，计数从 " + remainingSkillPoints + " 到 " + FH6AutomationConstants.SkillPoints.Max);
            RunMinuteWLoopUntilSkillPointsFull();
        }

        private void ReopenSubaruListFromVehicleListForDriveSearch()
        {
            SetStatus("reopen Subaru list", "Enter -> 0.5 秒 -> Backspace -> 0.5 秒 -> 向下滚动 10 格 -> 优先缓存点击斯巴鲁，必要时 OCR -> 鼠标停右侧");
            ClearOcrFields();
            DebugGate("reopen Subaru list", "按 Enter 进入车辆列表");
            input.Tap("ENTER");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            DebugGate("reopen Subaru list", "按 Backspace 打开制造商页面");
            input.Tap("BACKSPACE");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            SelectSubaruManufacturer();
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
            BuyScriptResult buyResult = RunVehicleBuyScriptRounds(buyRounds);
            ApplyBuyScriptResult(buyResult);
            AppendPurchasedVehiclesToVirtualTable(buyResult.CompletedRounds);
            FullAutoStageGap("自动买车脚本结束，准备返回菜单");
            for (int i = 0; i < FH6AutomationConstants.Flow.PreCreativeExitEscCount; i++)
            {
                input.Tap("ESC");
                FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            }

            if (buyResult.CompletedRounds < buyResult.RequestedRounds)
            {
                throw new CompletedException("CR 不足，买车已停止。本次需要补买 " + buyResult.RequestedRounds + " 辆，实际买到 " + buyResult.CompletedRounds + " 辆，剩余 CR " + remainingCredits + "，每辆需要 " + FH6AutomationConstants.Credits.VehiclePrice + "。");
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

        private BuyScriptResult RunVehicleBuyScriptRounds(int rounds)
        {
            SetStage("子程序: 自动买车");
            if (rounds <= 0)
            {
                SetStatus("full auto child", "当前状态 3 已达到 " + FH6AutomationConstants.Flow.BuyTargetValidNewCount + "，跳过自动买车脚本");
                return new BuyScriptResult(0, 0, remainingCredits, "skipped");
            }

            if (remainingCredits < FH6AutomationConstants.Credits.VehiclePrice)
            {
                SetStatus("full auto child", "CR 不足，跳过自动买车脚本；当前 CR " + remainingCredits + "，每辆 " + FH6AutomationConstants.Credits.VehiclePrice);
                return new BuyScriptResult(rounds, 0, remainingCredits, "insufficient_credits");
            }

            SetStatus("full auto child", "运行自动买车脚本 " + rounds + " 轮；当前 CR " + remainingCredits + "，每辆 " + FH6AutomationConstants.Credits.VehiclePrice);
            string safeStopFile = SafeStopPath(FH6AutomationConstants.Files.BuySafeStop);
            string resultFile = SafeStopPath("buy-result.txt");
            DeleteFileIfExists(safeStopFile);
            DeleteFileIfExists(resultFile);
            List<string> args = new List<string>();
            args.Add("--rounds");
            args.Add(rounds.ToString(CultureInfo.InvariantCulture));
            args.Add("--startup-delay-ms");
            args.Add("0");
            args.Add("--safe-stop-file");
            args.Add(safeStopFile);
            args.Add("--credits");
            args.Add(remainingCredits.ToString(CultureInfo.InvariantCulture));
            args.Add("--credit-cost");
            args.Add(FH6AutomationConstants.Credits.VehiclePrice.ToString(CultureInfo.InvariantCulture));
            args.Add("--buy-result-file");
            args.Add(resultFile);
            RunChildProcess(ResolveBinPath(FH6AutomationConstants.Files.BuyLoopExe), args, "buy");
            return ReadBuyScriptResult(resultFile, rounds);
        }

        private BuyScriptResult ReadBuyScriptResult(string path, int requestedRounds)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("买车结果文件不存在，无法确认实际买车数量。", path);

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }

            int completed = ReadIntValue(values, "completed_rounds", 0);
            long remaining = ReadLongValue(values, "remaining_credits", Math.Max(0, remainingCredits - completed * FH6AutomationConstants.Credits.VehiclePrice));
            string stopReason;
            if (!values.TryGetValue("stop_reason", out stopReason)) stopReason = "unknown";

            completed = Math.Max(0, Math.Min(requestedRounds, completed));
            return new BuyScriptResult(requestedRounds, completed, remaining, stopReason);
        }

        private void ApplyBuyScriptResult(BuyScriptResult result)
        {
            long before = remainingCredits;
            remainingCredits = Math.Max(0, result.RemainingCredits);
            SetOcrSummary(string.Format(
                CultureInfo.InvariantCulture,
                "买车CR: {0} -> {1}，本次买到 {2}/{3}，原因 {4}",
                before,
                remainingCredits,
                result.CompletedRounds,
                result.RequestedRounds,
                result.StopReason));
        }

        private static int ReadIntValue(Dictionary<string, string> values, string key, int fallback)
        {
            string raw;
            int value;
            return values.TryGetValue(key, out raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static long ReadLongValue(Dictionary<string, string> values, string key, long fallback)
        {
            string raw;
            long value;
            return values.TryGetValue(key, out raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private sealed class BuyScriptResult
        {
            public readonly int RequestedRounds;
            public readonly int CompletedRounds;
            public readonly long RemainingCredits;
            public readonly string StopReason;

            public BuyScriptResult(int requestedRounds, int completedRounds, long remainingCredits, string stopReason)
            {
                RequestedRounds = requestedRounds;
                CompletedRounds = completedRounds;
                RemainingCredits = remainingCredits;
                StopReason = stopReason ?? "";
            }
        }

        private CellKey FindDriveVehicleCell()
        {
            SetStatus("find drive vehicle", UseTableOnlyVehicleSearch() ? "按定表虚拟列表选择列表最前的 900 分状态 2 指定车型；没有候选则报错，不 OCR" : "按虚拟表规划下一步：选择、滚动、OCR 或默认第一格");
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

                if (UseTableOnlyVehicleSearch())
                {
                    FailTableOnlyVehicleSearch("drive", decision.Reason);
                }

                DebugGate("find drive vehicle", "OCR 车型和三位数性能分，第 " + (i + 1) + " 次：" + decision.Reason);
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

            SetOcrSummary(reason + "，没有找到开蓝图候选，默认第一列第一行就是可用车辆");
            lastTargetSummary = "开蓝图车: 默认 col=0, row=0";
            UpdateOverlay(null, null, null, null, new CellKey(0, 0));
            return new CellKey(0, 0);
        }

        private void ResetToSubaruListStartForDriveSearch()
        {
            SetStatus("reset Subaru list for drive", "Esc -> 0.5 秒 -> Enter -> 0.5 秒 -> Backspace -> 0.5 秒 -> 向下滚动 10 格 -> 优先缓存点击斯巴鲁，必要时 OCR -> 鼠标停右侧");
            input.Tap("ESC");
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            ReopenSubaruListFromVehicleListForDriveSearch();
        }

        private CellKey? RecordVisibleDriveGridFromOcr(OcrSnapshot snapshot, int scrollIndex)
        {
            VehicleGridObservation observation = BuildVehicleGridObservation(snapshot, scrollIndex);
            ApplyVehicleGridObservation(observation);
            CellKey? chosen = LeftTopCell(observation.DriveCells);
            SetOcrSummary(FullObservationSummary(observation, ", 找开蓝图车滚动=" + scrollIndex));
            lastTargetSummary = chosen.HasValue ? string.Format("开蓝图车: col={0}, row={1}", chosen.Value.Col, chosen.Value.Row) : "开蓝图车: 未找到候选";
            UpdateOverlay(observation.TargetCells, observation.ValidNewCells, observation.InvalidNewCells, observation.DeletableCells, observation.DriveCells, chosen);
            if (!chosen.HasValue) WriteOcrDump(snapshot, "drive-current");
            return chosen;
        }

    }
}
