using System;
using System.Runtime.InteropServices;
using System.Threading;
using FH6AutomationShared;

internal static class Program
{
    private const int ExitVirtualKey = FH6AutomationConstants.Keys.ExitVirtualKey;
    private const int HotkeyModifierVirtualKey = FH6AutomationConstants.Keys.HotkeyModifierVirtualKey;
    private const ushort KeyW = FH6AutomationConstants.Keys.W;
    private const ushort KeyD = FH6AutomationConstants.Keys.D;
    private const ushort KeyX = FH6AutomationConstants.Keys.X;
    private static readonly object InputLock = new object();

    private static int Main()
    {
        FH6FailureLog.InstallGlobalHandlers("AutoInputLoop");
        try
        {
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] " + ex.Message);
            FH6FailureLog.Write("AutoInputLoop", ex);
            return 1;
        }
    }

    private static void Run()
    {
        Console.Title = "AutoInputLoop - Space+C 退出";
        Console.WriteLine("程序已启动。");
        Console.WriteLine("每 10 秒依次执行：鼠标左键点击一次、W 按下 0.1 秒、D 按下 0.1 秒。");
        Console.WriteLine("另有一个独立循环：启动等待 10 秒后，每 5 秒按 X 0.1 秒。");
        Console.WriteLine("按 Space+C 退出。请在第一次 10 秒等待内切到目标窗口。");

        Thread xLoopThread = new Thread(RunXLoop);
        xLoopThread.Start();

        while (!ExitRequested())
        {
            if (!WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TenSecondsMs)))
            {
                break;
            }

            ClickLeftMouse();
            Console.WriteLine("{0:HH:mm:ss} 已点击鼠标左键", DateTime.Now);

            if (!WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TenSecondsMs)))
            {
                break;
            }

            PressKey(KeyW, TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TapMs));
            Console.WriteLine("{0:HH:mm:ss} 已按 W", DateTime.Now);

            if (!WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TenSecondsMs)))
            {
                break;
            }

            PressKey(KeyD, TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TapMs));
            Console.WriteLine("{0:HH:mm:ss} 已按 D", DateTime.Now);
        }

        xLoopThread.Join();
        Console.WriteLine("已退出。");
    }

    private static void RunXLoop()
    {
        if (!WaitOrExit(TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TenSecondsMs)))
        {
            return;
        }

        while (!ExitRequested())
        {
            PressKey(KeyX, TimeSpan.FromMilliseconds(FH6AutomationConstants.Timing.TapMs));
            Console.WriteLine("{0:HH:mm:ss} 已按 X", DateTime.Now);

            if (!WaitOrExit(TimeSpan.FromSeconds(5)))
            {
                break;
            }
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

            Thread.Sleep(25);
        }

        return true;
    }

    private static bool ExitRequested()
    {
        return IsKeyDown(HotkeyModifierVirtualKey) && IsKeyDown(ExitVirtualKey);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void ClickLeftMouse()
    {
        lock (InputLock)
        {
            SendMouseInput(MouseEventFlags.LeftDown);
            Thread.Sleep(FH6AutomationConstants.Timing.ShortMouseClickMs);
            SendMouseInput(MouseEventFlags.LeftUp);
        }
    }

    private static void PressKey(ushort virtualKey, TimeSpan holdFor)
    {
        lock (InputLock)
        {
            SendKeyboardInput(virtualKey, false);
            WaitOrExit(holdFor);
            SendKeyboardInput(virtualKey, true);
        }
    }

    private static void SendMouseInput(MouseEventFlags flags)
    {
        INPUT input = new INPUT();
        input.type = InputType.Mouse;
        input.U.mi = new MOUSEINPUT();
        input.U.mi.dwFlags = flags;

        SendInputOrThrow(input);
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
        Mouse = 0,
        Keyboard = 1
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004
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
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public MouseEventFlags dwFlags;
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
