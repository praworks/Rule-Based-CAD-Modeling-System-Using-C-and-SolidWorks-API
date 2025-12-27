using System;

namespace AICAD.UI
{
    // Minimal static helper to provide status/progress formatting and prefixes
    // referenced by the taskpane and status window code.
    public static class StatusConsole
    {
        public const string StatusPrefix = "[STATUS]";
        public const string ProgressPrefix = "[PROGRESS]";
        public const string LlmProgressPrefix = "[LLM-PROGRESS]";
        public const string ErrorPrefix = "[ERROR]";

        public static string FormatProgress(string bar, int pct)
        {
            return $"{ProgressPrefix} {bar} {pct}%";
        }

        public static string FormatProgressDone(string bar, int pct)
        {
            return $"{ProgressPrefix} {bar} {pct}% Done";
        }

        public static string FormatLlmProgress(string bar, int pct)
        {
            return $"{LlmProgressPrefix} {bar} {pct}%";
        }

        public static string FormatLlmProgressDone(string bar, int pct)
        {
            return $"{LlmProgressPrefix} {bar} {pct}% Done";
        }
    }
}
