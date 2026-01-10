using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Lightweight classifier that makes a "cheap" LLM call (prefer local) to classify a user intent
    /// into a category like "Hardware_Bent" or "Prismatic" and returns a specialized system prompt.
    /// It falls back to simple heuristics if no LLM provider is configured.
    /// </summary>
    public static class ClassifierService
    {
        // Mapping from category → specialized system prompt
        private static readonly Dictionary<string, string> _systemPrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Hardware_Bent", "You are an expert in swept hardware. To make a U-bolt: 1. Sketch the path ONLY on the Front Plane. 2. Output the 'sweep' operation with \"type\": \"circular\" and a \"diameter\" field (e.g., 12 for M12). Do NOT create a profile sketch." },
            { "Prismatic", "You are an expert in prismatic machined parts. Prefer sketch+extrude, extrude_cut for cuts, and Hole features for holes. Return only a compact JSON object when classifying, e.g. { \"category\": \"Prismatic\" }. Units: mm. Output ONLY JSON." },
            { "Rotational", "You are an expert in rotational/turned parts. Prefer revolve for bodies, use clear axis and profile, and use cosmetic thread for performance unless explicit real thread requested. Return only a compact JSON object when classifying, e.g. { \"category\": \"Rotational\" }. Units: mm. Output ONLY JSON." },
            { "Sheet_Metal", "You are an expert in sheet-metal parts. Prefer base flange and edge flange operations, honor bend radius, K-factor, and bend allowance. Output only a compact JSON object when classifying, e.g. { \"category\": \"Sheet_Metal\" }. Units: mm. Output ONLY JSON." },
            { "Fastener_Threaded", "You are an expert in threaded fasteners: bolts, screws, studs. Prefer revolve for shank/head, and use cosmetic thread unless user requests modeled thread. Expose size/pitch/head style. Output only a compact JSON object when classifying, e.g. { \"category\": \"Fastener_Threaded\", \"standard\": \"DIN_933\" }. Units: mm. Output ONLY JSON." },
            { "Washer", "You are an expert in washers (flat and spring). Prefer revolve/extrude for rings; for spring washers include helix/sweep-cut operations if modeled. Output only a compact JSON object when classifying, e.g. { \"category\": \"Washer\" }. Units: mm. Output ONLY JSON." },
            { "Plate", "You are an expert in mounting plates and flat plates. Prefer prismatic extrude with Hole Wizard patterns; expose pattern params. Output only a compact JSON object when classifying, e.g. { \"category\": \"Plate\" }. Units: mm. Output ONLY JSON." }
        };

        /// <summary>
        /// Returns the active system prompt for a given user input. Tries a cheap local LLM classification first,
        /// otherwise falls back to simple keyword heuristics.
        /// </summary>
        public static string GetSystemPromptForInput(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return ClarificationService.DEFAULT_SYSTEM_PROMPT;

            try
            {
                // Try local classifier if endpoint is configured
                var localEndpoint = Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.User)
                                    ?? Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.Process)
                                    ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(localEndpoint))
                {
                        var classifierSystemPrompt = Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", EnvironmentVariableTarget.User)
                                                         ?? "You are a small classifier. Reply only with a compact JSON object containing 'category' and optional 'standard' fields. Allowed categories: Hardware_Bent, Prismatic, Rotational, Sheet_Metal, Fastener_Threaded, Washer, Plate.";
                        var classifierPrompt = "Classify the following user request into one of: Hardware_Bent, Prismatic, Rotational, Sheet_Metal, Fastener_Threaded, Washer, Plate. Return ONLY a compact JSON object with keys 'category' and optionally 'standard'. Do NOT include any extra explanation. User request: \"" + userInput.Replace("\"", "\\\"") + "\"";

                    try
                    {
                        using (var localClient = new LocalHttpLlmClient(localEndpoint, "gpt-3.5-mini", classifierSystemPrompt))
                        {
                            var reply = localClient.GenerateAsync(classifierPrompt).GetAwaiter().GetResult();
                            if (!string.IsNullOrWhiteSpace(reply))
                            {
                                try
                                {
                                    var tok = JToken.Parse(reply);
                                    if (tok is JObject obj)
                                    {
                                        var category = obj.Value<string>("category");
                                        if (!string.IsNullOrWhiteSpace(category) && _systemPrompts.ContainsKey(category))
                                        {
                                            return _systemPrompts[category];
                                        }
                                    }
                                }
                                catch { /* ignore parse errors */ }
                            }
                        }
                    }
                    catch { /* local client failure; fall through to heuristics */ }
                }
            }
            catch { /* swallow classifier failures and fall back to heuristics */ }

            // Heuristic fallback: keyword mapping to categories (order matters)
            var lower = userInput.ToLowerInvariant();
            // Sheet metal indicators
            if (lower.Contains("sheet metal") || lower.Contains("bend") || lower.Contains("k-factor") || lower.Contains("bend allowance") || lower.Contains("edge flange") || lower.Contains("hem") || lower.Contains("gusset"))
            {
                return _systemPrompts["Sheet_Metal"];
            }
            // U-bolts and bent hardware: check these first (specific)
            if (lower.Contains("u-bolt") || lower.Contains("u bolt") || lower.Contains("u-bolts") || lower.Contains("u bolts") || lower.Contains("u-shaped bolt") || lower.Contains("u shaped bolt"))
            {
                return _systemPrompts["Hardware_Bent"];
            }
            // Fastener / threaded indicators (bolts, screws, studs) — avoid matching U-bolts above
            if (((lower.Contains("bolt") && !(lower.Contains("u-bolt") || lower.Contains("u bolt") || lower.Contains("u-bolts") || lower.Contains("u bolts"))) || lower.Contains("screw") || lower.Contains("m10") || lower.Contains("m8") || lower.Contains("thread") || lower.Contains("nut") || lower.Contains("stud") || lower.Contains("threaded rod")))
            {
                return _systemPrompts["Fastener_Threaded"];
            }
            // Washers
            if (lower.Contains("washer") || lower.Contains("spring washer") || lower.Contains("flat washer"))
            {
                return _systemPrompts["Washer"];
            }
            // Plate / mounting plate
            if (lower.Contains("plate") || lower.Contains("mounting plate") || lower.Contains("hole wizard") || lower.Contains("pattern") )
            {
                return _systemPrompts["Plate"];
            }
            // Rotational/turned parts
            if (lower.Contains("shaft") || lower.Contains("turned") || lower.Contains("lathe") || lower.Contains("revolve") || lower.Contains("revolved") )
            {
                return _systemPrompts["Rotational"];
            }
            // Prismatic fallback
            if (lower.Contains("block") || lower.Contains("bracket") || lower.Contains("extrude") || lower.Contains("hole") || lower.Contains("chamfer") || lower.Contains("fillet"))
            {
                return _systemPrompts["Prismatic"];
            }

            // Default
            return ClarificationService.DEFAULT_SYSTEM_PROMPT;
        }

        /// <summary>
        /// Public accessor to map a category name to a stored system prompt (if present).
        /// </summary>
        public static bool TryGetPromptForCategory(string category, out string systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                systemPrompt = null; return false;
            }
            return _systemPrompts.TryGetValue(category, out systemPrompt);
        }

        /// <summary>
        /// Add or update a system prompt mapping for a category at runtime.
        /// </summary>
        public static void RegisterOrUpdateCategoryPrompt(string category, string prompt)
        {
            if (string.IsNullOrWhiteSpace(category) || prompt == null) return;
            _systemPrompts[category] = prompt;
        }
    }
}
