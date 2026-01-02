using System;
using System.Collections.Generic;
using System.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Local rate limiter to protect Groq free tier from overload.
    /// Groq free tier typical limits (as of 2024-2025):
    /// - 30 requests per minute
    /// - 14,400 requests per day
    /// - ~6,000 tokens per minute
    /// This limiter enforces conservative local limits to stay well within API bounds.
    /// </summary>
    public class GroqRateLimiter
    {
        private static readonly object _lock = new object();
        private static readonly List<DateTime> _requestHistory = new List<DateTime>();
        private static DateTime? _lastRequestTime = null;

        // Conservative limits for free tier
        public static int MaxRequestsPerMinute { get; set; } = 20;  // Under 30/min API limit
        public static int MaxRequestsPerHour { get; set; } = 300;    // ~5 per minute sustained
        public static int MaxRequestsPerDay { get; set; } = 12000;   // Under 14,400/day API limit
        public static TimeSpan MinDelayBetweenRequests { get; set; } = TimeSpan.FromSeconds(2); // Prevent bursts

        static GroqRateLimiter()
        {
            // Try to load custom limits from environment
            try
            {
                var minStr = Environment.GetEnvironmentVariable("GROQ_MAX_PER_MINUTE", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(minStr) && int.TryParse(minStr, out int minVal) && minVal > 0)
                    MaxRequestsPerMinute = minVal;

                var hourStr = Environment.GetEnvironmentVariable("GROQ_MAX_PER_HOUR", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(hourStr) && int.TryParse(hourStr, out int hourVal) && hourVal > 0)
                    MaxRequestsPerHour = hourVal;

                var dayStr = Environment.GetEnvironmentVariable("GROQ_MAX_PER_DAY", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(dayStr) && int.TryParse(dayStr, out int dayVal) && dayVal > 0)
                    MaxRequestsPerDay = dayVal;

                var delayStr = Environment.GetEnvironmentVariable("GROQ_MIN_DELAY_SECONDS", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(delayStr) && double.TryParse(delayStr, out double delayVal) && delayVal >= 0)
                    MinDelayBetweenRequests = TimeSpan.FromSeconds(delayVal);
            }
            catch { }
        }

        public class RateLimitResult
        {
            public bool Allowed { get; set; }
            public string Reason { get; set; }
            public TimeSpan? SuggestedWait { get; set; }
            public int RequestsInLastMinute { get; set; }
            public int RequestsInLastHour { get; set; }
            public int RequestsInLastDay { get; set; }
        }

        /// <summary>
        /// Check if a request is allowed based on rate limits.
        /// Call this BEFORE making a Groq API request.
        /// </summary>
        public static RateLimitResult CheckRequest()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var result = new RateLimitResult { Allowed = true };

                // Clean up old entries
                CleanupHistory(now);

                // Check minimum delay between requests
                if (_lastRequestTime.HasValue)
                {
                    var timeSinceLastRequest = now - _lastRequestTime.Value;
                    if (timeSinceLastRequest < MinDelayBetweenRequests)
                    {
                        result.Allowed = false;
                        result.SuggestedWait = MinDelayBetweenRequests - timeSinceLastRequest;
                        result.Reason = $"Too fast: Wait {result.SuggestedWait.Value.TotalSeconds:F1}s between requests (anti-burst protection)";
                        return result;
                    }
                }

                // Count requests in time windows
                var oneMinuteAgo = now.AddMinutes(-1);
                var oneHourAgo = now.AddHours(-1);
                var oneDayAgo = now.AddDays(-1);

                result.RequestsInLastMinute = _requestHistory.Count(t => t >= oneMinuteAgo);
                result.RequestsInLastHour = _requestHistory.Count(t => t >= oneHourAgo);
                result.RequestsInLastDay = _requestHistory.Count(t => t >= oneDayAgo);

                // Check per-minute limit
                if (result.RequestsInLastMinute >= MaxRequestsPerMinute)
                {
                    result.Allowed = false;
                    result.SuggestedWait = TimeSpan.FromSeconds(60);
                    result.Reason = $"Minute limit reached: {result.RequestsInLastMinute}/{MaxRequestsPerMinute} requests in last 60s. Wait 1 minute.";
                    return result;
                }

                // Check per-hour limit
                if (result.RequestsInLastHour >= MaxRequestsPerHour)
                {
                    result.Allowed = false;
                    result.SuggestedWait = TimeSpan.FromMinutes(10);
                    result.Reason = $"Hourly limit reached: {result.RequestsInLastHour}/{MaxRequestsPerHour} requests in last hour. Wait 10+ minutes.";
                    return result;
                }

                // Check per-day limit
                if (result.RequestsInLastDay >= MaxRequestsPerDay)
                {
                    result.Allowed = false;
                    result.SuggestedWait = TimeSpan.FromHours(1);
                    result.Reason = $"Daily limit reached: {result.RequestsInLastDay}/{MaxRequestsPerDay} requests today. Try again tomorrow.";
                    return result;
                }

                return result;
            }
        }

        /// <summary>
        /// Record a successful request. Call this AFTER making a Groq API request.
        /// </summary>
        public static void RecordRequest()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _requestHistory.Add(now);
                _lastRequestTime = now;
                CleanupHistory(now);
            }
        }

        /// <summary>
        /// Get current usage statistics for display/logging
        /// </summary>
        public static string GetUsageStats()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                CleanupHistory(now);

                var oneMinuteAgo = now.AddMinutes(-1);
                var oneHourAgo = now.AddHours(-1);
                var oneDayAgo = now.AddDays(-1);

                var perMin = _requestHistory.Count(t => t >= oneMinuteAgo);
                var perHour = _requestHistory.Count(t => t >= oneHourAgo);
                var perDay = _requestHistory.Count(t => t >= oneDayAgo);

                return $"Groq usage: {perMin}/{MaxRequestsPerMinute} per min, {perHour}/{MaxRequestsPerHour} per hour, {perDay}/{MaxRequestsPerDay} per day";
            }
        }

        /// <summary>
        /// Reset all tracking (for testing or manual override)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _requestHistory.Clear();
                _lastRequestTime = null;
            }
        }

        private static void CleanupHistory(DateTime now)
        {
            // Keep only last 24 hours of history
            var cutoff = now.AddDays(-1);
            _requestHistory.RemoveAll(t => t < cutoff);
        }
    }
}
