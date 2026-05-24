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
            SleepWithFullAutoHotkey(config.AfterClickMs);
            MoveMouseToVehicleListSecondRowRightEdge("idle after entering Subaru vehicle list");
        }

        private void FindSubaruManufacturerByOcr(string cacheLabel, bool moveToIdleAfterClick)
        {
            SetStatus("select Subaru manufacturer", "先向下滚动 " + config.ManufacturerScrollTicks + " 格，再优先复用 " + cacheLabel + " 坐标；没有缓存才 OCR");
            SetOcrSummary("制造商定位: 向下滚动 " + config.ManufacturerScrollTicks + " 格 -> 检查缓存 -> 缓存缺失才 OCR");
            string cacheKey = UiClickCacheKey("text", cacheLabel, config.ManufacturerText);
            ScrollManufacturerList(config.ManufacturerScrollTicks, "manufacturer list scroll focus", "滚动后检查 " + cacheLabel + " 坐标缓存");
            SetStatus("manufacturer cache check", "滚动已完成，检查 " + cacheLabel + " 坐标缓存");
            if (TryClickCachedUiPoint(cacheKey, cacheLabel, moveToIdleAfterClick))
            {
                return;
            }
            SetStatus("manufacturer OCR fallback", cacheLabel + " 没有缓存，开始 OCR 查找 " + config.ManufacturerText);
            SetOcrSummary("制造商定位: " + cacheLabel + " 无缓存，进入 OCR 兜底");

            OcrSnapshot last = null;
            bool wroteMissCapture = false;
            int attempts = Math.Max(1, config.ManufacturerFindAttempts);
            for (int i = 0; i < attempts; i++)
            {
                DebugGate("find manufacturer " + cacheLabel, "OCR 找 " + config.ManufacturerText + " 并点击，第 " + (i + 1) + " 次");
                WaitBeforeUiOcrCapture("before OCR " + cacheLabel);
                last = ReadScreen();
                List<OcrMatch> matches = FindConfiguredCjkTextMatches(last, config.ManufacturerText);
                SetOcrFields(new OcrFieldGroup(cacheLabel, matches));
                SetOcrSummary("制造商OCR: " + config.ManufacturerText + "=" + matches.Count + "，尝试 " + (i + 1) + "/" + attempts + "，" + CaptureSummary(last));
                if (matches.Count > 0)
                {
                    OcrMatch chosen = ChooseUiTextMatch(matches, config.ManufacturerText);
                    Point center = chosen.RectCenter();
                    RememberUiClickPoint(cacheKey, cacheLabel, center);
                    input.MoveTo(center.X, center.Y);
                    input.Click();
                    if (moveToIdleAfterClick) MoveMouseToScreenBottomRight("idle after clicking " + cacheLabel);
                    return;
                }

                WriteOcrDump(last, "find-manufacturer-" + SanitizeDebugLabel(cacheLabel));
                if (!wroteMissCapture)
                {
                    WriteOcrSnapshotCapture(last, "manufacturer-miss-" + SanitizeDebugLabel(cacheLabel));
                    wroteMissCapture = true;
                }
                if (i + 1 < attempts)
                {
                    SetOcrSummary("制造商OCR: 未找到 " + config.ManufacturerText + "，补滚动后重试 " + (i + 2) + "/" + attempts);
                    ScrollManufacturerList(config.ManufacturerRetryScrollTicks, "manufacturer retry scroll focus", "补滚动后继续 OCR 查找 " + config.ManufacturerText);
                }
            }

            Fail(last, "manufacturer-not-found-" + cacheLabel);
        }

        private void ScrollManufacturerList(int ticks, string reason, string afterScrollAction)
        {
            SetStatus("manufacturer scroll", "鼠标移到屏幕中心，滚轮向下 " + ticks + " 格；" + afterScrollAction);
            MoveMouseToScreenCenter(reason);
            input.ScrollDown(ticks, config.ScrollTickDelayMs);
            SleepWithFullAutoHotkey(config.SingleScrollDelayMs);
            MoveMouseToScreenBottomRight("idle before manufacturer cache/OCR");
            SetStatus("manufacturer scroll done", afterScrollAction);
        }

        private static string CaptureSummary(OcrSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Screenshot == null || snapshot.Screenshot.Image == null) return "截图=无";
            return string.Format(
                CultureInfo.InvariantCulture,
                "截图=[{0},{1},{2},{3}]",
                snapshot.Screenshot.Left,
                snapshot.Screenshot.Top,
                snapshot.Screenshot.Image.Width,
                snapshot.Screenshot.Image.Height);
        }

    }
}
