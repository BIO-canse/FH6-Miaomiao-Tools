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
        private OcrSnapshot ReadScreen()
        {
            return ReadScreenInternal(config.OcrPsm);
        }

        private OcrSnapshot ReadScreenWithPsm(int psm)
        {
            return ReadScreenInternal(psm);
        }

        private OcrSnapshot ReadVehicleGridScreen()
        {
            if (!grid.Ready) return ReadScreen();
            Rectangle region = grid.CaptureBounds(FH6AutomationConstants.Ocr.GridCapturePaddingPx);
            if (region.Width <= 0 || region.Height <= 0) return ReadScreen();
            return ReadScreenInternal(config.OcrPsm, region);
        }

        private OcrSnapshot ReadScreenInternal(int psm)
        {
            return ReadScreenInternal(psm, Rectangle.Empty);
        }

        private OcrSnapshot ReadScreenInternal(int psm, Rectangle region)
        {
            RefreshWindowBindingAndGrid("before capture");
            overlay.HideForCapture(config.OverlayHideBeforeCaptureMs);
            try
            {
                Screenshot shot = region == Rectangle.Empty ? capture.Grab() : capture.Grab(region);
                string debugLabel = SaveDebugCapture(shot, psm, region);
                OcrSnapshot snapshot = ocr.Read(shot, psm, debugLabel);
                return snapshot;
            }
            catch (Exception ex)
            {
                WriteOcrException(ex);
                WriteFailureFullScreenCapture("ocr-exception");
                throw;
            }
            finally
            {
                overlay.ShowOverlay();
            }
        }

        private void EnableWindowBindingForAutomation(string reason)
        {
            capture.EnableWindowBinding(reason);
            RefreshWindowBindingAndGrid(reason);
            SetOcrSummary("窗口绑定: " + capture.BoundWindowSummary);
        }

        private void RefreshWindowBindingAndGrid(string reason)
        {
            if (!capture.WindowBindingEnabled) return;
            Rectangle bounds = capture.GetBounds();
            grid.SyncToClientBounds(bounds);
        }

        private void SetStatus(string newStatus, string newNextAction)
        {
            status = newStatus;
            if (newNextAction != null) nextAction = newNextAction;
            if (LooksLikeActionSequence(newNextAction)) actionSequence = newNextAction;
            Console.WriteLine("[STATE] " + status);
            UpdateOverlay(null, null, null, null);
        }

        private void SetStage(string newStage)
        {
            if (string.IsNullOrWhiteSpace(newStage)) return;
            bigStage = newStage;
            Console.WriteLine("[STAGE] " + bigStage);
            UpdateOverlay(null, null, null, null);
        }

        private void SetOcrSummary(string summary)
        {
            lastOcrSummary = summary;
            UpdateOverlay(null, null, null, null);
        }

        private void ClearOcrFields()
        {
            if (lastOcrFields.Count == 0) return;
            lastOcrFields = new List<OcrFieldView>();
            UpdateOverlay(null, null, null, null);
        }

        private void SetOcrFields(params OcrFieldGroup[] groups)
        {
            List<OcrFieldView> fields = new List<OcrFieldView>();
            foreach (OcrFieldGroup group in groups)
            {
                if (group == null || group.Matches == null) continue;
                foreach (OcrMatch match in group.Matches)
                {
                    fields.Add(new OcrFieldView(match.Rect, group.Label, match.Confidence));
                }
            }
            lastOcrFields = fields;
            UpdateOverlay(null, null, null, null);
        }

        private void DebugGate(string gateStatus, string action)
        {
            SetStatus(gateStatus, action);
            if (!stepDebug) return;
            nextAction = "等待 · 继续：" + action;
            UpdateOverlay(null, null, null, null);
            Console.WriteLine("[DEBUG_STEP] 等待 · 键：" + action);
            input.WaitForVkPress(InputController.VK_STEP);
            debugStepCount++;
            nextAction = "执行：" + action;
            UpdateOverlay(null, null, null, null);
        }

        private void UpdateOverlay(HashSet<CellKey> targets, HashSet<CellKey> validNew, HashSet<CellKey> invalidNew, CellKey? chosen)
        {
            UpdateOverlay(targets, validNew, invalidNew, null, null, chosen);
        }

        private void UpdateOverlay(HashSet<CellKey> targets, HashSet<CellKey> validNew, HashSet<CellKey> invalidNew, HashSet<CellKey> deletable, CellKey? chosen)
        {
            UpdateOverlay(targets, validNew, invalidNew, deletable, null, chosen);
        }

        private void UpdateOverlay(HashSet<CellKey> targets, HashSet<CellKey> validNew, HashSet<CellKey> invalidNew, HashSet<CellKey> deletable, HashSet<CellKey> drive, CellKey? chosen)
        {
            if (grid.Ready && (targets == null || validNew == null || invalidNew == null || deletable == null || drive == null))
            {
                HashSet<CellKey> cachedTargets;
                HashSet<CellKey> cachedValidNew;
                HashSet<CellKey> cachedInvalidNew;
                HashSet<CellKey> cachedDeletable;
                HashSet<CellKey> cachedDrive;
                vehicleList.GetVisibleStateSets(grid.VisibleColumns, out cachedTargets, out cachedValidNew, out cachedInvalidNew, out cachedDeletable, out cachedDrive);
                if (targets == null) targets = cachedTargets;
                if (validNew == null) validNew = cachedValidNew;
                if (invalidNew == null) invalidNew = cachedInvalidNew;
                if (deletable == null) deletable = cachedDeletable;
                if (drive == null) drive = cachedDrive;
            }

            targets = targets ?? new HashSet<CellKey>();
            validNew = validNew ?? new HashSet<CellKey>();
            invalidNew = invalidNew ?? new HashSet<CellKey>();
            deletable = deletable ?? new HashSet<CellKey>();
            drive = drive ?? new HashSet<CellKey>();
            List<CellView> cells = grid.Ready ? grid.CellViews(targets, validNew, invalidNew, deletable, drive, chosen) : new List<CellView>();
            if (grid.Ready)
            {
                Dictionary<CellKey, int> visibleStateCodes = vehicleList.GetVisibleStateCodes(grid.VisibleColumns);
                foreach (CellView cell in cells)
                {
                    cell.GlobalCol = vehicleList.CurrentOffset + cell.Col;
                    int stateCode;
                    if (visibleStateCodes.TryGetValue(new CellKey(cell.Row, cell.Col), out stateCode))
                    {
                        cell.Known = true;
                        cell.StateCode = stateCode;
                    }
                    else
                    {
                        cell.Known = false;
                        cell.StateCode = -1;
                    }
                }
            }
            OverlayDetails details = new OverlayDetails();
            details.Mode = TaskModePrefix() + (stepDebug ? "调试" : "正常");
            details.Stage = bigStage;
            details.Status = status;
            details.NextAction = nextAction;
            details.ActionSequence = actionSequence;
            details.Cycle = CycleSummary();
            details.LoopCount = loopCount;
            details.DebugSteps = debugStepCount;
            details.Calibration = GridCalibrationSummary();
            details.Grid = GridSummary();
            details.VirtualList = vehicleList.Summary();
            details.Ocr = lastOcrSummary;
            details.Target = lastTargetSummary;
            details.SkillPoints = SkillPointSummary();
            details.SuperWheelspins = "超级抽奖: " + superWheelspinCount;
            details.MinuteLoop = minuteLoopSummary;
            details.ElapsedSeconds = Math.Max(0, (DateTime.Now - startedAtLocal).TotalSeconds);
            details.Failures = failures;
            overlay.Update(details, cells, lastOcrFields);
        }

        private string SkillPointSummary()
        {
            if (task == AutomationTask.DeleteVehicles) return "删除车辆模式: 4=可删";
            if (task == AutomationTask.FullAuto) return "技术点: " + remainingSkillPoints + " / " + FullAutoSkillPointTarget() + (quickVerifyMode ? " 验证" : "") + "；CR: " + remainingCredits.ToString("N0", CultureInfo.InvariantCulture);
            return string.Format(
                CultureInfo.InvariantCulture,
                "技术点: {0}, 每轮-{1}",
                remainingSkillPoints,
                config.SkillPointsPerVehicle);
        }

        private string CycleSummary()
        {
            if (task == AutomationTask.FullAuto)
            {
                return "主循环: " + Math.Max(1, fullAutoCycleCount) + "；子流程循环: " + loopCount;
            }
            return "循环: " + loopCount;
        }

        private static bool LooksLikeActionSequence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.IndexOf("->", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf(" x", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Down x", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Esc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Enter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Backspace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("等待", StringComparison.Ordinal) >= 0;
        }

        private string TaskModePrefix()
        {
            if (task == AutomationTask.DeleteVehicles) return "删车/";
            if (task == AutomationTask.FullAuto) return "全自动/";
            return "";
        }

        private string GridCalibrationSummary()
        {
            if (!grid.Locked) return "missing manual cell";
            if (grid.WindowScaled)
            {
                return string.Format(CultureInfo.InvariantCulture, "window bound scale=({0:0.000},{1:0.000})", grid.WindowScaleX, grid.WindowScaleY);
            }
            return "manual locked";
        }

        private string GridSummary()
        {
            if (!grid.Ready) return "未建立";
            return string.Format("{0}x{1}, step=({2:0},{3:0})", grid.Rows, grid.VisibleColumns, grid.CellStepX, grid.CellStepY);
        }

        private sealed class OcrFieldGroup
        {
            public string Label;
            public List<OcrMatch> Matches;
            public OcrFieldGroup(string label, List<OcrMatch> matches)
            {
                Label = label;
                Matches = matches;
            }
        }
    }
}
