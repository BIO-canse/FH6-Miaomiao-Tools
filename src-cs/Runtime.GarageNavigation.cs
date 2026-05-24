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
        private void EnterGarageStandardPosition()
        {
            ClearOcrFields();
            if (task == AutomationTask.FullAuto) SetStage("A. 大世界自动进入车库标准位");
            SetStatus("enter garage standard position", "从大世界进入车库标准位");
            FullAutoCheckPoint();

            MoveMouseToScreenBottomRight("avoid menu selection interference");
            FullAutoCheckPoint();

            DebugGate("enter garage standard position", "按 Esc，等待 1 秒");
            input.Tap("ESC");
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.OneSecondMs);

            DebugGate("enter garage standard position", "Right，等待 1 秒，Right，Right，Right，等待 1 秒，Right，Right");
            input.Tap("RIGHT");
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.OneSecondMs);
            input.Tap("RIGHT");
            input.Tap("RIGHT");
            input.Tap("RIGHT");
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.OneSecondMs);
            input.Tap("RIGHT");
            input.Tap("RIGHT");

            DebugGate("enter garage standard position", "Enter，等待 1 秒，Enter，等待 15 秒，Right，等待 1 秒，Right");
            input.Tap("ENTER");
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.OneSecondMs);
            input.Tap("ENTER");
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.FifteenSecondsMs);
            input.Tap("RIGHT");
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.OneSecondMs);
            input.Tap("RIGHT");

            ClearOcrFields();
            SetStatus("garage standard position ready", "已进入车库标准位");
        }

        private void MoveMouseToScreenBottomRight(string reason)
        {
            Rectangle bounds = capture.GetBounds();
            int x = Math.Max(bounds.Left, bounds.Right - 2);
            int y = Math.Max(bounds.Top, bounds.Bottom - 2);
            Console.WriteLine("[INPUT] " + reason + " at bottom right " + x + "," + y);
            input.MoveTo(x, y);
        }

        private void MoveMouseToScreenCenter(string reason)
        {
            Rectangle bounds = capture.GetBounds();
            int x = bounds.Left + bounds.Width / 2;
            int y = bounds.Top + bounds.Height / 2;
            Console.WriteLine("[INPUT] " + reason + " at screen center " + x + "," + y);
            input.MoveTo(x, y);
        }
    }
}
