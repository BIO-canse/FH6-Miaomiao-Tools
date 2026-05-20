using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class MinuteWLoop
{
    private const int ExitVirtualKey = 0x43; // C
    private const int AltVirtualKey = 0x12;
    private const ushort KeyEnter = 0x0D;
    private const ushort KeyW = 0x57;
    private const ushort KeyX = 0x58;
    private static readonly object InputLock = new object();
    private static volatile bool StopRequested;

    private static void Main()
    {
        Console.Title = "MinuteWLoop - Alt+C 退出";
        Console.WriteLine("程序已启动。");
        Console.WriteLine("启动后先等待 10 秒。");
        Console.WriteLine("主循环：等待 1 分钟，按 X，等待 1 秒，按 Enter，等待 10 秒，按 Enter。");
        Console.WriteLine("并行动作：W 默认保持长按；每轮第二次 Enter 过后 1 秒，松开 W 0.1 秒再按回去。");
        Console.WriteLine("按 Alt+C 退出。请在第一次 10 秒等待内切到目标窗口。");

        if (!WaitOrExit(TimeSpan.FromSeconds(10)))
        {
            ReleaseW();
            Console.WriteLine("已退出。");
            return;
        }

        Thread timedActionThread = new Thread(RunTimedActionLoop);
        Thread wHoldThread = new Thread(RunWHoldLoop);
        timedActionThread.Start();
        wHoldThread.Start();

        while (!ExitRequested())
        {
            Thread.Sleep(25);
        }

        timedActionThread.Join();
        wHoldThread.Join();
        ReleaseW();
        Console.WriteLine("已退出。");
    }

    private static void RunTimedActionLoop()
    {
        while (!ExitRequested())
        {
            if (!WaitOrExit(TimeSpan.FromMinutes(1)))
            {
                break;
            }

            TapKey(KeyX);
            Console.WriteLine("{0:HH:mm:ss} 已按 X", DateTime.Now);

            if (!WaitOrExit(TimeSpan.FromSeconds(1)))
            {
                break;
            }

            TapKey(KeyEnter);
            Console.WriteLine("{0:HH:mm:ss} 已按 Enter", DateTime.Now);

            if (!WaitOrExit(TimeSpan.FromSeconds(10)))
            {
                break;
            }

            TapKey(KeyEnter);
            Console.WriteLine("{0:HH:mm:ss} 已按 Enter", DateTime.Now);
            ScheduleWReleasePulse();
        }
    }

    private static void RunWHoldLoop()
    {
        SendKeyDown(KeyW);
        Console.WriteLine("{0:HH:mm:ss} W 已按下并保持", DateTime.Now);

        while (!ExitRequested())
        {
            Thread.Sleep(25);
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
        if (StopRequested)
        {
            return true;
        }

        if (IsKeyDown(AltVirtualKey) && IsKeyDown(ExitVirtualKey))
        {
            StopRequested = true;
            return true;
        }

        return false;
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
            WaitOrExit(TimeSpan.FromMilliseconds(100));
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

    private static void ScheduleWReleasePulse()
    {
        Thread pulseThread = new Thread(RunWReleasePulse);
        pulseThread.IsBackground = true;
        pulseThread.Start();
    }

    private static void RunWReleasePulse()
    {
        if (!WaitOrExit(TimeSpan.FromSeconds(1)))
        {
            return;
        }

        ReleaseW();
        Console.WriteLine("{0:HH:mm:ss} W 已短暂松开", DateTime.Now);

        if (!WaitOrExit(TimeSpan.FromMilliseconds(100)))
        {
            return;
        }

        SendKeyDown(KeyW);
        Console.WriteLine("{0:HH:mm:ss} W 已重新按下并保持", DateTime.Now);
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
