using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace AICAD.Services.Logging
{
    internal sealed class StatusLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
        private readonly ConcurrentDictionary<string, StatusLogger> _loggers = new ConcurrentDictionary<string, StatusLogger>(StringComparer.Ordinal);

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new StatusLogger(name, () => _scopeProvider));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
        }

        private sealed class StatusLogger : ILogger
        {
            private readonly string _category;
            private readonly Func<IExternalScopeProvider> _scopeProviderFactory;

            public StatusLogger(string category, Func<IExternalScopeProvider> scopeProviderFactory)
            {
                _category = category;
                _scopeProviderFactory = scopeProviderFactory;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return _scopeProviderFactory().Push(state);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                try
                {
                    var message = formatter?.Invoke(state, exception) ?? string.Empty;
                    var sanitized = LogRedactor.Sanitize(message);
                    var scopeData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    _scopeProviderFactory().ForEachScope((scope, dict) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object>> kvpEnumerable)
                        {
                            foreach (var kvp in kvpEnumerable)
                            {
                                if (!dict.ContainsKey(kvp.Key)) dict[kvp.Key] = kvp.Value;
                            }
                        }
                    }, scopeData);

                    var corr = scopeData.TryGetValue("correlationId", out var c) ? c?.ToString() : null;
                    var op = scopeData.TryGetValue("operation", out var o) ? o?.ToString() : null;
                    var stage = scopeData.TryGetValue("stage", out var st) ? st?.ToString() : null;
                    var src = _category;
                    var provider = scopeData.TryGetValue("provider", out var pr) ? pr?.ToString() : null;
                    var levelText = NormalizeLevel(logLevel);

                    var line = $"{levelText} corr={(string.IsNullOrWhiteSpace(corr) ? "-" : corr)} op={(string.IsNullOrWhiteSpace(op) ? src : op)} stage={(string.IsNullOrWhiteSpace(stage) ? "-" : stage)} src={src}";
                    if (!string.IsNullOrWhiteSpace(provider)) line += $" provider={provider}";
                    line += $"  {sanitized}";
                    if (exception != null)
                    {
                        var exLine = $"{exception.GetType().Name}: {exception.Message}";
                        line += " err=" + LogRedactor.Sanitize(exLine);
                    }

                    AddinStatusLogger.Log(string.Empty, line);
                }
                catch
                {
                    // Swallow any logging failures
                }
            }
        }

        private static string NormalizeLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Information: return "INFO";
                case LogLevel.Warning: return "WARN";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Critical: return "CRIT";
                default: return level.ToString().ToUpperInvariant();
            }
        }
    }
}
