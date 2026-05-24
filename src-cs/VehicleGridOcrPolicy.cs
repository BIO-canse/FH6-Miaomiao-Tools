using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class VehicleGridOcrPolicy
    {
        public static bool IsReservedFirstCell(CellKey global)
        {
            return false;
        }

        public static bool ShouldSkipOcrWrite(CellKey global, bool globalAlreadyKnown)
        {
            return globalAlreadyKnown;
        }
    }
}
