using System.Collections.Generic;
using System.Linq;
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

            observation.TargetCells = MapTargetCells(observation.TargetMatches);
            observation.ManufacturerCells = MapVisibleCellsIncludingSelectedCell(observation.ManufacturerMatches);

            HashSet<CellKey> deleteMarkerCells = MapTargetCells(observation.DeleteMarkerMatches);
            foreach (CellKey cell in deleteMarkerCells)
            {
                if (observation.TargetCells.Contains(cell)) observation.DeletableCells.Add(cell);
            }

            HashSet<CellKey> driveMarkerCells = MapTargetCells(observation.DriveMarkerMatches);
            foreach (CellKey cell in driveMarkerCells)
            {
                if (observation.TargetCells.Contains(cell)) observation.DriveCells.Add(cell);
            }

            foreach (OcrMatch match in observation.NewBadgeMatches)
            {
                CellKey cell;
                if (!grid.MapPoint(match.RectCenter().X, match.RectCenter().Y, out cell)) continue;
                if (observation.TargetCells.Contains(cell)) observation.ValidNewCells.Add(cell);
                else observation.InvalidNewCells.Add(cell);
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
                observation.ManufacturerCells);
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
                + ", 3=" + observation.ValidNewCells.Count
                + ", 4=" + observation.DeletableCells.Count
                + ", 5=" + observation.DriveCells.Count
                + suffix;
        }
    }
}
