using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using AICAD.Services.Logging;

namespace AICAD.Services
{
    internal static class PromptCatalog
    {
        private static readonly object CatalogLock = new object();
        private static Lazy<JObject> _catalog = new Lazy<JObject>(() => LoadCatalog(ResolveCatalogPath()), LazyThreadSafetyMode.ExecutionAndPublication);
        private static string _catalogPathOverride;
        private static string _catalogPathUsed;

        public static string GetSystemPrompt(string key)
        {
            var val = GetString("systemPrompts", key);
            return val ?? string.Empty;
        }

        public static string GetSystemPromptForFeature(string featureKey)
        {
            var node = _catalog.Value;
            var byFeature = node?["systemPromptsByFeature"] as JObject;
            if (byFeature == null) return string.Empty;
            var token = byFeature[featureKey];
            if (token == null) return string.Empty;
            return token.Value<string>() ?? string.Empty;
        }

        public static string GetTemplate(string key)
        {
            var val = GetString("templates", key);
            return val ?? string.Empty;
        }

        private static JObject LoadCatalog(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("PromptCatalog path could not be resolved.");

            var absolutePath = Path.GetFullPath(path);
            _catalogPathUsed = absolutePath;

            try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", $"Prompt source: PromptCatalog.json (disk only)"); } catch { }
            try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", $"Prompt path: {absolutePath}"); } catch { }

            if (!File.Exists(absolutePath))
            {
                var message = $"PromptCatalog.json not found at '{absolutePath}'.";
                TryLogFatal(message, null);
                throw new FileNotFoundException(message, absolutePath);
            }

            string content;
            try
            {
                content = File.ReadAllText(absolutePath);
            }
            catch (Exception ex)
            {
                var message = $"Failed to read PromptCatalog.json at '{absolutePath}': {ex.Message}";
                TryLogFatal(message, ex);
                throw new InvalidOperationException(message, ex);
            }

            JObject parsed;
            try
            {
                parsed = JObject.Parse(content);
            }
            catch (Exception ex)
            {
                var message = $"PromptCatalog.json at '{absolutePath}' contains invalid JSON: {ex.Message}";
                TryLogFatal(message, ex);
                throw new InvalidOperationException(message, ex);
            }

            ValidateCatalog(parsed, absolutePath);
            LogPromptHashes(parsed, absolutePath);
            return parsed;
        }

        private static string ResolveCatalogPath()
        {
            // Prefer the folder where this assembly physically lives (SolidWorks hosts .NET add-ins from its own base dir)
            var asmLocation = typeof(PromptCatalog).Assembly.Location;
            var asmDir = string.IsNullOrWhiteSpace(asmLocation)
                ? null
                : Path.GetDirectoryName(asmLocation);

            // If the caller provided an override (mainly for tests), honor it first.
            if (!string.IsNullOrWhiteSpace(_catalogPathOverride))
                return Path.GetFullPath(_catalogPathOverride);

            // First choice: Config next to the add-in DLL (works when SolidWorks loads us in-process).
            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                var candidate = Path.GetFullPath(Path.Combine(asmDir, "Config", "PromptCatalog.json"));
                if (File.Exists(candidate))
                    return candidate;
            }

            // Fallback: use AppContext.BaseDirectory (e.g., test runners or atypical hosts)
            var baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Environment.CurrentDirectory;
            return Path.GetFullPath(Path.Combine(baseDir, "Config", "PromptCatalog.json"));
        }

        private static void ValidateCatalog(JObject catalog, string path)
        {
            if (catalog == null)
                throw new InvalidOperationException($"PromptCatalog.json at '{path}' is empty.");

            var sys = catalog["systemPrompts"] as JObject;
            if (sys == null)
                throw new InvalidOperationException($"PromptCatalog.json at '{path}' is missing the 'systemPrompts' object.");

            var templates = catalog["templates"] as JObject;
            if (templates == null)
                throw new InvalidOperationException($"PromptCatalog.json at '{path}' is missing the 'templates' object.");

            // Ensure required keys exist and resolve their content (file or literal)
            EnsureNonEmptyResolved(catalog, "systemPrompts", "decompose_system", path);
            EnsureNonEmptyResolved(catalog, "systemPrompts", "execute_system", path);
            EnsureNonEmptyResolved(catalog, "templates", "decompose_template", path);
            EnsureNonEmptyResolved(catalog, "templates", "execute_template", path);

            var decompose = ResolveString(catalog, "systemPrompts", "decompose_system").Trim();
            var execute = ResolveString(catalog, "systemPrompts", "execute_system").Trim();

            var decompLower = decompose.ToLowerInvariant();
            if (decompLower.Contains("\"steps\"") || decompLower.Contains("\"op\"") || decompLower.Contains("thinking"))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'decompose_system' contains executor tokens (\"steps\", \"op\", or 'thinking').");
            }
            if (!decompLower.Contains("\"features\"") || !decompLower.Contains("needs_description") || !decompLower.Contains("\"question\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'decompose_system' must reference 'features', 'needs_description', and 'question'.");
            }

