using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    public static class PromptHandler
    {
        public const string DefaultTemplatePath = "Config/PromptTemplates.json";
        public static string DEFAULT_SYSTEM_PROMPT => PromptCatalog.GetSystemPrompt("default");
        public static string EXECUTE_SYSTEM_PROMPT => DEFAULT_SYSTEM_PROMPT;
        public static string CLASSIFY_SYSTEM_PROMPT => PromptCatalog.GetSystemPrompt("classify");
        public static string DEFAULT_DECOMPOSE_SYSTEM_PROMPT => PromptCatalog.GetSystemPrompt("decompose");

        public static string BuildRefineSystemPrompt()
        {
            return PromptCatalog.GetSystemPrompt("refineSystem");
        }

        public static string BuildErrorAnalysisPrompt(string summary)
        {
            return FormatTemplate("errorAnalysisPrompt",
                ("summary", summary ?? string.Empty));
        }

        public static string BuildClassificationPrompt(string userPrompt, IEnumerable<string> categories)
        {
            var list = categories == null ? string.Empty : string.Join(", ", categories);
            return FormatTemplate("classificationPrompt",
                ("categories", list),
                ("userPrompt", userPrompt ?? string.Empty));
        }

        public static string BuildClassificationAndDescriptionPrompt(string userPrompt, IEnumerable<string> categories)
        {
            var list = categories == null ? string.Empty : string.Join(", ", categories);
            return FormatTemplate("classificationWithDescriptionPrompt",
                ("categories", list),
                ("userPrompt", userPrompt ?? string.Empty));
        }

        public static string BuildClarificationLocalSystemPrompt()
        {
            return PromptCatalog.GetSystemPrompt("clarificationLocal");
        }

        public static string BuildTaskpaneLocalSystemPrompt()
        {
            return PromptCatalog.GetSystemPrompt("taskpaneLocal");
        }

        public static string NormalizeCategory(string category, IEnumerable<string> categories)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Unknown";
            var trimmed = category.Trim();
            if (categories != null)
            {
                foreach (var c in categories)
                {
                    if (string.Equals(trimmed, c, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            return "Unknown";
        }

        public static PromptTemplateConfig LoadTemplateConfig()
        {
            var path = Environment.GetEnvironmentVariable("AICAD_PROMPT_TEMPLATES")
                       ?? DefaultTemplatePath;

            if (!File.Exists(path))
                return PromptTemplateConfig.Empty;

            try
            {
                var json = File.ReadAllText(path);
                var root = JObject.Parse(json);
                var cfg = new PromptTemplateConfig();
                cfg.CommonPreamble = root.Value<string>("common_preamble") ?? string.Empty;
                cfg.UnknownTemplate = root.Value<string>("unknown_template") ?? string.Empty;
                var arr = root["categories"] as JArray;
                if (arr != null)
                {
                    foreach (var item in arr)
                    {
                        var name = item?["name"]?.ToString();
                        var template = item?["template"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(template))
                            cfg.Categories[name] = template;
                    }
                }
                return cfg;
            }
            catch
            {
                return PromptTemplateConfig.Empty;
            }
        }

        public static string BuildTemplatePrompt(string template, string userText, string commonPreamble)
        {
            if (string.IsNullOrWhiteSpace(template))
                return userText ?? string.Empty;
            return template.Replace("{user_request}", userText ?? string.Empty)
                           .Replace("{common_preamble}", commonPreamble ?? string.Empty);
        }

        private static string BuildFactsSection(JObject facts)
        {
            if (facts == null)
                return string.Empty;
            return $"CURRENT MODEL STATE:\n{facts}\n\n";
        }

        private static string FormatTemplate(string key, params (string Key, string Value)[] tokens)
        {
            var template = PromptCatalog.GetTemplate(key);
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;
            foreach (var token in tokens)
            {
                template = template.Replace("{" + token.Key + "}", token.Value ?? string.Empty);
            }
            return template;
        }

        public static string BuildMissingPrompt(string systemPrompt, JArray missing)
        {
            var systemBlock = (systemPrompt ?? string.Empty) + "\n\n";
            var missingJson = missing == null ? "[]" : missing.ToString();
            return FormatTemplate("missingPrompt",
                ("systemPrompt", systemBlock),
                ("missingJson", missingJson));
        }

        public static string BuildSingleStepPrompt(string systemPrompt, JObject step, object handlerData)
        {
            var systemBlock = (systemPrompt ?? string.Empty) + "\n\n";
            var stepJson = step == null ? "{}" : step.ToString();
            var handlerDataSection = string.Empty;
            if (handlerData != null)
            {
                var builder = new StringBuilder();
                builder.AppendLine("Handler data:");
                try { builder.AppendLine(JToken.FromObject(handlerData).ToString()); }
                catch { builder.AppendLine(handlerData.ToString()); }
                handlerDataSection = builder.ToString();
            }
            return FormatTemplate("singleStepPrompt",
                ("systemPrompt", systemBlock),
                ("stepJson", stepJson),
                ("handlerDataSection", handlerDataSection));
        }

        public static string BuildDescriptionPrompt(string userPrompt)
        {
            return FormatTemplate("descriptionPrompt",
                ("userPrompt", userPrompt ?? string.Empty));
        }

        public static string BuildRefinePrompt(string systemPrompt, string rawPrompt)
        {
            var trimmed = systemPrompt?.Trim();
            var systemBlock = string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed + "\n\n";
            return FormatTemplate("refinePrompt",
                ("systemPrompt", systemBlock),
                ("rawPrompt", rawPrompt ?? string.Empty));
        }

        public static string BuildFinalPlanPrompt(string userText, string fewShotExamples, bool forceLocalOnly)
        {
            var text = userText ?? string.Empty;
            var examples = fewShotExamples ?? string.Empty;
            var prefix = string.IsNullOrWhiteSpace(examples) ? string.Empty : examples + "\n\n";
            var body = prefix + "User request: " + text;
            var key = forceLocalOnly ? "finalPlanPromptForceLocal" : "finalPlanPromptDefault";
            return FormatTemplate(key, ("body", body));
        }

        public static string BuildIntentPrompt(string systemPrompt, string intent, JObject facts)
        {
            var systemBlock = (systemPrompt ?? string.Empty) + "\n\n";
            var factsSection = BuildFactsSection(facts);
            return FormatTemplate("intentPrompt",
                ("systemPrompt", systemBlock),
                ("factsSection", factsSection),
                ("intent", intent ?? string.Empty));
        }

        public static string BuildIntentPromptWithCoT(string systemPrompt, string intent, JObject facts)
        {
            var systemBlock = (systemPrompt ?? string.Empty) + "\n\n";
            var factsSection = BuildFactsSection(facts);
            return FormatTemplate("intentPromptWithCoT",
                ("systemPrompt", systemBlock),
                ("factsSection", factsSection),
                ("intent", intent ?? string.Empty));
        }

        public static string BuildThreadSubtaskPrompt(string systemPrompt, JObject threadStep, JObject facts)
        {
            var systemBlock = (systemPrompt ?? string.Empty) + "\n\n";
            var factsSection = BuildFactsSection(facts);
            var threadJson = threadStep == null ? "{}" : threadStep.ToString();
            return FormatTemplate("threadSubtaskPrompt",
                ("systemPrompt", systemBlock),
                ("factsSection", factsSection),
                ("threadStep", threadJson));
        }

        public static string BuildThreadSubtaskPromptFromRequest(string systemPrompt, string userRequest, JObject facts)
        {
            var systemBlock = (systemPrompt ?? string.Empty) + "\n\n";
            var factsSection = BuildFactsSection(facts);
            return FormatTemplate("threadSubtaskPromptFromRequest",
                ("systemPrompt", systemBlock),
                ("factsSection", factsSection),
                ("userRequest", userRequest ?? string.Empty));
        }

        public static string BuildThreadbarShaftPrompt(string userRequest, string commonPreamble)
        {
            return FormatTemplate("threadbarShaftPrompt",
                ("commonPreamble", commonPreamble ?? string.Empty),
                ("userRequest", userRequest ?? string.Empty));
        }

        public static string BuildFeatureDecomposePrompt(string systemPrompt, string userRequest)
        {
            var activePrompt = string.IsNullOrWhiteSpace(systemPrompt) ? DEFAULT_DECOMPOSE_SYSTEM_PROMPT : systemPrompt;
            var systemBlock = activePrompt + "\n\n";
            return FormatTemplate("featureDecomposePrompt",
                ("systemPrompt", systemBlock),
                ("userRequest", userRequest ?? string.Empty));
        }

        public static string BuildFeaturePlanPrompt(string systemPrompt, JObject featureTask, JObject facts)
        {
            var factsSection = BuildFactsSection(facts);
            var taskJson = featureTask == null ? "{}" : featureTask.ToString();
            return FormatTemplate("featurePlanPrompt",
                ("factsSection", factsSection),
                ("featureTask", taskJson));
        }
    }

    public sealed class PromptTemplateConfig
    {
        public static readonly PromptTemplateConfig Empty = new PromptTemplateConfig();
        public Dictionary<string, string> Categories { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string CommonPreamble { get; set; } = string.Empty;
        public string UnknownTemplate { get; set; } = string.Empty;
    }
}
