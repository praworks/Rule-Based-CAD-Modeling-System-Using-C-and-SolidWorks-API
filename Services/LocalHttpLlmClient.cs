using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace AICAD.Services
{
    /// <summary>
    /// Calls a local HTTP LLM endpoint that implements an OpenAI-style chat/completions API.
    /// Defaults to http://127.0.0.1:1234/v1/chat/completions and the model used in your curl example.
    /// </summary>
    public class LocalHttpLlmClient : ILlmClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _model;
        private string _detectedModel;
        private readonly string _systemPrompt;

        public LocalHttpLlmClient(string endpoint = "http://127.0.0.1:1234/v1/chat/completions",
                                  string model = "google/functiongemma-270m",
                                  string systemPrompt = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _systemPrompt = systemPrompt;
            _http = new HttpClient();
            // Increase default timeout to 180s to allow slower local LLMs more time (Qwen, Llama-based runtimes)
            _http.Timeout = TimeSpan.FromSeconds(180);
        }

        public string Model => _detectedModel ?? _model;

        private async Task<string> DetectOllamaModelAsync(Uri baseUri)
        {
            try
            {
                var tagsUrl = new UriBuilder(baseUri) { Path = "/api/tags" }.Uri.ToString();
                var resp = await _http.GetAsync(tagsUrl).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var j = JObject.Parse(txt);
                var models = j["models"] as JArray;
                if (models != null && models.Count > 0)
                {
                    var first = models[0] as JObject;
                    if (first != null)
                    {
                        var nameToken = first["model"] ?? first["name"];
                        if (nameToken != null) return nameToken.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

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

            // Determine if this endpoint appears to be Ollama (default local port 11434)
            bool useOllama = false;
            string postUrl = _endpoint;
            try
            {
                var uri = new Uri(_endpoint);
                if (uri.Port == 11434)
                {
                    useOllama = true;
                    // Ollama expects POST to /api/generate by default
                    var b = new UriBuilder(uri) { Path = "/api/generate" };
                    postUrl = b.Uri.ToString();
                }
            }
            catch { /* ignore URI parse errors and fall back to provided endpoint */ }

            // Build payload differently for Ollama-style endpoints
            string json;
            if (useOllama)
            {
                // Attempt to auto-detect an available Ollama model if possible
                try
                {
                    var uri = new Uri(_endpoint);
                    var detected = await DetectOllamaModelAsync(uri).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(detected)) _detectedModel = detected;
                }
                catch { }

                var modelToUse = _detectedModel ?? _model;

                // Combine system prompt and user prompt into a single prompt string
                var combined = string.Empty;
                if (!string.IsNullOrWhiteSpace(_systemPrompt)) combined += _systemPrompt + "\n";
                combined += prompt;
                var oPayload = new JObject();
                if (!string.IsNullOrWhiteSpace(modelToUse)) oPayload["model"] = modelToUse;
                oPayload["prompt"] = combined;
                oPayload["temperature"] = 0.7;
                json = Newtonsoft.Json.JsonConvert.SerializeObject(oPayload);
            }
            else
            {
                // Use JsonConvert.SerializeObject to avoid depending on JToken.ToString overloads
                json = Newtonsoft.Json.JsonConvert.SerializeObject(jPayload);
            }

            HttpResponseMessage resp;
            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    resp = await _http.PostAsync(postUrl, content).ConfigureAwait(false);
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
                AddinStatusLogger.Error("LocalHttpLlmClient", $"HTTP {(int)resp.StatusCode} from {postUrl}", new Exception(respText));
                // Provide clearer guidance for common local-server errors
                if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest && respText != null && respText.IndexOf("No models loaded", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException($"Local LLM returned 400: No models loaded. Please load a model in the local LLM instance or change the configured model. Response: {respText}");
                }
                // If Ollama returned 405, try swapping to the /api/generate path if we didn't already
                if (!useOllama && resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed && _endpoint != null && _endpoint.Contains(":11434"))
                {
                    try
                    {
                        var uri2 = new Uri(_endpoint);
                        var b2 = new UriBuilder(uri2) { Path = "/api/generate" };
                        var altUrl = b2.Uri.ToString();
                        AddinStatusLogger.Log("LocalHttpLlmClient", $"Retrying with Ollama-style endpoint: {altUrl}");
                        using (var content2 = new StringContent(json, Encoding.UTF8, "application/json"))
                        {
                            resp = await _http.PostAsync(altUrl, content2).ConfigureAwait(false);
                        }
                        respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException($"LLM HTTP error {(int)resp.StatusCode}: {respText}");
                        }
                    }
                    catch (Exception retryEx)
                    {
                        AddinStatusLogger.Error("LocalHttpLlmClient", "Retry to Ollama-style endpoint failed", retryEx);
                        throw new InvalidOperationException($"LLM HTTP error {(int)resp.StatusCode}: {respText}");
                    }
                }
                throw new InvalidOperationException($"LLM HTTP error {(int)resp.StatusCode}: {respText}");
            }

            // Try to parse common OpenAI-style chat response shapes.
            try
            {
                var j = JObject.Parse(respText);
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
