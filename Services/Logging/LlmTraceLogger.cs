using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AICAD.Services.Logging
{
    internal static class LlmTraceLogger
    {
        public static event Action<string> OnTraceLine;
        public static event Action<LlmTraceEvent> OnTraceEvent;
        private const int MaxBufferedEvents = 200;
        // Evaluate enabled dynamically so changes to environment variables
        // take effect without restarting the host process.
        private static readonly Lazy<string> _baseDir = new Lazy<string>(() => InitBaseDir());
        private static readonly ConcurrentDictionary<string, object> _locks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _traceDate = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LlmTraceEvent> _buffer = new List<LlmTraceEvent>();
        private static readonly object _bufferLock = new object();

        public static bool Enabled => IsEnabled();

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
            if (!Enabled) return;
            try
            {
                if (string.IsNullOrWhiteSpace(traceId))
                {
                    traceId = GetOrCreateTraceIdFromContext();
                    if (string.IsNullOrWhiteSpace(traceId)) traceId = Guid.NewGuid().ToString("N").Substring(0, 12);
                }
                var ts = DateTime.UtcNow;
                var ctx = LoggingContext.Current;
                var evt = new LlmTraceEvent
                {
                    TsUtc = ts,
                    TraceId = traceId,
                    CorrelationId = ctx?.CorrelationId,
                    Operation = ctx?.Operation,
                    Stage = ctx?.Stage,
                    Provider = provider,
                    Model = model,
                    EventType = "SEND",
                    RequestId = requestId,
                    Url = url,
                    Method = method,
                    PayloadJson = payloadText
                };
                // Attach captured prompts to the in-memory event so UI/live subscribers can show them
                try { evt.SystemPrompt = systemPrompt; evt.UserPrompt = userPrompt; } catch { }

                BufferAndEmit(evt);
                AppendJsonLine(evt);
                AppendTranscriptSend(evt, systemPrompt, userPrompt);
                EmitTraceLine(BuildTraceLineSend(evt, systemPrompt, userPrompt));
            }
            catch
            {
                // swallow all exceptions to avoid impacting callers
            }
        }

        public static void LogRecv(string traceId, string provider, string model, string url, int? statusCode, string responseText, string assistantText, long? elapsedMs, JToken responseJson = null, string requestId = null)
        {
            if (!Enabled) return;
            try
            {
                if (string.IsNullOrWhiteSpace(traceId))
                {
                    traceId = GetOrCreateTraceIdFromContext();
                    if (string.IsNullOrWhiteSpace(traceId)) traceId = Guid.NewGuid().ToString("N").Substring(0, 12);
                }
                var ts = DateTime.UtcNow;
                var ctx = LoggingContext.Current;
                var evt = new LlmTraceEvent
                {
                    TsUtc = ts,
                    TraceId = traceId,
                    CorrelationId = ctx?.CorrelationId,
                    Operation = ctx?.Operation,
                    Stage = ctx?.Stage,
                    Provider = provider,
                    Model = model,
                    EventType = "RECV",
                    RequestId = requestId,
                    Url = url,
                    Method = "POST",
                    StatusCode = statusCode,
                    ElapsedMs = elapsedMs,
                    ResponseText = responseText,
                    ResponseJson = responseJson,
                    AssistantText = assistantText
                };

                BufferAndEmit(evt);
                AppendJsonLine(evt);
                AppendTranscriptRecv(evt, assistantText);
                EmitTraceLine(BuildTraceLineRecv(evt));
            }
            catch
            {
                // swallow all exceptions to avoid impacting callers
            }
        }

        public static IReadOnlyList<LlmTraceEvent> GetRecentEvents(int max = MaxBufferedEvents)
        {
            try
            {
                lock (_bufferLock)
                {
                    if (_buffer.Count == 0) return Array.Empty<LlmTraceEvent>();
                    var take = Math.Min(max, _buffer.Count);
                    var skip = Math.Max(0, _buffer.Count - take);
                    return _buffer.Skip(skip).Select(CloneEvent).ToList();
                }
            }
            catch
            {
                return Array.Empty<LlmTraceEvent>();
            }
        }

        private static void BufferAndEmit(LlmTraceEvent evt)
        {
            try
            {
                AppendToBuffer(evt);
                EmitTraceEvent(evt);
            }
            catch
            {
                // swallow
            }
        }

        private static void AppendToBuffer(LlmTraceEvent evt)
        {
            if (evt == null) return;
            lock (_bufferLock)
            {
                _buffer.Add(evt);
                if (_buffer.Count > MaxBufferedEvents)
                {
                    var remove = _buffer.Count - MaxBufferedEvents;
                    if (remove > 0) _buffer.RemoveRange(0, remove);
                }
            }
        }

        private static LlmTraceEvent CloneEvent(LlmTraceEvent evt)
        {
            if (evt == null) return null;
            return new LlmTraceEvent
            {
                TsUtc = evt.TsUtc,
                TraceId = evt.TraceId,
                CorrelationId = evt.CorrelationId,
                Operation = evt.Operation,
                Stage = evt.Stage,
                Provider = evt.Provider,
                Model = evt.Model,
                EventType = evt.EventType,
                Url = evt.Url,
                Method = evt.Method,
                RequestId = evt.RequestId,
                StatusCode = evt.StatusCode,
                ElapsedMs = evt.ElapsedMs,
                PayloadJson = evt.PayloadJson,
                ResponseText = evt.ResponseText,
                ResponseJson = evt.ResponseJson,
                AssistantText = evt.AssistantText
                    , SystemPrompt = evt.SystemPrompt
                    , UserPrompt = evt.UserPrompt
            };
        }

        private static JObject BuildHttp(string url, string method, int? statusCode)
        {
            var http = new JObject();
            if (!string.IsNullOrWhiteSpace(url)) http["url"] = url;
            if (!string.IsNullOrWhiteSpace(method)) http["method"] = method;
            if (statusCode.HasValue) http["statusCode"] = statusCode.Value;
            return http.HasValues ? http : null;
        }

        private static void AppendJsonLine(LlmTraceEvent evt)
        {
            try
            {
                if (evt == null || string.IsNullOrWhiteSpace(evt.TraceId)) return;
                var (jsonlPath, _) = GetTracePaths(evt.TraceId);
                if (string.IsNullOrWhiteSpace(jsonlPath)) return;
                var lck = _locks.GetOrAdd(evt.TraceId ?? string.Empty, _ => new object());
                var json = ToJson(evt);
                if (json == null) return;
                lock (lck)
                {
                    File.AppendAllText(jsonlPath, json.ToString(Formatting.None) + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // swallow
            }
        }

        private static void AppendTranscriptSend(LlmTraceEvent evt, string systemPrompt, string userPrompt)
        {
            try
            {
                if (evt == null || string.IsNullOrWhiteSpace(evt.TraceId)) return;
                var (_, transcriptPath) = GetTracePaths(evt.TraceId);
                if (string.IsNullOrWhiteSpace(transcriptPath)) return;
                var sb = new StringBuilder();
                sb.Append('[').Append(evt.TsUtc.ToString("o")).Append("] ");
                sb.Append("META provider=").Append(evt.Provider ?? "-");
                sb.Append(" model=").Append(string.IsNullOrWhiteSpace(evt.Model) ? "-" : evt.Model);
                if (!string.IsNullOrWhiteSpace(evt.CorrelationId)) sb.Append(" corr=").Append(evt.CorrelationId);
                if (!string.IsNullOrWhiteSpace(evt.Operation)) sb.Append(" op=").Append(evt.Operation);
                if (!string.IsNullOrWhiteSpace(evt.Stage)) sb.Append(" stage=").Append(evt.Stage);
                if (!string.IsNullOrWhiteSpace(evt.RequestId)) sb.Append(" req=").Append(evt.RequestId);
                sb.Append(" trace=").Append(evt.TraceId);
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

                var lck = _locks.GetOrAdd(evt.TraceId ?? string.Empty, _ => new object());
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

        private static void AppendTranscriptRecv(LlmTraceEvent evt, string assistantText)
        {
            try
            {
                if (evt == null || string.IsNullOrWhiteSpace(evt.TraceId)) return;
                var (_, transcriptPath) = GetTracePaths(evt.TraceId);
                if (string.IsNullOrWhiteSpace(transcriptPath)) return;
                var sb = new StringBuilder();
                sb.AppendLine("ASSISTANT:");
                sb.AppendLine(assistantText ?? string.Empty);
                sb.AppendLine();
                sb.AppendLine("--- END TURN ---");
                sb.AppendLine();

                var lck = _locks.GetOrAdd(evt.TraceId ?? string.Empty, _ => new object());
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

        private static void EmitTraceLine(string line)
        {
            try { OnTraceLine?.Invoke(line); } catch { }
        }

        private static void EmitTraceEvent(LlmTraceEvent evt)
        {
            try { OnTraceEvent?.Invoke(CloneEvent(evt)); } catch { }
        }

        private static string BuildTraceLineSend(LlmTraceEvent evt, string systemPrompt, string userPrompt)
        {
            var sb = new StringBuilder();
            sb.Append("[SEND ").Append(evt?.TsUtc.ToString("o")).Append("] ");
            sb.Append("provider=").Append(evt?.Provider ?? "-").Append(" model=").Append(string.IsNullOrWhiteSpace(evt?.Model) ? "-" : evt.Model);
            if (!string.IsNullOrWhiteSpace(evt?.CorrelationId)) sb.Append(" corr=").Append(evt.CorrelationId);
            if (!string.IsNullOrWhiteSpace(evt?.Operation)) sb.Append(" op=").Append(evt.Operation);
            if (!string.IsNullOrWhiteSpace(evt?.Stage)) sb.Append(" stage=").Append(evt.Stage);
            if (!string.IsNullOrWhiteSpace(evt?.RequestId)) sb.Append(" req=").Append(evt.RequestId);
            sb.Append(" trace=").Append(evt?.TraceId);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                sb.AppendLine("SYSTEM:");
                sb.AppendLine(systemPrompt);
            }
            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                if (!string.IsNullOrWhiteSpace(systemPrompt)) sb.AppendLine();
                sb.AppendLine("USER:");
                sb.AppendLine(userPrompt);
            }
            return sb.ToString();
        }

        private static string BuildTraceLineRecv(LlmTraceEvent evt)
        {
            var sb = new StringBuilder();
            sb.Append("[RECV ").Append(evt?.TsUtc.ToString("o")).Append("] ");
            sb.Append("provider=").Append(evt?.Provider ?? "-").Append(" model=").Append(string.IsNullOrWhiteSpace(evt?.Model) ? "-" : evt.Model);
            if (evt?.StatusCode != null) sb.Append(" status=").Append(evt.StatusCode.Value);
            if (evt?.ElapsedMs != null) sb.Append(" elapsedMs=").Append(evt.ElapsedMs.Value);
            if (!string.IsNullOrWhiteSpace(evt?.CorrelationId)) sb.Append(" corr=").Append(evt.CorrelationId);
            if (!string.IsNullOrWhiteSpace(evt?.Operation)) sb.Append(" op=").Append(evt.Operation);
            if (!string.IsNullOrWhiteSpace(evt?.Stage)) sb.Append(" stage=").Append(evt.Stage);
            if (!string.IsNullOrWhiteSpace(evt?.RequestId)) sb.Append(" req=").Append(evt.RequestId);
            sb.Append(" trace=").Append(evt?.TraceId);
            sb.AppendLine();
            sb.AppendLine("ASSISTANT:");
            sb.AppendLine(evt?.AssistantText ?? string.Empty);
            return sb.ToString();
        }

        private static JObject ToJson(LlmTraceEvent evt)
        {
            if (evt == null) return null;
            try
            {
                var j = new JObject
                {
                    ["tsUtc"] = evt.TsUtc.ToString("o")
                };
                if (!string.IsNullOrWhiteSpace(evt.TraceId)) j["traceId"] = evt.TraceId;
                if (!string.IsNullOrWhiteSpace(evt.CorrelationId)) j["correlationId"] = evt.CorrelationId;
                if (!string.IsNullOrWhiteSpace(evt.Operation)) j["operation"] = evt.Operation;
                if (!string.IsNullOrWhiteSpace(evt.Stage)) j["stage"] = evt.Stage;
                if (!string.IsNullOrWhiteSpace(evt.Provider)) j["provider"] = evt.Provider;
                if (!string.IsNullOrWhiteSpace(evt.Model)) j["model"] = evt.Model;
                if (!string.IsNullOrWhiteSpace(evt.EventType)) j["eventType"] = evt.EventType;
                if (!string.IsNullOrWhiteSpace(evt.EventType)) j["event"] = evt.EventType; // backward compatibility
                if (!string.IsNullOrWhiteSpace(evt.Url)) j["url"] = evt.Url;
                if (!string.IsNullOrWhiteSpace(evt.Method)) j["method"] = evt.Method;
                if (!string.IsNullOrWhiteSpace(evt.RequestId)) j["requestId"] = evt.RequestId;
                if (evt.StatusCode.HasValue) j["statusCode"] = evt.StatusCode.Value;
                if (evt.ElapsedMs.HasValue) j["elapsedMs"] = evt.ElapsedMs.Value;
                if (!string.IsNullOrEmpty(evt.PayloadJson)) j["payloadJson"] = evt.PayloadJson;
                if (!string.IsNullOrEmpty(evt.SystemPrompt)) j["systemPrompt"] = evt.SystemPrompt;
                if (!string.IsNullOrEmpty(evt.UserPrompt)) j["userPrompt"] = evt.UserPrompt;
                if (!string.IsNullOrEmpty(evt.ResponseText)) j["responseText"] = evt.ResponseText;
                if (!string.IsNullOrEmpty(evt.AssistantText)) j["assistantText"] = evt.AssistantText;
                var http = BuildHttp(evt.Url, evt.Method, evt.StatusCode);
                if (http != null) j["http"] = http;
                if (evt.ResponseJson != null)
                {
                    try
                    {
                        if (evt.ResponseJson is JToken token)
                        {
                            j["responseJson"] = token;
                        }
                        else
                        {
                            j["responseJson"] = JToken.FromObject(evt.ResponseJson);
                        }
                    }
                    catch { }
                }
                return j;
            }
            catch
            {
                return null;
            }
        }

        private static (string jsonlPath, string transcriptPath) GetTracePaths(string traceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(traceId)) return (null, null);
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
            return Enabled;
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
                return string.Equals(env.Trim(), "1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
