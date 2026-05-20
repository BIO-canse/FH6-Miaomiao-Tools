using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class EnterTapLoop
{
    private const int ExitVirtualKey = 0x43; // C
    private const int AltVirtualKey = 0x12;
    private const ushort KeyEnter = 0x0D;

    private static void Main()
    {
        Console.Title = "EnterTapLoop - Alt+C 退出";
        Console.WriteLine("程序已启动。");
        Console.WriteLine("启动后先等待 10 秒，然后循环按 Enter 0.1 秒，等待 0.1 秒。");
        Console.WriteLine("按 Alt+C 退出。请在第一次 10 秒等待内切到目标窗口。");

        if (!WaitOrExit(TimeSpan.FromSeconds(10)))
        {
            Console.WriteLine("已退出。");
            return;
        }

        while (!ExitRequested())
        {
            TapEnter();

            if (!WaitOrExit(TimeSpan.FromMilliseconds(100)))
            {
                break;
            }
        }

        Console.WriteLine("已退出。");
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
        return IsKeyDown(AltVirtualKey) && IsKeyDown(ExitVirtualKey);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void TapEnter()
    {
        SendKeyboardInput(KeyEnter, false);
        WaitOrExit(TimeSpan.FromMilliseconds(100));
        SendKeyboardInput(KeyEnter, true);
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
