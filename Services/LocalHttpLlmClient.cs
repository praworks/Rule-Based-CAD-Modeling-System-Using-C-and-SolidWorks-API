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
        // Simple cache-version to help callers know when an invalidate occurred
        private static int _cacheVersion = 0;
        public static int CacheVersion => _cacheVersion;

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
            // Prefer explicit systemPrompt, then AICAD_SYSTEM_PROMPT env var
            var envPrompt = System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                            ?? System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process);
            _systemPrompt = systemPrompt ?? envPrompt;
            // Shared HttpClient already configured with a sensible timeout.
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(180);
            return c;
        }

        private static string FormatJsonForLog(string json, int maxLength)
        {
            if (string.IsNullOrEmpty(json)) return "(empty)";
            try
            {
                var obj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                var formatted = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
                if (formatted.Length > maxLength)
                {
                    return formatted.Substring(0, maxLength) + "\n... (truncated)";
                }
                return formatted;
            }
            catch
            {
                // If JSON parsing fails, return compressed version
                var compressed = json.Replace("\r\n", " ").Replace("\n", " ");
                if (compressed.Length > maxLength)
                {
                    return compressed.Substring(0, maxLength) + "... (truncated)";
                }
                return compressed;
            }
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

            try
            {
                var prettyReq = FormatJsonForLog(json, 2000);
                AddinStatusLogger.Log("LocalHttpLlmClient", $"\n=== HTTP Request to {_endpoint} ===");
                AddinStatusLogger.Log("LocalHttpLlmClient", prettyReq);
                AddinStatusLogger.Log("LocalHttpLlmClient", "=====================================");
            }
            catch { }

            HttpResponseMessage resp = null;
            // Retries for transient timeouts; don't mark endpoint dead until attempts exhausted
            const int maxAttempts = 3;
            var attempt = 0;
            var baseDelayMs = 1500;
            while (attempt < maxAttempts)
            {
                attempt++;
                try
                {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        resp = await _sharedHttp.PostAsync(_endpoint, content).ConfigureAwait(false);
                    }
                    break; // success
                }
                catch (TaskCanceledException tex)
                {
                    AddinStatusLogger.Error("LocalHttpLlmClient", $"Request to {_endpoint} timed out (attempt {attempt}/{maxAttempts})", tex);
                    if (attempt >= maxAttempts)
                    {
                        try
                        {
                            var env = System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.User)
                                      ?? System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.Process)
                                      ?? "300";
                            if (!int.TryParse(env, out var secs)) secs = 300;
                            var until = DateTime.UtcNow.AddSeconds(secs);
                            _endpointDeadUntil[_endpoint] = until;
                            AddinStatusLogger.Log("LocalHttpLlmClient", $"Endpoint {_endpoint} marked unreachable until {until:u} due to repeated timeouts");
                        }
                        catch { }
                        return null;
                    }
                    // backoff then retry
                    try { await Task.Delay(baseDelayMs * attempt).ConfigureAwait(false); } catch { }
                    continue;
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
            }

            var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                var prettyResp = FormatJsonForLog(respText, 5000);
                AddinStatusLogger.Log("LocalHttpLlmClient", $"\n=== HTTP Response {(int)resp.StatusCode} from {_endpoint} ===");
                AddinStatusLogger.Log("LocalHttpLlmClient", prettyResp);
                AddinStatusLogger.Log("LocalHttpLlmClient", "=====================================");
            }
            catch { }
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

        public async Task StreamAsync(string prompt, Action<string> onDelta, System.Threading.CancellationToken cancellationToken)
        {
            // Fail fast if endpoint marked dead
            try
            {
                if (!string.IsNullOrWhiteSpace(_endpoint) && _endpointDeadUntil.TryGetValue(_endpoint, out var until) && DateTime.UtcNow < until)
                {
                    AddinStatusLogger.Log("LocalHttpLlmClient", $"Skipping streaming request to {_endpoint} - marked unreachable until {until:u}");
                    var full = await GenerateAsync(prompt).ConfigureAwait(false);
                    onDelta?.Invoke(full ?? string.Empty);
                    return;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(prompt)) { onDelta?.Invoke(string.Empty); return; }

            var messages = new System.Collections.Generic.List<object>();
            if (!string.IsNullOrWhiteSpace(_systemPrompt)) messages.Add(new { role = "system", content = _systemPrompt });
            messages.Add(new { role = "user", content = prompt });

            var jPayload = new JObject();
            if (!string.IsNullOrWhiteSpace(_model)) jPayload["model"] = _model;
            jPayload["messages"] = JArray.FromObject(messages);
            jPayload["temperature"] = 0.7;
            jPayload["stream"] = true;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(jPayload);

            try
            {
                AddinStatusLogger.Log("LocalHttpLlmClient", $"\n=== HTTP Streaming Request to {_endpoint} ===\n" + FormatJsonForLog(json, 1500));
            }
            catch { }

            using (var req = new HttpRequestMessage(HttpMethod.Post, _endpoint))
            {
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                req.Headers.Accept.Clear();
                req.Headers.Accept.ParseAdd("text/event-stream");
                HttpResponseMessage resp = null;
                try { resp = await _sharedHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false); }
                catch (TaskCanceledException tex)
                {
                    AddinStatusLogger.Error("LocalHttpLlmClient", "Streaming request timed out/canceled", tex);
                    onDelta?.Invoke(string.Empty); return;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AddinStatusLogger.Error("LocalHttpLlmClient", $"Streaming HTTP {(int)resp.StatusCode}", new Exception(txt));
                    // Fallback to non-streaming
                    var full = await GenerateAsync(prompt).ConfigureAwait(false);
                    onDelta?.Invoke(full ?? string.Empty);
                    return;
                }

                // Try to read as an SSE-style stream (lines prefixed with 'data: ')
                try
                {
                    using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                        {
                            line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;
                            if (line.Length == 0) continue; // skip keepalives
                            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                            {
                                var payload = line.Substring(5).Trim();
                                if (payload == "[DONE]") break;
                                try
                                {
                                    var j = JObject.Parse(payload);
                                    // Capture model name if provided
                                    try { if (j["model"] != null) _model = j["model"].ToString(); } catch { }

                                    string delta = null;
                                    var choices = j["choices"] as JArray;
                                    if (choices != null && choices.Count > 0)
                                    {
                                        var first = choices[0] as JObject;
                                        delta = first?["delta"]? ["content"]?.ToString()
                                                ?? first?["message"]? ["content"]?.ToString()
                                                ?? first?["text"]?.ToString();
                                    }
                                    else if (j["content"] != null)
                                    {
                                        delta = j["content"].ToString();
                                    }
                                    if (!string.IsNullOrEmpty(delta)) onDelta?.Invoke(delta);
                                }
                                catch
                                {
                                    // Some servers stream plain text lines instead of JSON chunks
                                    if (!string.IsNullOrEmpty(payload)) onDelta?.Invoke(payload);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddinStatusLogger.Error("LocalHttpLlmClient", "Streaming parse failed; falling back", ex);
                    var full = await GenerateAsync(prompt).ConfigureAwait(false);
                    onDelta?.Invoke(full ?? string.Empty);
                }
            }
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

        // Allow external code to invalidate caches (e.g. when env vars or settings change).
        public static void InvalidateCachedClients()
        {
            try
            {
                _endpointDeadUntil.Clear();
                System.Threading.Interlocked.Increment(ref _cacheVersion);
                try { AddinStatusLogger.Log("LocalHttpLlmClient", "InvalidateCachedClients called: cleared endpoint-dead cache"); } catch { }
            }
            catch { }
        }
    }
}
