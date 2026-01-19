using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using AICAD.Services.Logging;

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
                // Prefer explicit argument, then PromptCatalog default, then AICAD_SYSTEM_PROMPT env var as a last resort.
                // This enforces the catalog (`Config/PromptCatalog.json`) as the single source of truth by default.
                var envPrompt = System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                     ?? System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process);
                var catalogDefault = PromptHandler.DEFAULT_SYSTEM_PROMPT;
                _systemPrompt = !string.IsNullOrWhiteSpace(systemPrompt) ? systemPrompt
                                    : (!string.IsNullOrWhiteSpace(catalogDefault) ? catalogDefault
                                        : (!string.IsNullOrWhiteSpace(envPrompt) ? envPrompt : string.Empty));

            // Warn if the resolved system prompt is empty to help diagnose missing catalog/env issues
            if (string.IsNullOrWhiteSpace(_systemPrompt))
            {
                try
                {
                    var logger = Logging.LoggerFactoryBuilder.Factory.CreateLogger("GroqLlmClient");
                    var ctx = Logging.LoggingContext.Current ?? new Logging.LoggingContext { CorrelationId = "-", Operation = "LLM", Provider = "groq", Stage = "EXECUTE" };
                    logger.LogWithContext(Microsoft.Extensions.Logging.LogLevel.Warning, ctx, "GroqLlmClient constructed with empty system prompt; checked AICAD_SYSTEM_PROMPT and PromptHandler.DEFAULT_SYSTEM_PROMPT.");
                }
                catch { }
            }
        }

        public async Task<string> GenerateAsync(string prompt)
        {
            var logger = Logging.LoggerFactoryBuilder.Factory.CreateLogger("GroqLlmClient");
            var ctx = Logging.LoggingContext.Current ?? new Logging.LoggingContext { CorrelationId = "-", Operation = "LLM", Provider = "groq", Stage = "EXECUTE" };
            logger.LogWithContext(Microsoft.Extensions.Logging.LogLevel.Information, ctx, $"LLM_SEND groq promptLen={prompt?.Length ?? 0} promptHash={Logging.LogRedactor.StableHash(prompt ?? string.Empty)} promptPreview={Logging.LogRedactor.Truncate(prompt, 200)}");
            var traceId = LlmTraceLogger.GetOrCreateTraceIdFromContext();
            var startTime = DateTime.UtcNow;

            // If Groq is busy/anti-burst triggered, wait and retry automatically.
            // Configurable maximum total wait (seconds) via GROQ_MAX_WAIT_SECONDS (default 120s).
            var maxWaitSecondsStr = Environment.GetEnvironmentVariable("GROQ_MAX_WAIT_SECONDS", EnvironmentVariableTarget.User)
                                     ?? Environment.GetEnvironmentVariable("GROQ_MAX_WAIT_SECONDS", EnvironmentVariableTarget.Process);
            int maxWaitSeconds = 120;
            if (!string.IsNullOrWhiteSpace(maxWaitSecondsStr) && int.TryParse(maxWaitSecondsStr, out int parsed) && parsed >= 0)
            {
                maxWaitSeconds = parsed;
            }

            var totalWait = TimeSpan.Zero;
            var endpoint = "https://api.groq.com/openai/v1/chat/completions";

            while (true)
            {
                var rateLimitCheck = GroqRateLimiter.CheckRequest();
                if (rateLimitCheck.Allowed)
                {
                    break; // allowed to proceed
                }

                // Not allowed: determine suggested wait
                var suggested = rateLimitCheck.SuggestedWait ?? TimeSpan.FromSeconds(2);
                var remainingAllowedWait = TimeSpan.FromSeconds(maxWaitSeconds) - totalWait;
                if (remainingAllowedWait <= TimeSpan.Zero)
                {
                    AddinStatusLogger.Log("GroqLlmClient", $"Groq rate limit persists and max wait exceeded. Reason: {rateLimitCheck.Reason}");
                    throw new Exception($"Groq rate limit: {rateLimitCheck.Reason} (max wait exceeded)");
                }

                var waitFor = suggested <= remainingAllowedWait ? suggested : remainingAllowedWait;
                // Log and delay
                logger.LogWithContext(Microsoft.Extensions.Logging.LogLevel.Warning, ctx, $"LLM_WAIT groq waitMs={waitFor.TotalMilliseconds:F0} reason={rateLimitCheck.Reason}");
                try
                {
                    await Task.Delay(waitFor).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    throw new Exception("Groq generation cancelled during wait");
                }

                totalWait = DateTime.UtcNow - startTime;
                // Loop and re-check
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

            string payloadJson = null;
            try { payloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(payload); } catch { }
            LlmTraceLogger.LogSend(traceId, "groq", _model, endpoint, "POST", payloadJson, _systemPrompt, prompt);

            var response = await _client.SendAsync(endpoint, payload, CancellationToken.None).ConfigureAwait(false);

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
                    logger.LogWithContext(Microsoft.Extensions.Logging.LogLevel.Information, ctx, $"LLM_RECV groq status=200 elapsedMs={(DateTime.UtcNow-startTime).TotalMilliseconds:F0} replyLen={content?.Length ?? 0} replyHash={Logging.LogRedactor.StableHash(content ?? string.Empty)} replyPreview={Logging.LogRedactor.Truncate(content,200)}");
                    try
                    {
                        LlmTraceLogger.LogRecv(traceId, "groq", _model, endpoint, response.StatusCode, response.Body, content, (long?)(DateTime.UtcNow - startTime).TotalMilliseconds, response.Json);
                    }
                    catch { }
                    return content;
                }
            }

            logger.LogWithContext(Microsoft.Extensions.Logging.LogLevel.Error, ctx, $"LLM_RECV groq status={(response.Success ? "200" : "error")} elapsedMs={(DateTime.UtcNow-startTime).TotalMilliseconds:F0} error={response.ErrorMessage}");
            try
            {
                LlmTraceLogger.LogRecv(traceId, "groq", _model, endpoint, response?.StatusCode, response?.Body, response?.Body, (long?)(DateTime.UtcNow - startTime).TotalMilliseconds, response?.Json);
            }
            catch { }
            throw new Exception(response.ErrorMessage ?? "Groq generation failed");
        }

        public async Task StreamAsync(string prompt, System.Action<string> onDelta, System.Threading.CancellationToken cancellationToken)
        {
            // Current Groq client wrapper does not implement streaming; fallback to single-shot
            var text = await GenerateAsync(prompt).ConfigureAwait(false);
            onDelta?.Invoke(text ?? string.Empty);
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
