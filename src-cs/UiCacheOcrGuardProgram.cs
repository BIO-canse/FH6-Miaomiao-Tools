using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class UiCacheOcrGuardProgram
    {
        private static int Main(string[] args)
        {
            EnableDpiAwareness();
            FH6FailureLog.InstallGlobalHandlers("FH6UiCacheOcrGuard");

            try
            {
                GuardOptions options = GuardOptions.Parse(args);
                if (options.ShowHelp)
                {
                    Console.WriteLine("FH6UiCacheOcrGuard.exe --config path --text text --label label --result-file path --captured-file path");
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(options.Text)) throw new ArgumentException("缺少 --text。");
                if (string.IsNullOrWhiteSpace(options.ResultFile)) throw new ArgumentException("缺少 --result-file。");

                Config config = Config.Load(options.ConfigPath);
                string debugDir = Path.Combine(config.ResolvePath(config.DebugDir), "ui-cache-guard");
                Directory.CreateDirectory(debugDir);

                ScreenCapture capture = new ScreenCapture(config.MonitorIndex);
                capture.EnableWindowBinding("ui cache ocr guard");

                Screenshot shot = capture.Grab();
                WriteCapturedMarker(options.CapturedFile, shot);

                OcrSnapshot snapshot = null;
                using (OcrReader ocr = new OcrReader(config, debugDir))
                {
                    string debugLabel = SafeFilePart("ui-cache-" + options.Label);
                    snapshot = ocr.Read(shot, config.OcrPsm, debugLabel);
                    List<OcrMatch> matches = FindUiTextMatches(ocr, snapshot, options.Text);
                    if (matches.Count > 0)
                    {
                        WriteResult(options.ResultFile, "ok", options, snapshot, matches, null);
                        return 0;
                    }

                    SaveFailureCapture(debugDir, options, shot);
                    WriteResult(options.ResultFile, "fail", options, snapshot, matches, "OCR 未找到缓存点击对应文字：" + options.Text);
                    return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                FH6FailureLog.Write("FH6UiCacheOcrGuard", ex);
                try
                {
                    GuardOptions options = GuardOptions.Parse(args);
                    if (!string.IsNullOrWhiteSpace(options.ResultFile))
                    {
                        WriteResult(options.ResultFile, "error", options, null, new List<OcrMatch>(), ex.ToString());
                    }
                }
                catch
                {
                }
                return 1;
            }
        }

        private static List<OcrMatch> FindUiTextMatches(OcrReader ocr, OcrSnapshot snapshot, string text)
        {
            OcrSnapshot searchSnapshot = HasCjk(text) ? OcrLanguageFilter.Chinese(snapshot) : snapshot;
            List<OcrMatch> matches = ocr.Find(searchSnapshot, text);
            if (HasCjk(text))
            {
                matches.AddRange(FindCjkLooseMatches(
                    searchSnapshot,
                    text,
                    Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                    Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength)));
                matches.AddRange(ocr.FindCjkFuzzy(
                    searchSnapshot,
                    text,
                    Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                    Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength)));
            }
            return OcrMatchFilter.FilterUiTextMatches(matches, text);
        }

        private static List<OcrMatch> FindCjkLooseMatches(OcrSnapshot snapshot, string text, int minCommonChars, int maxNormalizedLength)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            string needle = NormalizeCjkLoose(text);
            if (snapshot == null || needle.Length == 0) return result;

            foreach (OcrMatch match in SnapshotCandidates(snapshot))
            {
                string haystack = NormalizeCjkLoose(match.Text);
                if (haystack.Length == 0 || haystack.Length > maxNormalizedLength) continue;
                if (haystack.Contains(needle) ||
                    (needle.Contains(haystack) && haystack.Length >= minCommonChars) ||
                    CommonCharCountLoose(needle, haystack) >= minCommonChars)
                {
                    result.Add(match);
                }
            }

            return result;
        }

        private static IEnumerable<OcrMatch> SnapshotCandidates(OcrSnapshot snapshot)
        {
            if (snapshot == null) yield break;
            if (snapshot.Words != null)
            {
                foreach (OcrMatch match in snapshot.Words) yield return match;
            }
            if (snapshot.Lines != null)
            {
                foreach (OcrMatch match in snapshot.Lines) yield return match;
            }
        }

        private static void WriteCapturedMarker(string path, Screenshot shot)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string text = string.Format(
                CultureInfo.InvariantCulture,
                "time={0:yyyy-MM-dd HH:mm:ss.fff}\r\ncapture_origin=[{1},{2}]\r\ncapture_size=[{3},{4}]\r\n",
                DateTime.Now,
                shot.Left,
                shot.Top,
                shot.Image.Width,
                shot.Image.Height);
            File.WriteAllText(path, text, Encoding.UTF8);
        }

        private static void SaveFailureCapture(string debugDir, GuardOptions options, Screenshot shot)
        {
            if (shot == null || shot.Image == null) return;
            string file = Path.Combine(
                debugDir,
                "ui-cache-guard-failure-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + SafeFilePart(options.Label) + ".png");
            shot.Image.Save(file, ImageFormat.Png);
        }

        private static void WriteResult(string path, string status, GuardOptions options, OcrSnapshot snapshot, List<OcrMatch> matches, string error)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("status=" + status);
            sb.AppendLine("label=" + options.Label);
            sb.AppendLine("text=" + options.Text);
            if (!string.IsNullOrWhiteSpace(error)) sb.AppendLine("error=" + error);
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
                sb.AppendLine("ocr_text=" + DescribeOcrText(snapshot, 160));
            }
            sb.AppendLine("match_count=" + (matches == null ? 0 : matches.Count).ToString(CultureInfo.InvariantCulture));
            if (matches != null)
            {
                foreach (OcrMatch match in matches.OrderBy(m => m.Rect.Top).ThenBy(m => m.Rect.Left).Take(80))
                {
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "match text={0} rect=[{1:0},{2:0},{3:0},{4:0}] conf={5:0.000}",
                        match.Text,
                        match.Rect.Left,
                        match.Rect.Top,
                        match.Rect.Width,
                        match.Rect.Height,
                        match.Confidence));
                }
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string DescribeOcrText(OcrSnapshot snapshot, int maxItems)
        {
            if (snapshot == null || snapshot.Words == null || snapshot.Words.Count == 0) return "无";
            return string.Join(" ", snapshot.Words.Select(w => w.Text).Take(maxItems).ToArray());
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

        private static bool HasCjk(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff');
        }

        private static string NormalizeCjkLoose(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (ch >= '\u4e00' && ch <= '\u9fff') sb.Append(ch);
            }
            return sb.ToString();
        }

        private static int CommonCharCountLoose(string a, string b)
        {
            HashSet<char> seen = new HashSet<char>(a.ToCharArray());
            int count = 0;
            foreach (char ch in b)
            {
                if (seen.Remove(ch)) count++;
            }
            return count;
        }

        private static string SafeFilePart(string value)
        {
            string safe = Regex.Replace(value ?? "guard", @"[^A-Za-z0-9_-]+", "_");
            safe = safe.Trim('_');
            return safe.Length == 0 ? "guard" : safe;
        }

        private static void EnableDpiAwareness()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private sealed class GuardOptions
        {
            public string ConfigPath = Path.Combine("config", "default.json");
            public string Text = "";
            public string Label = "";
            public string ResultFile = "";
            public string CapturedFile = "";
            public bool ShowHelp;

            public static GuardOptions Parse(string[] args)
            {
                GuardOptions options = new GuardOptions();
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg == "--help" || arg == "-h") options.ShowHelp = true;
                    else if (arg == "--config" && i + 1 < args.Length) options.ConfigPath = args[++i];
                    else if (arg == "--text" && i + 1 < args.Length) options.Text = args[++i];
                    else if (arg == "--label" && i + 1 < args.Length) options.Label = args[++i];
                    else if (arg == "--result-file" && i + 1 < args.Length) options.ResultFile = args[++i];
                    else if (arg == "--captured-file" && i + 1 < args.Length) options.CapturedFile = args[++i];
                }
                if (string.IsNullOrWhiteSpace(options.Label)) options.Label = options.Text;
                return options;
            }
        }
    }
}
