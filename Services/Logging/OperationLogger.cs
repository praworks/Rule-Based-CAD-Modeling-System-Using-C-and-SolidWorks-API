using System;
using Microsoft.Extensions.Logging;

namespace AICAD.Services.Logging
{
    /// <summary>
    /// Helper that emits start/end/fail telemetry with timing and redaction.
    /// </summary>
    internal sealed class OperationLogger : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ITelemetrySink _telemetry;
        private readonly LoggingContext _context;
        private readonly string _operation;
        private readonly DateTimeOffset _startUtc;
        private bool _completed;

        private OperationLogger(ILogger logger, ITelemetrySink telemetry, LoggingContext context, string operation)
        {
            _logger = logger;
            _telemetry = telemetry ?? new NullTelemetrySink();
            _context = context ?? new LoggingContext();
            _operation = string.IsNullOrWhiteSpace(operation) ? _context.Operation : operation;
            _startUtc = context?.StartTimeUtc ?? DateTimeOffset.UtcNow;
            Emit("start", null, LogLevel.Information);
        }

        public static OperationLogger Start(ILogger logger, ITelemetrySink telemetry, LoggingContext context, string operation)
        {
            return new OperationLogger(logger, telemetry, context, operation);
        }

        public void MarkSuccess(string result = "success")
        {
            if (_completed) return;
            _context.Result = result ?? "success";
            Emit("end", null, LogLevel.Information);
            _completed = true;
        }

        public void MarkFailure(Exception ex, string message = null, bool userVisible = false)
        {
            if (_completed) return;
            var category = ExceptionClassifier.ShouldSend(ex, _operation, message ?? string.Empty, out var _)
                ? "SendToLLM"
                : "Suppressed";
            _context.ErrorCategory = ExceptionClassifier.IsEnabled() ? category : "Unclassified";
            _context.Result = "failure";
            _context.UserVisible = userVisible;
            Emit("fail", ex, LogLevel.Error, message);
            _completed = true;
        }

        public void Dispose()
        {
            if (_completed) return;
            _context.Result = _context.Result ?? "success";
            Emit("end", null, LogLevel.Information);
            _completed = true;
        }

        private void Emit(string eventType, Exception ex, LogLevel level, string message = null)
        {
            try
            {
                var duration = _context.GetElapsedMs(DateTimeOffset.UtcNow);
                var scope = _context.ToScopeDictionary();
                scope["event"] = eventType;
                scope["durationMs"] = duration;
                if (ex != null)
                {
                    scope["exception"] = ex.GetType().Name;
                    scope["error"] = LogRedactor.Sanitize(ex.Message);
                }

                using (_logger?.BeginScope(scope))
                {
                    var msg = string.IsNullOrWhiteSpace(message) ? _operation : message;
                    if (ex != null)
                        _logger?.Log(level, ex, msg);
                    else
                        _logger?.Log(level, msg);
                }

                _telemetry.Emit(new TelemetryEvent
                {
                    EventType = eventType,
                    CorrelationId = _context.CorrelationId,
                    SessionId = _context.SessionId,
                    DocumentId = _context.DocumentId,
                    Operation = _operation,
                    Provider = _context.Provider,
                    DurationMs = duration,
                    Result = _context.Result,
                    ErrorCategory = _context.ErrorCategory,
                    UserMessage = null,
                    Retry = _context.Retry,
                    Fallback = _context.Fallback,
                    TimestampUtc = DateTimeOffset.UtcNow
                });
            }
            catch
            {
                // Never let logging crash callers
            }
        }
    }
}
