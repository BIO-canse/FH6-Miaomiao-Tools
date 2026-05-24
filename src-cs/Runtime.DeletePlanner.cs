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
                return DeleteSearchDecision.Select(vehicleList.ToGlobal(deleteSelection), "当前选中格仍是可删车辆");
            }

            CellKey targetGlobal;
            if (vehicleList.TryGetDeleteVehicleGlobalTarget(out targetGlobal))
            {
                return DeleteSearchDecision.Select(targetGlobal, "虚拟表内已有状态 4，进入工作分支生成键盘路径");
            }

            if (UseTableOnlyVehicleSearch())
            {
                if (vehicleList.HasPendingDeleteVehicle)
                {
                    return DeleteSearchDecision.Observe("定表内仍有状态 4，但当前 offset 后方没有状态 4，可能已经滚过目标");
                }
                return DeleteSearchDecision.Stop(
                    "定表虚拟列表内没有状态 4",
                    "定表虚拟列表内没有状态 4，可删车辆已处理完。");
            }

            if (IsDeleteCompletionBoundaryReached())
            {
                return DeleteSearchDecision.Stop(
                    "没有剩余状态 4 可删车辆",
                    "没有状态 4 可删车辆，且目标车型区间末尾的下一个格子已确认是 0 或 1。");
            }

            if (subaruListBoundaryReached)
            {
                return DeleteSearchDecision.Scroll(
                    1,
                    subaruListBoundaryReason + "，但还没有形成删车完成边界，继续滚动确认。");
            }

            if (!vehicleList.IsVisibleSearchRangeObserved(grid.VisibleColumns))
            {
                return DeleteSearchDecision.Observe("当前可见目标段还有未知格子");
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
                "可删格: global_col={0}, row={1}",
                target.Col,
                target.Row);
            UpdateOverlay(null, null, null, null, VisibleLocalFromGlobal(target));
            Console.WriteLine("[DELETE_TARGET] planned global row={0} col={1}", target.Row, target.Col);
        }

        private void CompleteDeleteSearch(string reason, string message)
        {
            SetStatus("completed", reason + "，自动删车停止");
            SetOcrSummary("车辆列表边界: " + reason);
            throw new CompletedException(message);
        }
    }
}
