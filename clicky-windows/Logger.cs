using System;
using System.IO;

namespace clicky_windows
{
    public static class Logger
    {
        private static readonly string LogFilePath;
        private static readonly object LockObj = new();

        static Logger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clickyFolder = Path.Combine(appData, "Clicky");
            
            try
            {
                if (!Directory.Exists(clickyFolder))
                {
                    Directory.CreateDirectory(clickyFolder);
                }
            }
            catch
            {
                // Fallback to current directory if AppData is not writable
                clickyFolder = AppDomain.CurrentDomain.BaseDirectory;
            }

            LogFilePath = Path.Combine(clickyFolder, "clicky.log");
        }

        public static void Info(string message) => Log(message, "INFO");
        public static void Warn(string message) => Log(message, "WARN");
        public static void Error(string message, Exception? ex = null) => Log(message, "ERROR", ex);

        private static void Log(string message, string level, Exception? ex = null)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logLine = $"[{timestamp}] [{level}] {message}";
            if (ex != null)
            {
                logLine += $"\nException: {ex.GetType().FullName}: {ex.Message}\nStack Trace:\n{ex.StackTrace}";
            }

            // Also output to console for development
            Console.WriteLine(logLine);

            try
            {
                lock (LockObj)
                {
                    File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // Suppress file write failures to avoid crashing the app
            }
        }
    }
}
