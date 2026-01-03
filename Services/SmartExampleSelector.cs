using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Intelligently selects the most relevant AND accurate few-shot examples
    /// from a large collection without feeding all to the LLM.
    /// 
    /// Strategy:
    /// 1. Find examples semantically similar to user prompt
    /// 2. Rank by accuracy (success rate, quality score)
    /// 3. Return top-k best examples
    /// </summary>
    public class SmartExampleSelector
    {
        public class ScoredExample
        {
            public JObject Example { get; set; }
            public double RelevanceScore { get; set; }  // 0-1: How similar to user prompt
            public double QualityScore { get; set; }    // 0-1: How well this example works
            public double CombinedScore { get; set; }   // Weighted combination
            public string Reason { get; set; }          // Why it was selected
        }

        /// <summary>
        /// Check if smart example selection is enabled via environment variable
        /// </summary>
        public static bool IsEnabled()
        {
            var setting = Environment.GetEnvironmentVariable("AICAD_SMART_EXAMPLE_SELECTION", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("AICAD_SMART_EXAMPLE_SELECTION");
            // Enabled by default (true) if not set or explicitly set to "1" or "true"
            if (string.IsNullOrEmpty(setting))
                return true;
            return setting == "1" || setting.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Select top-k most relevant AND accurate examples from a large pool
        /// </summary>
        public static List<ScoredExample> SelectBestExamples(
            string userPrompt,
            List<JObject> allExamples,
            int maxExamples = 3,
            bool verbose = false)
        {
            if (allExamples == null || allExamples.Count == 0)
                return new List<ScoredExample>();

            var scored = new List<ScoredExample>();

            foreach (var example in allExamples)
            {
                try
                {
                    // Calculate relevance: How similar is this example to user prompt?
                    var relevance = CalculateRelevance(userPrompt, example);

                    // Calculate quality: How well does this example work?
                    var quality = CalculateQuality(example);

                    // Combined score (relevance weighted 60%, quality 40%)
                    var combined = (relevance * 0.6) + (quality * 0.4);

                    scored.Add(new ScoredExample
                    {
                        Example = example,
                        RelevanceScore = relevance,
                        QualityScore = quality,
                        CombinedScore = combined,
                        Reason = GetSelectionReason(relevance, quality, example)
                    });
                }
                catch
                {
                    // Skip malformed examples
                }
            }

            // Sort by combined score (best first)
            var sorted = scored.OrderByDescending(s => s.CombinedScore).ToList();

            // Return top-k
            var result = sorted.Take(maxExamples).ToList();

            if (verbose)
            {
                AddinStatusLogger.Log("SmartExampleSelector", 
                    $"Selected {result.Count}/{allExamples.Count} examples. " +
                    $"Scores: {string.Join(", ", result.Select(r => r.CombinedScore.ToString("F2")))}");
            }

            return result;
        }

        /// <summary>
        /// Calculate relevance score (0-1) based on semantic similarity
        /// </summary>
        private static double CalculateRelevance(string userPrompt, JObject example)
        {
            try
            {
                var examplePrompt = example["prompt"]?.Value<string>() ?? "";
                var exampleCategory = example["category"]?.Value<string>() ?? "";
                var exampleDescription = example["description"]?.Value<string>() ?? "";

                // Extract keywords from user prompt
                var userKeywords = ExtractKeywords(userPrompt);

                // Score matches in example
                double score = 0.0;
                int matches = 0;

                // Check prompt similarity (weight: 0.5)
                foreach (var keyword in userKeywords)
                {
                    if (examplePrompt.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 0.5;
                        matches++;
                    }
                }

                // Check category match (weight: 0.3)
                foreach (var keyword in userKeywords)
                {
                    if (exampleCategory.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 0.3;
                        matches++;
                    }
                }

                // Check description match (weight: 0.2)
                foreach (var keyword in userKeywords)
                {
                    if (exampleDescription.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 0.2;
                        matches++;
                    }
                }

                // Normalize: max score is sum of all keyword matches
                if (matches > 0)
                {
                    score = Math.Min(1.0, score / (userKeywords.Count * 0.5));
                }

                return Math.Max(0, Math.Min(1, score));
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Calculate quality score (0-1) based on success rate and recency
        /// </summary>
        private static double CalculateQuality(JObject example)
        {
            try
            {
                double score = 0.5; // Base score

                // Success rate (if tracked)
                if (example["success_count"] != null && example["total_count"] != null)
                {
                    var successes = example["success_count"].Value<int>();
                    var total = example["total_count"].Value<int>();
                    if (total > 0)
                    {
                        var successRate = (double)successes / total;
                        score = successRate; // 0-1 based on success rate
                    }
                }

                // Recency bonus (prefer recently successful examples)
                if (example["timestamp"] != null && DateTime.TryParse(
                    example["timestamp"].Value<string>(), out var timestamp))
                {
                    var age = DateTime.UtcNow - timestamp;
                    if (age.TotalDays < 7)
                        score += 0.1; // Recent examples get +10%
                    else if (age.TotalDays > 90)
                        score -= 0.05; // Old examples get -5%
                }

                // Quality rating (if available)
                if (example["quality_rating"] != null)
                {
                    var rating = example["quality_rating"].Value<int>(); // 1-5
                    score = Math.Max(score, rating / 5.0);
                }

                // Complexity bonus (well-structured examples score higher)
                if (example["expected_json"] != null)
                {
                    var json = example["expected_json"].ToString();
                    if (json.Length > 100 && json.Contains("parameters"))
                        score = Math.Min(1.0, score + 0.05);
                }

                return Math.Max(0, Math.Min(1, score));
            }
            catch
            {
                return 0.3; // Default mediocre score
            }
        }

        /// <summary>
        /// Extract key terms from a prompt for matching
        /// </summary>
        private static List<string> ExtractKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var keywords = new List<string>();
            
            // Extract geometric terms
            var geometryTerms = new[] { 
                "cube", "box", "cylinder", "sphere", "cone", "wedge", "prism",
                "chamfer", "fillet", "hole", "pocket", "cut", "extrude", "revolve",
                "sketch", "plane", "edge", "face", "corner"
            };

            foreach (var term in geometryTerms)
            {
                if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    keywords.Add(term);
            }

            // Extract dimension numbers and units
            var dimensionPattern = new System.Text.RegularExpressions.Regex(@"(\d+)\s*(mm|cm|m|in)?");
            var matches = dimensionPattern.Matches(text);
            if (matches.Count > 0)
                keywords.Add("dimensions");

            // Extract feature types
            if (text.IndexOf("pattern", StringComparison.OrdinalIgnoreCase) >= 0)
                keywords.Add("pattern");
            if (text.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) >= 0)
                keywords.Add("mirror");
            if (text.IndexOf("array", StringComparison.OrdinalIgnoreCase) >= 0)
                keywords.Add("array");

            return keywords.Distinct().ToList();
        }

        /// <summary>
        /// Generate human-readable reason for selection
        /// </summary>
        private static string GetSelectionReason(double relevance, double quality, JObject example)
        {
            var reasons = new List<string>();

            if (relevance > 0.7)
                reasons.Add("Highly relevant");
            else if (relevance > 0.4)
                reasons.Add("Moderately relevant");

            if (quality > 0.8)
                reasons.Add("Proven reliable");
            else if (quality > 0.5)
                reasons.Add("Good quality");

            var category = example["category"]?.Value<string>() ?? "Unknown";
            if (!string.IsNullOrEmpty(category))
                reasons.Add($"Category: {category}");

            return string.Join(" | ", reasons);
        }

        /// <summary>
        /// Track example usage for quality scoring
        /// </summary>
        public static void RecordExampleUsage(JObject example, bool success)
        {
            try
            {
                var id = example["_id"]?.Value<string>() ?? example["id"]?.Value<string>();
                
                // Increment usage counter
                var totalCount = example["total_count"]?.Value<int>() ?? 0;
                example["total_count"] = totalCount + 1;

                // Increment success counter if successful
                if (success)
                {
                    var successCount = example["success_count"]?.Value<int>() ?? 0;
                    example["success_count"] = successCount + 1;
                }

                // Update timestamp
                example["timestamp"] = DateTime.UtcNow.ToIso8601String();

                AddinStatusLogger.Log("SmartExampleSelector", 
                    $"Recorded {(success ? "success" : "failure")} for example {id}");
            }
            catch
            {
                // Silently fail - don't break execution
            }
        }
    }

    public static class DateTimeExtensions
    {
        public static string ToIso8601String(this DateTime dt)
        {
            return dt.ToString("o");
        }
    }
}