            var execLower = execute.ToLowerInvariant();
            if (!execLower.Contains("\"steps\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must mention \"steps\" in its contract.");
            }
            if (execLower.Contains("\"command\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must use field name 'op' and must not use 'command'.");
            }
            if (!execLower.Contains("\"op\"") && !execLower.Contains(" op\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must require steps to use the 'op' field.");
            }
            if (!execLower.Contains("clarification_needed"))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must describe the clarification flow (clarification_needed).");
            }
            if (!execLower.Contains("\"feature_index\"") || !execLower.Contains("\"feature_type\"") || !execLower.Contains("\"questions\""))
            {
                throw new InvalidOperationException("PromptCatalog validation failed: 'execute_system' must mention feature_index, feature_type, and questions for clarifications.");
            }

            var byFeature = catalog["systemPromptsByFeature"] as JObject;
            if (byFeature != null)
            {
                foreach (var prop in byFeature.Properties())
                {
                    var resolved = ResolveString(catalog, "systemPromptsByFeature", prop.Name);
                    if (string.IsNullOrWhiteSpace(resolved))
                    {
                        throw new InvalidOperationException($"PromptCatalog validation failed: systemPromptsByFeature.{prop.Name} must be non-empty.");
                    }
                }
            }
        }

        private static void RequireNonEmpty(JObject obj, string key, string path)
        {
            var value = obj.Value<string>(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"PromptCatalog.json at '{path}' is missing required key '{obj.Path}.{key}'.");
            }
        }

        private static void LogPromptHashes(JObject catalog, string path)
        {
            try
            {
                var decompose = catalog["systemPrompts"]?["decompose_system"]?.Value<string>() ?? string.Empty;
                var execute = catalog["systemPrompts"]?["execute_system"]?.Value<string>() ?? string.Empty;
                var decompHash = LogRedactor.StableHash(decompose);
                var execHash = LogRedactor.StableHash(execute);
                var decompPreview = Truncate(decompose, 80);
                var execPreview = Truncate(execute, 80);
                DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", $"decompose_system hash={decompHash} preview={decompPreview}");
                DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "INFO", $"execute_system hash={execHash} preview={execPreview}");
            }
            catch { }

            try { AddinStatusLogger.Log("PromptCatalog", $"Loaded prompts from {path}"); } catch { }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static string GetString(string section, string key)
        {
            var node = _catalog.Value;
            return ResolveString(node, section, key);
        }

        private static string ResolveString(JObject node, string section, string key)
        {
            var token = node?[section]?[key];
            if (token == null)
                return string.Empty;
            var raw = token.Value<string>() ?? string.Empty;
            if (IsPathLike(raw))
            {
                // Resolve relative to the repository root (parent of Config folder where PromptCatalog.json lives)
                var catalogDir = Path.GetDirectoryName(_catalogPathUsed) ?? AppContext.BaseDirectory;
                var repoRoot = Path.GetFullPath(Path.Combine(catalogDir, ".."));
                var candidate = raw;
                if (!Path.IsPathRooted(candidate))
                    candidate = Path.GetFullPath(Path.Combine(repoRoot, raw.Replace('/', Path.DirectorySeparatorChar)));

                if (!File.Exists(candidate))
                {
                    var message = $"Prompt file for key '{section}.{key}' not found at '{candidate}'.";
                    TryLogFatal(message, null);
                    throw new FileNotFoundException(message, candidate);
                }

                try
                {
                    return File.ReadAllText(candidate);
                }
                catch (Exception ex)
                {
                    var message = $"Failed to read prompt file for key '{section}.{key}' at '{candidate}': {ex.Message}";
                    TryLogFatal(message, ex);
                    throw new InvalidOperationException(message, ex);
                }
            }
            return raw;
        }

        private static bool IsPathLike(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            var lower = t.ToLowerInvariant();
            if (lower.EndsWith(".txt") || lower.EndsWith(".md")) return true;
            if (lower.StartsWith("prompts/") || lower.StartsWith("prompts\\")) return true;
            if (Path.IsPathRooted(t)) return true;
            return false;
        }

        private static void EnsureNonEmptyResolved(JObject catalog, string section, string key, string path)
        {
            var value = ResolveString(catalog, section, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"PromptCatalog.json at '{path}' is missing required key '{section}.{key}' or the referenced file is empty.");
            }
        }

        internal static void StartupSelfCheck()
        {
            EnsureCatalogLoaded();
        }

        internal static void EnsureCatalogLoaded()
        {
            _ = _catalog.Value; // triggers load and validation; exceptions propagate
        }

        internal static void ResetForTests(string overridePath = null)
        {
            lock (CatalogLock)
            {
                _catalogPathOverride = overridePath;
                _catalog = new Lazy<JObject>(() => LoadCatalog(ResolveCatalogPath()), LazyThreadSafetyMode.ExecutionAndPublication);
            }
        }

        private static void TryLogFatal(string message, Exception ex)
        {
            try { DiagnosticLogWriter.LogLine(null, null, "PromptCatalog", "ERROR", message + (ex == null ? string.Empty : $" ex={ex.Message}")); } catch { }
            try { AddinStatusLogger.Error("PromptCatalog", message, ex); } catch { }
        }
    }
}
