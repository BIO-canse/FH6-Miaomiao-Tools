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
        private void RunFixedSequence()
        {
            MoveMouseToFirstVisibleCellCenter("idle in vehicle list before fixed sequence");
            DebugGate("fixed key sequence", "执行固定按键序列");
            for (int index = 0; index < config.FixedSequence.Count; index++)
            {
                string raw = config.FixedSequence[index];
                string step = raw.Trim().ToUpperInvariant();
                bool nextStepIsWait = index + 1 < config.FixedSequence.Count && IsWaitStep(config.FixedSequence[index + 1]);
                if (step.StartsWith("WAIT:", StringComparison.Ordinal))
                {
                    int waitMs = int.Parse(step.Substring(5), CultureInfo.InvariantCulture);
                    DebugGate("fixed key sequence", string.Format("等待 {0:g} 秒", waitMs / 1000.0));
                    input.SleepMs(waitMs);
                }
                else if (step.Contains("*"))
                {
                    string[] parts = step.Split('*');
                    string key = parts[0];
                    int count = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    DebugGate("fixed key sequence", "按 " + key + " x" + count);
                    for (int i = 0; i < count; i++)
                    {
                        input.Tap(key);
                        WaitAfterMenuKeyIfNeeded(key, i == count - 1 && nextStepIsWait);
                    }
                }
                else
                {
                    DebugGate("fixed key sequence", "按 " + step);
                    input.Tap(step);
                    WaitAfterMenuKeyIfNeeded(step, nextStepIsWait);
                }
            }
        }

        private static bool IsWaitStep(string raw)
        {
            return raw.Trim().ToUpperInvariant().StartsWith("WAIT:", StringComparison.Ordinal);
        }

        private void WaitAfterMenuKeyIfNeeded(string key, bool nextStepIsWait)
        {
            if (nextStepIsWait) return;
            if (key != "ENTER" && key != "ESC") return;
            DebugGate("fixed key sequence", key + " 后等待 1 秒");
            input.SleepMs(FH6AutomationConstants.Timing.OneSecondMs);
        }
    }
}
