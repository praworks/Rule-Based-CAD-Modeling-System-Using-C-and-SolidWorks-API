using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AICAD.Services.Logging;

namespace AICAD.Services
{
    /// <summary>
    /// Adapter to expose GroqClient via ILlmClient.
    /// Uses env var GROQ_ENDPOINT (full URL) or GROQ_MODEL to construct endpoint.
    /// Sends a minimal payload and returns a string extracted from JSON if available.
    /// </summary>
    public class GroqLlmAdapter : ILlmClient, IDisposable
    {
        private readonly GroqClient _g;
        private readonly string _endpoint;
        public string Model { get; private set; }

        public GroqLlmAdapter(string apiKey = null)
        {
            _g = new GroqClient(apiKey);
            // endpoint may be provided via env; prefer full endpoint
            _endpoint = Environment.GetEnvironmentVariable("GROQ_ENDPOINT", EnvironmentVariableTarget.User)
                        ?? Environment.GetEnvironmentVariable("GROQ_ENDPOINT", EnvironmentVariableTarget.Process)
                        ?? Environment.GetEnvironmentVariable("GROQ_ENDPOINT", EnvironmentVariableTarget.Machine)
                        ?? string.Empty;
            Model = Environment.GetEnvironmentVariable("GROQ_MODEL", EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable("GROQ_MODEL", EnvironmentVariableTarget.Process)
                    ?? Environment.GetEnvironmentVariable("GROQ_MODEL", EnvironmentVariableTarget.Machine)
                    ?? "groq";
            // If endpoint is empty but model provided, try a common path (may need override)
            if (string.IsNullOrWhiteSpace(_endpoint))
            {
                _endpoint = $"https://api.groq.com/v1/models/{Model}/chat";
            }
        }

        public void Dispose()
        {
            try { _g.Dispose(); } catch { }
        }

        public async Task<string> GenerateAsync(string prompt)
        {
            var traceId = LlmTraceLogger.GetOrCreateTraceIdFromContext();
            var sw = Stopwatch.StartNew();
            var ct = CancellationToken.None;
            // Minimal payload - Groq APIs vary; use 'input' field for simple requests.
            var payload = new { input = prompt };
            string payloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            LlmTraceLogger.LogSend(traceId, "groq", Model, _endpoint, "POST", payloadJson, null, prompt);
            var resp = await _g.SendAsync(_endpoint, payload, ct).ConfigureAwait(false);
            sw?.Stop();
            if (resp.Success && resp.Json != null)
            {
                // Try common fields
                string assistant = null;
                try
                {
                    if (resp.Json["output"] != null)
                    {
                        assistant = resp.Json["output"].ToString();
                    }
                    else if (resp.Json["text"] != null)
                    {
                        assistant = resp.Json["text"].ToString();
                    }
                    else if (resp.Json["choices"] != null)
                    {
                        var ch = resp.Json["choices"];
                        if (ch.HasValues && ch[0]["message"] != null) assistant = ch[0]["message"].ToString();
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(assistant)) assistant = resp.Body ?? string.Empty;
                LlmTraceLogger.LogRecv(traceId, "groq", Model, _endpoint, resp.StatusCode, resp.Body, assistant, sw?.ElapsedMilliseconds, resp.Json);
                return assistant;
            }
            LlmTraceLogger.LogRecv(traceId, "groq", Model, _endpoint, resp?.StatusCode, resp?.Body, null, sw?.ElapsedMilliseconds, resp?.Json);
            throw new InvalidOperationException("Groq request failed: " + (resp?.ErrorMessage ?? "unknown"));
        }

        public async Task StreamAsync(string prompt, Action<string> onDelta, CancellationToken cancellationToken)
        {
            // Groq client doesn't support streaming in this adapter, so invoke callback with full response
            var response = await GenerateAsync(prompt).ConfigureAwait(false);
            onDelta?.Invoke(response);
        }
    }
}
