using System;

namespace AICAD.Services
{
    internal static class MaterialIntentParser
    {
        public static bool TryExtractMaterial(string text, out string material)
        {
            return MaterialNameResolver.TryExtractFromText(text, out material);
        }
    }
}
