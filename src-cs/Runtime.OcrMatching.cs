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
            if (matches.Count > 0) return matches;
            return ocr.FindLatinFuzzy(snapshot, config.TargetVehicleText, FH6AutomationConstants.Ocr.TargetVehicleLatinFuzzyDistance);
        }

        private List<OcrMatch> FindNewBadgeMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, config.NewBadgeText);
            if (matches.Count > 0) return matches;
            return ocr.FindCjkFuzzy(snapshot, config.NewBadgeText, FH6AutomationConstants.Ocr.NewBadgeCjkMinCommonChars, FH6AutomationConstants.Ocr.NewBadgeCjkMaxNormalizedLength);
        }

        private List<OcrMatch> FindManufacturerMatches(OcrSnapshot snapshot)
        {
            return FindConfiguredCjkTextMatches(snapshot, config.ManufacturerText);
        }

        private List<OcrMatch> FindMyHorizonMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, config.MyHorizonText);
            if (matches.Count > 0) return matches;
            return ocr.FindCjkFuzzy(snapshot, config.MyHorizonText, FH6AutomationConstants.Ocr.MyHorizonCjkMinCommonChars, FH6AutomationConstants.Ocr.MyHorizonCjkMaxNormalizedLength);
        }

        private List<OcrMatch> FindDeleteMarkerMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, config.DeleteMarkerText);
            if (matches.Count > 0) return matches;
            return ocr.FindLatinFuzzy(snapshot, config.DeleteMarkerText, FH6AutomationConstants.Ocr.MarkerLatinFuzzyDistance);
        }

        private List<OcrMatch> FindDriveMarkerMatches(OcrSnapshot snapshot)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, config.DriveMarkerText);
            if (matches.Count > 0) return matches;
            return ocr.FindLatinFuzzy(snapshot, config.DriveMarkerText, FH6AutomationConstants.Ocr.MarkerLatinFuzzyDistance);
        }

        private List<OcrMatch> FindConfiguredCjkTextMatches(OcrSnapshot snapshot, string text)
        {
            List<OcrMatch> matches = ocr.Find(snapshot, text);
            if (matches.Count > 0) return matches;
            return ocr.FindCjkFuzzy(
                snapshot,
                text,
                Math.Min(FH6AutomationConstants.Ocr.UiCjkMaxCommonChars, Math.Max(1, text.Length - 1)),
                Math.Max(FH6AutomationConstants.Ocr.UiCjkMaxExtraLength, text.Length + FH6AutomationConstants.Ocr.UiCjkMaxExtraLength));
        }

        private OcrMatch ChooseUiTextMatch(List<OcrMatch> matches, string text)
        {
            string needle = NormalizeUiText(text);
            List<OcrMatch> exact = matches
                .Where(m => NormalizeUiText(m.Text) == needle)
                .OrderBy(SortLeftTop)
                .ToList();
            if (exact.Count > 0) return exact.First();

            return matches
                .OrderBy(m => NormalizeUiText(m.Text).Length)
                .ThenBy(m => m.Rect.Width * m.Rect.Height)
                .ThenBy(SortLeftTop)
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
    }
}
