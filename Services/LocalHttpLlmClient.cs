using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace AICAD.Services
{
    /// <summary>
    /// Calls a local HTTP LLM endpoint that implements an OpenAI-style chat/completions API.
    /// Defaults to http://localhost:1234/v1/chat/completions.
    /// </summary>
    public class LocalHttpLlmClient : ILlmClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private string _model; // Removed 'readonly' so we can update it from the response
        private readonly string _systemPrompt;

        public LocalHttpLlmClient(string endpoint = "http://localhost:1234/v1/chat/completions",
                                  string model = "qwen2.5-coder-3b-instruct",
                                  string systemPrompt = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            // Ensure endpoint includes the API path for OpenAI-style chat completions
            if (!_endpoint.Contains("/v1/chat/completions"))
            {
                if (_endpoint.EndsWith("/"))
                    _endpoint += "v1/chat/completions";
                else
                    _endpoint += "/v1/chat/completions";
            }
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _systemPrompt = systemPrompt;
            _http = new HttpClient();
            // Increase default timeout to 180s to allow slower local LLMs more time (Qwen, Llama-based runtimes)
            _http.Timeout = TimeSpan.FromSeconds(180);
        }

        public string Model => _model;

        public async Task<string> GenerateAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;

            var messages = new System.Collections.Generic.List<object>();
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
                messages.Add(new { role = "system", content = _systemPrompt });
            messages.Add(new { role = "user", content = prompt });

            // Build payload dynamically so we can omit fields (some local servers dislike e.g. negative max_tokens)
            var jPayload = new JObject();
            if (!string.IsNullOrWhiteSpace(_model)) jPayload["model"] = _model;
            jPayload["messages"] = JArray.FromObject(messages);
            jPayload["temperature"] = 0.7;
            jPayload["stream"] = false;

            // Use JsonConvert.SerializeObject to avoid depending on JToken.ToString overloads
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(jPayload);

            HttpResponseMessage resp;
            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    resp = await _http.PostAsync(_endpoint, content).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException tex)
            {
                // More actionable message: local LLM may be busy, crashed, or out of VRAM (Qwen/Llama variants)
                var msg = $"Local LLM request to {_endpoint} timed out after {_http.Timeout.TotalSeconds}s. " +
                          "The local model may be busy, out of VRAM, or the server may be unresponsive. " +
                          "Try restarting the local LLM or lowering the model size.\n" + tex.Message;
                AddinStatusLogger.Error("LocalHttpLlmClient", "Request timed out", tex);
                // Throw with a friendly message so UI can surface it to the user
                throw new TimeoutException(msg, tex);
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LocalHttpLlmClient", "Request failed", ex);
                throw;
            }

            var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                AddinStatusLogger.Error("LocalHttpLlmClient", $"HTTP {(int)resp.StatusCode} from {_endpoint}", new Exception(respText));
                // Provide clearer guidance for common local-server errors
                if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest && respText != null && respText.IndexOf("No models loaded", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException($"Local LLM returned 400: No models loaded. Please load a model in the local LLM instance or change the configured model. Response: {respText}");
                }
                throw new InvalidOperationException($"LLM HTTP error {(int)resp.StatusCode}: {respText}");
            }

            // Try to parse common OpenAI-style chat response shapes.
            try
            {
                    var j = JObject.Parse(respText);

                    // Capture the actual model name returned by the server
                    try
                    {
                        if (j["model"] != null)
                        {
                            _model = j["model"].ToString();
                        }
                    }
                    catch { }

                    var choices = j["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    var first = choices[0] as JObject;
                    var message = first?["message"] as JObject;
                    if (message != null && message["content"] != null)
                    {
                        return message["content"].ToString();
                    }
                    if (first?["text"] != null) return first["text"].ToString();
                }

                if (j["result"] != null && j["result"].Type == JTokenType.String) return j["result"].ToString();
                if (j["output"] != null && j["output"].Type == JTokenType.String) return j["output"].ToString();
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LocalHttpLlmClient", "Failed to parse LLM response", ex);
            }

            // Fallback: return raw response text
            return respText;
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
