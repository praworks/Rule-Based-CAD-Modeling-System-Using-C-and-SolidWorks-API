using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace AICAD.Services
{
    public class GeminiClient : IDisposable, ILlmClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private string _model;
        private static readonly string[] DefaultFallbackModels = new[] { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash", "gemini-flash-latest", "gemini-1.5-flash" };
        private const string BaseUrlV1 = "https://generativelanguage.googleapis.com/v1";
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public GeminiClient(string apiKey, string model = null)
        {
            // Prefer an environment-provided key so we don't accidentally use a hardcoded value passed in.
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

            // Allow overriding model via GEMINI_MODEL env var (user/process/machine),
            // otherwise use provided model parameter, otherwise fall back to a safe default.
            var envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.User)
                           ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Process)
                           ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Machine);
            _model = !string.IsNullOrWhiteSpace(envModel) ? envModel.Trim()
                     : (!string.IsNullOrWhiteSpace(model) ? model.Trim() : "gemini-1.0");

            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(60);
            try
            {
                var src = !string.IsNullOrWhiteSpace(envKey) ? "env" : (!string.IsNullOrWhiteSpace(apiKey) ? "ctor" : "none");
                AddinStatusLogger.Log("GeminiClient", $"Ctor model={_model} apiKeySource={src}");
            }
            catch { }
        }

        public void SetModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
        }

    public string Model => _model;

        public async Task<string> GenerateAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
            // Prefer OAuth bearer token (if signed in) to avoid embedding API keys. Fallback to API key query param.
            var oauthConfig = GoogleOAuthConfig.Load();
            string bearer = null;
            try
            {
                if (oauthConfig != null) bearer = await TokenManager.GetAccessTokenAsync(oauthConfig).ConfigureAwait(false);
            }
            catch { bearer = null; }

            if (string.IsNullOrWhiteSpace(bearer) && string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("No Gemini credentials available. Sign in with Google OAuth or set a GEMINI_API_KEY.");
            }
            // Ensure the model exists for our credentials; consult ListModels and pick a fallback if needed.
            try
            {
                var available = await ListAvailableModelsAsync(bearer).ConfigureAwait(false);
                if (available != null && available.Count > 0)
                {
                    if (!string.IsNullOrEmpty(_model) && !available.Contains($"models/{_model}"))
                    {
                        // Try to pick a fallback from defaults
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
                contents = new[]
                {
                    new Content
                    {
                        parts = new[] { new Part { text = prompt } }
                    }
                }
            };

            var serializer = new DataContractJsonSerializer(typeof(GenerateRequest));
            string jsonBody;
            using (var ms = new System.IO.MemoryStream())
            {
                serializer.WriteObject(ms, req);
                jsonBody = Encoding.UTF8.GetString(ms.ToArray());
            }

            const int maxRetries = 3;
            int attempt = 0;
            while (true)
            {
                attempt++;
                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage resp = null;
                    if (!string.IsNullOrWhiteSpace(bearer))
                    {
                        var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
                        httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                        httpReq.Content = content;
                        resp = await _http.SendAsync(httpReq).ConfigureAwait(false);
                    }
                    else
                    {
                        // fallback to API key in query string
                        if (string.IsNullOrWhiteSpace(_apiKey))
                        {
                            throw new InvalidOperationException("GEMINI_API_KEY not set and OAuth token unavailable.");
                        }
                        var urlWithKey = url + "?key=" + Uri.EscapeDataString(_apiKey);
                        resp = await _http.PostAsync(urlWithKey, content).ConfigureAwait(false);
                    }

                    var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        var respSerializer = new DataContractJsonSerializer(typeof(GenerateResponse));
                        using (var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(respText)))
                        {
                            var parsed = (GenerateResponse)respSerializer.ReadObject(ms);
                            var text = parsed?.GetFirstText();
                            try { AddinStatusLogger.Log("GeminiClient", $"GenerateAsync success textLen={text?.Length ?? 0}"); } catch { }
                            return text ?? string.Empty;
                        }
                    }

                    try { AddinStatusLogger.Error("GeminiClient", $"HTTP {(int)resp.StatusCode} response", new Exception(respText)); } catch { }
                    var status = (int)resp.StatusCode;

                    // If transient (rate limit or server error), optionally retry with backoff
                    if ((status == 429 || status == 503) && attempt <= maxRetries)
                    {
                        int delaySeconds = 1 << (attempt - 1); // 1,2,4
                        // honor Retry-After header if present
                        if (resp.Headers.RetryAfter != null)
                        {
                            if (resp.Headers.RetryAfter.Delta.HasValue) delaySeconds = (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds;
                        }
                        try { AddinStatusLogger.Log("GeminiClient", $"Transient HTTP {status} - retry {attempt}/{maxRetries} after {delaySeconds}s"); } catch { }
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
                        continue;
                    }

                    // If model not found (or API reports model issues), try a single automatic fallback before failing hard
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
                                foreach (var f in DefaultFallbackModels)
                                {
                                    if (available.Contains($"models/{f}"))
                                    {
                                        pick = f;
                                        break;
                                    }
                                }
                                if (pick == null)
                                {
                                    // take first available model name (strip "models/")
                                    var first = available[0];
                                    if (first.StartsWith("models/")) pick = first.Substring("models/".Length);
                                }
                            }
                            if (pick == null)
                            {
                                // Last-resort: pick from our built-in fallback list
                                pick = DefaultFallbackModels.Length > 0 ? DefaultFallbackModels[0] : _model;
                            }
                            if (!string.IsNullOrWhiteSpace(pick) && pick != _model)
                            {
                                try { AddinStatusLogger.Log("GeminiClient", $"Falling back from '{_model}' to '{pick}' and retrying request."); } catch { }
                                _model = pick;
                                url = $"{BaseUrl}/models/{_model}:generateContent";
                                // reset attempt counter so we still honor retries for transient errors
                                attempt = 0;
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            try { AddinStatusLogger.Error("GeminiClient", "Fallback attempt failed", ex); } catch { }
                        }
                    }

                    // Non-retriable or exhausted retries — provide actionable hint
                    string hint = string.Empty;
                    switch (status)
                    {
                        case 403:
                            hint = "Forbidden: check the API key, project/billing status, and key restrictions (API or application restrictions).";
                            break;
                        case 404:
                            hint = "Not found: the requested model may not support the generateContent method for this API version or your key; try a different model from ListModels.";
                            break;
                        case 429:
                            hint = "Rate limited: your quota may be exhausted or you're sending too many requests; consider checking billing/limits or reducing request rate.";
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

        public void Dispose()
        {
            _http?.Dispose();
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
                    resp = await _http.SendAsync(req).ConfigureAwait(false);
                }
                else
                {
                    var key = _apiKey;
                    if (string.IsNullOrWhiteSpace(key)) return null;
                    resp = await _http.GetAsync(url + "?key=" + Uri.EscapeDataString(key)).ConfigureAwait(false);
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

        /// <summary>
        /// Tests whether the configured API credentials (API key or provided bearer token)
        /// can access the Generative Language Models list and returns detailed diagnostics.
        /// </summary>
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
                    resp = await _http.SendAsync(req).ConfigureAwait(false);
                }
                else
                {
                    var key = _apiKey;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        return new ApiKeyTestResult
                        {
                            Success = false,
                            StatusCode = null,
                            Message = "No API key configured",
                            Hint = "Set GEMINI_API_KEY or sign in with Google OAuth to obtain a bearer token.",
                            UsedBearer = false
                        };
                    }
                    resp = await _http.GetAsync(url + "?key=" + Uri.EscapeDataString(key)).ConfigureAwait(false);
                }

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = new ApiKeyTestResult { StatusCode = (int)resp.StatusCode, UsedBearer = usedBearer };
                if (resp.IsSuccessStatusCode)
                {
                    // parse model names similarly to ListAvailableModelsAsync
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

                // unsuccessful: provide actionable hint
                string hint = string.Empty;
                switch ((int)resp.StatusCode)
                {
                    case 403:
                        hint = "Forbidden: check the API key, project/billing status, and key restrictions (API or application restrictions).";
                        break;
                    case 404:
                        hint = "Not found: the API endpoint or model may be unavailable to this key or project.";
                        break;
                    case 429:
                        hint = "Rate limited: quota may be exhausted or requests are too frequent.";
                        break;
                    default:
                        hint = string.Empty;
                        break;
                }

                result.Success = false;
                result.Message = string.IsNullOrWhiteSpace(body) ? resp.ReasonPhrase : body;
                result.Hint = hint;
                return result;
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
    }

    /// <summary>
    /// Result object returned by <see cref="GeminiClient.TestApiKeyAsync"/> with richer diagnostics.
    /// </summary>
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
            foreach (var p in parts)
            {
                if (!string.IsNullOrEmpty(p?.text)) return p.text;
            }
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
            foreach (var c in candidates)
            {
                var t = c?.content?.GetFirstText();
                if (!string.IsNullOrEmpty(t)) return t;
            }
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
