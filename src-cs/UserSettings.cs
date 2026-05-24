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

namespace FH6SkillPointOcr
{
    internal sealed class UserSettings
    {
        public int VisibleRows;
        public int VisibleColumns;
        public double GridCellLeft;
        public double GridCellTop;
        public double GridCellWidth;
        public double GridCellHeight;
        public bool DpiAwareCoordinates;
        public string CalibrationMode;
        public bool WindowBoundCalibration;
        public double CalibrationClientLeft;
        public double CalibrationClientTop;
        public double CalibrationClientWidth;
        public double CalibrationClientHeight;

        public static UserSettings LoadOrCreate(Config config)
        {
            string path = Path.Combine(config.BaseDir, "config", "user-settings.json");
            if (File.Exists(path))
            {
                UserSettings settings = Load(path, config);
                Apply(config, settings);
                Console.WriteLine("[SETTINGS] 已读取 " + path);
                Console.WriteLine("[SETTINGS] 我的车辆页面完整可见行数：" + settings.VisibleRows);
                Console.WriteLine("[SETTINGS] 我的车辆页面完整可见列数：" + settings.VisibleColumns);
                Console.WriteLine("[SETTINGS] 左上格子：left={0:0}, top={1:0}, width={2:0}, height={3:0}",
                    settings.GridCellLeft,
                    settings.GridCellTop,
                    settings.GridCellWidth,
                    settings.GridCellHeight);
                if (settings.WindowBoundCalibration)
                {
                    Console.WriteLine("[SETTINGS] 框选时窗口客户区：left={0:0}, top={1:0}, width={2:0}, height={3:0}",
                        settings.CalibrationClientLeft,
                        settings.CalibrationClientTop,
                        settings.CalibrationClientWidth,
                        settings.CalibrationClientHeight);
                }
                else
                {
                    Console.WriteLine("[SETTINGS] 旧设置没有窗口客户区基准；可继续按绝对坐标运行，但无法自动等比迁移。");
                }
                return settings;
            }

            UserSettings created = CreateFromConsole(path, config);
            Apply(config, created);
            return created;
        }

        public static UserSettings Reset(Config config)
        {
            string path = Path.Combine(config.BaseDir, "config", "user-settings.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                Console.WriteLine("[SETTINGS] 已删除旧设置 " + path);
            }
            else
            {
                Console.WriteLine("[SETTINGS] 当前没有旧设置，直接创建新设置。");
            }

            UserSettings created = CreateFromConsole(path, config);
            Apply(config, created);
            return created;
        }

        private static UserSettings Load(string path, Config config)
        {
            Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            UserSettings settings = new UserSettings();
            settings.DpiAwareCoordinates = GetBool(json, "dpi_aware_coordinates", false);
            settings.CalibrationMode = GetString(json, "calibration_mode", "");
            settings.VisibleRows = GetInt(json, "visible_rows", 0);
            settings.VisibleColumns = GetInt(json, "visible_columns", 0);
            settings.GridCellLeft = GetDouble(json, "grid_cell_left", 0);
            settings.GridCellTop = GetDouble(json, "grid_cell_top", 0);
            settings.GridCellWidth = GetDouble(json, "grid_cell_width", 0);
            settings.GridCellHeight = GetDouble(json, "grid_cell_height", 0);
            settings.WindowBoundCalibration = GetBool(json, "window_bound_calibration", false);
            settings.CalibrationClientLeft = GetDouble(json, "calibration_client_left", 0);
            settings.CalibrationClientTop = GetDouble(json, "calibration_client_top", 0);
            settings.CalibrationClientWidth = GetDouble(json, "calibration_client_width", 0);
            settings.CalibrationClientHeight = GetDouble(json, "calibration_client_height", 0);
            if (!settings.DpiAwareCoordinates)
            {
                Console.WriteLine("[SETTINGS] 旧设置不是 DPI aware 坐标，截图会偏移，需要重新框选。");
                return CreateFromConsole(path, config);
            }
            if (!string.Equals(settings.CalibrationMode, "full_grid_v1", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SETTINGS] 旧设置只框选单个格子，需要改为框选完整可见格子区域。");
                return CreateFromConsole(path, config);
            }
            if (settings.VisibleRows <= 0 || settings.VisibleColumns <= 0 || settings.GridCellWidth <= 0 || settings.GridCellHeight <= 0)
            {
                Console.WriteLine("[SETTINGS] user-settings.json 缺少可见行列或格子尺寸，需要重新设置。");
                return CreateFromConsole(path, config);
            }
            return settings;
        }

