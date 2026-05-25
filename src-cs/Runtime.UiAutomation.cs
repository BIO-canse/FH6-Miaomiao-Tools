using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private void FindTextAndClick(string text, string label)
        {
            FindTextAndClick(text, label, true);
        }

        private void FindTextAndClick(string text, string label, bool moveToIdleAfterClick)
        {
            string cacheKey = UiClickCacheKey("text", label, text);
            if (TryClickCachedUiPoint(cacheKey, label, text, moveToIdleAfterClick))
            {
                return;
            }

            OcrSnapshot last = null;
            for (int i = 0; i < FH6AutomationConstants.Ocr.UiFindAttempts; i++)
            {
                DebugGate("find text " + label, "OCR 找 " + text + " 并点击，第 " + (i + 1) + " 次");
                WaitBeforeUiOcrCapture("before OCR " + label);
                last = ReadScreen();
                List<OcrMatch> matches = FindConfiguredCjkTextMatches(last, text);
                SetOcrFields(new OcrFieldGroup(label, matches));
                SetOcrSummary("OCR: " + text + "=" + matches.Count);
                if (matches.Count > 0)
                {
                    OcrMatch chosen = ChooseUiTextMatch(matches, text);
                    Point center = chosen.RectCenter();
                    RememberUiClickPoint(cacheKey, label, center);
                    input.MoveTo(center.X, center.Y);
                    input.Click();
                    if (moveToIdleAfterClick) MoveMouseToScreenBottomRight("idle after clicking " + label);
                    return;
                }

                WriteOcrDump(last, "find-text-" + SanitizeDebugLabel(label));
                FullAutoSleep(FH6AutomationConstants.Timing.UiFindRetryDelayMs);
            }

            Fail(last, "text-not-found-" + label);
        }

        private string UiClickCacheKey(string category, string label, string text)
        {
            return category + "|" + label + "|" + text;
        }

        private bool TryClickCachedUiPoint(string cacheKey, string label, string text, bool moveToIdleAfterClick)
        {
            Point point;
            if (!uiClickCache.TryGetValue(cacheKey, out point)) return false;

            DebugGate("ui click cache " + label, "复用 UI 坐标 " + label + " (" + point.X + "," + point.Y + ")");
            SetOcrSummary("UI坐标缓存: " + label + " -> " + point.X + "," + point.Y + "，等待 0.5 秒后点击");
            StartUiCacheOcrGuard(label, text);
            FullAutoSleep(FH6AutomationConstants.Timing.HalfSecondMs);
            input.MoveTo(point.X, point.Y);
            input.Click();
            if (moveToIdleAfterClick) MoveMouseToScreenBottomRight("idle after clicking cached " + label);
            return true;
        }

        private void RememberUiClickPoint(string cacheKey, string label, Point point)
        {
            uiClickCache[cacheKey] = point;
            PersistSharedUiClickCache("remember " + label);
            SetOcrSummary("UI坐标缓存: 已记录 " + label + " -> " + point.X + "," + point.Y);
        }

        private bool SharedUiClickCacheAllowed()
        {
            return task == AutomationTask.FullAuto || handoffStart;
        }

        private void LoadSharedUiClickCacheIfAllowed()
        {
            if (!SharedUiClickCacheAllowed()) return;

            try
            {
                if (string.IsNullOrWhiteSpace(uiClickCachePath) || !File.Exists(uiClickCachePath)) return;

                Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(uiClickCachePath, Encoding.UTF8));
                object entriesValue;
                ArrayList entries = root != null && root.TryGetValue("entries", out entriesValue)
                    ? entriesValue as ArrayList
                    : null;
                if (entries == null) return;

                foreach (object item in entries)
                {
                    Dictionary<string, object> entry = item as Dictionary<string, object>;
                    if (entry == null) continue;

                    object keyValue;
                    object xValue;
                    object yValue;
                    if (!entry.TryGetValue("key", out keyValue) ||
                        !entry.TryGetValue("x", out xValue) ||
                        !entry.TryGetValue("y", out yValue))
                    {
                        continue;
                    }

                    string key = Convert.ToString(keyValue, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    uiClickCache[key] = new Point(
                        Convert.ToInt32(xValue, CultureInfo.InvariantCulture),
                        Convert.ToInt32(yValue, CultureInfo.InvariantCulture));
                }

                Console.WriteLine("[UI_CACHE] 已读取 " + uiClickCache.Count + " 个 UI 坐标缓存：" + uiClickCachePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UI_CACHE] 读取失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.LoadSharedUiClickCache", ex);
            }
        }

        private void ClearSharedUiClickCache(string reason)
        {
            uiClickCache.Clear();
            try
            {
                if (!string.IsNullOrWhiteSpace(uiClickCachePath) && File.Exists(uiClickCachePath)) File.Delete(uiClickCachePath);
                Console.WriteLine("[UI_CACHE] 已清空 UI 坐标缓存：" + reason);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UI_CACHE] 清空失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.ClearSharedUiClickCache", ex);
            }
        }

        private void PersistSharedUiClickCache(string reason)
        {
            if (!SharedUiClickCacheAllowed()) return;

            try
            {
                string directory = Path.GetDirectoryName(uiClickCachePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["schema"] = "fh6_ui_click_cache.v1";
                root["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                root["reason"] = reason;

                ArrayList entries = new ArrayList();
                foreach (KeyValuePair<string, Point> pair in uiClickCache.OrderBy(p => p.Key))
                {
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["key"] = pair.Key;
                    entry["x"] = pair.Value.X;
                    entry["y"] = pair.Value.Y;
                    entries.Add(entry);
                }

                root["entries"] = entries;
                File.WriteAllText(uiClickCachePath, new JavaScriptSerializer().Serialize(root), Encoding.UTF8);
                Console.WriteLine("[UI_CACHE] 已写入 " + uiClickCache.Count + " 个 UI 坐标缓存：" + reason);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UI_CACHE] 写入失败：" + ex.Message);
                FH6FailureLog.Write("Runtime.PersistSharedUiClickCache", ex);
            }
        }

        private void WaitBeforeUiOcrCapture(string reason)
        {
            SetOcrSummary("UI OCR 等待画面稳定 1 秒: " + reason);
            SleepWithFullAutoHotkey(FH6AutomationConstants.Timing.UiOcrStableWaitMs);
        }

        private static string SanitizeDebugLabel(string label)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in label)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            return sb.Length == 0 ? "text" : sb.ToString();
        }
    }
}
