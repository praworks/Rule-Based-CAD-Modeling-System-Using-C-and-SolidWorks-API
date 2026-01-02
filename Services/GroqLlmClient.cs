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

        public GroqLlmClient(string apiKey = null, string model = "llama3-8b-8192", string systemPrompt = null)
        {
            _client = new GroqClient(apiKey);
            _model = !string.IsNullOrWhiteSpace(model) ? model : "llama3-8b-8192";
            _systemPrompt = systemPrompt ?? "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text.";
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
    }
}
