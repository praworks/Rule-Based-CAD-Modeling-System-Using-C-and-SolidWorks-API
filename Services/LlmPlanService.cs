using System;
using System.Linq;
using System.Diagnostics;
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
                var sw = Stopwatch.StartNew();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "LLM request start: thread_subtask");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Prompt: " + DiagnosticLogWriter.Truncate(prompt, 800));

                var reply = GenerateWithPriority(prompt, 120, runId, requestId);
                sw.Stop();
                if (string.IsNullOrWhiteSpace(reply))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"LLM request end: thread_subtask empty_reply elapsedMs={sw.ElapsedMilliseconds}");
                    return null;
                }

                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"LLM request end: thread_subtask elapsedMs={sw.ElapsedMilliseconds}");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Raw reply: " + DiagnosticLogWriter.Truncate(reply, 800));

                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Thread subtask steps={extracted.Count}");
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
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "PlanThreadSubtask failed: " + ex.Message);
                return null;
            }
        }

        public static JArray DecomposeByFeature(string userRequest, string runId = null, string requestId = null, int timeoutSeconds = 120)
        {
            try
            {
                var prompt = PromptHandler.BuildFeatureDecomposePrompt(PromptHandler.DEFAULT_DECOMPOSE_SYSTEM_PROMPT, userRequest);
                var sw = Stopwatch.StartNew();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "LLM request start: decompose");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Prompt: " + DiagnosticLogWriter.Truncate(prompt, 1200));
                var reply = GenerateWithPriority(prompt, timeoutSeconds, runId, requestId);
                if (string.IsNullOrWhiteSpace(reply))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"LLM request end: decompose empty_reply elapsedMs={sw.ElapsedMilliseconds}");
                    return null;
                }
                sw.Stop();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"LLM request end: decompose elapsedMs={sw.ElapsedMilliseconds}");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Raw reply: " + DiagnosticLogWriter.Truncate(reply, 1200));
                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Parsed tasks count={extracted.Count}");
                    for (int i = 0; i < extracted.Count; i++)
                    {
                        var taskJson = string.Empty;
                        try { taskJson = Newtonsoft.Json.JsonConvert.SerializeObject(extracted[i], Newtonsoft.Json.Formatting.None); } catch { }
                        DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", $"Task[{i}]: " + DiagnosticLogWriter.Truncate(taskJson, 800));
                    }
                    return extracted;
                }
                return null;
            }
            catch (Exception ex)
            {
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "DecomposeByFeature failed: " + ex.Message);
                return null;
            }
        }

        public static FeaturePlanResult PlanFeatureSubtask(JObject featureTask, JObject modelFacts = null, string fewShot = null, string runId = null, string requestId = null, int timeoutSeconds = 120)
        {
            try
            {
                var prompt = PromptHandler.BuildFeaturePlanPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, featureTask, modelFacts, fewShot);
                var label = featureTask?.Value<string>("feature_type") ?? "feature";
                var sw = Stopwatch.StartNew();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"LLM request start: expand feature={label}");
                int effectiveTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 120;
                try
                {
                    var env = System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.User);
                    if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var secs) && secs > 0)
                        effectiveTimeoutSeconds = secs;
                }
                catch { }
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Prompt: " + DiagnosticLogWriter.Truncate(prompt, 1600));
                var reply = GenerateWithPriority(prompt, effectiveTimeoutSeconds, runId, requestId);
                if (string.IsNullOrWhiteSpace(reply))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"LLM request end: expand empty_reply elapsedMs={sw.ElapsedMilliseconds}");
                    return null;
                }
                sw.Stop();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"LLM request end: expand elapsedMs={sw.ElapsedMilliseconds}");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Raw reply: " + DiagnosticLogWriter.Truncate(reply, 1600));
                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Thinking=<none> steps={extracted.Count}");
                    return new FeaturePlanResult { Steps = extracted };
                }
                try
                {
                    var obj = ExtractJsonObject(reply);
                    if (obj != null && obj["steps"] is JArray arr && arr.Count > 0)
                    {
                        var thinking = obj.Value<string>("thinking") ?? string.Empty;
                        DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Thinking={DiagnosticLogWriter.Truncate(thinking, 400)} steps={arr.Count}");
                        return new FeaturePlanResult
                        {
                            Steps = arr,
                            Thinking = thinking
                        };
                    }
                }
                catch { }
                try
                {
                    var truncated = reply.Length > 800 ? reply.Substring(0, 800) + "..." : reply;
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "Feature plan parse failed; reply=" + DiagnosticLogWriter.Truncate(truncated, 800));
                }
                catch { }
                return null;
            }
            catch (Exception ex)
            {
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "PlanFeatureSubtask failed: " + ex.Message);
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
                var sw = Stopwatch.StartNew();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "LLM request start: classify");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Prompt: " + DiagnosticLogWriter.Truncate(prompt, 1200));
                var response = GenerateWithPriority(prompt, timeoutSeconds, runId, requestId);
                if (string.IsNullOrWhiteSpace(response))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"LLM request end: classify empty_reply elapsedMs={sw.ElapsedMilliseconds}");
                    return new ClassifyResult { Category = "Unknown", Description = string.Empty };
                }
                sw.Stop();
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"LLM request end: classify elapsedMs={sw.ElapsedMilliseconds}");
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", "Raw reply: " + DiagnosticLogWriter.Truncate(response, 1200));

                try
                {
                    var json = ExtractRawJson(response);
                    var obj = JObject.Parse(json);
                    var cat = obj["category"]?.ToString();
                    var desc = obj["description"]?.ToString();
                    cat = PromptHandler.NormalizeCategory(cat, categories);
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Parsed category={cat} description={DiagnosticLogWriter.Truncate(desc ?? string.Empty, 400)}");
                    return new ClassifyResult { Category = cat, Description = desc ?? string.Empty };
                }
                catch
                {
                    var cat = PromptHandler.NormalizeCategory(response, categories);
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Parsed category={cat} description=<none>");
                    return new ClassifyResult { Category = cat, Description = string.Empty };
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "ClassifyAndDescribe failed: " + ex.Message);
                return new ClassifyResult { Category = "Unknown", Description = string.Empty };
            }
        }

        public static string GenerateWithPriority(string prompt, int timeoutSeconds = 120, string runId = null, string requestId = null)
        {
            try
            {
                var priority = ProviderRouter.GetFallbackOrder().ToList();

                Exception lastEx = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    try
                    {
                        EnforceProviderPacing(provider, 2000);
                        var markedDead = ProviderRouter.IsDead(provider);
                        DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"provider={provider} marked_dead={markedDead} attempting");
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
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "provider=local reply_len=" + (reply?.Length ?? 0));
                                    if (!string.IsNullOrWhiteSpace(reply))
                                        return reply;
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "WARN", "provider=local empty_reply continuing");
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
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "provider=gemini reply_len=" + (reply?.Length ?? 0));
                                    if (!string.IsNullOrWhiteSpace(reply))
                                        return reply;
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "WARN", "provider=gemini empty_reply continuing");
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
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "provider=groq reply_len=" + (reply?.Length ?? 0));
                                    if (!string.IsNullOrWhiteSpace(reply))
                                        return reply;
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "WARN", "provider=groq empty_reply continuing");
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
                            DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"{provider} {tag}: {ex.Message}. Marking dead and continuing");
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
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "GenerateWithPriority failed: " + ex.Message);
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
