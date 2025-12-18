using System;
using System.IO;

namespace SolidWorks.TaskpaneCalculator.Services
{
    // Simple global logger for the Add-in which raises events and optionally writes to a local file.
    public static class AddinStatusLogger
    {
        // Raised when a new log line is available. UI should subscribe and append to console.
        public static event Action<string> OnLog;

    private static readonly object _sync = new object();
    private static readonly string _filePath = Path.Combine(Path.GetTempPath(), "AI_CAD_Addin.log");

        public static void Log(string category, string message)
        {
            var line = string.IsNullOrWhiteSpace(category) ? message : $"[{category}] {message}";
            Emit(line);
        }

        public static void Error(string category, string message, Exception ex = null)
        {
            var line = string.IsNullOrWhiteSpace(category) ? "ERROR: " + message : $"[ERROR:{category}] {message}";
            if (ex != null) line += " => " + ex.ToString();
            Emit(line);
        }

        private static void Emit(string line)
        {
            try
            {
                OnLog?.Invoke(line);
            }
            catch { }
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_filePath, DateTime.Now.ToString("o") + " " + line + System.Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
