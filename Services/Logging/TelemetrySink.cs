using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AICAD.Services.Logging
{
    internal interface ITelemetrySink
    {
        void Emit(TelemetryEvent evt);
    }

    internal sealed class NullTelemetrySink : ITelemetrySink
    {
        public void Emit(TelemetryEvent evt) { }
    }

    internal sealed class CompositeTelemetrySink : ITelemetrySink
    {
        private readonly IReadOnlyList<ITelemetrySink> _sinks;
        public CompositeTelemetrySink(params ITelemetrySink[] sinks)
        {
            _sinks = sinks ?? Array.Empty<ITelemetrySink>();
        }

        public void Emit(TelemetryEvent evt)
        {
            foreach (var sink in _sinks)
            {
                try { sink?.Emit(evt); } catch { }
            }
        }
    }

    /// <summary>
    /// Writes telemetry as JSON lines through the legacy AddinStatusLogger for compatibility.
    /// </summary>
    internal sealed class StatusTelemetrySink : ITelemetrySink
    {
        public void Emit(TelemetryEvent evt)
        {
            try
            {
                // Single-line human + machine: keep JSON compact without duplicate plain text.
                var json = evt?.ToJson() ?? new JObject();
                json["channel"] = "telemetry";
                AddinStatusLogger.Log(string.Empty, json.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch { }
        }
    }
}
