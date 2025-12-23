using System;
using System.Threading;
using System.Threading.Tasks;

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
            var ct = CancellationToken.None;
            // Minimal payload — Groq APIs vary; use 'input' field for simple requests.
            var payload = new { input = prompt };
            var resp = await _g.SendAsync(_endpoint, payload, ct).ConfigureAwait(false);
            if (resp.Success && resp.Json != null)
            {
                // Try common fields
                try
                {
                    if (resp.Json["output"] != null)
                    {
                        return resp.Json["output"].ToString();
                    }
                    if (resp.Json["text"] != null) return resp.Json["text"].ToString();
                    if (resp.Json["choices"] != null)
                    {
                        var ch = resp.Json["choices"];
                        if (ch.HasValues && ch[0]["message"] != null) return ch[0]["message"].ToString();
                    }
                }
                catch { }
                // fallback to raw body
                return resp.Body ?? string.Empty;
            }
            throw new InvalidOperationException("Groq request failed: " + (resp?.ErrorMessage ?? "unknown"));
        }
    }
}
