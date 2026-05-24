using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace FH6SkillPointOcr
{
    internal sealed class WindowBinding
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string ProcessName;
        public string Title;
        public Rectangle ClientBounds;

        public string Summary()
        {
            return string.Format(
                "hwnd=0x{0:X}, pid={1}, process={2}, title={3}, client=[{4},{5},{6},{7}]",
                Handle.ToInt64(),
                ProcessId,
                string.IsNullOrWhiteSpace(ProcessName) ? "?" : ProcessName,
                string.IsNullOrWhiteSpace(Title) ? "?" : Title,
                ClientBounds.Left,
                ClientBounds.Top,
                ClientBounds.Width,
                ClientBounds.Height);
        }
    }

    internal static class WindowLocator
    {
        private const int GA_ROOT = 2;
        private const int GW_OWNER = 4;
        private const int MinClientWidth = 320;
        private const int MinClientHeight = 200;

        public static bool TryBindForeground(int currentProcessId, out WindowBinding binding)
        {
            binding = null;
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            hwnd = GetAncestor(hwnd, GA_ROOT);
            return TryCreateBinding(hwnd, currentProcessId, out binding);
        }

        public static bool TryBindFromPoint(Point point, int currentProcessId, out WindowBinding binding)
        {
            binding = null;
            IntPtr found = IntPtr.Zero;
            WindowBinding selected = null;
            EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
            {
                WindowBinding candidate;
                if (!TryCreateBinding(hwnd, currentProcessId, out candidate)) return true;
                if (!candidate.ClientBounds.Contains(point)) return true;
                found = hwnd;
                selected = candidate;
                return false;
            }, IntPtr.Zero);

            binding = selected;
            return found != IntPtr.Zero && binding != null;
        }

        public static bool TryRefresh(WindowBinding binding)
        {
            if (binding == null || binding.Handle == IntPtr.Zero) return false;
            Rectangle bounds;
            if (!TryGetClientBounds(binding.Handle, out bounds)) return false;
            binding.ClientBounds = bounds;
            binding.Title = GetWindowTitle(binding.Handle);
            binding.ProcessName = GetProcessName(binding.ProcessId);
            return true;
        }

        private static bool TryCreateBinding(IntPtr hwnd, int currentProcessId, out WindowBinding binding)
        {
            binding = null;
            if (hwnd == IntPtr.Zero) return false;
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;

            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0 || processId == (uint)currentProcessId) return false;

            Rectangle bounds;
            if (!TryGetClientBounds(hwnd, out bounds)) return false;
            if (bounds.Width < MinClientWidth || bounds.Height < MinClientHeight) return false;

            binding = new WindowBinding
            {
                Handle = hwnd,
                ProcessId = processId,
                ProcessName = GetProcessName(processId),
                Title = GetWindowTitle(hwnd),
                ClientBounds = bounds
            };
            return true;
        }

        private static bool TryGetClientBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            RECT client;
            if (!GetClientRect(hwnd, out client)) return false;
            if (client.Right <= client.Left || client.Bottom <= client.Top) return false;

            POINT topLeft = new POINT { X = client.Left, Y = client.Top };
            if (!ClientToScreen(hwnd, ref topLeft)) return false;
            bounds = new Rectangle(topLeft.X, topLeft.Y, client.Right - client.Left, client.Bottom - client.Top);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0) return "";
            StringBuilder sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetProcessName(uint processId)
        {
            try
            {
                Process process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch
            {
                return "";
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, int gaFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
