using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal static class PromptCatalog
    {
        private static readonly Lazy<JObject> Catalog = new Lazy<JObject>(LoadCatalog);

        public static string GetSystemPrompt(string key) => GetString("systemPrompts", key);

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

        public static string GetTemplate(string key) => GetString("templates", key);

        private static JObject LoadCatalog()
        {
            var path = LocateCatalogPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    var parsed = JObject.Parse(content);
                    ValidateSystemPrompts(parsed);
                    return parsed;
                }
                catch (Exception ex)
                {
                    AddinStatusLogger.Error("PromptCatalog", $"Failed to load PromptCatalog from '{path}'", ex);
                    throw new InvalidOperationException($"Failed to load PromptCatalog from '{path}': {ex.Message}", ex);
                }
            }

            var embedded = LoadEmbeddedCatalog();
            if (embedded != null)
                return embedded;

            AddinStatusLogger.Log("PromptCatalog", "PromptCatalog.json not found on disk or as embedded resource. Using empty catalog.");
            return new JObject();
        }

        private static JObject LoadEmbeddedCatalog()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("Config.PromptCatalog.json", StringComparison.OrdinalIgnoreCase)
                                            || name.EndsWith("PromptCatalog.json", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(resourceName))
                    return null;

                using (var stream = asm.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream ?? Stream.Null))
                {
                    var content = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        AddinStatusLogger.Log("PromptCatalog", $"Embedded PromptCatalog resource '{resourceName}' was empty.");
                        return null;
                    }
                    var parsed = JObject.Parse(content);
                    ValidateSystemPrompts(parsed);
                    AddinStatusLogger.Log("PromptCatalog", $"Loaded embedded PromptCatalog resource '{resourceName}'.");
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("PromptCatalog", "Failed to load embedded PromptCatalog resource", ex);
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
            var baseDir = AppContext.BaseDirectory;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Config", "PromptCatalog.json");
                if (visited.Contains(candidate))
                    continue;
                visited.Add(candidate);
                if (File.Exists(candidate))
                    return candidate;
            }

            var fallback = Path.Combine(Environment.CurrentDirectory, "Config", "PromptCatalog.json");
            return File.Exists(fallback) ? fallback : null;
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
            catch
            {
                return string.Empty;
            }
        }
    }
}
