using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FH6AutomationShared;

internal static class SpaceDownEnterLoop
{
    private const int ExitVirtualKey = FH6AutomationConstants.Keys.ExitVirtualKey;
    private const int HotkeyModifierVirtualKey = FH6AutomationConstants.Keys.HotkeyModifierVirtualKey;
    private const ushort KeyEnter = FH6AutomationConstants.Keys.Enter;
    private const ushort KeySpace = FH6AutomationConstants.Keys.Space;
    private const ushort KeyDown = FH6AutomationConstants.Keys.Down;
    private const int LoopDelayMs = FH6AutomationConstants.Timing.HalfSecondMs;
    private static volatile bool StopRequested;
    private static string SafeStopFile;
    private static string BuyResultFile;
    private static bool TrackCredits;
    private static long InitialCredits;
    private static long Credits;
    private static long CreditCost = FH6AutomationConstants.Credits.VehiclePrice;
    private static int CompletedRounds;
    private static int RequestedRounds;
    private static string StopReason = "completed";

    private static int Main(string[] args)
    {
        FH6FailureLog.InstallGlobalHandlers("SpaceDownEnterLoop");
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] " + ex.Message);
            FH6FailureLog.Write("SpaceDownEnterLoop", ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        int rounds = 0;
        int startupDelayMs = FH6AutomationConstants.Timing.StartupDelayMs;
        ParseArgs(args, out rounds, out startupDelayMs);
        RequestedRounds = rounds;
        InitialCredits = Credits;
        WriteBuyResult("start");

        Console.Title = "SpaceDownEnterLoop - Space+C 退出";
        Console.WriteLine("程序已启动。");
        Console.WriteLine(startupDelayMs > 0 ? "启动后先等待 10 秒，然后循环：空格、下、Enter、Enter、Enter。" : "立即开始循环：空格、下、Enter、Enter、Enter。");
        Console.WriteLine("每个键按下 0.1 秒，键与键之间等待 0.5 秒。");
        if (rounds > 0) Console.WriteLine("本次按参数运行 " + rounds + " 轮后自动退出。");
        if (TrackCredits) Console.WriteLine("CR 保险：当前 {0}，每辆扣 {1}。", Credits, CreditCost);
        Console.WriteLine("按 Space+C 退出。请在第一次 10 秒等待内切到目标窗口。");

        if (!WaitOrExit(TimeSpan.FromMilliseconds(startupDelayMs)))
        {
            Console.WriteLine("已退出。");
            StopReason = "startup_exit";
            WriteBuyResult(StopReason);
            return 0;
        }

        while (!ExitRequested() && (rounds <= 0 || CompletedRounds < rounds))
        {
            if (TrackCredits && Credits < CreditCost)
            {
                StopReason = "insufficient_credits";
                Console.WriteLine("CR 不足：当前 {0}，需要 {1}。不会按 Space 购买，已停止。", Credits, CreditCost);
                WriteBuyResult(StopReason);
                return 0;
            }

            TapKey(KeySpace);
            WaitOrExit(TimeSpan.FromMilliseconds(LoopDelayMs));

            TapKey(KeyDown);
            WaitOrExit(TimeSpan.FromMilliseconds(LoopDelayMs));

            TapKey(KeyEnter);
            WaitOrExit(TimeSpan.FromMilliseconds(LoopDelayMs));

            TapKey(KeyEnter);
            WaitOrExit(TimeSpan.FromMilliseconds(LoopDelayMs));

            TapKey(KeyEnter);
            WaitOrExit(TimeSpan.FromMilliseconds(LoopDelayMs));

            CompletedRounds++;
            if (TrackCredits) Credits = Math.Max(0, Credits - CreditCost);
            WriteBuyResult("round_completed");
            if (rounds > 0) Console.WriteLine("已完成 " + CompletedRounds + " / " + rounds + " 轮。");
            if (TrackCredits) Console.WriteLine("CR 已扣除 {0}，剩余 {1}。", CreditCost, Credits);
            if (SafeStopRequested())
            {
                Console.WriteLine("收到安全退出请求，当前轮已完成。");
                StopReason = "safe_stop";
                WriteBuyResult(StopReason);
                break;
            }
        }

        if (ExitRequested()) StopReason = "hotkey_exit";
        else if (rounds > 0 && CompletedRounds >= rounds) StopReason = "completed";
        WriteBuyResult(StopReason);
        Console.WriteLine("已退出。");
        return 0;
    }

