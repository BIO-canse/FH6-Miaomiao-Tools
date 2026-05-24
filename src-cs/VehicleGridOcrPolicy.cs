using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class VehicleGridOcrPolicy
    {
        public static bool IsReservedFirstCell(CellKey global)
        {
            return global.Row == 0 && global.Col == 0;
        }

        public static bool ShouldSkipOcrWrite(CellKey global, bool globalAlreadyKnown)
        {
            if (IsReservedFirstCell(global)) return true;
            return globalAlreadyKnown;
        }
    }
}
