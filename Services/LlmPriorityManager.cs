using System;

namespace AICAD.Services
{
    public static class LlmPriorityManager
    {
        private const string EnvName = "AICAD_LLM_PRIORITY";

        public static string GetPriority()
        {
            try
            {
                var v = Environment.GetEnvironmentVariable(EnvName, EnvironmentVariableTarget.Machine)
                        ?? Environment.GetEnvironmentVariable(EnvName, EnvironmentVariableTarget.User)
                        ?? Environment.GetEnvironmentVariable(EnvName, EnvironmentVariableTarget.Process);
                if (string.IsNullOrWhiteSpace(v)) v = "local,gemini,groq";
                return v;
            }
            catch
            {
                return Environment.GetEnvironmentVariable(EnvName, EnvironmentVariableTarget.Process) ?? "local,gemini,groq";
            }
        }

        /// <summary>
        /// Attempts to persist the priority globally (Machine). If that fails (no permissions), falls back to User.
        /// Returns true if Machine was updated, false if only User (or failure).
        /// </summary>
        public static bool SetPriority(string priority)
        {
            try
            {
                Environment.SetEnvironmentVariable(EnvName, priority, EnvironmentVariableTarget.Machine);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    AddinLogger.Error(nameof(LlmPriorityManager), "Failed to write Machine-level LLM priority, falling back to User-level", ex);
                }
                catch { }
                try
                {
                    Environment.SetEnvironmentVariable(EnvName, priority, EnvironmentVariableTarget.User);
                }
                catch (Exception ex2)
                {
                    try { AddinLogger.Error(nameof(LlmPriorityManager), "Failed to write User-level LLM priority", ex2); } catch { }
                    return false;
                }
                return false;
            }
        }
    }
}
