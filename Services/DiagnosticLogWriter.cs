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
        private const int LevelWidth = 5;
        private const int IdWidth = 8;
        private const int ComponentWidth = 24;
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
            WriteRaw("Log Settings");
            WriteRaw(HeaderLine);
            if (settings != null)
            {
                LogLine(runId, null, component, "INFO", $"ProviderPriority={settings.ProviderPriority ?? string.Empty}");
                LogLine(runId, null, component, "INFO", $"Timeouts: classify={settings.ClassifyTimeoutSeconds}s decompose={settings.DecomposeTimeoutSeconds}s expand={settings.ExpandTimeoutSeconds}s");
                LogLine(runId, null, component, "INFO", $"FewShot: enabled={settings.FewShotEnabled} randomize={settings.FewShotRandomize} force_static={settings.FewShotForceStatic}");
                LogLine(runId, null, component, "INFO", $"LocalEndpoint={settings.LocalEndpoint ?? string.Empty} GeminiKeyPresent={settings.GeminiKeyPresent} GroqKeyPresent={settings.GroqKeyPresent}");
            }
            WriteRaw($"DIAGNOSTIC LOG FOR \"{userPrompt ?? string.Empty}\"");
            WriteRaw(HeaderLine);
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
            var lvl = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
            var rid = FormatId(runId);
            var req = FormatId(requestId);
            var comp = FitFixed(component, ComponentWidth, "Unknown");
            var msg = message ?? string.Empty;
            var line = string.Format(
                "| {0,-5} | run={1,-8} | req={2,-8} | {3,-24} | {4}",
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
            if (len <= maxLen)
                return text + $" (len={len})";

            var suffix = $"... (len={len})";
            var headLen = Math.Max(0, maxLen - suffix.Length);
            var head = headLen > 0 ? text.Substring(0, headLen) : string.Empty;
            return head + suffix;
        }

        private static string FormatId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "-";

            var trimmed = id.Trim();
            if (Guid.TryParse(trimmed, out var guid))
            {
                var compact = guid.ToString("N");
                return compact.Substring(0, Math.Min(IdWidth, compact.Length));
            }

            return trimmed.Length > IdWidth ? trimmed.Substring(0, IdWidth) : trimmed;
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
