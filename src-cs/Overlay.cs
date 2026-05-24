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
    internal sealed class OverlayRenderer
    {
        private readonly bool enabled;
        private OverlayForm form;
        private Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);

        public OverlayRenderer(bool enabled)
        {
            this.enabled = enabled;
        }

        public void Start()
        {
            if (!enabled) return;
            thread = new Thread(RunThread);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(2000);
        }

        public void Update(OverlayDetails details, List<CellView> cells, List<OcrFieldView> ocrFields)
        {
            if (!enabled || form == null || form.IsDisposed) return;
            form.SafeUpdate(details, cells, ocrFields);
        }

        public void Update(OverlayDetails details, List<CellView> cells, List<OcrFieldView> ocrFields, List<OverlayPointView> points)
        {
            if (!enabled || form == null || form.IsDisposed) return;
            form.SafeUpdate(details, cells, ocrFields, points);
        }

        public void HideForCapture(int ms)
        {
            if (enabled && form != null && !form.IsDisposed) form.SafeHide();
            FlushDesktopComposition();
            Thread.Sleep(Math.Max(ms, 150));
        }

        public void ShowOverlay()
        {
            if (enabled && form != null && !form.IsDisposed) form.SafeShow();
        }

        public void Stop()
        {
            if (enabled && form != null && !form.IsDisposed) form.SafeClose();
        }

        private void RunThread()
        {
            try
            {
                Application.EnableVisualStyles();
                form = new OverlayForm();
                ready.Set();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OVERLAY] 叠加层启动失败：" + ex.Message);
                FH6FailureLog.Write("Overlay.RunThread", ex);
                ready.Set();
            }
        }

        private static void FlushDesktopComposition()
        {
            try
            {
                DwmFlush();
            }
            catch
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();
    }

    internal sealed class OverlayForm : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private OverlayDetails details = new OverlayDetails();
        private List<CellView> cells = new List<CellView>();
        private List<OcrFieldView> ocrFields = new List<OcrFieldView>();
        private List<OverlayPointView> points = new List<OverlayPointView>();

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            Bounds = SystemInformation.VirtualScreen;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MakeClickThrough();
        }

        public void SafeUpdate(OverlayDetails newDetails, List<CellView> newCells, List<OcrFieldView> newOcrFields)
        {
            SafeUpdate(newDetails, newCells, newOcrFields, null);
        }

        public void SafeUpdate(OverlayDetails newDetails, List<CellView> newCells, List<OcrFieldView> newOcrFields, List<OverlayPointView> newPoints)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<OverlayDetails, List<CellView>, List<OcrFieldView>, List<OverlayPointView>>(SafeUpdate), newDetails, newCells, newOcrFields, newPoints);
                return;
            }
            details = newDetails;
            cells = newCells ?? new List<CellView>();
            ocrFields = newOcrFields ?? new List<OcrFieldView>();
            points = newPoints ?? new List<OverlayPointView>();
            Invalidate();
        }

        public void SafeHide()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(SafeHide));
                return;
            }
            Hide();
            Update();
        }

        public void SafeShow()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(SafeShow));
                return;
            }
            Show();
        }

        public void SafeClose()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(SafeClose));
                return;
            }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            foreach (CellView cell in cells)
            {
                RectangleF rect = new RectangleF(cell.Rect.Left - virtualScreen.Left, cell.Rect.Top - virtualScreen.Top, cell.Rect.Width, cell.Rect.Height);
                Color border = Color.FromArgb(0, 115, 255);
                using (Pen pen = new Pen(border, 1))
                {
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    if (cell.State == "state2" || cell.State == "state3" || cell.State == "state4")
                    {
                        g.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
                    }
                    if (cell.State == "state3")
                    {
                        g.DrawLine(pen, rect.Right, rect.Top, rect.Left, rect.Bottom);
                    }
                    if (cell.State == "state4")
                    {
                        float midX = rect.Left + rect.Width / 2;
                        float midY = rect.Top + rect.Height / 2;
                        g.DrawLine(pen, midX, rect.Top, midX, rect.Bottom);
                        g.DrawLine(pen, rect.Left, midY, rect.Right, midY);
                    }
                    if (cell.DriveCandidate)
                    {
                        float diameter = Math.Max(10, Math.Min(rect.Width, rect.Height) * 0.14f);
                        float circleX = rect.Left + rect.Width / 2 - diameter / 2;
                        float circleY = rect.Top + rect.Height / 2 - diameter / 2;
                        g.DrawEllipse(pen, circleX, circleY, diameter, diameter);
                    }
                    if (cell.Chosen)
                    {
                        using (Pen chosenPen = new Pen(Color.FromArgb(210, 90, 255), 3))
                        {
                            g.DrawRectangle(chosenPen, rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
                        }
                    }
                }
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    string label = string.Format(CultureInfo.InvariantCulture, "G{0},R{1} S{2}", cell.GlobalCol, cell.Row, cell.Known ? cell.StateCode.ToString(CultureInfo.InvariantCulture) : "?");
                    g.DrawString(label, new Font("Consolas", 10, FontStyle.Bold), textBrush, rect.X + 8, rect.Y + 8);
                }
            }

            foreach (OcrFieldView field in ocrFields)
            {
                RectangleF rect = new RectangleF(field.Rect.Left - virtualScreen.Left, field.Rect.Top - virtualScreen.Top, field.Rect.Width, field.Rect.Height);
                Color fieldColor = OcrFieldColor(field.Label);
                using (Pen pen = new Pen(fieldColor, 1.5f))
                using (Brush labelBack = new SolidBrush(Color.FromArgb(205, fieldColor.R / 3, fieldColor.G / 3, fieldColor.B / 3)))
                using (Brush labelBrush = new SolidBrush(Color.White))
                using (Font labelFont = new Font("Microsoft YaHei UI", 9, FontStyle.Bold))
                {
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    string label = field.Confidence >= 0
                        ? string.Format(CultureInfo.InvariantCulture, "{0} {1:0}", field.Label, field.Confidence)
                        : field.Label;
                    SizeF size = g.MeasureString(label, labelFont);
                    RectangleF labelRect = new RectangleF(rect.X, Math.Max(0, rect.Y - size.Height - 2), size.Width + 8, size.Height + 2);
                    g.FillRectangle(labelBack, labelRect);
                    g.DrawString(label, labelFont, labelBrush, labelRect.X + 4, labelRect.Y);
                }
            }

            foreach (OverlayPointView point in points)
            {
                float radius = Math.Max(3, point.Radius);
                float x = point.Point.X - virtualScreen.Left;
                float y = point.Point.Y - virtualScreen.Top;
                using (Brush brush = new SolidBrush(point.Color))
                using (Pen pen = new Pen(Color.White, 2))
                using (Font font = new Font("Consolas", 11, FontStyle.Bold))
                using (Brush labelBrush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                    if (!string.IsNullOrWhiteSpace(point.Label))
                    {
                        g.DrawString(point.Label, font, labelBrush, x + radius + 6, y - radius - 2);
                    }
                }
            }

            string debugLine = details.DebugSteps > 0 ? "\n调试步数: " + details.DebugSteps.ToString(CultureInfo.InvariantCulture) : "";
            string minuteLine = string.IsNullOrWhiteSpace(details.MinuteLoop) || details.MinuteLoop == "-" ? "" : "\n刷点进度: " + details.MinuteLoop;
            string text = string.Format(
                "模式: {0}\n大阶段: {1}\n当前操作: {2}\n下一步: {3}\n动作串: {4}\n{5}\n{6}    {7}{8}\n虚拟表: {9}\n总耗时: {10}    失败: {11}\nSpace+C 立即退出 / Space+V 安全结束",
                details.Mode,
                details.Stage,
                details.Status,
                details.NextAction,
                details.ActionSequence,
                details.Cycle,
                details.SkillPoints,
                details.SuperWheelspins,
                minuteLine + debugLine,
                details.VirtualList,
                FormatElapsed(details.ElapsedSeconds),
                details.Failures);
            using (Brush panelBrush = new SolidBrush(Color.FromArgb(120, 17, 17, 17)))
            using (Pen panelPen = new Pen(Color.FromArgb(0, 140, 255), 2))
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Font font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold))
            {
                SizeF textSize = g.MeasureString(text, font);
                float panelWidth = Math.Min(Width - 16, Math.Max(900, textSize.Width + 32));
                float panelHeight = Math.Min(Height - 16, Math.Max(250, textSize.Height + 32));
                RectangleF panel = new RectangleF(8, 8, panelWidth, panelHeight);
                g.FillRectangle(panelBrush, panel);
                g.DrawRectangle(panelPen, panel.X, panel.Y, panel.Width, panel.Height);
                g.DrawString(text, font, textBrush, 16, 16);
            }
        }

        private static Color OcrFieldColor(string label)
        {
            if (label == "车名") return Color.FromArgb(0, 220, 170);
            if (label == "全新") return Color.FromArgb(255, 150, 40);
            if (label == "600") return Color.FromArgb(255, 80, 80);
            if (label == "斯巴鲁-选中") return Color.FromArgb(255, 60, 220);
            if (label == "斯巴鲁") return Color.FromArgb(210, 120, 255);
            return Color.FromArgb(0, 220, 255);
        }

        private static string FormatElapsed(double seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0}小时{1:00}分{2:00}秒", Math.Floor(span.TotalHours), span.Minutes, span.Seconds);
            }
            if (span.TotalMinutes >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0}分{1:00}秒", Math.Floor(span.TotalMinutes), span.Seconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:0}秒", span.TotalSeconds);
        }

        private void MakeClickThrough()
        {
            int style = GetWindowLong(Handle, -20);
            SetWindowLong(Handle, -20, style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

}
