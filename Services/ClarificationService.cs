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
        // Shared default system prompt - DRY principle: define once, use everywhere
        public const string DEFAULT_SYSTEM_PROMPT = 
            "You are a CAD planning agent for SOLIDWORKS. " +
            "Convert user requests into step plan JSON with a top-level 'steps' array. " +
            "Supported ops: new_part; select_plane{name}; select_face{id}; sketch_begin; rectangle_center{cx,cy,w,h}; circle_center{cx,cy,r|diameter}; line; arc; dimension; constraint; sketch_end; extrude{depth}; extrude_cut{depth}; revolve; sweep; loft; fillet; chamfer; hole; pocket; set_material{material}; description{text}; zoom_to_fit. " +
            "CRITICAL: Use extrude_cut (separate op) for cuts, NOT extrude with type='cut'. Use select_face with id='top'/'front'/'right', NOT numeric IDs. " +
            "For auto_dimension on circles, use radius or diameter field, NOT w/h. For rectangles, copy cx, cy, w, h values. " +
            "Units are millimeters. Output ONLY raw JSON - no markdown, no extra text.";

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
                var prompt = BuildMissingPrompt(missing);
                AddinStatusLogger.Log("ClarificationService", "Requesting LLM clarification for missing dimension params");

                // Respect provider priority like the UI: AICAD_LLM_PRIORITY (e.g. "local,gemini,groq")
                var priorityStr = System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.User)
                                  ?? System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.Process)
                                  ?? "local,gemini,groq";
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = BuildMissingPrompt(missing);
                foreach (var provider in priority)
                {
                    // Skip providers currently marked dead
                    try
                    {
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
                                                   ?? "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text. For dimension operations, you MUST copy the cx, cy, w, h values from the rectangle.";

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
                                                     ?? DEFAULT_SYSTEM_PROMPT;
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
            var res = new ClarificationResult<JArray> { Parsed = null, Prompt = BuildMissingPrompt(missing), RawReply = null };
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
                var prompt = BuildSingleStepPrompt(step, handlerData);
                AddinStatusLogger.Log("ClarificationService", "Requesting LLM clarification for single step");

                var priorityStr = System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.User)
                                  ?? System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.Process)
                                  ?? "local,gemini,groq";
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    // Skip providers currently marked dead
                    try
                    {
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
                                                   ?? "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text. For dimension operations, you MUST copy the cx, cy, w, h values from the rectangle.";

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
                                                     ?? DEFAULT_SYSTEM_PROMPT;
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
                                                      ?? DEFAULT_SYSTEM_PROMPT;
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

        private static string BuildMissingPrompt(JArray missing)
        {
             // Strong directive: return only a JSON array. If required numeric values are missing,
             // choose safe defaults (cx=0, cy=0, w=100, h=100) rather than asking questions.
             // Additionally: ALWAYS include explicit dimension steps for any sketch geometry you create.
             return DEFAULT_SYSTEM_PROMPT + "\n\n" +
                 "INSTRUCTIONS:\n" +
                 "- You MUST reply with a single JSON ARRAY only (no surrounding text, no commentary).\n" +
                 "- Each element must be a complete step object matching the SolidWorks plan schema.\n" +
                "- For rectangle geometry, include numeric fields for the shape and prefer using the auto-dimension operator: \"op\":\"auto_dimension\" (or \"auto-dimension\"). Include numeric fields such as \"cx\", \"cy\", \"w\", \"h\" (all in mm).\n" +
                "- ALWAYS include appropriate \"auto_dimension\" steps (op:\"auto_dimension\") for any sketch geometry you create (e.g., horizontal and vertical dimensions for rectangles with a numeric \"value\" in mm).\n" +
                 "- If any numeric values are missing, do NOT ask questions — fill sensible defaults: cx=0, cy=0, w=100, h=100.\n" +
                 "- Do NOT emit any natural-language question or explanation. Output JSON ONLY.\n\n" +
                 "Provide corrected steps for the following missing entries (same order):\n" + missing.ToString();
        }

        private static string BuildSingleStepPrompt(JObject step, object handlerData)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(DEFAULT_SYSTEM_PROMPT + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- Reply with a single JSON OBJECT only (no commentary).\n");
            sb.AppendLine("- The object must be a valid plan step. For dimension steps include numeric fields: cx, cy, w, h (mm).\n");
            sb.AppendLine("- ALWAYS use op:'auto_dimension' (NOT 'dimension') for sketch dimension steps.\n");
            sb.AppendLine("- If you need numeric values, do NOT ask questions — supply sensible defaults: cx=0, cy=0, w=100, h=100.\n");
            sb.AppendLine("- Do NOT include any natural-language text; output JSON only.\n");
            sb.AppendLine("Original step:");
            sb.AppendLine(step.ToString());
            if (handlerData != null)
            {
                sb.AppendLine("Handler data:");
                try { sb.AppendLine(JToken.FromObject(handlerData).ToString()); } catch { sb.AppendLine(handlerData.ToString()); }
            }
            return sb.ToString();
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
                var priorityStr = System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.User)
                                  ?? System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.Process)
                                  ?? "local,gemini,groq";
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    try
                    {
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
                                                   ?? DEFAULT_SYSTEM_PROMPT;

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
                                                     ?? DEFAULT_SYSTEM_PROMPT;
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
                                                      ?? DEFAULT_SYSTEM_PROMPT;
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
                var priorityStr = System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.User)
                                  ?? System.Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", System.EnvironmentVariableTarget.Process)
                                  ?? "local,gemini,groq";
                var priority = priorityStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLower()).ToList();

                Exception lastEx = null;
                string lastReply = null;
                var promptText = prompt;
                foreach (var provider in priority)
                {
                    try
                    {
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
        public static JArray PlanFromIntent(string intent, JObject modelFacts = null)
        {
            try
            {
                var prompt = BuildIntentPrompt(intent, modelFacts);
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

        private static string BuildIntentPrompt(string intent, JObject facts)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(DEFAULT_SYSTEM_PROMPT + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- The user wants to modify an existing SolidWorks model based on their intent.");
            sb.AppendLine("- You are provided with the current model state (features, geometry, etc.).");
            sb.AppendLine("- Generate a JSON ARRAY of steps to fulfill the user's request.");
            sb.AppendLine("- Output ONLY the JSON array — no markdown, no extra text.");
            sb.AppendLine("- Use operations that work on the existing model (select faces, sketch, cut, etc.).");
            sb.AppendLine("- For dice pips: select face by id (top/bottom/left/right/front/back), sketch circles at calculated positions, extrude_cut shallow depth.\n");
            
            if (facts != null)
            {
                sb.AppendLine("CURRENT MODEL STATE:");
                sb.AppendLine(facts.ToString());
                sb.AppendLine();
            }
            
            sb.AppendLine("USER INTENT:");
            sb.AppendLine(intent);
            sb.AppendLine();
            sb.AppendLine("Generate the steps array now:");
            
            return sb.ToString();
        }

    }
}
