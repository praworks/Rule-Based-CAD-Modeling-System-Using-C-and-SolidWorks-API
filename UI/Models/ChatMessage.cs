using System;

namespace AICAD.UI.Models
{
    /// <summary>
    /// Simple chat message model for the Exchange window conversation view.
    /// </summary>
    public class ChatMessage
    {
        public bool IsOutgoing { get; set; }
        public string Sender { get; set; }
        public string Meta { get; set; }
        public string Body { get; set; }
        public string EventType { get; set; }
        public string TraceId { get; set; }
        public string CorrelationId { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string Stage { get; set; }
        public string TemplateKey { get; set; }
        public string SystemPromptKey { get; set; }
        public string Operation { get; set; }
        public string RequestId { get; set; }
        public int? StatusCode { get; set; }
        public long? ElapsedMs { get; set; }
        public DateTime TsUtc { get; set; }
    }
}
