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
    internal sealed class InputController
    {
        public const int VK_STEP = FH6AutomationConstants.Keys.DebugStepVirtualKey;
        private const int VK_HOTKEY_MODIFIER = FH6AutomationConstants.Keys.HotkeyModifierVirtualKey;
        private const int VK_C = FH6AutomationConstants.Keys.ExitVirtualKey;
        private const int VK_V = FH6AutomationConstants.Keys.SafeStopVirtualKey;
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120;

        private readonly int tapMs;
        private readonly int repeatIntervalMs;
        private readonly bool dryRun;
        private readonly string safeStopFile;
        private bool safeStopRequested;
        private readonly Dictionary<string, ushort> vk = new Dictionary<string, ushort>
        {
            {"BACKSPACE", FH6AutomationConstants.Keys.Backspace},
            {"ENTER", FH6AutomationConstants.Keys.Enter},
            {"ESC", FH6AutomationConstants.Keys.Escape},
            {"UP", FH6AutomationConstants.Keys.Up},
            {"DOWN", FH6AutomationConstants.Keys.Down},
            {"LEFT", FH6AutomationConstants.Keys.Left},
            {"RIGHT", FH6AutomationConstants.Keys.Right}
        };

        public InputController(int tapMs, int repeatIntervalMs, bool dryRun, string safeStopFile)
        {
            this.tapMs = tapMs;
            this.repeatIntervalMs = repeatIntervalMs;
            this.dryRun = dryRun;
            this.safeStopFile = safeStopFile;
        }

        public bool ShouldStop()
        {
            return (GetAsyncKeyState(VK_HOTKEY_MODIFIER) & 0x8000) != 0 && (GetAsyncKeyState(VK_C) & 0x8000) != 0;
        }

        public bool SafeStopRequested
        {
            get
            {
                PollSafeStop();
                return safeStopRequested;
            }
        }

        public bool PollSafeStop()
        {
            if (!safeStopRequested && (GetAsyncKeyState(VK_HOTKEY_MODIFIER) & 0x8000) != 0 && (GetAsyncKeyState(VK_V) & 0x8000) != 0)
            {
                safeStopRequested = true;
                Console.WriteLine("[SAFE_STOP] Space+V detected, will stop after current loop reset.");
            }
            if (!safeStopRequested && !string.IsNullOrWhiteSpace(safeStopFile) && File.Exists(safeStopFile))
            {
                safeStopRequested = true;
                Console.WriteLine("[SAFE_STOP] safe-stop-file detected, will stop after current loop reset.");
            }
            return safeStopRequested;
        }

        public void SleepMs(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(Math.Min(FH6AutomationConstants.Timing.SleepSliceMs, Math.Max(1, ms - (int)sw.ElapsedMilliseconds)));
            }
        }

        public void WaitForVkPress(int key)
        {
            while ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
            while ((GetAsyncKeyState(key) & 0x8000) == 0)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
            while ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                if (ShouldStop()) throw new StopRequestedException();
                PollSafeStop();
                Thread.Sleep(FH6AutomationConstants.Timing.DebugKeyPollMs);
            }
        }

        public void Tap(string key)
        {
            Console.WriteLine("[INPUT] " + key);
            KeyDown(key);
            SleepMs(tapMs);
            KeyUp(key);
            SleepMs(repeatIntervalMs);
        }

        public void Click()
        {
            Console.WriteLine("[INPUT] LEFT_CLICK");
            if (!dryRun)
            {
                SendMouse(0, 0, 0, MOUSEEVENTF_LEFTDOWN);
                SleepMs(tapMs);
                SendMouse(0, 0, 0, MOUSEEVENTF_LEFTUP);
            }
            else
            {
                SleepMs(tapMs);
            }
            SleepMs(repeatIntervalMs);
        }

        public void MoveTo(int x, int y)
        {
            Console.WriteLine("[INPUT] MOVE " + x + "," + y);
            if (!dryRun) SetCursorPos(x, y);
            SleepMs(repeatIntervalMs);
        }

        public void ScrollDown(int ticks, int tickDelayMs)
        {
            Console.WriteLine("[INPUT] WHEEL_DOWN x" + ticks);
            for (int i = 0; i < ticks; i++)
            {
                if (!dryRun)
                {
                    uint sent = SendMouse(0, 0, -WHEEL_DELTA, MOUSEEVENTF_WHEEL);
                    if (sent == 0)
                    {
                        Console.WriteLine("[INPUT_ERROR] WHEEL SendInput failed, lastError=" + Marshal.GetLastWin32Error());
                    }
                }
                SleepMs(tickDelayMs);
            }
        }

        public void ScrollUp(int ticks, int tickDelayMs)
        {
            Console.WriteLine("[INPUT] WHEEL_UP x" + ticks);
            for (int i = 0; i < ticks; i++)
            {
                if (!dryRun)
                {
                    uint sent = SendMouse(0, 0, WHEEL_DELTA, MOUSEEVENTF_WHEEL);
                    if (sent == 0)
                    {
                        Console.WriteLine("[INPUT_ERROR] WHEEL SendInput failed, lastError=" + Marshal.GetLastWin32Error());
                    }
                }
                SleepMs(tickDelayMs);
            }
        }

        private void KeyDown(string key)
        {
            if (!dryRun) SendKeyboard(vk[key], 0);
        }

        private void KeyUp(string key)
        {
            if (!dryRun) SendKeyboard(vk[key], KEYEVENTF_KEYUP);
        }

        private void SendKeyboard(ushort virtualKey, int flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki.wVk = virtualKey;
            input.U.ki.wScan = 0;
            input.U.ki.dwFlags = flags;
            input.U.ki.time = 0;
            input.U.ki.dwExtraInfo = IntPtr.Zero;
            SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private uint SendMouse(int dx, int dy, int data, int flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            input.U.mi.dx = dx;
            input.U.mi.dy = dy;
            input.U.mi.mouseData = data;
            input.U.mi.dwFlags = flags;
            input.U.mi.time = 0;
            input.U.mi.dwExtraInfo = IntPtr.Zero;
            return SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
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
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }
    }

}
