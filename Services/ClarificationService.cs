using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Small helper that asks the LLM for clarifications when a plan step is missing required fields
    /// or when a handler reports that it made no changes. Uses existing GeminiClient and logs the exchange.
    /// </summary>
    public static class ClarificationService
    {
        public class CoTPlan
        {
            public string Thinking { get; set; }
            public JArray Steps { get; set; }
        }

        // Expose last used prompt and raw reply for callers to log when helpful
        public static string LastPromptUsed { get; private set; }
        public static string LastRawReply { get; private set; }
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
        // Track providers that recently failed so we can skip them for a cooldown period
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
        public class ClarificationResult<T>
        {
            public T Parsed { get; set; }
            public string Prompt { get; set; }
            public string RawReply { get; set; }
        }

        /// <summary>
        /// Ask the LLM to provide corrected step objects for the given array of missing entries.
        /// The returned JArray should contain steps in the same order corresponding to the missing entries.
        /// </summary>
        public static JArray ClarifyMissingDimensionSteps(JArray missing)
        {
            try
            {
                var prompt = PromptHandler.BuildMissingPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, missing);
                AddinStatusLogger.Log("ClarificationService", "Requesting LLM clarification for missing dimension params");

                // Respect provider priority like the UI: AICAD_LLM_PRIORITY (e.g. "local,gemini,groq")
                var priorityStr = LlmPriorityManager.GetPriority();
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = PromptHandler.BuildMissingPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, missing);
                foreach (var provider in priority)
                {
                    // Skip providers currently marked dead
                    try
                    {
                        EnforceProviderPacing(provider, 2000);
                        if (IsProviderMarkedDead(provider))
                        {
                            AddinStatusLogger.Log("ClarificationService", $"Skipping provider {provider} - marked dead");
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
                                                   ?? PromptHandler.BuildClarificationLocalSystemPrompt();

                                var localClient = GetLocalClient(localEndpoint, preferredModel, systemPrompt);
                                if (localClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => localClient.GenerateAsync(promptText), "local");
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Local LLM reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
                                    var extracted = ExtractJsonArray(reply);
                                    if (extracted != null) return extracted;
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
                                    var reply = AwaitWithTimeout(() => gemClient.GenerateAsync(promptText), "gemini");
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Gemini reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
                                    var extracted = ExtractJsonArray(reply);
                                    if (extracted != null) return extracted;
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
                                var groqClient = GetGroqClient(groqKey, groqModel);
                                if (groqClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => groqClient.GenerateAsync(promptText), "groq");
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Groq reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
                                    var extracted = ExtractJsonArray(reply);
                                    if (extracted != null) return extracted;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Treat timeouts and connection-refused as transient: mark provider dead and continue
                        if (ex is TimeoutException || IsConnectionRefused(ex))
                        {
                            try { AddinStatusLogger.Log("ClarificationService", $"{provider} transient failure: {ex.Message}. Marking dead and continuing"); } catch { }
                            try { MarkProviderDead(provider); } catch { }
                            continue;
                        }

                        lastEx = ex;
                        if (provider == "groq" && ex.Message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddinStatusLogger.Log("ClarificationService", "⚠️ [GROQ RATE LIMIT] " + ex.Message);
                        }
                        else
                        {
                            AddinStatusLogger.Log("ClarificationService", provider + " failed: " + ex.Message);
                        }
                    }
                }

                if (lastEx != null)
                {
                    // record last prompt/reply for external logging
                    try { LastRawReply = lastReply; } catch { }
                    try { LastPromptUsed = promptText; } catch { }
                    // surface last raw reply in the exception data for callers to log
                    try { if (!string.IsNullOrEmpty(lastReply)) lastEx.Data["llm_reply"] = lastReply; } catch { }
                    try { if (!string.IsNullOrEmpty(promptText)) lastEx.Data["llm_prompt"] = promptText; } catch { }
                    throw lastEx;
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "ClarifyMissingDimensionSteps failed", ex);
            }
            return null;
        }

        public static ClarificationResult<JArray> ClarifyMissingDimensionStepsWithDebug(JArray missing)
        {
            var res = new ClarificationResult<JArray> { Parsed = null, Prompt = PromptHandler.BuildMissingPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, missing), RawReply = null };
            try
            {
                var parsed = ClarifyMissingDimensionSteps(missing);
                res.Parsed = parsed;
                return res;
            }
            catch (Exception ex)
            {
                try { res.RawReply = ex.Data["llm_reply"]?.ToString(); } catch { }
                return res;
            }
        }

        /// <summary>
        /// Ask the LLM to clarify a single step that executed but apparently made no changes.
        /// Returns a replacement step JToken (object or array) or null.
        /// </summary>
        public static JToken ClarifySingleStep(JObject step, object handlerData = null)
        {
            try
            {
                var prompt = PromptHandler.BuildSingleStepPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, step, handlerData);
                AddinStatusLogger.Log("ClarificationService", "Requesting LLM clarification for single step");

                var priorityStr = LlmPriorityManager.GetPriority();
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    // Skip providers currently marked dead
                    try
                    {
                        EnforceProviderPacing(provider, 2000);
                        if (IsProviderMarkedDead(provider))
                        {
                            AddinStatusLogger.Log("ClarificationService", $"Skipping provider {provider} - marked dead");
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
                                                   ?? PromptHandler.BuildClarificationLocalSystemPrompt();

                                var localClient = GetLocalClient(localEndpoint, preferredModel, systemPrompt);
                                if (localClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => localClient.GenerateAsync(prompt), "local");
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Local LLM reply length=" + (reply?.Length ?? 0));
                                    var extracted = ExtractJsonToken(reply);
                                    if (extracted != null) return extracted;
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
                                    var reply = AwaitWithTimeout(() => gemClient.GenerateAsync(prompt), "gemini");
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Gemini reply length=" + (reply?.Length ?? 0));
                                    var extracted = ExtractJsonToken(reply);
                                    if (extracted != null) return extracted;
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
                                    var reply = AwaitWithTimeout(() => groqClient.GenerateAsync(prompt), "groq");
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Groq reply length=" + (reply?.Length ?? 0));
                                    var extracted = ExtractJsonToken(reply);
                                    if (extracted != null) return extracted;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Treat timeouts and connection-refused as transient: mark provider dead and continue
                        if (ex is TimeoutException || IsConnectionRefused(ex))
                        {
                            try { AddinStatusLogger.Log("ClarificationService", $"{provider} transient failure: {ex.Message}. Marking dead and continuing"); } catch { }
                            try { MarkProviderDead(provider); } catch { }
                            continue;
                        }

                        lastEx = ex;
                        if (provider == "groq" && ex.Message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddinStatusLogger.Log("ClarificationService", "⚠️ [GROQ RATE LIMIT] " + ex.Message);
                        }
                        else
                        {
                            AddinStatusLogger.Log("ClarificationService", provider + " failed: " + ex.Message);
                        }
                    }
                }

                if (lastEx != null)
                {
                    // record last prompt/reply for external logging
                    try { LastRawReply = lastReply; } catch { }
                    try { LastPromptUsed = promptText; } catch { }
                    // surface last raw reply in the exception data for callers to log
                    try { if (!string.IsNullOrEmpty(lastReply)) lastEx.Data["llm_reply"] = lastReply; } catch { }
                    try { if (!string.IsNullOrEmpty(promptText)) lastEx.Data["llm_prompt"] = promptText; } catch { }
                    throw lastEx;
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "ClarifySingleStep failed", ex);
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
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "ExtractJsonArray parse failed", ex);
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
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "ExtractJsonObject parse failed", ex);
                return null;
            }
        }

        private static JToken ExtractJsonToken(string txt)
        {
            // Prefer array if present; otherwise try object
            var arr = ExtractJsonArray(txt);
            if (arr != null) return arr;
            return ExtractJsonObject(txt);
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

        // Return true if provider is marked dead (until a future UTC time)
        private static bool IsProviderMarkedDead(string provider)
        {
            try
            {
                if (_providerDeadUntil.TryGetValue(provider ?? string.Empty, out var until))
                {
                    if (DateTime.UtcNow < until) return true;
                    // expired - remove
                    _providerDeadUntil.TryRemove(provider, out _);
                }
            }
            catch { }
            return false;
        }

        // Mark provider dead for a cooldown period read from env or default 300s
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
                AddinStatusLogger.Log("ClarificationService", $"Provider {provider} marked unreachable until {until:u}");
            }
            catch { }
        }

        // Inspect exception chain for socket/connect errors
        private static bool IsConnectionRefused(Exception ex)
        {
            if (ex == null) return false;
            Exception cur = ex;
            while (cur != null)
            {
                if (cur is SocketException) return true;
                if (cur is System.Net.Http.HttpRequestException && cur.InnerException is SocketException) return true;
                var msg = cur.Message ?? string.Empty;
                if (msg.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("no connection could be made", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                cur = cur.InnerException;
            }
            return false;
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

        private static string AwaitWithTimeout(Func<Task<string>> taskFactory, string provider, int seconds = 120)
        {
            var task = taskFactory();
            var timeoutMs = seconds * 1000;
            bool completed = Task.WaitAll(new[] { task }, timeoutMs);
            
            if (!completed)
            {
                throw new TimeoutException($"LLM {provider} timed out after {seconds}s");
            }
            
            // Task is guaranteed to be completed here; safely get result
            return task.Result;
        }

        /// <summary>
        /// Generate raw LLM text using the configured provider priority (AICAD_LLM_PRIORITY).
        /// This is a synchronous helper that mirrors the provider selection logic used
        /// by the clarifier helpers and returns the raw reply or null.
        /// </summary>
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
                            AddinStatusLogger.Log("ClarificationService", $"Skipping provider {provider} - marked dead");
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
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Local LLM reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
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
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Gemini reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
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
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Groq reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
                                    return reply;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is TimeoutException || IsConnectionRefused(ex))
                        {
                            try { AddinStatusLogger.Log("ClarificationService", $"{provider} transient failure: {ex.Message}. Marking dead and continuing"); } catch { }
                            try { MarkProviderDead(provider); } catch { }
                            continue;
                        }

                        lastEx = ex;
                        if (provider == "groq" && ex.Message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddinStatusLogger.Log("ClarificationService", "⚠️ [GROQ RATE LIMIT] " + ex.Message);
                        }
                        else
                        {
                            AddinStatusLogger.Log("ClarificationService", provider + " failed: " + ex.Message);
                        }
                    }
                }

                if (lastEx != null)
                {
                    try { LastRawReply = lastReply; } catch { }
                    try { LastPromptUsed = promptText; } catch { }
                    try { if (!string.IsNullOrEmpty(lastReply)) lastEx.Data["llm_reply"] = lastReply; } catch { }
                    try { if (!string.IsNullOrEmpty(promptText)) lastEx.Data["llm_prompt"] = promptText; } catch { }
                    throw lastEx;
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "GenerateWithPriority failed", ex);
            }
            return null;
        }

        /// <summary>
        /// Generate using provider priority but suppress the system prompt (send only a user message).
        /// Useful for short helper prompts like description generation where global system instructions
        /// would interfere.
        /// </summary>
        public static string GenerateUserOnlyWithPriority(string prompt, int timeoutSeconds = 120)
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
                            AddinStatusLogger.Log("ClarificationService", $"Skipping provider {provider} - marked dead");
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
                                var systemPrompt = string.Empty;

                                var localClient = GetLocalClient(localEndpoint, preferredModel, systemPrompt);
                                if (localClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => localClient.GenerateAsync(promptText), "local", timeoutSeconds);
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Local LLM reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
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
                                var gemSystemPrompt = string.Empty;
                                var gemClient = GetGeminiClient(gemKey, gemModel, gemSystemPrompt);
                                if (gemClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => gemClient.GenerateAsync(promptText), "gemini", timeoutSeconds);
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Gemini reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
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
                                var groqSystemPrompt = string.Empty;
                                var groqClient = GetGroqClient(groqKey, groqModel, groqSystemPrompt);
                                if (groqClient != null)
                                {
                                    var reply = AwaitWithTimeout(() => groqClient.GenerateAsync(promptText), "groq", timeoutSeconds);
                                    lastReply = reply;
                                    try { LastRawReply = reply; LastPromptUsed = promptText; } catch { }
                                    AddinStatusLogger.Log("ClarificationService", "Groq reply length=" + (reply?.Length ?? 0));
                                    try
                                    {
                                        var truncated = (reply ?? string.Empty).Replace("\r\n", "\\n");
                                        if (truncated.Length > 1500) truncated = truncated.Substring(0, 1500) + "...";
                                        AddinStatusLogger.Log("ClarificationService", "LLM Prompt: " + (promptText ?? string.Empty).Replace("\r\n", "\\n"));
                                        AddinStatusLogger.Log("ClarificationService", "LLM Reply (truncated): " + truncated);
                                    }
                                    catch { }
                                    return reply;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is TimeoutException || IsConnectionRefused(ex))
                        {
                            try { AddinStatusLogger.Log("ClarificationService", $"{provider} transient failure: {ex.Message}. Marking dead and continuing"); } catch { }
                            try { MarkProviderDead(provider); } catch { }
                            continue;
                        }

                        lastEx = ex;
                        if (provider == "groq" && ex.Message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddinStatusLogger.Log("ClarificationService", "⚠️ [GROQ RATE LIMIT] " + ex.Message);
                        }
                        else
                        {
                            AddinStatusLogger.Log("ClarificationService", provider + " failed: " + ex.Message);
                        }
                    }
                }

                if (lastEx != null)
                {
                    try { LastRawReply = lastReply; } catch { }
                    try { LastPromptUsed = promptText; } catch { }
                    try { if (!string.IsNullOrEmpty(lastReply)) lastEx.Data["llm_reply"] = lastReply; } catch { }
                    try { if (!string.IsNullOrEmpty(promptText)) lastEx.Data["llm_prompt"] = promptText; } catch { }
                    throw lastEx;
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "GenerateWithPriority failed", ex);
            }
            return null;
        }

        /// <summary>
        /// Ask the LLM to generate a plan (steps array) based on user intent and optional model facts.
        /// Returns a JArray of steps ready for execution.
        /// </summary>
        public static JToken PlanFromIntent(string intent, JObject modelFacts = null)
        {
            try
            {
                // Prefer CoT-enabled plan when available; fallback to legacy array
                // If user has enabled require-spec-clarification, do a quick heuristic check
                try
                {
                    var require = SettingsManager.GetBool("RequireSpecClarification", false);
                    if (require)
                    {
                        var missing = CheckForMissingEngineeringSpecs(intent, modelFacts);
                        if (!string.IsNullOrWhiteSpace(missing))
                        {
                            // Return a JSON object indicating what clarification is needed
                            var clar = new JObject();
                            clar["clarification_needed"] = missing;
                            return clar;
                        }
                    }
                }
                catch { }

                var cot = PlanFromIntentWithThinking(intent, modelFacts);
                if (cot != null && cot.Steps != null && cot.Steps.Count > 0)
                {
                    AddinStatusLogger.Log("ClarificationService", $"PlanFromIntent (CoT) returned {cot.Steps.Count} steps");
                    return cot.Steps;
                }

                var prompt = PromptHandler.BuildIntentPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, intent, modelFacts);
                AddinStatusLogger.Log("ClarificationService", $"Requesting LLM plan from intent: {intent}");

                var reply = GenerateWithPriority(prompt);
                if (string.IsNullOrWhiteSpace(reply))
                    return null;

                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    AddinStatusLogger.Log("ClarificationService", $"PlanFromIntent returned {extracted.Count} steps");
                    return extracted;
                }

                // If no array found, try extracting object with "steps" property
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
                AddinStatusLogger.Error("ClarificationService", "PlanFromIntent failed", ex);
                return null;
            }
        }

        // Very small heuristic checker to determine if intent lacks engineering-critical specs.
        // Returns a human-readable message describing missing information, or null/empty if OK.
        private static string CheckForMissingEngineeringSpecs(string intent, JObject facts)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(intent)) return "User intent is empty; please provide details.";
                var lower = intent.ToLowerInvariant();

                // Flange: require PN class or standard (e.g., PN10, PN16, ANSI 150)
                if (lower.Contains("flange") || lower.Contains("flanged"))
                {
                    if (!(lower.Contains("pn") || lower.Contains("pn ") || System.Text.RegularExpressions.Regex.IsMatch(lower, "pn\\s*\\d+") || lower.Contains("ansi") || lower.Contains("class")))
                    {
                        return "Flange requests require PN class or flange standard (e.g., 'PN16' or 'ANSI 150').";
                    }
                }

                // Base/block/plate: require explicit dimensions (LxWxH or numeric values)
                if (lower.Contains("base") || lower.Contains("plate") || lower.Contains("block") || lower.Contains("box") || lower.Contains("rectan"))
                {
                    // look for numeric dimension patterns (e.g., 100x50x20 or 100 x 50 x 20, or '100 mm')
                    var hasDims = System.Text.RegularExpressions.Regex.IsMatch(lower, "\\d+(\\.\\d+)?\\s*(mm|cm|m)?") || System.Text.RegularExpressions.Regex.IsMatch(lower, "\\d+(\\.\\d+)?\\s*[x×]\\s*\\d+(\\.\\d+)?");
                    if (!hasDims)
                    {
                        return "Please provide explicit dimensions for the base/plate (length x width x height) in millimeters.";
                    }
                }

                // Cylinder: require radius/diameter and height
                if (lower.Contains("cylinder"))
                {
                    var hasRadius = lower.Contains("radius") || lower.Contains("r=") || lower.Contains("diameter") || lower.Contains("dia") || System.Text.RegularExpressions.Regex.IsMatch(lower, "\\d+\\s*mm");
                    var hasHeight = lower.Contains("height") || lower.Contains("h=") || System.Text.RegularExpressions.Regex.IsMatch(lower, "\\d+[x×]\\d+");
                    if (!hasRadius || !hasHeight)
                    {
                        return "Cylinder requests require radius/diameter and height values (in mm).";
                    }
                }

                // Hole(s): require diameter
                if (lower.Contains("hole") || lower.Contains("holes"))
                {
                    if (!(lower.Contains("diameter") || lower.Contains("dia") || lower.Contains("d=") || lower.Contains("r=")))
                    {
                        return "Hole operations require a diameter (e.g., 'diameter 10mm').";
                    }
                }

                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Ask the LLM to generate a JSON OBJECT with fields: thinking (string) and steps (array).
        /// Returns a CoTPlan with both fields populated when possible.
        /// </summary>
        public static CoTPlan PlanFromIntentWithThinking(string intent, JObject modelFacts = null)
        {
            try
            {
                var prompt = PromptHandler.BuildIntentPromptWithCoT(PromptHandler.DEFAULT_SYSTEM_PROMPT, intent, modelFacts);
                AddinStatusLogger.Log("ClarificationService", $"Requesting LLM CoT plan from intent: {intent}");

                var reply = GenerateWithPriority(prompt);
                if (string.IsNullOrWhiteSpace(reply))
                    return null;

                // Try to extract a JSON object first
                var obj = ExtractJsonObject(reply);
                if (obj != null)
                {
                    var thinking = obj["thinking"]?.ToString();
                    var steps = obj["steps"] as JArray;
                    if (steps == null)
                    {
                        // Fallback if only array is returned
                        steps = ExtractJsonArray(reply);
                    }
                    return new CoTPlan { Thinking = thinking, Steps = steps };
                }

                // Fallback: if the model ignored instructions and returned an array
                var arr = ExtractJsonArray(reply);
                if (arr != null)
                {
                    return new CoTPlan { Thinking = null, Steps = arr };
                }

                return null;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error("ClarificationService", "PlanFromIntentWithThinking failed", ex);
                return null;
            }
        }

    }
}
