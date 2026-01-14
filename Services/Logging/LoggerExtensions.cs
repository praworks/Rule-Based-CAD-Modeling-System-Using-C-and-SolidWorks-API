using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace AICAD.Services.Logging
{
    internal static class LoggerExtensions
    {
        public static void LogWithContext(this ILogger logger, LogLevel level, LoggingContext context, string message, Exception ex = null, IDictionary<string, object> extra = null)
        {
            try
            {
                var scope = context?.ToScopeDictionary() ?? new Dictionary<string, object>();
                if (extra != null)
                {
                    foreach (var kvp in extra)
                    {
                        if (!scope.ContainsKey(kvp.Key))
                            scope[kvp.Key] = kvp.Value;
                    }
                }
                using (logger.BeginScope(scope))
                {
                    if (ex != null)
                        logger.Log(level, ex, LogRedactor.Sanitize(message));
                    else
                        logger.Log(level, LogRedactor.Sanitize(message));
                }
            }
            catch
            {
                // Never throw from logging
            }
        }

        public static void LogException(this ILogger logger, LoggingContext context, Exception ex, string message, bool userVisible = false)
        {
            try
            {
                var category = ExceptionClassifier.IsEnabled()
                    ? (ExceptionClassifier.ShouldSend(ex, context?.Operation ?? "op", message ?? string.Empty, out var reason) ? reason : "Suppressed")
                    : "Unclassified";
                var userMessage = FriendlyErrorTranslator.TranslateError(ex?.Message ?? message);
                var extra = new Dictionary<string, object>
                {
                    ["errorCategory"] = category,
                    ["userMessage"] = userMessage,
                    ["exceptionType"] = ex?.GetType().Name ?? "Exception",
                    ["userVisible"] = userVisible,
                    ["stage"] = context?.Stage ?? context?.Operation ?? "op"
                };
                if (context != null) context.ErrorCategory = category;
                logger.LogWithContext(LogLevel.Error, context, message, ex, extra);
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }
}
