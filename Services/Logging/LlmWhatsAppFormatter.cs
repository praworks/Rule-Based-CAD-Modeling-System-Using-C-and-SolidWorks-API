using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AICAD.UI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AICAD.Services.Logging
{
    /// <summary>
    /// Formats structured LLM trace events into chat-friendly messages for the Exchange window.
    /// </summary>
    internal static class LlmWhatsAppFormatter
    {
        public static List<ChatMessage> ToChatMessages(IEnumerable<LlmTraceEvent> events)
        {
            var result = new List<ChatMessage>();
            if (events == null) return result;
            try
            {
                foreach (var evt in events.OrderBy(e => e?.TsUtc))
                {
                    var chat = ToChatMessage(evt);
                    if (chat != null) result.Add(chat);
                }
            }
            catch
            {
                // swallow; return what we have
            }
            return result;
        }

        public static ChatMessage ToChatMessage(LlmTraceEvent evt)
        {
            if (evt == null) return null;
            try
            {
                var isSend = string.Equals(evt.EventType, "SEND", StringComparison.OrdinalIgnoreCase);
                var meta = BuildMeta(evt);
                var body = isSend ? BuildSendBody(evt) : BuildRecvBody(evt);
                var sender = isSend ? "You" : "LLM";
                return new ChatMessage
                {
                    IsOutgoing = isSend,
                    Sender = sender,
                    Meta = meta,
                    Body = body,
                    EventType = evt.EventType,
                    TraceId = evt.TraceId,
                    CorrelationId = evt.CorrelationId,
                    Provider = evt.Provider,
                    Model = evt.Model,
                    Stage = evt.Stage,
                    TemplateKey = evt.TemplateKey,
                    SystemPromptKey = evt.SystemPromptKey,
                    Operation = evt.Operation,
                    RequestId = evt.RequestId,
                    StatusCode = evt.StatusCode,
                    ElapsedMs = evt.ElapsedMs,
                    TsUtc = evt.TsUtc
                };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildMeta(LlmTraceEvent evt)
        {
            try
            {
                var parts = new List<string>();
                parts.Add(SafeTime(evt.TsUtc));
                var providerModel = $"{(string.IsNullOrWhiteSpace(evt.Provider) ? "-" : evt.Provider)} / {(string.IsNullOrWhiteSpace(evt.Model) ? "-" : evt.Model)}";
                parts.Add(providerModel);
                if (!string.IsNullOrWhiteSpace(evt.Operation)) parts.Add("op=" + evt.Operation);
                if (!string.IsNullOrWhiteSpace(evt.Stage))
                {
                    parts.Add("stage=" + evt.Stage);
                }
                if (!string.IsNullOrWhiteSpace(evt.TemplateKey)) parts.Add("tpl=" + evt.TemplateKey);
                if (!string.IsNullOrWhiteSpace(evt.SystemPromptKey)) parts.Add("sys=" + evt.SystemPromptKey);
                if (!string.IsNullOrWhiteSpace(evt.CorrelationId)) parts.Add("corr=" + evt.CorrelationId);
                if (!string.IsNullOrWhiteSpace(evt.TraceId)) parts.Add("trace=" + evt.TraceId);
                if (!string.IsNullOrWhiteSpace(evt.RequestId)) parts.Add("req=" + evt.RequestId);
                if (evt.StatusCode.HasValue) parts.Add("status=" + evt.StatusCode.Value);
                if (evt.ElapsedMs.HasValue) parts.Add("elapsedMs=" + evt.ElapsedMs.Value);
                return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            }
            catch
            {
                return string.Empty;
            }
        }

        private const int PayloadMaxLength = 50_000;

        private static string BuildSendBody(LlmTraceEvent evt)
        {
            var sb = new StringBuilder();
            try
            {
                var payload = evt?.PayloadJson;
                var parsedPayload = TryParsePayload(payload);

                if (!string.IsNullOrWhiteSpace(evt?.SystemPrompt))
                {
                    sb.AppendLine("SYSTEM:");
                    sb.AppendLine(evt.SystemPrompt);
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(evt?.UserPrompt))
                {
                    sb.AppendLine("USER:");
                    sb.AppendLine(evt.UserPrompt);
                    sb.AppendLine();
                }

                var messageSnippet = TryFormatMessages(parsedPayload);
                if (!string.IsNullOrWhiteSpace(messageSnippet))
                {
                    sb.AppendLine(messageSnippet);
                    sb.AppendLine();
                }

                var payloadSection = FormatPayloadSection(payload, parsedPayload);
                if (!string.IsNullOrWhiteSpace(payloadSection))
                {
                    sb.AppendLine(payloadSection);
                }

                return sb.ToString().TrimEnd();
            }
            catch
            {
                return evt?.PayloadJson ?? string.Empty;
            }
        }

        private static JToken TryParsePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            try
            {
                return JToken.Parse(payload);
            }
            catch
            {
                return null;
            }
        }

        private static string TryFormatMessages(JToken payload)
        {
            if (payload == null) return null;
            var messages = payload["messages"] as JArray;
            if (messages == null || messages.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                var role = msg?["role"]?.ToString() ?? "user";
                var content = ExtractContentText(msg?["content"]);
                sb.AppendLine(role.ToUpperInvariant() + ":");
                sb.AppendLine(content);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatPayloadSection(string payload, JToken parsedPayload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            var builder = new StringBuilder();
            builder.AppendLine("PAYLOAD JSON:");
            var formatted = parsedPayload != null
                ? parsedPayload.ToString(Formatting.Indented)
                : payload;
            builder.AppendLine(TruncateWithNotice(formatted, PayloadMaxLength));
            return builder.ToString().TrimEnd();
        }

        private static string TruncateWithNotice(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "\n... (truncated)";
        }

        private static string BuildRecvBody(LlmTraceEvent evt)
        {
            try
            {
                var sb = new StringBuilder();
                var assistant = evt?.AssistantText;
                if (!string.IsNullOrWhiteSpace(assistant))
                {
                    sb.AppendLine("ASSISTANT:");
                    sb.Append(assistant);
                    return sb.ToString();
                }

                var jsonText = TryFormatJson(evt?.ResponseJson, evt?.ResponseText);
                if (!string.IsNullOrEmpty(jsonText))
                {
                    sb.AppendLine("ASSISTANT:");
                    sb.Append(jsonText);
                    return sb.ToString();
                }

                if (!string.IsNullOrWhiteSpace(evt?.ResponseText))
                {
                    sb.AppendLine("ASSISTANT:");
                    sb.Append(evt.ResponseText);
                    return sb.ToString();
                }
            }
            catch
            {
                // swallow
            }

            return string.Empty;
        }

        private static string TryFormatJson(object responseJson, string fallbackText)
        {
            if (responseJson == null && string.IsNullOrWhiteSpace(fallbackText)) return null;
            try
            {
                if (responseJson is JToken tkn)
                {
                    return tkn.ToString(Formatting.Indented);
                }
                if (responseJson != null)
                {
                    return JToken.FromObject(responseJson).ToString(Formatting.Indented);
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrWhiteSpace(fallbackText))
                {
                    var t = JToken.Parse(fallbackText);
                    return t.ToString(Formatting.Indented);
                }
            }
            catch { }

            return fallbackText;
        }

        private static string ExtractContentText(JToken contentToken)
        {
            if (contentToken == null) return string.Empty;
            try
            {
                if (contentToken.Type == JTokenType.String)
                {
                    return contentToken.ToString();
                }
                if (contentToken is JArray arr)
                {
                    var sb = new StringBuilder();
                    foreach (var part in arr)
                    {
                        var txt = part?["text"]?.ToString();
                        if (string.IsNullOrWhiteSpace(txt))
                        {
                            txt = part?.ToString();
                        }
                        if (!string.IsNullOrWhiteSpace(txt))
                        {
                            if (sb.Length > 0) sb.AppendLine();
                            sb.Append(txt);
                        }
                    }
                    return sb.Length > 0 ? sb.ToString() : contentToken.ToString(Formatting.None);
                }
                if (contentToken.Type == JTokenType.Object)
                {
                    var objText = contentToken["text"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(objText)) return objText;
                }
                return contentToken.ToString(Formatting.None);
            }
            catch
            {
                return contentToken.ToString();
            }
        }

        private static string SafeTime(DateTime utc)
        {
            try
            {
                return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
