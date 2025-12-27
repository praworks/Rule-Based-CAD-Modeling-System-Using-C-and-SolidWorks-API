using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Runtime.Serialization.Json;
using System.IO;
using Newtonsoft.Json.Linq;
using Google.Apis.Auth.OAuth2;
using System.Threading;

namespace AICAD.Services
{
    public static class TokenManager
    {
        private const string CredentialTarget = "SolidWorksTextToCAD_OAuthToken";

        // Load token JSON from Credential Manager and return access_token; refresh if needed.
        // If no stored token exists or refresh fails, try service-account flow using
        // GOOGLE_APPLICATION_CREDENTIALS to mint a token.
        public static async Task<string> GetAccessTokenAsync(GoogleOAuthConfig config)
        {
            // If OAuth client config isn't available, we can still attempt service-account flow below.
            var tokenJson = CredentialManager.ReadGenericSecret(CredentialTarget);
            if (!string.IsNullOrWhiteSpace(tokenJson))
            {
                try
                {
                    var j = JObject.Parse(tokenJson);
                    var access = j.Value<string>("access_token");
                    var expiresIn = j.Value<long?>("expires_in");
                    var obtained = j.Value<long?>("obtained_at");
                    var refresh = j.Value<string>("refresh_token");
                    if (!string.IsNullOrWhiteSpace(access) && obtained.HasValue && expiresIn.HasValue)
                    {
                        var expiry = DateTimeOffset.FromUnixTimeSeconds(obtained.Value).AddSeconds(expiresIn.Value - 60);
                        if (DateTimeOffset.UtcNow < expiry)
                        {
                            return access;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(refresh) && config != null && !string.IsNullOrWhiteSpace(config.ClientId))
                    {
                        var newToken = await RefreshAsync(config, refresh).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(newToken))
                        {
                            CredentialManager.WriteGenericSecret(CredentialTarget, newToken);
                            var j2 = JObject.Parse(newToken);
                            return j2.Value<string>("access_token");
                        }
                    }

                    // fall through to service-account attempt
                }
                catch
                {
                    // ignore and try service-account
                }
            }

            // Try service-account flow
            var sa = await TryGetServiceAccountAccessTokenAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sa)) return sa;

            // nothing available
            return null;
        }

        public static void SaveTokenJson(string tokenJson)
        {
            if (string.IsNullOrWhiteSpace(tokenJson)) return;
            try
            {
                var j = JObject.Parse(tokenJson);
                // add obtained timestamp if missing
                if (j.Value<long?>("obtained_at") == null)
                {
                    j["obtained_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                    CredentialManager.WriteGenericSecret(CredentialTarget, AICAD.Services.JsonUtils.SerializeCompact(j));
            }
            catch { }
        }

        private static async Task<string> TryGetServiceAccountAccessTokenAsync()
        {
            try
            {
                // Check env var (process/user/machine)
                var path = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", EnvironmentVariableTarget.Process)
                           ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", EnvironmentVariableTarget.User)
                           ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

                GoogleCredential credential;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    credential = GoogleCredential.FromStream(stream);
                }
                var scoped = credential.CreateScoped(new[] { "https://www.googleapis.com/auth/cloud-platform" });
                var token = await scoped.UnderlyingCredential.GetAccessTokenForRequestAsync(null, CancellationToken.None).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token)) return null;

                // Cache token in Credential Manager
                try
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var json = new JObject
                    {
                        ["access_token"] = token,
                        ["expires_in"] = 3600,
                        ["obtained_at"] = now
                    };
                    CredentialManager.WriteGenericSecret(CredentialTarget, AICAD.Services.JsonUtils.SerializeCompact(json));
                }
                catch { }

                return token;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> RefreshAsync(GoogleOAuthConfig config, string refreshToken)
        {
            try
            {
                using (var http = new HttpClient())
                {
                    var dict = new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = refreshToken
                    };
                    if (config.HasClientSecret)
                    {
                        dict["client_secret"] = config.ClientSecret;
                    }
                    var content = new FormUrlEncodedContent(dict);
                    var resp = await http.PostAsync("https://oauth2.googleapis.com/token", content).ConfigureAwait(false);
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return null;
                    var j = JObject.Parse(text);
                    j["obtained_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    return AICAD.Services.JsonUtils.SerializeCompact(j);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
