using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class FatalStopReporter
    {
        public static void Report(Exception ex, Config config, CliOptions options)
        {
            string category = Classify(ex);
            string debugDir = ResolveDebugDir(config);
            Directory.CreateDirectory(debugDir);

            string summary = BuildSummary(category, ex, debugDir);
            string summaryPath = Path.Combine(debugDir, "fatal-stop-last.txt");
            File.WriteAllText(summaryPath, summary, Encoding.UTF8);

            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine("自动流程已停止");
            Console.WriteLine("类型：" + category);
            Console.WriteLine("原因：" + TrimForDisplay(ex == null ? "" : ex.Message, 1200));
            Console.WriteLine("完整日志：" + Path.Combine(debugDir, "last-error.txt"));
            Console.WriteLine("停止摘要：" + summaryPath);
            Console.WriteLine("============================================================");
            Console.WriteLine();

            if (ShouldHoldVisibleWindow(options))
            {
                TryShowMessageBox(category, ex, debugDir);
                Console.WriteLine("按 Enter 关闭窗口。");
                try { Console.ReadLine(); } catch { }
            }
        }

        public static string Classify(Exception ex)
        {
            string text = ex == null ? "" : ex.ToString();
            string message = ex == null ? "" : (ex.Message ?? "");
            if (text.IndexOf("UI缓存保险OCR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("缓存保险OCR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "校验失败";
            }
            if (message.IndexOf("OCR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("OcrReader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("MediaOCR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("PaddleOCR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("RapidOCR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Tesseract", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "OCR崩溃或识别失败";
            }
            return "程序错误";
        }

        private static string BuildSummary(string category, Exception ex, string debugDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("category=" + category);
            sb.AppendLine("debug_dir=" + debugDir);
            sb.AppendLine("last_error=" + Path.Combine(debugDir, "last-error.txt"));
            sb.AppendLine();
            sb.AppendLine("message=");
            sb.AppendLine(ex == null ? "" : ex.Message);
            sb.AppendLine();
            sb.AppendLine("exception=");
            sb.AppendLine(ex == null ? "" : ex.ToString());
            return sb.ToString();
        }

        private static string ResolveDebugDir(Config config)
        {
            try
            {
                if (config != null) return config.ResolvePath(config.DebugDir);
            }
            catch
            {
            }
            return FH6FailureLog.ResolveDebugDir();
        }

        private static bool ShouldHoldVisibleWindow(CliOptions options)
        {
            if (!Environment.UserInteractive) return false;
            if (Console.IsInputRedirected) return false;
            if (options != null && options.ReuseVehicleListState) return false;
            return true;
        }

        private static void TryShowMessageBox(string category, Exception ex, string debugDir)
        {
            try
            {
                string message =
                    "自动流程已停止。\r\n\r\n" +
                    "类型：" + category + "\r\n" +
                    "原因：" + TrimForDisplay(ex == null ? "" : ex.Message, 800) + "\r\n\r\n" +
                    "完整日志：\r\n" + Path.Combine(debugDir, "last-error.txt") + "\r\n\r\n" +
                    "停止摘要：\r\n" + Path.Combine(debugDir, "fatal-stop-last.txt");
                MessageBox.Show(message, "地平线6妙妙工具 - 已停止", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private static string TrimForDisplay(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxChars) return value;
            return value.Substring(0, maxChars) + "...";
        }
    }
}