    private static void ParseArgs(string[] args, out int rounds, out int startupDelayMs)
    {
        rounds = 0;
        startupDelayMs = FH6AutomationConstants.Timing.StartupDelayMs;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--rounds" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out rounds);
            }
            else if (arg == "--startup-delay-ms" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out startupDelayMs);
                if (startupDelayMs < 0) startupDelayMs = 0;
            }
            else if (arg == "--safe-stop-file" && i + 1 < args.Length)
            {
                SafeStopFile = args[++i];
            }
            else if (arg == "--credits" && i + 1 < args.Length)
            {
                long value;
                if (long.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
                {
                    Credits = value;
                    TrackCredits = true;
                }
            }
            else if (arg == "--credit-cost" && i + 1 < args.Length)
            {
                long value;
                if (long.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
                {
                    CreditCost = value;
                }
            }
            else if (arg == "--buy-result-file" && i + 1 < args.Length)
            {
                BuyResultFile = args[++i];
            }
        }
    }

    private static void WriteBuyResult(string reason)
    {
        if (string.IsNullOrWhiteSpace(BuyResultFile)) return;
        try
        {
            string directory = Path.GetDirectoryName(BuyResultFile);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("reason=" + reason);
            sb.AppendLine("stop_reason=" + StopReason);
            sb.AppendLine("requested_rounds=" + RequestedRounds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("completed_rounds=" + CompletedRounds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("track_credits=" + TrackCredits.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("initial_credits=" + InitialCredits.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("remaining_credits=" + Credits.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("credit_cost=" + CreditCost.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(BuyResultFile, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine("买车结果写入失败：{0}", ex.Message);
            FH6FailureLog.Write("SpaceDownEnterLoop.WriteBuyResult", ex);
        }
    }

    private static bool WaitOrExit(TimeSpan duration)
    {
        DateTime deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            if (ExitRequested())
            {
                return false;
            }

            Thread.Sleep(10);
        }

        return true;
    }

    private static bool ExitRequested()
    {
        if (StopRequested)
        {
            return true;
        }

        if (IsKeyDown(HotkeyModifierVirtualKey) && IsKeyDown(ExitVirtualKey))
        {
            StopRequested = true;
            return true;
        }

        return false;
    }

    private static bool SafeStopRequested()
    {
        return !string.IsNullOrEmpty(SafeStopFile) && System.IO.File.Exists(SafeStopFile);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void TapKey(ushort virtualKey)
    {
        SendKeyboardInput(virtualKey, false);
        WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TapMs));
        SendKeyboardInput(virtualKey, true);
    }

    private static void SendKeyboardInput(ushort virtualKey, bool keyUp)
    {
        INPUT input = new INPUT();
        input.type = InputType.Keyboard;
        input.U.ki = new KEYBDINPUT();
        input.U.ki.wVk = virtualKey;
        input.U.ki.dwFlags = keyUp ? KeyboardEventFlags.KeyUp : 0;

        SendInputOrThrow(input);
    }

    private static void SendInputOrThrow(INPUT input)
    {
        INPUT[] inputs = new[] { input };
        uint sent = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != 1)
        {
            throw new InvalidOperationException(
                string.Format("SendInput 失败，错误码：{0}", Marshal.GetLastWin32Error()));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private enum InputType : uint
    {
        Keyboard = 1
    }

    [Flags]
    private enum KeyboardEventFlags : uint
    {
        KeyUp = 0x0002
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public InputType type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KeyboardEventFlags dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
