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
            observation.PerformanceScoreMatches = FindPerformanceScoreMatches(snapshot);
            Dictionary<CellKey, PerformanceReading> performanceReadings = MapPerformanceReadings(snapshot);
            foreach (KeyValuePair<CellKey, PerformanceReading> pair in performanceReadings)
            {
                observation.PerformanceScores[pair.Key] = pair.Value.Score;
                if (!string.IsNullOrEmpty(pair.Value.ClassName)) observation.PerformanceClasses[pair.Key] = pair.Value.ClassName;
            }

            observation.TargetCells = MapTargetCells(observation.TargetMatches);
            observation.ManufacturerCells = MapVisibleCellsIncludingSelectedCell(observation.ManufacturerMatches);

            foreach (KeyValuePair<CellKey, int> pair in observation.PerformanceScores)
            {
                if (observation.TargetCells.Contains(pair.Key) && pair.Value == 600) observation.DeletableCells.Add(pair.Key);
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

            foreach (KeyValuePair<CellKey, int> pair in observation.PerformanceScores)
            {
                if (!observation.TargetCells.Contains(pair.Key)) continue;
                if (pair.Value != 900) continue;
                if (observation.ValidNewCells.Contains(pair.Key)) continue;
                if (observation.DeletableCells.Contains(pair.Key)) continue;
                if (observation.InvalidNewCells.Contains(pair.Key)) continue;
                observation.DriveCells.Add(pair.Key);
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
                new OcrFieldGroup("性能分", observation.PerformanceScoreMatches));
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
                observation.PerformanceClasses,
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
                + ", 性能分=" + observation.PerformanceScoreMatches.Count
                + ", 斯巴鲁=" + observation.ManufacturerMatches.Count
                + ", 分数=" + observation.PerformanceScores.Count
                + ", 空格=" + observation.BlankCells.Count
                + ", 3=" + observation.ValidNewCells.Count
                + ", 4=" + observation.DeletableCells.Count
                + ", 开车候选=" + observation.DriveCells.Count
                + suffix;
        }

        private Dictionary<CellKey, PerformanceReading> MapPerformanceReadings(OcrSnapshot snapshot)
        {
            Dictionary<CellKey, PerformanceReading> result = new Dictionary<CellKey, PerformanceReading>();
            if (snapshot == null || snapshot.Words == null) return result;

            foreach (OcrMatch match in snapshot.Words)
            {
                string performanceClass;
                int score;
                if (!TryParsePerformanceReading(match.Text, out performanceClass, out score)) continue;

                CellKey cell;
                Point center = match.RectCenter();
                if (!grid.MapPoint(center.X, center.Y, out cell)) continue;
                if (!IsPerformanceScorePosition(match, cell)) continue;

                PerformanceReading old;
                if (!result.TryGetValue(cell, out old) ||
                    score > old.Score ||
                    (score == old.Score && string.IsNullOrEmpty(old.ClassName) && !string.IsNullOrEmpty(performanceClass)))
                {
                    result[cell] = new PerformanceReading(performanceClass, score);
                }
            }

            return result;
        }

        private List<OcrMatch> FindPerformanceScoreMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            if (snapshot == null || snapshot.Words == null) return result;

            foreach (OcrMatch match in snapshot.Words)
            {
                string performanceClass;
                int score;
                if (!TryParsePerformanceReading(match.Text, out performanceClass, out score)) continue;
                CellKey cell;
                Point center = match.RectCenter();
                if (!grid.MapPoint(center.X, center.Y, out cell)) continue;
                if (!IsPerformanceScorePosition(match, cell)) continue;
                result.Add(match);
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

        private bool IsPerformanceScorePosition(OcrMatch match, CellKey cell)
        {
            if (match == null) return false;
            if (grid.CellStepX <= 0 || grid.CellStepY <= 0) return false;

            Point center = match.RectCenter();
            double cellLeft = grid.AnchorOriginX + cell.Col * grid.CellStepX;
            double cellTop = grid.AnchorOriginY + cell.Row * grid.CellStepY;
            double relativeX = (center.X - cellLeft) / grid.CellStepX;
            double relativeY = (center.Y - cellTop) / grid.CellStepY;
            return relativeX >= 0.5 && relativeY >= 0.5;
        }

        private static bool TryParsePerformanceReading(string text, out string performanceClass, out int score)
        {
            performanceClass = "";
            score = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string normalized = Regex.Replace((text ?? "").Trim().ToUpperInvariant(), @"\s+", " ");

            Match match = Regex.Match(normalized, @"^([1-9]\d{2})$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out score))
            {
                return score > 0 && score < 1000;
            }

            match = Regex.Match(normalized, @"^(S2|S1|S|A|B|C|D|R)\s*([1-9]\d{2})$");
            if (!match.Success) return false;
            performanceClass = match.Groups[1].Value;
            if (!int.TryParse(match.Groups[2].Value, out score)) return false;
            return score > 0 && score < 1000;
        }

        private sealed class PerformanceReading
        {
            public readonly string ClassName;
            public readonly int Score;

            public PerformanceReading(string className, int score)
            {
                ClassName = className ?? "";
                Score = score;
            }
        }
    }
}
