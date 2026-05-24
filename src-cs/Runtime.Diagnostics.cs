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
                sb.AppendLine("status=" + status);
                sb.AppendLine("stage=" + bigStage);
                sb.AppendLine("next=" + nextAction);
                sb.AppendLine("target_vehicle=" + config.TargetVehicleText);
                sb.AppendLine("new_badge=" + config.NewBadgeText);
                sb.AppendLine("manufacturer=" + config.ManufacturerText);
                if (snapshot != null)
                {
                    sb.AppendLine("engine=" + snapshot.EngineName);
                    if (snapshot.Screenshot != null && snapshot.Screenshot.Image != null)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_origin=[{0},{1}]", snapshot.Screenshot.Left, snapshot.Screenshot.Top));
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_size=[{0},{1}]", snapshot.Screenshot.Image.Width, snapshot.Screenshot.Image.Height));
                    }
                    sb.AppendLine("word_count=" + (snapshot.Words == null ? 0 : snapshot.Words.Count).ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("line_count=" + (snapshot.Lines == null ? 0 : snapshot.Lines.Count).ToString(CultureInfo.InvariantCulture));
                    AppendSection(sb, "engine_diagnostics", snapshot.EngineDiagnostics, 6000);
                    AppendSection(sb, "ocr_stderr", snapshot.ErrorOutput, 12000);
                    AppendSection(sb, "raw_ocr_response", snapshot.RawResponse, 120000);
                }
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

        private void WriteOcrSnapshotCapture(OcrSnapshot snapshot, string reason)
        {
            if (snapshot == null || snapshot.Screenshot == null || snapshot.Screenshot.Image == null) return;
            try
            {
                Directory.CreateDirectory(debugDir);
                string safeReason = Regex.Replace(reason ?? "ocr-capture", @"[^A-Za-z0-9_-]", "_");
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                string imagePath = Path.Combine(debugDir, "ocr-capture-" + safeReason + "-" + stamp + ".png");
                string metaPath = Path.Combine(debugDir, "ocr-capture-" + safeReason + "-" + stamp + ".txt");
                snapshot.Screenshot.Image.Save(imagePath, ImageFormat.Png);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.AppendLine("reason=" + reason);
                sb.AppendLine("status=" + status);
                sb.AppendLine("next=" + nextAction);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_origin=[{0},{1}]", snapshot.Screenshot.Left, snapshot.Screenshot.Top));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_size=[{0},{1}]", snapshot.Screenshot.Image.Width, snapshot.Screenshot.Image.Height));
                sb.AppendLine("image=" + Path.GetFileName(imagePath));
                sb.AppendLine("ocr_text=" + DescribeOcrText(snapshot, 160));
                if (snapshot != null)
                {
                    sb.AppendLine("engine=" + snapshot.EngineName);
                    AppendSection(sb, "engine_diagnostics", snapshot.EngineDiagnostics, 6000);
                    AppendSection(sb, "ocr_stderr", snapshot.ErrorOutput, 12000);
                    AppendSection(sb, "raw_ocr_response", snapshot.RawResponse, 120000);
                }
                File.WriteAllText(metaPath, sb.ToString(), Encoding.UTF8);
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

        private void WriteTableBuildObservationTrace(VehicleGridObservation observation, int ignoredLeadingColumns)
        {
            if (observation == null) return;
            try
            {
                Directory.CreateDirectory(debugDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                string baseName = string.Format(
                    CultureInfo.InvariantCulture,
                    "table-build-{0:000}-{1}",
                    observation.ScrollIndex,
                    stamp);
                string imageName = "";
                OcrSnapshot snapshot = observation.Snapshot;
                if (snapshot != null && snapshot.Screenshot != null && snapshot.Screenshot.Image != null)
                {
                    imageName = baseName + ".png";
                    snapshot.Screenshot.Image.Save(Path.Combine(debugDir, imageName), ImageFormat.Png);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.AppendLine("reason=table-build-observation");
                sb.AppendLine("status=" + status);
                sb.AppendLine("stage=" + bigStage);
                sb.AppendLine("next=" + nextAction);
                sb.AppendLine("scroll_index=" + observation.ScrollIndex.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("ignored_leading_columns=" + ignoredLeadingColumns.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("visible_columns=" + grid.VisibleColumns.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("rows=" + grid.Rows.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("current_offset=" + vehicleList.CurrentOffset.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(imageName)) sb.AppendLine("image=" + imageName);
                if (snapshot != null)
                {
                    sb.AppendLine("engine=" + snapshot.EngineName);
                    if (snapshot.Screenshot != null && snapshot.Screenshot.Image != null)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_origin=[{0},{1}]", snapshot.Screenshot.Left, snapshot.Screenshot.Top));
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "capture_size=[{0},{1}]", snapshot.Screenshot.Image.Width, snapshot.Screenshot.Image.Height));
                    }
                    sb.AppendLine("word_count=" + (snapshot.Words == null ? 0 : snapshot.Words.Count).ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("line_count=" + (snapshot.Lines == null ? 0 : snapshot.Lines.Count).ToString(CultureInfo.InvariantCulture));
                    AppendSection(sb, "engine_diagnostics", snapshot.EngineDiagnostics, 6000);
                    AppendSection(sb, "ocr_stderr", snapshot.ErrorOutput, 12000);
                }

                sb.AppendLine("target_cells=" + FormatCellSet(observation.TargetCells));
                sb.AppendLine("valid_new_cells=" + FormatCellSet(observation.ValidNewCells));
                sb.AppendLine("invalid_new_cells=" + FormatCellSet(observation.InvalidNewCells));
                sb.AppendLine("deletable_cells=" + FormatCellSet(observation.DeletableCells));
                sb.AppendLine("drive_cells=" + FormatCellSet(observation.DriveCells));
                sb.AppendLine("manufacturer_cells=" + FormatCellSet(observation.ManufacturerCells));
                sb.AppendLine("blank_cells=" + FormatCellSet(observation.BlankCells));
                sb.AppendLine("performance_scores=" + FormatScoreMap(observation.PerformanceScores));
                AppendMatches(sb, "target_matches", observation.TargetMatches);
                AppendMatches(sb, "new_badge_matches", observation.NewBadgeMatches);
                AppendMatches(sb, "manufacturer_matches", observation.ManufacturerMatches);
                AppendMatches(sb, "raw_600_matches_overlay_only", observation.DeleteMarkerMatches);
                AppendMatches(sb, "raw_900_matches_overlay_only", observation.DriveMarkerMatches);
                if (snapshot != null) AppendSection(sb, "raw_ocr_response", snapshot.RawResponse, 120000);

                string text = sb.ToString();
                File.WriteAllText(Path.Combine(debugDir, baseName + ".txt"), text, Encoding.UTF8);
                File.WriteAllText(Path.Combine(debugDir, "table-build-last.txt"), text, Encoding.UTF8);
            }
            catch
            {
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

        private string FormatCellSet(IEnumerable<CellKey> cells)
        {
            if (cells == null) return "";
            return string.Join(
                " ",
                cells.OrderBy(c => c.Col).ThenBy(c => c.Row).Select(FormatCellMapping).ToArray());
        }

        private string FormatScoreMap(Dictionary<CellKey, int> scores)
        {
            if (scores == null || scores.Count == 0) return "";
            return string.Join(
                " ",
                scores.OrderBy(p => p.Key.Col).ThenBy(p => p.Key.Row)
                    .Select(p => FormatCellMapping(p.Key) + "=" + p.Value.ToString(CultureInfo.InvariantCulture))
                    .ToArray());
        }

        private string FormatCellMapping(CellKey local)
        {
            CellKey global = vehicleList.ToGlobal(local);
            return string.Format(
                CultureInfo.InvariantCulture,
                "L[c{0}r{1}]->G[c{2}r{3}]",
                local.Col,
                local.Row,
                global.Col,
                global.Row);
        }

        private void AppendMatches(StringBuilder sb, string title, IEnumerable<OcrMatch> matches)
        {
            sb.AppendLine("---- " + title + " ----");
            if (matches == null)
            {
                sb.AppendLine("(empty)");
                return;
            }

            int count = 0;
            foreach (OcrMatch match in matches.OrderBy(m => m.Rect.Top).ThenBy(m => m.Rect.Left).Take(200))
            {
                count++;
                Point center = match.RectCenter();
                CellKey local;
                string cellText = grid.MapPoint(center.X, center.Y, out local)
                    ? FormatCellMapping(local)
                    : "outside-grid";
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0:0},{1:0},{2:0},{3:0}] center=[{4},{5}] conf={6:0.000} cell={7} text={8}",
                    match.Rect.Left,
                    match.Rect.Top,
                    match.Rect.Width,
                    match.Rect.Height,
                    center.X,
                    center.Y,
                    match.Confidence,
                    cellText,
                    match.Text));
            }
            if (count == 0) sb.AppendLine("(empty)");
        }

        private static void AppendSection(StringBuilder sb, string title, string body, int maxChars)
        {
            sb.AppendLine("---- " + title + " ----");
            if (string.IsNullOrEmpty(body))
            {
                sb.AppendLine("(empty)");
                return;
            }

            if (body.Length <= maxChars)
            {
                sb.AppendLine(body);
                return;
            }

            sb.AppendLine(body.Substring(0, maxChars));
            sb.AppendLine("... truncated, total_chars=" + body.Length.ToString(CultureInfo.InvariantCulture));
        }
    }
}
