using System;
using Microsoft.Extensions.Logging;

namespace AICAD.Services.Logging
{
    internal static class LoggerFactoryBuilder
    {
        private static readonly object Sync = new object();
        private static ILoggerFactory _factory;
        private static ITelemetrySink _telemetry = new StatusTelemetrySink();

        public static ILoggerFactory Factory
        {
            get
            {
                EnsureFactory();
                return _factory;
            }
        }

        public static ITelemetrySink TelemetrySink
        {
            get
            {
                EnsureFactory();
                return _telemetry ?? new NullTelemetrySink();
            }
            set
            {
                _telemetry = value ?? new NullTelemetrySink();
            }
        }

        public static ILogger<T> CreateLogger<T>()
        {
            return Factory.CreateLogger<T>();
        }

        private static void EnsureFactory()
        {
            if (_factory != null) return;
            lock (Sync)
            {
                if (_factory != null) return;
                _factory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddProvider(new StatusLoggerProvider());
                });
            }
        }
    }
}
