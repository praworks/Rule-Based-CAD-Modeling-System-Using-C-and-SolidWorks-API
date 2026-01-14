using System;
using System.Collections.Generic;
using System.Threading;

namespace AICAD.Services.Logging
{
    /// <summary>
    /// Carries correlation metadata across the pipeline for consistent, structured logging.
    /// </summary>
    internal sealed class LoggingContext
    {
        private static readonly AsyncLocal<LoggingContext> _current = new AsyncLocal<LoggingContext>();
        public static LoggingContext Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public string CorrelationId { get; set; }
        public string SessionId { get; set; }
        public string DocumentId { get; set; }
        public string Operation { get; set; }
        public string Provider { get; set; }
        public string Stage { get; set; }
        public string ParentId { get; set; }
        public bool? Retry { get; set; }
        public bool? Fallback { get; set; }
        public string Result { get; set; }
        public string ErrorCategory { get; set; }
        public bool? UserVisible { get; set; }
        public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.UtcNow;

        public IDictionary<string, object> ToScopeDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Add(dict, "correlationId", CorrelationId);
            Add(dict, "sessionId", SessionId);
            Add(dict, "documentId", DocumentId);
            Add(dict, "operation", Operation);
            Add(dict, "provider", Provider);
            Add(dict, "stage", Stage);
            Add(dict, "parentId", ParentId);
            if (Retry.HasValue) dict["retry"] = Retry.Value;
            if (Fallback.HasValue) dict["fallback"] = Fallback.Value;
            if (!string.IsNullOrWhiteSpace(Result)) dict["result"] = Result;
            if (!string.IsNullOrWhiteSpace(ErrorCategory)) dict["errorCategory"] = ErrorCategory;
            if (UserVisible.HasValue) dict["userVisible"] = UserVisible.Value;
            return dict;
        }

        public LoggingContext CloneForChild(string operation = null, string provider = null)
        {
            return new LoggingContext
            {
                CorrelationId = CorrelationId,
                SessionId = SessionId,
                DocumentId = DocumentId,
                Operation = operation ?? Operation,
                Provider = provider ?? Provider,
                ParentId = CorrelationId,
                Retry = Retry,
                Fallback = Fallback,
                Result = Result,
                ErrorCategory = ErrorCategory,
                UserVisible = UserVisible,
                StartTimeUtc = DateTimeOffset.UtcNow
            };
        }

        public long GetElapsedMs(DateTimeOffset? endUtc = null)
        {
            var end = endUtc ?? DateTimeOffset.UtcNow;
            var ms = (long)(end - StartTimeUtc).TotalMilliseconds;
            return ms < 0 ? 0 : ms;
        }

        private static void Add(IDictionary<string, object> dict, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                dict[key] = value;
        }
    }
}
