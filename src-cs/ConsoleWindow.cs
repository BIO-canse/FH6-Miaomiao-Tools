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
    internal static class ConsoleWindow
    {
        private static readonly object Sync = new object();
        private static int hideDepth;

        public static IDisposable Hide()
        {
            IntPtr handle = GetConsoleWindow();
            if (handle == IntPtr.Zero) return new Scope(IntPtr.Zero);
            lock (Sync)
            {
                if (hideDepth == 0) ShowWindow(handle, 0);
                hideDepth++;
            }
            return new Scope(handle);
        }

        private sealed class Scope : IDisposable
        {
            private IntPtr handle;

            public Scope(IntPtr handle)
            {
                this.handle = handle;
            }

            public void Dispose()
            {
                if (handle == IntPtr.Zero) return;
                lock (Sync)
                {
                    hideDepth = Math.Max(0, hideDepth - 1);
                    if (hideDepth == 0) ShowWindow(handle, 5);
                }
                handle = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
