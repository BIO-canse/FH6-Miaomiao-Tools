using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class BuyPreludeDebugProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            EnableDpiAwareness();

            try
            {
                string configPath = Path.Combine("config", "default.json");
                bool dryRun = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--config" && i + 1 < args.Length) configPath = args[++i];
                    else if (args[i] == "--dry-run") dryRun = true;
                }

                Config config = Config.Load(configPath);
                InputController input = new InputController(config.TapMs, config.RepeatIntervalMs, dryRun, null);
                ScreenCapture capture = new ScreenCapture(config.MonitorIndex);
                string debugDir = Path.Combine(config.ResolvePath(config.DebugDir), "buy-prelude-step");
                ResetDebugDir(debugDir);
                OverlayRenderer overlay = new OverlayRenderer(config.OverlayEnabled);
                overlay.Start();
                using (OcrReader ocr = new OcrReader(config, debugDir))
                {
                    Console.Title = "FH6BuyPreludeStepDebug - ` 下一步 / Space+C 退出";
                    Console.WriteLine("买车前置步进调试。每一步按 ` 继续，Space+C 退出。");
                    Console.WriteLine("起点：请先手动停在买车前置中即将按 Backspace 打开制造商页面的位置。");
                    Console.WriteLine("范围：只执行 Backspace 找制造商、点击斯巴鲁、Down 移到目标车；不会按 Enter 进入。");

                    Step(input, "Backspace，然后等待 0.5 秒", delegate
                    {
                        input.Tap("BACKSPACE");
                        input.SleepMs(FH6AutomationConstants.Timing.HalfSecondMs);
                    });

                    Step(input, "鼠标移动到屏幕中心，准备让滚轮落在制造商列表上", delegate
                    {
                        MoveMouseToScreenCenter(input, capture, "manufacturer list scroll focus");
                    });

                    Step(input, "滚轮向下 " + config.ManufacturerScrollTicks + " 格，然后等待 " + config.SingleScrollDelayMs + "ms", delegate
                    {
                        input.ScrollDown(config.ManufacturerScrollTicks, config.ScrollTickDelayMs);
                        input.SleepMs(config.SingleScrollDelayMs);
                    });

                    Step(input, "鼠标移动到屏幕右下角", delegate
                    {
                        MoveMouseToScreenBottomRight(input, capture, "idle after Subaru list scroll");
                    });

                    Step(input, "整屏 OCR 找斯巴鲁，移动并点击；然后等待 0.5 秒", delegate
                    {
                        FindSubaruAndClick(config, capture, ocr, input, overlay, debugDir);
                        input.SleepMs(FH6AutomationConstants.Timing.HalfSecondMs);
                    });

                    Step(input, "按 Down，把选中移动到目标车辆；不按 Enter 进入", delegate
                    {
                        input.Tap("DOWN");
                    });

                    Console.WriteLine("完成：已执行到 Down，不会继续 Enter。");
                }
                overlay.Stop();

                return 0;
            }
            catch (StopRequestedException)
            {
                Console.WriteLine("[EXIT] Space+C");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                return 1;
            }
        }

        private static void Step(InputController input, string description, Action action)
        {
            Console.WriteLine();
            Console.WriteLine("[NEXT] " + description);
            Console.Write("按 ` 继续：");
            input.WaitForVkPress(InputController.VK_STEP);
            Console.WriteLine();
            Console.WriteLine("[RUN] " + description);
            action();
        }

        private static void FindSubaruAndClick(Config config, ScreenCapture capture, OcrReader ocr, InputController input, OverlayRenderer overlay, string debugDir)
        {
            overlay.HideForCapture(config.OverlayHideBeforeCaptureMs);
            Screenshot shot = capture.Grab();
            overlay.ShowOverlay();
            OcrSnapshot snapshot = ocr.Read(shot, config.OcrPsm, "buy-prelude-subaru");
            List<OcrMatch> matches = FindConfiguredCjkTextMatches(config, ocr, snapshot, config.ManufacturerText);
            WriteOcrLog(debugDir, shot, matches);
            Console.WriteLine("[OCR] 斯巴鲁 matches=" + matches.Count);
            foreach (OcrMatch match in matches.OrderBy(m => m.Rect.Left).ThenBy(m => m.Rect.Top))
            {
                Point center = match.RectCenter();
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  text={0}, center=({1},{2}), rect=({3:0},{4:0},{5:0},{6:0}), conf={7:0.###}",
                    match.Text,
                    center.X,
                    center.Y,
                    match.Rect.Left,
                    match.Rect.Top,
                    match.Rect.Width,
                    match.Rect.Height,
                    match.Confidence));
            }

            if (matches.Count == 0) throw new InvalidOperationException("OCR 未找到斯巴鲁。");
            OcrMatch chosen = ChooseUiTextMatch(matches, config.ManufacturerText);
            Point point = chosen.RectCenter();
            File.AppendAllText(Path.Combine(debugDir, "ocr-candidates.log"), "[CHOSEN] center=(" + point.X + "," + point.Y + "), text=" + chosen.Text + "\r\n");
            Console.WriteLine("[OCR] 选择最左上的斯巴鲁 center=(" + point.X + "," + point.Y + ")");
            ShowSubaruOverlay(overlay, matches, chosen, point);
            input.MoveTo(point.X, point.Y);
            input.Click();
        }

        private static void WriteOcrLog(string debugDir, Screenshot shot, List<OcrMatch> matches)
        {
            Directory.CreateDirectory(debugDir);
            string path = Path.Combine(debugDir, "ocr-candidates.log");
            List<string> lines = new List<string>();
            lines.Add("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            lines.Add("screenshot_left=" + shot.Left + ", top=" + shot.Top + ", width=" + shot.Image.Width + ", height=" + shot.Image.Height);
            lines.Add("matches=" + matches.Count);
            int index = 0;
            foreach (OcrMatch match in matches.OrderBy(m => m.Rect.Left).ThenBy(m => m.Rect.Top))
            {
                Point center = match.RectCenter();
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] text={1}, center=({2},{3}), rect=({4:0.###},{5:0.###},{6:0.###},{7:0.###}), conf={8:0.###}",
                    index++,
                    match.Text,
                    center.X,
                    center.Y,
                    match.Rect.Left,
                    match.Rect.Top,
                    match.Rect.Width,
                    match.Rect.Height,
                    match.Confidence));
            }
            lines.Add("");
            File.AppendAllLines(path, lines, Encoding.UTF8);
        }

        private static void ResetDebugDir(string debugDir)
        {
            try
            {
                if (Directory.Exists(debugDir)) Directory.Delete(debugDir, true);
                Directory.CreateDirectory(debugDir);
            }
            catch
            {
            }
        }

        private static void ShowSubaruOverlay(OverlayRenderer overlay, List<OcrMatch> matches, OcrMatch chosen, Point point)
        {
            List<OcrFieldView> fields = new List<OcrFieldView>();
            foreach (OcrMatch match in matches)
            {
                string label = Object.ReferenceEquals(match, chosen) ? "斯巴鲁-选中" : "斯巴鲁";
                fields.Add(new OcrFieldView(match.Rect, label, match.Confidence));
            }

            OverlayDetails details = new OverlayDetails();
            details.Mode = "买车前置步进";
            details.Stage = "OCR 找制造商";
            details.Status = "已识别斯巴鲁候选 " + matches.Count + " 个";
            details.NextAction = "鼠标将移动到 (" + point.X + "," + point.Y + ") 并点击";
            details.Ocr = "斯巴鲁 matches=" + matches.Count;
            details.Target = "选择坐标: x=" + point.X + ", y=" + point.Y;
            details.SkillPoints = "按 ` 逐步继续 / Space+C 退出";
            List<OverlayPointView> points = new List<OverlayPointView>();
            points.Add(new OverlayPointView(point, "MOVE " + point.X + "," + point.Y, Color.FromArgb(255, 40, 220), 7));
            overlay.Update(details, new List<CellView>(), fields, points);
        }

        private static List<OcrMatch> FindConfiguredCjkTextMatches(Config config, OcrReader ocr, OcrSnapshot snapshot, string text)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, text);
            if (matches.Count > 0) return matches;
            return ocr.FindCjkFuzzy(
                snapshot,
                text,
                Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength));
        }

        private static OcrMatch ChooseUiTextMatch(List<OcrMatch> matches, string text)
        {
            string needle = NormalizeUiText(text);
            List<OcrMatch> exact = matches
                .Where(m => NormalizeUiText(m.Text) == needle)
                .OrderBy(m => m.Rect.Left * FH6AutomationConstants.Ranking.LeftFirstWeight + m.Rect.Top)
                .ToList();
            if (exact.Count > 0) return exact.First();

            return matches
                .OrderBy(m => NormalizeUiText(m.Text).Length)
                .ThenBy(m => m.Rect.Width * m.Rect.Height)
                .ThenBy(m => m.Rect.Left * FH6AutomationConstants.Ranking.LeftFirstWeight + m.Rect.Top)
                .First();
        }

        private static string NormalizeUiText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch)) continue;
                sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        private static void MoveMouseToScreenBottomRight(InputController input, ScreenCapture capture, string reason)
        {
            Rectangle bounds = capture.GetBounds();
            int x = Math.Max(bounds.Left, bounds.Right - 2);
            int y = Math.Max(bounds.Top, bounds.Bottom - 2);
            Console.WriteLine("[INPUT] " + reason + " at bottom right " + x + "," + y);
            input.MoveTo(x, y);
        }

        private static void MoveMouseToScreenCenter(InputController input, ScreenCapture capture, string reason)
        {
            Rectangle bounds = capture.GetBounds();
            int x = bounds.Left + bounds.Width / 2;
            int y = bounds.Top + bounds.Height / 2;
            Console.WriteLine("[INPUT] " + reason + " at screen center " + x + "," + y);
            input.MoveTo(x, y);
        }

        private static void EnableDpiAwareness()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
