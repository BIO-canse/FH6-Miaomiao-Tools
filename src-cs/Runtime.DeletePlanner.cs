using System;
using System.Globalization;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private enum DeleteSearchActionKind
        {
            Select,
            Scroll,
            Observe,
            Stop
        }

        private sealed class DeleteSearchDecision
        {
            public DeleteSearchActionKind Kind;
            public CellKey Target;
            public int ScrollTicks;
            public string Reason;
            public string Message;

            public static DeleteSearchDecision Select(CellKey target, string reason)
            {
                return new DeleteSearchDecision
                {
                    Kind = DeleteSearchActionKind.Select,
                    Target = target,
                    Reason = reason
                };
            }

            public static DeleteSearchDecision Scroll(int ticks, string reason)
            {
                return new DeleteSearchDecision
                {
                    Kind = DeleteSearchActionKind.Scroll,
                    ScrollTicks = ticks,
                    Reason = reason
                };
            }

            public static DeleteSearchDecision Observe(string reason)
            {
                return new DeleteSearchDecision
                {
                    Kind = DeleteSearchActionKind.Observe,
                    Reason = reason
                };
            }

            public static DeleteSearchDecision Stop(string reason, string message)
            {
                return new DeleteSearchDecision
                {
                    Kind = DeleteSearchActionKind.Stop,
                    Reason = reason,
                    Message = message
                };
            }
        }

        private DeleteSearchDecision PlanDeleteVehicleSearch()
        {
            if (deleteSelectionKnown && vehicleList.IsVisibleDeleteVehicle(deleteSelection, grid.VisibleColumns))
            {
                return DeleteSearchDecision.Select(deleteSelection, "当前选中格仍是可删车辆");
            }

            CellKey target;
            if (vehicleList.TryGetVisibleDeleteVehicle(grid.VisibleColumns, out target))
            {
                return DeleteSearchDecision.Select(target, "当前可见范围已有可删车辆");
            }

            if (!vehicleList.IsVisibleSearchRangeObserved(grid.VisibleColumns))
            {
                return DeleteSearchDecision.Observe("当前可见目标段还有未知格子");
            }

            if (VisibleHasOtherManufacturerOrUnknown())
            {
                return DeleteSearchDecision.Stop(
                    "当前页已经出现删车完成边界",
                    "当前页已确认目标车型区间末尾后面是 0 或 1，后面不会再有可删车辆。");
            }

            CellKey knownTarget;
            int targetOffset;
            if (vehicleList.TryGetDeleteVehicleTarget(grid.VisibleColumns, out knownTarget, out targetOffset))
            {
                int delta = targetOffset - vehicleList.CurrentOffset;
                if (delta > 0)
                {
                    return DeleteSearchDecision.Scroll(
                        delta,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "虚拟表后方已有可删车辆，滚动 {0} 格到 offset={1}",
                            delta,
                            targetOffset));
                }
            }

            if (IsDeleteCompletionBoundaryReached())
            {
                return DeleteSearchDecision.Stop(
                    "没有剩余状态 2 可删车辆",
                    "没有状态 2/4 可删车辆，且目标车型区间末尾的下一个格子已确认是 0 或 1。");
            }

            if (subaruListBoundaryReached)
            {
                return DeleteSearchDecision.Scroll(
                    1,
                    subaruListBoundaryReason + "，但还没有形成删车完成边界，继续滚动确认。");
            }

            int skip;
            if (vehicleList.TryGetKnownNonDeleteToUnknownSkip(grid.VisibleColumns, out skip))
            {
                return DeleteSearchDecision.Scroll(
                    skip,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "当前没有可删车辆，跳过已知不可删区间 {0} 格",
                        skip));
            }

            return DeleteSearchDecision.Scroll(1, "当前可见范围已观察但没有可删车辆，向下滚动 1 格继续找");
        }

        private void PrepareDeleteTarget(CellKey target, string reason)
        {
            ClearOcrFields();
            SetOcrSummary("虚拟列表: " + reason + "，直接处理，不 OCR");
            lastTargetSummary = string.Format(
                CultureInfo.InvariantCulture,
                "可删格: col={0}, row={1}",
                target.Col,
                target.Row);
            RememberVehicleResumeOffset();
            UpdateOverlay(null, null, null, null, target);
            Console.WriteLine("[DELETE_TARGET] planned row={0} col={1}", target.Row, target.Col);
        }

        private void CompleteDeleteSearch(string reason, string message)
        {
            SetStatus("completed", reason + "，自动删车停止");
            SetOcrSummary("车辆列表边界: " + reason);
            throw new CompletedException(message);
        }
    }
}
