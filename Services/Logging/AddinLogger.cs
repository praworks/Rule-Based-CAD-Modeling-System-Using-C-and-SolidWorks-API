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
        private static bool _enabled = false;

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
            // Route through central pipeline for consistency
            AddinStatusLogger.Log(component, message);
        }

        public static void Error(string component, string message, Exception ex)
        {
            // Route through central pipeline for consistency
            AddinStatusLogger.Error(component, message, ex);
        }

        // Allow tests or diagnostics to opt-in to legacy file sink to avoid duplication
        public static void Enable(bool enabled = true)
        {
            _enabled = enabled;
        }
    }
}
