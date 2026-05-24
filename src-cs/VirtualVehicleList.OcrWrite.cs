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
                            SetCellWithLog(global, next, "SET_REFRESH", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
                            continue;
                        }
                    }

                    SetCellWithLog(global, next, "SET", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
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
                            SetCellWithLog(global, next, "DELETE_SCAN_REFRESH", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
                            continue;
                        }
                    }

                    SetCellWithLog(global, next, "DELETE_SCAN_SET", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
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
                    string newState = FH6AutomationConstants.VehicleState.None;

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
                            SetCellWithLog(global, next, "DRIVE_SCAN_REFRESH", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
                            continue;
                        }
                    }

                    SetCellWithLog(global, next, "DRIVE_SCAN_SET", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
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
            ApplyFullObservation(
                visibleColumns,
                targets,
                validNew,
                invalidNew,
                deletable,
                driveCells,
                manufacturerCells,
                new Dictionary<CellKey, int>(),
                new HashSet<CellKey>());
        }

        public void ApplyFullObservation(
            int visibleColumns,
            HashSet<CellKey> targets,
            HashSet<CellKey> validNew,
            HashSet<CellKey> invalidNew,
            HashSet<CellKey> deletable,
            HashSet<CellKey> driveCells,
            HashSet<CellKey> manufacturerCells,
            Dictionary<CellKey, int> performanceScores,
            HashSet<CellKey> blankCells)
        {
            ApplyObservationInternal(
                visibleColumns,
                0,
                true,
                targets,
                validNew,
                invalidNew,
                deletable,
                driveCells,
                manufacturerCells,
                performanceScores,
                blankCells,
                "FULL_SCAN_SET",
                "full_visible_observation");
        }

        public void ApplyTableBuildObservation(
            int visibleColumns,
            int ignoredLeadingColumns,
            HashSet<CellKey> targets,
            HashSet<CellKey> validNew,
            HashSet<CellKey> invalidNew,
            HashSet<CellKey> deletable,
            HashSet<CellKey> driveCells,
            HashSet<CellKey> manufacturerCells,
            Dictionary<CellKey, int> performanceScores,
            HashSet<CellKey> blankCells)
        {
            ApplyObservationInternal(
                visibleColumns,
                ignoredLeadingColumns,
                true,
                targets,
                validNew,
                invalidNew,
                deletable,
                driveCells,
                manufacturerCells,
                performanceScores,
                blankCells,
                "TABLE_BUILD_SET",
                "table_build_observation");
        }

        private void ApplyObservationInternal(
            int visibleColumns,
            int ignoredLeadingColumns,
            bool respectOcrWritePolicy,
            HashSet<CellKey> targets,
            HashSet<CellKey> validNew,
            HashSet<CellKey> invalidNew,
            HashSet<CellKey> deletable,
            HashSet<CellKey> driveCells,
            HashSet<CellKey> manufacturerCells,
            Dictionary<CellKey, int> performanceScores,
            HashSet<CellKey> blankCells,
            string logPrefix,
            string persistReason)
        {
            if (visibleColumns <= 0) return;
            ObservationCount++;
            ignoredLeadingColumns = ignoredLeadingColumns < 0 ? 0 : ignoredLeadingColumns;
            targets = targets ?? new HashSet<CellKey>();
            validNew = validNew ?? new HashSet<CellKey>();
            invalidNew = invalidNew ?? new HashSet<CellKey>();
            deletable = deletable ?? new HashSet<CellKey>();
            driveCells = driveCells ?? new HashSet<CellKey>();
            manufacturerCells = manufacturerCells ?? new HashSet<CellKey>();
            performanceScores = performanceScores ?? new Dictionary<CellKey, int>();
            blankCells = blankCells ?? new HashSet<CellKey>();

            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    if (col < ignoredLeadingColumns) continue;
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (respectOcrWritePolicy && ShouldSkipOcrWriteCell(local, global)) continue;

                    bool isDrive = driveCells.Contains(local);
                    bool isDelete = deletable.Contains(local);
                    bool isValidNew = validNew.Contains(local);
                    bool isInvalidNew = invalidNew.Contains(local);
                    bool isTarget = targets.Contains(local) || isDelete || isValidNew;
                    bool isManufacturer = manufacturerCells.Contains(local) || isTarget;
                    int performanceScore;
                    if (!performanceScores.TryGetValue(local, out performanceScore)) performanceScore = -1;

                    string newState = FH6AutomationConstants.VehicleState.None;
                    if (isValidNew) newState = FH6AutomationConstants.VehicleState.ValidNewName;
                    else if (isDelete) newState = FH6AutomationConstants.VehicleState.DeletableName;
                    else if (isInvalidNew) newState = FH6AutomationConstants.VehicleState.InvalidNew;
                    else if (isTarget) newState = FH6AutomationConstants.VehicleState.None;
                    else if (blankCells.Contains(local)) newState = FH6AutomationConstants.VehicleState.BlankName;

                    VirtualVehicleCell next = new VirtualVehicleCell();
                    next.Row = row;
                    next.Col = global.Col;
                    next.IsManufacturer = isManufacturer;
                    next.IsTarget = isTarget;
                    next.NewState = newState;
                    next.PerformanceScore = performanceScore;
                    next.LastSeenOffset = CurrentOffset;
                    next.SeenCount = 1;

                    VirtualVehicleCell old;
                    if (cells.TryGetValue(global, out old))
                    {
                        next.SeenCount = old.SeenCount + 1;
                        if (old.IsManufacturer == next.IsManufacturer && old.IsTarget == next.IsTarget && old.NewState == next.NewState && old.LastSeenOffset == next.LastSeenOffset && old.PerformanceScore == next.PerformanceScore)
                        {
                            SetCellWithLog(global, next, logPrefix + "_REFRESH", string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
                            continue;
                        }
                    }

                    SetCellWithLog(global, next, logPrefix, string.Format(CultureInfo.InvariantCulture, "local_row={0} local_col={1} obs={2}", row, col, ObservationCount));
                    EditCount++;
                    Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} row={1} col={2} subaru={3} target={4} state={5} score={6} offset={7} obs={8}",
                        logPrefix,
                        row,
                        global.Col,
                        isManufacturer,
                        isTarget,
                        StateCode(next),
                        performanceScore,
                        CurrentOffset,
                        ObservationCount));
                }
            }
            Persist(persistReason);
        }

        private bool ShouldSkipOcrWriteCell(CellKey local, CellKey global)
        {
            return VehicleGridOcrPolicy.ShouldSkipOcrWrite(global, cells.ContainsKey(global));
        }
    }
}
