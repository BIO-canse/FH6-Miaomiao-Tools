using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace FH6SkillPointOcr
{
    internal static class OcrLanguageFilter
    {
        public static OcrSnapshot Chinese(OcrSnapshot snapshot)
        {
            return Filter(snapshot, IsChineseOrUnknown);
        }

        public static OcrSnapshot English(OcrSnapshot snapshot)
        {
            return Filter(snapshot, IsEnglishOrUnknown);
        }

        public static bool IsChineseOrUnknown(OcrMatch match)
        {
            string language = NormalizeLanguage(match);
            return language.Length == 0 || language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsEnglishOrUnknown(OcrMatch match)
        {
            string language = NormalizeLanguage(match);
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return true;
            return language.Length == 0 && HasLatinOrDigit(match == null ? "" : match.Text);
        }

        private static OcrSnapshot Filter(OcrSnapshot snapshot, Func<OcrMatch, bool> include)
        {
            if (snapshot == null) return null;

            List<List<OcrMatch>> wordLines = new List<List<OcrMatch>>();
            if (snapshot.WordLines != null)
            {
                foreach (List<OcrMatch> line in snapshot.WordLines)
                {
                    List<OcrMatch> filtered = line.Where(include).ToList();
                    if (filtered.Count > 0) wordLines.Add(filtered);
                }
            }

            List<OcrMatch> words = snapshot.Words == null
                ? new List<OcrMatch>()
                : snapshot.Words.Where(include).ToList();
            List<OcrMatch> lines = wordLines.Select(MergeWords).ToList();

            OcrSnapshot filteredSnapshot = new OcrSnapshot(snapshot.Screenshot, words, wordLines, lines);
            filteredSnapshot.EngineName = snapshot.EngineName;
            filteredSnapshot.RawResponse = snapshot.RawResponse;
            filteredSnapshot.ErrorOutput = snapshot.ErrorOutput;
            filteredSnapshot.EngineDiagnostics = snapshot.EngineDiagnostics;
            filteredSnapshot.SkippedLeadingGridColumns = snapshot.SkippedLeadingGridColumns;
            return filteredSnapshot;
        }

        private static OcrMatch MergeWords(List<OcrMatch> words)
        {
            float left = words.Min(w => w.Rect.Left);
            float top = words.Min(w => w.Rect.Top);
            float right = words.Max(w => w.Rect.Right);
            float bottom = words.Max(w => w.Rect.Bottom);
            double confidence = words.Where(w => w.Confidence >= 0).Select(w => w.Confidence).DefaultIfEmpty(-1).Average();
            return new OcrMatch(
                string.Join(" ", words.Select(w => w.Text).ToArray()),
                new RectangleF(left, top, right - left, bottom - top),
                confidence,
                MergeLanguage(words));
        }

        private static string MergeLanguage(List<OcrMatch> words)
        {
            List<string> languages = words
                .Select(w => NormalizeLanguage(w))
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return languages.Count == 1 ? languages[0] : "";
        }

        private static string NormalizeLanguage(OcrMatch match)
        {
            return match == null || string.IsNullOrWhiteSpace(match.Language)
                ? ""
                : match.Language.Trim();
        }

        private static bool HasLatinOrDigit(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char ch in text)
            {
                if ((ch >= 'A' && ch <= 'Z') ||
                    (ch >= 'a' && ch <= 'z') ||
                    char.IsDigit(ch))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
