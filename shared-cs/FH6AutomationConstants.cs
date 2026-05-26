namespace FH6AutomationShared
{
    internal static class FH6AutomationConstants
    {
        internal static class Keys
        {
            public const int HotkeyModifierVirtualKey = 0x20; // Space
            public const int ExitVirtualKey = 0x43; // C
            public const int SafeStopVirtualKey = 0x56; // V
            public const int FullAutoSafeStopVirtualKey = SafeStopVirtualKey;
            public const int DebugStepVirtualKey = 0xC0;

            public const ushort Backspace = 0x08;
            public const ushort Enter = 0x0D;
            public const ushort Escape = 0x1B;
            public const ushort Space = 0x20;
            public const ushort Left = 0x25;
            public const ushort Up = 0x26;
            public const ushort Right = 0x27;
            public const ushort Down = 0x28;
            public const ushort D = 0x44;
            public const ushort W = 0x57;
            public const ushort X = 0x58;
        }

        internal static class Timing
        {
            public const int StartupDelayMs = 10000;
            public const int TapMs = 100;
            public const int ShortMouseClickMs = 50;
            public const int RepeatIntervalMs = 100;
            public const int AfterClickMs = 250;
            public const int HalfSecondMs = 500;
            public const int OneSecondMs = 1000;
            public const int FiveSecondsMs = 5000;
            public const int TenSecondsMs = 10000;
            public const int TwelveSecondsMs = 12000;
            public const int ThirteenSecondsMs = 13000;
            public const int FifteenSecondsMs = 15000;
            public const int TwentySecondsMs = 20000;
            public const int FullAutoStageGapMs = 1000;
            public const int ChildProcessPollMs = 250;
            public const int UiFindRetryDelayMs = 300;
            public const int UiOcrStableWaitMs = 1000;
            public const int SleepSliceMs = 50;
            public const int FullAutoSleepSliceMs = 100;
            public const int UiCacheGuardCaptureWaitMs = 700;
            public const int DebugKeyPollMs = 30;
            public const int ManufacturerRecordPollMs = 20;
            public const int OverlayHideBeforeCaptureMs = 180;
        }

        internal static class Ocr
        {
            public const double Scale = 2.0;
            public const double MinConfidence = 0;
            public const int Psm = 6;
            public const int BridgeInitTimeoutMs = 180000;
            public const int BridgeRequestTimeoutMs = 120000;
            public const int VcRedistInstallTimeoutMs = 600000;
            public const string VcRedistX64Url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
            public const string VcRedistX64FileName = "vc_redist.x64.exe";
            public const int GridCapturePaddingPx = 24;
            public const int UiFindAttempts = 6;
            public const int TargetVehicleLatinFuzzyDistance = 2;
            public const int MarkerLatinFuzzyDistance = 1;
            public const int NewBadgeCjkMinCommonChars = 2;
            public const int NewBadgeCjkMaxNormalizedLength = 4;
            public const int MyHorizonCjkMinCommonChars = 3;
            public const int MyHorizonCjkMaxNormalizedLength = 8;
            public const int UiCjkMaxCommonChars = 3;
            public const int UiCjkMaxExtraLength = 4;
        }

        internal static class Text
        {
            public const string Manufacturer = "斯巴鲁";
            public const string MyHorizon = "我的地平线";
            public const string TargetVehicle = "IMPREZA 22B-STI";
            public const string NewBadge = "全新";
            public const string DeleteMarker = "600";
            public const string DriveMarker = "900";
            public const string CreativeCenter = "创意中心";
            public const string LatestHot = "最新最热";
            public const string MyFavorites = "我的收藏";
        }

        internal static class Flow
        {
            public const int ManufacturerScrollTicks = 10;
            public const int ManufacturerFindAttempts = 12;
            public const int ManufacturerRetryScrollTicks = 20;
            public const int ScrollTickDelayMs = 20;
            public const int SingleScrollDelayMs = 300;
            public const int MaxFindVehicleScrolls = 100;
            public const int MaxFindNewScrolls = 240;
            public const int BuyTargetValidNewCount = 31;
            public const int PostMinuteDownCount = 4;
            public const int DeleteMenuDownCount = 4;
            public const int PreCreativeExitEscCount = 4;
            public const int DefaultGridRows = 3;
            public const int RetainedKnownColumns = 1;
        }

        internal static class SkillPoints
        {
            public const int Max = 999;
            public const int QuickVerifyTarget = 100;
            public const int PerVehicle = 32;
            public const int MinuteLoopGain = 10;
            public const int MinuteLoopEnterToXWaitMs = 40000;
            public const int MinuteLoopAfterXWaitMs = Timing.OneSecondMs;
            public const int MinuteLoopEstimatedLoopMs = 51300;
        }

        internal static class Credits
        {
            public const long VehiclePrice = 86000;
        }

        internal static class VehicleState
        {
            public const int OtherManufacturerOrUnknown = 0;
            public const int UnknownOrNonTarget = 1;
            public const int Target = 2;
            public const int ValidNew = 3;
            public const int Deletable = 4;
            public const int Drive = 5;
            public const int Blank = -1;

            public const string None = "none";
            public const string InvalidNew = "invalid-new";
            public const string ValidNewName = "valid-new";
            public const string DeletableName = "deletable";
            public const string DriveName = "drive";
            public const string DriveCheckedName = "drive-checked";
            public const string BlankName = "blank";
        }

        internal static class Ranking
        {
            public const int LeftFirstWeight = 1000;
        }

        internal static class Files
        {
            public const string DebugDir = "debug";
            public const string VirtualListPath = "state/virtual-vehicle-list.json";
            public const string SkillPointExe = "FH6SkillPointOcr.exe";
            public const string DeleteVehicleExe = "FH6VehicleDeleteOcr.exe";
            public const string FullAutoExe = "FH6FullAuto.exe";
            public const string BlueprintCycleTestExe = "FH6BlueprintCycleTest.exe";
            public const string EmergencyStopWatcherExe = "FH6EmergencyStopWatcher.exe";
            public const string UiCacheGuardExe = "FH6UiCacheOcrGuard.exe";
            public const string MinuteLoopExe = "MinuteWLoop.exe";
            public const string BuyLoopExe = "SpaceDownEnterLoop.exe";
            public const string SkillSafeStop = "skill-safe-stop.request";
            public const string DeleteSafeStop = "delete-safe-stop.request";
            public const string MinuteSafeStop = "minute-safe-stop.request";
            public const string BuySafeStop = "buy-safe-stop.request";
            public const string SkillPointsState = "full-auto-skill-points.json";
            public const string UiClickCache = "ui-click-cache.json";
        }

        internal static class FixedSequences
        {
            public static readonly string[] SkillPointSequence = new[]
            {
                "ENTER",
                "WAIT:15000",
                "ESC",
                "WAIT:2000",
                "DOWN",
                "ENTER",
                "WAIT:1000",
                "DOWN*7",
                "ENTER",
                "WAIT:1000",
                "ENTER",
                "WAIT:1000",
                "UP",
                "ENTER",
                "WAIT:1000",
                "RIGHT",
                "ENTER",
                "WAIT:1000",
                "UP",
                "ENTER",
                "WAIT:1000",
                "UP",
                "ENTER",
                "WAIT:1000",
                "LEFT",
                "ENTER",
                "WAIT:1000",
                "ESC",
                "WAIT:1000",
                "ESC",
                "WAIT:1000",
                "UP"
            };
        }
    }
}
