using System;

namespace AICAD.Services
{
    /// <summary>
    /// Lightweight classifier that decides whether an exception should be sent to the LLM for analysis.
    /// Respects user setting "EnableExceptionClassifier" persisted via SettingsManager.
    /// </summary>
    public static class ExceptionClassifier
    {
        private const string SettingKey = "EnableExceptionClassifier";

        public static bool IsEnabled()
        {
            return SettingsManager.GetBool(SettingKey, false);
        }

        // Decide whether an exception should be sent to LLM. Returns reason for decision.
        public static bool ShouldSend(Exception ex, string category, string message, out string reason)
        {
            reason = "";
            try
            {
                if (!IsEnabled())
                {
                    reason = "Disabled in settings";
                    return false;
                }

                if (ex == null)
                {
                    reason = "No exception object";
                    return false;
                }

                // Known noisy or environmental errors we DO NOT send
                var msg = (ex.Message ?? "").ToLowerInvariant();
                if (msg.Contains("locked by") || msg.Contains("the process cannot access the file") || msg.Contains("msb3027") || msg.Contains("file is locked"))
                {
                    reason = "File-lock / environment error";
                    return false;
                }
                if (msg.Contains("disp_e_badindex") || msg.Contains("bad index"))
                {
                    // Chamfer edge bad-index errors are common; don't auto-send unless explicitly enabled
                    reason = "Known SolidWorks DISP_E_BADINDEX; suppressed";
                    return false;
                }

                // For COM/interop exceptions with HRESULTs we don't recognize, allow sending
                if (ex.HResult != 0)
                {
                    reason = $"Non-zero HRESULT {ex.HResult}";
                    return true;
                }

                // For typical runtime exceptions, send to LLM for guidance
                if (ex is NullReferenceException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    reason = "Runtime exception type worth sending";
                    return true;
                }

                // Default: do not send
                reason = "Default: not relevant";
                return false;
            }
            catch (Exception e)
            {
                try { AddinStatusLogger.Error("ExceptionClassifier", "Classifier failed", e); } catch { }
                reason = "Classifier internal error";
                return false;
            }
        }
    }
}
