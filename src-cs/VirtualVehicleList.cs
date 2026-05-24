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
    internal sealed partial class VirtualVehicleList
    {
        private readonly int rows;
        private readonly string editLogPath;
        private readonly string snapshotPath;
        private readonly Dictionary<CellKey, VirtualVehicleCell> cells = new Dictionary<CellKey, VirtualVehicleCell>();
        private bool hasResumeOffset;

        public int CurrentOffset { get; private set; }
        public int ResumeOffset { get; private set; }
        public int ObservationCount { get; private set; }
        public int EditCount { get; private set; }
        public int LastKnownEmptyRun { get; private set; }
        public int LastSuggestedSkip { get; private set; }

        public VirtualVehicleList(int rows, string editLogPath, string snapshotPath, VirtualListLoadMode loadMode)
        {
            this.rows = rows;
            this.editLogPath = editLogPath;
            this.snapshotPath = snapshotPath;
            EnsureParentDirectory(snapshotPath);
            if (!string.IsNullOrEmpty(editLogPath))
            {
                EnsureParentDirectory(editLogPath);
                File.AppendAllText(editLogPath, "# VirtualVehicleList edit log\r\n", Encoding.UTF8);
            }
            if (loadMode == VirtualListLoadMode.FullState) LoadExistingSnapshot();
            EnsureReservedFirstCell("init");
            Persist(loadMode == VirtualListLoadMode.FullState ? "init_load_existing_full_state" : "init");
        }

        public bool ReloadFullStateFromDisk(string reason)
        {
            if (string.IsNullOrEmpty(snapshotPath) || !File.Exists(snapshotPath))
            {
                Log("RELOAD_FULL_STATE skipped missing_snapshot reason=" + reason);
                return false;
            }

            ClearInMemoryState();
            bool loaded = LoadExistingSnapshot();
            EnsureReservedFirstCell("reload_full_state");
            if (loaded)
            {
                Log("RELOAD_FULL_STATE reason=" + reason + " cells=" + cells.Count);
                Persist("reload_full_state");
            }
            return loaded;
        }

        public bool HasResumeOffset
        {
            get { return hasResumeOffset; }
        }

        public void ResetView()
        {
            CurrentOffset = 0;
            Log("RESET_VIEW offset=0");
            Persist("reset_view");
        }

        public void ScrollDown(int ticks)
        {
            if (ticks <= 0) return;
            CurrentOffset += ticks;
            Log("SCROLL_DOWN ticks=" + ticks + " offset=" + CurrentOffset);
            Persist("scroll_down");
        }

        public void RememberResumeOffset(int offset)
        {
            int normalized = Math.Max(0, offset);
            if (hasResumeOffset && ResumeOffset == normalized) return;
            Log("RESUME_OFFSET " + (hasResumeOffset ? ResumeOffset.ToString(CultureInfo.InvariantCulture) : "-") + " -> " + normalized);
            ResumeOffset = normalized;
            hasResumeOffset = true;
            Persist("resume_offset");
        }

        public bool HasPendingValidNew
        {
            get
            {
                return cells.Values.Any(c =>
                    c.NewState == FH6AutomationConstants.VehicleState.ValidNewName &&
                    IsBeforeKnownBoundary(new CellKey(c.Row, c.Col)));
            }
        }

        public bool HasPendingDeleteVehicle
        {
            get { return cells.Values.Any(c => IsDeleteTargetCell(c) && IsBeforeKnownBoundary(new CellKey(c.Row, c.Col))); }
        }

        public bool HasDriveVehicle
        {
            get { return cells.Values.Any(c => c.NewState == FH6AutomationConstants.VehicleState.DriveName && IsBeforeKnownBoundary(new CellKey(c.Row, c.Col))); }
        }

        public int CountValidNewVehicles()
        {
            return cells.Values.Count(c =>
                StateCode(c) == FH6AutomationConstants.VehicleState.ValidNew &&
                !IsReservedFirstCell(new CellKey(c.Row, c.Col)) &&
                IsBeforeKnownBoundary(new CellKey(c.Row, c.Col)));
        }

        public bool IsCompletionBoundaryReached(out CellKey lastTarget, out CellKey nextCell)
        {
            lastTarget = new CellKey(0, 0);
            nextCell = new CellKey(0, 0);
            if (HasPendingValidNew) return false;

            VirtualVehicleCell last = cells.Values
                .Where(c => StateCode(c) == FH6AutomationConstants.VehicleState.Target)
                .OrderByDescending(c => OrderIndex(c.Row, c.Col))
                .FirstOrDefault();

            if (last == null) return false;

            lastTarget = new CellKey(last.Row, last.Col);
            nextCell = NextCell(lastTarget);

            VirtualVehicleCell next;
            if (!cells.TryGetValue(nextCell, out next)) return false;
            return StateCode(next) == FH6AutomationConstants.VehicleState.UnknownOrNonTarget;
        }

        public int PreferredEntryOffset(int visibleColumns)
        {
            CellKey localCell;
            int pendingOffset;
            if (TryGetPendingValidNewTarget(visibleColumns, out localCell, out pendingOffset)) return pendingOffset;
            return hasResumeOffset ? ResumeOffset : 0;
        }

        public bool TryGetPendingValidNewTarget(int visibleColumns, out CellKey localCell, out int targetOffset)
        {
            localCell = new CellKey(0, 0);
            targetOffset = 0;
            if (visibleColumns <= 0) return false;

            VirtualVehicleCell pending = cells.Values
                .Where(c => c.NewState == FH6AutomationConstants.VehicleState.ValidNewName
                    && !IsReservedFirstCell(new CellKey(c.Row, c.Col))
                    && IsBeforeKnownBoundary(new CellKey(c.Row, c.Col)))
                .OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row)
                .FirstOrDefault();

            if (pending == null) return false;

            targetOffset = Math.Max(0, pending.Col - visibleColumns + 1);
            localCell = new CellKey(pending.Row, pending.Col - targetOffset);
            return localCell.Col >= 0 && localCell.Col < visibleColumns;
        }

        public bool TryGetPendingValidNewTargetAtOrAfterCurrent(int visibleColumns, out CellKey localCell, out int targetOffset)
        {
            localCell = new CellKey(0, 0);
            targetOffset = 0;
            if (visibleColumns <= 0) return false;

            VirtualVehicleCell pending = cells.Values
                .Where(c => c.NewState == FH6AutomationConstants.VehicleState.ValidNewName
                    && c.Col >= CurrentOffset
                    && !IsReservedFirstCell(new CellKey(c.Row, c.Col))
                    && IsBeforeKnownBoundary(new CellKey(c.Row, c.Col)))
                .OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row)
                .FirstOrDefault();

            if (pending == null) return false;

            targetOffset = Math.Max(CurrentOffset, pending.Col - visibleColumns + 1);
            localCell = new CellKey(pending.Row, pending.Col - targetOffset);
            return localCell.Col >= 0 && localCell.Col < visibleColumns;
        }

        public bool TryGetVisiblePendingValidNew(int visibleColumns, out CellKey localCell)
        {
            localCell = new CellKey(0, 0);
            if (visibleColumns <= 0) return false;

            List<CellKey> localCells = new List<CellKey>();
            foreach (VirtualVehicleCell cell in cells.Values)
            {
                if (cell.NewState != FH6AutomationConstants.VehicleState.ValidNewName) continue;
                CellKey globalCell = new CellKey(cell.Row, cell.Col);
                if (IsReservedFirstCell(globalCell)) continue;
                if (!IsBeforeKnownBoundary(globalCell)) continue;
                int localCol = cell.Col - CurrentOffset;
                if (localCol < 0 || localCol >= visibleColumns) continue;
                if (!IsBeforeVisibleBoundary(globalCell, visibleColumns)) continue;
                CellKey local = new CellKey(cell.Row, localCol);
                localCells.Add(local);
            }

            if (localCells.Count == 0) return false;
            localCell = localCells.OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row).First();
            return true;
        }

        public bool TryGetVisibleDeleteVehicle(int visibleColumns, out CellKey localCell)
        {
            localCell = new CellKey(0, 0);
            if (visibleColumns <= 0) return false;

            List<CellKey> localCells = new List<CellKey>();
            foreach (VirtualVehicleCell cell in cells.Values)
            {
                if (!IsDeleteTargetCell(cell)) continue;
                CellKey globalCell = new CellKey(cell.Row, cell.Col);
                if (!IsBeforeKnownBoundary(globalCell)) continue;
                int localCol = cell.Col - CurrentOffset;
                if (localCol < 0 || localCol >= visibleColumns) continue;
                CellKey local = new CellKey(cell.Row, localCol);
                if (ShouldSkipPlannerCell(globalCell)) continue;
                if (!IsBeforeVisibleBoundary(globalCell, visibleColumns)) continue;
                localCells.Add(local);
            }

            if (localCells.Count == 0) return false;
            localCell = localCells.OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row).First();
            return true;
        }

        public bool TryGetVisibleDriveVehicle(int visibleColumns, out CellKey localCell)
        {
            localCell = new CellKey(0, 0);
            if (visibleColumns <= 0) return false;

            List<CellKey> localCells = new List<CellKey>();
            foreach (VirtualVehicleCell cell in cells.Values)
            {
                if (cell.NewState != FH6AutomationConstants.VehicleState.DriveName) continue;
                CellKey globalCell = new CellKey(cell.Row, cell.Col);
                if (!IsBeforeKnownBoundary(globalCell)) continue;
                int localCol = cell.Col - CurrentOffset;
                if (localCol < 0 || localCol >= visibleColumns) continue;
                CellKey local = new CellKey(cell.Row, localCol);
                if (ShouldSkipPlannerCell(globalCell)) continue;
                if (!IsBeforeVisibleBoundary(globalCell, visibleColumns)) continue;
                localCells.Add(local);
            }

            if (localCells.Count == 0) return false;
            localCell = localCells.OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row).First();
            return true;
        }

        public bool IsVisibleDeleteVehicle(CellKey localCell, int visibleColumns)
        {
            if (visibleColumns <= 0) return false;
            if (localCell.Row < 0 || localCell.Row >= rows) return false;
            if (localCell.Col < 0 || localCell.Col >= visibleColumns) return false;

            CellKey global = ToGlobal(localCell);
            if (ShouldSkipPlannerCell(global)) return false;
            if (!IsBeforeKnownBoundary(global)) return false;
            if (!IsBeforeVisibleBoundary(global, visibleColumns)) return false;
            VirtualVehicleCell cell;
            return cells.TryGetValue(global, out cell) && IsDeleteTargetCell(cell);
        }

        public bool IsVisibleSearchRangeObserved(int visibleColumns)
        {
            if (visibleColumns <= 0) return false;
            CellKey knownBoundary;
            if (TryGetFirstKnownBoundaryZero(out knownBoundary) && OrderIndex(knownBoundary.Row, knownBoundary.Col) < OrderIndex(0, CurrentOffset))
            {
                return true;
            }

            bool requiredAny = false;
            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipPlannerCell(global)) continue;

                    VirtualVehicleCell cell;
                    if (cells.TryGetValue(global, out cell) && StateCode(cell) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown)
                    {
                        return true;
                    }

                    requiredAny = true;
                    if (!cells.ContainsKey(global)) return false;
                }
            }

            return requiredAny;
        }

        public bool IsVisibleRangeObserved(int visibleColumns)
        {
            if (visibleColumns <= 0) return false;

            bool requiredAny = false;
            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipPlannerCell(global)) continue;
                    requiredAny = true;
                    if (!cells.ContainsKey(global)) return false;
                }
            }

            return requiredAny;
        }

        public bool TryGetDeleteVehicleTarget(int visibleColumns, out CellKey localCell, out int targetOffset)
        {
            localCell = new CellKey(0, 0);
            targetOffset = 0;
            if (visibleColumns <= 0) return false;

            VirtualVehicleCell pending = cells.Values
                .Where(c =>
                {
                    if (!IsDeleteTargetCell(c) || c.Col < CurrentOffset) return false;
                    if (!IsBeforeKnownBoundary(new CellKey(c.Row, c.Col))) return false;
                    int targetOffsetForCell = Math.Max(CurrentOffset, c.Col - visibleColumns + 1);
                    CellKey localForCell = new CellKey(c.Row, c.Col - targetOffsetForCell);
                    return !ShouldSkipPlannerCell(new CellKey(c.Row, c.Col));
                })
                .OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row)
                .FirstOrDefault();

            if (pending == null) return false;

            targetOffset = Math.Max(CurrentOffset, pending.Col - visibleColumns + 1);
            localCell = new CellKey(pending.Row, pending.Col - targetOffset);
            return localCell.Col >= 0 && localCell.Col < visibleColumns;
        }

        public bool TryGetDriveVehicleTarget(int visibleColumns, out CellKey localCell, out int targetOffset)
        {
            localCell = new CellKey(0, 0);
            targetOffset = 0;
            if (visibleColumns <= 0) return false;

            VirtualVehicleCell pending = cells.Values
                .Where(c =>
                {
                    if (c.NewState != FH6AutomationConstants.VehicleState.DriveName || c.Col < CurrentOffset) return false;
                    if (!IsBeforeKnownBoundary(new CellKey(c.Row, c.Col))) return false;
                    int targetOffsetForCell = Math.Max(CurrentOffset, c.Col - visibleColumns + 1);
                    CellKey localForCell = new CellKey(c.Row, c.Col - targetOffsetForCell);
                    return !ShouldSkipPlannerCell(new CellKey(c.Row, c.Col));
                })
                .OrderBy(c => c.Col * FH6AutomationConstants.Ranking.LeftFirstWeight + c.Row)
                .FirstOrDefault();

            if (pending == null) return false;

            targetOffset = Math.Max(CurrentOffset, pending.Col - visibleColumns + 1);
            localCell = new CellKey(pending.Row, pending.Col - targetOffset);
            return localCell.Col >= 0 && localCell.Col < visibleColumns;
        }

        public void GetVisibleStateSets(
            int visibleColumns,
            out HashSet<CellKey> targets,
            out HashSet<CellKey> validNew,
            out HashSet<CellKey> invalidNew)
        {
            HashSet<CellKey> deletable;
            HashSet<CellKey> drive;
            GetVisibleStateSets(visibleColumns, out targets, out validNew, out invalidNew, out deletable, out drive);
        }

        public void GetVisibleStateSets(
            int visibleColumns,
            out HashSet<CellKey> targets,
            out HashSet<CellKey> validNew,
            out HashSet<CellKey> invalidNew,
            out HashSet<CellKey> deletable)
        {
            HashSet<CellKey> drive;
            GetVisibleStateSets(visibleColumns, out targets, out validNew, out invalidNew, out deletable, out drive);
        }

        public void GetVisibleStateSets(
            int visibleColumns,
            out HashSet<CellKey> targets,
            out HashSet<CellKey> validNew,
            out HashSet<CellKey> invalidNew,
            out HashSet<CellKey> deletable,
            out HashSet<CellKey> drive)
        {
            targets = new HashSet<CellKey>();
            validNew = new HashSet<CellKey>();
            invalidNew = new HashSet<CellKey>();
            deletable = new HashSet<CellKey>();
            drive = new HashSet<CellKey>();
            if (visibleColumns <= 0) return;

            foreach (VirtualVehicleCell cell in cells.Values)
            {
                int localCol = cell.Col - CurrentOffset;
                if (localCol < 0 || localCol >= visibleColumns) continue;

                CellKey local = new CellKey(cell.Row, localCol);
                if (cell.IsTarget || cell.NewState == FH6AutomationConstants.VehicleState.ValidNewName || cell.NewState == FH6AutomationConstants.VehicleState.DeletableName || cell.NewState == FH6AutomationConstants.VehicleState.DriveName) targets.Add(local);
                if (cell.NewState == FH6AutomationConstants.VehicleState.ValidNewName) validNew.Add(local);
                else if (cell.NewState == FH6AutomationConstants.VehicleState.DeletableName) deletable.Add(local);
                else if (cell.NewState == FH6AutomationConstants.VehicleState.DriveName) drive.Add(local);
                else if (cell.NewState == FH6AutomationConstants.VehicleState.InvalidNew) invalidNew.Add(local);
            }
        }

        public Dictionary<CellKey, int> GetVisibleStateCodes(int visibleColumns)
        {
            Dictionary<CellKey, int> result = new Dictionary<CellKey, int>();
            if (visibleColumns <= 0) return result;

            foreach (VirtualVehicleCell cell in cells.Values)
            {
                int localCol = cell.Col - CurrentOffset;
                if (localCol < 0 || localCol >= visibleColumns) continue;
                result[new CellKey(cell.Row, localCol)] = StateCode(cell);
            }

            return result;
        }

        public bool TryGetKnownEmptySkip(int visibleColumns, out int skip, out bool reachesNewColumn)
        {
            skip = 0;
            reachesNewColumn = false;

            if (visibleColumns <= 0)
            {
                LastKnownEmptyRun = 0;
                LastSuggestedSkip = 0;
                return false;
            }

            if (HasPendingValidNew)
            {
                LastKnownEmptyRun = 0;
                LastSuggestedSkip = 0;
                return false;
            }

            LastKnownEmptyRun = CountKnownEmptyColumnsFrom(CurrentOffset);
            if (LastKnownEmptyRun < visibleColumns)
            {
                LastSuggestedSkip = 0;
                return false;
            }

            skip = RetainKnownLeftColumnSkip(LastKnownEmptyRun);
            if (skip <= 0)
            {
                LastSuggestedSkip = 0;
                return false;
            }
            reachesNewColumn = skip == LastKnownEmptyRun - FH6AutomationConstants.Flow.RetainedKnownColumns;

            LastSuggestedSkip = skip;
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "KNOWN_EMPTY_SKIP offset={0} visible={1} emptyRun={2} skip={3} reachesNew={4}",
                CurrentOffset,
                visibleColumns,
                LastKnownEmptyRun,
                LastSuggestedSkip,
                reachesNewColumn));
            Persist("known_empty_skip");
            return true;
        }

        public bool TryGetKnownNonPendingRunSkip(int visibleColumns, out int skip)
        {
            skip = 0;
            if (visibleColumns <= 0) return false;
            if (HasPendingValidNew) return false;

            int run = CountKnownNonPendingColumnsFrom(CurrentOffset);
            if (run <= 0) return false;

            skip = RetainKnownLeftColumnSkip(run);
            if (skip <= 0) return false;

            Log(string.Format(
                CultureInfo.InvariantCulture,
                "KNOWN_NON_PENDING_RUN_SKIP offset={0} visible={1} run={2} skip={3}",
                CurrentOffset,
                visibleColumns,
                run,
                skip));
            Persist("known_non_pending_run_skip");
            return true;
        }

        public bool TryGetKnownNonDeleteToUnknownSkip(int visibleColumns, out int skip)
        {
            skip = 0;
            if (visibleColumns <= 0) return false;
            if (HasDeleteAtOrAfter(CurrentOffset)) return false;

            int run = CountKnownNonDeleteColumnsFrom(CurrentOffset);
            if (run <= 0) return false;

            skip = RetainKnownLeftColumnSkip(run);
            if (skip <= 0) return false;
            LastKnownEmptyRun = run;
            LastSuggestedSkip = skip;
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "NO_KNOWN_DELETE_AGGRESSIVE_SKIP offset={0} visible={1} run={2} skip={3} visibleUnknown={4}",
                CurrentOffset,
                visibleColumns,
                run,
                skip,
                VisibleUnknownCellCount(CurrentOffset, visibleColumns)));
            Persist("known_non_delete_to_unknown_skip");
            return true;
        }

        public bool TryGetKnownNonDriveToUnknownSkip(int visibleColumns, out int skip)
        {
            skip = 0;
            if (visibleColumns <= 0) return false;
            if (cells.Values.Any(c =>
                c.NewState == FH6AutomationConstants.VehicleState.DriveName &&
                c.Col >= CurrentOffset &&
                IsBeforeKnownBoundary(new CellKey(c.Row, c.Col)))) return false;

            int run = CountKnownNonDriveColumnsFrom(CurrentOffset);
            if (run <= 0) return false;

            skip = RetainKnownLeftColumnSkip(run);
            if (skip <= 0) return false;
            LastKnownEmptyRun = run;
            LastSuggestedSkip = skip;
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "NO_KNOWN_DRIVE_AGGRESSIVE_SKIP offset={0} visible={1} run={2} skip={3} visibleUnknown={4}",
                CurrentOffset,
                visibleColumns,
                run,
                skip,
                VisibleUnknownCellCount(CurrentOffset, visibleColumns)));
            Persist("known_non_drive_to_unknown_skip");
            return true;
        }

        public bool VisibleHasOtherManufacturerOrUnknown(int visibleColumns)
        {
            CellKey boundary;
            return TryGetFirstVisibleBoundaryZero(visibleColumns, out boundary);
        }

        public bool SearchBoundaryReached(int visibleColumns)
        {
            if (visibleColumns <= 0) return false;
            CellKey boundary;
            if (!TryGetFirstKnownBoundaryZero(out boundary)) return false;
            int visibleEndCol = CurrentOffset + visibleColumns - 1;
            return OrderIndex(boundary.Row, boundary.Col) <= OrderIndex(rows - 1, visibleEndCol);
        }

        public bool IsDeleteCompletionBoundaryReached(out CellKey lastTarget, out CellKey nextCell)
        {
            lastTarget = new CellKey(0, 0);
            nextCell = new CellKey(0, 0);
            if (HasPendingDeleteVehicle) return false;

            VirtualVehicleCell last = cells.Values
                .OrderByDescending(c => OrderIndex(c.Row, c.Col))
                .FirstOrDefault();

            if (last == null) return false;

            lastTarget = new CellKey(last.Row, last.Col);
            nextCell = NextCell(lastTarget);
            return StateCode(last) == FH6AutomationConstants.VehicleState.UnknownOrNonTarget;
        }

        public void MarkProcessed(CellKey localCell)
        {
            CellKey global = ToGlobal(localCell);
            if (IsReservedFirstCell(global))
            {
                Log("PROCESSED ignored reserved_first_cell");
                return;
            }
            VirtualVehicleCell old;
            VirtualVehicleCell next = new VirtualVehicleCell();
            next.Row = global.Row;
            next.Col = global.Col;
            next.IsManufacturer = true;
            next.IsTarget = true;
            next.NewState = FH6AutomationConstants.VehicleState.None;
            next.LastSeenOffset = CurrentOffset;
            next.SeenCount = cells.TryGetValue(global, out old) ? old.SeenCount + 1 : 1;
            cells[global] = next;
            EditCount++;
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "PROCESSED row={0} col={1} offset={2}",
                global.Row,
                global.Col,
                CurrentOffset));
            Persist("processed");
        }

        public void MarkDeletedAndShift(CellKey localCell)
        {
            CellKey global = ToGlobal(localCell);
            if (IsReservedFirstCell(global))
            {
                Log("DELETE_SHIFT ignored reserved_first_cell");
                return;
            }
            int deletedIndex = OrderIndex(global.Row, global.Col);
            Dictionary<CellKey, VirtualVehicleCell> shifted = new Dictionary<CellKey, VirtualVehicleCell>();

            foreach (VirtualVehicleCell cell in cells.Values)
            {
                int index = OrderIndex(cell.Row, cell.Col);
                if (index == deletedIndex) continue;

                VirtualVehicleCell next = CloneCell(cell);
                if (index > deletedIndex)
                {
                    CellKey shiftedKey = CellFromOrderIndex(index - 1);
                    next.Row = shiftedKey.Row;
                    next.Col = shiftedKey.Col;
                }

                shifted[new CellKey(next.Row, next.Col)] = next;
            }

            cells.Clear();
            EnsureReservedFirstCell("delete_shift");
            foreach (KeyValuePair<CellKey, VirtualVehicleCell> pair in shifted)
            {
                if (IsReservedFirstCell(pair.Key)) continue;
                cells[pair.Key] = pair.Value;
            }

            EditCount++;
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "DELETE_SHIFT deleted_row={0} deleted_col={1} offset={2} cells={3}",
                global.Row,
                global.Col,
                CurrentOffset,
                cells.Count));
            Persist("delete_shift");
        }

        public CellKey ToGlobal(CellKey local)
        {
            return new CellKey(local.Row, CurrentOffset + local.Col);
        }

        public bool HasKnownCell(CellKey global)
        {
            return cells.ContainsKey(global);
        }

        public string Summary()
        {
            int state1 = cells.Values.Count(c => StateCode(c) == FH6AutomationConstants.VehicleState.UnknownOrNonTarget);
            int state2 = cells.Values.Count(c => StateCode(c) == FH6AutomationConstants.VehicleState.Target);
            int state3 = cells.Values.Count(c => StateCode(c) == FH6AutomationConstants.VehicleState.ValidNew);
            int state4 = cells.Values.Count(c => StateCode(c) == FH6AutomationConstants.VehicleState.Deletable);
            int state5 = cells.Values.Count(c => StateCode(c) == FH6AutomationConstants.VehicleState.Drive);
            int state0 = cells.Values.Count(c => StateCode(c) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown);
            int invalidNew = cells.Values.Count(c => c.NewState == FH6AutomationConstants.VehicleState.InvalidNew);
            int knownEmpty = CountKnownEmptyColumns();
            return string.Format(
                CultureInfo.InvariantCulture,
                "虚拟列表: offset={0}, resume={1}, cells={2}, no3Cols={3}, no3Run={4}, skip={5}, 0={6}, 1={7}, 2={8}, 3={9}, 4={10}, 5={11}, invalid={12}, edits={13}",
                CurrentOffset,
                hasResumeOffset ? ResumeOffset.ToString(CultureInfo.InvariantCulture) : "-",
                cells.Count,
                knownEmpty,
                LastKnownEmptyRun,
                LastSuggestedSkip,
                state0,
                state1,
                state2,
                state3,
                state4,
                state5,
                invalidNew,
                EditCount);
        }

        private int StateCode(VirtualVehicleCell cell)
        {
            if (cell.NewState == FH6AutomationConstants.VehicleState.DriveName) return FH6AutomationConstants.VehicleState.Drive;
            if (cell.NewState == FH6AutomationConstants.VehicleState.DeletableName) return FH6AutomationConstants.VehicleState.Deletable;
            if (cell.NewState == FH6AutomationConstants.VehicleState.ValidNewName) return FH6AutomationConstants.VehicleState.ValidNew;
            if (cell.IsTarget) return FH6AutomationConstants.VehicleState.Target;
            if (cell.IsManufacturer) return FH6AutomationConstants.VehicleState.UnknownOrNonTarget;
            return FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown;
        }

        private int RetainKnownLeftColumnSkip(int knownRun)
        {
            int retainedColumns = Math.Min(FH6AutomationConstants.Flow.RetainedKnownColumns, Math.Max(0, knownRun));
            return Math.Max(0, knownRun - retainedColumns);
        }

        private bool IsReservedFirstCell(CellKey global)
        {
            return VehicleGridOcrPolicy.IsReservedFirstCell(global);
        }

        private bool ShouldSkipPlannerCell(CellKey global)
        {
            return IsReservedFirstCell(global);
        }

        private bool IsBeforeVisibleBoundary(CellKey global, int visibleColumns)
        {
            CellKey boundary;
            if (!TryGetFirstVisibleBoundaryZero(visibleColumns, out boundary)) return true;
            return OrderIndex(global.Row, global.Col) < OrderIndex(boundary.Row, boundary.Col);
        }

        private bool IsBeforeKnownBoundary(CellKey global)
        {
            CellKey boundary;
            if (!TryGetFirstKnownBoundaryZero(out boundary)) return true;
            return OrderIndex(global.Row, global.Col) < OrderIndex(boundary.Row, boundary.Col);
        }

        private bool TryGetFirstVisibleBoundaryZero(int visibleColumns, out CellKey boundary)
        {
            boundary = new CellKey(0, 0);
            if (visibleColumns <= 0) return false;

            for (int col = 0; col < visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    CellKey local = new CellKey(row, col);
                    CellKey global = ToGlobal(local);
                    if (ShouldSkipPlannerCell(global)) continue;

                    VirtualVehicleCell cell;
                    if (!cells.TryGetValue(global, out cell)) continue;
                    if (StateCode(cell) != FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown) continue;

                    boundary = global;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetFirstKnownBoundaryZero(out CellKey boundary)
        {
            boundary = new CellKey(0, 0);
            VirtualVehicleCell first = cells.Values
                .Where(c =>
                {
                    CellKey global = new CellKey(c.Row, c.Col);
                    return !ShouldSkipPlannerCell(global) && StateCode(c) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown;
                })
                .OrderBy(c => OrderIndex(c.Row, c.Col))
                .FirstOrDefault();

            if (first == null) return false;
            boundary = new CellKey(first.Row, first.Col);
            return true;
        }

        private void EnsureReservedFirstCell(string reason)
        {
            CellKey key = new CellKey(0, 0);
            VirtualVehicleCell reserved = new VirtualVehicleCell();
            reserved.Row = 0;
            reserved.Col = 0;
            reserved.IsManufacturer = false;
            reserved.IsTarget = false;
            reserved.NewState = FH6AutomationConstants.VehicleState.None;
            reserved.LastSeenOffset = 0;
            reserved.SeenCount = 1;
            cells[key] = reserved;
            Log("RESERVED_FIRST_CELL " + reason);
        }

        private int OrderIndex(int row, int col)
        {
            return col * rows + row;
        }

        private CellKey NextCell(CellKey cell)
        {
            if (cell.Row + 1 < rows) return new CellKey(cell.Row + 1, cell.Col);
            return new CellKey(0, cell.Col + 1);
        }

        private CellKey CellFromOrderIndex(int index)
        {
            int normalized = Math.Max(0, index);
            return new CellKey(normalized % rows, normalized / rows);
        }

        private int CountKnownEmptyColumnsFrom(int startCol)
        {
            int count = 0;
            int col = startCol;
            while (IsKnownEmptyColumn(col))
            {
                count++;
                col++;
            }
            return count;
        }

        private int CountKnownEmptyColumns()
        {
            HashSet<int> columns = new HashSet<int>(cells.Keys.Select(c => c.Col));
            int count = 0;
            foreach (int col in columns)
            {
                if (IsKnownEmptyColumn(col)) count++;
            }
            return count;
        }

        private int CountKnownNonPendingColumnsFrom(int startCol)
        {
            int count = 0;
            int col = startCol;
            while (IsKnownNonPendingColumn(col))
            {
                count++;
                col++;
            }
            return count;
        }

        private int CountKnownNonDeleteColumnsFrom(int startCol)
        {
            int count = 0;
            int col = startCol;
            while (IsKnownNonDeleteColumn(col))
            {
                count++;
                col++;
            }
            return count;
        }

        private int CountKnownNonDriveColumnsFrom(int startCol)
        {
            int count = 0;
            int col = startCol;
            while (IsKnownNonDriveColumn(col))
            {
                count++;
                col++;
            }
            return count;
        }

        private int VisibleUnknownCellCount(int startCol, int visibleColumns)
        {
            int count = 0;
            for (int col = startCol; col < startCol + visibleColumns; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    if (!cells.ContainsKey(new CellKey(row, col))) count++;
                }
            }
            return count;
        }

        private bool HasDeleteAtOrAfter(int col)
        {
            return cells.Values.Any(c =>
                IsDeleteTargetCell(c) &&
                c.Col >= col &&
                IsBeforeKnownBoundary(new CellKey(c.Row, c.Col)));
        }

        private bool IsKnownEmptyColumn(int col)
        {
            bool hasAny = false;
            for (int row = 0; row < rows; row++)
            {
                if (ShouldIgnoreKnownColumnCell(row, col)) continue;
                VirtualVehicleCell cell;
                if (!cells.TryGetValue(new CellKey(row, col), out cell)) return false;
                hasAny = true;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown) return false;
                if (cell.NewState == FH6AutomationConstants.VehicleState.ValidNewName) return false;
            }
            return hasAny;
        }

        private bool IsKnownNonPendingColumn(int col)
        {
            bool hasAny = false;
            for (int row = 0; row < rows; row++)
            {
                if (ShouldIgnoreKnownColumnCell(row, col)) continue;
                VirtualVehicleCell cell;
                if (!cells.TryGetValue(new CellKey(row, col), out cell)) return false;
                hasAny = true;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown) return false;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.ValidNew) return false;
            }
            return hasAny;
        }

        private bool IsKnownNonDeleteColumn(int col)
        {
            bool hasAny = false;
            for (int row = 0; row < rows; row++)
            {
                if (ShouldIgnoreKnownColumnCell(row, col)) continue;
                VirtualVehicleCell cell;
                if (!cells.TryGetValue(new CellKey(row, col), out cell)) return false;
                hasAny = true;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown) return false;
                if (IsDeleteTargetCell(cell)) return false;
            }
            return hasAny;
        }

        private bool IsDeleteTargetCell(VirtualVehicleCell cell)
        {
            int state = StateCode(cell);
            return state == FH6AutomationConstants.VehicleState.Target ||
                   state == FH6AutomationConstants.VehicleState.Deletable;
        }

        private bool IsKnownNonDriveColumn(int col)
        {
            bool hasAny = false;
            for (int row = 0; row < rows; row++)
            {
                if (ShouldIgnoreKnownColumnCell(row, col)) continue;
                VirtualVehicleCell cell;
                if (!cells.TryGetValue(new CellKey(row, col), out cell)) return false;
                hasAny = true;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown) return false;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.Drive) return false;
                if (StateCode(cell) == FH6AutomationConstants.VehicleState.Target &&
                    cell.NewState != FH6AutomationConstants.VehicleState.DriveCheckedName) return false;
            }
            return hasAny;
        }

        private bool ShouldIgnoreKnownColumnCell(int row, int col)
        {
            return ShouldSkipPlannerCell(new CellKey(row, col));
        }

        private VirtualVehicleCell CloneCell(VirtualVehicleCell cell)
        {
            VirtualVehicleCell clone = new VirtualVehicleCell();
            clone.Row = cell.Row;
            clone.Col = cell.Col;
            clone.IsManufacturer = cell.IsManufacturer;
            clone.IsTarget = cell.IsTarget;
            clone.NewState = cell.NewState;
            clone.LastSeenOffset = cell.LastSeenOffset;
            clone.SeenCount = cell.SeenCount;
            return clone;
        }

        private bool LoadExistingSnapshot()
        {
            if (string.IsNullOrEmpty(snapshotPath) || !File.Exists(snapshotPath)) return false;

            try
            {
                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(snapshotPath, Encoding.UTF8));
                object rowsValue;
                if (root.TryGetValue("rows", out rowsValue) && Convert.ToInt32(rowsValue, CultureInfo.InvariantCulture) != rows)
                {
                    Log("LOAD_EXISTING skipped rows_mismatch");
                    return false;
                }

                object currentOffsetValue;
                if (root.TryGetValue("current_offset", out currentOffsetValue))
                {
                    CurrentOffset = Math.Max(0, Convert.ToInt32(currentOffsetValue, CultureInfo.InvariantCulture));
                }

                object hasResumeValue;
                object resumeValue;
                if (root.TryGetValue("has_resume_offset", out hasResumeValue) && Convert.ToBoolean(hasResumeValue, CultureInfo.InvariantCulture)
                    && root.TryGetValue("resume_offset", out resumeValue))
                {
                    ResumeOffset = Math.Max(0, Convert.ToInt32(resumeValue, CultureInfo.InvariantCulture));
                    hasResumeOffset = true;
                }

                object cellsValue;
                IEnumerable items = root.TryGetValue("cells", out cellsValue) ? cellsValue as IEnumerable : null;
                if (items == null || cellsValue is string) return false;

                foreach (object raw in items)
                {
                    Dictionary<string, object> item = raw as Dictionary<string, object>;
                    if (item == null) continue;

                    VirtualVehicleCell cell = new VirtualVehicleCell();
                    cell.Row = Math.Max(0, Convert.ToInt32(item["row"], CultureInfo.InvariantCulture));
                    cell.Col = Math.Max(0, Convert.ToInt32(item["col"], CultureInfo.InvariantCulture));
                    object isManufacturerValue;
                    cell.IsManufacturer = item.TryGetValue("is_manufacturer", out isManufacturerValue) && Convert.ToBoolean(isManufacturerValue, CultureInfo.InvariantCulture);
                    object isTargetValue;
                    cell.IsTarget = item.TryGetValue("is_target", out isTargetValue) && Convert.ToBoolean(isTargetValue, CultureInfo.InvariantCulture);
                    object newStateValue;
                    cell.NewState = item.TryGetValue("new_state", out newStateValue) ? Convert.ToString(newStateValue, CultureInfo.InvariantCulture) : FH6AutomationConstants.VehicleState.None;

                    object stateValue;
                    if (item.TryGetValue("state", out stateValue))
                    {
                        int state = Convert.ToInt32(stateValue, CultureInfo.InvariantCulture);
                        if (state == FH6AutomationConstants.VehicleState.Deletable)
                        {
                            cell.IsManufacturer = true;
                            cell.IsTarget = true;
                            cell.NewState = FH6AutomationConstants.VehicleState.DeletableName;
                        }
                        else if (state == FH6AutomationConstants.VehicleState.ValidNew)
                        {
                            cell.IsManufacturer = true;
                            cell.IsTarget = true;
                            cell.NewState = FH6AutomationConstants.VehicleState.ValidNewName;
                        }
                        else if (state == FH6AutomationConstants.VehicleState.Target)
                        {
                            cell.IsManufacturer = true;
                            cell.IsTarget = true;
                            if (cell.NewState == FH6AutomationConstants.VehicleState.DeletableName || cell.NewState == FH6AutomationConstants.VehicleState.ValidNewName) cell.NewState = FH6AutomationConstants.VehicleState.None;
                        }
                        else if (state == FH6AutomationConstants.VehicleState.Drive)
                        {
                            cell.IsManufacturer = true;
                            cell.IsTarget = true;
                            cell.NewState = FH6AutomationConstants.VehicleState.DriveName;
                        }
                        else if (state == FH6AutomationConstants.VehicleState.UnknownOrNonTarget)
                        {
                            cell.IsManufacturer = true;
                        }
                    }

                    object lastSeenValue;
                    if (item.TryGetValue("last_seen_offset", out lastSeenValue)) cell.LastSeenOffset = Convert.ToInt32(lastSeenValue, CultureInfo.InvariantCulture);
                    object seenValue;
                    cell.SeenCount = item.TryGetValue("seen_count", out seenValue) ? Convert.ToInt32(seenValue, CultureInfo.InvariantCulture) : 1;
                    cells[new CellKey(cell.Row, cell.Col)] = cell;
                }

                Log(string.Format(CultureInfo.InvariantCulture, "LOAD_EXISTING cells={0} offset={1} resume={2}", cells.Count, CurrentOffset, hasResumeOffset ? ResumeOffset.ToString(CultureInfo.InvariantCulture) : "-"));
                return true;
            }
            catch (Exception ex)
            {
                Log("LOAD_EXISTING failed " + ex.Message);
                return false;
            }
        }

        private void ClearInMemoryState()
        {
            cells.Clear();
            hasResumeOffset = false;
            CurrentOffset = 0;
            ResumeOffset = 0;
            ObservationCount = 0;
            LastKnownEmptyRun = 0;
            LastSuggestedSkip = 0;
        }

        private void Log(string message)
        {
            if (string.IsNullOrEmpty(editLogPath)) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + "\r\n";
            File.AppendAllText(editLogPath, line, Encoding.UTF8);
        }

        private void Persist(string reason)
        {
            if (string.IsNullOrEmpty(snapshotPath)) return;

            try
            {
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["schema"] = "fh6_virtual_vehicle_list.v1";
                root["note"] = "虚拟列表运行期快照。独立启动只复用格子几何并重新 OCR；衔接启动和总控阶段交接会读取本文件的车辆状态。";
                root["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                root["update_reason"] = reason;
                root["rows"] = rows;
                root["current_offset"] = CurrentOffset;
                root["has_resume_offset"] = hasResumeOffset;
                root["resume_offset"] = ResumeOffset;
                root["observation_count"] = ObservationCount;
                root["edit_count"] = EditCount;
                root["last_known_empty_run"] = LastKnownEmptyRun;
                root["last_suggested_skip"] = LastSuggestedSkip;
                root["state_codes"] = "0=非斯巴鲁或未确认制造商, 1=斯巴鲁但非目标车, 2=目标车但没有有效全新/不可删, 3=目标车且有有效全新, 4=可删车辆, 5=用来开刷技术点蓝图的车辆";

                List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                foreach (VirtualVehicleCell cell in cells.Values.OrderBy(c => c.Col).ThenBy(c => c.Row))
                {
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["row"] = cell.Row;
                    item["col"] = cell.Col;
                    item["order_index"] = OrderIndex(cell.Row, cell.Col);
                    item["state"] = StateCode(cell);
                    item["is_manufacturer"] = cell.IsManufacturer;
                    item["is_target"] = cell.IsTarget;
                    item["new_state"] = cell.NewState ?? FH6AutomationConstants.VehicleState.None;
                    item["last_seen_offset"] = cell.LastSeenOffset;
                    item["seen_count"] = cell.SeenCount;
                    items.Add(item);
                }
                root["cells"] = items;

                string json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(root);
                File.WriteAllText(snapshotPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                TryLogPersistFailure(ex);
            }
        }

        private void TryLogPersistFailure(Exception ex)
        {
            try
            {
                if (string.IsNullOrEmpty(editLogPath)) return;
                string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " PERSIST_FAILED " + ex.Message + "\r\n";
                File.AppendAllText(editLogPath, line, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void EnsureParentDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }
    }

    internal sealed class VirtualVehicleCell
    {
        public int Row;
        public int Col;
        public bool IsTarget;
        public bool IsManufacturer;
        public string NewState;
        public int LastSeenOffset;
        public int SeenCount;
    }
}
