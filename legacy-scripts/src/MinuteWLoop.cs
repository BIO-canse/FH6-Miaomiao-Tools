using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using FH6AutomationShared;

internal static class MinuteWLoop
{
    private const int ExitVirtualKey = FH6AutomationConstants.Keys.ExitVirtualKey;
    private const int HotkeyModifierVirtualKey = FH6AutomationConstants.Keys.HotkeyModifierVirtualKey;
    private const ushort KeyEnter = FH6AutomationConstants.Keys.Enter;
    private const ushort KeyW = FH6AutomationConstants.Keys.W;
    private const ushort KeyX = FH6AutomationConstants.Keys.X;
    private static readonly object InputLock = new object();
    private static volatile bool StopRequested;
    private static volatile bool SafeStopRequested;
    private static string SafeStopFile;
    private static string SkillPointsStateFile;
    private static string SkillPointsLogFile;
    private static int SkillPoints;
    private static int SuperWheelspins;
    private static int EventIndex;
    private static int MinuteLoopCount;
    private static bool TrackSkillPoints;
    private static bool HandoffStart;

    private static int Main(string[] args)
    {
        FH6FailureLog.InstallGlobalHandlers("MinuteWLoop");
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] " + ex.Message);
            FH6FailureLog.Write("MinuteWLoop", ex);
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        SafeStopFile = ParseSafeStopFile(args);
        SkillPointsStateFile = ParseArg(args, "--skill-points-state-file");
        SkillPointsLogFile = ParseArg(args, "--skill-points-log-file");
        HandoffStart = HasFlag(args, "--handoff");
        TrackSkillPoints = !string.IsNullOrWhiteSpace(SkillPointsStateFile);
        if (TrackSkillPoints)
        {
            ReadSkillPointsState(SkillPointsStateFile, 0);
            WriteSkillPointsState("minute_loop_start");
            AppendSkillPointEvent("minute_loop_start", SkillPoints, SkillPoints, 0, SuperWheelspins, SuperWheelspins);
        }

        Console.Title = "MinuteWLoop - Space+C 退出";
        Console.WriteLine("程序已启动。");
        Console.WriteLine(HandoffStart ? "衔接启动：跳过开局 10 秒等待。" : "启动后先等待 10 秒。");
        Console.WriteLine("主循环：确保 W 松开，按 Enter，1 秒后按住 W，Enter 后等待 37 秒，松开 W，按 X，等待 1 秒，按 Enter，等待 10 秒。");
        Console.WriteLine("W 不会在 Enter 前按下，避免菜单选项被 W 移动。");
        if (TrackSkillPoints)
        {
            Console.WriteLine("内部技术点计数：当前 {0}，每轮 +{1}，到 {2} 后安全停止。", SkillPoints, FH6AutomationConstants.SkillPoints.MinuteLoopGain, FH6AutomationConstants.SkillPoints.Max);
        }
        Console.WriteLine(HandoffStart
            ? "按 Space+C 立即退出。外部安全退出会跑完当前刷技术点循环后退出。衔接启动由主程序负责切回目标窗口。"
            : "按 Space+C 立即退出。外部安全退出会跑完当前刷技术点循环后退出。请在第一次 10 秒等待内切到目标窗口。");

        if (TrackSkillPoints && SkillPoints >= FH6AutomationConstants.SkillPoints.Max)
        {
            Console.WriteLine("技术点已达到 {0}，不启动刷点循环。", FH6AutomationConstants.SkillPoints.Max);
            WriteSkillPointsState("already_full");
            return;
        }

