using System;
using System.Linq;
using System.Threading.Tasks;
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
        public class ClassifyResult
        {
            public string Category { get; set; }
            public string Description { get; set; }
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

        public static JArray PlanThreadSubtask(JObject threadStep, JObject modelFacts = null, string runId = null, string requestId = null)
        {
            try
            {
                var prompt = PromptHandler.BuildThreadSubtaskPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, threadStep, modelFacts);
                AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} Requesting LLM subtask for thread steps");

                var reply = GenerateWithPriority(prompt, 120, runId, requestId);
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

        public static JArray DecomposeByFeature(string userRequest, string runId = null, string requestId = null)
        {
            try
            {
                var prompt = PromptHandler.BuildFeatureDecomposePrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, userRequest);
                AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} Requesting LLM feature decomposition");
                try { AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} LLM Prompt: " + (prompt ?? string.Empty).Replace("\r\n", "\\n")); } catch { }
                var reply = GenerateWithPriority(prompt, 120, runId, requestId);
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

        public static FeaturePlanResult PlanFeatureSubtask(JObject featureTask, JObject modelFacts = null, string fewShot = null, string runId = null, string requestId = null)
        {
            try
            {
                var prompt = PromptHandler.BuildFeaturePlanPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, featureTask, modelFacts, fewShot);
                var label = featureTask?.Value<string>("feature_type") ?? "feature";
                AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} Requesting LLM plan for feature: {label}");
                int timeoutSeconds = 120;
                try
                {
                    var env = System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.User);
                    if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var secs) && secs > 0)
                        timeoutSeconds = secs;
                }
                catch { }
                try { AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} LLM Prompt: " + (prompt ?? string.Empty).Replace("\r\n", "\\n")); } catch { }
                var reply = GenerateWithPriority(prompt, timeoutSeconds, runId, requestId);
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

        public static ClassifyResult ClassifyAndDescribe(string userPrompt, IReadOnlyCollection<string> categories, string runId = null, string requestId = null, int timeoutSeconds = 25)
        {
            if (string.IsNullOrWhiteSpace(userPrompt) || categories == null || categories.Count == 0)
                return new ClassifyResult { Category = "Unknown", Description = string.Empty };

            try
            {
                var prompt = PromptHandler.BuildClassificationAndDescriptionPrompt(userPrompt, categories);
                AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} Requesting LLM classification");
                try { AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} LLM Prompt: " + (prompt ?? string.Empty).Replace("\r\n", "\\n")); } catch { }
                var response = GenerateWithPriority(prompt, timeoutSeconds, runId, requestId);
                if (string.IsNullOrWhiteSpace(response))
                    return new ClassifyResult { Category = "Unknown", Description = string.Empty };

                try
                {
                    var json = ExtractRawJson(response);
                    var obj = JObject.Parse(json);
                    var cat = obj["category"]?.ToString();
                    var desc = obj["description"]?.ToString();
                    cat = PromptHandler.NormalizeCategory(cat, categories);
                    return new ClassifyResult { Category = cat, Description = desc ?? string.Empty };
                }
                catch
                {
                    var cat = PromptHandler.NormalizeCategory(response, categories);
                    return new ClassifyResult { Category = cat, Description = string.Empty };
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("LlmPlanService", "ClassifyAndDescribe failed", ex);
                return new ClassifyResult { Category = "Unknown", Description = string.Empty };
            }
        }

        public static string GenerateWithPriority(string prompt, int timeoutSeconds = 120, string runId = null, string requestId = null)
        {
            try
            {
                var priority = ProviderRouter.GetFallbackOrder().ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    try
                    {
                        EnforceProviderPacing(provider, 2000);
                        if (ProviderRouter.IsDead(provider))
                        {
                            AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} provider={provider} marked_dead=true skipping");
                            continue;
                        }
                        AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} provider={provider} marked_dead=false attempting");
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
                                    AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} provider=local reply_len=" + (reply?.Length ?? 0));
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
                                    AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} provider=gemini reply_len=" + (reply?.Length ?? 0));
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
                                    AddinStatusLogger.Log("LlmPlanService", $"run={runId} req={requestId} provider=groq reply_len=" + (reply?.Length ?? 0));
                                    return reply;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        var transient = ex is TimeoutException || IsConnectionRefused(ex);
                        try
                        {
                            var tag = transient ? "transient" : "failure";
                            AddinStatusLogger.Log("LlmPlanService", $"{provider} {tag}: {ex.Message}. Marking dead and continuing");
                        }
                        catch { }
                        try { ProviderRouter.MarkDead(provider); } catch { }
                        continue;
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

        private static string ExtractRawJson(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return null;
            try
            {
                var first = txt.IndexOf('{');
                if (first < 0) return null;
                var last = txt.LastIndexOf('}');
                if (last <= first) return null;
                return txt.Substring(first, last - first + 1);
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
