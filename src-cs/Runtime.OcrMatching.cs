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
            List<OcrMatch> matches = ocr.Find(snapshot, config.TargetVehicleText);
            matches.AddRange(FindLatinContainsMatches(snapshot, config.TargetVehicleText));
            matches.AddRange(ocr.FindLatinFuzzy(snapshot, config.TargetVehicleText, FH6AutomationConstants.Ocr.TargetVehicleLatinFuzzyDistance));
            return OcrMatchFilter.FilterUiTextMatches(matches, config.TargetVehicleText);
        }

        private List<OcrMatch> FindNewBadgeMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, config.NewBadgeText);
            if (matches.Count > 0) return OcrMatchFilter.FilterUiTextMatches(matches, config.NewBadgeText);
            return OcrMatchFilter.FilterUiTextMatches(
                ocr.FindCjkFuzzy(snapshot, config.NewBadgeText, FH6AutomationConstants.Ocr.NewBadgeCjkMinCommonChars, FH6AutomationConstants.Ocr.NewBadgeCjkMaxNormalizedLength),
                config.NewBadgeText);
        }

        private List<OcrMatch> FindManufacturerMatches(OcrSnapshot snapshot)
        {
            return FindConfiguredCjkTextMatches(snapshot, config.ManufacturerText);
        }

        private List<OcrMatch> FindMyHorizonMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, config.MyHorizonText);
            if (matches.Count > 0) return OcrMatchFilter.FilterUiTextMatches(matches, config.MyHorizonText);
            return OcrMatchFilter.FilterUiTextMatches(
                ocr.FindCjkFuzzy(snapshot, config.MyHorizonText, FH6AutomationConstants.Ocr.MyHorizonCjkMinCommonChars, FH6AutomationConstants.Ocr.MyHorizonCjkMaxNormalizedLength),
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
            List<OcrMatch> matches = ocr.Find(snapshot, text);
            matches.AddRange(FindCjkLooseMatches(
                snapshot,
                text,
                Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength)));
            matches.AddRange(ocr.FindCjkFuzzy(
                snapshot,
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
                if (haystack.Contains(needle) || needle.Contains(haystack) || CommonCharCountLoose(needle, haystack) >= minCommonChars)
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
    }
}
