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

namespace FH6SkillPointOcr
{
    internal sealed class GridGeometry
    {
        private readonly Config config;
        private readonly Dictionary<CellKey, RectangleF> cells = new Dictionary<CellKey, RectangleF>();
        public int Rows { get { return config.GridRows; } }
        public int CalibrationCount { get; private set; }
        public int CalibrationLimit { get { return 1; } }
        public int VisibleColumns { get; private set; }
        public bool Locked { get; private set; }
        public bool Ready { get { return cells.Count > 0; } }
        public double AnchorOriginX { get; private set; }
        public double AnchorOriginY { get; private set; }
        public double CellStepX { get; private set; }
        public double CellStepY { get; private set; }

        public GridGeometry(Config config)
        {
            this.config = config;
            if (config.VisibleColumns > 0 && config.GridRows > 0 && config.GridCellWidth > 0 && config.GridCellHeight > 0)
            {
                AnchorOriginX = config.GridCellLeft;
                AnchorOriginY = config.GridCellTop;
                CellStepX = config.GridCellWidth;
                CellStepY = config.GridCellHeight;
                VisibleColumns = config.VisibleColumns;
                BuildCells();
                CalibrationCount = 1;
                Locked = true;
            }
        }

        public bool MapPoint(int x, int y, out CellKey cell)
        {
            foreach (KeyValuePair<CellKey, RectangleF> pair in cells)
            {
                if (pair.Value.Contains(x, y))
                {
                    cell = pair.Key;
                    return true;
                }
            }
            cell = new CellKey();
            return false;
        }

        public Rectangle CaptureBounds(float padding)
        {
            if (cells.Count == 0) return Rectangle.Empty;
            float left = cells.Values.Min(r => r.Left) - padding;
            float top = cells.Values.Min(r => r.Top) - padding;
            float right = cells.Values.Max(r => r.Right) + padding;
            float bottom = cells.Values.Max(r => r.Bottom) + padding;
            return Rectangle.FromLTRB(
                (int)Math.Floor(left),
                (int)Math.Floor(top),
                (int)Math.Ceiling(right),
                (int)Math.Ceiling(bottom));
        }

        public bool TryGetFirstVisibleCellCenter(out Point point)
        {
            point = Point.Empty;
            if (VisibleColumns <= 0 || Rows <= 0) return false;

            RectangleF rect;
            if (!cells.TryGetValue(new CellKey(0, 0), out rect)) return false;
            point = new Point(
                (int)Math.Round(rect.Left + rect.Width / 2),
                (int)Math.Round(rect.Top + rect.Height / 2));
            return true;
        }

        public bool TryGetCellCenter(CellKey cell, out Point point)
        {
            point = Point.Empty;
            RectangleF rect;
            if (!cells.TryGetValue(cell, out rect)) return false;
            point = new Point(
                (int)Math.Round(rect.Left + rect.Width / 2),
                (int)Math.Round(rect.Top + rect.Height / 2));
            return true;
        }

        public List<CellKey> AllCellKeys()
        {
            return cells.Keys.OrderBy(c => c.Col).ThenBy(c => c.Row).ToList();
        }

        public List<CellView> CellViews(HashSet<CellKey> targets, HashSet<CellKey> validNew, HashSet<CellKey> invalidNew, CellKey? chosen)
        {
            return CellViews(targets, validNew, invalidNew, new HashSet<CellKey>(), new HashSet<CellKey>(), chosen);
        }

        public List<CellView> CellViews(HashSet<CellKey> targets, HashSet<CellKey> validNew, HashSet<CellKey> invalidNew, HashSet<CellKey> deletable, CellKey? chosen)
        {
            return CellViews(targets, validNew, invalidNew, deletable, new HashSet<CellKey>(), chosen);
        }

        public List<CellView> CellViews(HashSet<CellKey> targets, HashSet<CellKey> validNew, HashSet<CellKey> invalidNew, HashSet<CellKey> deletable, HashSet<CellKey> drive, CellKey? chosen)
        {
            List<CellView> result = new List<CellView>();
            foreach (KeyValuePair<CellKey, RectangleF> pair in cells.OrderBy(p => p.Key.Col).ThenBy(p => p.Key.Row))
            {
                string state = "empty";
                if (targets.Contains(pair.Key)) state = "state2";
                if (validNew.Contains(pair.Key)) state = "state3";
                if (deletable.Contains(pair.Key)) state = "state4";
                if (drive.Contains(pair.Key)) state = "state5";
                bool isChosen = chosen.HasValue && pair.Key.Equals(chosen.Value);
                result.Add(new CellView(pair.Key.Row, pair.Key.Col, pair.Value, state, isChosen));
            }
            return result;
        }

        private void BuildCells()
        {
            cells.Clear();
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < VisibleColumns; col++)
                {
                    float left = (float)(AnchorOriginX + col * CellStepX);
                    float top = (float)(AnchorOriginY + row * CellStepY);
                    cells[new CellKey(row, col)] = new RectangleF(left, top, (float)CellStepX, (float)CellStepY);
                }
            }
        }

    }

}
