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
    internal sealed class Config
    {
        public string BaseDir;
        public string SourcePath;
        public string OcrEngine;
        public string RapidOcrPython;
        public string RapidOcrBridge;
        public string PaddleOcrPython;
        public string PaddleOcrBridge;
        public string TesseractCmd;
        public string OcrLanguages;
        public int MonitorIndex;
        public int StartupDelayMs;
        public int TapMs;
        public int RepeatIntervalMs;
        public int AfterClickMs;
        public double OcrScale;
        public double OcrMinConfidence;
        public int OcrPsm;
        public string ManufacturerText;
        public string MyHorizonText;
        public string TargetVehicleText;
        public string NewBadgeText;
        public string DeleteMarkerText;
        public string DriveMarkerText;
        public string CreativeCenterText;
        public string LatestHotText;
        public string MyFavoritesText;
        public int ManufacturerScrollTicks;
        public int ManufacturerFindAttempts;
        public int ManufacturerRetryScrollTicks;
        public int ScrollTickDelayMs;
        public int SingleScrollDelayMs;
        public int MaxFindVehicleScrolls;
        public int MaxFindNewScrolls;
        public int SkillPointsPerVehicle;
        public int GridRows;
        public int VisibleColumns;
        public double GridCellLeft;
        public double GridCellTop;
        public double GridCellWidth;
        public double GridCellHeight;
        public bool WindowBoundCalibration;
        public double CalibrationClientLeft;
        public double CalibrationClientTop;
        public double CalibrationClientWidth;
        public double CalibrationClientHeight;
        public bool OverlayEnabled;
        public int OverlayHideBeforeCaptureMs;
        public string DebugDir;
        public string VirtualListPath;
        public List<string> FixedSequence;

        public static Config Load(string path)
        {
            string fullPath = ResolveConfigPath(path);
            Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(fullPath, Encoding.UTF8));
            Config cfg = new Config();
            cfg.SourcePath = fullPath;
            cfg.BaseDir = ResolveBaseDir(fullPath);
            cfg.OcrEngine = GetString(json, "ocr_engine", "paddleocr");
            cfg.RapidOcrPython = GetString(json, "rapidocr_python", "");
            cfg.RapidOcrBridge = GetString(json, "rapidocr_bridge", Path.Combine("runtime", "rapidocr_bridge.py"));
            cfg.PaddleOcrPython = GetString(json, "paddleocr_python", "");
            cfg.PaddleOcrBridge = GetString(json, "paddleocr_bridge", Path.Combine("runtime", "paddleocr_bridge.py"));
            cfg.TesseractCmd = GetString(json, "tesseract_cmd", Path.Combine("runtime", "tesseract", "tesseract.exe"));
            cfg.OcrLanguages = GetString(json, "ocr_languages", "chi_sim+eng");
            cfg.MonitorIndex = GetInt(json, "monitor_index", 1);
            cfg.StartupDelayMs = GetInt(json, "startup_delay_ms", FH6AutomationConstants.Timing.StartupDelayMs);
            cfg.TapMs = GetInt(json, "tap_ms", FH6AutomationConstants.Timing.TapMs);
            cfg.RepeatIntervalMs = GetInt(json, "repeat_interval_ms", FH6AutomationConstants.Timing.RepeatIntervalMs);
            cfg.AfterClickMs = GetInt(json, "after_click_ms", FH6AutomationConstants.Timing.AfterClickMs);
            cfg.OcrScale = GetDouble(json, "ocr_scale", FH6AutomationConstants.Ocr.Scale);
            cfg.OcrMinConfidence = GetDouble(json, "ocr_min_confidence", FH6AutomationConstants.Ocr.MinConfidence);
            cfg.OcrPsm = GetInt(json, "ocr_psm", FH6AutomationConstants.Ocr.Psm);
            cfg.ManufacturerText = GetString(json, "manufacturer_text", FH6AutomationConstants.Text.Manufacturer);
            cfg.MyHorizonText = GetString(json, "my_horizon_text", FH6AutomationConstants.Text.MyHorizon);
            cfg.TargetVehicleText = GetString(json, "target_vehicle_text", FH6AutomationConstants.Text.TargetVehicle);
            cfg.NewBadgeText = GetString(json, "new_badge_text", FH6AutomationConstants.Text.NewBadge);
            cfg.DeleteMarkerText = GetString(json, "delete_marker_text", FH6AutomationConstants.Text.DeleteMarker);
            cfg.DriveMarkerText = GetString(json, "drive_marker_text", FH6AutomationConstants.Text.DriveMarker);
            cfg.CreativeCenterText = GetString(json, "creative_center_text", FH6AutomationConstants.Text.CreativeCenter);
            cfg.LatestHotText = GetString(json, "latest_hot_text", FH6AutomationConstants.Text.LatestHot);
            cfg.MyFavoritesText = GetString(json, "my_favorites_text", FH6AutomationConstants.Text.MyFavorites);
            cfg.ManufacturerScrollTicks = GetInt(json, "manufacturer_scroll_ticks", FH6AutomationConstants.Flow.ManufacturerScrollTicks);
            cfg.ManufacturerFindAttempts = GetInt(json, "manufacturer_find_attempts", FH6AutomationConstants.Flow.ManufacturerFindAttempts);
            cfg.ManufacturerRetryScrollTicks = GetInt(json, "manufacturer_retry_scroll_ticks", FH6AutomationConstants.Flow.ManufacturerRetryScrollTicks);
            cfg.ScrollTickDelayMs = GetInt(json, "scroll_tick_delay_ms", FH6AutomationConstants.Flow.ScrollTickDelayMs);
            cfg.SingleScrollDelayMs = GetInt(json, "single_scroll_delay_ms", FH6AutomationConstants.Flow.SingleScrollDelayMs);
            cfg.MaxFindVehicleScrolls = GetInt(json, "max_find_vehicle_scrolls", FH6AutomationConstants.Flow.MaxFindVehicleScrolls);
            cfg.MaxFindNewScrolls = GetInt(json, "max_find_new_scrolls", FH6AutomationConstants.Flow.MaxFindNewScrolls);
            cfg.SkillPointsPerVehicle = GetInt(json, "skill_points_per_vehicle", FH6AutomationConstants.SkillPoints.PerVehicle);
            cfg.GridRows = GetInt(json, "grid_rows", FH6AutomationConstants.Flow.DefaultGridRows);
            cfg.VisibleColumns = GetInt(json, "visible_columns", 0);
            cfg.GridCellLeft = GetDouble(json, "grid_cell_left", 0);
            cfg.GridCellTop = GetDouble(json, "grid_cell_top", 0);
            cfg.GridCellWidth = GetDouble(json, "grid_cell_width", 0);
            cfg.GridCellHeight = GetDouble(json, "grid_cell_height", 0);
            cfg.WindowBoundCalibration = GetBool(json, "window_bound_calibration", false);
            cfg.CalibrationClientLeft = GetDouble(json, "calibration_client_left", 0);
            cfg.CalibrationClientTop = GetDouble(json, "calibration_client_top", 0);
            cfg.CalibrationClientWidth = GetDouble(json, "calibration_client_width", 0);
            cfg.CalibrationClientHeight = GetDouble(json, "calibration_client_height", 0);
            cfg.OverlayEnabled = GetBool(json, "overlay_enabled", true);
            cfg.OverlayHideBeforeCaptureMs = GetInt(json, "overlay_hide_before_capture_ms", FH6AutomationConstants.Timing.OverlayHideBeforeCaptureMs);
            cfg.DebugDir = GetString(json, "debug_dir", FH6AutomationConstants.Files.DebugDir);
            cfg.VirtualListPath = GetString(json, "virtual_list_path", FH6AutomationConstants.Files.VirtualListPath);
            cfg.FixedSequence = GetStringList(json, "fixed_sequence");
            if (cfg.FixedSequence.Count == 0)
            {
                cfg.FixedSequence.AddRange(FH6AutomationConstants.FixedSequences.SkillPointSequence);
            }
            return cfg;
        }

        public string ResolvePath(string raw)
        {
            if (Path.IsPathRooted(raw)) return raw;
            return Path.GetFullPath(Path.Combine(BaseDir, raw));
        }

        private static string ResolveConfigPath(string path)
        {
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

            List<string> candidates = new List<string>();
            candidates.Add(Path.GetFullPath(path));

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, path)));

            DirectoryInfo parent = Directory.GetParent(exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parent != null) candidates.Add(Path.GetFullPath(Path.Combine(parent.FullName, path)));

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            return candidates[0];
        }

        private static string ResolveBaseDir(string configPath)
        {
            string configDir = Path.GetDirectoryName(configPath);
            if (string.IsNullOrEmpty(configDir)) return Directory.GetCurrentDirectory();

            if (string.Equals(Path.GetFileName(configDir), "config", StringComparison.OrdinalIgnoreCase))
            {
                DirectoryInfo parent = Directory.GetParent(configDir);
                if (parent != null) return parent.FullName;
            }

            return configDir;
        }

        private static string GetString(Dictionary<string, object> json, string key, string fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static int GetInt(Dictionary<string, object> json, string key, int fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static double GetDouble(Dictionary<string, object> json, string key, double fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static bool GetBool(Dictionary<string, object> json, string key, bool fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static List<string> GetStringList(Dictionary<string, object> json, string key)
        {
            List<string> result = new List<string>();
            object value;
            if (!json.TryGetValue(key, out value) || value == null) return result;
            object[] array = value as object[];
            if (array != null)
            {
                foreach (object item in array) result.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
            }
            else
            {
                ArrayList list = value as ArrayList;
                if (list != null)
                {
                    foreach (object item in list) result.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                }
            }
            return result;
        }
    }

}
