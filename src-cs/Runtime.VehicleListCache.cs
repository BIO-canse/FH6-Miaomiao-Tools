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
        private void EnterVehicleListAtCachedOffset()
        {
            vehicleList.ResetView();
            UpdateOverlay(null, null, null, null);

            ClearOcrFields();
            SetOcrSummary("车辆列表入口: 已复位到第一页，优先使用衔接虚拟列表");
            UpdateOverlay(null, null, null, null);
        }

        private void ReloadVehicleListStateFromHandoff(string reason)
        {
            if (vehicleList.ReloadFullStateFromDisk(reason))
            {
                ClearOcrFields();
                SetOcrSummary("虚拟列表: 已读取衔接状态，" + vehicleList.Summary());
                UpdateOverlay(null, null, null, null);
                return;
            }

            SetOcrSummary("虚拟列表: 未能读取衔接状态，后续会按当前屏幕 OCR");
            UpdateOverlay(null, null, null, null);
        }

        private void ScrollVehicleListDown(int ticks, string action)
        {
            string amount = ticks == 1 ? "一格" : ticks + " 格";
            DebugGate(action, "车辆列表滚轮向下" + amount);
            ClearOcrFields();
            MoveMouseToFirstVisibleCellCenter("vehicle list scroll focus");
            input.ScrollDown(ticks, config.ScrollTickDelayMs);
            vehicleList.ScrollDown(ticks);
            if (task == AutomationTask.DeleteVehicles) deleteSelectionKnown = false;
            input.SleepMs(config.SingleScrollDelayMs);
            MoveMouseToFirstVisibleCellCenter("idle in vehicle list after scroll");
            UpdateOverlay(null, null, null, null);
        }

        private void ReturnVehicleListToFirstPage(string reason)
        {
            int offset = vehicleList.CurrentOffset;
            if (offset <= 0) return;

            DebugGate("return vehicle list top", reason + " x" + offset);
            ClearOcrFields();
            MoveMouseToFirstVisibleCellCenter("vehicle list scroll focus");
            input.ScrollUp(offset, config.ScrollTickDelayMs);
            vehicleList.ResetView();
            if (task == AutomationTask.DeleteVehicles) deleteSelectionKnown = false;
            input.SleepMs(config.SingleScrollDelayMs);
            MoveMouseToFirstVisibleCellCenter("idle in vehicle list after scroll up");
            UpdateOverlay(null, null, null, null);
        }

        private void UpdateSubaruListBoundary(List<OcrMatch> manufacturerMatches)
        {
            subaruListBoundaryReached = false;
            subaruListBoundaryReason = "";
            if (!grid.Ready) return;

            HashSet<CellKey> manufacturerCells = MapVisibleCellsIncludingSelectedCell(manufacturerMatches);
            if (manufacturerCells.Count == 0)
            {
                subaruListBoundaryReached = true;
                subaruListBoundaryReason = "当前车辆列表区域没有识别到斯巴鲁";
                return;
            }

            bool sawReservedFirstCell = false;
            foreach (CellKey local in manufacturerCells)
            {
                CellKey global = vehicleList.ToGlobal(local);
                if (global.Row == 0 && global.Col == 0)
                {
                    sawReservedFirstCell = true;
                    continue;
                }
                if (vehicleList.HasKnownCell(global)) continue;
                return;
            }

            subaruListBoundaryReached = true;
            subaruListBoundaryReason = sawReservedFirstCell
                ? "只识别到全局第一个占位斯巴鲁"
                : "当前车辆列表可处理区域没有识别到斯巴鲁";
        }

        private bool VisibleHasOtherManufacturerOrUnknown()
        {
            return grid.Ready && vehicleList.SearchBoundaryReached(grid.VisibleColumns);
        }

        private void RememberVehicleResumeOffset()
        {
            vehicleList.RememberResumeOffset(vehicleList.CurrentOffset);
            UpdateOverlay(null, null, null, null);
        }

        private void MarkVehicleCellProcessed(CellKey localCell)
        {
            vehicleList.MarkProcessed(localCell);
            UpdateOverlay(null, null, null, null);
        }

        private void ReturnToSkillPointStandardPosition()
        {
            SetStatus("return standard position", "没有可点车辆，按 Esc 回到标准位");
            DebugGate("return standard position", "按 Esc 回到标准位");
            input.Tap("ESC");
            input.SleepMs(FH6AutomationShared.FH6AutomationConstants.Timing.OneSecondMs);
        }

        private void CompleteSkillSearchWithoutTarget(string reason, string completedMessage)
        {
            SetStatus("completed", reason + "，自动点技术点停止");
            ReturnToSkillPointStandardPosition();
            throw new CompletedException(completedMessage);
        }

        private void MarkVehicleCellDeleted(CellKey localCell)
        {
            vehicleList.MarkDeletedAndShift(localCell);
            UpdateOverlay(null, null, null, null);
        }

        private bool IsCompletionBoundaryReached()
        {
            CellKey lastTarget;
            CellKey nextCell;
            if (!vehicleList.IsCompletionBoundaryReached(out lastTarget, out nextCell)) return false;

            if (lastTarget.Row < 0)
            {
                lastTargetSummary = string.Format(
                    CultureInfo.InvariantCulture,
                    "完成边界: 未找到目标车型，首个0=col{0}/row{1}",
                    nextCell.Col,
                    nextCell.Row);
                SetOcrSummary("虚拟列表: 无状态2/3/4/5，列表末尾已确认出现0");
                return true;
            }

            lastTargetSummary = string.Format(
                CultureInfo.InvariantCulture,
                "完成边界: 最后目标车型=col{0}/row{1}, 下一个=col{2}/row{3}",
                lastTarget.Col,
                lastTarget.Row,
                nextCell.Col,
                nextCell.Row);
            SetOcrSummary("虚拟列表: 无状态3，目标车型区间末尾已确认是0或1");
            return true;
        }

        private bool IsDeleteCompletionBoundaryReached()
        {
            CellKey lastTarget;
            CellKey nextCell;
            if (!vehicleList.IsDeleteCompletionBoundaryReached(out lastTarget, out nextCell)) return false;

            if (lastTarget.Row < 0)
            {
                lastTargetSummary = string.Format(
                    CultureInfo.InvariantCulture,
                    "删车完成边界: 未找到目标车型，首个0=col{0}/row{1}",
                    nextCell.Col,
                    nextCell.Row);
                SetOcrSummary("虚拟列表: 无状态2/3/4/5，列表末尾已确认出现0");
                return true;
            }

            lastTargetSummary = string.Format(
                CultureInfo.InvariantCulture,
                "删车完成边界: 最后目标车型=col{0}/row{1}, 下一个=col{2}/row{3}",
                lastTarget.Col,
                lastTarget.Row,
                nextCell.Col,
                nextCell.Row);
            SetOcrSummary("虚拟列表: 无状态4，目标车型区间末尾已确认是0或1");
            return true;
        }

        private void MoveMouseToFirstVisibleCellCenter(string reason)
        {
            MoveMouseToVehicleListSecondRowRightEdge(reason);
        }

        private void MoveMouseToVehicleListSecondRowRightEdge(string reason)
        {
            if (!grid.Ready) return;
            int row = Math.Min(1, Math.Max(0, grid.Rows - 1));
            int col = Math.Max(0, grid.VisibleColumns - 1);
            Point point;
            if (!grid.TryGetCellCenter(new CellKey(row, col), out point)) return;
            Rectangle screen = capture.GetBounds();
            Point idle = new Point(screen.Right - 2, point.Y);
            Console.WriteLine("[INPUT] " + reason + " at vehicle list second-row last-column right edge " + idle.X + "," + idle.Y);
            input.MoveTo(idle.X, idle.Y);
        }
    }
}
