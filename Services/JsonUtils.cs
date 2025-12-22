using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace AICAD.Services
{
    internal static class JsonUtils
    {
        // Safely serialize a JToken to compact JSON without depending on specific JToken overloads
        public static string SerializeCompact(object token)
        {
            try
            {
                if (token == null) return string.Empty;
                if (token is string s) return s;
                if (token is JToken jt)
                {
                    // Prefer JsonConvert to avoid calling potentially-missing JToken.ToString(Formatting) overloads
                    return JsonConvert.SerializeObject(jt, Formatting.None);
                }
                return JsonConvert.SerializeObject(token, Formatting.None);
            }
            catch
            {
                try { return token?.ToString() ?? string.Empty; } catch { return string.Empty; }
            }
        }
    }
}
