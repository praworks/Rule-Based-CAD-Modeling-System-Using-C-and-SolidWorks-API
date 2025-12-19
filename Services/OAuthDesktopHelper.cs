using System;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Net.Sockets;

namespace AICAD.Services
{
    // Minimal Desktop OAuth (PKCE + loopback) helper for Google OAuth
    // Usage: call OAuthDesktopHelper.AuthorizeAsync(clientId, scopes) to get a JSON token response.
    public static class OAuthDesktopHelper
    {
        public static async Task<string> AuthorizeAsync(string clientId, string[] scopes)
        {
            if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException(nameof(clientId));
            var codeVerifier = RandomString(64);
            var codeChallenge = Base64UrlEncode(Sha256(codeVerifier));
            var state = Guid.NewGuid().ToString("N");
            var port = GetFreePort();
            var redirect = $"http://127.0.0.1:{port}/";
            var scope = string.Join(" ", scopes ?? new string[] { "openid", "email", "profile" });
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}&scope={Uri.EscapeDataString(scope)}&state={state}&code_challenge={codeChallenge}&code_challenge_method=S256&access_type=offline&prompt=consent";

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(redirect);
                listener.Start();
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                var ctx = await listener.GetContextAsync().ConfigureAwait(false);
                var req = ctx.Request;
                var res = ctx.Response;
                var query = req.QueryString;
                var returnedState = query["state"];
                var code = query["code"];
                var html = "<html><body><h2>Authentication complete. You can close this window.</h2></body></html>";
                var buffer = Encoding.UTF8.GetBytes(html);
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
                res.OutputStream.Close();
                listener.Stop();

                if (returnedState != state) throw new InvalidOperationException("Invalid state");
                if (string.IsNullOrEmpty(code)) throw new InvalidOperationException("No code returned");

                // Exchange code for tokens
                var token = await ExchangeCodeAsync(code, clientId, codeVerifier, redirect).ConfigureAwait(false);
                return token;
            }
        }

        private static async Task<string> ExchangeCodeAsync(string code, string clientId, string codeVerifier, string redirect)
        {
            using (var http = new HttpClient())
            {
                var dict = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = clientId,
                    ["code_verifier"] = codeVerifier,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = redirect
                };
                var content = new FormUrlEncodedContent(dict);
                var resp = await http.PostAsync("https://oauth2.googleapis.com/token", content).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("Token exchange failed: " + text);
                return text;
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string RandomString(int length)
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private static byte[] Sha256(string input)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.ASCII.GetBytes(input));
            }
        }

        private static string Base64UrlEncode(byte[] input)
        {
            var s = Convert.ToBase64String(input);
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-');
            s = s.Replace('/', '_');
            return s;
        }
    }
}
