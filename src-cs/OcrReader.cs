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
    internal sealed class OcrReader : IDisposable
    {
        private readonly Config config;
        private readonly bool useMediaOcr;
        private readonly bool usePaddleOcr;
        private readonly bool useRapidOcr;
        private readonly JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
        private readonly string tesseractPath;
        private readonly string tesseractDir;
        private readonly string tempDir;
        private readonly string paddleOcrPython;
        private readonly string paddleOcrBridge;
        private readonly string mediaOcrPowerShell;
        private readonly string mediaOcrBridge;
        private readonly string rapidOcrPython;
        private readonly string rapidOcrBridge;
        private readonly string debugImageDir;
        private readonly string diagnosticsDir;
        private readonly Func<bool> shouldStop;
        private readonly Action pollSafeStop;
        private Process paddleOcrProcess;
        private Process mediaOcrProcess;
        private Process rapidOcrProcess;
        private readonly StringBuilder paddleOcrErrors = new StringBuilder();
        private readonly StringBuilder mediaOcrErrors = new StringBuilder();
        private readonly StringBuilder rapidOcrErrors = new StringBuilder();
        private static readonly object MediaOcrValidationLock = new object();

        public bool IsMediaOcr
        {
            get { return useMediaOcr; }
        }

        public OcrReader(Config config, string debugImageDir)
            : this(config, debugImageDir, null, null)
        {
        }

        public static void Preflight(Config config)
        {
            using (OcrReader reader = new OcrReader(config, null))
            {
                reader.RunLiveOcrSelfCheck();
            }
        }

        public OcrReader(Config config, string debugImageDir, Func<bool> shouldStop, Action pollSafeStop)
        {
            this.config = config;
            this.debugImageDir = debugImageDir;
            diagnosticsDir = config.ResolvePath(config.DebugDir);
            this.shouldStop = shouldStop;
            this.pollSafeStop = pollSafeStop;
            jsonSerializer.MaxJsonLength = int.MaxValue;
            useMediaOcr = string.Equals(config.OcrEngine, "mediaocr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(config.OcrEngine, "windowsmediaocr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(config.OcrEngine, "windowsocr", StringComparison.OrdinalIgnoreCase);
            usePaddleOcr = string.Equals(config.OcrEngine, "paddleocr", StringComparison.OrdinalIgnoreCase);
            useRapidOcr = string.Equals(config.OcrEngine, "rapidocr", StringComparison.OrdinalIgnoreCase);
            paddleOcrPython = ResolvePython(config, config.PaddleOcrPython, "FH6_PADDLEOCR_PYTHON");
            paddleOcrBridge = config.ResolvePath(config.PaddleOcrBridge);
            mediaOcrPowerShell = ResolvePowerShell(config, config.MediaOcrPowerShell, "FH6_MEDIAOCR_POWERSHELL");
            mediaOcrBridge = config.ResolvePath(config.MediaOcrBridge);
            rapidOcrPython = ResolvePython(config, config.RapidOcrPython, "FH6_RAPIDOCR_PYTHON");
            rapidOcrBridge = config.ResolvePath(config.RapidOcrBridge);
            tesseractPath = config.ResolvePath(config.TesseractCmd);
            tesseractDir = Path.GetDirectoryName(tesseractPath);
            if (useMediaOcr)
            {
                ValidateMediaOcrRuntime();
            }
            else if (usePaddleOcr)
            {
                if (!File.Exists(paddleOcrPython)) throw new FileNotFoundException("找不到 PaddleOCR Python", paddleOcrPython);
                if (!File.Exists(paddleOcrBridge)) throw new FileNotFoundException("找不到 PaddleOCR bridge", paddleOcrBridge);
                ValidatePaddleRuntime();
            }
            else if (useRapidOcr)
            {
                if (!File.Exists(rapidOcrPython)) throw new FileNotFoundException("找不到 RapidOCR Python", rapidOcrPython);
                if (!File.Exists(rapidOcrBridge)) throw new FileNotFoundException("找不到 RapidOCR bridge", rapidOcrBridge);
            }
            else if (!File.Exists(tesseractPath))
            {
                throw new FileNotFoundException("找不到 tesseract.exe", tesseractPath);
            }
            tempDir = Path.Combine(Path.GetTempPath(), "FH6SkillPointOcr", "ocr-temp");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(diagnosticsDir);
            if (!string.IsNullOrWhiteSpace(debugImageDir)) Directory.CreateDirectory(debugImageDir);
        }

        private void ValidatePaddleRuntime()
        {
            ValidateBundledPythonRuntime();
            string site = config.ResolvePath(Path.Combine("runtime", "paddleocr-py"));
            string det = config.ResolvePath(Path.Combine("runtime", "paddleocr-models", "PP-OCRv5_mobile_det", "inference.pdiparams"));
            string rec = config.ResolvePath(Path.Combine("runtime", "paddleocr-models", "PP-OCRv5_mobile_rec", "inference.pdiparams"));
            string paddleLib = config.ResolvePath(Path.Combine("runtime", "paddleocr-py", "paddle", "base", "libpaddle.pyd"));
            if (!Directory.Exists(site)) throw new DirectoryNotFoundException("找不到 PaddleOCR 依赖目录：" + site + "\r\n请完整解压发布包，不要只复制 exe。");
            if (!File.Exists(paddleLib)) throw new FileNotFoundException("找不到 PaddlePaddle 核心库，请完整解压发布包或检查杀毒软件是否隔离文件。", paddleLib);
            if (!File.Exists(det)) throw new FileNotFoundException("找不到 PaddleOCR 检测模型，请下载完整新版压缩包并完整解压。", det);
            if (!File.Exists(rec)) throw new FileNotFoundException("找不到 PaddleOCR 识别模型，请下载完整新版压缩包并完整解压。", rec);
            RunPaddleDependencySelfCheck(site, det, rec, paddleLib);
        }

        private void ValidateBundledPythonRuntime()
        {
            string localPython = Path.Combine(config.BaseDir, "runtime", "python", "python.exe");
            if (!string.Equals(Path.GetFullPath(paddleOcrPython), Path.GetFullPath(localPython), StringComparison.OrdinalIgnoreCase)) return;

            string pythonDll = Path.Combine(config.BaseDir, "runtime", "python", "python312.dll");
            string vcRuntime = Path.Combine(config.BaseDir, "runtime", "python", "vcruntime140.dll");
            string vcRuntimeExtra = Path.Combine(config.BaseDir, "runtime", "python", "vcruntime140_1.dll");
            if (!File.Exists(pythonDll)) throw new FileNotFoundException("包内 Python 不完整，缺少 python312.dll。请完整解压发布包。", pythonDll);
            if (!File.Exists(vcRuntime)) throw new FileNotFoundException("包内 Python 不完整，缺少 vcruntime140.dll。请完整解压发布包。", vcRuntime);
            if (!File.Exists(vcRuntimeExtra)) throw new FileNotFoundException("包内 Python 不完整，缺少 vcruntime140_1.dll。请完整解压发布包。", vcRuntimeExtra);
        }

        private void ValidateMediaOcrRuntime()
        {
            lock (MediaOcrValidationLock)
            {
                ValidateMediaOcrRuntimeLocked();
            }
        }

        private void ValidateMediaOcrRuntimeLocked()
        {
            string reportPath = Path.Combine(diagnosticsDir, "mediaocr-dependency-check-last.txt");
            string stampPath = Path.Combine(diagnosticsDir, "mediaocr-dependency-check-ok.txt");
            string signature = BuildMediaOcrDependencySignature();

            try
            {
                Directory.CreateDirectory(diagnosticsDir);
                bool forceCheck = string.Equals(Environment.GetEnvironmentVariable("FH6_FORCE_OCR_DEPENDENCY_CHECK"), "1", StringComparison.OrdinalIgnoreCase);
                if (!forceCheck && IsPaddleDependencyStampCurrent(stampPath, signature))
                {
                    if (!File.Exists(reportPath))
                    {
                        File.WriteAllText(
                            reportPath,
                            BuildDependencyReportHeader("MediaOCR dependency self-check skipped: matching success stamp.") +
                            "powershell=" + mediaOcrPowerShell + "\r\n" +
                            "powershell_exists=" + File.Exists(mediaOcrPowerShell) + "\r\n" +
                            "bridge=" + mediaOcrBridge + "\r\n" +
                            "bridge_exists=" + File.Exists(mediaOcrBridge) + "\r\n" +
                            "signature=" + signature + "\r\n",
                            Encoding.UTF8);
                    }
                    return;
                }

                if (!File.Exists(mediaOcrBridge)) throw new FileNotFoundException("找不到 MediaOCR bridge", mediaOcrBridge);

                ProcessStartInfo psi = CreateMediaOcrProcessInfo("-SelfCheck");
                using (Process process = Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                    WaitForProcessExitWithHotkeys(process, FH6AutomationConstants.Ocr.BridgeInitTimeoutMs, "MediaOCR 依赖自检超时");
                    string stdout = stdoutTask.Result;
                    string stderr = stderrTask.Result;
                    string report =
                        BuildDependencyReportHeader("MediaOCR dependency self-check") +
                        "powershell=" + mediaOcrPowerShell + "\r\n" +
                        "powershell_exists=" + File.Exists(mediaOcrPowerShell) + "\r\n" +
                        "bridge=" + mediaOcrBridge + "\r\n" +
                        "bridge_exists=" + File.Exists(mediaOcrBridge) + "\r\n" +
                        "configured_languages=" + config.OcrLanguages + "\r\n" +
                        "ocr_scale=" + config.OcrScale.ToString("0.###", CultureInfo.InvariantCulture) + "\r\n" +
                        "signature=" + signature + "\r\n" +
                        "exit_code=" + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "\r\n\r\nSTDOUT:\r\n" + stdout + "\r\nSTDERR:\r\n" + stderr;
                    File.WriteAllText(reportPath, report, Encoding.UTF8);
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("MediaOCR 依赖自检失败，已写入 " + reportPath + "。当前系统可能缺少 Windows 中文/英文 OCR 语言包，或 PowerShell/WinRT OCR 不可用。");
                    }
                    File.WriteAllText(stampPath, signature, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                FH6FailureLog.Write("OcrReader.MediaOcrDependencyCheck", ex);
                if (ex is InvalidOperationException) throw;
                throw new InvalidOperationException("MediaOCR 自检无法完成。请确认系统支持 Windows.Media.Ocr，或下载 PaddleOCR 版。日志：" + reportPath, ex);
            }
        }

        private string BuildMediaOcrDependencySignature()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("engine=mediaocr|");
            sb.Append("base=").Append(config.BaseDir).Append("|");
            sb.Append("ps=").Append(mediaOcrPowerShell).Append("|");
            sb.Append("bridge=").Append(mediaOcrBridge).Append("|");
            sb.Append("bridge_mtime=").Append(File.Exists(mediaOcrBridge) ? File.GetLastWriteTimeUtc(mediaOcrBridge).Ticks.ToString(CultureInfo.InvariantCulture) : "missing").Append("|");
            sb.Append("languages=").Append(config.OcrLanguages).Append("|");
            sb.Append("scale=").Append(config.OcrScale.ToString("0.###", CultureInfo.InvariantCulture)).Append("|");
            sb.Append("os=").Append(Environment.OSVersion).Append("|");
            sb.Append("is64=").Append(Environment.Is64BitOperatingSystem);
            return sb.ToString();
        }

        private void RunPaddleDependencySelfCheck(string site, string det, string rec, string paddleLib)
        {
            string reportPath = Path.Combine(diagnosticsDir, "ocr-dependency-check-last.txt");
            string stampPath = Path.Combine(diagnosticsDir, "ocr-dependency-check-ok.txt");
            string signature = BuildPaddleDependencySignature(site, det, rec, paddleLib);

            try
            {
                Directory.CreateDirectory(diagnosticsDir);
                bool forceCheck = string.Equals(Environment.GetEnvironmentVariable("FH6_FORCE_OCR_DEPENDENCY_CHECK"), "1", StringComparison.OrdinalIgnoreCase);
                if (!forceCheck && IsPaddleDependencyStampCurrent(stampPath, signature))
                {
                    if (!File.Exists(reportPath))
                    {
                        File.WriteAllText(
                            reportPath,
                            BuildDependencyReportHeader("PaddleOCR dependency self-check skipped: matching success stamp.") +
                            "python=" + paddleOcrPython + "\r\n" +
                            "python_exists=" + File.Exists(paddleOcrPython) + "\r\n" +
                            "bridge=" + paddleOcrBridge + "\r\n" +
                            "bridge_exists=" + File.Exists(paddleOcrBridge) + "\r\n" +
                            "signature=" + signature + "\r\n" +
                            "stamp=" + stampPath + "\r\n",
                            Encoding.UTF8);
                    }
                    return;
                }

                ProcessStartInfo psi = CreateBridgeProcessInfo(paddleOcrPython, paddleOcrBridge, "--self-check");
                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();

                    System.Threading.Tasks.Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                    WaitForProcessExitWithHotkeys(process, FH6AutomationConstants.Ocr.BridgeInitTimeoutMs, "PaddleOCR 依赖自检超时");

                    string stdout = stdoutTask.Result ?? "";
                    string stderr = stderrTask.Result ?? "";
                    string exitDescription = DescribeNativeExitCode(process.ExitCode);
                    string report =
                        BuildDependencyReportHeader("PaddleOCR dependency self-check") +
                        "python=" + paddleOcrPython + "\r\n" +
                        "python_exists=" + File.Exists(paddleOcrPython) + "\r\n" +
                        "bridge=" + paddleOcrBridge + "\r\n" +
                        "bridge_exists=" + File.Exists(paddleOcrBridge) + "\r\n" +
                        "signature=" + signature + "\r\n" +
                        "exit_code=" + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                        "exit_description=" + exitDescription + "\r\n" +
                        "\r\nSTDOUT:\r\n" + stdout + "\r\n" +
                        "\r\nSTDERR:\r\n" + stderr + "\r\n";
                    File.WriteAllText(reportPath, report, Encoding.UTF8);

                    bool ok = process.ExitCode == 0 && SelfCheckJsonOk(stdout);
                    if (!ok)
                    {
                        throw new InvalidOperationException("PaddleOCR 依赖自检失败，已写入 " + reportPath + "。" + OcrCrashHint(process.ExitCode, stderr, stdout));
                    }

                    File.WriteAllText(
                        stampPath,
                        "signature=" + signature + "\r\n" +
                        "checked_at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "\r\n",
                        Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                FH6FailureLog.Write("OcrReader.PaddleDependencySelfCheck", ex);
                if (ex is InvalidOperationException) throw;
                throw new InvalidOperationException("PaddleOCR 依赖自检无法完成，已写入 debug/last-error.txt。请确认完整解压发布包，并检查杀毒软件是否拦截 runtime 目录。", ex);
            }
        }

        private bool SelfCheckJsonOk(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout)) return false;
            string line = stdout.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(line)) return false;
            try
            {
                Dictionary<string, object> root = jsonSerializer.Deserialize<Dictionary<string, object>>(line);
                object codeValue;
                return root.TryGetValue("code", out codeValue) && Convert.ToInt32(codeValue, CultureInfo.InvariantCulture) == 0;
            }
            catch
            {
                return false;
            }
        }

        private string BuildDependencyReportHeader(string title)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("title=" + title);
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.AppendLine("base_dir=" + config.BaseDir);
            sb.AppendLine("current_dir=" + Environment.CurrentDirectory);
            sb.AppendLine("process_64bit=" + Environment.Is64BitProcess);
            sb.AppendLine("os_64bit=" + Environment.Is64BitOperatingSystem);
            sb.AppendLine("os_version=" + Environment.OSVersion);
            sb.AppendLine("dotnet=" + Environment.Version);
            return sb.ToString();
        }

        private string BuildPaddleDependencySignature(string site, string det, string rec, string paddleLib)
        {
            return string.Join(
                "|",
                new[]
                {
                    "machine=" + Environment.MachineName,
                    "os=" + Environment.OSVersion,
                    "base=" + config.BaseDir,
                    FileSignature(paddleOcrPython),
                    FileSignature(paddleOcrBridge),
                    DirectorySignature(site),
                    FileSignature(det),
                    FileSignature(rec),
                    FileSignature(paddleLib),
                    FileSignature(Path.Combine(config.BaseDir, "runtime", "python", "python312.dll")),
                    FileSignature(Path.Combine(config.BaseDir, "runtime", "python", "vcruntime140.dll")),
                    FileSignature(Path.Combine(config.BaseDir, "runtime", "python", "vcruntime140_1.dll"))
                });
        }

        private static string FileSignature(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) return path + "=missing";
                return path + "=" + info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return path + "=error";
            }
        }

        private static string DirectorySignature(string path)
        {
            try
            {
                DirectoryInfo info = new DirectoryInfo(path);
                if (!info.Exists) return path + "=missing";
                return path + "=dir:" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return path + "=error";
            }
        }

        private static bool IsPaddleDependencyStampCurrent(string stampPath, string signature)
        {
            try
            {
                if (!File.Exists(stampPath)) return false;
                string text = File.ReadAllText(stampPath, Encoding.UTF8);
                return text.Contains("signature=" + signature);
            }
            catch
            {
                return false;
            }
        }

        private static string OcrCrashHint(int exitCode, string stderr, string stdout)
        {
            string native = DescribeNativeExitCode(exitCode);
            string text = ((stderr ?? "") + "\n" + (stdout ?? "")).ToLowerInvariant();
            if (text.Contains("dll load failed") || text.Contains("找不到指定的模块"))
            {
                native += "；日志中出现 DLL load failed，通常是 VC++ 运行库、包内 DLL、Paddle 原生依赖缺失或被杀毒软件隔离。";
            }
            if (text.Contains("no module named"))
            {
                native += "；日志中出现 no module named，通常是没有完整解压 runtime/paddleocr-py。";
            }
            return string.IsNullOrWhiteSpace(native) ? "" : "\r\n" + native;
        }

        private static string DescribeNativeExitCode(int exitCode)
        {
            uint code = unchecked((uint)exitCode);
            if (code == 0) return "正常退出";
            if (code == 0xC0000135) return "Windows 原生加载失败：缺少 DLL 或 DLL 被拦截，常见于 VC++ 运行库/包内 DLL 缺失。";
            if (code == 0xC000007B) return "Windows 原生加载失败：32/64 位 DLL 不匹配或 DLL 损坏。";
            if (code == 0xC0000005) return "Windows 原生库访问冲突：可能与 PaddlePaddle 原生库、显卡驱动或底层运行库有关。";
            return "0x" + code.ToString("X8", CultureInfo.InvariantCulture);
        }

        private void RunLiveOcrSelfCheck()
        {
            string engineName = CurrentEngineName();
            string reportPath = Path.Combine(diagnosticsDir, "ocr-live-check-last.txt");
            try
            {
                Directory.CreateDirectory(diagnosticsDir);
                using (Bitmap bitmap = CreateLiveCheckBitmap())
                {
                    Screenshot screenshot = new Screenshot(bitmap, 0, 0);
                    OcrSnapshot snapshot = Read(screenshot, config.OcrPsm, "ocr-live-check");
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("title=OCR live self-check");
                    sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                    sb.AppendLine("engine=" + engineName);
                    sb.AppendLine("word_count=" + (snapshot.Words == null ? 0 : snapshot.Words.Count).ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("line_count=" + (snapshot.Lines == null ? 0 : snapshot.Lines.Count).ToString(CultureInfo.InvariantCulture));
                    AppendTextSection(sb, "engine_diagnostics", snapshot.EngineDiagnostics, 6000);
                    AppendTextSection(sb, "ocr_stderr", snapshot.ErrorOutput, 12000);
                    AppendTextSection(sb, "raw_ocr_response", snapshot.RawResponse, 120000);
                    File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                string body =
                    "title=OCR live self-check failed\r\n" +
                    "time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "\r\n" +
                    "engine=" + engineName + "\r\n" +
                    "base_dir=" + config.BaseDir + "\r\n" +
                    "error=\r\n" + ex + "\r\n";
                try { File.WriteAllText(reportPath, body, Encoding.UTF8); } catch { }
                FH6FailureLog.Write("OcrReader.LiveSelfCheck", ex);
                throw new InvalidOperationException(engineName + " 运行自检失败，已提前拦截。请查看 " + reportPath + " 和 debug/last-error.txt。", ex);
            }
        }

        private static Bitmap CreateLiveCheckBitmap()
        {
            Bitmap bitmap = new Bitmap(520, 160);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (Font font = new Font("Microsoft YaHei UI", 34, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(Color.Black))
                {
                    graphics.DrawString("斯巴鲁 OCR 123", font, brush, 18, 24);
                    graphics.DrawString("IMPREZA 22B-STI", font, brush, 18, 82);
                }
            }
            return bitmap;
        }

        private string CurrentEngineName()
        {
            if (useMediaOcr) return "MediaOCR";
            if (usePaddleOcr) return "PaddleOCR";
            if (useRapidOcr) return "RapidOCR";
            return "Tesseract";
        }

        private static void AppendTextSection(StringBuilder sb, string title, string body, int maxChars)
        {
            sb.AppendLine("---- " + title + " ----");
            if (string.IsNullOrEmpty(body))
            {
                sb.AppendLine("(empty)");
                return;
            }
            if (body.Length <= maxChars)
            {
                sb.AppendLine(body);
                return;
            }
            sb.AppendLine(body.Substring(0, maxChars));
            sb.AppendLine("...(truncated)");
        }

        public OcrSnapshot Read(Screenshot screenshot)
        {
            return Read(screenshot, config.OcrPsm, null);
        }

        public OcrSnapshot Read(Screenshot screenshot, int psm)
        {
            return Read(screenshot, psm, null);
        }

        public OcrSnapshot Read(Screenshot screenshot, int psm, string debugLabel)
        {
            if (useMediaOcr) return ReadMediaOcr(screenshot, debugLabel);
            if (usePaddleOcr) return ReadPaddleOcr(screenshot, debugLabel);
            if (useRapidOcr) return ReadRapidOcr(screenshot, debugLabel);
            return ReadTesseract(screenshot, psm, debugLabel);
        }

        private OcrSnapshot ReadMediaOcr(Screenshot screenshot, string debugLabel)
        {
            EnsureMediaOcrProcess();
            OcrEncodedImage image = EncodeOcrImage(screenshot, debugLabel);
            WriteBridgeRequest(mediaOcrProcess, "{\"image_base64\":\"" + image.Base64 + "\"}", "MediaOCR", mediaOcrErrors);

            string line = ReadBridgeLineWithTimeout(
                mediaOcrProcess,
                FH6AutomationConstants.Ocr.BridgeRequestTimeoutMs,
                "MediaOCR 识别超时",
                mediaOcrErrors,
                "MediaOCR");
            if (line == null)
            {
                throw new InvalidOperationException("MediaOCR 进程没有返回结果。" + MediaOcrErrorSuffix());
            }

            SaveDebugText(debugLabel, "ocr-response", line);
            OcrSnapshot snapshot = ParseOcrJson(line, screenshot, image.Scale, "MediaOCR", MediaOcrErrorSuffix());
            AttachBridgeDiagnostics(snapshot, "MediaOCR", line, mediaOcrProcess, mediaOcrPowerShell, mediaOcrBridge, mediaOcrErrors, Path.Combine(diagnosticsDir, "mediaocr-dependency-check-last.txt"));
            return snapshot;
        }

        private OcrSnapshot ReadTesseract(Screenshot screenshot, int psm, string debugLabel)
        {
            string tempImage = Path.Combine(tempDir, "ocr-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (Bitmap processed = Preprocess(screenshot.Image))
                {
                    SaveDebugImage(processed, debugLabel, "ocr-input");
                    processed.Save(tempImage, ImageFormat.Png);
                }

                string tsv = RunTesseract(tempImage, psm);
                OcrSnapshot snapshot = ParseTsv(tsv, screenshot);
                snapshot.EngineName = "Tesseract";
                snapshot.RawResponse = tsv;
                snapshot.EngineDiagnostics = "engine=Tesseract\r\ntesseract_path=" + tesseractPath + "\r\ntesseract_dir=" + tesseractDir;
                return snapshot;
            }
            finally
            {
                try { if (File.Exists(tempImage)) File.Delete(tempImage); } catch { }
            }
        }

        private OcrSnapshot ReadPaddleOcr(Screenshot screenshot, string debugLabel)
        {
            EnsurePaddleOcrProcess();
            OcrEncodedImage image = EncodeOcrImage(screenshot, debugLabel);
            WriteBridgeRequest(paddleOcrProcess, "{\"image_base64\":\"" + image.Base64 + "\"}", "PaddleOCR", paddleOcrErrors);

            string line = ReadBridgeLineWithTimeout(
                paddleOcrProcess,
                FH6AutomationConstants.Ocr.BridgeRequestTimeoutMs,
                "PaddleOCR 识别超时",
                paddleOcrErrors,
                "PaddleOCR");
            if (line == null)
            {
                throw new InvalidOperationException("PaddleOCR 进程没有返回结果。" + PaddleOcrErrorSuffix());
            }

            SaveDebugText(debugLabel, "ocr-response", line);
            OcrSnapshot snapshot = ParseOcrJson(line, screenshot, image.Scale, "PaddleOCR", PaddleOcrErrorSuffix());
            AttachBridgeDiagnostics(snapshot, "PaddleOCR", line, paddleOcrProcess, paddleOcrPython, paddleOcrBridge, paddleOcrErrors);
            return snapshot;
        }

        private OcrSnapshot ReadRapidOcr(Screenshot screenshot, string debugLabel)
        {
            EnsureRapidOcrProcess();
            OcrEncodedImage image = EncodeOcrImage(screenshot, debugLabel);
            WriteBridgeRequest(rapidOcrProcess, "{\"image_base64\":\"" + image.Base64 + "\"}", "RapidOCR", rapidOcrErrors);

            string line = ReadBridgeLineWithTimeout(
                rapidOcrProcess,
                FH6AutomationConstants.Ocr.BridgeRequestTimeoutMs,
                "RapidOCR 识别超时",
                rapidOcrErrors,
                "RapidOCR");
            if (line == null)
            {
                throw new InvalidOperationException("RapidOCR 进程没有返回结果。" + RapidOcrErrorSuffix());
            }

            SaveDebugText(debugLabel, "ocr-response", line);
            OcrSnapshot snapshot = ParseOcrJson(line, screenshot, image.Scale, "RapidOCR", RapidOcrErrorSuffix());
            AttachBridgeDiagnostics(snapshot, "RapidOCR", line, rapidOcrProcess, rapidOcrPython, rapidOcrBridge, rapidOcrErrors);
            return snapshot;
        }

        private OcrEncodedImage EncodeOcrImage(Screenshot screenshot, string debugLabel)
        {
            string imageBase64;
            double actualScale;
            using (MemoryStream ms = new MemoryStream())
            {
                using (Bitmap processed = PreprocessBridgeOcr(screenshot.Image, out actualScale))
                {
                    SaveDebugImage(processed, debugLabel, "ocr-input");
                    processed.Save(ms, ImageFormat.Png);
                }
                imageBase64 = Convert.ToBase64String(ms.ToArray());
            }
            return new OcrEncodedImage(imageBase64, actualScale);
        }

        private void SaveDebugImage(Bitmap image, string debugLabel, string suffix)
        {
            if (string.IsNullOrWhiteSpace(debugImageDir) || string.IsNullOrWhiteSpace(debugLabel) || image == null) return;
            try
            {
                Directory.CreateDirectory(debugImageDir);
                image.Save(Path.Combine(debugImageDir, debugLabel + "-" + suffix + ".png"), ImageFormat.Png);
            }
            catch (Exception ex)
            {
                FH6FailureLog.Write("OcrReader.SaveDebugImage", ex);
            }
        }

        private void SaveDebugText(string debugLabel, string suffix, string body)
        {
            if (string.IsNullOrWhiteSpace(debugImageDir) || string.IsNullOrWhiteSpace(debugLabel)) return;
            try
            {
                Directory.CreateDirectory(debugImageDir);
                File.WriteAllText(Path.Combine(debugImageDir, debugLabel + "-" + suffix + ".json"), body ?? "", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                FH6FailureLog.Write("OcrReader.SaveDebugText", ex);
            }
        }

        public List<OcrMatch> Find(OcrSnapshot snapshot, string query)
        {
            List<OcrMatch> phrase = FindPhrase(snapshot.WordLines, query);
            if (phrase.Count > 0) return phrase;

            if (HasCjk(query))
            {
                string needle = NormalizeCjk(query);
                return PreferExactOrCompact(
                    snapshot.Lines.Where(line => NormalizeCjk(line.Text).Contains(needle)).ToList(),
                    needle,
                    true);
            }
            else
            {
                string needle = NormalizeLatin(query);
                return PreferExactOrCompact(
                    snapshot.Lines.Where(line => NormalizeLatin(line.Text).Contains(needle)).ToList(),
                    needle,
                    false);
            }
        }

        public List<OcrMatch> FindCjkFuzzy(OcrSnapshot snapshot, string query, int minCommonChars, int maxNormalizedLength)
        {
            string needle = NormalizeCjk(query);
            List<OcrMatch> candidates = new List<OcrMatch>();
            candidates.AddRange(snapshot.Words);
            candidates.AddRange(snapshot.Lines);

            List<OcrMatch> matches = new List<OcrMatch>();
            foreach (OcrMatch candidate in candidates)
            {
                string haystack = NormalizeCjk(candidate.Text);
                if (haystack.Length == 0 || haystack.Length > maxNormalizedLength) continue;
                if (haystack.Contains(needle) || (needle.Contains(haystack) && haystack.Length >= minCommonChars))
                {
                    matches.Add(candidate);
                    continue;
                }

                if (CommonCharCount(needle, haystack) >= minCommonChars)
                {
                    matches.Add(candidate);
                }
            }

            return PreferExactOrCompact(matches, needle, true);
        }

        public List<OcrMatch> FindLatinFuzzy(OcrSnapshot snapshot, string query, int maxDistance)
        {
            string needle = NormalizeLatin(query);
            if (needle.Length == 0) return new List<OcrMatch>();

            List<OcrMatch> candidates = new List<OcrMatch>();
            candidates.AddRange(snapshot.Words);
            candidates.AddRange(snapshot.Lines);

            List<OcrMatch> matches = new List<OcrMatch>();
            foreach (OcrMatch candidate in candidates)
            {
                string haystack = NormalizeLatin(candidate.Text);
                if (haystack.Length == 0) continue;
                if (haystack.Contains(needle) || FuzzyContains(haystack, needle, maxDistance))
                {
                    matches.Add(candidate);
                }
            }

            return PreferExactOrCompact(matches, needle, false);
        }

        private Bitmap Preprocess(Bitmap source)
        {
            int width = Math.Max(1, (int)Math.Round(source.Width * config.OcrScale));
            int height = Math.Max(1, (int)Math.Round(source.Height * config.OcrScale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            float contrast = 1.8f;
            float translate = 0.5f * (1.0f - contrast);
            ColorMatrix matrix = new ColorMatrix(new[]
            {
                new[] {0.299f * contrast, 0.299f * contrast, 0.299f * contrast, 0, 0},
                new[] {0.587f * contrast, 0.587f * contrast, 0.587f * contrast, 0, 0},
                new[] {0.114f * contrast, 0.114f * contrast, 0.114f * contrast, 0, 0},
                new[] {0f, 0f, 0f, 1f, 0f},
                new[] {translate, translate, translate, 0f, 1f}
            });
            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                attributes.SetColorMatrix(matrix);
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, width, height),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }
            return result;
        }

        private Bitmap PreprocessBridgeOcr(Bitmap source, out double actualScale)
        {
            double scale = 1.0;
            actualScale = scale;
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return result;
        }

        private sealed class OcrEncodedImage
        {
            public readonly string Base64;
            public readonly double Scale;

            public OcrEncodedImage(string base64, double scale)
            {
                Base64 = base64;
                Scale = scale;
            }
        }

        private string RunTesseract(string imagePath, int psm)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = tesseractPath;
            psi.WorkingDirectory = tesseractDir;
            psi.Arguments = "\"" + imagePath + "\" stdout --tessdata-dir tessdata -l " + config.OcrLanguages + " --psm " + psm + " tsv";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["TESSDATA_PREFIX"] = "tessdata";
            using (Process process = Process.Start(psi))
            {
                System.Threading.Tasks.Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                WaitForProcessExitWithHotkeys(process, FH6AutomationConstants.Ocr.BridgeRequestTimeoutMs, "Tesseract 执行超时");
                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;
                if (process.ExitCode != 0) throw new InvalidOperationException("Tesseract 执行失败：" + stderr);
                return stdout;
            }
        }

        private OcrSnapshot ParseTsv(string tsv, Screenshot screenshot)
        {
            Dictionary<string, List<OcrMatch>> grouped = new Dictionary<string, List<OcrMatch>>();
            List<OcrMatch> words = new List<OcrMatch>();
            string[] lines = tsv.Replace("\r\n", "\n").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] parts = lines[i].Split('\t');
                if (parts.Length < 12) continue;
                string text = parts[11].Trim();
                if (text.Length == 0) continue;
                double conf = ParseDouble(parts[10], -1);
                if (conf >= 0 && conf < config.OcrMinConfidence) continue;

                double left = ParseDouble(parts[6], 0) / config.OcrScale + screenshot.Left;
                double top = ParseDouble(parts[7], 0) / config.OcrScale + screenshot.Top;
                double right = left + ParseDouble(parts[8], 0) / config.OcrScale;
                double bottom = top + ParseDouble(parts[9], 0) / config.OcrScale;
                OcrMatch match = new OcrMatch(text, new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top)), conf);
                words.Add(match);
                string key = parts[2] + ":" + parts[3] + ":" + parts[4];
                if (!grouped.ContainsKey(key)) grouped[key] = new List<OcrMatch>();
                grouped[key].Add(match);
            }

            List<List<OcrMatch>> wordLines = grouped.Values.Select(list => list.OrderBy(w => w.Rect.Left).ToList()).ToList();
            List<OcrMatch> lineMatches = new List<OcrMatch>();
            foreach (List<OcrMatch> line in wordLines)
            {
                lineMatches.Add(MergeWords(line));
            }
            return new OcrSnapshot(screenshot, words, wordLines, lineMatches);
        }

        private List<OcrMatch> FindPhrase(List<List<OcrMatch>> wordLines, string query)
        {
            bool cjk = HasCjk(query);
            bool cjkOnlyPhrase = cjk && NormalizeCjkOnly(query).Length > 0 && NormalizeCjkOnly(query) == NormalizeCjk(query);
            string needle = cjkOnlyPhrase ? NormalizeCjkOnly(query) : (cjk ? NormalizeCjk(query) : NormalizeLatin(query));
            List<OcrMatch> matches = new List<OcrMatch>();
            foreach (List<OcrMatch> line in wordLines)
            {
                List<Tuple<string, OcrMatch>> normalized = new List<Tuple<string, OcrMatch>>();
                foreach (OcrMatch word in line)
                {
                    string text = cjkOnlyPhrase ? NormalizeCjkOnly(word.Text) : (cjk ? NormalizeCjk(word.Text) : NormalizeLatin(word.Text));
                    if (text.Length > 0) normalized.Add(Tuple.Create(text, word));
                }

                for (int start = 0; start < normalized.Count; start++)
                {
                    string combined = "";
                    List<Tuple<string, OcrMatch>> selected = new List<Tuple<string, OcrMatch>>();
                    for (int index = start; index < normalized.Count; index++)
                    {
                        combined += normalized[index].Item1;
                        selected.Add(normalized[index]);
                        if (combined.Contains(needle))
                        {
                            matches.Add(MergeWords(TrimPhraseSelection(selected, needle)));
                            break;
                        }
                        if (combined.Length > needle.Length + 12 && !combined.Contains(needle)) break;
                    }
                }
            }
            return PreferExactOrCompact(matches, needle, cjk);
        }

        private static List<OcrMatch> TrimPhraseSelection(List<Tuple<string, OcrMatch>> selected, string needle)
        {
            int left = 0;
            int right = selected.Count - 1;
            while (left < right && SpanTextContains(selected, left + 1, right, needle)) left++;
            while (right > left && SpanTextContains(selected, left, right - 1, needle)) right--;

            List<OcrMatch> result = new List<OcrMatch>();
            for (int i = left; i <= right; i++)
            {
                result.Add(selected[i].Item2);
            }
            return result;
        }

        private static bool SpanTextContains(List<Tuple<string, OcrMatch>> selected, int left, int right, string needle)
        {
            if (left > right) return false;
            StringBuilder sb = new StringBuilder();
            for (int i = left; i <= right; i++)
            {
                sb.Append(selected[i].Item1);
            }
            return sb.ToString().Contains(needle);
        }

        private static List<OcrMatch> PreferExactOrCompact(List<OcrMatch> matches, string normalizedNeedle, bool cjk)
        {
            List<OcrMatch> deduped = Dedupe(matches);
            if (deduped.Count <= 1) return deduped;

            List<OcrMatch> exact = deduped
                .Where(m => NormalizeForMode(m.Text, cjk) == normalizedNeedle)
                .ToList();
            if (exact.Count > 0) return exact;

            int allowance = cjk ? 2 : 4;
            List<OcrMatch> compact = deduped
                .Where(m => NormalizeForMode(m.Text, cjk).Length <= normalizedNeedle.Length + allowance)
                .ToList();
            if (compact.Count > 0) return compact;

            int minLength = deduped
                .Select(m => NormalizeForMode(m.Text, cjk).Length)
                .Where(length => length > 0)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            return deduped
                .Where(m => NormalizeForMode(m.Text, cjk).Length == minLength)
                .ToList();
        }

        private static string NormalizeForMode(string text, bool cjk)
        {
            return cjk ? NormalizeCjk(text) : NormalizeLatin(text);
        }

        private static List<OcrMatch> Dedupe(List<OcrMatch> matches)
        {
            List<OcrMatch> result = new List<OcrMatch>();
            HashSet<string> seen = new HashSet<string>();
            foreach (OcrMatch match in matches)
            {
                string key = string.Format("{0}:{1}:{2}:{3}", (int)Math.Round(match.Rect.Left / 3), (int)Math.Round(match.Rect.Top / 3), (int)Math.Round(match.Rect.Right / 3), (int)Math.Round(match.Rect.Bottom / 3));
                if (seen.Add(key)) result.Add(match);
            }
            return result;
        }

        private static OcrMatch MergeWords(List<OcrMatch> words)
        {
            float left = words.Min(w => w.Rect.Left);
            float top = words.Min(w => w.Rect.Top);
            float right = words.Max(w => w.Rect.Right);
            float bottom = words.Max(w => w.Rect.Bottom);
            double conf = words.Where(w => w.Confidence >= 0).Select(w => w.Confidence).DefaultIfEmpty(-1).Average();
            return new OcrMatch(string.Join(" ", words.Select(w => w.Text).ToArray()), new RectangleF(left, top, right - left, bottom - top), conf, MergeLanguage(words));
        }

        private static string MergeLanguage(List<OcrMatch> words)
        {
            List<string> languages = words
                .Select(w => w.Language ?? "")
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return languages.Count == 1 ? languages[0] : "";
        }

        private static double ParseDouble(string raw, double fallback)
        {
            double value;
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool HasCjk(string text)
        {
            return text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff');
        }

        private static string NormalizeCjk(string text)
        {
            string normalized = Regex.Replace(text ?? "", @"\s+", "");
            normalized = normalized.Replace("魯", "鲁");
            normalized = normalized.Replace("臺", "台");
            normalized = normalized.Replace("賓", "宾");
            return Regex.Replace(normalized, @"[^\u4e00-\u9fffA-Za-z0-9]", "");
        }

        private static string NormalizeCjkOnly(string text)
        {
            string normalized = Regex.Replace(text ?? "", @"\s+", "");
            normalized = normalized.Replace("魯", "鲁");
            normalized = normalized.Replace("臺", "台");
            normalized = normalized.Replace("賓", "宾");
            return Regex.Replace(normalized, @"[^\u4e00-\u9fff]", "");
        }

        private void EnsureMediaOcrProcess()
        {
            if (mediaOcrProcess != null && !mediaOcrProcess.HasExited) return;

            mediaOcrErrors.Length = 0;
            ProcessStartInfo psi = CreateMediaOcrProcessInfo("");

            mediaOcrProcess = new Process();
            mediaOcrProcess.StartInfo = psi;
            mediaOcrProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (mediaOcrErrors)
                    {
                        if (mediaOcrErrors.Length < 12000) mediaOcrErrors.AppendLine(e.Data);
                    }
                }
            };

            mediaOcrProcess.Start();
            mediaOcrProcess.BeginErrorReadLine();

            string ready = ReadBridgeLineWithTimeout(
                mediaOcrProcess,
                FH6AutomationConstants.Ocr.BridgeInitTimeoutMs,
                "MediaOCR 初始化超时",
                mediaOcrErrors,
                "MediaOCR");
            if (ready == null || !ready.Contains("\"ready\""))
            {
                throw new InvalidOperationException("MediaOCR 初始化失败。" + MediaOcrErrorSuffix());
            }
        }

        private void EnsurePaddleOcrProcess()
        {
            if (paddleOcrProcess != null && !paddleOcrProcess.HasExited) return;

            paddleOcrErrors.Length = 0;
            ProcessStartInfo psi = CreateBridgeProcessInfo(paddleOcrPython, paddleOcrBridge);

            paddleOcrProcess = new Process();
            paddleOcrProcess.StartInfo = psi;
            paddleOcrProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (paddleOcrErrors)
                    {
                        if (paddleOcrErrors.Length < 12000) paddleOcrErrors.AppendLine(e.Data);
                    }
                }
            };

            paddleOcrProcess.Start();
            paddleOcrProcess.BeginErrorReadLine();

            string ready = ReadBridgeLineWithTimeout(
                paddleOcrProcess,
                FH6AutomationConstants.Ocr.BridgeInitTimeoutMs,
                "PaddleOCR 初始化超时",
                paddleOcrErrors,
                "PaddleOCR");
            if (ready == null || !ready.Contains("\"ready\""))
            {
                throw new InvalidOperationException("PaddleOCR 初始化失败。" + PaddleOcrErrorSuffix());
            }
        }

        private void EnsureRapidOcrProcess()
        {
            if (rapidOcrProcess != null && !rapidOcrProcess.HasExited) return;

            rapidOcrErrors.Length = 0;
            ProcessStartInfo psi = CreateBridgeProcessInfo(rapidOcrPython, rapidOcrBridge);

            rapidOcrProcess = new Process();
            rapidOcrProcess.StartInfo = psi;
            rapidOcrProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (rapidOcrErrors)
                    {
                        if (rapidOcrErrors.Length < 8000) rapidOcrErrors.AppendLine(e.Data);
                    }
                }
            };

            rapidOcrProcess.Start();
            rapidOcrProcess.BeginErrorReadLine();

            string ready = ReadBridgeLineWithTimeout(
                rapidOcrProcess,
                FH6AutomationConstants.Ocr.BridgeInitTimeoutMs,
                "RapidOCR 初始化超时",
                rapidOcrErrors,
                "RapidOCR");
            if (ready == null || !ready.Contains("\"ready\""))
            {
                throw new InvalidOperationException("RapidOCR 初始化失败。" + RapidOcrErrorSuffix());
            }
        }

        private void WriteBridgeRequest(Process process, string request, string engineName, StringBuilder errors)
        {
            try
            {
                if (process == null)
                {
                    throw new InvalidOperationException(engineName + " OCR 进程不存在。");
                }
                if (process.HasExited)
                {
                    throw BuildBridgeExitedException(engineName + " OCR 请求前进程已退出", process, engineName, errors);
                }
                process.StandardInput.WriteLine(request);
                process.StandardInput.Flush();
            }
            catch (StopRequestedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException && ex.Message.IndexOf("OCR 请求前进程已退出", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw;
                }
                throw new InvalidOperationException(engineName + " OCR 请求写入失败，OCR 子进程可能已经崩溃。" + BridgeProcessStatus(process, engineName, errors), ex);
            }
        }

        private string ReadBridgeLineWithTimeout(Process process, int timeoutMs, string context, StringBuilder errors, string engineName)
        {
            System.Threading.Tasks.Task<string> readTask = process.StandardOutput.ReadLineAsync();
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                PollOcrWaitHotkeys(process);
                if (readTask.Wait(FH6AutomationConstants.Timing.SleepSliceMs))
                {
                    string line = readTask.Result;
                    if (line != null) return line;
                    if (process.HasExited)
                    {
                        throw BuildBridgeExitedException(context + "：OCR 子进程没有返回内容", process, engineName, errors);
                    }
                    return null;
                }
                if (process.HasExited)
                {
                    if (readTask.Wait(200) && !string.IsNullOrWhiteSpace(readTask.Result))
                    {
                        return readTask.Result;
                    }
                    throw BuildBridgeExitedException(context + "：OCR 子进程已退出", process, engineName, errors);
                }
                if (stopwatch.ElapsedMilliseconds < timeoutMs) continue;

                TryKillProcess(process);
                string stderr;
                lock (errors)
                {
                    stderr = errors.ToString();
                }
                string suffix = string.IsNullOrWhiteSpace(stderr) ? "" : "\r\nSTDERR:\r\n" + stderr;
                throw new TimeoutException(context + "，超过 " + (timeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " 秒。" + suffix);
            }
        }

        private Exception BuildBridgeExitedException(string context, Process process, string engineName, StringBuilder errors)
        {
            return new InvalidOperationException(context + "。" + BridgeProcessStatus(process, engineName, errors));
        }

        private string BridgeProcessStatus(Process process, string engineName, StringBuilder errors)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("OCR子进程状态：");
            sb.AppendLine("engine=" + engineName);
            if (process == null)
            {
                sb.AppendLine("process=null");
            }
            else
            {
                try { sb.AppendLine("process_id=" + process.Id.ToString(CultureInfo.InvariantCulture)); } catch { }
                bool hasExited = false;
                try
                {
                    hasExited = process.HasExited;
                    sb.AppendLine("process_has_exited=" + hasExited);
                }
                catch
                {
                    sb.AppendLine("process_has_exited=unknown");
                }
                if (hasExited)
                {
                    try
                    {
                        int exitCode = process.ExitCode;
                        sb.AppendLine("process_exit_code=" + exitCode.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("process_exit_description=" + DescribeNativeExitCode(exitCode));
                    }
                    catch
                    {
                    }
                }
            }

            string stderr;
            lock (errors)
            {
                stderr = errors == null ? "" : errors.ToString();
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine();
                sb.AppendLine(engineName + " stderr:");
                sb.AppendLine(stderr);
            }
            return sb.ToString();
        }

        private void WaitForProcessExitWithHotkeys(Process process, int timeoutMs, string context)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                PollOcrWaitHotkeys(process);
                if (process.WaitForExit(FH6AutomationConstants.Timing.SleepSliceMs)) return;
                if (stopwatch.ElapsedMilliseconds < timeoutMs) continue;

                TryKillProcess(process);
                throw new TimeoutException(context + "，超过 " + (timeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " 秒。");
            }
        }

        private void PollOcrWaitHotkeys(Process process)
        {
            if (shouldStop != null && shouldStop())
            {
                TryKillProcess(process);
                throw new StopRequestedException();
            }
            if (pollSafeStop != null) pollSafeStop();
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited) process.Kill();
            }
            catch
            {
            }
        }

        private ProcessStartInfo CreateBridgeProcessInfo(string pythonPath, string bridgePath)
        {
            return CreateBridgeProcessInfo(pythonPath, bridgePath, "");
        }

        private ProcessStartInfo CreateBridgeProcessInfo(string pythonPath, string bridgePath, string extraArguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = pythonPath;
            psi.WorkingDirectory = config.BaseDir;
            psi.Arguments = "\"" + bridgePath + "\"" + (string.IsNullOrWhiteSpace(extraArguments) ? "" : " " + extraArguments);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            PrependPythonPath(psi, config.ResolvePath(Path.Combine("runtime", "paddleocr-py")));
            PrependPythonPath(psi, config.ResolvePath(Path.Combine("runtime", "rapidocr-py")));
            return psi;
        }

        private ProcessStartInfo CreateMediaOcrProcessInfo(string extraArguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = mediaOcrPowerShell;
            psi.WorkingDirectory = config.BaseDir;
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + mediaOcrBridge + "\"" + (string.IsNullOrWhiteSpace(extraArguments) ? "" : " " + extraArguments);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["FH6_MEDIAOCR_LANGUAGES"] = config.OcrLanguages ?? "";
            psi.EnvironmentVariables["FH6_MEDIAOCR_SCALE"] = config.OcrScale.ToString("0.###", CultureInfo.InvariantCulture);
            return psi;
        }

        private void AttachBridgeDiagnostics(OcrSnapshot snapshot, string engineName, string rawResponse, Process process, string pythonPath, string bridgePath, StringBuilder errors)
        {
            AttachBridgeDiagnostics(snapshot, engineName, rawResponse, process, pythonPath, bridgePath, errors, Path.Combine(diagnosticsDir, "ocr-dependency-check-last.txt"));
        }

        private void AttachBridgeDiagnostics(OcrSnapshot snapshot, string engineName, string rawResponse, Process process, string executablePath, string bridgePath, StringBuilder errors, string dependencyReportPath)
        {
            if (snapshot == null) return;

            string stderr;
            lock (errors)
            {
                stderr = errors.ToString();
            }

            snapshot.EngineName = engineName;
            snapshot.RawResponse = rawResponse ?? "";
            snapshot.ErrorOutput = stderr ?? "";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("engine=" + engineName);
            sb.AppendLine("base_dir=" + config.BaseDir);
            sb.AppendLine("executable=" + executablePath);
            sb.AppendLine("executable_exists=" + File.Exists(executablePath));
            sb.AppendLine("bridge=" + bridgePath);
            sb.AppendLine("bridge_exists=" + File.Exists(bridgePath));
            sb.AppendLine("dependency_report=" + dependencyReportPath);
            sb.AppendLine("ocr_scale=" + config.OcrScale.ToString("0.###", CultureInfo.InvariantCulture));
            if (process == null)
            {
                sb.AppendLine("process=null");
            }
            else
            {
                sb.AppendLine("process_id=" + process.Id.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("process_has_exited=" + process.HasExited);
                if (process.HasExited) sb.AppendLine("process_exit_code=" + process.ExitCode.ToString(CultureInfo.InvariantCulture));
            }
            snapshot.EngineDiagnostics = sb.ToString();
        }

        private static void PrependPythonPath(ProcessStartInfo psi, string path)
        {
            if (!Directory.Exists(path)) return;

            string old = psi.EnvironmentVariables["PYTHONPATH"];
            if (string.IsNullOrWhiteSpace(old))
            {
                psi.EnvironmentVariables["PYTHONPATH"] = path;
            }
            else if (old.IndexOf(path, StringComparison.OrdinalIgnoreCase) < 0)
            {
                psi.EnvironmentVariables["PYTHONPATH"] = path + Path.PathSeparator + old;
            }
        }

        private OcrSnapshot ParseOcrJson(string rawJson, Screenshot screenshot, double imageScale, string engineName, string errorSuffix)
        {
            Dictionary<string, object> root = jsonSerializer.Deserialize<Dictionary<string, object>>(rawJson);
            int code = Convert.ToInt32(root["code"], CultureInfo.InvariantCulture);
            if (code != 100 && code != 0)
            {
                string error = root.ContainsKey("error") ? Convert.ToString(root["error"], CultureInfo.InvariantCulture) : rawJson;
                throw new InvalidOperationException(engineName + " 执行失败：" + error + errorSuffix);
            }

            object responseScaleObj;
            if (root.TryGetValue("scale", out responseScaleObj))
            {
                imageScale = ParseDouble(Convert.ToString(responseScaleObj, CultureInfo.InvariantCulture), imageScale);
            }

            List<OcrMatch> matches = new List<OcrMatch>();
            object dataObj;
            if (root.TryGetValue("data", out dataObj))
            {
                IEnumerable items = dataObj as IEnumerable;
                if (items != null && !(dataObj is string))
                {
                    foreach (object itemObj in items)
                    {
                        Dictionary<string, object> item = itemObj as Dictionary<string, object>;
                        if (item == null) continue;
                        string text = Convert.ToString(item["text"], CultureInfo.InvariantCulture);
                        double confidence = item.ContainsKey("confidence") ? Convert.ToDouble(item["confidence"], CultureInfo.InvariantCulture) : -1;
                        RectangleF rect = ParseOcrRect(item["rect"], screenshot, imageScale);
                        string language = item.ContainsKey("language") ? Convert.ToString(item["language"], CultureInfo.InvariantCulture) : "";
                        matches.Add(new OcrMatch(text, rect, confidence, language));
                    }
                }
            }

            List<List<OcrMatch>> wordLines = GroupOcrLines(matches);
            List<OcrMatch> lineMatches = wordLines.Select(MergeWords).ToList();
            return new OcrSnapshot(screenshot, matches, wordLines, lineMatches);
        }

        private RectangleF ParseOcrRect(object rectObj, Screenshot screenshot, double imageScale)
        {
            List<object> values = new List<object>();
            IEnumerable enumerable = rectObj as IEnumerable;
            if (enumerable != null && !(rectObj is string))
            {
                foreach (object value in enumerable) values.Add(value);
            }
            if (values.Count < 4) return new RectangleF(screenshot.Left, screenshot.Top, 1, 1);
            double scale = imageScale > 0 ? imageScale : Math.Max(1.0, config.OcrScale);
            float left = (float)(Convert.ToDouble(values[0], CultureInfo.InvariantCulture) / scale) + screenshot.Left;
            float top = (float)(Convert.ToDouble(values[1], CultureInfo.InvariantCulture) / scale) + screenshot.Top;
            float width = (float)(Convert.ToDouble(values[2], CultureInfo.InvariantCulture) / scale);
            float height = (float)(Convert.ToDouble(values[3], CultureInfo.InvariantCulture) / scale);
            return new RectangleF(left, top, width, height);
        }

        private static List<List<OcrMatch>> GroupOcrLines(List<OcrMatch> matches)
        {
            List<List<OcrMatch>> lines = new List<List<OcrMatch>>();
            foreach (OcrMatch match in matches.OrderBy(m => m.Rect.Top).ThenBy(m => m.Rect.Left))
            {
                float centerY = match.Rect.Top + match.Rect.Height / 2;
                List<OcrMatch> target = null;
                foreach (List<OcrMatch> line in lines)
                {
                    float lineCenter = line.Average(m => m.Rect.Top + m.Rect.Height / 2);
                    float tolerance = Math.Max(12, line.Average(m => m.Rect.Height) * 0.65f);
                    if (Math.Abs(centerY - lineCenter) <= tolerance)
                    {
                        target = line;
                        break;
                    }
                }

                if (target == null)
                {
                    target = new List<OcrMatch>();
                    lines.Add(target);
                }
                target.Add(match);
            }

            foreach (List<OcrMatch> line in lines)
            {
                line.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));
            }
            return lines;
        }

        private string PaddleOcrErrorSuffix()
        {
            lock (paddleOcrErrors)
            {
                if (paddleOcrErrors.Length == 0) return "";
                return "\r\nPaddleOCR stderr:\r\n" + paddleOcrErrors;
            }
        }

        private string MediaOcrErrorSuffix()
        {
            lock (mediaOcrErrors)
            {
                if (mediaOcrErrors.Length == 0) return "";
                return "\r\nMediaOCR stderr:\r\n" + mediaOcrErrors;
            }
        }

        private string RapidOcrErrorSuffix()
        {
            lock (rapidOcrErrors)
            {
                if (rapidOcrErrors.Length == 0) return "";
                return "\r\nRapidOCR stderr:\r\n" + rapidOcrErrors;
            }
        }

        private static string NormalizeLatin(string text)
        {
            return Regex.Replace((text ?? "").ToUpperInvariant(), @"[^A-Z0-9]", "");
        }

        private static int CommonCharCount(string a, string b)
        {
            HashSet<char> seen = new HashSet<char>();
            int count = 0;
            foreach (char ch in a)
            {
                if (!seen.Add(ch)) continue;
                if (b.IndexOf(ch) >= 0) count++;
            }
            return count;
        }

        private static bool FuzzyContains(string haystack, string needle, int maxDistance)
        {
            if (haystack.Length <= needle.Length + 4)
            {
                return LevenshteinDistance(haystack, needle) <= maxDistance;
            }

            int minLength = Math.Max(1, needle.Length - 2);
            int maxLength = Math.Min(haystack.Length, needle.Length + 4);
            for (int length = minLength; length <= maxLength; length++)
            {
                for (int start = 0; start + length <= haystack.Length; start++)
                {
                    if (LevenshteinDistance(haystack.Substring(start, length), needle) <= maxDistance) return true;
                }
            }
            return false;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int delete = dp[i - 1, j] + 1;
                    int insert = dp[i, j - 1] + 1;
                    int replace = dp[i - 1, j - 1] + cost;
                    dp[i, j] = Math.Min(Math.Min(delete, insert), replace);
                }
            }

            return dp[a.Length, b.Length];
        }

        private static string ResolvePython(Config config, string configuredPath, string envName)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : config.ResolvePath(configuredPath);
            }

            string env = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(env)) return env;

            string localRuntime = Path.Combine(config.BaseDir, "runtime", "python", "python.exe");
            if (File.Exists(localRuntime)) return localRuntime;

            return "python.exe";
        }

        private static string ResolvePowerShell(Config config, string configuredPath, string envName)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : config.ResolvePath(configuredPath);
            }

            string env = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(env)) return env;

            string systemPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(systemPowerShell)) return systemPowerShell;

            return "powershell.exe";
        }

        public void Dispose()
        {
            StopBridgeProcess(ref paddleOcrProcess);
            StopBridgeProcess(ref mediaOcrProcess);
            StopBridgeProcess(ref rapidOcrProcess);
        }

        private static void StopBridgeProcess(ref Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("__exit__");
                    process.StandardInput.Flush();
                    if (!process.WaitForExit(1500)) process.Kill();
                }
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            }
            finally
            {
                process.Dispose();
                process = null;
            }
        }
    }

}
