using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal static class PromptCatalog
    {
        private static readonly Lazy<JObject> Catalog = new Lazy<JObject>(LoadCatalog);
        private static readonly object _warnLock = new object();
        private static readonly HashSet<string> _warnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Hardcoded fallback for DECOMPOSE stage when catalog file/resource is unavailable.
        internal const string FALLBACK_DECOMPOSE_SYSTEM_PROMPT =
            "You are a decomposition assistant. Given a user request, return a JSON object with keys 'description' (short description), 'needs_description' (true/false), 'question' (if needs_description true), and 'features' (an array of feature objects). Each feature should be a JSON object describing a single feature and must NOT include executor fields like 'steps' or 'op'. The 'features' array should be minimal and schematic to allow downstream planning. Example: {\\\"description\\\": \\\"...\\\", \\\"needs_description\\\": false, \\\"question\\\": null, \\\"features\\\": [ {\\\"feature_type\\\": \\\"hole\\\", \\\"params\\\": {\\\"x\\\":0} } ] }";
        internal const string FALLBACK_EXECUTE_SYSTEM_PROMPT =
            "You are a CAD execution agent for SOLIDWORKS. You will be given ONE CAD feature task produced by the DECOMPOSE stage. Use ONLY the allowed operations listed in the user message; do NOT invent new ops. If required inputs are missing, return a clarification JSON object. Otherwise return JSON with a 'steps' array (each step uses the 'op' field; never use 'command'). You may include an optional 'thinking' string but it is not required. Output RAW JSON only.";
        internal const string FALLBACK_DEFAULT_SYSTEM_PROMPT =
            "You are a helpful CAD assistant for SOLIDWORKS. Follow the provided instructions and output JSON when asked.";
        internal const string FALLBACK_DECOMPOSE_TEMPLATE =
            "{systemPrompt}\n\nUSER REQUEST:\n{userRequest}\n\nReturn the decomposition JSON object now.";
        internal const string FALLBACK_EXECUTE_TEMPLATE =
            "{systemPrompt}\n\nALLOWED OPS (use only these, do NOT invent new ops):\n{allowedOps}\n\nIf unsure for boxes/cubes: select_plane -> sketch_begin -> rectangle_center -> dimension -> sketch_end -> extrude.\n\n{factsSection}FEATURE TASK:\n{featureTask}\n\nRespond with JSON only:\n{ \"steps\": [ { \"op\": \"...\", \"params\": { ... } } ] }\nIf required inputs are missing, return the clarification shape instead.";

        public static string GetSystemPrompt(string key)
        {
            var val = GetString("systemPrompts", key);
            if (!string.IsNullOrWhiteSpace(val))
                return val;

            if (string.Equals(key, "decompose_system", StringComparison.OrdinalIgnoreCase))
                return UseFallbackOnce("systemPrompts.decompose_system", FALLBACK_DECOMPOSE_SYSTEM_PROMPT);
            if (string.Equals(key, "execute_system", StringComparison.OrdinalIgnoreCase))
                return UseFallbackOnce("systemPrompts.execute_system", FALLBACK_EXECUTE_SYSTEM_PROMPT);
            if (string.Equals(key, "default", StringComparison.OrdinalIgnoreCase))
                return UseFallbackOnce("systemPrompts.default", FALLBACK_DEFAULT_SYSTEM_PROMPT);

            return string.Empty;
        }

        public static string GetSystemPromptForFeature(string featureKey)
        {
            try
            {
                var node = Catalog.Value;
                var byFeature = node?["systemPromptsByFeature"] as JObject;
                if (byFeature == null) return string.Empty;
                var token = byFeature[featureKey];
                if (token == null) return string.Empty;
                return token.Value<string>() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetTemplate(string key)
        {
            var val = GetString("templates", key);
            if (!string.IsNullOrWhiteSpace(val))
                return val;

            if (string.Equals(key, "decompose_template", StringComparison.OrdinalIgnoreCase))
                return UseFallbackOnce("templates.decompose_template", FALLBACK_DECOMPOSE_TEMPLATE);
            if (string.Equals(key, "execute_template", StringComparison.OrdinalIgnoreCase))
                return UseFallbackOnce("templates.execute_template", FALLBACK_EXECUTE_TEMPLATE);

            return string.Empty;
        }

        private static JObject LoadCatalog()
        {
            var candidates = GetCatalogSearchPaths();
            try
            {
                DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", "PromptCatalog search paths: " + string.Join(" | ", candidates));
            }
            catch { }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                    continue;
                try
                {
                    var content = File.ReadAllText(candidate);
                    var parsed = JObject.Parse(content);
                    ValidateSystemPrompts(parsed);
                    try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", $"Loaded PromptCatalog from '{candidate}'."); } catch { }
                    return parsed;
                }
                catch (Exception ex)
                {
                    try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "ERROR", $"Failed to parse/validate PromptCatalog at '{candidate}': {ex}"); } catch { }
                    // continue to next candidate / embedded fallback
                }
            }

            // Try embedded resource fallback
            var embedded = TryLoadFromEmbeddedResource(out var resourceName, out var embeddedError);
            if (embedded != null)
            {
                try
                {
                    ValidateSystemPrompts(embedded);
                    try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", $"Loaded PromptCatalog from embedded resource '{resourceName}'."); } catch { }
                    return embedded;
                }
                catch (Exception ex)
                {
                    try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "ERROR", $"Embedded PromptCatalog validation failed ({resourceName}): {ex}"); } catch { }
                }
            }
            else if (embeddedError != null)
            {
                try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "ERROR", $"Embedded PromptCatalog parse failed: {embeddedError}"); } catch { }
            }

            try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "WARN", "PromptCatalog not found/invalid on disk and embedded resource missing or invalid. Using built-in defaults."); } catch { }
            return BuildFallbackCatalog();
        }

        private static JObject TryLoadFromEmbeddedResource(out string resourceName, out Exception parseError)
        {
            resourceName = null;
            parseError = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames();
                var res = names.FirstOrDefault(n => n.EndsWith("PromptCatalog.json", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(res)) return null;
                resourceName = res;
                using (var s = asm.GetManifestResourceStream(res))
                {
                    if (s == null) return null;
                    using (var sr = new StreamReader(s))
                    {
                        var txt = sr.ReadToEnd();
                        try
                        {
                            return JObject.Parse(txt);
                        }
                        catch (Exception ex)
                        {
                            parseError = ex;
                            return null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                parseError = ex;
                return null;
            }
        }

        private static void ValidateSystemPrompts(JObject catalog)
        {
            if (catalog == null)
                return;

            var sys = catalog["systemPrompts"] as JObject;
            if (sys == null)
                return;

            string Get(string k)
            {
                try
                {
                    var t = sys[k];
                    return t?.Value<string>() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            var decompose = Get("decompose_system").Trim();
            var execute = Get("execute_system").Trim();

            // decompose: must NOT contain steps/op/thinking and should reference feature task contract
            var decompLower = decompose.ToLowerInvariant();
            if (decompLower.Contains("\"steps\"") || decompLower.Contains("\"op\"") || decompLower.Contains("thinking"))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'decompose_system' contains executor tokens (\"steps\", \"op\", or 'thinking'). Decompose prompt must stay schematic.");
            }
            if (!decompLower.Contains("\"features\"") || !decompLower.Contains("needs_description") || !decompLower.Contains("\"question\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'decompose_system' must reference 'features', 'needs_description', and 'question'.");
            }

            // execute: must contain steps and op, must not request feature_type inside steps
            var execLower = execute.ToLowerInvariant();
            if (!execLower.Contains("\"steps\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must mention \"steps\" in its contract.");
            }
            if (!execLower.Contains("\"op\"") && !execLower.Contains(" op\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must require steps use the \"op\" field (never use 'command').");
            }
            if (!execLower.Contains("clarification_needed"))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must describe the clarification flow (clarification_needed).");
            }
            if (!execLower.Contains("\"feature_index\"") || !execLower.Contains("\"feature_type\"") || !execLower.Contains("\"questions\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must mention feature_index, feature_type, and questions for clarifications.");
            }
            if (execLower.Contains("\"command\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must use field name 'op' and must not use 'command'.");
            }
        }

        private static string LocateCatalogPath()
        {
            foreach (var candidate in GetCatalogSearchPaths())
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private static List<string> GetCatalogSearchPaths()
        {
            var paths = new List<string>();
            void Add(string p)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                if (!paths.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                    paths.Add(p);
            }

            try
            {
                var asmLocation = typeof(PromptCatalog).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(asmLocation))
                {
                    var asmDir = Path.GetDirectoryName(asmLocation);
                    if (!string.IsNullOrWhiteSpace(asmDir))
                    {
                        Add(Path.Combine(asmDir, "Config", "PromptCatalog.json"));
                        Add(Path.Combine(asmDir, "PromptCatalog.json"));
                    }
                }
            }
            catch { }

            var baseDir = AppContext.BaseDirectory;
            try
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, "Config", "PromptCatalog.json");
                    if (visited.Contains(candidate))
                        continue;
                    visited.Add(candidate);
                    Add(candidate);
                }
            }
            catch { }

            Add(Path.Combine(Environment.CurrentDirectory, "Config", "PromptCatalog.json"));

            try
            {
                var repoCandidate = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Config", "PromptCatalog.json");
                Add(Path.GetFullPath(repoCandidate));
            }
            catch { }

            return paths;
        }

        private static string GetString(string section, string key)
        {
            try
            {
                var node = Catalog.Value;
                var token = node?[section]?[key];
                if (token == null)
                    return string.Empty;
                return token.Value<string>() ?? string.Empty;
            }
            catch (Exception ex)
            {
                try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "ERROR", $"PromptCatalog GetString failure: section={section} key={key} ex={ex.Message}"); } catch { }
                return string.Empty;
            }
        }

        private static string UseFallbackOnce(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(fallback))
                return string.Empty;

            var shouldLog = false;
            lock (_warnLock)
            {
                if (!_warnedKeys.Contains(key))
                {
                    _warnedKeys.Add(key);
                    shouldLog = true;
                }
            }

            if (shouldLog)
            {
                try
                {
                    var path = LocateCatalogPath();
                    DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "WARN", $"Using built-in fallback for {key} because PromptCatalog is missing or empty. catalogPath={(path ?? "not found")}");
                }
                catch { }
            }

            return fallback;
        }

        private static JObject BuildFallbackCatalog()
        {
            var systemPrompts = new JObject
            {
                ["default"] = FALLBACK_DEFAULT_SYSTEM_PROMPT,
                ["decompose_system"] = FALLBACK_DECOMPOSE_SYSTEM_PROMPT,
                ["execute_system"] = FALLBACK_EXECUTE_SYSTEM_PROMPT
            };
            var templates = new JObject
            {
                ["decompose_template"] = FALLBACK_DECOMPOSE_TEMPLATE,
                ["execute_template"] = FALLBACK_EXECUTE_TEMPLATE
            };
            return new JObject
            {
                ["systemPrompts"] = systemPrompts,
                ["templates"] = templates
            };
        }

        internal static void StartupSelfCheck()
        {
            try
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(GetSystemPrompt("decompose_system")))
                    missing.Add("systemPrompts.decompose_system");
                if (string.IsNullOrWhiteSpace(GetSystemPrompt("execute_system")))
                    missing.Add("systemPrompts.execute_system");
                if (string.IsNullOrWhiteSpace(GetTemplate("execute_template")))
                    missing.Add("templates.execute_template");

                if (missing.Count > 0)
                {
                    var msg = "PromptCatalog.json missing or invalid; check Copy to Output Directory for Config/PromptCatalog.json. Missing: " + string.Join(", ", missing);
                    AddinStatusLogger.Error("PromptCatalog", msg);
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("PromptCatalog", "Startup self-check failed", ex); } catch { }
            }
        }
    }
}
