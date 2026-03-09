using System;
using System.Text.RegularExpressions;

namespace AICAD.Services
{
    internal static class MaterialIntentParser
    {
        // Ordered from specific to generic to avoid "steel" matching before "stainless steel".
        private static readonly (string Phrase, string Canonical)[] MaterialPhrases = new[]
        {
            ("stainless steel", "stainless steel"),
            ("mild steel", "steel"),
            ("plain carbon steel", "steel"),
            ("carbon steel", "steel"),
            ("alloy steel", "steel"),
            ("aluminium", "aluminum"),
            ("aluminum", "aluminum"),
            ("titanium", "titanium"),
            ("copper", "copper"),
            ("brass", "brass"),
            ("bronze", "bronze"),
            ("nylon", "nylon"),
            ("plastic", "plastic"),
            ("abs", "abs"),
            ("pla", "pla"),
            ("wood", "wood"),
            ("steel", "steel")
        };

        public static bool TryExtractMaterial(string text, out string material)
        {
            material = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var source = text.Trim();
            foreach (var entry in MaterialPhrases)
            {
                if (ContainsWholePhrase(source, entry.Phrase))
                {
                    material = entry.Canonical;
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsWholePhrase(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
                return false;

            var escaped = Regex.Escape(phrase.Trim()).Replace("\\ ", "\\s+");
            var pattern = $@"\b{escaped}\b";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
