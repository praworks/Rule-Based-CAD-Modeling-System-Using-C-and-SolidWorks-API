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
        private readonly string _systemPrompt;

        public LocalHttpLlmClient(string endpoint = "http://127.0.0.1:1234/v1/chat/completions",
                                  string model = "google/functiongemma-270m",
                                  string systemPrompt = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _systemPrompt = systemPrompt;
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public string Model => _model;

        public async Task<string> GenerateAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;

            var messages = new System.Collections.Generic.List<object>();
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
                messages.Add(new { role = "system", content = _systemPrompt });
            messages.Add(new { role = "user", content = prompt });

            var payload = new
            {
                model = _model,
                messages = messages,
                temperature = 0.7,
                max_tokens = -1,
                stream = false
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            HttpResponseMessage resp;
            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    resp = await _http.PostAsync(_endpoint, content).ConfigureAwait(false);
                }
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
