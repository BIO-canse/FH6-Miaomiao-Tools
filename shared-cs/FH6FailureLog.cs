using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FH6AutomationShared
{
    internal static class FH6FailureLog
    {
        private static int handlersInstalled;

        public static void InstallGlobalHandlers(string context)
        {
            if (Interlocked.Exchange(ref handlersInstalled, 1) != 0) return;

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    Write(context + ".UnhandledException", ex);
                }
                else
                {
                    Write(context + ".UnhandledException", "Unhandled non-Exception object: " + Convert.ToString(e.ExceptionObject, CultureInfo.InvariantCulture));
                }
            };

            TaskScheduler.UnobservedTaskException += delegate(object sender, UnobservedTaskExceptionEventArgs e)
            {
                Write(context + ".UnobservedTaskException", e.Exception);
            };
        }

        public static void Write(string context, Exception ex)
        {
            Write(context, ex == null ? "(null exception)" : ex.ToString());
        }

        public static void Write(string context, string message)
        {
            try
            {
                string debugDir = ResolveDebugDir();
                Directory.CreateDirectory(debugDir);

                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                int pid = Process.GetCurrentProcess().Id;
                string exe = AppDomain.CurrentDomain.FriendlyName;
                string body =
                    "time=" + stamp + "\r\n" +
                    "context=" + (context ?? "") + "\r\n" +
                    "pid=" + pid.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "exe=" + exe + "\r\n" +
                    "base_dir=" + ResolveApplicationBaseDir() + "\r\n" +
                    "current_dir=" + Environment.CurrentDirectory + "\r\n" +
                    "\r\n" +
                    (message ?? "") + "\r\n";

                File.WriteAllText(Path.Combine(debugDir, "last-error.txt"), body, Encoding.UTF8);

                string dayLog = Path.Combine(debugDir, "error-log-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(dayLog, body + "\r\n---\r\n", Encoding.UTF8);

                string safeContext = MakeSafeFilePart(context);
                string errorFile = Path.Combine(debugDir, "error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + safeContext + ".txt");
                File.WriteAllText(errorFile, body, Encoding.UTF8);
            }
            catch (Exception logEx)
            {
                try
                {
                    Console.Error.WriteLine("[ERROR_LOG_FAILED] " + logEx);
                    Console.Error.WriteLine("[ORIGINAL_ERROR] " + message);
                }
                catch
                {
                }
            }
        }

        public static string ResolveDebugDir()
        {
            return Path.Combine(ResolveApplicationBaseDir(), FH6AutomationConstants.Files.DebugDir);
        }

        public static string ResolveApplicationBaseDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string trimmed = exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), "bin", StringComparison.OrdinalIgnoreCase))
            {
                DirectoryInfo parent = Directory.GetParent(trimmed);
                if (parent != null) return parent.FullName;
            }
            return exeDir;
        }

        private static string MakeSafeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append('_');
                }
            }
            if (sb.Length == 0) return "unknown";
            if (sb.Length > 64) return sb.ToString(0, 64);
            return sb.ToString();
        }
    }
}
