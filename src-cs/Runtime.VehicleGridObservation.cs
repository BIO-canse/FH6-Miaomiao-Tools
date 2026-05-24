using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private VehicleGridObservation BuildVehicleGridObservation(OcrSnapshot snapshot, int scrollIndex)
        {
            VehicleGridObservation observation = new VehicleGridObservation();
            observation.Snapshot = snapshot;
            observation.ScrollIndex = scrollIndex;

            observation.TargetMatches = FindTargetVehicleMatches(snapshot);
            observation.NewBadgeMatches = FindNewBadgeMatches(snapshot);
            observation.ManufacturerMatches = FindManufacturerMatches(snapshot);
            observation.DeleteMarkerMatches = FindDeleteMarkerMatches(snapshot);
            observation.DriveMarkerMatches = FindDriveMarkerMatches(snapshot);
            observation.PerformanceScores = MapPerformanceScores(snapshot);

            observation.TargetCells = MapTargetCells(observation.TargetMatches);
            observation.ManufacturerCells = MapVisibleCellsIncludingSelectedCell(observation.ManufacturerMatches);

            foreach (KeyValuePair<CellKey, int> pair in observation.PerformanceScores)
            {
                if (observation.TargetCells.Contains(pair.Key) && pair.Value == 600) observation.DeletableCells.Add(pair.Key);
            }

            foreach (KeyValuePair<CellKey, int> pair in observation.PerformanceScores)
            {
                if (observation.TargetCells.Contains(pair.Key) && pair.Value == 900) observation.DriveCells.Add(pair.Key);
            }

            foreach (OcrMatch match in observation.NewBadgeMatches)
            {
                CellKey cell;
                if (!grid.MapPoint(match.RectCenter().X, match.RectCenter().Y, out cell)) continue;
                int score;
                bool scoreIs600 = observation.PerformanceScores.TryGetValue(cell, out score) && score == 600;
                if (observation.TargetCells.Contains(cell) && scoreIs600) observation.ValidNewCells.Add(cell);
                else observation.InvalidNewCells.Add(cell);
            }

            HashSet<CellKey> cellsWithAnyText = MapCellsWithAnyText(snapshot);
            foreach (CellKey cell in grid.AllCellKeys())
            {
                if (!cellsWithAnyText.Contains(cell)) observation.BlankCells.Add(cell);
            }

            return observation;
        }

        private void ApplyVehicleGridObservation(VehicleGridObservation observation)
        {
            SetOcrFields(
                new OcrFieldGroup("车名", observation.TargetMatches),
                new OcrFieldGroup("全新", observation.NewBadgeMatches),
                new OcrFieldGroup("斯巴鲁", observation.ManufacturerMatches),
                new OcrFieldGroup("600", observation.DeleteMarkerMatches),
                new OcrFieldGroup("900", observation.DriveMarkerMatches));
            UpdateSubaruListBoundary(observation.ManufacturerMatches);
            vehicleList.ApplyFullObservation(
                grid.VisibleColumns,
                observation.TargetCells,
                observation.ValidNewCells,
                observation.InvalidNewCells,
                observation.DeletableCells,
                observation.DriveCells,
                observation.ManufacturerCells,
                observation.PerformanceScores,
                observation.BlankCells);
        }

        private CellKey? LeftTopCell(HashSet<CellKey> cells)
        {
            if (cells.Count == 0) return null;
            return cells.OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row).First();
        }

        private string FullObservationSummary(VehicleGridObservation observation, string suffix)
        {
            return "OCR: IMPREZA=" + observation.TargetMatches.Count
                + ", 全新=" + observation.NewBadgeMatches.Count
                + ", 600=" + observation.DeleteMarkerMatches.Count
                + ", 900=" + observation.DriveMarkerMatches.Count
                + ", 斯巴鲁=" + observation.ManufacturerMatches.Count
                + ", 分数=" + observation.PerformanceScores.Count
                + ", 空格=" + observation.BlankCells.Count
                + ", 3=" + observation.ValidNewCells.Count
                + ", 4=" + observation.DeletableCells.Count
                + ", 5=" + observation.DriveCells.Count
                + suffix;
        }

        private Dictionary<CellKey, int> MapPerformanceScores(OcrSnapshot snapshot)
        {
            Dictionary<CellKey, int> result = new Dictionary<CellKey, int>();
            if (snapshot == null || snapshot.Words == null) return result;

            foreach (OcrMatch match in snapshot.Words)
            {
                int score;
                if (!TryParsePerformanceScore(match.Text, out score)) continue;

                CellKey cell;
                Point center = match.RectCenter();
                if (!grid.MapPoint(center.X, center.Y, out cell)) continue;

                int old;
                if (!result.TryGetValue(cell, out old) || score > old)
                {
                    result[cell] = score;
                }
            }

            return result;
        }

        private HashSet<CellKey> MapCellsWithAnyText(OcrSnapshot snapshot)
        {
            HashSet<CellKey> result = new HashSet<CellKey>();
            if (snapshot == null || snapshot.Words == null) return result;

            foreach (OcrMatch match in snapshot.Words)
            {
                if (string.IsNullOrWhiteSpace(match.Text)) continue;
                CellKey cell;
                Point center = match.RectCenter();
                if (grid.MapPoint(center.X, center.Y, out cell)) result.Add(cell);
            }

            return result;
        }

        private static bool TryParsePerformanceScore(string text, out int score)
        {
            score = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            Match match = Regex.Match(text, @"(?<!\d)([1-9]\d{2})(?!\d)");
            if (!match.Success) return false;
            if (!int.TryParse(match.Groups[1].Value, out score)) return false;
            return score > 0 && score < 1000;
        }
    }
}
