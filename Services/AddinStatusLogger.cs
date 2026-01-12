using System;
using System.IO;

namespace AICAD.Services
{
    // Simple global logger for the Add-in which raises events and optionally writes to a local file.
    public static class AddinStatusLogger
    {
        // Raised when a new log line is available. UI should subscribe and append to console.
        public static event Action<string> OnLog;

    private static readonly object _sync = new object();
        // Keep a short in-memory buffer so logs emitted before UI is ready can be shown
        private static readonly System.Collections.Generic.List<string> _buffer = new System.Collections.Generic.List<string>();
        private const int BufferSize = 500;

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

            try
            {
                // Decide whether to send this exception to the LLM for analysis
                if (ex != null)
                {
                    if (ExceptionClassifier.ShouldSend(ex, category, message, out var reason))
                    {
                        // Fire-and-forget reporting so we don't block callers
                        try { System.Threading.Tasks.Task.Run(() => LlmErrorReporter.ReportAsync(category, message, ex)); } catch { }
                    }
                    else
                    {
                        // Optionally log why it was not sent
                        AddinStatusLogger.Log("ExceptionClassifier", $"Not sending to LLM: {reason}");
                    }
                }
            }
            catch { }
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
                    // mirror to file
                    var txt = DateTime.Now.ToString("o") + " " + line + System.Environment.NewLine;
                    TempFileWriter.AppendAllText("AI_CAD_Addin.log", txt);

                    // store in buffer for UI windows opened later
                    try
                    {
                        _buffer.Add(DateTime.Now.ToString("HH:mm:ss.ffffff") + " " + line);
                        if (_buffer.Count > BufferSize) _buffer.RemoveAt(0);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Return a snapshot of buffered log lines (most-recent last)
        public static string[] GetBufferedLines()
        {
            lock (_sync)
            {
                return _buffer.ToArray();
            }
        }
    }
}
