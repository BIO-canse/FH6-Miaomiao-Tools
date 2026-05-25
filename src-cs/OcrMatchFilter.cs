using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal static class OcrMatchFilter
    {
        public static List<OcrMatch> FilterUiTextMatches(IEnumerable<OcrMatch> matches, string query)
        {
            List<OcrMatch> deduped = DeduplicateByRect(matches);
            string needle = NormalizeUiText(query);
            if (needle.Length == 0 || deduped.Count == 0) return deduped;

            List<ScoredMatch> scored = deduped
                .Select(match => new ScoredMatch(match, NormalizeUiText(match.Text), needle))
                .Where(item => item.Normalized.Length > 0)
                .ToList();
            if (scored.Count == 0) return deduped;

            List<OcrMatch> exact = scored
                .Where(item => item.Kind == MatchKind.Exact)
                .OrderBy(item => LeftTopRank(item.Match))
                .Select(item => item.Match)
                .ToList();
            if (exact.Count > 0) return exact;

            List<ScoredMatch> contains = scored
                .Where(item => item.Kind == MatchKind.ContainsNeedle)
                .ToList();
            if (contains.Count > 0)
            {
                int minExtra = contains.Min(item => item.ExtraLength);
                int allowance = HasCjk(needle) ? 0 : 2;
                return contains
                    .Where(item => item.ExtraLength <= minExtra + allowance)
                    .OrderBy(item => item.ExtraLength)
                    .ThenBy(item => item.Normalized.Length)
                    .ThenBy(item => item.Match.Rect.Width * item.Match.Rect.Height)
                    .ThenBy(item => LeftTopRank(item.Match))
                    .Select(item => item.Match)
                    .ToList();
            }

            List<ScoredMatch> partial = scored
                .Where(item => item.Kind == MatchKind.Partial)
                .ToList();
            if (partial.Count > 0)
            {
                if (HasCjk(needle)) return new List<OcrMatch>();

                int minMissing = partial.Min(item => item.ExtraLength);
                return partial
                    .Where(item => item.ExtraLength <= minMissing)
                    .OrderBy(item => item.ExtraLength)
                    .ThenByDescending(item => item.Normalized.Length)
                    .ThenBy(item => LeftTopRank(item.Match))
                    .Select(item => item.Match)
                    .ToList();
            }

            int minLength = scored.Min(item => item.Normalized.Length);
            int fuzzyAllowance = HasCjk(needle) ? 1 : 4;
            return scored
                .Where(item => item.Normalized.Length <= minLength + fuzzyAllowance)
                .OrderBy(item => item.Normalized.Length)
                .ThenBy(item => item.Match.Rect.Width * item.Match.Rect.Height)
                .ThenBy(item => LeftTopRank(item.Match))
                .Select(item => item.Match)
                .ToList();
        }

        public static OcrMatch ChooseUiTextMatch(IEnumerable<OcrMatch> matches, string query)
        {
            List<OcrMatch> filtered = FilterUiTextMatches(matches, query);
            if (filtered.Count == 0) return null;
            string needle = NormalizeUiText(query);
            return filtered
                .OrderBy(match => MatchSortKind(NormalizeUiText(match.Text), needle))
                .ThenBy(match => Math.Abs(NormalizeUiText(match.Text).Length - needle.Length))
                .ThenBy(match => match.Rect.Width * match.Rect.Height)
                .ThenBy(match => LeftTopRank(match))
                .First();
        }

        public static List<OcrMatch> DeduplicateByRect(IEnumerable<OcrMatch> matches)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            HashSet<string> seen = new HashSet<string>();
            if (matches == null) return result;

            foreach (OcrMatch match in matches)
            {
                if (match == null) continue;
                string key = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}:{2}:{3}",
                    (int)Math.Round(match.Rect.Left / 3),
                    (int)Math.Round(match.Rect.Top / 3),
                    (int)Math.Round(match.Rect.Right / 3),
                    (int)Math.Round(match.Rect.Bottom / 3));
                if (seen.Add(key)) result.Add(match);
            }

            return result;
        }

        public static string NormalizeUiText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch) || (ch >= '\u4e00' && ch <= '\u9fff'))
                {
                    sb.Append(char.ToUpperInvariant(NormalizeVariant(ch)));
                }
            }
            return sb.ToString();
        }

        public static double LeftTopRank(OcrMatch match)
        {
            return match.Rect.Left * FH6AutomationConstants.Ranking.LeftFirstWeight + match.Rect.Top;
        }

        private static int MatchSortKind(string normalized, string needle)
        {
            if (normalized == needle) return 0;
            if (normalized.Contains(needle)) return 1;
            if (needle.Contains(normalized)) return 2;
            return 3;
        }

        private static char NormalizeVariant(char ch)
        {
            switch (ch)
            {
                case '魯': return '鲁';
                case '臺': return '台';
                case '賓': return '宾';
                default: return ch;
            }
        }

        private static bool HasCjk(string text)
        {
            foreach (char ch in text)
            {
                if (ch >= '\u4e00' && ch <= '\u9fff') return true;
            }
            return false;
        }

        private enum MatchKind
        {
            Exact,
            ContainsNeedle,
            Partial,
            Fuzzy
        }

        private sealed class ScoredMatch
        {
            public readonly OcrMatch Match;
            public readonly string Normalized;
            public readonly MatchKind Kind;
            public readonly int ExtraLength;

            public ScoredMatch(OcrMatch match, string normalized, string needle)
            {
                Match = match;
                Normalized = normalized;
                if (normalized == needle)
                {
                    Kind = MatchKind.Exact;
                    ExtraLength = 0;
                }
                else if (normalized.Contains(needle))
                {
                    Kind = MatchKind.ContainsNeedle;
                    ExtraLength = Math.Max(0, normalized.Length - needle.Length);
                }
                else if (needle.Contains(normalized))
                {
                    Kind = MatchKind.Partial;
                    ExtraLength = Math.Max(0, needle.Length - normalized.Length);
                }
                else
                {
                    Kind = MatchKind.Fuzzy;
                    ExtraLength = Math.Abs(normalized.Length - needle.Length);
                }
            }
        }
    }
}
