using System;
using System.Threading.Tasks;

namespace AICAD.Services
{
    public static class LlmErrorReporter
    {
        // Environment variable to override local endpoint if needed
        private const string EnvEndpoint = "AICAD_LOCAL_LLM_ENDPOINT";

        public static async Task ReportAsync(string category, string message, Exception ex)
        {
            try
            {
                // Build short, non-sensitive prompt summarizing the error.
                var summary = BuildSummary(category, message, ex);

                // Use the global provider priority (AICAD_LLM_PRIORITY) to generate the report
                var prompt = PromptHandler.BuildErrorAnalysisPrompt(summary);
                try
                {
                    var resp = ClarificationService.GenerateWithPriority(prompt, 30);
                    AddinStatusLogger.Log("LLMErrorReport", resp ?? "(empty response)");
                }
                catch
                {
                    // If generation failed entirely, fall back to local endpoint as a last resort
                    try
                    {
                        var endpoint = Environment.GetEnvironmentVariable(EnvEndpoint, EnvironmentVariableTarget.User)
                                       ?? Environment.GetEnvironmentVariable(EnvEndpoint)
                                       ?? "http://localhost:1234/v1/chat/completions";
                        // Prefer the configured AICAD_SYSTEM_PROMPT (if any) rather than hard-coding a different system prompt here.
                        using (var client = new LocalHttpLlmClient(endpoint, "gpt-3.5-mini"))
                        {
                            var fallbackResp = await client.GenerateAsync(prompt).ConfigureAwait(false);
                            AddinStatusLogger.Log("LLMErrorReport", fallbackResp ?? "(empty response)");
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        AddinStatusLogger.Error("LlmErrorReporter", "Failed to report error to LLM (priority and fallback)", fallbackEx);
                    }
                }
            }
            catch (Exception rptEx)
            {
                try { AddinStatusLogger.Error("LlmErrorReporter", "Failed to report error to LLM", rptEx); } catch { }
            }
        }

        private static string BuildSummary(string category, string message, Exception ex)
        {
            var head = $"Category: {category}\nMessage: {message}\nExceptionType: {ex?.GetType().FullName}\n";
            var msg = ex?.Message ?? "(no message)";
            if (ex?.StackTrace != null)
            {
                // Limit stack trace length to avoid sending long traces
                var st = ex.StackTrace.Length > 800 ? ex.StackTrace.Substring(0, 800) + "..." : ex.StackTrace;
                return head + "ExceptionMessage: " + msg + "\nStackTrace:\n" + st;
            }
            return head + "ExceptionMessage: " + msg;
        }
    }
}
