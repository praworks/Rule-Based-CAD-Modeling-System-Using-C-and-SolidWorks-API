using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal static class FewShotSelector
    {
        public static string SelectFeatureFewShot(JObject task, IGoodFeedbackStore goodStore, IStepStore stepStore, int maxCount)
        {
            if (task == null) return null;
            var featureType = task.Value<string>("feature_type") ?? string.Empty;
            var intent = task.Value<string>("intent") ?? string.Empty;
            var seed = (featureType + " " + intent).Trim();
            var sb = new StringBuilder();
            try
            {
                if (goodStore != null)
                {
                    var shots = goodStore.GetRecentFewShots(maxCount);
                    foreach (var s in shots) sb.Append(s);
                }
                if (stepStore != null && !string.IsNullOrWhiteSpace(seed))
                {
                    var shots = stepStore.GetRelevantFewShots(seed, maxCount);
                    foreach (var s in shots) sb.Append(s);
                }
            }
            catch { }
            var txt = sb.ToString();
            return string.IsNullOrWhiteSpace(txt) ? null : txt;
        }
    }
}
