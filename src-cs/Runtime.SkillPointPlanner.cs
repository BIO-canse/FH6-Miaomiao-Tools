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
            CellKey target;
            if (vehicleList.TryGetVisiblePendingValidNew(grid.VisibleColumns, out target))
            {
                return SkillPointSearchDecision.Select(target, "当前可见范围已有状态 3");
            }

            if (!vehicleList.IsVisibleSearchRangeObserved(grid.VisibleColumns))
            {
                return SkillPointSearchDecision.Observe("当前可见目标段还有未知格子");
            }

            if (VisibleHasOtherManufacturerOrUnknown())
            {
                return SkillPointSearchDecision.StopWithReset(
                    "当前页已经出现状态 0",
                    "当前页已经出现非斯巴鲁或未确认制造商格子，当前可见目标段已处理完，后面不会再有可点技术点车辆。");
            }

            CellKey knownTarget;
            int targetOffset;
            if (vehicleList.TryGetPendingValidNewTargetAtOrAfterCurrent(grid.VisibleColumns, out knownTarget, out targetOffset))
            {
                int delta = targetOffset - vehicleList.CurrentOffset;
                if (delta > 0)
                {
                    return SkillPointSearchDecision.Scroll(
                        delta,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "虚拟表后方已有状态 3，滚动 {0} 格到 offset={1}",
                            delta,
                            targetOffset));
                }
            }

            if (subaruListBoundaryReached)
            {
                return SkillPointSearchDecision.StopWithReset(
                    subaruListBoundaryReason,
                    subaruListBoundaryReason + "，没有剩余可点技术点的斯巴鲁车辆。");
            }

            if (IsCompletionBoundaryReached())
            {
                return SkillPointSearchDecision.StopWithReset(
                    "没有剩余待点技术点的目标车",
                    "没有状态 3，且最后一个 2 的下一个格子已确认是 1。");
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
                "目标格: col={0}, row={1}（虚拟列表 3）",
                target.Col,
                target.Row);
            RememberVehicleResumeOffset();
            UpdateOverlay(null, null, null, target);
            Console.WriteLine("[TARGET] planned row={0} col={1}", target.Row, target.Col);
        }
    }
}
