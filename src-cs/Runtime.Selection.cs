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
        private CellKey MoveSelectionToCell(CellKey targetGlobal)
        {
            if (targetGlobal.Row < 0 || targetGlobal.Row >= config.GridRows || targetGlobal.Col < 0) throw new InvalidOperationException("目标格子越界");
            int dx = targetGlobal.Col - vehicleList.CurrentOffset;
            lastTargetSummary = string.Format("目标格: global_col={0}, row={1}", targetGlobal.Col, targetGlobal.Row);
            DebugGate(
                "move selection global row=" + targetGlobal.Row + " col=" + targetGlobal.Col,
                string.Format(
                    "{0} x{1}, Down x{2}, Enter",
                    dx >= 0 ? "Right" : "Left",
                    Math.Abs(dx),
                    targetGlobal.Row));

            MoveVehicleListViewByKeyboard(dx, "skill target path");
            for (int i = 0; i < targetGlobal.Row; i++) input.Tap("DOWN");

            CellKey selectedLocal = new CellKey(targetGlobal.Row, 0);
            input.Tap("ENTER");
            input.SleepMs(FH6AutomationConstants.Timing.OneSecondMs);
            UpdateOverlay(null, null, null, selectedLocal);
            MoveMouseToFirstVisibleCellCenter("idle in vehicle list after selecting target");
            return selectedLocal;
        }

        private void MoveVehicleListViewByKeyboard(int deltaColumns, string reason)
        {
            if (deltaColumns == 0) return;

            string key = deltaColumns > 0 ? "RIGHT" : "LEFT";
            int count = Math.Abs(deltaColumns);
            for (int i = 0; i < count; i++) input.Tap(key);

            if (deltaColumns > 0)
            {
                vehicleList.KeyboardMoveViewRight(deltaColumns, reason);
            }
            else
            {
                vehicleList.KeyboardMoveViewLeft(-deltaColumns, reason);
            }
            UpdateOverlay(null, null, null, null);
        }

        private CellKey? VisibleLocalFromGlobal(CellKey global)
        {
            if (!grid.Ready) return null;
            int localCol = global.Col - vehicleList.CurrentOffset;
            if (localCol < 0 || localCol >= grid.VisibleColumns) return null;
            if (global.Row < 0 || global.Row >= config.GridRows) return null;
            return new CellKey(global.Row, localCol);
        }
    }
}
