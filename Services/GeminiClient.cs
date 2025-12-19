using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace AICAD.Services
{
    public class GeminiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private string _model;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public GeminiClient(string apiKey, string model = null)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

            // Allow overriding model via GEMINI_MODEL env var (user/process/machine),
            // otherwise use provided model parameter, otherwise fall back to a safe default.
            var envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.User)
                           ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Process)
                           ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Machine);
            _model = !string.IsNullOrWhiteSpace(envModel) ? envModel.Trim()
                     : (!string.IsNullOrWhiteSpace(model) ? model.Trim() : "gemini-1.0");

            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(60);
            try { AddinStatusLogger.Log("GeminiClient", $"Ctor model={_model}"); } catch { }
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

            var url = $"{BaseUrl}/models/{_model}:generateContent";
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

            using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
            {
                HttpResponseMessage resp;
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
                if (!resp.IsSuccessStatusCode)
                {
                    try { AddinStatusLogger.Error("GeminiClient", $"HTTP {(int)resp.StatusCode} response", new Exception(respText)); } catch { }
                    var status = (int)resp.StatusCode;
                    // Give a short, actionable hint without exposing the key or sensitive data.
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

                var respSerializer = new DataContractJsonSerializer(typeof(GenerateResponse));
                using (var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(respText)))
                {
                    var parsed = (GenerateResponse)respSerializer.ReadObject(ms);
                    var text = parsed?.GetFirstText();
                    try { AddinStatusLogger.Log("GeminiClient", $"GenerateAsync success textLen={text?.Length ?? 0}"); } catch { }
                    return text ?? string.Empty;
                }
            }
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
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
