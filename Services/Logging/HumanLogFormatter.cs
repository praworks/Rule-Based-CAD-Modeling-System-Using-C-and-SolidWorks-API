using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AICAD.Services.Logging
{
    /// <summary>
    /// Human-facing formatter that normalizes log lines, removes duplicate timestamps,
    /// compresses stage lists, applies indentation, and drops noisy entries.
    /// </summary>
    internal static class HumanLogFormatter
    {
        private static readonly Regex StageRegex = new Regex(@"stage=([A-Za-z0-9_\/\s,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CorrRegex = new Regex(@"corr=([^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex OpRegex = new Regex(@"op=([^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LevelRegex = new Regex(@"\b(Information|Warning|Error|Debug|Critical|INFO|WARN|ERROR|DEBUG|CRIT)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DupTsRegex = new Regex(@"^(\d{2}:\d{2}:\d{2}\.\d{3,9})\s+(\d{2}:\d{2}:\d{2}\.\d{3,9})\s+", RegexOptions.Compiled);

        private static readonly Dictionary<string, int> StageDepth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "UI", 0 }, { "PIPELINE", 0 }, { "CLASSIFY", 1 }, { "DECOMPOSE", 1 }, { "EXECUTE", 1 }, { "VALIDATE", 1 }, { "DB", 1 }
        };

        private static readonly Dictionary<string, string> _lastStageByCorr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Format(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            // Drop noisy HTTP request bodies by default
            if (line.StartsWith("[GroqClient] Request sent", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("[GroqLlmClient] Groq rate limit", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("[GroqClient] Response", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("[LocalHttpLlmClient]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            line = DeduplicateTimestamp(line.Trim());

            // Extract pieces
            var corr = ExtractValue(CorrRegex, line);
            if (string.IsNullOrWhiteSpace(corr) && LoggingContext.Current != null)
                corr = LoggingContext.Current.CorrelationId;
            if (string.IsNullOrWhiteSpace(corr)) corr = "-";
            var op = ExtractValue(OpRegex, line);
            if (string.IsNullOrWhiteSpace(op) && LoggingContext.Current != null)
                op = LoggingContext.Current.Operation;
            if (string.IsNullOrWhiteSpace(op)) op = "log";
            var level = NormalizeLevel(line);

            var stage = NormalizeStage(ExtractValue(StageRegex, line));
            if (string.IsNullOrWhiteSpace(stage))
            {
                _lastStageByCorr.TryGetValue(corr, out var prev);
                if (string.IsNullOrWhiteSpace(prev) && LoggingContext.Current != null)
                    prev = LoggingContext.Current.Stage ?? LoggingContext.Current.Operation;
                stage = prev ?? "UI";
            }

            var src = ExtractSource(line, op);
            var msg = ExtractMessage(line);
            msg = LogRedactor.Truncate(msg, 120);

            // Track last stage per correlation to avoid spamming headers
            _lastStageByCorr.TryGetValue(corr, out var lastStage);
            bool stageChanged = !string.Equals(lastStage, stage, StringComparison.OrdinalIgnoreCase);
            _lastStageByCorr[corr] = stage;

            var indent = new string(' ', GetIndent(stage));
            var timestamp = ExtractTimestamp(line);
            var header = $"{timestamp} {level,-5} corr={corr} op={op} stage={stage} src={src}";
            var body = $"{indent}{msg}";
            if (stageChanged)
            {
                var stageHeader = $"{timestamp} INFO  corr={corr} op={op} stage={stage} src={src}  -- {stage} --";
                return stageHeader + Environment.NewLine + $"{header} {body}";
            }
            return $"{header} {body}";
        }

        private static string DeduplicateTimestamp(string line)
        {
            var m = DupTsRegex.Match(line);
            if (m.Success)
            {
                // keep second timestamp
                return $"{m.Groups[2].Value} {line.Substring(m.Length)}".TrimStart();
            }
            return line;
        }

        private static string ExtractTimestamp(string line)
        {
            var m = Regex.Match(line ?? string.Empty, @"^\d{2}:\d{2}:\d{2}\.\d{3,9}");
            if (m.Success) return m.Value;
            return DateTime.Now.ToString("HH:mm:ss.fff");
        }

        private static string NormalizeStage(string stageRaw)
        {
            if (string.IsNullOrWhiteSpace(stageRaw)) return string.Empty;
            var parts = stageRaw.Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            if (parts.Count == 0) return string.Empty;
            var last = parts.Last();
            switch (last.ToUpperInvariant())
            {
                case "UI": return "UI";
                case "CLASSIFY": return "CLASSIFY";
                case "DECOMPOSE": return "DECOMPOSE";
                case "EXECUTE": return "EXECUTE";
                case "VALIDATE": return "VALIDATE";
                case "DB": return "DB";
                default: return last;
            }
        }

        private static int GetIndent(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage)) return 0;
            if (StageDepth.TryGetValue(stage, out var depth))
                return depth * 2;
            return 0;
        }

        private static string NormalizeLevel(string line)
        {
            var lvl = ExtractValue(LevelRegex, line);
            if (string.IsNullOrWhiteSpace(lvl)) return "INFO";
            switch (lvl.ToLowerInvariant())
            {
                case "information": return "INFO";
                case "warning": return "WARN";
                case "error": return "ERROR";
                case "debug": return "DEBUG";
                case "critical": return "CRIT";
                default: return lvl.ToUpperInvariant();
            }
        }

        private static string ExtractMessage(string line)
        {
            // Try to strip leading metadata up to provider/src markers
            var idx = line.IndexOf("  ", StringComparison.Ordinal);
            if (idx > 0)
            {
                var msg = line.Substring(idx).Trim();
                // Remove key/value tokens already captured
                msg = StageRegex.Replace(msg, string.Empty);
                msg = CorrRegex.Replace(msg, string.Empty);
                msg = OpRegex.Replace(msg, string.Empty);
                msg = LevelRegex.Replace(msg, string.Empty);
                return msg.Trim();
            }
            return line;
        }

        private static string ExtractSource(string line, string fallback)
        {
            // If there is a provider=<x> keep it as source
            var provider = ExtractValue(new Regex(@"provider=([^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled), line);
            if (!string.IsNullOrWhiteSpace(provider)) return provider;
            // Bracket prefix [Source]
            if (line.StartsWith("["))
            {
                var end = line.IndexOf(']');
                if (end > 1)
                    return line.Substring(1, end - 1);
            }
            return fallback ?? "log";
        }

        private static string ExtractValue(Regex regex, string text)
        {
            var m = regex.Match(text ?? string.Empty);
            if (m.Success && m.Groups.Count > 1)
                return m.Groups[1].Value;
            return string.Empty;
        }

        private static bool Extract(Regex regex, string text, out string value, out string remainder)
        {
            var m = regex.Match(text ?? string.Empty);
            if (m.Success && m.Groups.Count > 1)
            {
                value = m.Groups[1].Value;
                remainder = text.Remove(m.Index, m.Length);
                return true;
            }
            value = string.Empty;
            remainder = text;
            return false;
        }
    }
}
