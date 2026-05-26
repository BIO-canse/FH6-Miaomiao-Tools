using System;
using System.Drawing;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed class SemanticCellMapper
    {
        private readonly GridGeometry grid;

        public SemanticCellMapper(GridGeometry grid)
        {
            this.grid = grid;
        }

        public bool TryMapName(OcrMatch match, out CellKey cell)
        {
            cell = new CellKey();
            if (match == null) return false;
            Point center = match.RectCenter();
            return TryMapNamePoint(center.X, center.Y, out cell);
        }

        public bool TryMapBottomMarker(OcrMatch match, out CellKey cell)
        {
            cell = new CellKey();
            if (match == null) return false;
            Point center = match.RectCenter();
            return TryMapBottomMarkerPoint(center.X, center.Y, out cell);
        }

        public bool TryMapGeneric(OcrMatch match, out CellKey cell)
        {
            cell = new CellKey();
            if (match == null) return false;
            Point center = match.RectCenter();
            return grid.MapPoint(center.X, center.Y, out cell);
        }

        private bool TryMapNamePoint(double x, double y, out CellKey cell)
        {
            cell = new CellKey();
            double relativeX;
            double relativeY;
            int col;
            int row;
            if (!TryProjectPoint(x, y, out row, out col, out relativeX, out relativeY)) return false;

            if (relativeY >= FH6AutomationConstants.Ocr.NameLowerQuarterCarryToNextRow)
            {
                row++;
            }

            if (!InBounds(row, col)) return false;
            cell = new CellKey(row, col);
            return true;
        }

        private bool TryMapBottomMarkerPoint(double x, double y, out CellKey cell)
        {
            cell = new CellKey();
            double relativeX;
            double relativeY;
            int col;
            int row;
            if (!TryProjectPoint(x, y, out row, out col, out relativeX, out relativeY)) return false;
            if (relativeY < FH6AutomationConstants.Ocr.BottomMarkerMinRelativeY) return false;

            if (relativeX <= FH6AutomationConstants.Ocr.BottomMarkerPreviousColumnMaxRelativeX && col > 0)
            {
                col--;
            }

            if (!InBounds(row, col)) return false;
            cell = new CellKey(row, col);
            return true;
        }

        private bool TryProjectPoint(double x, double y, out int row, out int col, out double relativeX, out double relativeY)
        {
            row = 0;
            col = 0;
            relativeX = 0;
            relativeY = 0;

            if (grid == null || !grid.Ready) return false;
            if (grid.CellStepX <= 0 || grid.CellStepY <= 0) return false;

            double projectedX = (x - grid.AnchorOriginX) / grid.CellStepX;
            double projectedY = (y - grid.AnchorOriginY) / grid.CellStepY;
            col = (int)Math.Floor(projectedX);
            row = (int)Math.Floor(projectedY);
            relativeX = projectedX - col;
            relativeY = projectedY - row;

            return row >= -1 &&
                row <= grid.Rows &&
                col >= 0 &&
                col < grid.VisibleColumns;
        }

        private bool InBounds(int row, int col)
        {
            return row >= 0 &&
                row < grid.Rows &&
                col >= 0 &&
                col < grid.VisibleColumns;
        }
    }
}
