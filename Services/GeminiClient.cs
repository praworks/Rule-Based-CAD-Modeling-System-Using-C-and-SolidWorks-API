using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AICAD.Services.Logging;

namespace AICAD.Services
{
    public class GeminiClient : IDisposable, ILlmClient
    {
        private static readonly HttpClient _sharedHttp = CreateSharedHttpClient();
        private readonly string _apiKey;
        private string _model;
        private readonly string _systemPrompt;
        private static readonly string[] DefaultFallbackModels = new[] { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash", "gemini-flash-latest", "gemini-1.5-flash" };
        private const string BaseUrlV1 = "https://generativelanguage.googleapis.com/v1";
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        // Cache version and invalidation hook so external code can signal clients to refresh cached instances.
        private static int _cacheVersion = 0;
        public static int CacheVersion => _cacheVersion;
        public static void InvalidateCachedClients()
        {
            try
            {
                System.Threading.Interlocked.Increment(ref _cacheVersion);
                try { AddinStatusLogger.Log("GeminiClient", "InvalidateCachedClients called"); } catch { }
            }
            catch { }
        }

        // Added optional systemPrompt. If omitted, use PromptCatalog.json (single source of truth).
        public GeminiClient(string apiKey, string model = null, string systemPrompt = null)
        {
            var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                _apiKey = envKey.Trim();
            }
            else
            {
                _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            }

            var envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.User)
                           ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Process)
                           ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Machine);
            _model = !string.IsNullOrWhiteSpace(envModel) ? envModel.Trim()
                     : (!string.IsNullOrWhiteSpace(model) ? model.Trim() : "gemini-1.0");

            _systemPrompt = !string.IsNullOrWhiteSpace(systemPrompt)
                ? systemPrompt
                : PromptHandler.DEFAULT_SYSTEM_PROMPT;

            try { AddinStatusLogger.Log("GeminiClient", $"Ctor model={_model} apiKeySource={(string.IsNullOrEmpty(_apiKey) ? "none" : "env/ctor")}" ); } catch { }
        }

        public void SetModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
        }

        public string Model => _model;

        public async Task<string> GenerateAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
            var traceId = LlmTraceLogger.GetOrCreateTraceIdFromContext();
            var userPrompt = prompt;
            // If a system prompt is configured, prepend it to the user prompt so Gemini receives it.
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
            {
                prompt = _systemPrompt + "\n\n" + prompt;
            }
            var oauthConfig = GoogleOAuthConfig.Load();
            string bearer = null;
            try { if (oauthConfig != null) bearer = await TokenManager.GetAccessTokenAsync(oauthConfig).ConfigureAwait(false); } catch { bearer = null; }

            if (string.IsNullOrWhiteSpace(bearer) && string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("No Gemini credentials available. Sign in with Google OAuth or set a GEMINI_API_KEY.");
            }

            try
            {
                var available = await ListAvailableModelsAsync(bearer).ConfigureAwait(false);
                if (available != null && available.Count > 0)
                {
                    if (!string.IsNullOrEmpty(_model) && !available.Contains($"models/{_model}"))
                    {
                        foreach (var f in DefaultFallbackModels)
                        {
                            if (available.Contains($"models/{f}"))
                            {
                                try { AddinStatusLogger.Log("GeminiClient", $"Configured model '{_model}' not available; falling back to {f}"); } catch { }
                                _model = f;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            var url = $"{BaseUrl}/models/{_model}:generateContent";
            bool triedModelFallback = false;
            try { AddinStatusLogger.Log("GeminiClient", $"GenerateAsync: URL='{url}', Model='{_model}', HasBearerToken={!string.IsNullOrEmpty(bearer)}, HasApiKey={!string.IsNullOrEmpty(_apiKey)}"); } catch { }

            var req = new GenerateRequest
            {
                contents = new[] { new Content { parts = new[] { new Part { text = prompt } } } }
            };

            var serializer = new DataContractJsonSerializer(typeof(GenerateRequest));
            string jsonBody;
            using (var ms = new System.IO.MemoryStream()) { serializer.WriteObject(ms, req); jsonBody = Encoding.UTF8.GetString(ms.ToArray()); }

            try
            {
                var prettyReq = FormatJsonForLog(jsonBody, 3000);
                AddinStatusLogger.Log("GeminiClient", $"\n=== HTTP Request to {url} ===");
                AddinStatusLogger.Log("GeminiClient", prettyReq);
                AddinStatusLogger.Log("GeminiClient", "=====================================");
            }
            catch { }

            const int maxRetries = 3;
            int attempt = 0;
            while (true)
            {
                attempt++;
                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                {
                    var attemptSw = Stopwatch.StartNew();
                    HttpResponseMessage resp = null;
                    string effectiveUrl = url;
                    if (!string.IsNullOrWhiteSpace(bearer))
                    {
                        var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
                        httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                        httpReq.Content = content;
                        LlmTraceLogger.LogSend(traceId, "gemini", _model, effectiveUrl, "POST", jsonBody, _systemPrompt, userPrompt);
                        resp = await _sharedHttp.SendAsync(httpReq).ConfigureAwait(false);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(_apiKey)) throw new InvalidOperationException("GEMINI_API_KEY not set and OAuth token unavailable.");
                        effectiveUrl = url + "?key=" + Uri.EscapeDataString(_apiKey);
                        LlmTraceLogger.LogSend(traceId, "gemini", _model, effectiveUrl, "POST", jsonBody, _systemPrompt, userPrompt);
                        resp = await _sharedHttp.PostAsync(effectiveUrl, content).ConfigureAwait(false);
                    }

                    var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    attemptSw.Stop();
                    JToken parsedJson = null;
                    try { parsedJson = JToken.Parse(respText); } catch { }
                    try
                    {
                        var prettyResp = FormatJsonForLog(respText, 6000);
                        AddinStatusLogger.Log("GeminiClient", $"\n=== HTTP Response {(int)resp.StatusCode} from {url} ===");
                        AddinStatusLogger.Log("GeminiClient", prettyResp);
                        AddinStatusLogger.Log("GeminiClient", "=====================================");
                    }
                    catch { }
                    if (resp.IsSuccessStatusCode)
                    {
                        var respSerializer = new DataContractJsonSerializer(typeof(GenerateResponse));
                        using (var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(respText)))
                        {
                            var parsed = (GenerateResponse)respSerializer.ReadObject(ms);
                            var text = parsed?.GetFirstText();
                            try { AddinStatusLogger.Log("GeminiClient", $"GenerateAsync success textLen={text?.Length ?? 0}"); } catch { }
                            LlmTraceLogger.LogRecv(traceId, "gemini", _model, effectiveUrl, (int)resp.StatusCode, respText, text ?? string.Empty, attemptSw.ElapsedMilliseconds, parsedJson);
                            return text ?? string.Empty;
                        }
                    }

                    try { AddinStatusLogger.Error("GeminiClient", $"HTTP {(int)resp.StatusCode} response", new Exception(respText)); } catch { }
                    var status = (int)resp.StatusCode;
                    LlmTraceLogger.LogRecv(traceId, "gemini", _model, effectiveUrl, status, respText, respText, attemptSw.ElapsedMilliseconds, parsedJson);

                    if ((status == 429 || status == 503) && attempt <= maxRetries)
                    {
                        int delaySeconds = 1 << (attempt - 1);
                        if (resp.Headers.RetryAfter != null && resp.Headers.RetryAfter.Delta.HasValue) delaySeconds = (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds;
                        try { AddinStatusLogger.Log("GeminiClient", $"Transient HTTP {status} - retry {attempt}/{maxRetries} after {delaySeconds}s"); } catch { }
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
                        continue;
                    }

                    if (!triedModelFallback && (status == 404 || (!string.IsNullOrEmpty(respText) && respText.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        triedModelFallback = true;
                        try { AddinStatusLogger.Log("GeminiClient", $"Model '{_model}' appears unavailable (HTTP {status}). Attempting automatic fallback."); } catch { }
                        try
                        {
                            var available = await ListAvailableModelsAsync(bearer).ConfigureAwait(false);
                            string pick = null;
                            if (available != null && available.Count > 0)
                            {
                                foreach (var f in DefaultFallbackModels) { if (available.Contains($"models/{f}")) { pick = f; break; } }
                                if (pick == null)
                                {
                                    var first = available[0]; if (first.StartsWith("models/")) pick = first.Substring("models/".Length);
                                }
                            }
                            if (pick == null) pick = DefaultFallbackModels.Length > 0 ? DefaultFallbackModels[0] : _model;
                            if (!string.IsNullOrWhiteSpace(pick) && pick != _model)
                            {
                                try { AddinStatusLogger.Log("GeminiClient", $"Falling back from '{_model}' to '{pick}' and retrying request."); } catch { }
                                _model = pick;
                                url = $"{BaseUrl}/models/{_model}:generateContent";
                                attempt = 0;
                                continue;
                            }
                        }
                        catch (Exception ex) { try { AddinStatusLogger.Error("GeminiClient", "Fallback attempt failed", ex); } catch { } }
                    }

                    string hint = string.Empty;
                    switch (status)
                    {
                        case 403:
                            hint = "Forbidden: check the API key, project/billing status, and key restrictions (API or application restrictions).";
                            break;
                        case 404:
                            hint = "Not found: the requested model may not support the generateContent method for this API version or your key; try a different model from ListModels.";
                            break;
                        default:
                            hint = string.Empty;
                            break;
                    }
                    var suggestion = "Suggestion: verify the Generative Language API is enabled, billing is active for the project, and the GEMINI_MODEL environment variable is set to a supported model. You can call ListModels to see available models.";
                    var message = $"Gemini error {status}: {hint} {suggestion}";
                    throw new InvalidOperationException(message);
                }
            }
        }

        public async Task StreamAsync(string prompt, System.Action<string> onDelta, System.Threading.CancellationToken cancellationToken)
        {
            // Gemini streaming not implemented here; fallback to single-shot
            var text = await GenerateAsync(prompt).ConfigureAwait(false);
            onDelta?.Invoke(text ?? string.Empty);
        }

        public void Dispose() { /* keep shared HttpClient for process lifetime */ }

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

        public async Task<System.Collections.Generic.List<string>> ListAvailableModelsAsync(string bearerToken = null)
        {
            try
            {
                var url = BaseUrlV1 + "/models";
                HttpResponseMessage resp;
                if (!string.IsNullOrEmpty(bearerToken))
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
                    resp = await _sharedHttp.SendAsync(req).ConfigureAwait(false);
                }
                else
                {
                    var key = _apiKey; if (string.IsNullOrWhiteSpace(key)) return null;
                    resp = await _sharedHttp.GetAsync(url + "?key=" + Uri.EscapeDataString(key)).ConfigureAwait(false);
                }
                if (!resp.IsSuccessStatusCode) return null;
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var list = new System.Collections.Generic.List<string>();
                var idx = 0;
                while (true)
                {
                    var nm = "\"name\": \"models/";
                    var pos = body.IndexOf(nm, idx, StringComparison.OrdinalIgnoreCase);
                    if (pos < 0) break;
                    pos += nm.Length;
                    var end = body.IndexOf('"', pos);
                    if (end < 0) break;
                    var modelName = body.Substring(pos, end - pos);
                    list.Add("models/" + modelName);
                    idx = end + 1;
                }
                return list;
            }
            catch { return null; }
        }

        public async Task<ApiKeyTestResult> TestApiKeyAsync(string bearerToken = null)
        {
            try
            {
                var url = BaseUrlV1 + "/models";
                HttpResponseMessage resp;
                var usedBearer = !string.IsNullOrEmpty(bearerToken);
                if (usedBearer)
                {
                    var req = new System.Net.Http.HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
                    resp = await _sharedHttp.SendAsync(req).ConfigureAwait(false);
                }
                else
                {
                    var key = _apiKey; if (string.IsNullOrWhiteSpace(key)) return new ApiKeyTestResult { Success = false, StatusCode = null, Hint = "Set GEMINI_API_KEY or sign in with Google OAuth to obtain a bearer token.", UsedBearer = false };
                    resp = await _sharedHttp.GetAsync(url + "?key=" + Uri.EscapeDataString(key)).ConfigureAwait(false);
                }

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = new ApiKeyTestResult { StatusCode = (int)resp.StatusCode, UsedBearer = usedBearer };
                {
                    var models = new System.Collections.Generic.List<string>();
                    var idx = 0;
                    while (true)
                    {
                        var nm = "\"name\": \"models/";
                        var pos = body.IndexOf(nm, idx, StringComparison.OrdinalIgnoreCase);
                        if (pos < 0) break;
                        pos += nm.Length;
                        var end = body.IndexOf('"', pos);
                        if (end < 0) break;
                        var modelName = body.Substring(pos, end - pos);
                        models.Add("models/" + modelName);
                        idx = end + 1;
                    }
                    result.Success = true;
                    result.Message = "OK";
                    result.ModelsFound = models.Count;
                    result.ModelNames = models;
                    return result;
                }
            }
            catch (Exception ex)
            {
                return new ApiKeyTestResult
                {
                    Success = false,
                    StatusCode = null,
                    Message = ex.Message,
                    Hint = "Exception while attempting to contact the Generative Language API.",
                    UsedBearer = !string.IsNullOrEmpty(bearerToken)
                };
            }
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(60);
            return c;
        }
    }

    public class ApiKeyTestResult
    {
        public bool Success { get; set; }
        public int? StatusCode { get; set; }
        public string Message { get; set; }
        public string Hint { get; set; }
        public int ModelsFound { get; set; }
        public System.Collections.Generic.List<string> ModelNames { get; set; }
        public bool UsedBearer { get; set; }
    }

    [DataContract]
    internal class GenerateRequest
    {
        [DataMember]
        public Content[] contents { get; set; }
    }

    [DataContract]
    internal class Content
    {
        [DataMember]
        public Part[] parts { get; set; }

        public string GetFirstText()
        {
            if (parts == null) return null;
            foreach (var p in parts) { if (!string.IsNullOrEmpty(p?.text)) return p.text; }
            return null;
        }
    }

    [DataContract]
    internal class Part
    {
        [DataMember(EmitDefaultValue = false)]
        public string text { get; set; }
    }

    [DataContract]
    internal class GenerateResponse
    {
        [DataMember(EmitDefaultValue = false)]
        public Candidate[] candidates { get; set; }

        public string GetFirstText()
        {
            if (candidates == null) return null;
            foreach (var c in candidates) { var t = c?.content?.GetFirstText(); if (!string.IsNullOrEmpty(t)) return t; }
            return null;
        }
    }

    [DataContract]
    internal class Candidate
    {
        [DataMember(EmitDefaultValue = false)]
        public Content content { get; set; }
    }
}
