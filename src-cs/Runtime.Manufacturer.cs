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
        private void OpenSubaruList()
        {
            ClearOcrFields();
            OpenManufacturerSelectionPage();
            SelectSubaruManufacturer();
            input.SleepMs(config.AfterClickMs);
        }

        private void OpenManufacturerSelectionPage()
        {
            SetStatus("open manufacturer list", "Enter -> 等待 1 秒 -> Backspace -> 等待 0.5 秒打开制造商选择页");
            Console.WriteLine("[MANUFACTURER] 打开制造商选择页：Enter -> 等待 1 秒 -> Backspace -> 等待 0.5 秒");
            DebugGate("open manufacturer list", "按 Enter");
            input.Tap("ENTER");
            DebugGate("open manufacturer list", "等待 1 秒");
            input.SleepMs(FH6AutomationConstants.Timing.OneSecondMs);
            DebugGate("open manufacturer list", "按 Backspace");
            input.Tap("BACKSPACE");
            DebugGate("open manufacturer list", "等待制造商选择页稳定 0.5 秒");
            input.SleepMs(FH6AutomationConstants.Timing.HalfSecondMs);
        }

        private void SelectSubaruManufacturer()
        {
            FindSubaruManufacturerByOcr("车库斯巴鲁制造商", false);
        }

        private void FindSubaruManufacturerByOcr(string cacheLabel, bool moveToIdleAfterClick)
        {
            SetStatus("select Subaru manufacturer", "鼠标移到屏幕中心，滚动到底，OCR 点击斯巴鲁");
            SetOcrSummary("制造商定位: 滚动到底后 OCR 查找斯巴鲁");
            MoveMouseToScreenCenter("manufacturer list scroll focus");
            input.ScrollDown(config.ManufacturerScrollTicks, config.ScrollTickDelayMs);
            SleepWithFullAutoHotkey(config.SingleScrollDelayMs);
            FindTextAndClick(config.ManufacturerText, cacheLabel, moveToIdleAfterClick);
        }
    }
}
