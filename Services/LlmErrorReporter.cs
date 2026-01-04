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

                // Only use the local HTTP LLM client for now; endpoint can be customized via env var
                var endpoint = Environment.GetEnvironmentVariable(EnvEndpoint, EnvironmentVariableTarget.User)
                               ?? Environment.GetEnvironmentVariable(EnvEndpoint)
                               ?? "http://localhost:1234/v1/chat/completions";

                using (var client = new LocalHttpLlmClient(endpoint, "gpt-3.5-mini", "You are a helpful assistant for diagnosing CAD add-in errors."))
                {
                    // Ask the model for a short actionable suggestion
                    var prompt = $"Analyze this error and give 2 concise troubleshooting steps (non-sensitive):\n\n{summary}";
                    var resp = await client.GenerateAsync(prompt).ConfigureAwait(false);
                    // Write the LLM reply to the addin log so humans can review it
                    AddinStatusLogger.Log("LLMErrorReport", resp ?? "(empty response)");
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
