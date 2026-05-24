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
    internal sealed partial class Runtime
    {
        private void Fail(OcrSnapshot snapshot, string reason)
        {
            failures++;
            WriteOcrDump(snapshot, reason);
            WriteFailureFullScreenCapture(reason);
            throw new InvalidOperationException(reason + "，OCR文本：" + DescribeOcrText(snapshot, 80));
        }

        private string DescribeOcrText(OcrSnapshot snapshot, int maxItems)
        {
            if (snapshot == null || snapshot.Words == null || snapshot.Words.Count == 0) return "无";
            return string.Join(" ", snapshot.Words.Select(w => w.Text).Take(maxItems).ToArray());
        }

        private static double SortLeftTop(OcrMatch match)
        {
            return match.Rect.Left * FH6AutomationConstants.Ranking.LeftFirstWeight + match.Rect.Top;
        }

        private void WriteOcrDump(OcrSnapshot snapshot, string reason)
        {
            try
            {
                Directory.CreateDirectory(debugDir);
                string safeReason = Regex.Replace(reason ?? "ocr", @"[^A-Za-z0-9_-]", "_");
                string path = Path.Combine(debugDir, "ocr-last-" + safeReason + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("reason=" + reason);
                sb.AppendLine("target_vehicle=" + config.TargetVehicleText);
                sb.AppendLine("new_badge=" + config.NewBadgeText);
                if (snapshot == null || snapshot.Words == null || snapshot.Words.Count == 0)
                {
                    sb.AppendLine("OCR: 无");
                }
                else
                {
                    sb.AppendLine("OCR words:");
                    foreach (OcrMatch word in snapshot.Words.OrderBy(w => w.Rect.Top).ThenBy(w => w.Rect.Left).Take(500))
                    {
                        sb.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "[{0:0},{1:0},{2:0},{3:0}] conf={4:0.000} text={5}",
                            word.Rect.Left,
                            word.Rect.Top,
                            word.Rect.Width,
                            word.Rect.Height,
                            word.Confidence,
                            word.Text));
                    }
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private void WriteOcrException(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(debugDir);
                string path = Path.Combine(debugDir, "ocr-exception-last.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("status=" + status);
                sb.AppendLine("next=" + nextAction);
                sb.AppendLine(ex.ToString());
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private void WriteFailureFullScreenCapture(string reason)
        {
            try
            {
                Directory.CreateDirectory(debugDir);
                string safeReason = Regex.Replace(reason ?? "failure", @"[^A-Za-z0-9_-]", "_");
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                string imagePath = Path.Combine(debugDir, "failure-fullscreen-" + safeReason + "-" + stamp + ".png");
                string metaPath = Path.Combine(debugDir, "failure-fullscreen-" + safeReason + "-" + stamp + ".txt");

                overlay.HideForCapture(config.OverlayHideBeforeCaptureMs);
                Screenshot shot = capture.Grab();
                shot.Image.Save(imagePath, ImageFormat.Png);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.AppendLine("reason=" + reason);
                sb.AppendLine("status=" + status);
                sb.AppendLine("next=" + nextAction);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_origin=[{0},{1}]", shot.Left, shot.Top));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_size=[{0},{1}]", shot.Image.Width, shot.Image.Height));
                sb.AppendLine("image=" + Path.GetFileName(imagePath));
                File.WriteAllText(metaPath, sb.ToString(), Encoding.UTF8);
                shot.Image.Dispose();
            }
            catch
            {
            }
            finally
            {
                overlay.ShowOverlay();
            }
        }

        private void ResetDebugScreenshots()
        {
            try
            {
                if (Directory.Exists(debugScreenshotDir)) Directory.Delete(debugScreenshotDir, true);
                Directory.CreateDirectory(debugScreenshotDir);
                Console.WriteLine("[DEBUG] 已清理调试截图目录 " + debugScreenshotDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DEBUG] 清理调试截图失败：" + ex.Message);
            }
        }

        private string SaveDebugCapture(Screenshot shot, int psm, Rectangle requestedRegion)
        {
            if (!stepDebug || shot == null || shot.Image == null) return null;
            try
            {
                Directory.CreateDirectory(debugScreenshotDir);
                debugScreenshotCounter++;
                string label = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0000}-{1}",
                    debugScreenshotCounter,
                    SafeFilePart(status));
                string imagePath = Path.Combine(debugScreenshotDir, label + "-capture.png");
                shot.Image.Save(imagePath, ImageFormat.Png);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("status=" + status);
                sb.AppendLine("next=" + nextAction);
                sb.AppendLine("psm=" + psm);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "requested_region=[{0},{1},{2},{3}]", requestedRegion.Left, requestedRegion.Top, requestedRegion.Width, requestedRegion.Height));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_origin=[{0},{1}]", shot.Left, shot.Top));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_size=[{0},{1}]", shot.Image.Width, shot.Image.Height));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "ocr_scale={0:0.###}", config.OcrScale));
                sb.AppendLine("ocr_input=" + label + "-ocr-input.png");
                sb.AppendLine("ocr_response=" + label + "-ocr-response.json");
                File.WriteAllText(Path.Combine(debugScreenshotDir, label + "-meta.txt"), sb.ToString(), Encoding.UTF8);
                return label;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DEBUG] 保存调试截图失败：" + ex.Message);
                return null;
            }
        }

        private static string SafeFilePart(string value)
        {
            string safe = Regex.Replace(value ?? "capture", @"[^A-Za-z0-9_-]+", "_");
            safe = safe.Trim('_');
            return safe.Length == 0 ? "capture" : safe;
        }
    }
}
