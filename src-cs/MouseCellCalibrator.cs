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
    internal static class MouseCellCalibrator
    {
        public static RectangleF Capture(int monitorIndex)
        {
            Screen[] screens = Screen.AllScreens;
            int index = Math.Max(0, monitorIndex - 1);
            if (index >= screens.Length) index = 0;

            using (ConsoleWindow.Hide())
            {
                using (CellCalibrationOverlayForm form = new CellCalibrationOverlayForm(screens[index].Bounds))
                {
                    DialogResult result = form.ShowDialog();
                    return result == DialogResult.OK ? form.SelectedRectangle : RectangleF.Empty;
                }
            }
        }
    }

    internal sealed class CellCalibrationOverlayForm : Form
    {
        private const int VK_LBUTTON = 0x01;
        private const int VK_ESCAPE = 0x1B;

        private readonly Rectangle screenBounds;
        private readonly System.Windows.Forms.Timer timer;
        private bool dragging;
        private bool wasDown;
        private Point start;
        private Point current;
            private string message = "框选所有完整可见车辆格子的整体区域：从左上完整格拖到右下完整格，Esc 取消";

        public RectangleF SelectedRectangle { get; private set; }

        public CellCalibrationOverlayForm(Rectangle screenBounds)
        {
            this.screenBounds = screenBounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            DoubleBuffered = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 16;
            timer.Tick += PollMouse;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MakeClickThrough();
            timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            base.OnFormClosed(e);
        }

        private void PollMouse(object sender, EventArgs e)
        {
            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
            {
                SelectedRectangle = RectangleF.Empty;
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            Point cursor;
            GetCursorPos(out cursor);
            bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool inside = screenBounds.Contains(cursor);

            if (down && !wasDown && inside)
            {
                dragging = true;
                start = cursor;
                current = cursor;
                message = "松开鼠标左键保存这个完整可见车辆区域，Esc 取消";
                Invalidate();
            }
            else if (down && dragging)
            {
                current = cursor;
                Invalidate();
            }
            else if (!down && wasDown && dragging)
            {
                dragging = false;
                current = cursor;
                Rectangle rect = NormalizedRect(start, current);
                if (rect.Width < 20 || rect.Height < 20)
                {
                    message = "框选太小，请重新框选完整可见车辆区域";
                    Invalidate();
                }
                else
                {
                    SelectedRectangle = new RectangleF(rect.Left, rect.Top, rect.Width, rect.Height);
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
            }

            wasDown = down;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Brush labelBack = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            using (Brush labelBrush = new SolidBrush(Color.White))
            using (Font font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold))
            {
                SizeF size = g.MeasureString(message, font);
                RectangleF label = new RectangleF(Math.Max(24, (Width - size.Width - 28) / 2), 24, size.Width + 28, size.Height + 18);
                g.FillRectangle(labelBack, label);
                g.DrawString(message, font, labelBrush, label.X + 14, label.Y + 8);
            }

            if (!dragging) return;

            Rectangle local = ToLocal(NormalizedRect(start, current));
            Point localStart = ToLocal(start);
            Point localCurrent = ToLocal(current);
            using (Pen rectPen = new Pen(Color.FromArgb(0, 180, 255), 2))
            using (Pen pointPen = new Pen(Color.Yellow, 2))
            using (Brush pointBrush = new SolidBrush(Color.Yellow))
            {
                g.DrawRectangle(rectPen, local);
                g.FillEllipse(pointBrush, localStart.X - 3, localStart.Y - 3, 6, 6);
                g.DrawEllipse(pointPen, localCurrent.X - 4, localCurrent.Y - 4, 8, 8);
            }
        }

        private Point ToLocal(Point screenPoint)
        {
            return new Point(screenPoint.X - screenBounds.Left, screenPoint.Y - screenBounds.Top);
        }

        private Rectangle ToLocal(Rectangle screenRect)
        {
            return new Rectangle(screenRect.Left - screenBounds.Left, screenRect.Top - screenBounds.Top, screenRect.Width, screenRect.Height);
        }

        private static Rectangle NormalizedRect(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void MakeClickThrough()
        {
            int style = GetWindowLong(Handle, -20);
            SetWindowLong(Handle, -20, style | 0x00080000 | 0x00000020 | 0x00000080);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);
    }
}
