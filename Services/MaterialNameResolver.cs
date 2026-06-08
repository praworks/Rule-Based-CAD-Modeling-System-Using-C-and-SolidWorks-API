using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AICAD.Services
{
    internal static class MaterialNameResolver
    {
        private const string DefaultDatabaseName = "solidworks materials.sldmat";
        private const string DefaultDatabasePath = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang\english\sldmaterials\solidworks materials.sldmat";
        private static readonly object MaterialCacheLock = new object();
        private static IReadOnlyList<string> _installedMaterialNames;
        private static string _preferredDatabaseName;

        private sealed class MaterialAlias
        {
            public MaterialAlias(string alias, string solidWorksName)
            {
                Alias = alias;
                SolidWorksName = solidWorksName;
            }

            public string Alias { get; }
            public string SolidWorksName { get; }
        }

        // Ordered from specific to generic so alloy- and grade-specific phrases win.
        private static readonly MaterialAlias[] Aliases = new[]
        {
            new MaterialAlias("cast alloy steel", "Cast Alloy Steel"),
            new MaterialAlias("plain carbon steel", "Plain Carbon Steel"),
            new MaterialAlias("steel mild", "Plain Carbon Steel"),
            new MaterialAlias("mild steel", "Plain Carbon Steel"),
            new MaterialAlias("carbon steel", "Plain Carbon Steel"),
            new MaterialAlias("alloy steel", "Alloy Steel"),

            new MaterialAlias("304 stainless steel", "AISI 304"),
            new MaterialAlias("stainless steel 304", "AISI 304"),
            new MaterialAlias("304 stainless", "AISI 304"),
            new MaterialAlias("316 stainless steel", "AISI 316 Stainless Steel Sheet (SS)"),
            new MaterialAlias("stainless steel 316", "AISI 316 Stainless Steel Sheet (SS)"),
            new MaterialAlias("316 stainless", "AISI 316 Stainless Steel Sheet (SS)"),
            new MaterialAlias("316l stainless steel", "AISI Type 316L stainless steel"),
            new MaterialAlias("316l stainless", "AISI Type 316L stainless steel"),
            new MaterialAlias("stainless steel", "AISI 304"),
            new MaterialAlias("stainless", "AISI 304"),

            new MaterialAlias("aluminum 1060 alloy", "1060 Alloy"),
            new MaterialAlias("aluminium 1060 alloy", "1060 Alloy"),
            new MaterialAlias("aluminum 1060", "1060 Alloy"),
            new MaterialAlias("aluminium 1060", "1060 Alloy"),
            new MaterialAlias("aluminum 2014 alloy", "2014 Alloy"),
            new MaterialAlias("aluminium 2014 alloy", "2014 Alloy"),
            new MaterialAlias("aluminum 2014", "2014 Alloy"),
            new MaterialAlias("aluminium 2014", "2014 Alloy"),
            new MaterialAlias("aluminum 5052 h32", "5052-H32"),
            new MaterialAlias("aluminium 5052 h32", "5052-H32"),
            new MaterialAlias("aluminum 5052 h34", "5052-H34"),
            new MaterialAlias("aluminium 5052 h34", "5052-H34"),
            new MaterialAlias("aluminum 5052 o", "5052-O"),
            new MaterialAlias("aluminium 5052 o", "5052-O"),
            new MaterialAlias("aluminum 5052 alloy", "5052-H32"),
            new MaterialAlias("aluminium 5052 alloy", "5052-H32"),
            new MaterialAlias("aluminum 5052", "5052-H32"),
            new MaterialAlias("aluminium 5052", "5052-H32"),
            new MaterialAlias("aluminum 6061 alloy", "6061 Alloy"),
            new MaterialAlias("aluminium 6061 alloy", "6061 Alloy"),
            new MaterialAlias("aluminum 6061", "6061 Alloy"),
            new MaterialAlias("aluminium 6061", "6061 Alloy"),
            new MaterialAlias("aluminum 7075 t6", "7075-T6, Plate (SS)"),
            new MaterialAlias("aluminium 7075 t6", "7075-T6, Plate (SS)"),
            new MaterialAlias("aluminum 7075 alloy", "7075-T6, Plate (SS)"),
            new MaterialAlias("aluminium 7075 alloy", "7075-T6, Plate (SS)"),
            new MaterialAlias("aluminum 7075", "7075-T6, Plate (SS)"),
            new MaterialAlias("aluminium 7075", "7075-T6, Plate (SS)"),
            new MaterialAlias("aluminum", "1060 Alloy"),
            new MaterialAlias("aluminium", "1060 Alloy"),

            new MaterialAlias("titanium grade 2", "Titanium"),
            new MaterialAlias("titanium", "Titanium"),
            new MaterialAlias("abs plastic", "ABS"),
            new MaterialAlias("abs pc", "ABS PC"),
            new MaterialAlias("pvc rigid", "PVC Rigid"),
            new MaterialAlias("nylon 6 10", "Nylon 6/10"),
            new MaterialAlias("nylon 6/10", "Nylon 6/10"),
            new MaterialAlias("nylon", "Nylon 6/10"),
            new MaterialAlias("plastic", "ABS"),
            new MaterialAlias("abs", "ABS"),
            new MaterialAlias("brass", "Brass"),
            new MaterialAlias("bronze", "Bronze"),
            new MaterialAlias("copper", "Copper"),
            new MaterialAlias("steel", "Plain Carbon Steel")
        };

        private static readonly string[] FallbackMaterialNames = new[]
        {
            "Plain Carbon Steel",
            "Alloy Steel",
            "Cast Alloy Steel",
            "AISI 304",
            "AISI 316 Stainless Steel Sheet (SS)",
            "1060 Alloy",
            "2014 Alloy",
            "5052-H32",
            "6061 Alloy",
            "7075-T6, Plate (SS)",
            "Brass",
            "Copper",
            "Bronze",
            "Titanium",
            "ABS",
            "ABS PC",
            "PVC Rigid",
            "Nylon 6/10"
        };

        public static string ResolveForSolidWorks(string material)
        {
            if (string.IsNullOrWhiteSpace(material))
                return material ?? string.Empty;

            var trimmed = material.Trim();
            var installed = FindInstalledMaterial(trimmed);
            if (!string.IsNullOrWhiteSpace(installed))
                return installed;

            var alias = FindAlias(trimmed);
            if (alias != null)
            {
                var resolvedAlias = FindInstalledMaterial(alias.SolidWorksName);
                return resolvedAlias ?? alias.SolidWorksName;
            }

            return trimmed;
        }

        public static bool TryExtractFromText(string text, out string material)
        {
            material = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var source = text.Trim();
            foreach (var installed in GetInstalledMaterialNames().OrderByDescending(n => n.Length))
            {
                if (ContainsWholePhrase(source, installed))
                {
                    material = installed;
                    return true;
                }
            }

            foreach (var alias in Aliases)
            {
                if (ContainsWholePhrase(source, alias.Alias))
                {
                    material = ResolveForSolidWorks(alias.SolidWorksName);
                    return true;
                }
            }

            return false;
        }

        public static IReadOnlyList<string> GetInstalledMaterialNames()
        {
            EnsureMaterialCache();
            return _installedMaterialNames;
        }

        public static string GetPreferredMaterialDatabaseName()
        {
            EnsureMaterialCache();
            return _preferredDatabaseName ?? DefaultDatabaseName;
        }

        public static bool AreEquivalent(string left, string right)
        {
            var leftValue = left?.Trim() ?? string.Empty;
            var rightValue = right?.Trim() ?? string.Empty;

            if (leftValue.Length == 0 || rightValue.Length == 0)
                return string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);

            return string.Equals(
                NormalizeComparisonKey(leftValue),
                NormalizeComparisonKey(rightValue),
                StringComparison.Ordinal);
        }

        private static MaterialAlias FindAlias(string material)
        {
            var key = NormalizeLoose(material);
            foreach (var alias in Aliases)
            {
                if (NormalizeLoose(alias.Alias) == key)
                    return alias;
            }

            return null;
        }

        private static string FindInstalledMaterial(string material)
        {
            EnsureMaterialCache();

            var key = NormalizeLoose(material);
            foreach (var installed in _installedMaterialNames)
            {
                if (NormalizeLoose(installed) == key)
                    return installed;
            }

            return null;
        }

        private static string NormalizeComparisonKey(string material)
        {
            return NormalizeLoose(ResolveForSolidWorks(material));
        }

        private static string NormalizeLoose(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = Regex.Replace(text.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ");
            return Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        private static bool ContainsWholePhrase(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
                return false;

            var escaped = Regex.Escape(phrase.Trim()).Replace("\\ ", "\\s+");
            var pattern = $@"\b{escaped}\b";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void EnsureMaterialCache()
        {
            if (_installedMaterialNames != null)
                return;

            lock (MaterialCacheLock)
            {
                if (_installedMaterialNames != null)
                    return;

                try
                {
                    var path = FindPreferredMaterialDatabasePath();
                    var materials = LoadMaterialNamesFromDatabase(path);
                    if (materials.Count > 0)
                    {
                        _installedMaterialNames = materials;
                        _preferredDatabaseName = Path.GetFileName(path);
                        return;
                    }
                }
                catch { }

                _installedMaterialNames = FallbackMaterialNames;
                _preferredDatabaseName = DefaultDatabaseName;
            }
        }

        private static string FindPreferredMaterialDatabasePath()
        {
            if (File.Exists(DefaultDatabasePath))
                return DefaultDatabasePath;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                var candidate = Path.Combine(programFiles, "SOLIDWORKS Corp", "SOLIDWORKS", "lang", "english", "sldmaterials", DefaultDatabaseName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return DefaultDatabasePath;
        }

        private static IReadOnlyList<string> LoadMaterialNamesFromDatabase(string path)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return results;

            var doc = XDocument.Load(path);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "material"))
            {
                var name = element.Attribute("name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (seen.Add(name))
                    results.Add(name);
            }

            return results;
        }
    }
}
