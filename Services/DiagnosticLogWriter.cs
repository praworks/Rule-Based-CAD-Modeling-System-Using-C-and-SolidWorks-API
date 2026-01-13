using System;
using System.Collections.Generic;

namespace AICAD.Services
{
    internal sealed class DiagnosticLogSettings
    {
        public string ProviderPriority { get; set; }
        public int ClassifyTimeoutSeconds { get; set; }
        public int DecomposeTimeoutSeconds { get; set; }
        public int ExpandTimeoutSeconds { get; set; }
        public bool FewShotEnabled { get; set; }
        public bool FewShotRandomize { get; set; }
        public bool FewShotForceStatic { get; set; }
        public string LocalEndpoint { get; set; }
        public bool GeminiKeyPresent { get; set; }
        public bool GroqKeyPresent { get; set; }
    }

    internal static class DiagnosticLogWriter
    {
        private const string HeaderLine = "===============================================================================";
        private const string SectionLine = "───────────────────────────────────────────────────────────────────────────────";
        private const int TimestampWidth = 12;
        private const int LevelWidth = 5;
        private const int IdWidth = 6;
        private const int ComponentWidth = 20;
        private static readonly object _lock = new object();
        private static readonly HashSet<string> _startedRuns = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> _sectionsByRun = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public static void BeginRun(string runId, string userPrompt, DiagnosticLogSettings settings, string component = "TaskpaneWpf")
        {
            if (string.IsNullOrWhiteSpace(runId))
                return;

            lock (_lock)
            {
                if (_startedRuns.Contains(runId))
                    return;
                _startedRuns.Add(runId);
                _sectionsByRun[runId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            WriteRaw(HeaderLine);
            WriteRaw($"DIAGNOSTIC LOG FOR \"{userPrompt ?? string.Empty}\"");
            WriteRaw(HeaderLine);
            if (settings != null)
            {
                var provider = string.IsNullOrWhiteSpace(settings.ProviderPriority) ? "NA" : settings.ProviderPriority;
                var timeout = settings.ExpandTimeoutSeconds > 0 ? settings.ExpandTimeoutSeconds
                             : (settings.DecomposeTimeoutSeconds > 0 ? settings.DecomposeTimeoutSeconds
                             : settings.ClassifyTimeoutSeconds);
                var timeoutText = timeout > 0 ? timeout.ToString() : "NA";
                LogLine(runId, null, component, "INFO", $"Run Config: Provider={provider}, Timeout={timeoutText}s");
            }
            WriteRaw(string.Empty);
        }

        public static void StartSection(string runId, string name)
        {
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(name))
                return;

            lock (_lock)
            {
                if (!_sectionsByRun.TryGetValue(runId, out var sections))
                {
                    sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _sectionsByRun[runId] = sections;
                }

                if (sections.Contains(name))
                    return;

                sections.Add(name);
            }

            WriteRaw(SectionLine);
            WriteRaw(name);
            WriteRaw(SectionLine);
        }

        public static void SkipSection(string runId, string name, string reason, string component = "TaskpaneWpf")
        {
            StartSection(runId, name);
            LogLine(runId, null, component, "INFO", $"SKIPPED: {reason}");
        }

        public static void FeatureHeader(string runId, int index, string featureType)
        {
            var label = $"FEATURE {index} — {featureType}";
            WriteRaw(label);
        }

        public static void EndRun(string runId, bool success, string error, long? elapsedMs, string component = "TaskpaneWpf")
        {
            if (!string.IsNullOrWhiteSpace(runId))
            {
                var msg = $"Run complete: success={success}";
                if (elapsedMs.HasValue)
                    msg += $" elapsedMs={elapsedMs.Value}";
                if (!string.IsNullOrWhiteSpace(error))
                    msg += $" error={error}";
                LogLine(runId, null, component, success ? "INFO" : "ERROR", msg);
            }

            WriteRaw(HeaderLine);
            WriteRaw("END");
            WriteRaw(HeaderLine);

            if (!string.IsNullOrWhiteSpace(runId))
            {
                lock (_lock)
                {
                    _sectionsByRun.Remove(runId);
                    _startedRuns.Remove(runId);
                }
            }
        }

        public static void LogLine(string runId, string requestId, string component, string level, string message)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var lvl = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
            var rid = FormatId(runId);
            var req = FormatId(requestId);
            var comp = FitFixed(component, ComponentWidth, "-");
            var msg = StripStepPrefix(message ?? string.Empty);
            var line = string.Format(
                "{0,-12} | {1,-5} | {2,-6} | {3,-6} | {4,-20} | {5}",
                FitFixed(ts, TimestampWidth, "00:00:00.000"),
                FitFixed(lvl, LevelWidth, "INFO"),
                rid,
                req,
                comp,
                msg);
            AddinStatusLogger.Log(string.Empty, line);
        }

        public static string Truncate(string text, int maxLen)
        {
            if (maxLen < 1)
                return string.Empty;

            if (string.IsNullOrEmpty(text))
                return "(len=0)";

            var len = text.Length;
            var suffix = $" (len={len})";
            if (len <= maxLen - suffix.Length)
                return text + suffix;

            var available = maxLen - suffix.Length;
            if (available <= 0)
                return suffix;

            var ellipsis = "...";
            if (available <= ellipsis.Length)
                return ellipsis + suffix;

            var headTailLen = available - ellipsis.Length;
            var headLen = headTailLen / 2;
            var tailLen = headTailLen - headLen;
            var head = headLen > 0 ? text.Substring(0, headLen) : string.Empty;
            var tail = tailLen > 0 ? text.Substring(len - tailLen, tailLen) : string.Empty;
            return head + ellipsis + tail + suffix;
        }

        private static string FormatId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "-";

            var trimmed = id.Trim();
            if (Guid.TryParse(trimmed, out var guid))
                trimmed = guid.ToString("N");

            return trimmed.Length > IdWidth ? trimmed.Substring(trimmed.Length - IdWidth) : trimmed;
        }

        private static string StripStepPrefix(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message ?? string.Empty;

            var trimmed = message.TrimStart();
            if (!trimmed.StartsWith("STEP ", StringComparison.OrdinalIgnoreCase))
                return message;

            var idx = 5;
            while (idx < trimmed.Length && (char.IsDigit(trimmed[idx]) || trimmed[idx] == '.'))
                idx++;
            while (idx < trimmed.Length && (trimmed[idx] == ':' || trimmed[idx] == '-' || char.IsWhiteSpace(trimmed[idx])))
                idx++;
            if (idx >= trimmed.Length)
                return trimmed;
            return trimmed.Substring(idx);
        }

        private static string FitFixed(string value, int width, string fallback)
        {
            var text = string.IsNullOrWhiteSpace(value) ? (fallback ?? string.Empty) : value.Trim();
            return text.Length > width ? text.Substring(0, width) : text;
        }

        private static void WriteRaw(string line)
        {
            AddinStatusLogger.Log(string.Empty, line ?? string.Empty);
        }
    }
}
