using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Runtime.Serialization.Json;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    public static class TokenManager
    {
        private const string CredentialTarget = "SolidWorksTextToCAD_OAuthToken";

        // Load token JSON from Credential Manager and return access_token; refresh if needed.
        public static async Task<string> GetAccessTokenAsync(GoogleOAuthConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.ClientId)) return null;
            var tokenJson = CredentialManager.ReadGenericSecret(CredentialTarget);
            if (string.IsNullOrWhiteSpace(tokenJson)) return null;
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
                if (string.IsNullOrWhiteSpace(refresh)) return access; // cannot refresh
                var newToken = await RefreshAsync(config, refresh).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(newToken))
                {
                    CredentialManager.WriteGenericSecret(CredentialTarget, newToken);
                    var j2 = JObject.Parse(newToken);
                    return j2.Value<string>("access_token");
                }
                return access;
            }
            catch
            {
                return null;
            }
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