        if (!HandoffStart && !WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.StartupDelayMs)))
        {
            ReleaseW();
            Console.WriteLine("已退出。");
            return;
        }

        Thread timedActionThread = new Thread(RunTimedActionLoop);
        timedActionThread.Start();

        while (!ExitRequested())
        {
            Thread.Sleep(25);
        }

        timedActionThread.Join();
        ReleaseW();
        Console.WriteLine("已退出。");
    }

    private static void RunTimedActionLoop()
    {
        try
        {
            while (!StopRequested)
            {
                ReleaseW();
                TapKey(KeyEnter);
                Console.WriteLine("{0:HH:mm:ss} 已按 Enter", DateTime.Now);
                ScheduleWPressAfterEnter();

                if (!WaitOrImmediateExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.SkillPoints.MinuteLoopEnterToXWaitMs)))
                {
                    break;
                }

                ReleaseW();
                Console.WriteLine("{0:HH:mm:ss} W 已松开", DateTime.Now);
                TapKey(KeyX);
                Console.WriteLine("{0:HH:mm:ss} 已按 X", DateTime.Now);

                if (!WaitOrImmediateExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.SkillPoints.MinuteLoopAfterXWaitMs)))
                {
                    break;
                }

                ReleaseW();
                TapKey(KeyEnter);
                Console.WriteLine("{0:HH:mm:ss} 已按 Enter", DateTime.Now);

                if (!WaitOrImmediateExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TenSecondsMs)))
                {
                    break;
                }

                AddSkillPointsForCompletedLoop();
                if (SafeStopRequested)
                {
                    StopRequested = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            FH6FailureLog.Write("MinuteWLoop.RunTimedActionLoop", ex);
            throw;
        }
        finally
        {
            StopRequested = true;
        }
    }

    private static bool WaitOrExit(TimeSpan duration)
    {
        return WaitOrImmediateExit(duration);
    }

    private static bool WaitOrImmediateExit(TimeSpan duration)
    {
        DateTime deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            if (ExitRequested())
            {
                return false;
            }

            Thread.Sleep(25);
        }

        return true;
    }

    private static bool ExitRequested()
    {
        PollSafeStopFile();
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

    private static void PollSafeStopFile()
    {
        if (SafeStopRequested || string.IsNullOrWhiteSpace(SafeStopFile)) return;
        if (File.Exists(SafeStopFile))
        {
            SafeStopRequested = true;
            Console.WriteLine("{0:HH:mm:ss} 收到安全退出请求：跑完当前循环后退出", DateTime.Now);
        }
    }

    private static string ParseSafeStopFile(string[] args)
    {
        return ParseArg(args, "--safe-stop-file");
    }

    private static string ParseArg(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name) return true;
        }
        return false;
    }

    private static void AddSkillPointsForCompletedLoop()
    {
        if (!TrackSkillPoints) return;
        int before = SkillPoints;
        SkillPoints = Math.Min(FH6AutomationConstants.SkillPoints.Max, SkillPoints + FH6AutomationConstants.SkillPoints.MinuteLoopGain);
        MinuteLoopCount++;
        AppendSkillPointEvent("minute_loop_completed", before, SkillPoints, SkillPoints - before, SuperWheelspins, SuperWheelspins);
        WriteSkillPointsState("minute_loop_completed");
        Console.WriteLine("{0:HH:mm:ss} 技术点计数 {1} -> {2}", DateTime.Now, before, SkillPoints);
        if (SkillPoints >= FH6AutomationConstants.SkillPoints.Max)
        {
            SafeStopRequested = true;
            StopRequested = true;
            Console.WriteLine("{0:HH:mm:ss} 技术点已到 {1}，安全停止。", DateTime.Now, FH6AutomationConstants.SkillPoints.Max);
        }
    }

    private static void ReadSkillPointsState(string path, int fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                SkillPoints = fallback;
                return;
            }
            string body = File.ReadAllText(path);
            SkillPoints = Math.Max(0, Math.Min(FH6AutomationConstants.SkillPoints.Max, ReadIntField(body, "skill_points", fallback)));
            SuperWheelspins = Math.Max(0, ReadIntField(body, "super_wheelspins", SuperWheelspins));
            EventIndex = Math.Max(0, ReadIntField(body, "event_index", EventIndex));
            MinuteLoopCount = Math.Max(0, ReadIntField(body, "minute_loop_count", MinuteLoopCount));
        }
        catch
        {
            SkillPoints = fallback;
        }
    }

    private static int ReadIntField(string body, string name, int fallback)
    {
        Match match = Regex.Match(body, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)");
        if (!match.Success) return fallback;
        int value;
        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
    }

    private static void WriteSkillPointsState(string reason)
    {
        if (!TrackSkillPoints) return;
        try
        {
            string directory = Path.GetDirectoryName(SkillPointsStateFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string body = "{"
                + "\"schema\":\"fh6_skill_points_state.v1\","
                + "\"updated_at\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "\","
                + "\"reason\":\"" + reason + "\","
                + "\"skill_points\":" + SkillPoints.ToString(CultureInfo.InvariantCulture) + ","
                + "\"super_wheelspins\":" + SuperWheelspins.ToString(CultureInfo.InvariantCulture) + ","
                + "\"event_index\":" + EventIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"minute_loop_count\":" + MinuteLoopCount.ToString(CultureInfo.InvariantCulture)
                + "}";
            File.WriteAllText(SkillPointsStateFile, body);
        }
        catch (Exception ex)
        {
            Console.WriteLine("技术点计数写入失败：{0}", ex.Message);
            FH6FailureLog.Write("MinuteWLoop.WriteSkillPointsState", ex);
        }
    }

    private static void AppendSkillPointEvent(string reason, int before, int after, int delta, int superBefore, int superAfter)
    {
        if (string.IsNullOrWhiteSpace(SkillPointsLogFile)) return;
        try
        {
            string directory = Path.GetDirectoryName(SkillPointsLogFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            EventIndex++;
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff}\tevent={1}\ttask=MinuteWLoop\tcycle=0\tloop={2}\treason={3}\tskill={4}->{5}\tdelta={6}\tsuper={7}->{8}",
                DateTime.Now,
                EventIndex,
                MinuteLoopCount,
                reason,
                before,
                after,
                delta,
                superBefore,
                superAfter);
            File.AppendAllText(SkillPointsLogFile, line + Environment.NewLine);
            Console.WriteLine("{0:HH:mm:ss} 技术点事件：{1} {2}->{3} ({4:+#;-#;0})", DateTime.Now, reason, before, after, delta);
        }
        catch (Exception ex)
        {
            Console.WriteLine("技术点事件日志写入失败：{0}", ex.Message);
            FH6FailureLog.Write("MinuteWLoop.AppendSkillPointEvent", ex);
        }
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void TapKey(ushort virtualKey)
    {
        lock (InputLock)
        {
            SendKeyboardInput(virtualKey, false);
            WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TapMs));
            SendKeyboardInput(virtualKey, true);
        }
    }

    private static void SendKeyDown(ushort virtualKey)
    {
        lock (InputLock)
        {
            SendKeyboardInput(virtualKey, false);
        }
    }

    private static void ScheduleWPressAfterEnter()
    {
        Thread pressThread = new Thread(RunWPressAfterEnter);
        pressThread.IsBackground = true;
        pressThread.Start();
    }

    private static void RunWPressAfterEnter()
    {
        if (!WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.OneSecondMs)))
        {
            return;
        }

        SendKeyDown(KeyW);
        Console.WriteLine("{0:HH:mm:ss} W 已按下并保持", DateTime.Now);
    }

    private static void ReleaseW()
    {
        lock (InputLock)
        {
            SendKeyboardInput(KeyW, true);
        }
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
