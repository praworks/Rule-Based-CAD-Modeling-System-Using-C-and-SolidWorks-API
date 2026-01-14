using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AICAD.Services.Logging
{
    internal static class LlmTraceLogger
    {
        private static readonly Lazy<bool> _enabled = new Lazy<bool>(() => IsEnabled());
        private static readonly Lazy<string> _baseDir = new Lazy<string>(() => InitBaseDir());
        private static readonly ConcurrentDictionary<string, object> _locks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _traceDate = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool Enabled => _enabled.Value;

        public static string BaseDir => _baseDir.Value;

        public static string GetOrCreateTraceIdFromContext(string preferred = null)
        {
            if (!Enabled) return null;
            try
            {
                var candidate = preferred;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = LoggingContext.Current?.CorrelationId;
                }
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = Guid.NewGuid().ToString("N").Substring(0, 12);
                }
                return SanitizeTraceId(candidate);
            }
            catch
            {
                return null;
            }
        }

        public static void LogSend(string traceId, string provider, string model, string url, string method, string payloadText, string systemPrompt, string userPrompt, string requestId = null)
        {
            if (!ShouldLog(traceId)) return;
            try
            {
                var ts = DateTimeOffset.UtcNow;
                var ctx = LoggingContext.Current;
                var evt = CreateBaseEvent(traceId, ctx, provider, model, requestId, ts);
                evt["event"] = "SEND";

                var http = BuildHttp(url, method, null, null);
                if (http != null) evt["http"] = http;

                if (!string.IsNullOrEmpty(payloadText))
                {
                    evt["payload"] = payloadText;
                }

                AppendJsonLine(traceId, evt);
                AppendTranscriptSend(traceId, ts, provider, model, ctx?.CorrelationId, requestId, systemPrompt, userPrompt);
            }
            catch
            {
                // swallow all exceptions to avoid impacting callers
            }
        }

        public static void LogRecv(string traceId, string provider, string model, string url, int? statusCode, string responseText, string assistantText, long? elapsedMs, JToken responseJson = null, string requestId = null)
        {
            if (!ShouldLog(traceId)) return;
            try
            {
                var ts = DateTimeOffset.UtcNow;
                var ctx = LoggingContext.Current;
                var evt = CreateBaseEvent(traceId, ctx, provider, model, requestId, ts);
                evt["event"] = "RECV";

                var http = BuildHttp(url, "POST", statusCode, null);
                if (http != null) evt["http"] = http;

                if (!string.IsNullOrEmpty(responseText)) evt["responseText"] = responseText;
                if (responseJson != null) evt["responseJson"] = responseJson;
                if (elapsedMs.HasValue) evt["elapsedMs"] = elapsedMs.Value;

                AppendJsonLine(traceId, evt);
                AppendTranscriptRecv(traceId, assistantText);
            }
            catch
            {
                // swallow all exceptions to avoid impacting callers
            }
        }

        private static JObject CreateBaseEvent(string traceId, LoggingContext ctx, string provider, string model, string requestId, DateTimeOffset ts)
        {
            var evt = new JObject
            {
                ["tsUtc"] = ts.ToString("o"),
                ["traceId"] = traceId
            };

            if (ctx != null && !string.IsNullOrWhiteSpace(ctx.CorrelationId)) evt["correlationId"] = ctx.CorrelationId;
            if (!string.IsNullOrWhiteSpace(requestId)) evt["requestId"] = requestId;
            if (!string.IsNullOrWhiteSpace(provider)) evt["provider"] = provider;
            if (!string.IsNullOrWhiteSpace(model)) evt["model"] = model;

            return evt;
        }

        private static JObject BuildHttp(string url, string method, int? statusCode, IDictionary<string, string> headers)
        {
            var http = new JObject();
            if (!string.IsNullOrWhiteSpace(url)) http["url"] = url;
            if (!string.IsNullOrWhiteSpace(method)) http["method"] = method;
            if (statusCode.HasValue) http["statusCode"] = statusCode.Value;
            if (headers != null)
            {
                var jHeaders = new JObject();
                foreach (var kvp in headers)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    jHeaders[kvp.Key] = kvp.Value ?? string.Empty;
                }
                if (jHeaders.HasValues) http["headers"] = jHeaders;
            }
            return http.HasValues ? http : null;
        }

        private static void AppendJsonLine(string traceId, JObject evt)
        {
            try
            {
                var (jsonlPath, _) = GetTracePaths(traceId);
                if (string.IsNullOrWhiteSpace(jsonlPath)) return;
                var lck = _locks.GetOrAdd(traceId, _ => new object());
                lock (lck)
                {
                    File.AppendAllText(jsonlPath, evt.ToString(Formatting.None) + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // swallow
            }
        }

        private static void AppendTranscriptSend(string traceId, DateTimeOffset ts, string provider, string model, string correlationId, string requestId, string systemPrompt, string userPrompt)
        {
            try
            {
                var (_, transcriptPath) = GetTracePaths(traceId);
                if (string.IsNullOrWhiteSpace(transcriptPath)) return;
                var sb = new StringBuilder();
                sb.Append('[').Append(ts.ToString("o")).Append("] ");
                sb.Append("META provider=").Append(provider ?? "-");
                sb.Append(" model=").Append(string.IsNullOrWhiteSpace(model) ? "-" : model);
                if (!string.IsNullOrWhiteSpace(correlationId)) sb.Append(" corr=").Append(correlationId);
                if (!string.IsNullOrWhiteSpace(requestId)) sb.Append(" req=").Append(requestId);
                sb.Append(" trace=").Append(traceId);
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    sb.AppendLine("SYSTEM:");
                    sb.AppendLine(systemPrompt);
                    sb.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(userPrompt))
                {
                    sb.AppendLine("USER:");
                    sb.AppendLine(userPrompt);
                    sb.AppendLine();
                }

                var lck = _locks.GetOrAdd(traceId, _ => new object());
                lock (lck)
                {
                    File.AppendAllText(transcriptPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // swallow
            }
        }

        private static void AppendTranscriptRecv(string traceId, string assistantText)
        {
            try
            {
                var (_, transcriptPath) = GetTracePaths(traceId);
                if (string.IsNullOrWhiteSpace(transcriptPath)) return;
                var sb = new StringBuilder();
                sb.AppendLine("ASSISTANT:");
                sb.AppendLine(assistantText ?? string.Empty);
                sb.AppendLine();
                sb.AppendLine("--- END TURN ---");
                sb.AppendLine();

                var lck = _locks.GetOrAdd(traceId, _ => new object());
                lock (lck)
                {
                    File.AppendAllText(transcriptPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // swallow
            }
        }

        private static (string jsonlPath, string transcriptPath) GetTracePaths(string traceId)
        {
            try
            {
                var baseDir = BaseDir;
                if (string.IsNullOrWhiteSpace(baseDir)) return (null, null);

                var datePart = _traceDate.GetOrAdd(traceId, _ => DateTime.UtcNow.ToString("yyyyMMdd"));
                var jsonl = Path.Combine(baseDir, $"llm_trace_{datePart}_{traceId}.jsonl");
                var transcript = Path.Combine(baseDir, $"llm_chat_{datePart}_{traceId}.txt");
                return (jsonl, transcript);
            }
            catch
            {
                return (null, null);
            }
        }

        private static bool ShouldLog(string traceId)
        {
            return Enabled && !string.IsNullOrWhiteSpace(traceId) && !string.IsNullOrWhiteSpace(BaseDir);
        }

        private static string InitBaseDir()
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string path;
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    path = Path.Combine(docs, "AICAD", "Logging");
                }
                else
                {
                    path = Path.Combine(Environment.CurrentDirectory, "Logging");
                }
                Directory.CreateDirectory(path);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeTraceId(string traceId)
        {
            var sb = new StringBuilder();
            foreach (var ch in traceId ?? string.Empty)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append('-');
                }
            }
            var result = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result)) result = Guid.NewGuid().ToString("N").Substring(0, 12);
            return result.Length > 48 ? result.Substring(0, 48) : result;
        }

        private static bool IsEnabled()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AICAD_DEV_LLM_TRACE", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("AICAD_DEV_LLM_TRACE", EnvironmentVariableTarget.User)
                          ?? Environment.GetEnvironmentVariable("AICAD_DEV_LLM_TRACE", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrWhiteSpace(env)) return false;
                return env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase) || env.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
