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
        private void MoveSelectionToCell(CellKey target)
        {
            if (target.Row < 0 || target.Row >= config.GridRows || target.Col < 0) throw new InvalidOperationException("目标格子越界");
            lastTargetSummary = string.Format("目标格: col={0}, row={1}", target.Col, target.Row);
            DebugGate("move selection row=" + target.Row + " col=" + target.Col, string.Format("Right x{0}, Down x{1}, Enter", target.Col, target.Row));
            for (int i = 0; i < target.Col; i++) input.Tap("RIGHT");
            for (int i = 0; i < target.Row; i++) input.Tap("DOWN");
            input.Tap("ENTER");
            input.SleepMs(FH6AutomationConstants.Timing.OneSecondMs);
            UpdateOverlay(null, null, null, target);
            MoveMouseToFirstVisibleCellCenter("idle in vehicle list after selecting target");
        }
    }
}
