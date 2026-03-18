using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using AICAD.Services.Logging;
using AICAD.Services.Operations;

namespace AICAD.Services
{
    internal static class LlmPlanService
    {
        internal static event Action<string, string, string> ThinkingUpdated;

        public class FeaturePlanResult
        {
            public JArray Steps { get; set; }
            public string Thinking { get; set; }
            public bool ClarificationNeeded { get; set; }
            public JObject Clarification { get; set; }
        }
        public class DecomposeResult
        {
            public string Description { get; set; }
            public bool NeedsDescription { get; set; }
            public string Question { get; set; }
            public JArray Features { get; set; }
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
        private static string _geminiSystemPrompt;
        private static GroqLlmClient _groqClient;
        private static string _groqKey;
        private static string _groqModel;
        private static string _groqSystemPrompt;
        private static readonly object _rateLimitLock = new object();
        private static readonly Dictionary<string, DateTime> _lastProviderCall = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        internal static Func<string, string> OpRepairResponder; // test hook: if set, used instead of LLM during op repair

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
            var ctx = new LoggingContext { CorrelationId = runId, Operation = "Classify", Provider = "thread_subtask", StartTimeUtc = DateTimeOffset.UtcNow };
            var logger = LoggerFactoryBuilder.Factory.CreateLogger("LlmPlanService");
            try
            {
                var prompt = PromptHandler.BuildThreadSubtaskPrompt(PromptHandler.DEFAULT_SYSTEM_PROMPT, threadStep, modelFacts);
                var sw = Stopwatch.StartNew();
                LogLlmSend(logger, ctx, "classify", "priority", prompt);

                var reply = GenerateWithPriority(prompt, "CLASSIFY", 120, runId, requestId);
                sw.Stop();
                if (string.IsNullOrWhiteSpace(reply))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"LLM request end: thread_subtask empty_reply elapsedMs={sw.ElapsedMilliseconds}");
                    return null;
                }

                LogLlmRecv(logger, ctx, "classify", "priority", reply, sw.ElapsedMilliseconds, 200);

                var extracted = ExtractJsonArray(reply);
                if (extracted != null && extracted.Count > 0)
                {
                    logger.LogWithContext(LogLevel.Information, ctx, $"Thread subtask steps={extracted.Count}");
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
                logger.LogException(ctx, ex, "PlanThreadSubtask failed");
                return null;
            }
        }

        public static DecomposeResult DecomposeByFeature(string userRequest, string runId = null, string requestId = null, int timeoutSeconds = 120)
        {
            try
            {
                var localUbTask = TryBuildUbBoltTask(userRequest);
                if (localUbTask != null)
                {
                    var tasks = new JArray { localUbTask };
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "Decompose shortcut: detected U-bolt request, using local task"); } catch { }
                    return new DecomposeResult
                    {
                        Description = "U-bolt",
                        Features = tasks
                    };
                }

