using System;
using Microsoft.Extensions.Logging;
using AICAD.Services.Logging;

namespace AICAD.Services
{
    internal static class PromptSelectionValidator
    {
        public static void Validate(string stageKey, string resolvedPrompt)
        {
            if (string.IsNullOrWhiteSpace(stageKey) || string.IsNullOrWhiteSpace(resolvedPrompt))
                return;

            var stage = stageKey.ToUpperInvariant();
            switch (stage)
            {
                case "CLASSIFY":
                    AssertStagePrompt(stage, resolvedPrompt, PromptHandler.CLASSIFY_SYSTEM_PROMPT);
                    break;
                case "DECOMPOSE":
                    AssertStagePrompt(stage, resolvedPrompt, PromptHandler.DEFAULT_DECOMPOSE_SYSTEM_PROMPT);
                    break;
                case "EXECUTE":
                    if (!PromptHandler.IsExecuteSystemPrompt(resolvedPrompt))
                        LogWarning(stage, "resolved to a non-execute system prompt.");
                    break;
            }
        }

        private static void AssertStagePrompt(string stage, string actual, string expected)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal))
                return;
            LogWarning(stage, $"resolved to unexpected system prompt; expected the standard stage prompt but got \"{Truncate(actual)}\".");
        }

        private static void LogWarning(string stage, string message)
        {
            try
            {
                var logger = LoggerFactoryBuilder.Factory.CreateLogger("PromptSelectionValidator");
                logger.LogWarning("Prompt validation — stage={Stage}: {Message}", stage, message);
            }
            catch { }
        }

        private static string Truncate(string value, int max = 120)
        {
            if (value == null) return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}
