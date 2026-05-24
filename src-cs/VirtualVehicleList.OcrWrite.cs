using System.Collections.Generic;
using System.Globalization;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class VirtualVehicleList
    {
        public void ApplyVisibleObservation(
            int visibleColumns,
            HashSet<CellKey> targets,
            HashSet<CellKey> validNew,
            HashSet<CellKey> invalidNew,
            HashSet<CellKey> manufacturerCells)
        {
            if (visibleColumns <= 0) return;
            ObservationCount++;

            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipOcrWriteCell(local, global)) continue;
                    bool isTarget = targets.Contains(local);
                    bool isManufacturer = manufacturerCells.Contains(local) || isTarget || validNew.Contains(local);
                    string newState = FH6AutomationConstants.VehicleState.None;
                    if (validNew.Contains(local)) newState = FH6AutomationConstants.VehicleState.ValidNewName;
                    else if (invalidNew.Contains(local)) newState = FH6AutomationConstants.VehicleState.InvalidNew;

                    VirtualVehicleCell next = new VirtualVehicleCell();
                    next.Row = row;
                    next.Col = global.Col;
                    next.IsManufacturer = isManufacturer;
                    next.IsTarget = isTarget;
                    next.NewState = newState;
                    next.LastSeenOffset = CurrentOffset;
                    next.SeenCount = 1;

                    VirtualVehicleCell old;
                    if (cells.TryGetValue(global, out old))
                    {
                        next.SeenCount = old.SeenCount + 1;
                        if (old.IsManufacturer == next.IsManufacturer && old.IsTarget == next.IsTarget && old.NewState == next.NewState && old.LastSeenOffset == next.LastSeenOffset)
                        {
                            cells[global] = next;
                            continue;
                        }
                    }

                    cells[global] = next;
                    EditCount++;
                    Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "SET row={0} col={1} subaru={2} target={3} new={4} offset={5} obs={6}",
                        row,
                        global.Col,
                        isManufacturer,
                        isTarget,
                        newState,
                        CurrentOffset,
                        ObservationCount));
                }
            }
            Persist("visible_observation");
        }

        public void ApplyVisibleDeleteObservation(
            int visibleColumns,
            HashSet<CellKey> targets,
            HashSet<CellKey> deletable,
            HashSet<CellKey> manufacturerCells)
        {
            if (visibleColumns <= 0) return;
            ObservationCount++;

            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipOcrWriteCell(local, global)) continue;
                    bool isDelete = deletable.Contains(local);
                    bool isTarget = isDelete || targets.Contains(local);
                    bool isManufacturer = manufacturerCells.Contains(local) || isTarget;
                    string newState = isDelete ? FH6AutomationConstants.VehicleState.DeletableName : FH6AutomationConstants.VehicleState.None;

                    VirtualVehicleCell next = new VirtualVehicleCell();
                    next.Row = row;
                    next.Col = global.Col;
                    next.IsManufacturer = isManufacturer;
                    next.IsTarget = isTarget;
                    next.NewState = newState;
                    next.LastSeenOffset = CurrentOffset;
                    next.SeenCount = 1;

                    VirtualVehicleCell old;
                    if (cells.TryGetValue(global, out old))
                    {
                        next.SeenCount = old.SeenCount + 1;
                        if (old.IsManufacturer == next.IsManufacturer && old.IsTarget == next.IsTarget && old.NewState == next.NewState && old.LastSeenOffset == next.LastSeenOffset)
                        {
                            cells[global] = next;
                            continue;
                        }
                    }

                    cells[global] = next;
                    EditCount++;
                    Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "DELETE_SCAN_SET row={0} col={1} subaru={2} target={3} state={4} offset={5} obs={6}",
                        row,
                        global.Col,
                        isManufacturer,
                        isTarget,
                        StateCode(next),
                        CurrentOffset,
                        ObservationCount));
                }
            }
            Persist("delete_visible_observation");
        }

        public void ApplyVisibleDriveObservation(
            int visibleColumns,
            HashSet<CellKey> targets,
            HashSet<CellKey> driveCells,
            HashSet<CellKey> manufacturerCells)
        {
            if (visibleColumns <= 0) return;
            ObservationCount++;

            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipOcrWriteCell(local, global)) continue;
                    bool isDrive = driveCells.Contains(local);
                    bool isTarget = isDrive || targets.Contains(local);
                    bool isManufacturer = manufacturerCells.Contains(local) || isTarget;
                    string newState = isDrive
                        ? FH6AutomationConstants.VehicleState.DriveName
                        : (isTarget ? FH6AutomationConstants.VehicleState.DriveCheckedName : FH6AutomationConstants.VehicleState.None);

                    VirtualVehicleCell next = new VirtualVehicleCell();
                    next.Row = row;
                    next.Col = global.Col;
                    next.IsManufacturer = isManufacturer;
                    next.IsTarget = isTarget;
                    next.NewState = newState;
                    next.LastSeenOffset = CurrentOffset;
                    next.SeenCount = 1;

                    VirtualVehicleCell old;
                    if (cells.TryGetValue(global, out old))
                    {
                        next.SeenCount = old.SeenCount + 1;
                        if (old.IsManufacturer == next.IsManufacturer && old.IsTarget == next.IsTarget && old.NewState == next.NewState && old.LastSeenOffset == next.LastSeenOffset)
                        {
                            cells[global] = next;
                            continue;
                        }
                    }

                    cells[global] = next;
                    EditCount++;
                    Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "DRIVE_SCAN_SET row={0} col={1} subaru={2} target={3} state={4} offset={5} obs={6}",
                        row,
                        global.Col,
                        isManufacturer,
                        isTarget,
                        StateCode(next),
                        CurrentOffset,
                        ObservationCount));
                }
            }
            Persist("drive_visible_observation");
        }

        public void ApplyFullObservation(
            int visibleColumns,
            HashSet<CellKey> targets,
            HashSet<CellKey> validNew,
            HashSet<CellKey> invalidNew,
            HashSet<CellKey> deletable,
            HashSet<CellKey> driveCells,
            HashSet<CellKey> manufacturerCells)
        {
            if (visibleColumns <= 0) return;
            ObservationCount++;

            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipOcrWriteCell(local, global)) continue;

                    bool isDrive = driveCells.Contains(local);
                    bool isDelete = deletable.Contains(local);
                    bool isValidNew = validNew.Contains(local);
                    bool isInvalidNew = invalidNew.Contains(local);
                    bool isTarget = targets.Contains(local) || isDrive || isDelete || isValidNew;
                    bool isManufacturer = manufacturerCells.Contains(local) || isTarget;

                    string newState = FH6AutomationConstants.VehicleState.None;
                    if (isValidNew) newState = FH6AutomationConstants.VehicleState.ValidNewName;
                    else if (isDrive) newState = FH6AutomationConstants.VehicleState.DriveName;
                    else if (isDelete) newState = FH6AutomationConstants.VehicleState.DeletableName;
                    else if (isInvalidNew) newState = FH6AutomationConstants.VehicleState.InvalidNew;
                    else if (isTarget) newState = FH6AutomationConstants.VehicleState.DriveCheckedName;

                    VirtualVehicleCell next = new VirtualVehicleCell();
                    next.Row = row;
                    next.Col = global.Col;
                    next.IsManufacturer = isManufacturer;
                    next.IsTarget = isTarget;
                    next.NewState = newState;
                    next.LastSeenOffset = CurrentOffset;
                    next.SeenCount = 1;

                    VirtualVehicleCell old;
                    if (cells.TryGetValue(global, out old))
                    {
                        next.SeenCount = old.SeenCount + 1;
                        if (old.IsManufacturer == next.IsManufacturer && old.IsTarget == next.IsTarget && old.NewState == next.NewState && old.LastSeenOffset == next.LastSeenOffset)
                        {
                            cells[global] = next;
                            continue;
                        }
                    }

                    cells[global] = next;
                    EditCount++;
                    Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "FULL_SCAN_SET row={0} col={1} subaru={2} target={3} state={4} offset={5} obs={6}",
                        row,
                        global.Col,
                        isManufacturer,
                        isTarget,
                        StateCode(next),
                        CurrentOffset,
                        ObservationCount));
                }
            }
            Persist("full_visible_observation");
        }

        private bool ShouldSkipOcrWriteCell(CellKey local, CellKey global)
        {
            return VehicleGridOcrPolicy.ShouldSkipOcrWrite(global, cells.ContainsKey(global));
        }
    }
}
