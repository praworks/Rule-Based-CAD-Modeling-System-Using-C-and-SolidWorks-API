using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal static class LlmPlanService
    {
        public class FeaturePlanResult
        {
            public JArray Steps { get; set; }
            public string Thinking { get; set; }
        }
        private static readonly object _clientLock = new object();
        private static LocalHttpLlmClient _localClient;
        private static string _localEndpoint;
        private static string _localModel;
        private static string _localSystemPrompt;
        private static GeminiClient _geminiClient;
        private static string _geminiKey;
        private static string _geminiModel;
        private static GroqLlmClient _groqClient;
        private static string _groqKey;
        private static string _groqModel;
        private static readonly ConcurrentDictionary<string, DateTime> _providerDeadUntil = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _rateLimitLock = new object();
        private static readonly Dictionary<string, DateTime> _lastProviderCall = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static void EnforceProviderPacing(string provider, int minIntervalMs)
        {
            if (minIntervalMs <= 0 || string.IsNullOrWhiteSpace(provider)) return;
            DateTime last;
            lock (_rateLimitLock)
            {
                if (!_lastProviderCall.TryGetValue(provider, out last))
                {
                    _lastProviderCall[provider] = DateTime.UtcNow;
                    return;
                }
            }
            var elapsedMs = (DateTime.UtcNow - last).TotalMilliseconds;
            if (elapsedMs < minIntervalMs)
            {
                var sleepMs = (int)Math.Ceiling(minIntervalMs - elapsedMs);
                if (sleepMs > 0) System.Threading.Thread.Sleep(sleepMs);
            }
            lock (_rateLimitLock) { _lastProviderCall[provider] = DateTime.UtcNow; }
        }

        public static JArray PlanThreadSubtask(JObject threadStep, JObject modelFacts = null)
        {
            try
            {
                var prompt = PromptHandler.BuildThreadSubtaskPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, threadStep, modelFacts);
                AddinStatusLogger.Log("LlmPlanService", "Requesting LLM subtask for thread steps");

                var reply = GenerateWithPriority(prompt);
                if (string.IsNullOrWhiteSpace(reply))
                    return null;

                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    AddinStatusLogger.Log("LlmPlanService", $"Thread subtask returned {extracted.Count} steps");
                    return extracted;
                }

                try
                {
                    var obj = ExtractJsonObject(reply);
                    if (obj != null && obj["steps"] is JArray arr)
                        return arr;
                }
                catch { }

                return null;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LlmPlanService", "PlanThreadSubtask failed", ex);
                return null;
            }
        }

        public static JArray DecomposeByFeature(string userRequest)
        {
            try
            {
                var prompt = PromptHandler.BuildFeatureDecomposePrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, userRequest);
                AddinStatusLogger.Log("LlmPlanService", "Requesting LLM feature decomposition");
                try { AddinStatusLogger.Log("LlmPlanService", "LLM Prompt: " + (prompt ?? string.Empty).Replace("\r\n", "\\n")); } catch { }
                var reply = GenerateWithPriority(prompt);
                if (string.IsNullOrWhiteSpace(reply))
                    return null;
                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    AddinStatusLogger.Log("LlmPlanService", $"Feature decomposition returned {extracted.Count} tasks");
                    return extracted;
                }
                return null;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LlmPlanService", "DecomposeByFeature failed", ex);
                return null;
            }
        }

        public static FeaturePlanResult PlanFeatureSubtask(JObject featureTask, JObject modelFacts = null)
        {
            try
            {
                var prompt = PromptHandler.BuildFeaturePlanPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, featureTask, modelFacts);
                var label = featureTask?.Value<string>("feature_type") ?? "feature";
                AddinStatusLogger.Log("LlmPlanService", $"Requesting LLM plan for feature: {label}");
                int timeoutSeconds = 120;
                try
                {
                    var env = System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.User);
                    if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var secs) && secs > 0)
                        timeoutSeconds = secs;
                }
                catch { }
                try { AddinStatusLogger.Log("LlmPlanService", "LLM Prompt: " + (prompt ?? string.Empty).Replace("\r\n", "\\n")); } catch { }
                var reply = GenerateWithPriority(prompt, timeoutSeconds);
                if (string.IsNullOrWhiteSpace(reply))
                    return null;
                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                    return new FeaturePlanResult { Steps = extracted };
                try
                {
                    var obj = ExtractJsonObject(reply);
                    if (obj != null && obj["steps"] is JArray arr && arr.Count > 0)
                    {
                        return new FeaturePlanResult
                        {
                            Steps = arr,
                            Thinking = obj.Value<string>("thinking")
                        };
                    }
                }
                catch { }
                try
                {
                    var truncated = reply.Length > 800 ? reply.Substring(0, 800) + "..." : reply;
                    AddinStatusLogger.Log("LlmPlanService", $"Feature plan parse failed; reply={truncated}");
                }
                catch { }
                return null;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LlmPlanService", "PlanFeatureSubtask failed", ex);
                return null;
            }
        }

        public static string GenerateWithPriority(string prompt, int timeoutSeconds = 120)
        {
            try
            {
                var priorityStr = LlmPriorityManager.GetPriority();
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    try
                    {
                        EnforceProviderPacing(provider, 2000);
                        if (IsProviderMarkedDead(provider))
                        {
                            AddinStatusLogger.Log("LlmPlanService", $"Skipping provider {provider} - marked dead");
                            continue;
                        }
                        if (provider == "local")
                        {
                            var localEndpoint = System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.User)
                                                ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                                ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(localEndpoint))
                            {
                                var preferredModel = System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.User)
                                                     ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.Process)
                                                     ?? "local-model";
                                var systemPrompt = System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                                   ?? PromptHandler.DEFAULT_SYSTEM_PROMPT;

                                var localClient = GetLocalClient(localEndpoint, preferredModel, systemPrompt);
                                if (localClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => localClient.GenerateAsync(promptText), "local", timeoutSeconds);
                                    lastReply = reply;
                                    AddinStatusLogger.Log("LlmPlanService", "Local LLM reply length=" + (reply?.Length ?? 0));
                                    return reply;
                                }
                            }
                        }
                        else if (provider == "gemini")
                        {
                            var gemKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.User)
                                         ?? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.Process)
                                         ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(gemKey))
                            {
                                var gemModel = System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.User)
                                               ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Process)
                                               ?? "gemini-1.5-flash";
                                var gemSystemPrompt = System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                                     ?? System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process)
                                                     ?? PromptHandler.DEFAULT_SYSTEM_PROMPT;
                                var gemClient = GetGeminiClient(gemKey, gemModel, gemSystemPrompt);
                                if (gemClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => gemClient.GenerateAsync(promptText), "gemini", timeoutSeconds);
                                    lastReply = reply;
                                    AddinStatusLogger.Log("LlmPlanService", "Gemini reply length=" + (reply?.Length ?? 0));
                                    return reply;
                                }
                            }
                        }
                        else if (provider == "groq")
                        {
                            var groqKey = System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.User)
                                          ?? System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.Process)
                                          ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(groqKey))
                            {
                                var groqModel = System.Environment.GetEnvironmentVariable("GROQ_MODEL", System.EnvironmentVariableTarget.User)
                                                ?? System.Environment.GetEnvironmentVariable("GROQ_MODEL", System.EnvironmentVariableTarget.Process)
                                                ?? "llama-3.3-70b-versatile";
                                var groqSystemPrompt = System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                                      ?? System.Environment.GetEnvironmentVariable("AICAD_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process)
                                                      ?? PromptHandler.DEFAULT_SYSTEM_PROMPT;
                                var groqClient = GetGroqClient(groqKey, groqModel, groqSystemPrompt);
                                if (groqClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => groqClient.GenerateAsync(promptText), "groq", timeoutSeconds);
                                    lastReply = reply;
                                    AddinStatusLogger.Log("LlmPlanService", "Groq reply length=" + (reply?.Length ?? 0));
                                    return reply;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is TimeoutException || IsConnectionRefused(ex))
                        {
                            try { AddinStatusLogger.Log("LlmPlanService", $"{provider} transient failure: {ex.Message}. Marking dead and continuing"); } catch { }
                            try { MarkProviderDead(provider); } catch { }
                            continue;
                        }

                        lastEx = ex;
                        AddinStatusLogger.Log("LlmPlanService", provider + " failed: " + ex.Message);
                    }
                }

                if (lastEx != null)
                    throw lastEx;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LlmPlanService", "GenerateWithPriority failed", ex);
            }
            return null;
        }

        private static JArray ExtractJsonArray(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return null;
            try
            {
                var first = txt.IndexOf('[');
                if (first < 0) return null;
                var last = txt.LastIndexOf(']');
                if (last <= first) return null;
                var json = txt.Substring(first, last - first + 1);
                return JArray.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static JObject ExtractJsonObject(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return null;
            try
            {
                var first = txt.IndexOf('{');
                if (first < 0) return null;
                var last = txt.LastIndexOf('}');
                if (last <= first) return null;
                var json = txt.Substring(first, last - first + 1);
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static LocalHttpLlmClient GetLocalClient(string endpoint, string model, string systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return null;
            lock (_clientLock)
            {
                var same = _localClient != null
                           && string.Equals(_localEndpoint, endpoint, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(_localModel, model, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(_localSystemPrompt, systemPrompt, StringComparison.Ordinal);
                if (!same)
                {
                    try { (_localClient as IDisposable)?.Dispose(); } catch { }
                    _localClient = new LocalHttpLlmClient(endpoint, model, systemPrompt);
                    _localEndpoint = endpoint; _localModel = model; _localSystemPrompt = systemPrompt;
                }
                return _localClient;
            }
        }

        private static GeminiClient GetGeminiClient(string key, string model, string systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (_clientLock)
            {
                var same = _geminiClient != null
                           && string.Equals(_geminiKey, key, StringComparison.Ordinal)
                           && string.Equals(_geminiModel, model, StringComparison.OrdinalIgnoreCase);
                if (!same)
                {
                    try { (_geminiClient as IDisposable)?.Dispose(); } catch { }
                    _geminiClient = new GeminiClient(key, model, systemPrompt);
                    _geminiKey = key; _geminiModel = model;
                }
                return _geminiClient;
            }
        }

        private static GroqLlmClient GetGroqClient(string key, string model, string systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (_clientLock)
            {
                var same = _groqClient != null
                           && string.Equals(_groqKey, key, StringComparison.Ordinal)
                           && string.Equals(_groqModel, model, StringComparison.OrdinalIgnoreCase);
                if (!same)
                {
                    try { (_groqClient as IDisposable)?.Dispose(); } catch { }
                    _groqClient = new GroqLlmClient(key, model, systemPrompt);
                    _groqKey = key; _groqModel = model;
                }
                return _groqClient;
            }
        }

        private static bool IsProviderMarkedDead(string provider)
        {
            try
            {
                if (_providerDeadUntil.TryGetValue(provider ?? string.Empty, out var until))
                {
                    if (DateTime.UtcNow < until) return true;
                    _providerDeadUntil.TryRemove(provider, out _);
                }
            }
            catch { }
            return false;
        }

        private static void MarkProviderDead(string provider)
        {
            try
            {
                var env = System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.User)
                          ?? System.Environment.GetEnvironmentVariable("AICAD_PROVIDER_DEAD_COOLDOWN_SECONDS", System.EnvironmentVariableTarget.Process)
                          ?? "300";
                if (!int.TryParse(env, out var secs)) secs = 300;
                var until = DateTime.UtcNow.AddSeconds(secs);
                _providerDeadUntil[provider] = until;
                AddinStatusLogger.Log("LlmPlanService", $"Provider {provider} marked unreachable until {until:u}");
            }
            catch { }
        }

        private static bool IsConnectionRefused(Exception ex)
        {
            if (ex == null) return false;
            Exception cur = ex;
            while (cur != null)
            {
                if (cur is System.Net.Sockets.SocketException) return true;
                if (cur is System.Net.Http.HttpRequestException && cur.InnerException is System.Net.Sockets.SocketException) return true;
                var msg = cur.Message ?? string.Empty;
                if (msg.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("no connection could be made", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                cur = cur.InnerException;
            }
            return false;
        }

        private static string AwaitWithTimeout(Func<Task<string>> taskFactory, string provider, int seconds = 120)
        {
            var task = taskFactory();
            var timeoutMs = seconds * 1000;
            bool completed = Task.WaitAll(new[] { task }, timeoutMs);
            if (!completed)
                throw new TimeoutException($"LLM {provider} timed out after {seconds}s");
            return task.Result;
        }
    }
}
