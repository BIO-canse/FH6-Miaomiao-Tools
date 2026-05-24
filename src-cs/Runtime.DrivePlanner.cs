using System;
using System.Globalization;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private enum DriveSearchActionKind
        {
            Select,
            Scroll,
            Observe,
            UseDefault
        }

        private sealed class DriveSearchDecision
        {
            public DriveSearchActionKind Kind;
            public CellKey Target;
            public int ScrollTicks;
            public string Reason;

            public static DriveSearchDecision Select(CellKey target, string reason)
            {
                return new DriveSearchDecision
                {
                    Kind = DriveSearchActionKind.Select,
                    Target = target,
                    Reason = reason
                };
            }

            public static DriveSearchDecision Scroll(int ticks, string reason)
            {
                return new DriveSearchDecision
                {
                    Kind = DriveSearchActionKind.Scroll,
                    ScrollTicks = ticks,
                    Reason = reason
                };
            }

            public static DriveSearchDecision Observe(string reason)
            {
                return new DriveSearchDecision
                {
                    Kind = DriveSearchActionKind.Observe,
                    Reason = reason
                };
            }

            public static DriveSearchDecision UseDefault(string reason)
            {
                return new DriveSearchDecision
                {
                    Kind = DriveSearchActionKind.UseDefault,
                    Reason = reason
                };
            }
        }

        private DriveSearchDecision PlanDriveVehicleSearch()
        {
            CellKey targetGlobal;
            if (vehicleList.TryGetDriveVehicleGlobalTarget(out targetGlobal))
            {
                return DriveSearchDecision.Select(targetGlobal, "虚拟表内已有状态 5，进入工作分支生成键盘路径");
            }

            if (UseTableOnlyVehicleSearch())
            {
                if (vehicleList.HasDriveVehicle)
                {
                    return DriveSearchDecision.Observe("定表内仍有状态 5，但当前 offset 后方没有状态 5，可能已经滚过目标");
                }
                return DriveSearchDecision.Observe("定表虚拟列表内没有状态 5；按规则这里不允许默认退出或默认第一格");
            }

            if (subaruListBoundaryReached)
            {
                return DriveSearchDecision.UseDefault("车辆列表边界: " + subaruListBoundaryReason);
            }

            if (VisibleHasOtherManufacturerOrUnknown())
            {
                return DriveSearchDecision.UseDefault("当前页已经出现目标车型完成边界，当前可见目标段已处理完");
            }

            if (!vehicleList.IsVisibleSearchRangeObserved(grid.VisibleColumns))
            {
                return DriveSearchDecision.Observe("当前可见目标段还有未知格子");
            }

            int skip;
            if (vehicleList.TryGetKnownNonDriveToUnknownSkip(grid.VisibleColumns, out skip))
            {
                return DriveSearchDecision.Scroll(
                    skip,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "当前没有状态 5，跳过已知非 5 区间 {0} 格",
                        skip));
            }

            return DriveSearchDecision.Scroll(1, "当前可见范围已观察但没有状态 5，向下滚动 1 格继续找");
        }

        private void PrepareDriveTarget(CellKey target, string reason)
        {
            ClearOcrFields();
            SetOcrSummary("虚拟列表: " + reason + "，直接处理，不 OCR");
            lastTargetSummary = string.Format(
                CultureInfo.InvariantCulture,
                "开蓝图车: global_col={0}, row={1}",
                target.Col,
                target.Row);
            UpdateOverlay(null, null, null, null, VisibleLocalFromGlobal(target));
            Console.WriteLine("[DRIVE_TARGET] planned global row={0} col={1}", target.Row, target.Col);
        }
    }
}
