using System;
using System.Collections.Generic;

namespace AICAD.Services
{
    internal static class PromptStageRouter
    {
        private static readonly Dictionary<string, StagePromptKeys> StageMap = new Dictionary<string, StagePromptKeys>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLASSIFY"] = new StagePromptKeys("CLASSIFY", "classify_system", "classify_template"),
            ["DECOMPOSE"] = new StagePromptKeys("DECOMPOSE", "decompose_system", "decompose_template"),
            ["EXECUTE"] = new StagePromptKeys("EXECUTE", "execute_system", "execute_template")
        };

        public static StagePromptKeys GetKeys(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage))
                stage = "EXECUTE";
            var normalized = stage.ToUpperInvariant();
            if (StageMap.TryGetValue(normalized, out var keys))
                return keys;
            throw new InvalidOperationException($"Unsupported LLM stage '{stage}'.");
        }
    }

    internal readonly struct StagePromptKeys
    {
        public StagePromptKeys(string stage, string systemPromptKey, string templateKey)
        {
            Stage = stage;
            SystemPromptKey = systemPromptKey;
            TemplateKey = templateKey;
        }

        public string Stage { get; }
        public string SystemPromptKey { get; }
        public string TemplateKey { get; }
    }
}
