using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal static class PromptCatalog
    {
        private static readonly Lazy<JObject> Catalog = new Lazy<JObject>(LoadCatalog);

        public static string GetSystemPrompt(string key) => GetString("systemPrompts", key);

        public static string GetTemplate(string key) => GetString("templates", key);

        private static JObject LoadCatalog()
        {
            var path = LocateCatalogPath();
            if (string.IsNullOrWhiteSpace(path))
                return new JObject();

            try
            {
                var content = File.ReadAllText(path);
                return JObject.Parse(content);
            }
            catch
            {
                return new JObject();
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
