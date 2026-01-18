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

            var strict = IsStrictModeEnabled();
            var stage = stageKey.ToUpperInvariant();
            switch (stage)
            {
                case "CLASSIFY":
                    AssertStagePrompt(stage, resolvedPrompt, PromptHandler.CLASSIFY_SYSTEM_PROMPT, strict);
                    break;
                case "DECOMPOSE":
                    AssertStagePrompt(stage, resolvedPrompt, PromptHandler.DEFAULT_DECOMPOSE_SYSTEM_PROMPT, strict);
                    break;
                case "EXECUTE":
                    if (!PromptHandler.IsExecuteSystemPrompt(resolvedPrompt))
                    {
                        var msg = "resolved to a non-execute system prompt.";
                        if (strict) throw new InvalidOperationException($"Prompt validation failed — stage={stage}: {msg}");
                        LogWarning(stage, msg);
                    }
                    break;
            }
        }

        private static void AssertStagePrompt(string stage, string actual, string expected, bool strict)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal))
                return;
            var msg = $"resolved to unexpected system prompt; expected the standard stage prompt but got \"{Truncate(actual)}\".";
            if (strict)
                throw new InvalidOperationException($"Prompt validation failed — stage={stage}: {msg}");
            LogWarning(stage, msg);
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

        private static bool IsStrictModeEnabled()
        {
            try
            {
                var env = System.Environment.GetEnvironmentVariable("AICAD_PROMPT_STRICT", EnvironmentVariableTarget.Process)
                          ?? System.Environment.GetEnvironmentVariable("AICAD_PROMPT_STRICT", EnvironmentVariableTarget.User)
                          ?? System.Environment.GetEnvironmentVariable("AICAD_PROMPT_STRICT", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrWhiteSpace(env)) return false;
                return string.Equals(env.Trim(), "1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
