using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    public class GroqLlmClient : ILlmClient, IDisposable
    {
        private readonly GroqClient _client;
        private readonly string _model;
        private readonly string _systemPrompt;

        public string Model => _model;

        public GroqLlmClient(string apiKey = null, string model = "llama-3.3-70b-versatile", string systemPrompt = null)
        {
            _client = new GroqClient(apiKey);
            _model = !string.IsNullOrWhiteSpace(model) ? model : "llama-3.3-70b-versatile";
            // Prefer explicit argument, then AICAD_SYSTEM_PROMPT env var, then hard-coded default
            var envPrompt = System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                            ?? System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process);
            _systemPrompt = systemPrompt ?? envPrompt ?? ClarificationService.DEFAULT_SYSTEM_PROMPT;
        }

        public async Task<string> GenerateAsync(string prompt)
        {
            // Check rate limits BEFORE making the request
            var rateLimitCheck = GroqRateLimiter.CheckRequest();
            if (!rateLimitCheck.Allowed)
            {
                var waitMsg = rateLimitCheck.SuggestedWait.HasValue 
                    ? $" (Wait {rateLimitCheck.SuggestedWait.Value.TotalSeconds:F0}s)" 
                    : "";
                throw new Exception($"Groq rate limit: {rateLimitCheck.Reason}{waitMsg}");
            }

            var payload = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = _systemPrompt },
                    new { role = "user", content = prompt }
                },
                temperature = 0.1,
                max_tokens = 4096,
                stream = false
            };

            var response = await _client.SendAsync("https://api.groq.com/openai/v1/chat/completions", payload, CancellationToken.None);
            
            // Record successful request for rate limiting
            if (response.Success)
            {
                GroqRateLimiter.RecordRequest();
            }

            if (response.Success && response.Json != null)
            {
                var choices = response.Json["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    var content = choices[0]["message"]?["content"]?.ToString();
                    return content;
                }
            }

            throw new Exception(response.ErrorMessage ?? "Groq generation failed");
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        // Backward-compatible wrapper used by older call sites that passed a CancellationToken.
        public async Task<string> SendPromptAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            return await GenerateAsync(prompt).ConfigureAwait(false);
        }
    }
}
