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
        private void DeleteVehicleLoopOnce()
        {
            loopCount++;
            SetStage("自动删车");
            SetStatus("delete loop " + loopCount, "查找状态 2 可删车辆");
            if (!grid.Locked) BuildGrid();

            SetStage("查找可删车辆");
            CellKey target = FindDeletableVehicleCell();
            MoveDeleteSelectionToCell(target);
            SetStage("执行删车固定序列");
            RunDeleteVehicleSequence();
            MarkVehicleCellDeleted(target);
            ResetDeleteSelectionToFirstCell();
        }

        private CellKey FindDeletableVehicleCell()
        {
            SetStatus("find deletable vehicle", "按虚拟表规划下一步：选择、滚动、OCR 或停止");
            OcrSnapshot last = null;
            for (int i = 0; i < config.MaxFindNewScrolls; i++)
            {
                DeleteSearchDecision decision = PlanDeleteVehicleSearch();
                if (decision.Kind == DeleteSearchActionKind.Select)
                {
                    PrepareDeleteTarget(decision.Target, decision.Reason);
                    return decision.Target;
                }

                if (decision.Kind == DeleteSearchActionKind.Stop)
                {
                    CompleteDeleteSearch(decision.Reason, decision.Message);
                }

                if (decision.Kind == DeleteSearchActionKind.Scroll)
                {
                    SetOcrSummary("虚拟列表: " + decision.Reason);
                    ScrollVehicleListDown(decision.ScrollTicks, "find deletable vehicle");
                    continue;
                }

                DebugGate("find deletable vehicle", "OCR 车型，第 " + (i + 1) + " 次：" + decision.Reason);
                last = ReadVehicleGridScreen();
                RecordVisibleDeleteGridFromOcr(last, i, true);
            }

            Fail(last, "deletable-vehicle-not-found");
            throw new InvalidOperationException("unreachable");
        }

        private CellKey? RecordVisibleDeleteGridFromOcr(OcrSnapshot snapshot, int scrollIndex, bool dumpWhenNoChosen)
        {
            VehicleGridObservation observation = BuildVehicleGridObservation(snapshot, scrollIndex);
            ApplyVehicleGridObservation(observation);
            HashSet<CellKey> deleteCandidates = new HashSet<CellKey>(observation.TargetCells);
            deleteCandidates.ExceptWith(observation.DriveCells);
            deleteCandidates.ExceptWith(observation.ValidNewCells);
            CellKey? chosen = LeftTopCell(deleteCandidates);
            SetOcrSummary(FullObservationSummary(observation, ", 删车滚动=" + scrollIndex));
            lastTargetSummary = chosen.HasValue ? string.Format("可删格: col={0}, row={1}", chosen.Value.Col, chosen.Value.Row) : "可删格: 未找到状态 2";

            UpdateOverlay(observation.TargetCells, observation.ValidNewCells, observation.InvalidNewCells, observation.DeletableCells, observation.DriveCells, chosen);
            if (!chosen.HasValue && dumpWhenNoChosen) WriteOcrDump(snapshot, "delete-current");
            return chosen;
        }

        private void RunDeleteVehicleSequence()
        {
            DebugGate("delete vehicle sequence", "等待菜单弹出 0.5 秒，然后 Down x4, Enter, 等待 0.5 秒, Down, Enter");
            input.SleepMs(FH6AutomationConstants.Timing.HalfSecondMs);
            for (int i = 0; i < FH6AutomationConstants.Flow.DeleteMenuDownCount; i++) input.Tap("DOWN");
            input.Tap("ENTER");
            input.SleepMs(FH6AutomationConstants.Timing.HalfSecondMs);
            input.Tap("DOWN");
            input.Tap("ENTER");
            input.SleepMs(FH6AutomationConstants.Timing.HalfSecondMs);
        }

        private void ResetDeleteSelectionToFirstCell()
        {
            deleteSelection = new CellKey(0, 0);
            deleteSelectionKnown = true;
            UpdateOverlay(null, null, null, null, deleteSelection);
        }

        private void MoveDeleteSelectionToCell(CellKey target)
        {
            MoveDeleteSelectionToCell(target, FH6AutomationConstants.Timing.OneSecondMs);
        }

        private void MoveDeleteSelectionToCell(CellKey target, int postEnterWaitMs)
        {
            MoveDeleteSelectionToCell(target, postEnterWaitMs, true, "可删格");
        }

        private void MoveDeleteSelectionToCell(CellKey target, int postEnterWaitMs, bool moveMouseAfterSelect, string targetLabel)
        {
            if (target.Row < 0 || target.Row >= config.GridRows || target.Col < 0) throw new InvalidOperationException("目标格子越界");

            CellKey start = deleteSelectionKnown ? deleteSelection : new CellKey(0, 0);
            int dx = target.Col - start.Col;
            int dy = target.Row - start.Row;
            lastTargetSummary = string.Format("{0}: col={1}, row={2}", targetLabel, target.Col, target.Row);
            DebugGate(
                "move delete selection row=" + target.Row + " col=" + target.Col,
                string.Format("从 col={0}, row={1} 移动到 col={2}, row={3}, Enter", start.Col, start.Row, target.Col, target.Row));

            string horizontal = dx >= 0 ? "RIGHT" : "LEFT";
            for (int i = 0; i < Math.Abs(dx); i++) input.Tap(horizontal);

            string vertical = dy >= 0 ? "DOWN" : "UP";
            for (int i = 0; i < Math.Abs(dy); i++) input.Tap(vertical);

            input.Tap("ENTER");
            input.SleepMs(postEnterWaitMs);
            deleteSelection = target;
            deleteSelectionKnown = true;
            UpdateOverlay(null, null, null, null, target);
            if (moveMouseAfterSelect) MoveMouseToFirstVisibleCellCenter("idle in vehicle list after selecting delete target");
        }
    }
}
