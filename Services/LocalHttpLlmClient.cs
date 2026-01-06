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
        // Shared HttpClient to avoid disposed/connection issues when multiple callers create/dispose instances.
        private static readonly HttpClient _sharedHttp = CreateSharedHttpClient();
        // Track endpoints marked unreachable so callers can fail fast for a cooldown period
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _endpointDeadUntil = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
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
            // Shared HttpClient already configured with a sensible timeout.
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(180);
            return c;
        }

        public string Model => _model;

        public async Task<string> GenerateAsync(string prompt)
        {
            // If this endpoint was recently marked dead, fail fast to avoid repeated socket attempts
            try
            {
                if (!string.IsNullOrWhiteSpace(_endpoint) && _endpointDeadUntil.TryGetValue(_endpoint, out var until) && DateTime.UtcNow < until)
                {
                    AddinStatusLogger.Log("LocalHttpLlmClient", $"Skipping request to {_endpoint} - previously marked unreachable until {until:u}");
                    return null;
                }
            }
            catch { }

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
                    resp = await _sharedHttp.PostAsync(_endpoint, content).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException tex)
            {
                AddinStatusLogger.Error("LocalHttpLlmClient", $"Request to {_endpoint} timed out", tex);
                try
                {
                    var env = System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.User)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.Process)
                              ?? "300";
                    if (!int.TryParse(env, out var secs)) secs = 300;
                    var until = DateTime.UtcNow.AddSeconds(secs);
                    _endpointDeadUntil[_endpoint] = until;
                    AddinStatusLogger.Log("LocalHttpLlmClient", $"Endpoint {_endpoint} marked unreachable until {until:u} due to timeout");
                }
                catch { }
                return null;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LocalHttpLlmClient", "Request failed", ex);
                // If this looks like a connection-refused / socket error, mark the endpoint dead for cooldown
                try
                {
                    var isSocket = false;
                    Exception cur = ex;
                    while (cur != null)
                    {
                        if (cur is System.Net.Sockets.SocketException) { isSocket = true; break; }
                        if (cur is System.Net.Http.HttpRequestException && cur.InnerException is System.Net.Sockets.SocketException) { isSocket = true; break; }
                        var msg = cur.Message ?? string.Empty;
                        if (msg.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0
                            || msg.IndexOf("no connection could be made", StringComparison.OrdinalIgnoreCase) >= 0)
                        { isSocket = true; break; }
                        cur = cur.InnerException;
                    }
                    if (isSocket)
                    {
                        var env = System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.User)
                                  ?? System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.Process)
                                  ?? "300";
                        if (!int.TryParse(env, out var secs)) secs = 300;
                        var until = DateTime.UtcNow.AddSeconds(secs);
                        _endpointDeadUntil[_endpoint] = until;
                        AddinStatusLogger.Log("LocalHttpLlmClient", $"Endpoint {_endpoint} marked unreachable until {until:u}");
                    }
                }
                catch { }
                return null;
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

        // Backward-compatible wrapper used by older call sites that passed a CancellationToken.
        public async Task<string> SendPromptAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            return await GenerateAsync(prompt).ConfigureAwait(false);
        }

        // Do not dispose the shared HttpClient; keep it for the app lifetime.
        public void Dispose()
        {
            // no-op
        }
    }
}
