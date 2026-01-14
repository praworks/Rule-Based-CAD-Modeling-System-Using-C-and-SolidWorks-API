using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AICAD.Services.Logging
{
    /// <summary>
    /// Centralized redaction/sanitization for any potentially sensitive text.
    /// </summary>
    internal static class LogRedactor
    {
        private const int DefaultHashThreshold = 200;

        public static string Sanitize(string text, int hashThreshold = DefaultHashThreshold)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                var scrubbed = StripSecrets(text);
                return HashIfLarge(scrubbed, hashThreshold);
            }
            catch
            {
                return "<redaction_error>";
            }
        }

        public static string Truncate(string text, int maxLen)
        {
            if (maxLen < 1) return string.Empty;
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLen) return text;
            var hash = Sha256(text);
            var head = text.Substring(0, Math.Min( Math.Max(8, maxLen / 2), text.Length));
            var tail = text.Substring(Math.Max(0, text.Length - Math.Max(8, maxLen / 2)));
            return $"{head}...{tail} len={text.Length} hash={hash}";
        }

        public static string HashIfLarge(string text, int threshold = DefaultHashThreshold, int keepChars = 64)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return string.Empty;
                if (text.Length <= threshold) return text;
                var hash = StableHash(text);
                var head = text.Substring(0, Math.Min(keepChars, text.Length));
                return $"{head}... [hash:{hash}]";
            }
            catch
            {
                return "<hash_failed>";
            }
        }

        public static string StableHash(string input, int take = 12)
        {
            try
            {
                var full = Sha256(input);
                if (take > 0 && take < full.Length) return full.Substring(0, take);
                return full;
            }
            catch { return "0000"; }
        }

        private static string Sha256(string input)
        {
            try
            {
                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                    var hash = sha.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch
            {
                return "0000";
            }
        }

        private static string StripSecrets(string text)
        {
            try
            {
                var scrubbed = text ?? string.Empty;
                scrubbed = Regex.Replace(scrubbed, @"(sk-[A-Za-z0-9]{20,})", "***");
                scrubbed = Regex.Replace(scrubbed, @"(?i)api[_-]?key\s*[:=]\s*[^\\s]+", "api_key=***");
                scrubbed = Regex.Replace(scrubbed, @"(?i)bearer\s+[A-Za-z0-9\.\-_]+", "bearer ***");
                return scrubbed;
            }
            catch
            {
                return "***";
            }
        }
    }
}
