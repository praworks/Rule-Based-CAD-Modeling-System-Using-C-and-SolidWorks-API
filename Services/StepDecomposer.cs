using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AICAD.Services
{
    /// <summary>
    /// Decomposes complex multi-step prompts into individual executable steps.
    /// Example: "Create cube with 1mm chamfer" → ["Create cube", "Add 1mm chamfer"]
    /// </summary>
    public static class StepDecomposer
    {
        /// <summary>
        /// Breaks a complex prompt into simpler, independent steps
        /// </summary>
        public static List<string> DecomposePrompt(string userPrompt)
        {
            var steps = new List<string>();
            if (string.IsNullOrWhiteSpace(userPrompt))
                return steps;

            userPrompt = userPrompt.Trim();

            // Pattern 1: "Create X with Y" or "Make X with Y"
            // Example: "Create cube 50x50x50mm with 1mm chamfer"
            var withMatch = Regex.Match(userPrompt, @"^(.*?)\s+with\s+(.+)$", RegexOptions.IgnoreCase);
            if (withMatch.Success && withMatch.Groups.Count >= 3)
            {
                var basePart = withMatch.Groups[1].Value.Trim();
                var features = withMatch.Groups[2].Value.Trim();
                
                steps.Add(basePart);  // "Create cube 50x50x50mm"
                
                // Handle "X and Y" in the features part
                if (features.Contains(" and "))
                {
                    foreach (var feature in features.Split(new[] { " and " }, StringSplitOptions.None))
                        if (!string.IsNullOrWhiteSpace(feature))
                            steps.Add("Then add " + feature.Trim());
                }
                else
                {
                    steps.Add("Then add " + features);
                }
                
                return steps;
            }

            // Pattern 2: "Create X. Add Y. Apply Z"
            if (userPrompt.Contains(". "))
            {
                var parts = userPrompt.Split(new[] { ". " }, StringSplitOptions.None);
                foreach (var part in parts)
                    if (!string.IsNullOrWhiteSpace(part))
                        steps.Add(part.Trim());
                return steps;
            }

            // Pattern 3: "Create X, then Y, then Z"
            if (userPrompt.Contains(", then "))
            {
                var parts = userPrompt.Split(new[] { ", then " }, StringSplitOptions.None);
                foreach (var part in parts)
                    if (!string.IsNullOrWhiteSpace(part))
                        steps.Add(part.Trim());
                return steps;
            }

            // Pattern 4: "Create X\nAdd Y\nApply Z" (multiline)
            if (userPrompt.Contains("\n"))
            {
                var lines = userPrompt.Split(new[] { "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                    if (!string.IsNullOrWhiteSpace(line))
                        steps.Add(line.Trim());
                return steps;
            }

            // Default: single step (no decomposition needed)
            steps.Add(userPrompt);
            return steps;
        }

        /// <summary>
        /// Checks if a prompt likely requires multiple steps
        /// </summary>
        public static bool IsComplexPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            // Check for multi-step keywords
            var keywords = new[] { " with ", ", then ", ". Add", ". Apply", "\nAdd", "\nApply" };
            foreach (var k in keywords)
                if (prompt.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>
        /// Categorizes a step for proper routing to LLM/executor
        /// </summary>
        public static string CategorizeStep(string step)
        {
            step = step.ToLower();
            
            if (step.StartsWith("create") || step.StartsWith("make"))
                return "base_feature";
            else if (step.StartsWith("add") || step.StartsWith("apply"))
                return "applied_feature";
            else if (step.StartsWith("then"))
                return "applied_feature";
            else if (step.Contains("pattern") || step.Contains("mirror"))
                return "pattern";
            else if (step.Contains("hole") || step.Contains("pocket"))
                return "cut_feature";
            else if (step.Contains("fillet") || step.Contains("chamfer"))
                return "edge_feature";
            else
                return "generic";
        }
    }
}
