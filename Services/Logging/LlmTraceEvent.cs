using System;

namespace AICAD.Services.Logging
{
    /// <summary>
    /// Structured representation of an LLM trace event used for in-memory buffering and UI replay.
    /// </summary>
    public class LlmTraceEvent
    {
        public DateTime TsUtc { get; set; }
        public string TraceId { get; set; }
        public string CorrelationId { get; set; }
        public string Operation { get; set; }
        public string Stage { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string EventType { get; set; } // "SEND" | "RECV"
        public string Url { get; set; }
        public string Method { get; set; }
        public string RequestId { get; set; }
        public int? StatusCode { get; set; }
        public long? ElapsedMs { get; set; }
        public string PayloadJson { get; set; }
        public string ResponseText { get; set; }
        public object ResponseJson { get; set; }
        public string AssistantText { get; set; }
        // Optional captured prompts (when available) so UI can display exact user/system turns
        public string SystemPrompt { get; set; }
        public string UserPrompt { get; set; }
        public string SystemPromptKey { get; set; }
        public string TemplateKey { get; set; }
    }
}
