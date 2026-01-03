using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Helper for locating Google OAuth client configuration (client id/secret) either from
    /// environment variables or from a local client_secret*.json file bundled with the add-in.
    /// </summary>
    public sealed class GoogleOAuthConfig
    {
        private GoogleOAuthConfig(string clientId, string clientSecret, string sourcePath, string[] scopes)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            SourcePath = sourcePath;
            Scopes = scopes ?? Array.Empty<string>();
        }

        public string ClientId { get; }
        public string ClientSecret { get; }
        public string SourcePath { get; }
        public IReadOnlyList<string> Scopes { get; }
        public bool HasClientSecret => !string.IsNullOrWhiteSpace(ClientSecret);

        private static GoogleOAuthConfig _cached;

        public static GoogleOAuthConfig Load()
        {
            if (_cached != null) return _cached;
            _cached = TryLoadInternal();
            return _cached;
        }

        public static void RefreshCache() => _cached = null;

        private static GoogleOAuthConfig TryLoadInternal()
        {
            var clientId = ReadEnv("GOOGLE_OAUTH_CLIENT_ID");
            var clientSecret = ReadEnv("GOOGLE_OAUTH_CLIENT_SECRET");
            string sourcePath = null;

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                sourcePath = LocateClientSecretJson();
                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    try
                    {
                        var text = File.ReadAllText(sourcePath);
                        var root = JObject.Parse(text);
                        var installed = root["installed"] as JObject ?? root["web"] as JObject;
                        if (installed != null)
                        {
                            var fileClientId = installed.Value<string>("client_id");
                            var fileClientSecret = installed.Value<string>("client_secret");
                            if (!string.IsNullOrWhiteSpace(fileClientId) && fileClientId.IndexOf("REPLACE", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                if (string.IsNullOrWhiteSpace(clientId)) clientId = fileClientId.Trim();
                            }
                            if (!string.IsNullOrWhiteSpace(fileClientSecret))
                            {
                                if (string.IsNullOrWhiteSpace(clientSecret)) clientSecret = fileClientSecret.Trim();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore JSON parse errors; caller will handle missing config.
                        sourcePath = null;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(clientId)) return null;

            // Include OpenID scopes so ID token (email/name) is returned during the OAuth flow,
            // while also keeping cloud-platform for API access when present.
            var scopes = new[] { "openid", "email", "profile", "https://www.googleapis.com/auth/cloud-platform" };
            return new GoogleOAuthConfig(clientId.Trim(), string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim(), sourcePath, scopes);
        }

        private static string ReadEnv(string name)
        {
            var targets = new[]
            {
                EnvironmentVariableTarget.User,
                EnvironmentVariableTarget.Process,
                EnvironmentVariableTarget.Machine
            };
            foreach (var target in targets)
            {
                try
                {
                    var value = Environment.GetEnvironmentVariable(name, target);
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
                catch
                {
                    // Ignore failures and continue.
                }
            }
            return null;
        }

        private static string LocateClientSecretJson()
        {
            var envPath = ReadEnv("GOOGLE_OAUTH_CLIENT_FILE");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                try
                {
                    var full = Path.GetFullPath(envPath);
                    if (File.Exists(full)) return full;
                }
                catch
                {
                    // Ignore and fall back to discovery.
                }
            }

            try
            {
                foreach (var dir in EnumerateCandidateDirectories())
                {
                    try
                    {
                        var matches = Directory.GetFiles(dir, "client_secret*.json", SearchOption.TopDirectoryOnly);
                        if (matches.Length > 0) return matches[0];
                    }
                    catch { }
                }

                // Also search Secrets/ subdirectory in each candidate directory
                foreach (var dir in EnumerateCandidateDirectories())
                {
                    try
                    {
                        var secretsDir = Path.Combine(dir, "Secrets");
                        if (Directory.Exists(secretsDir))
                        {
                            var matches = Directory.GetFiles(secretsDir, "client_secret*.json", SearchOption.TopDirectoryOnly);
                            if (matches.Length > 0) return matches[0];
                        }
                    }
                    catch { }
                }

                foreach (var dir in EnumerateCandidateDirectories())
                {
                    try
                    {
                        var placeholder = Path.Combine(dir, "client_oauth_placeholder.json");
                        if (File.Exists(placeholder)) return placeholder;
                    }
                    catch { }
                }
            }
            catch
            {
                // Ignore discovery errors.
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidateDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<string>();

            void Capture(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try { path = Path.GetFullPath(path); } catch { }
                if (!Directory.Exists(path)) return;
                if (seen.Add(path)) results.Add(path);
            }

            try
            {
                Capture(AppDomain.CurrentDomain.BaseDirectory);
                var asmLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(asmLocation))
                {
                    var dir = Path.GetDirectoryName(asmLocation);
                    while (!string.IsNullOrWhiteSpace(dir))
                    {
                        Capture(dir);
                        var parent = Directory.GetParent(dir);
                        if (parent == null) break;
                        dir = parent.FullName;
                    }
                }
                Capture(Environment.CurrentDirectory);
            }
            catch
            {
                // Ignore failures.
            }

            return results;
        }
    }
}