                var prompt = PromptHandler.BuildFeatureDecomposePrompt(PromptHandler.DEFAULT_DECOMPOSE_SYSTEM_PROMPT, userRequest);
                var sw = Stopwatch.StartNew();
                var ctx = new LoggingContext { CorrelationId = runId, Operation = "Build", Provider = "decompose", StartTimeUtc = DateTimeOffset.UtcNow };
                var logger = LoggerFactoryBuilder.Factory.CreateLogger("LlmPlanService");
                LogLlmSend(logger, ctx, "decompose", "priority", prompt);
                var reply = GenerateWithPriority(prompt, "DECOMPOSE", timeoutSeconds, runId, requestId);
                if (string.IsNullOrWhiteSpace(reply))
                {
                    LogLlmRecv(logger, ctx, "decompose", "priority", string.Empty, sw.ElapsedMilliseconds, 0, "empty");
                    return null;
                }
                sw.Stop();
                LogLlmRecv(logger, ctx, "decompose", "priority", reply, sw.ElapsedMilliseconds, 200);
                var extracted = TryExtractDecomposeResult(reply);
                if (extracted != null)
                {
                    var count = extracted.Features?.Count ?? 0;
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Parsed tasks count={count} needs_description={extracted.NeedsDescription}");
                    for (int i = 0; i < count; i++)
                    {
                        var taskJson = string.Empty;
                        try { taskJson = Newtonsoft.Json.JsonConvert.SerializeObject(extracted.Features[i], Newtonsoft.Json.Formatting.None); } catch { }
                        DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", $"Task[{i}]: " + DiagnosticLogWriter.Truncate(taskJson, 800));
                    }
                    return extracted;
                }
                return null;
            }
            catch (Exception ex)
            {
                if (IsPromptCatalogFatal(ex)) throw;
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "DecomposeByFeature failed: " + ex.Message);
                return null;
            }
        }

        public static FeaturePlanResult PlanFeatureSubtask(JObject featureTask, JObject modelFacts = null, string runId = null, string requestId = null, int timeoutSeconds = 120)
        {
            try
            {
                var featureType = featureTask?.Value<string>("feature_type") ?? string.Empty;
                if (IsUBoltFeature(featureType))
                {
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "PlanFeatureSubtask shortcut: using local U-bolt plan"); } catch { }
                    return BuildUbBoltPlan(featureTask, modelFacts);
                }
                if (IsMaterialFeature(featureType))
                {
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", "PlanFeatureSubtask shortcut: using local material plan"); } catch { }
                    return BuildMaterialPlan(featureTask, modelFacts);
                }

                var label = featureTask?.Value<string>("feature_type") ?? "feature";
                var systemPromptOverride = PromptHandler.GetExecuteSystemPromptForFeatureType(featureType);
                var resolvedSystemPrompt = string.IsNullOrWhiteSpace(systemPromptOverride)
                    ? (string.IsNullOrWhiteSpace(PromptHandler.EXECUTE_SYSTEM_PROMPT) ? PromptHandler.DEFAULT_SYSTEM_PROMPT : PromptHandler.EXECUTE_SYSTEM_PROMPT)
                    : systemPromptOverride;
                var prompt = PromptHandler.BuildFeaturePlanPrompt(resolvedSystemPrompt, featureTask, modelFacts);
                var ctx = new LoggingContext { CorrelationId = runId, Operation = "Build", Provider = label, StartTimeUtc = DateTimeOffset.UtcNow };
                var logger = LoggerFactoryBuilder.Factory.CreateLogger("LlmPlanService");
                int effectiveTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 120;
                try
                {
                    var env = System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_FEATURE_PLAN_TIMEOUT_SECONDS", System.EnvironmentVariableTarget.User);
                    if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var secs) && secs > 0)
                        effectiveTimeoutSeconds = secs;
                }
                catch { }

                LogPromptSelection(runId, requestId, "EXECUTE", PromptStageRouter.GetKeys("EXECUTE"), resolvedSystemPrompt, PromptCatalog.GetTemplate("execute_template"));

                var reply = SendFeaturePlanAttempt(logger, ctx, label, prompt, effectiveTimeoutSeconds, runId, requestId, resolvedSystemPrompt);
                if (string.IsNullOrWhiteSpace(reply))
                    return null;

                var planResult = TryExtractFeaturePlan(reply);
                            if (PlanNeedsCorrection(planResult))
                            {
                                var reason = DescribePlanSchemaIssue(planResult);
                                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "WARNING", $"Feature plan schema validation failed; retrying with schema correction instructions. reason={reason}");
                                var correctedPrompt = prompt + SchemaCorrectionSuffix;
                                reply = SendFeaturePlanAttempt(logger, ctx, label, correctedPrompt, effectiveTimeoutSeconds, runId, requestId, resolvedSystemPrompt);
                                if (string.IsNullOrWhiteSpace(reply))
                                    return null;
                                planResult = TryExtractFeaturePlan(reply);
                                if (PlanNeedsCorrection(planResult))
                                {
                                    var secondReason = DescribePlanSchemaIssue(planResult);
                                    try { AddinStatusLogger.Error("LlmPlanService", $"EXECUTE schema invalid after retry: {secondReason}"); } catch { }
                                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"Feature plan schema invalid after retry: {secondReason}");
                                    LogPlanParseFailure(runId, requestId, reply);
                                    return null;
                                }
                            }

                planResult = ValidateAndRepairUnknownOps(planResult, label, prompt, resolvedSystemPrompt, effectiveTimeoutSeconds, runId, requestId);
                if (planResult == null)
                    return null;

                if (planResult?.ClarificationNeeded == true)
                {
                    EmitThinkingUpdated(runId, label, planResult.Thinking);
                    return planResult;
                }
                if (planResult?.Steps != null && planResult.Steps.Count > 0)
                {
                    EmitThinkingUpdated(runId, label, planResult.Thinking);
                    var thinkingPreview = DiagnosticLogWriter.Truncate(planResult.Thinking ?? string.Empty, 400);
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "INFO", $"Thinking={thinkingPreview} steps={planResult.Steps.Count}");
                    return new FeaturePlanResult
                    {
                        Steps = planResult.Steps,
                        Thinking = planResult.Thinking
                    };
                }

                LogPlanParseFailure(runId, requestId, reply);
                return null;
            }
            catch (Exception ex)
            {
                if (IsPromptCatalogFatal(ex)) throw;
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "PlanFeatureSubtask failed: " + ex.Message);
                return null;
            }
        }

        private static void EmitThinkingUpdated(string runId, string featureType, string thinking)
        {
            if (string.IsNullOrWhiteSpace(thinking))
                return;
            try { ThinkingUpdated?.Invoke(runId, featureType ?? string.Empty, thinking); } catch { }
        }

        private const string SchemaCorrectionSuffix = "\n\nSchema correction: Return JSON with a 'steps' array only. Each step must include an 'op' field (never 'command') and list operation parameters as top-level keys (no nested 'params'). Do not include description/features/needs_description/question.";

        private static string SendFeaturePlanAttempt(ILogger logger, LoggingContext ctx, string label, string prompt, int timeoutSeconds, string runId, string requestId, string systemPromptOverride)
        {
            var sw = Stopwatch.StartNew();
            LogLlmSend(logger, ctx, $"expand-{label}", "priority", prompt);
            var reply = GenerateWithPriority(prompt, "EXECUTE", timeoutSeconds, runId, requestId, systemPromptOverride);
            sw.Stop();
            if (string.IsNullOrWhiteSpace(reply))
            {
                LogLlmRecv(logger, ctx, $"expand-{label}", "priority", string.Empty, sw.ElapsedMilliseconds, 0, "empty");
                return null;
            }
            LogLlmRecv(logger, ctx, $"expand-{label}", "priority", reply, sw.ElapsedMilliseconds, 200);
            return reply;
        }

        private static FeaturePlanResult TryExtractFeaturePlan(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return null;
            try
            {
                var obj = ExtractJsonObject(reply);
                if (obj != null)
                {
                    if (obj.Value<bool?>("clarification_needed") == true)
                    {
                        return new FeaturePlanResult
                        {
                            ClarificationNeeded = true,
                            Clarification = obj,
                            Thinking = obj.Value<string>("thinking") ?? string.Empty
                        };
                    }
                    if (obj["steps"] is JArray arr && arr.Count > 0)
                    {
                        var thinking = obj.Value<string>("thinking") ?? string.Empty;
                        return new FeaturePlanResult
                        {
                            Steps = arr,
                            Thinking = thinking
                        };
                    }
                }
                var array = ExtractJsonArray(reply);
                if (array != null && array.Count > 0)
                {
                    return new FeaturePlanResult { Steps = array };
                }
            }
            catch { }
            return null;
        }

        private static bool PlanNeedsCorrection(FeaturePlanResult planResult)
        {
            if (planResult?.ClarificationNeeded == true)
                return false;
            if (planResult?.Steps == null || planResult.Steps.Count == 0)
                return true;
            foreach (var token in planResult.Steps)
            {
                if (!(token is JObject stepObj))
                    return true;
                if (stepObj["command"] != null)
                    return true;
                var opToken = stepObj["op"];
                if (opToken == null || string.IsNullOrWhiteSpace(opToken.ToString()))
                    return true;
            }
            return false;
        }

        private static string DescribePlanSchemaIssue(FeaturePlanResult planResult)
        {
            if (planResult == null)
                return "plan result is null";
            if (planResult.ClarificationNeeded)
                return "received clarification payload instead of execution steps";
            if (planResult.Steps == null || planResult.Steps.Count == 0)
                return "missing or empty steps array";
            int index = 0;
            foreach (var token in planResult.Steps)
            {
                if (!(token is JObject stepObj))
                    return $"step[{index}] is not an object";
                if (stepObj["command"] != null)
                    return $"step[{index}] uses forbidden 'command' field";
                var opToken = stepObj["op"];
                if (opToken == null || string.IsNullOrWhiteSpace(opToken.ToString()))
                    return $"step[{index}] missing 'op'";
                index++;
            }
            return "unknown schema mismatch";
        }

        private static FeaturePlanResult ValidateAndRepairUnknownOps(FeaturePlanResult planResult, string label, string originalPrompt, string resolvedSystemPrompt, int timeoutSeconds, string runId, string requestId)
        {
            var allowedOps = new HashSet<string>(OperationRegistry.CreateDefault().GetRegisteredOperations(), StringComparer.OrdinalIgnoreCase);
            var unknown = FindUnknownOps(planResult?.Steps, allowedOps);
            if (unknown.Count == 0)
                return planResult;

            var allowedPreview = string.Join(", ", allowedOps.Take(30));
            try { DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "WARNING", $"Unknown ops detected in EXECUTE plan: {string.Join(", ", unknown)}. Allowed (first 30): {allowedPreview}"); } catch { }
            try { AddinStatusLogger.Log("ExecutePlan", $"WARNING: Unknown ops in plan: {string.Join(", ", unknown)}. Allowed: {allowedPreview}"); } catch { }

            var repairPrompt = BuildOpRepairPrompt(originalPrompt, unknown, allowedOps);
            var ctx = new LoggingContext { CorrelationId = runId, Operation = "Build", Provider = label + "-repair", StartTimeUtc = DateTimeOffset.UtcNow };
            var logger = LoggerFactoryBuilder.Factory.CreateLogger("LlmPlanService");
            string reply;
            if (OpRepairResponder != null)
            {
                reply = OpRepairResponder(repairPrompt);
            }
            else
            {
                reply = SendFeaturePlanAttempt(logger, ctx, label + "-repair", repairPrompt, timeoutSeconds, runId, requestId, resolvedSystemPrompt);
            }
            if (string.IsNullOrWhiteSpace(reply))
                return null;

            var repaired = TryExtractFeaturePlan(reply);
            if (PlanNeedsCorrection(repaired))
            {
                var reason = DescribePlanSchemaIssue(repaired);
                try { AddinStatusLogger.Error("ExecutePlan", $"Op-repair schema invalid: {reason}"); } catch { }
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"Op-repair schema invalid: {reason}");
                LogPlanParseFailure(runId, requestId, reply);
                return null;
            }

            var remainingUnknown = FindUnknownOps(repaired?.Steps, allowedOps);
            if (remainingUnknown.Count > 0)
            {
                var remPreview = string.Join(", ", remainingUnknown);
                try { AddinStatusLogger.Error("ExecutePlan", $"Op-repair failed; still unknown ops: {remPreview}. Allowed: {allowedPreview}"); } catch { }
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", $"Op-repair failed; unknown ops remain: {remPreview}");
                return null;
            }

            return repaired;
        }

        internal static FeaturePlanResult ValidateAndRepairOpsForTest(FeaturePlanResult planResult, string originalPrompt, string resolvedSystemPrompt, string label, int timeoutSeconds, string runId, string requestId)
        {
            return ValidateAndRepairUnknownOps(planResult, label, originalPrompt, resolvedSystemPrompt, timeoutSeconds, runId, requestId);
        }

        private static List<string> FindUnknownOps(JArray steps, HashSet<string> allowedOps)
        {
            var unknown = new List<string>();
            if (steps == null) return unknown;
            foreach (var token in steps)
            {
                if (token is JObject obj)
                {
                    var op = obj.Value<string>("op") ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(op) && !allowedOps.Contains(op))
                    {
                        unknown.Add(op);
                    }
                }
            }
            return unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildOpRepairPrompt(string originalPrompt, IList<string> unknownOps, HashSet<string> allowedOps)
        {
            var allowedList = string.Join(", ", allowedOps);
            var unknownList = string.Join(", ", unknownOps);
            var guidance = string.Empty;
            if (unknownOps.Any(o => o.Equals("create_cube", StringComparison.OrdinalIgnoreCase) || o.Equals("create_box", StringComparison.OrdinalIgnoreCase)))
            {
                guidance = "For cubes/boxes, use: select_plane -> sketch_begin -> rectangle_center -> dimension -> sketch_end -> extrude.";
            }
            return originalPrompt + "\n\nREPAIR REQUEST:\nThe previous plan used unknown ops: " + unknownList +
                   ". Allowed ops (use only these, do NOT invent new ops): " + allowedList + ". " + guidance +
                   "\nRegenerate the steps JSON as { \"steps\": [ { \"op\": \"...\", \"<param>\": <value> } ] } using only allowed ops. Do NOT use nested \"params\" objects.";
        }

        private static void LogPromptSelection(string runId, string requestId, string stageKey, StagePromptKeys promptKeys, string systemPrompt, string templateBody)
        {
            try
            {
                var sysPreview = DiagnosticLogWriter.Truncate(systemPrompt ?? string.Empty, 80);
                var tplPreview = DiagnosticLogWriter.Truncate(templateBody ?? string.Empty, 80);
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "DEBUG", $"Prompt selection stage={stageKey} sysKey={promptKeys.SystemPromptKey} tplKey={promptKeys.TemplateKey} sysPreview={sysPreview} tplPreview={tplPreview}");
            }
            catch { }
        }

        private static void LogPlanParseFailure(string runId, string requestId, string reply)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reply))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "Feature plan parse failed; reply empty");
                    return;
                }
                var truncated = reply.Length > 800 ? reply.Substring(0, 800) + "..." : reply;
                DiagnosticLogWriter.LogLine(runId, requestId, "LlmPlanService", "ERROR", "Feature plan parse failed; reply=" + DiagnosticLogWriter.Truncate(truncated, 800));
            }
            catch { }
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
                var response = GenerateWithPriority(prompt, "CLASSIFY", timeoutSeconds, runId, requestId);
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

        public static string GenerateWithPriority(string prompt, string stage, int timeoutSeconds = 120, string runId = null, string requestId = null, string systemPromptOverride = null)
        {
            try
            {
                var priority = ProviderRouter.GetFallbackOrder().ToList();

                Exception lastEx = null;
                var promptText = prompt;
                var normalizedStage = NormalizeStage(stage);
                var stageKey = string.IsNullOrWhiteSpace(normalizedStage) ? "EXECUTE" : normalizedStage.ToUpperInvariant();
                var promptKeys = PromptStageRouter.GetKeys(stageKey);
                var currentCtx = LoggingContext.Current;
                if (currentCtx != null)
                    currentCtx.PromptMetadata = new PromptMetadata(promptKeys.Stage, promptKeys.SystemPromptKey, promptKeys.TemplateKey);
                var resolvedLogPrompt = !string.IsNullOrWhiteSpace(systemPromptOverride)
                    ? systemPromptOverride
                    : GetDefaultSystemPromptForStage(stageKey);
                var templateBody = PromptCatalog.GetTemplate(promptKeys.TemplateKey);
                LogPromptSelection(runId, requestId, stageKey, promptKeys, resolvedLogPrompt, templateBody);
                // Detect and abort early if the assembled user prompt is empty to avoid sending empty payloads
                if (string.IsNullOrWhiteSpace(promptText))
                {
                    throw new InvalidOperationException($"Assembled prompt text was empty for stage={stageKey} using templateKey={promptKeys.TemplateKey}.");
                }
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
                                var localPrompt = GetLocalSystemPromptForStage(stageKey, systemPromptOverride);
                                PromptSelectionValidator.Validate(stageKey, localPrompt);
                                var localClient = GetLocalClient(localEndpoint, preferredModel, localPrompt);
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
                                var gemSystemPrompt = GetRemoteSystemPromptForStage(stageKey, systemPromptOverride);
                                PromptSelectionValidator.Validate(stageKey, gemSystemPrompt);
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
                                var groqSystemPrompt = GetRemoteSystemPromptForStage(stageKey, systemPromptOverride);
                                if (string.IsNullOrWhiteSpace(groqSystemPrompt))
                                {
                                    throw new InvalidOperationException($"Resolved system prompt empty for provider=groq stage={stageKey} systemPromptKey={promptKeys.SystemPromptKey}.");
                                }
                                PromptSelectionValidator.Validate(stageKey, groqSystemPrompt);
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
                if (IsPromptCatalogFatal(ex)) throw;
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

        private static DecomposeResult TryExtractDecomposeResult(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return null;
            try
            {
                var obj = ExtractJsonObject(reply);
                if (obj != null)
                {
                    var features = obj["features"] as JArray;
                    if (features == null)
                        features = ExtractJsonArray(reply);
                    if (features == null)
                        features = new JArray();
                    var needs = obj.Value<bool?>("needs_description") ?? false;
                    return new DecomposeResult
                    {
                        Description = obj.Value<string>("description") ?? string.Empty,
                        NeedsDescription = needs,
                        Question = obj.Value<string>("question") ?? string.Empty,
                        Features = features
                    };
                }
            }
            catch { }
            var arr = ExtractJsonArray(reply);
            if (arr != null)
            {
                return new DecomposeResult
                {
                    Description = string.Empty,
                    NeedsDescription = false,
                    Question = string.Empty,
                    Features = arr
                };
            }
            return null;
        }

        private static void LogLlmSend(ILogger logger, LoggingContext ctx, string name, string provider, string prompt)
        {
            ctx.Stage = NormalizeStage(ctx.Stage ?? ctx.Operation);
            var preview = LogRedactor.Truncate(prompt, 200);
            var hash = LogRedactor.StableHash(prompt ?? string.Empty);
            logger.LogWithContext(LogLevel.Information, ctx, $"LLM ► {name}", null, new Dictionary<string, object>
            {
                ["event"] = "LLM_SEND",
                ["provider"] = provider,
                ["promptLen"] = prompt?.Length ?? 0,
                ["promptHash"] = hash,
                ["promptPreview"] = preview
            });
        }

        private static void LogLlmRecv(ILogger logger, LoggingContext ctx, string name, string provider, string reply, long elapsedMs, int previewLen = 200, string status = "200")
        {
            ctx.Stage = NormalizeStage(ctx.Stage ?? ctx.Operation);
            var preview = LogRedactor.Truncate(reply, previewLen);
            var hash = LogRedactor.StableHash(reply ?? string.Empty);
            logger.LogWithContext(LogLevel.Information, ctx, $"LLM ◄ {name}", null, new Dictionary<string, object>
            {
                ["event"] = "LLM_RECV",
                ["provider"] = provider,
                ["status"] = status,
                ["elapsedMs"] = elapsedMs,
                ["replyLen"] = reply?.Length ?? 0,
                ["replyHash"] = hash,
                ["replyPreview"] = preview
            });
        }

        private static string NormalizeStage(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage)) return null;
            var parts = stage.Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return stage;
            var last = parts[parts.Length - 1];
            switch (last.ToUpperInvariant())
            {
                case "CLASSIFY":
                case "DECOMPOSE":
                case "EXECUTE":
                case "VALIDATE":
                case "UI":
                    return last.ToUpperInvariant();
                default:
                    return last;
            }
        }

        internal static string GetDefaultSystemPromptForStage(string stageKey)
        {
            if (string.IsNullOrWhiteSpace(stageKey))
                return PromptHandler.DEFAULT_SYSTEM_PROMPT;

            switch (stageKey.ToUpperInvariant())
            {
                case "CLASSIFY":
                    return PromptHandler.CLASSIFY_SYSTEM_PROMPT;
                case "DECOMPOSE":
                    return PromptHandler.DEFAULT_DECOMPOSE_SYSTEM_PROMPT;
                case "EXECUTE":
                    return PromptHandler.EXECUTE_SYSTEM_PROMPT;
                default:
                    return PromptHandler.DEFAULT_SYSTEM_PROMPT;
            }
        }

        private static string GetLocalSystemPromptForStage(string stageKey, string overridePrompt)
        {
            if (!string.IsNullOrWhiteSpace(overridePrompt))
                return overridePrompt;

            var key = string.IsNullOrWhiteSpace(stageKey) ? "EXECUTE" : stageKey.ToUpperInvariant();
            // Enforce PromptCatalog.json as the single source of truth for system prompts.
            // Do not consult environment variables for local system prompts.
            return GetDefaultSystemPromptForStage(key);
        }

        private static string GetRemoteSystemPromptForStage(string stageKey, string overridePrompt)
        {
            if (!string.IsNullOrWhiteSpace(overridePrompt))
                return overridePrompt;

            var key = string.IsNullOrWhiteSpace(stageKey) ? "EXECUTE" : stageKey.ToUpperInvariant();
            // Enforce PromptCatalog.json as the single source of truth for system prompts.
            // Do not consult environment variables for remote system prompts.
            return GetDefaultSystemPromptForStage(key);
        }

        private static string TryGetEnvironmentVariable(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var fromUser = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(fromUser))
                return fromUser;
            var fromProcess = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(fromProcess))
                return fromProcess;
            return null;
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
                           && string.Equals(_geminiModel, model, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(_geminiSystemPrompt ?? string.Empty, systemPrompt ?? string.Empty, StringComparison.Ordinal);
                if (!same)
                {
                    try { (_geminiClient as IDisposable)?.Dispose(); } catch { }
                    _geminiClient = new GeminiClient(key, model, systemPrompt);
                    _geminiKey = key; _geminiModel = model; _geminiSystemPrompt = systemPrompt ?? string.Empty;
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
                           && string.Equals(_groqModel, model, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(_groqSystemPrompt ?? string.Empty, systemPrompt ?? string.Empty, StringComparison.Ordinal);
                if (!same)
                {
                    try { (_groqClient as IDisposable)?.Dispose(); } catch { }
                    _groqClient = new GroqLlmClient(key, model, systemPrompt);
                    _groqKey = key; _groqModel = model; _groqSystemPrompt = systemPrompt ?? string.Empty;
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

        private static JObject TryBuildUbBoltTask(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest)) return null;
            if (!IsUBoltRequest(userRequest)) return null;

            var task = new JObject
            {
                ["feature_type"] = "u_bolt",
                ["intent"] = "create U-bolt",
                ["params"] = BuildUbBoltParamsFromText(userRequest)
            };
            return task;
        }

        private static bool IsUBoltRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\bu[\s-]?bolt\b", RegexOptions.IgnoreCase);
        }

        private static bool IsUBoltFeature(string featureType)
        {
            if (string.IsNullOrWhiteSpace(featureType)) return false;
            return featureType.Equals("u_bolt", StringComparison.OrdinalIgnoreCase)
                   || featureType.Equals("u-bolt", StringComparison.OrdinalIgnoreCase)
                   || featureType.Equals("ubolt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMaterialFeature(string featureType)
        {
            if (string.IsNullOrWhiteSpace(featureType)) return false;
            return featureType.Equals("material", StringComparison.OrdinalIgnoreCase)
                   || featureType.Equals("set_material", StringComparison.OrdinalIgnoreCase);
        }

        private static FeaturePlanResult BuildUbBoltPlan(JObject featureTask, JObject modelFacts)
        {
            var steps = new JArray();
            if (modelFacts == null)
            {
                steps.Add(new JObject { ["op"] = "new_part" });
            }

            var p = featureTask?["params"] as JObject;
            var dims = ResolveUbBoltDims(p);
            var plane = NormalizePlaneName(p?.Value<string>("plane"));

            steps.Add(new JObject { ["op"] = "select_plane", ["name"] = plane });
            steps.Add(new JObject { ["op"] = "sketch_begin" });
            steps.Add(new JObject
            {
                ["op"] = "line",
                ["x1"] = -dims.CenterlineRadiusMm,
                ["y1"] = 0,
                ["x2"] = -dims.CenterlineRadiusMm,
                ["y2"] = dims.LegLengthMm
            });
            steps.Add(new JObject
            {
                ["op"] = "line",
                ["x1"] = dims.CenterlineRadiusMm,
                ["y1"] = 0,
                ["x2"] = dims.CenterlineRadiusMm,
                ["y2"] = dims.LegLengthMm
            });
            steps.Add(new JObject
            {
                ["op"] = "arc",
                ["cx"] = 0,
                ["cy"] = 0,
                ["r"] = dims.CenterlineRadiusMm,
                ["start_angle"] = 180,
                ["end_angle"] = 360
            });
            steps.Add(new JObject { ["op"] = "auto_dimension" });
            steps.Add(new JObject { ["op"] = "sketch_end" });
            steps.Add(new JObject
            {
                ["op"] = "sweep",
                ["type"] = "circular",
                ["diameter"] = dims.RodDiameterMm
            });

            return new FeaturePlanResult
            {
                Steps = steps,
                Thinking = "Sketch a U-shaped path and sweep a circular profile to form the U-bolt."
            };
        }

        private static FeaturePlanResult BuildMaterialPlan(JObject featureTask, JObject modelFacts)
        {
            var material = featureTask?.Value<string>("material") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(material))
            {
                var intent = featureTask?.Value<string>("intent") ?? string.Empty;
                MaterialIntentParser.TryExtractMaterial(intent, out material);
            }

            if (string.IsNullOrWhiteSpace(material))
            {
                return new FeaturePlanResult
                {
                    ClarificationNeeded = true,
                    Clarification = new JObject
                    {
                        ["clarification_needed"] = true,
                        ["feature_index"] = featureTask?.Value<int?>("index") ?? 0,
                        ["feature_type"] = "material",
                        ["questions"] = new JArray("What material should be applied?")
                    },
                    Thinking = "Material name is missing."
                };
            }

            var steps = new JArray();
            if (modelFacts == null)
                steps.Add(new JObject { ["op"] = "new_part" });

            steps.Add(new JObject
            {
                ["op"] = "set_material",
                ["material"] = material
            });

            return new FeaturePlanResult
            {
                Steps = steps,
                Thinking = $"Apply material '{material}' to the active part."
            };
        }

        private static void CopyParamIfPresent(JObject src, JObject dst, string key)
        {
            if (src == null || dst == null || string.IsNullOrWhiteSpace(key)) return;
            if (src.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token) && token != null && token.Type != JTokenType.Null)
                dst[key] = token;
        }

        private static JObject BuildUbBoltParamsFromText(string text)
        {
            var result = new JObject();
            if (string.IsNullOrWhiteSpace(text)) return result;

            double? spacing = null;
            double? insideRadius = null;
            double? legLength = null;
            double? rodDiameter = null;

            var dnMatch = Regex.Match(text, @"\bDN\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (dnMatch.Success && double.TryParse(dnMatch.Groups[1].Value, out var dn))
            {
                spacing = dn;
                insideRadius = dn / 2.0;
            }

            var mMatch = Regex.Match(text, @"\bM\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (mMatch.Success && double.TryParse(mMatch.Groups[1].Value, out var msize))
            {
                rodDiameter = msize;
            }

            var spacingTagged = ExtractTaggedNumber(text, "spacing", "inside", "width");
            if (spacingTagged.HasValue) spacing = spacingTagged.Value;

            var insideTagged = ExtractTaggedNumber(text, "inside_radius", "inside_bend_radius", "bend_radius", "radius", "r");
            if (insideTagged.HasValue) insideRadius = insideTagged.Value;

            var lengthTagged = ExtractTaggedNumber(text, "leg_length", "leg", "length", "len");
            if (lengthTagged.HasValue) legLength = lengthTagged.Value;

            var diameterTagged = ExtractTaggedNumber(text, "rod_diameter", "rod", "diameter", "dia");
            if (diameterTagged.HasValue) rodDiameter = diameterTagged.Value;

            if (spacing.HasValue && !insideRadius.HasValue)
                insideRadius = spacing.Value / 2.0;
            if (insideRadius.HasValue && !spacing.HasValue)
                spacing = insideRadius.Value * 2.0;

            if (rodDiameter.HasValue) result["rod_diameter"] = rodDiameter.Value;
            if (legLength.HasValue) result["leg_length"] = legLength.Value;
            if (spacing.HasValue) result["spacing"] = spacing.Value;
            if (insideRadius.HasValue) result["inside_bend_radius"] = insideRadius.Value;

            return result;
        }

        private static double? ExtractTaggedNumber(string text, params string[] tags)
        {
            if (string.IsNullOrWhiteSpace(text) || tags == null || tags.Length == 0) return null;
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var pattern = $@"\b{Regex.Escape(tag)}\s*[:=]?\s*(\d+(?:\.\d+)?)";
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                if (double.TryParse(match.Groups[1].Value, out var value))
                    return value;
            }
            return null;
        }

        private static (double RodDiameterMm, double LegLengthMm, double SpacingMm, double InsideRadiusMm, double CenterlineRadiusMm) ResolveUbBoltDims(JObject parameters)
        {
            double rodDiameterMm = parameters?.Value<double?>("rod_diameter") ?? 10.0;
            double legLengthMm = parameters?.Value<double?>("leg_length") ?? 50.0;
            double spacingMm = parameters?.Value<double?>("spacing") ?? double.NaN;
            double insideRadiusMm = parameters?.Value<double?>("inside_bend_radius") ?? double.NaN;

            bool spacingProvided = !double.IsNaN(spacingMm) && spacingMm > 0;
            bool insideProvided = !double.IsNaN(insideRadiusMm) && insideRadiusMm > 0;

            if (!spacingProvided && insideProvided)
                spacingMm = insideRadiusMm * 2.0;
            if (!insideProvided && spacingProvided)
                insideRadiusMm = spacingMm / 2.0;
            if (!spacingProvided && !insideProvided)
            {
                spacingMm = 40.0;
                insideRadiusMm = spacingMm / 2.0;
            }

            var centerlineRadiusMm = (spacingMm + rodDiameterMm) / 2.0;
            return (rodDiameterMm, legLengthMm, spacingMm, insideRadiusMm, centerlineRadiusMm);
        }

        private static string NormalizePlaneName(string planeName)
        {
            if (string.IsNullOrWhiteSpace(planeName)) return "Top Plane";
            var trimmed = planeName.Trim();
            if (trimmed.Equals("Top", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Top Plane", StringComparison.OrdinalIgnoreCase))
                return "Top Plane";
            if (trimmed.Equals("Right", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Right Plane", StringComparison.OrdinalIgnoreCase))
                return "Right Plane";
            if (trimmed.Equals("Front", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Front Plane", StringComparison.OrdinalIgnoreCase))
                return "Front Plane";
            return "Top Plane";
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

        private static bool IsPromptCatalogFatal(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is InvalidOperationException || cur is System.IO.FileNotFoundException)
                    return true;
            }
            return false;
        }
    }
}
