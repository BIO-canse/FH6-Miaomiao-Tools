using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed partial class Runtime
    {
        private readonly List<UiCacheGuardRun> uiCacheGuards = new List<UiCacheGuardRun>();
        private int uiCacheGuardIndex;

        private UiCacheGuardRun StartUiCacheOcrGuard(string label, string text)
        {
            if (!SharedUiClickCacheAllowed()) return null;
            PollUiCacheOcrGuards();

            string exePath = ResolveBinPath(FH6AutomationConstants.Files.UiCacheGuardExe);
            string stateDir = Path.Combine(config.BaseDir, "state");
            Directory.CreateDirectory(stateDir);

            uiCacheGuardIndex++;
            string safeName = SafeGuardFilePart(label) + "-" + uiCacheGuardIndex.ToString("000", CultureInfo.InvariantCulture);
            string resultFile = Path.Combine(stateDir, "ui-cache-guard-" + safeName + ".result.txt");
            string capturedFile = Path.Combine(stateDir, "ui-cache-guard-" + safeName + ".captured");
            DeleteFileIfExists(resultFile);
            DeleteFileIfExists(capturedFile);

            List<string> args = new List<string>();
            args.Add("--config");
            args.Add(config.SourcePath);
            args.Add("--text");
            args.Add(text);
            args.Add("--label");
            args.Add(label);
            args.Add("--result-file");
            args.Add(resultFile);
            args.Add("--captured-file");
            args.Add(capturedFile);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.WorkingDirectory = config.BaseDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Arguments = JoinArgs(args);

            overlay.HideForCapture(config.OverlayHideBeforeCaptureMs);
            try
            {
                Process process = Process.Start(psi);
                UiCacheGuardRun run = new UiCacheGuardRun();
                run.Label = label;
                run.Text = text;
                run.ResultFile = resultFile;
                run.CapturedFile = capturedFile;
                run.Process = process;
                run.StartedUtc = DateTime.UtcNow;
                uiCacheGuards.Add(run);

                SetOcrSummary("UI缓存保险OCR已启动: " + label + " pid=" + process.Id);
                WaitForUiCacheGuardCapture(run);
                return run;
            }
            finally
            {
                overlay.ShowOverlay();
            }
        }

        private void WaitForUiCacheGuardCapture(UiCacheGuardRun run)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < FH6AutomationConstants.Timing.UiCacheGuardCaptureWaitMs)
            {
                if (File.Exists(run.CapturedFile)) return;
                if (run.Process.HasExited)
                {
                    PollUiCacheOcrGuards();
                    return;
                }
                ThrowIfFullAutoSafeStopWithoutChild();
                Thread.Sleep(FH6AutomationConstants.Timing.SleepSliceMs);
            }

            SetOcrSummary("UI缓存保险OCR: " + run.Label + " 截图标记未及时返回，继续主流程并后台等待结果");
        }

        private void WaitForUiCacheGuardCompletion(UiCacheGuardRun run)
        {
            if (run == null) return;
            SetOcrSummary("UI缓存保险OCR等待结果: " + run.Label);
            while (uiCacheGuards.Contains(run))
            {
                if (run.Process == null || run.Process.HasExited)
                {
                    PollUiCacheOcrGuards();
                    continue;
                }

                ThrowIfFullAutoSafeStopWithoutChild();
                Thread.Sleep(FH6AutomationConstants.Timing.ChildProcessPollMs);
            }
        }

        private void PollUiCacheOcrGuards()
        {
            for (int i = uiCacheGuards.Count - 1; i >= 0; i--)
            {
                UiCacheGuardRun run = uiCacheGuards[i];
                if (run.Process == null) 
                {
                    uiCacheGuards.RemoveAt(i);
                    continue;
                }
                if (!run.Process.HasExited) continue;

                int exitCode = run.Process.ExitCode;
                run.Process.Dispose();
                uiCacheGuards.RemoveAt(i);

                string result = ReadGuardResult(run.ResultFile);
                if (exitCode == 0)
                {
                    SetOcrSummary("UI缓存保险OCR通过: " + run.Label);
                    continue;
                }

                FH6FailureLog.Write("Runtime.UiCacheOcrGuard." + run.Label, result);
                throw new InvalidOperationException("UI缓存保险OCR失败，说明前置流程可能出错。label=" + run.Label + " text=" + run.Text + " exit=" + exitCode + "\r\n" + result);
            }
        }

        private void KillUiCacheOcrGuards()
        {
            foreach (UiCacheGuardRun run in uiCacheGuards)
            {
                try
                {
                    if (run.Process != null && !run.Process.HasExited) run.Process.Kill();
                }
                catch
                {
                }
                finally
                {
                    try { if (run.Process != null) run.Process.Dispose(); } catch { }
                }
            }
            uiCacheGuards.Clear();
        }

        private string ReadGuardResult(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return "读取 UI 缓存保险 OCR 结果失败: " + ex;
            }
            return "UI 缓存保险 OCR 没有写出结果文件。";
        }

        private static string SafeGuardFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "guard";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }
            if (sb.Length == 0) return "guard";
            if (sb.Length > 48) return sb.ToString(0, 48);
            return sb.ToString();
        }

        private sealed class UiCacheGuardRun
        {
            public string Label;
            public string Text;
            public string ResultFile;
            public string CapturedFile;
            public Process Process;
            public DateTime StartedUtc;
        }
    }
}
