using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Google.Apis.Auth.OAuth2;
using System.Threading;

namespace AICAD.Services
{
    public static class TokenManager
    {
        private const string CredentialTarget = "SolidWorksTextToCAD_OAuthToken";
        private const string TokenFileName = "google_oauth_token.dat";

        // Load token JSON from Credential Manager and return access_token; refresh if needed.
        // If no stored token exists or refresh fails, try service-account flow using
        // GOOGLE_APPLICATION_CREDENTIALS to mint a token.
        public static async Task<string> GetAccessTokenAsync(GoogleOAuthConfig config)
        {
            // If OAuth client config isn't available, we can still attempt service-account flow below.
            var tokenJson = LoadStoredTokenJson();
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
                            var mergedToken = MergePersistedTokenData(j, newToken);
                            SaveTokenJson(mergedToken);
                            var j2 = JObject.Parse(mergedToken);
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
                var existingJson = CredentialManager.ReadGenericSecret(CredentialTarget);
                if (!string.IsNullOrWhiteSpace(existingJson))
                {
                    try
                    {
                        var existing = JObject.Parse(existingJson);
                        CopyIfMissing(existing, j, "refresh_token");
                        CopyIfMissing(existing, j, "id_token");
                        CopyIfMissing(existing, j, "profile_name");
                        CopyIfMissing(existing, j, "profile_email");
                    }
                    catch { }
                }

                TryStampProfileFromIdToken(j);
                // add obtained timestamp if missing
                if (j.Value<long?>("obtained_at") == null)
                {
                    j["obtained_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                var normalized = AICAD.Services.JsonUtils.SerializeCompact(j);
                SaveProtectedTokenToFile(normalized);
            }
            catch { }
        }

        public static string LoadStoredTokenJson()
        {
            try
            {
                var fromFile = LoadProtectedTokenFromFile();
                if (!string.IsNullOrWhiteSpace(fromFile)) return fromFile;
            }
            catch { }

            try
            {
                var legacy = CredentialManager.ReadGenericSecret(CredentialTarget);
                if (string.IsNullOrWhiteSpace(legacy)) return null;

                // Validate before migrating.
                var parsed = JObject.Parse(legacy);
                TryStampProfileFromIdToken(parsed);
                var normalized = AICAD.Services.JsonUtils.SerializeCompact(parsed);
                SaveProtectedTokenToFile(normalized);
                return normalized;
            }
            catch
            {
                return null;
            }
        }

        public static void ClearToken()
        {
            try
            {
                var path = GetTokenFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }

            try
            {
                CredentialManager.DeleteGenericSecret(CredentialTarget);
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
#pragma warning disable CS0618
                    credential = GoogleCredential.FromStream(stream);
#pragma warning restore CS0618
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
                    SaveTokenJson(AICAD.Services.JsonUtils.SerializeCompact(json));
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

        private static string MergePersistedTokenData(JObject existingToken, string refreshedTokenJson)
        {
            var refreshed = JObject.Parse(refreshedTokenJson);
            if (existingToken != null)
            {
                CopyIfMissing(existingToken, refreshed, "refresh_token");
                CopyIfMissing(existingToken, refreshed, "id_token");
                CopyIfMissing(existingToken, refreshed, "profile_name");
                CopyIfMissing(existingToken, refreshed, "profile_email");
            }

            TryStampProfileFromIdToken(refreshed);
            return AICAD.Services.JsonUtils.SerializeCompact(refreshed);
        }

        private static void CopyIfMissing(JObject source, JObject target, string propertyName)
        {
            if (source == null || target == null || string.IsNullOrWhiteSpace(propertyName)) return;
            if (target[propertyName] != null) return;
            var token = source[propertyName];
            if (token != null) target[propertyName] = token.DeepClone();
        }

        private static void TryStampProfileFromIdToken(JObject tokenJson)
        {
            if (tokenJson == null) return;
            var idToken = tokenJson.Value<string>("id_token");
            if (string.IsNullOrWhiteSpace(idToken)) return;

            try
            {
                var payload = DecodeJwtPayload(idToken);
                if (payload == null) return;

                var name = payload.Value<string>("name") ?? payload.Value<string>("preferred_username");
                var email = payload.Value<string>("email");
                if (!string.IsNullOrWhiteSpace(name)) tokenJson["profile_name"] = name;
                if (!string.IsNullOrWhiteSpace(email)) tokenJson["profile_email"] = email;
            }
            catch { }
        }

        private static JObject DecodeJwtPayload(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                var payload = parts[1];
                payload = payload.Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static string GetTokenFilePath()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AI-CAD");
            Directory.CreateDirectory(root);
            return Path.Combine(root, TokenFileName);
        }

        private static void SaveProtectedTokenToFile(string tokenJson)
        {
            if (string.IsNullOrWhiteSpace(tokenJson)) return;
            var path = GetTokenFilePath();
            var plainBytes = Encoding.UTF8.GetBytes(tokenJson);
            var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, protectedBytes);
        }

        private static string LoadProtectedTokenFromFile()
        {
            var path = GetTokenFilePath();
            if (!File.Exists(path)) return null;
            var protectedBytes = File.ReadAllBytes(path);
            if (protectedBytes == null || protectedBytes.Length == 0) return null;
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
