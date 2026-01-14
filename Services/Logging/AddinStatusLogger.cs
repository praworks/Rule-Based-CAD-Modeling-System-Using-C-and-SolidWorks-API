using System;
using System.IO;
using AICAD.Services.Logging;

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
            line = StripDuplicateTimestamp(line);
            line = EnrichWithContext(line);
            line = HumanLogFormatter.Format(line);
            if (string.IsNullOrWhiteSpace(line)) return;
            try
            {
                OnLog?.Invoke(line);
            }
            catch { }
            try
            {
                lock (_sync)
                {
                    // The line is already formatted with a single timestamp (HH:mm:ss.fff). Write as-is.
                    var txt = (line ?? string.Empty) + System.Environment.NewLine;
                    TempFileWriter.AppendAllText("AI_CAD_Addin.log", txt);

                    // store in buffer for UI windows opened later
                    try
                    {
                        _buffer.Add(line ?? string.Empty);
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

        // Remove leading duplicate timestamp if the line already contains two timestamps back-to-back
        private static string StripDuplicateTimestamp(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line ?? string.Empty;
            try
            {
                var trimmed = line.TrimStart();
                // Drop banner-only lines early
                if (IsNoiseBanner(trimmed))
                    return string.Empty;

                // Collect up to two timestamps and return the remainder prefixed with the last timestamp (one only)
                if (TryTakeTimestamp(trimmed, out var ts1, out var rest1))
                {
                    var rest1Trim = rest1.TrimStart();
                    if (TryTakeTimestamp(rest1Trim, out var ts2, out var rest2))
                    {
                        // Two timestamps found; keep the second + remainder
                        return $"{ts2} {rest2.TrimStart()}".Trim();
                    }
                    // Only one timestamp found; rebuild with that single ts + remainder
                    return $"{ts1} {rest1Trim}".Trim();
                }
                return line;
            }
            catch { return line; }
        }

        private static bool TryTakeTimestamp(string text, out string timestamp, out string remainder)
        {
            timestamp = string.Empty;
            remainder = text;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length < 12) return false;
            var firstSpace = text.IndexOf(' ');
            if (firstSpace <= 0) return false;
            var token = text.Substring(0, firstSpace);
            if (TimeSpan.TryParseExact(token, new[] { @"hh\:mm\:ss\.fff", @"hh\:mm\:ss\.ffffff", @"hh\:mm\:ss\.fffffff", @"hh\:mm\:ss\.fffffffff" }, null, out _))
            {
                timestamp = token;
                remainder = text.Substring(firstSpace + 1);
                return true;
            }
            return false;
        }

        private static bool HasTimestampPrefix(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            line = line.TrimStart();
            int firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0) return false;
            var token = line.Substring(0, firstSpace);
            return TimeSpan.TryParseExact(token, new[] { @"hh\:mm\:ss\.fff", @"hh\:mm\:ss\.ffffff", @"hh\:mm\:ss\.fffffff", @"hh\:mm\:ss\.fffffffff" }, null, out _);
        }

        private static bool IsNoiseBanner(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            if (t.StartsWith("---") || t.StartsWith("===") || t.StartsWith("___") || t.StartsWith("――") || t.StartsWith("—"))
                return true;
            if (t.Equals("UI / PIPELINE", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Equals("CLASSIFY", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Equals("DECOMPOSE", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Equals("EXECUTE", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Equals("END", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // If corr/op/stage are missing, enrich using ambient LoggingContext.Current
        private static string EnrichWithContext(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line ?? string.Empty;
            var ctx = Logging.LoggingContext.Current;
            if (ctx == null) return line;

            bool hasCorr = line.IndexOf("corr=", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasOp = line.IndexOf("op=", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasStage = line.IndexOf("stage=", StringComparison.OrdinalIgnoreCase) >= 0;
            if (hasCorr && hasOp && hasStage) return line;

            var suffix = "";
            if (!hasCorr && !string.IsNullOrWhiteSpace(ctx.CorrelationId)) suffix += $" corr={ctx.CorrelationId}";
            if (!hasOp && !string.IsNullOrWhiteSpace(ctx.Operation)) suffix += $" op={ctx.Operation}";
            var stage = ctx.Stage ?? ctx.Operation;
            if (!hasStage && !string.IsNullOrWhiteSpace(stage)) suffix += $" stage={stage}";
            if (string.IsNullOrWhiteSpace(suffix)) return line;
            return $"{line} {suffix.Trim()}";
        }
    }
}
