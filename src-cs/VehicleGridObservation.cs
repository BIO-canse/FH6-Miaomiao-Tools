using System.Collections.Generic;

namespace FH6SkillPointOcr
{
    internal sealed class VehicleGridObservation
    {
        public OcrSnapshot Snapshot;
        public int ScrollIndex;

        public List<OcrMatch> TargetMatches = new List<OcrMatch>();
        public List<OcrMatch> NewBadgeMatches = new List<OcrMatch>();
        public List<OcrMatch> ManufacturerMatches = new List<OcrMatch>();
        public List<OcrMatch> DeleteMarkerMatches = new List<OcrMatch>();
        public List<OcrMatch> DriveMarkerMatches = new List<OcrMatch>();
        public List<OcrMatch> PerformanceScoreMatches = new List<OcrMatch>();

        public HashSet<CellKey> TargetCells = new HashSet<CellKey>();
        public HashSet<CellKey> ValidNewCells = new HashSet<CellKey>();
        public HashSet<CellKey> InvalidNewCells = new HashSet<CellKey>();
        public HashSet<CellKey> ManufacturerCells = new HashSet<CellKey>();
        public HashSet<CellKey> DeletableCells = new HashSet<CellKey>();
        public HashSet<CellKey> DriveCells = new HashSet<CellKey>();
        public HashSet<CellKey> BlankCells = new HashSet<CellKey>();
        public Dictionary<CellKey, int> PerformanceScores = new Dictionary<CellKey, int>();
    }
}
