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

namespace FH6SkillPointOcr
{
    internal sealed class ScreenCapture
    {
        private readonly int monitorIndex;
        private readonly int currentProcessId;

        public ScreenCapture(int monitorIndex)
        {
            this.monitorIndex = monitorIndex;
            currentProcessId = Process.GetCurrentProcess().Id;
        }

        public Screenshot Grab()
        {
            Rectangle bounds = GetBounds();
            return Grab(bounds);
        }

        public Screenshot Grab(Rectangle region)
        {
            Rectangle bounds = GetBounds();
            Rectangle clipped = Rectangle.Intersect(bounds, region);
            if (clipped.Width <= 0 || clipped.Height <= 0) clipped = bounds;
            Bitmap bitmap = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(clipped.Left, clipped.Top, 0, 0, clipped.Size, CopyPixelOperation.SourceCopy);
            }
            return new Screenshot(bitmap, clipped.Left, clipped.Top);
        }

        public Rectangle GetBounds()
        {
            Rectangle foregroundBounds;
            if (TryGetForegroundScreenBounds(out foregroundBounds)) return foregroundBounds;
            return GetConfiguredBounds();
        }

        private Rectangle GetConfiguredBounds()
        {
            Screen[] screens = Screen.AllScreens;
            int index = Math.Max(0, monitorIndex - 1);
            if (index >= screens.Length) index = 0;
            return screens[index].Bounds;
        }

        private bool TryGetForegroundScreenBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || IsIconic(hwnd)) return false;

            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId == (uint)currentProcessId) return false;

            Screen screen = Screen.FromHandle(hwnd);
            if (screen == null || screen.Bounds.Width <= 0 || screen.Bounds.Height <= 0) return false;
            bounds = screen.Bounds;
            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }

}
