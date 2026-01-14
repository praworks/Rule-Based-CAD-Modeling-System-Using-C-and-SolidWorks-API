using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AICAD.Services.Logging
{
    internal sealed class TelemetryEvent
    {
        public string EventType { get; set; }
        public string CorrelationId { get; set; }
        public string SessionId { get; set; }
        public string DocumentId { get; set; }
        public string Operation { get; set; }
        public string Provider { get; set; }
        public long? DurationMs { get; set; }
        public string Result { get; set; }
        public string ErrorCategory { get; set; }
        public string UserMessage { get; set; }
        public bool? Retry { get; set; }
        public bool? Fallback { get; set; }
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public IDictionary<string, object> Metadata { get; set; }

        public JObject ToJson()
        {
            var obj = new JObject
            {
                ["eventType"] = EventType,
                ["correlationId"] = CorrelationId,
                ["sessionId"] = SessionId,
                ["documentId"] = DocumentId,
                ["operation"] = Operation,
                ["provider"] = Provider,
                ["durationMs"] = DurationMs,
                ["result"] = Result,
                ["errorCategory"] = ErrorCategory,
                ["userMessage"] = UserMessage,
                ["retry"] = Retry,
                ["fallback"] = Fallback,
                ["timestamp"] = TimestampUtc
            };
            if (Metadata != null)
                obj["metadata"] = JObject.FromObject(Metadata);
            return obj;
        }
    }
}