        private static UserSettings CreateFromConsole(string path, Config config)
        {
            Console.WriteLine("首次运行需要保存一个本机显示设置。");
            Console.WriteLine("请进入“我的车辆”页面，数一下屏幕里能看到几行、几列完整车辆格子。");

            int rows = ReadPositiveInt("请输入完整可见行数，直接回车默认 3：", 3);
            int columns = ReadPositiveInt("请输入完整可见列数，然后回车：", 0);

            Console.WriteLine("接下来请用鼠标框选“所有完整可见车辆格子的整体区域”。");
            Console.WriteLine("例如 3 行 4 列，就从左上完整格子的左上角拖到右下完整格子的右下角。程序会按你输入的行列数自动切分。");
            Console.Write("按 Enter 后隐藏窗口并开始框选：");
            Console.ReadLine();
            RectangleF gridRect = MouseCellCalibrator.Capture(config.MonitorIndex);
            while (gridRect.Width <= 0 || gridRect.Height <= 0)
            {
                Console.WriteLine("框选无效，请重新框选。");
                Console.Write("按 Enter 后隐藏窗口并重新框选：");
                Console.ReadLine();
                gridRect = MouseCellCalibrator.Capture(config.MonitorIndex);
            }

            UserSettings settings = new UserSettings();
            settings.VisibleRows = rows;
            settings.VisibleColumns = columns;
            settings.GridCellLeft = gridRect.Left;
            settings.GridCellTop = gridRect.Top;
            settings.GridCellWidth = gridRect.Width / columns;
            settings.GridCellHeight = gridRect.Height / rows;
            settings.DpiAwareCoordinates = true;
            settings.CalibrationMode = "full_grid_v1";

            Point center = new Point(
                (int)Math.Round(gridRect.Left + gridRect.Width / 2),
                (int)Math.Round(gridRect.Top + gridRect.Height / 2));
            WindowBinding binding;
            if (WindowLocator.TryBindFromPoint(center, Process.GetCurrentProcess().Id, out binding))
            {
                settings.WindowBoundCalibration = true;
                settings.CalibrationClientLeft = binding.ClientBounds.Left;
                settings.CalibrationClientTop = binding.ClientBounds.Top;
                settings.CalibrationClientWidth = binding.ClientBounds.Width;
                settings.CalibrationClientHeight = binding.ClientBounds.Height;
                Console.WriteLine("[SETTINGS] 已绑定框选区域下方窗口：" + binding.Summary());
            }
            else
            {
                settings.WindowBoundCalibration = false;
                Console.WriteLine("[SETTINGS] 未能识别框选区域下方窗口；本设置将按绝对坐标保存，无法自动等比迁移。");
            }

            Save(path, settings);
            Console.WriteLine("[SETTINGS] 已保存 " + path);
            Console.WriteLine("[SETTINGS] 整体区域：left={0:0}, top={1:0}, width={2:0}, height={3:0}", gridRect.Left, gridRect.Top, gridRect.Width, gridRect.Height);
            Console.WriteLine("[SETTINGS] 单格尺寸：width={0:0}, height={1:0}", settings.GridCellWidth, settings.GridCellHeight);
            return settings;
        }

        private static int ReadPositiveInt(string prompt, int defaultValue)
        {
            int columns = 0;
            while (columns <= 0)
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) && defaultValue > 0) return defaultValue;
                if (!int.TryParse((input ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out columns) || columns <= 0)
                {
                    columns = 0;
                    Console.WriteLine("输入无效，请输入正整数。");
                }
            }
            return columns;
        }

        private static void Save(string path, UserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Dictionary<string, object> json = new Dictionary<string, object>();
            json["dpi_aware_coordinates"] = true;
            json["calibration_mode"] = "full_grid_v1";
            json["visible_rows"] = settings.VisibleRows;
            json["visible_columns"] = settings.VisibleColumns;
            json["grid_cell_left"] = Math.Round(settings.GridCellLeft, 2);
            json["grid_cell_top"] = Math.Round(settings.GridCellTop, 2);
            json["grid_cell_width"] = Math.Round(settings.GridCellWidth, 2);
            json["grid_cell_height"] = Math.Round(settings.GridCellHeight, 2);
            json["window_bound_calibration"] = settings.WindowBoundCalibration;
            json["calibration_client_left"] = Math.Round(settings.CalibrationClientLeft, 2);
            json["calibration_client_top"] = Math.Round(settings.CalibrationClientTop, 2);
            json["calibration_client_width"] = Math.Round(settings.CalibrationClientWidth, 2);
            json["calibration_client_height"] = Math.Round(settings.CalibrationClientHeight, 2);
            string body = new JavaScriptSerializer().Serialize(json);
            File.WriteAllText(path, PrettyJson(body), Encoding.UTF8);
        }

        private static void Apply(Config config, UserSettings settings)
        {
            if (settings.VisibleRows > 0) config.GridRows = settings.VisibleRows;
            if (settings.VisibleColumns > 0) config.VisibleColumns = settings.VisibleColumns;
            config.GridCellLeft = settings.GridCellLeft;
            config.GridCellTop = settings.GridCellTop;
            config.GridCellWidth = settings.GridCellWidth;
            config.GridCellHeight = settings.GridCellHeight;
            config.WindowBoundCalibration = settings.WindowBoundCalibration;
            config.CalibrationClientLeft = settings.CalibrationClientLeft;
            config.CalibrationClientTop = settings.CalibrationClientTop;
            config.CalibrationClientWidth = settings.CalibrationClientWidth;
            config.CalibrationClientHeight = settings.CalibrationClientHeight;
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

        private static string GetString(Dictionary<string, object> json, string key, string fallback)
        {
            object value;
            return json.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback;
        }

        private static string PrettyJson(string compact)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char ch = compact[i];
                if (ch == '"' && (i == 0 || compact[i - 1] != '\\')) inString = !inString;

                if (!inString && (ch == '{' || ch == '['))
                {
                    sb.Append(ch).AppendLine();
                    indent++;
                    sb.Append(new string(' ', indent * 2));
                }
                else if (!inString && (ch == '}' || ch == ']'))
                {
                    sb.AppendLine();
                    indent--;
                    sb.Append(new string(' ', indent * 2)).Append(ch);
                }
                else if (!inString && ch == ',')
                {
                    sb.Append(ch).AppendLine();
                    sb.Append(new string(' ', indent * 2));
                }
                else if (!inString && ch == ':')
                {
                    sb.Append(": ");
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
