using System;
using System.IO;

namespace AICAD.Services
{
    /// <summary>
    /// Minimal file logger used by NameEasy features to avoid crashing on logging errors.
    /// </summary>
    public static class AddinLogger
    {
        private static readonly string LogPath;
        private static readonly object Sync = new object();

        static AddinLogger()
        {
            try
            {
                var asmDir = Path.GetDirectoryName(typeof(AddinLogger).Assembly.Location) ?? string.Empty;
                LogPath = Path.Combine(asmDir, "NameEasy.log");
            }
            catch
            {
                LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NameEasy.log");
            }
        }

        public static void Log(string component, string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{component}] {message}";
                lock (Sync)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never throw from logger
            }
        }

        public static void Error(string component, string message, Exception ex)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [{component}] {message}\n    Exception: {ex.GetType().Name}: {ex.Message}\n    StackTrace: {ex.StackTrace}";
                lock (Sync)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never throw from logger
            }
        }
    }
}
