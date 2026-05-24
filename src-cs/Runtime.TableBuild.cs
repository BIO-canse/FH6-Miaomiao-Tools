using System;
using System.Collections.Generic;
using System.Globalization;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private void BuildInitialVehicleTableFromGarageStandardPosition()
        {
            SetStage("定表阶段");
            SetStatus("table build", "从车库标准位进入斯巴鲁车辆列表，完整扫描目标车型段");
            ClearOcrFields();

            OpenSubaruList();
            EnterVehicleListAtCachedOffset();
            if (!grid.Locked) BuildGrid();

            SetStatus("table build initial scroll", "车辆列表先按 Left 1 次，第一次 OCR 的可见第一列是上一制造商，不写入表");
            ScrollVehicleListUp(1, "table build initial scroll");
            vehicleList.SetPreviousManufacturerColumnVisibleForTableBuild("table build initial left");
            UpdateOverlay(null, null, null, null);

            OcrSnapshot last = null;
            int scrollStep = Math.Max(1, grid.VisibleColumns - 1);
            for (int i = 0; i < config.MaxFindNewScrolls; i++)
            {
                int ignoredLeadingColumns = i == 0 ? 1 : 0;
                DebugGate("table build OCR", "定表 OCR，第 " + (i + 1) + " 次，忽略可见前置列数=" + ignoredLeadingColumns);
                last = ReadVehicleGridScreen();
                RecordTableBuildObservation(last, i, ignoredLeadingColumns);

                CellKey lastTarget;
                CellKey nextCell;
                if (vehicleList.IsTargetModelBoundaryReached(out lastTarget, out nextCell))
                {
                    lastTargetSummary = string.Format(
                        CultureInfo.InvariantCulture,
                        "定表完成: 最后目标=col{0}/row{1}, 边界=col{2}/row{3}",
                        lastTarget.Col,
                        lastTarget.Row,
                        nextCell.Col,
                        nextCell.Row);
                    SetOcrSummary("定表完成: 已发现目标车型段后的 0/1 边界");
                    UpdateOverlay(null, null, null, null);
                    ReturnToSkillPointStandardPosition();
                    return;
                }

                SetOcrSummary("定表: 未到目标车型段边界，激进滚动 " + scrollStep + " 格继续");
                ScrollVehicleListDown(scrollStep, "table build aggressive scroll");
            }

            Fail(last, "table-build-boundary-not-found");
        }

        private void RecordTableBuildObservation(OcrSnapshot snapshot, int scrollIndex, int ignoredLeadingColumns)
        {
            VehicleGridObservation observation = BuildVehicleGridObservation(snapshot, scrollIndex);
            WriteTableBuildObservationTrace(observation, ignoredLeadingColumns);
            vehicleList.ApplyTableBuildObservation(
                grid.VisibleColumns,
                ignoredLeadingColumns,
                observation.TargetCells,
                observation.ValidNewCells,
                observation.InvalidNewCells,
                observation.DeletableCells,
                observation.DriveCells,
                observation.ManufacturerCells,
                observation.PerformanceScores,
                observation.BlankCells);

            SetOcrFields(
                new OcrFieldGroup("车名", observation.TargetMatches),
                new OcrFieldGroup("全新", observation.NewBadgeMatches),
                new OcrFieldGroup("斯巴鲁", observation.ManufacturerMatches),
                new OcrFieldGroup("600", observation.DeleteMarkerMatches),
                new OcrFieldGroup("性能分", observation.PerformanceScoreMatches));
            SetOcrSummary(FullObservationSummary(observation, ", 定表滚动=" + scrollIndex + ", 忽略前置列=" + ignoredLeadingColumns));
            UpdateOverlay(
                observation.TargetCells,
                observation.ValidNewCells,
                observation.InvalidNewCells,
                observation.DeletableCells,
                observation.DriveCells,
                null);
        }

        private void AppendPurchasedVehiclesToVirtualTable(int count)
        {
            if (count <= 0) return;
            vehicleList.AppendPurchasedValidNewVehicles(count);
            SetOcrSummary("买车追加: 已按性能分规则把 " + count + " 辆新车写入虚拟表为状态 3");
            UpdateOverlay(null, null, null, null);
        }
    }
}
