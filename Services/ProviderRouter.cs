using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AICAD.Services
{
    internal static class ProviderRouter
    {
        private static readonly ConcurrentDictionary<string, DateTime> _deadUntil = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> GetFallbackOrder()
        {
            return new[] { "groq", "local", "gemini" };
        }

        public static bool IsDead(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return false;
            if (_deadUntil.TryGetValue(provider, out var until))
            {
                if (DateTime.UtcNow < until) return true;
                _deadUntil.TryRemove(provider, out _);
            }
            return false;
        }

        public static void MarkDead(string provider, int cooldownSeconds = 60)
        {
            if (string.IsNullOrWhiteSpace(provider)) return;
            if (cooldownSeconds <= 0) cooldownSeconds = 60;
            _deadUntil[provider] = DateTime.UtcNow.AddSeconds(cooldownSeconds);
        }
    }
}
