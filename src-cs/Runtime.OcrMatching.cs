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
        private List<OcrMatch> FindTargetVehicleMatches(OcrSnapshot snapshot)
        {
            OcrSnapshot english = OcrLanguageFilter.English(snapshot);
            List<OcrMatch> cellMatches = FindTargetVehicleGridCellMatches(english);
            if (cellMatches.Count > 0) return cellMatches;

            List<OcrMatch> matches = FindTargetVehicleCompactWordMatches(english);
            matches.AddRange(ocr.Find(english, config.TargetVehicleText));
            matches.AddRange(FindLatinContainsMatches(english, config.TargetVehicleText));
            matches.AddRange(ocr.FindLatinFuzzy(english, config.TargetVehicleText, FH6AutomationConstants.Ocr.TargetVehicleLatinFuzzyDistance));
            return OcrMatchFilter.FilterUiTextMatches(matches, config.TargetVehicleText);
        }

        private List<OcrMatch> FindTargetVehicleGridCellMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            if (snapshot == null || snapshot.Words == null || !grid.Ready) return result;

            Dictionary<CellKey, List<OcrMatch>> wordsByCell = new Dictionary<CellKey, List<OcrMatch>>();
            foreach (OcrMatch word in snapshot.Words)
            {
                if (word == null || string.IsNullOrWhiteSpace(word.Text)) continue;
                CellKey cell;
                if (!cellMapper.TryMapName(word, out cell)) continue;

                List<OcrMatch> list;
                if (!wordsByCell.TryGetValue(cell, out list))
                {
                    list = new List<OcrMatch>();
                    wordsByCell[cell] = list;
                }
                list.Add(word);
            }

            foreach (KeyValuePair<CellKey, List<OcrMatch>> pair in wordsByCell)
            {
                List<OcrMatch> ordered = pair.Value
                    .OrderBy(m => m.Rect.Top)
                    .ThenBy(m => m.Rect.Left)
                    .ToList();

                OcrMatch impreza = null;
                OcrMatch twentyTwoB = null;
                foreach (OcrMatch word in ordered)
                {
                    string normalized = NormalizeLatinLoose(word.Text);
                    if (impreza == null && LooksLikeImpreza(normalized))
                    {
                        impreza = word;
                        continue;
                    }

                    if (twentyTwoB == null && LooksLike22B(normalized))
                    {
                        twentyTwoB = word;
                    }
                }

                if (impreza == null || twentyTwoB == null) continue;
                result.Add(MergeOcrMatches(new[] { impreza, twentyTwoB }));
            }

            return OcrMatchFilter.DeduplicateByRect(result)
                .OrderBy(match => OcrMatchFilter.LeftTopRank(match))
                .ToList();
        }

        private List<OcrMatch> FindTargetVehicleCompactWordMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            if (snapshot == null || snapshot.WordLines == null) return result;

            foreach (List<OcrMatch> line in snapshot.WordLines)
            {
                if (line == null || line.Count == 0) continue;
                List<OcrMatch> ordered = line.OrderBy(m => m.Rect.Left).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    string current = NormalizeLatinLoose(ordered[i].Text);
                    if (current != "IMPREZA") continue;

                    for (int j = i + 1; j < ordered.Count && j <= i + 5; j++)
                    {
                        string next = NormalizeLatinLoose(ordered[j].Text);
                        if (next == "IMPREZA") break;
                        if (!LooksLike22B(next)) continue;

                        result.Add(MergeOcrMatches(new[] { ordered[i], ordered[j] }));
                        break;
                    }
                }
            }

            return OcrMatchFilter.DeduplicateByRect(result);
        }

        private List<OcrMatch> FindNewBadgeMatches(OcrSnapshot snapshot)
        {
            OcrSnapshot chinese = OcrLanguageFilter.Chinese(snapshot);
            List<OcrMatch> matches = ocr.Find(chinese, config.NewBadgeText);
            if (matches.Count > 0) return OcrMatchFilter.FilterUiTextMatches(matches, config.NewBadgeText);
            return OcrMatchFilter.FilterUiTextMatches(
                ocr.FindCjkFuzzy(chinese, config.NewBadgeText, FH6AutomationConstants.Ocr.NewBadgeCjkMinCommonChars, FH6AutomationConstants.Ocr.NewBadgeCjkMaxNormalizedLength),
                config.NewBadgeText);
        }

        private List<OcrMatch> FindManufacturerMatches(OcrSnapshot snapshot)
        {
            return FindConfiguredCjkTextMatches(snapshot, config.ManufacturerText);
        }

        private List<OcrMatch> FindMyHorizonMatches(OcrSnapshot snapshot)
        {
            OcrSnapshot chinese = OcrLanguageFilter.Chinese(snapshot);
            List<OcrMatch> matches = ocr.Find(chinese, config.MyHorizonText);
            if (matches.Count > 0) return OcrMatchFilter.FilterUiTextMatches(matches, config.MyHorizonText);
            return OcrMatchFilter.FilterUiTextMatches(
                ocr.FindCjkFuzzy(chinese, config.MyHorizonText, FH6AutomationConstants.Ocr.MyHorizonCjkMinCommonChars, FH6AutomationConstants.Ocr.MyHorizonCjkMaxNormalizedLength),
                config.MyHorizonText);
        }

        private List<OcrMatch> FindDeleteMarkerMatches(OcrSnapshot snapshot)
        {
            return OcrMatchFilter.FilterUiTextMatches(ocr.Find(snapshot, config.DeleteMarkerText), config.DeleteMarkerText);
        }

        private List<OcrMatch> FindDriveMarkerMatches(OcrSnapshot snapshot)
        {
            return OcrMatchFilter.FilterUiTextMatches(ocr.Find(snapshot, config.DriveMarkerText), config.DriveMarkerText);
        }

        private List<OcrMatch> FindConfiguredCjkTextMatches(OcrSnapshot snapshot, string text)
        {
            OcrSnapshot chinese = OcrLanguageFilter.Chinese(snapshot);
            List<OcrMatch> matches = ocr.Find(chinese, text);
            matches.AddRange(FindCjkLooseMatches(
                chinese,
                text,
                Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength)));
            matches.AddRange(ocr.FindCjkFuzzy(
                chinese,
                text,
                Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength)));
            return OcrMatchFilter.FilterUiTextMatches(matches, text);
        }

        private OcrMatch ChooseUiTextMatch(List<OcrMatch> matches, string text)
        {
            return OcrMatchFilter.ChooseUiTextMatch(matches, text);
        }

        private List<OcrMatch> FindLatinContainsMatches(OcrSnapshot snapshot, string text)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            string needle = NormalizeLatinLoose(text);
            if (snapshot == null || needle.Length == 0) return result;

            foreach (OcrMatch match in SnapshotCandidates(snapshot))
            {
                string haystack = NormalizeLatinLoose(match.Text);
                if (haystack.Contains(needle)) result.Add(match);
            }

            return result;
        }

        private List<OcrMatch> FindCjkLooseMatches(OcrSnapshot snapshot, string text, int minCommonChars, int maxNormalizedLength)
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

        private IEnumerable<OcrMatch> SnapshotCandidates(OcrSnapshot snapshot)
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

        private static OcrMatch MergeOcrMatches(IEnumerable<OcrMatch> matches)
        {
            List<OcrMatch> list = matches.Where(m => m != null).ToList();
            if (list.Count == 0) return null;
            float left = list.Min(m => m.Rect.Left);
            float top = list.Min(m => m.Rect.Top);
            float right = list.Max(m => m.Rect.Right);
            float bottom = list.Max(m => m.Rect.Bottom);
            double confidence = list.Where(m => m.Confidence >= 0).Select(m => m.Confidence).DefaultIfEmpty(-1).Average();
            return new OcrMatch(
                string.Join(" ", list.Select(m => m.Text).ToArray()),
                new RectangleF(left, top, right - left, bottom - top),
                confidence);
        }

        private static bool LooksLike22B(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return false;
            if (normalized.StartsWith("22B", StringComparison.OrdinalIgnoreCase)) return true;
            return LevenshteinDistance(normalized, "22BSTI") <= 1;
        }

        private static bool LooksLikeImpreza(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return false;
            return normalized == "IMPREZA" ||
                normalized == "MPREZA" ||
                normalized == "PREZA" ||
                normalized.EndsWith("IMPREZA", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLatinLoose(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
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

        private static int LevenshteinDistance(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int delete = dp[i - 1, j] + 1;
                    int insert = dp[i, j - 1] + 1;
                    int replace = dp[i - 1, j - 1] + cost;
                    dp[i, j] = Math.Min(Math.Min(delete, insert), replace);
                }
            }

            return dp[a.Length, b.Length];
        }
    }
}
