using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Minimal Groq API client with polite rate-limit handling.
    /// - Reads API key from constructor or `GROQ_API_KEY` user env var.
    /// - Observes `retry-after` and `x-ratelimit-*` headers on 429 responses.
    /// - Exposes a simple SendAsync that returns parsed JSON and rate-limit info.
    /// Note: This client attempts to avoid crossing limits by refusing to send
    /// if remaining requests/tokens are below configured safety thresholds.
    /// </summary>
    public class GroqClient : IDisposable
    {
        // Shared HttpClient for GroqClient instances (re-creatable if disposed)
        private static HttpClient _sharedHttp = CreateSharedHttpClient();
        private static readonly object _sharedHttpLock = new object();
        private HttpClient GetSharedHttp()
        {
            lock (_sharedHttpLock)
            {
                if (_sharedHttp == null)
                {
                    _sharedHttp = CreateSharedHttpClient();
                }
                return _sharedHttp;
            }
        }
        private readonly string _apiKey;

        // Safety thresholds (tunable)
        public int MinRemainingRequestsThreshold { get; set; }
        public int MinRemainingTokensThreshold { get; set; }

        public GroqClient(string apiKey = null)
        {
            _apiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey :
                     (Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.Process)
                      ?? Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User)
                      ?? "");

            // Do not mutate shared client's default headers per-instance. We'll set Authorization per-request.

            // conservative defaults
            MinRemainingRequestsThreshold = 2;
            MinRemainingTokensThreshold = 128;
        }

        // Do not dispose shared HttpClient
        public void Dispose()
        {
            // no-op
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(30);
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

        public class RateLimitInfo
        {
            public int? RemainingRequests;
            public int? RemainingTokens;
            public string RetryAfterRaw;
            public TimeSpan? RetryAfter;
        }

        public class GroqResponse
        {
            public bool Success;
            public int StatusCode;
            public string Body;
            public JObject Json;
            public RateLimitInfo RateInfo;
            public string ErrorMessage;
        }

        /// <summary>
        /// Send a POST to the Groq-style endpoint and respect rate-limit headers.
        /// endpoint should be a full URL like "https://api.groq.com/v1/models/<id>/chat" or similar.
        /// payload is any object serializable to JSON.
        /// </summary>
        public async Task<GroqResponse> SendAsync(string endpoint, object payload, CancellationToken ct)
        {
            var res = new GroqResponse();

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                res.Success = false;
                res.ErrorMessage = "Endpoint is empty";
                return res;
            }

            // Build request
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload ?? new { });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var prettyReq = FormatJsonForLog(json, 3000);
                AddinStatusLogger.Log("GroqClient", $"\n=== HTTP Request to {endpoint} ===");
                AddinStatusLogger.Log("GroqClient", prettyReq);
                AddinStatusLogger.Log("GroqClient", "=====================================");
            }
            catch { }

            // Try send with polite retries on 429; do not retry indefinitely
            int attempt = 0;
            int maxAttempts = 4;
            var rnd = new Random();

            while (true)
            {
                attempt++;
                HttpResponseMessage httpResp = null;
                try
                {
                    // Create HttpRequestMessage so we can set per-request Authorization header safely
                    using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        req.Content = content;
                        if (!string.IsNullOrWhiteSpace(_apiKey))
                        {
                            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                        }
                        try
                        {
                            httpResp = await GetSharedHttp().SendAsync(req, ct).ConfigureAwait(false);
                        }
                        catch (ObjectDisposedException) when (attempt == 1)
                        {
                            // recreate shared client once and retry
                            lock (_sharedHttpLock) { _sharedHttp = CreateSharedHttpClient(); }
                            httpResp = await GetSharedHttp().SendAsync(req, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    res.Success = false;
                    res.ErrorMessage = "Request failed: " + ex.Message;
                    return res;
                }

                res.StatusCode = (int)httpResp.StatusCode;
                res.RateInfo = ParseRateLimitHeaders(httpResp);

                res.Body = null;
                try { res.Body = await httpResp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                try
                {
                    var prettyResp = FormatJsonForLog(res.Body, 8000);
                    AddinStatusLogger.Log("GroqClient", $"\n=== HTTP Response {(int)httpResp.StatusCode} from {endpoint} ===");
                    AddinStatusLogger.Log("GroqClient", prettyResp);
                    AddinStatusLogger.Log("GroqClient", "=====================================");
                }
                catch { }

                if ((int)httpResp.StatusCode == 429)
                {
                    // If server provided Retry-After, use it. Otherwise exponential backoff.
                    TimeSpan wait = TimeSpan.FromSeconds(1);
                    if (res.RateInfo != null && res.RateInfo.RetryAfter.HasValue)
                    {
                        wait = res.RateInfo.RetryAfter.Value;
                    }
                    else
                    {
                        // exponential backoff with jitter
                        wait = TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(rnd.Next(0, 500));
                    }

                    if (attempt >= maxAttempts)
                    {
                        res.Success = false;
                        res.ErrorMessage = "Rate limited (429) and max retries reached";
                        return res;
                    }

                    try { await Task.Delay(wait, ct).ConfigureAwait(false); } catch (TaskCanceledException) { res.Success = false; res.ErrorMessage = "Cancelled"; return res; }
                    continue; // retry
                }

                // Non-429: parse body as JSON if present
                if (!string.IsNullOrWhiteSpace(res.Body))
                {
                    try { res.Json = JObject.Parse(res.Body); } catch { res.Json = null; }
                }

                // Safety check: refuse to proceed if remaining requests/tokens low (pre-send check not available here)
                if (res.RateInfo != null)
                {
                    if (res.RateInfo.RemainingRequests.HasValue && res.RateInfo.RemainingRequests.Value <= MinRemainingRequestsThreshold)
                    {
                        res.Success = false;
                        res.ErrorMessage = "Remaining requests below safety threshold; aborting to avoid rate limit.";
                        return res;
                    }
                    if (res.RateInfo.RemainingTokens.HasValue && res.RateInfo.RemainingTokens.Value <= MinRemainingTokensThreshold)
                    {
                        res.Success = false;
                        res.ErrorMessage = "Remaining tokens below safety threshold; aborting to avoid token limit.";
                        return res;
                    }
                }

                res.Success = httpResp.IsSuccessStatusCode;
                if (!res.Success && string.IsNullOrWhiteSpace(res.ErrorMessage))
                {
                    res.ErrorMessage = string.Format("Request failed: {0} {1}", (int)httpResp.StatusCode, httpResp.ReasonPhrase);
                }

                return res;
            }
        }

        private RateLimitInfo ParseRateLimitHeaders(HttpResponseMessage resp)
        {
            try
            {
                var info = new RateLimitInfo();
                if (resp.Headers.Contains("x-ratelimit-remaining-requests"))
                {
                    var v = resp.Headers.GetValues("x-ratelimit-remaining-requests");
                    foreach (var s in v) { int tmp; if (int.TryParse(s, out tmp)) { info.RemainingRequests = tmp; break; } }
                }
                if (resp.Headers.Contains("x-ratelimit-remaining-tokens"))
                {
                    var v = resp.Headers.GetValues("x-ratelimit-remaining-tokens");
                    foreach (var s in v) { int tmp; if (int.TryParse(s, out tmp)) { info.RemainingTokens = tmp; break; } }
                }
                if (resp.Headers.Contains("retry-after"))
                {
                    var v = resp.Headers.GetValues("retry-after");
                    foreach (var s in v)
                    {
                        info.RetryAfterRaw = s;
                        int secs;
                        if (int.TryParse(s, out secs)) { info.RetryAfter = TimeSpan.FromSeconds(secs); break; }
                        // try parse HTTP-date not implemented; leave RetryAfter null if failure
                    }
                }
                return info;
            }
            catch { return null; }
        }
    }
}
