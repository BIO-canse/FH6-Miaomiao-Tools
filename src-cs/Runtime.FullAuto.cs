using System;
using System.Collections.Generic;
using System.Globalization;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private void RunFullAutoLoop()
        {
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
            RunChildProcess(ResolveBinPath(FH6AutomationConstants.Files.SkillPointExe), args, "skill");
            LoadFullAutoSkillPoints("skill child completed");
        }

        private void RunFullAutoStartupPreflight()
        {
            ClearSharedUiClickCache("full auto startup");
            SetOcrSummary("总控启动前置: 第一轮先定表；后续点技能点、删车、找5车直接读写虚拟列表，不再做车辆格 OCR");
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
            RunVehicleBuyScriptRounds(buyRounds);
            AppendPurchasedVehiclesToVirtualTable(buyRounds);
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
            SetStatus("find drive vehicle", UseTableOnlyVehicleSearch() ? "按定表虚拟列表规划下一步：选择、滚动或默认第一格，不 OCR" : "按虚拟表规划下一步：选择、滚动、OCR 或默认第一格");
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
            SetOcrSummary(FullObservationSummary(observation, ", 找5滚动=" + scrollIndex));
            lastTargetSummary = chosen.HasValue ? string.Format("开蓝图车: col={0}, row={1}", chosen.Value.Col, chosen.Value.Row) : "开蓝图车: 未找到状态 5";
            UpdateOverlay(observation.TargetCells, observation.ValidNewCells, observation.InvalidNewCells, observation.DeletableCells, observation.DriveCells, chosen);
            if (!chosen.HasValue) WriteOcrDump(snapshot, "drive-current");
            return chosen;
        }

    }
}
