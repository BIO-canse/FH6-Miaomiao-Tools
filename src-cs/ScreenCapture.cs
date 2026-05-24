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

        public ScreenCapture(int monitorIndex)
        {
            this.monitorIndex = monitorIndex;
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
            Screen[] screens = Screen.AllScreens;
            int index = Math.Max(0, monitorIndex - 1);
            if (index >= screens.Length) index = 0;
            return screens[index].Bounds;
        }
    }

}
