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
        private void BuildGrid()
        {
            SetStatus("build grid", "缺少手动框选的车辆格子设置");
            throw new InvalidOperationException("缺少车辆格子设置。删除 config/user-settings.json 后重新启动，并框选所有完整可见车辆格子的整体区域。");
        }

        private HashSet<CellKey> MapTargetCells(List<OcrMatch> matches)
        {
            HashSet<CellKey> cells = new HashSet<CellKey>();
            foreach (OcrMatch match in matches)
            {
                CellKey cell;
                Point center = match.RectCenter();
                if (grid.MapPoint(center.X, center.Y, out cell)) cells.Add(cell);
            }
            return cells;
        }

        private HashSet<CellKey> MapVisibleCellsIncludingSelectedCell(List<OcrMatch> matches)
        {
            HashSet<CellKey> cells = new HashSet<CellKey>();
            foreach (OcrMatch match in matches)
            {
                CellKey cell;
                Point center = match.RectCenter();
                if (grid.MapPoint(center.X, center.Y, out cell)) cells.Add(cell);
            }
            return cells;
        }
    }
}
