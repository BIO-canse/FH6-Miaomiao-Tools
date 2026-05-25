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
    internal sealed class CliOptions
    {
        public string ConfigPath = Path.Combine("config", "default.json");
        public bool DryRun;
        public bool NoOverlay;
        public bool ShowHelp;
        public string Mode;
        public AutomationTask? Task;
        public bool ReuseVehicleListState;
        public string SafeStopFile;
        public string SkillPointsStateFile;
        public string SkillPointsLogFile;
        public int? SkillPoints;
        public long? Credits;
        public bool? QuickVerify;

        public static CliOptions Parse(string[] args)
        {
            CliOptions options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--help" || arg == "-h")
                {
                    options.ShowHelp = true;
                }
                else if (arg == "--config" && i + 1 < args.Length)
                {
                    options.ConfigPath = args[++i];
                }
                else if (arg == "--dry-run")
                {
                    options.DryRun = true;
                }
                else if (arg == "--no-overlay")
                {
                    options.NoOverlay = true;
                }
                else if (arg == "--mode" && i + 1 < args.Length)
                {
                    options.Mode = args[++i].Trim().ToLowerInvariant();
                }
                else if (arg == "--task" && i + 1 < args.Length)
                {
                    string task = args[++i].Trim().ToLowerInvariant();
                    if (task == "delete" || task == "delete-vehicles" || task == "vehicle-delete")
                    {
                        options.Task = AutomationTask.DeleteVehicles;
                    }
                    else if (task == "skill" || task == "skill-points")
                    {
                        options.Task = AutomationTask.SkillPoints;
                    }
                    else if (task == "fullauto" || task == "full-auto" || task == "autoflow" || task == "auto-flow")
                    {
                        options.Task = AutomationTask.FullAuto;
                    }
                    else if (task == "blueprint-test" || task == "blueprint-cycle-test" || task == "minute-test")
                    {
                        options.Task = AutomationTask.BlueprintCycleTest;
                    }
                }
                else if (arg == "--handoff")
                {
                    options.ReuseVehicleListState = true;
                }
                else if (arg == "--safe-stop-file" && i + 1 < args.Length)
                {
                    options.SafeStopFile = args[++i];
                }
                else if (arg == "--skill-points-state-file" && i + 1 < args.Length)
                {
                    options.SkillPointsStateFile = args[++i];
                }
                else if (arg == "--skill-points-log-file" && i + 1 < args.Length)
                {
                    options.SkillPointsLogFile = args[++i];
                }
                else if (arg == "--skill-points" && i + 1 < args.Length)
                {
                    int value;
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
                    {
                        options.SkillPoints = value;
                    }
                }
                else if (arg == "--credits" && i + 1 < args.Length)
                {
                    long value;
                    if (long.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
                    {
                        options.Credits = value;
                    }
                }
                else if (arg == "--quick-verify" || arg == "--quick-verify-mode")
                {
                    options.QuickVerify = true;
                }
                else if (arg == "--normal-full-auto")
                {
                    options.QuickVerify = false;
                }
            }
            return options;
        }
    }

}
