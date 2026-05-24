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
        private CellKey FindValidNewCell()
        {
            SetStatus("find valid new badge", UseTableOnlyVehicleSearch() ? "按定表虚拟列表规划下一步：选择、滚动或停止，不 OCR" : "按虚拟表规划下一步：选择、滚动、OCR 或停止");
            OcrSnapshot last = null;
            for (int i = 0; i < config.MaxFindNewScrolls; i++)
            {
                SkillPointSearchDecision decision = PlanSkillPointSearch();
                if (decision.Kind == SkillPointSearchActionKind.Select)
                {
                    PrepareSkillPointTarget(decision.Target, decision.Reason);
                    return decision.Target;
                }

                if (decision.Kind == SkillPointSearchActionKind.StopWithReset)
                {
                    CompleteSkillSearchWithoutTarget(decision.Reason, decision.Message);
                }

                if (decision.Kind == SkillPointSearchActionKind.Scroll)
                {
                    SetOcrSummary("虚拟列表: " + decision.Reason);
                    ScrollVehicleListDown(decision.ScrollTicks, "find valid new badge");
                    continue;
                }

                if (UseTableOnlyVehicleSearch())
                {
                    FailTableOnlyVehicleSearch("skill", decision.Reason);
                }

                DebugGate("find valid new badge", "OCR 车型和全新，第 " + (i + 1) + " 次：" + decision.Reason);
                last = ReadVehicleGridScreen();
                RecordVisibleGridFromOcr(last, i, true);
            }
            Fail(last, "valid-new-not-found");
            throw new InvalidOperationException("unreachable");
        }

        private CellKey? RecordVisibleGridFromOcr(OcrSnapshot snapshot, int scrollIndex, bool dumpWhenNoChosen)
        {
            VehicleGridObservation observation = BuildVehicleGridObservation(snapshot, scrollIndex);
            ApplyVehicleGridObservation(observation);
            CellKey? chosen = LeftTopCell(observation.ValidNewCells);
            SetOcrSummary(FullObservationSummary(observation, ", 找全新滚动=" + scrollIndex));
            lastTargetSummary = chosen.HasValue ? string.Format("目标格: col={0}, row={1}", chosen.Value.Col, chosen.Value.Row) : "目标格: 未找到有效全新";
            UpdateOverlay(observation.TargetCells, observation.ValidNewCells, observation.InvalidNewCells, observation.DeletableCells, observation.DriveCells, chosen);
            if (!chosen.HasValue && dumpWhenNoChosen) WriteOcrDump(snapshot, "valid-new-current");
            return chosen;
        }
    }
}
