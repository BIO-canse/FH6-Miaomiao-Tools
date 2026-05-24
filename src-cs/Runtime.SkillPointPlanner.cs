using System;
using System.Globalization;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private enum SkillPointSearchActionKind
        {
            Select,
            Scroll,
            Observe,
            StopWithReset
        }

        private sealed class SkillPointSearchDecision
        {
            public SkillPointSearchActionKind Kind;
            public CellKey Target;
            public int ScrollTicks;
            public string Reason;
            public string Message;

            public static SkillPointSearchDecision Select(CellKey target, string reason)
            {
                return new SkillPointSearchDecision
                {
                    Kind = SkillPointSearchActionKind.Select,
                    Target = target,
                    Reason = reason
                };
            }

            public static SkillPointSearchDecision Scroll(int ticks, string reason)
            {
                return new SkillPointSearchDecision
                {
                    Kind = SkillPointSearchActionKind.Scroll,
                    ScrollTicks = ticks,
                    Reason = reason
                };
            }

            public static SkillPointSearchDecision Observe(string reason)
            {
                return new SkillPointSearchDecision
                {
                    Kind = SkillPointSearchActionKind.Observe,
                    Reason = reason
                };
            }

            public static SkillPointSearchDecision StopWithReset(string reason, string message)
            {
                return new SkillPointSearchDecision
                {
                    Kind = SkillPointSearchActionKind.StopWithReset,
                    Reason = reason,
                    Message = message
                };
            }
        }

        private SkillPointSearchDecision PlanSkillPointSearch()
        {
            CellKey targetGlobal;
            if (vehicleList.TryGetPendingValidNewGlobalTarget(out targetGlobal))
            {
                return SkillPointSearchDecision.Select(targetGlobal, "虚拟表内已有状态 3，进入工作分支生成键盘路径");
            }

            if (UseTableOnlyVehicleSearch())
            {
                if (vehicleList.HasPendingValidNew)
                {
                    return SkillPointSearchDecision.Observe("定表内仍有状态 3，但当前 offset 后方没有状态 3，可能已经滚过目标");
                }
                return SkillPointSearchDecision.StopWithReset(
                    "定表虚拟列表内没有状态 3",
                    "定表虚拟列表内没有状态 3，可点技术点车辆已处理完。");
            }

            if (IsCompletionBoundaryReached())
            {
                return SkillPointSearchDecision.StopWithReset(
                    "没有剩余待点技术点的目标车",
                    "没有状态 3，且目标车型区间末尾的下一个格子已确认是 0 或 1。");
            }

            if (subaruListBoundaryReached)
            {
                return SkillPointSearchDecision.Scroll(
                    1,
                    subaruListBoundaryReason + "，但还没有形成目标车型完成边界，继续滚动确认。");
            }

            if (!vehicleList.IsVisibleSearchRangeObserved(grid.VisibleColumns))
            {
                return SkillPointSearchDecision.Observe("当前可见目标段还有未知格子");
            }

            int skip;
            if (vehicleList.TryGetKnownNonPendingRunSkip(grid.VisibleColumns, out skip))
            {
                return SkillPointSearchDecision.Scroll(
                    skip,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "当前没有状态 3，跳过已知非 3 区间 {0} 格",
                        skip));
            }

            return SkillPointSearchDecision.Scroll(1, "当前可见范围已观察但没有状态 3，向下滚动 1 格继续找");
        }

        private void PrepareSkillPointTarget(CellKey target, string reason)
        {
            ClearOcrFields();
            SetOcrSummary("虚拟列表: " + reason + "，直接处理，不 OCR");
            lastTargetSummary = string.Format(
                CultureInfo.InvariantCulture,
                "目标格: global_col={0}, row={1}（虚拟列表 3）",
                target.Col,
                target.Row);
            UpdateOverlay(null, null, null, VisibleLocalFromGlobal(target));
            Console.WriteLine("[TARGET] planned global row={0} col={1}", target.Row, target.Col);
        }
    }
}
