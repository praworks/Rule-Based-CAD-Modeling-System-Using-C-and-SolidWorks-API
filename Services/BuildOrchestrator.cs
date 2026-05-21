using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using AICAD.Services.Logging;

namespace AICAD.Services
{
    internal sealed class BuildOrchestrator
    {
        public sealed class BuildResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public JArray FeatureTasks { get; set; }
            public StepExecutionResult Execution { get; set; }
            public JObject ModelState { get; set; }
            public JObject ExecutedPlan { get; set; }
            public int? FailedTaskIndex { get; set; }
            public int? FailedStepIndex { get; set; }
            public string LastOp { get; set; }
            public JObject FailedModelState { get; set; }
            public long LlmMs { get; set; }
        }

        private readonly ISldWorks _swApp;
        private readonly IGoodFeedbackStore _goodStore;
        private readonly IStepStore _stepStore;
        private readonly ILogger<BuildOrchestrator> _logger;
        private readonly ITelemetrySink _telemetry;

        public BuildOrchestrator(ISldWorks swApp, IGoodFeedbackStore goodStore, IStepStore stepStore, ILogger<BuildOrchestrator> logger = null, ITelemetrySink telemetrySink = null)
        {
            _swApp = swApp;
            _goodStore = goodStore;
            _stepStore = stepStore;
            _logger = logger ?? LoggerFactoryBuilder.CreateLogger<BuildOrchestrator>();
            _telemetry = telemetrySink ?? LoggerFactoryBuilder.TelemetrySink;
        }

        public BuildResult Run(string userPrompt, IReadOnlyCollection<string> categories, bool useFewShot, int maxFewShotCount, string runId, Func<JObject, string, string, StepExecutionResult> executePlan, DiagnosticLogSettings settings = null)
        {
            var result = new BuildResult();
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                result.Success = false;
                result.Error = "Empty prompt";
                return result;
            }
            var originalUserPrompt = userPrompt;
            userPrompt = NormalizeUserPrompt(userPrompt);
            var llmUserPrompt = userPrompt;

            var correlationId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId;
            var context = new LoggingContext
            {
                CorrelationId = correlationId,
                SessionId = GetSessionId(),
                DocumentId = GetDocumentId(),
                Operation = "Build"
            };
            LoggingContext.Current = context;
            using (_logger.BeginScope(context.ToScopeDictionary()))
            {
                var effectiveRunId = correlationId;
                var llmSw = Stopwatch.StartNew();
                var classifyTimeout = (settings?.ClassifyTimeoutSeconds ?? 25);
                var decomposeTimeout = (settings?.DecomposeTimeoutSeconds ?? 120);
                var expandTimeout = (settings?.ExpandTimeoutSeconds ?? 120);
                var fewShotEnabled = settings?.FewShotEnabled ?? useFewShot;
                using (var buildOp = OperationLogger.Start(_logger, _telemetry, context, "Build"))
                {
                    try
                    {
                try
                {
                    var fastenerLookup = FastenerInternetLookupService.TryEnrichPrompt(userPrompt);
                    if (fastenerLookup != null && fastenerLookup.Applied && !string.IsNullOrWhiteSpace(fastenerLookup.EnrichedPrompt))
                    {
                        llmUserPrompt = fastenerLookup.EnrichedPrompt;
                        _logger.LogWithContext(LogLevel.Information, context, fastenerLookup.Summary ?? "Fastener lookup applied.");
                    }
                    else if (!string.IsNullOrWhiteSpace(fastenerLookup?.Summary))
                    {
                        _logger.LogWithContext(LogLevel.Debug, context, fastenerLookup.Summary);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWithContext(LogLevel.Warning, context, $"Fastener internet lookup failed: {ex.Message}");
                }
                try
                {
                    var providerPriority = settings?.ProviderPriority ?? string.Join(",", ProviderRouter.GetFallbackOrder());
                    _logger.LogWithContext(LogLevel.Information, context, $"Settings loaded provider_priority={providerPriority} classify_timeout={classifyTimeout}s decompose_timeout={decomposeTimeout}s expand_timeout={expandTimeout}s few_shot={fewShotEnabled}");
                }
                catch { }
                context.Stage = "DECOMPOSE";
                var decomposeReqId = Guid.NewGuid().ToString("N");
                DiagnosticLogWriter.StartSection(effectiveRunId, "DECOMPOSE");
                var decomposeCtx = context.CloneForChild("Decompose");
                LlmPlanService.DecomposeResult decomposeResult = null;
                using (_logger.BeginScope(decomposeCtx.ToScopeDictionary()))
                {
                    var decomposeOp = OperationLogger.Start(_logger, _telemetry, decomposeCtx, "Decompose");
                    _logger.LogWithContext(LogLevel.Information, decomposeCtx, "Decompose start");
                    decomposeResult = LlmPlanService.DecomposeByFeature(llmUserPrompt, effectiveRunId, decomposeReqId, decomposeTimeout);
                    decomposeOp.MarkSuccess();
                }
                if (decomposeResult == null)
                {
                    result.Success = false;
                    result.Error = "Decompose returned no data";
                    result.FeatureTasks = new JArray();
                    _logger.LogWithContext(LogLevel.Error, decomposeCtx, "Decompose returned no data");
                    result.LlmMs = llmSw.ElapsedMilliseconds;
                    buildOp.MarkFailure(null, result.Error, userVisible: true);
                    return result;
                }
                if (decomposeResult.NeedsDescription)
                {
                    result.Success = false;
                    var question = string.IsNullOrWhiteSpace(decomposeResult.Question) ? "Provide a short description of the request." : decomposeResult.Question;
                    result.Error = question;
                    result.FeatureTasks = new JArray();
                    result.Execution = new StepExecutionResult
                    {
                        Success = false,
                        Clarification = new JObject
                        {
                            ["clarification_needed"] = true,
                            ["feature_index"] = 0,
                            ["feature_type"] = "decompose",
                            ["questions"] = new JArray(question)
                        }
                    };
                    _logger.LogWithContext(LogLevel.Warning, decomposeCtx, "Decompose requested clarification: " + question);
                    result.LlmMs = llmSw.ElapsedMilliseconds;
                    buildOp.MarkFailure(null, result.Error, userVisible: true);
                    return result;
                }
                var tasks = decomposeResult.Features ?? new JArray();
                AppendMaterialTaskFromPrompt(tasks, userPrompt, decomposeCtx);
                EnsureTaskIndexes(tasks);
                if (tasks.Count == 0)
                {
                    result.Success = false;
                    result.Error = "Decompose returned no tasks";
                    result.FeatureTasks = tasks;
                    _logger.LogWithContext(LogLevel.Error, decomposeCtx, "Decompose returned no tasks");
                    result.LlmMs = llmSw.ElapsedMilliseconds;
                    buildOp.MarkFailure(null, result.Error, userVisible: true);
                    return result;
                }

                result.FeatureTasks = tasks;
                var executedSteps = new JArray();
                result.Description = decomposeResult.Description ?? string.Empty;
                result.Category = "Unknown";
                context.Stage = "EXECUTE";
                DiagnosticLogWriter.StartSection(effectiveRunId, "EXECUTE");
                        JObject modelFacts = null;
                        for (int ti = 0; ti < tasks.Count; ti++)
                        {
                            var task = tasks[ti] as JObject ?? new JObject();
                            NormalizeTaskParams(task);
                            var reqId = Guid.NewGuid().ToString("N");
                            var featureType = task.Value<string>("feature_type") ?? "feature";
                            var intent = task.Value<string>("intent") ?? string.Empty;
                            var featureCtx = context.CloneForChild("ExecuteFeature", featureType);
                            featureCtx.Provider = featureType;
                            featureCtx.Operation = $"Feature-{featureType}";
                            featureCtx.ParentId = correlationId;
                            DiagnosticLogWriter.FeatureHeader(effectiveRunId, ti, featureType);
                            _logger.LogWithContext(LogLevel.Information, featureCtx, $"Feature start index={ti} feature_type={featureType} intent={LogRedactor.Sanitize(intent)}");

                            string fewShotExamples = null;
                            if (fewShotEnabled && maxFewShotCount > 0)
                            {
                                try
                                {
                                    fewShotExamples = FewShotSelector.SelectFeatureFewShot(task, _goodStore, _stepStore, maxFewShotCount);
                                    var exampleCount = CountFewShotExamples(fewShotExamples);
                                    _logger.LogWithContext(LogLevel.Information, featureCtx, $"FewShot enabled example_count={exampleCount} max={maxFewShotCount}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWithContext(LogLevel.Warning, featureCtx, $"FewShot selection failed: {ex.Message}");
                                }
                            }
                            else
                            {
                                _logger.LogWithContext(LogLevel.Debug, featureCtx, "FewShot disabled for plan stage");
                            }

                            _logger.LogWithContext(LogLevel.Information, featureCtx, $"Build prompt model_state={(modelFacts != null)}");
                        var plan = LlmPlanService.PlanFeatureSubtask(task, modelFacts, effectiveRunId, reqId, expandTimeout, fewShotExamples);
                        if (plan == null)
                        {
                            result.Success = false;
                            result.Error = "Feature task expansion failed";
                            result.FailedTaskIndex = ti;
                            _logger.LogWithContext(LogLevel.Error, featureCtx, "Feature task expansion failed");
                            result.LlmMs = llmSw.ElapsedMilliseconds;
                            buildOp.MarkFailure(null, result.Error, userVisible: true);
                            return result;
                        }
                        if (plan.ClarificationNeeded)
                        {
                            var clarification = plan.Clarification ?? new JObject();
                            result.Success = false;
                            result.Error = BuildClarificationMessage(clarification) ?? "Clarification required for feature.";
                            result.Execution = new StepExecutionResult { Success = false, Clarification = clarification };
                            result.FailedTaskIndex = ti;
                            _logger.LogWithContext(LogLevel.Warning, featureCtx, "Clarification needed for feature index=" + ti);
                            result.LlmMs = llmSw.ElapsedMilliseconds;
                            buildOp.MarkFailure(null, result.Error, userVisible: true);
                            return result;
                        }
                        if (plan.Steps == null || plan.Steps.Count == 0)
                        {
                            result.Success = false;
                            result.Error = "Feature task expansion returned no steps";
                            result.FailedTaskIndex = ti;
                            _logger.LogWithContext(LogLevel.Error, featureCtx, "Feature task expansion returned no steps");
                            result.LlmMs = llmSw.ElapsedMilliseconds;
                            buildOp.MarkFailure(null, result.Error, userVisible: true);
                            return result;
                        }

                        var perPlan = new JObject
                        {
                            ["steps"] = plan.Steps,
                            ["__llm_prompt"] = "feature_plan",
                            ["__llm_raw"] = string.Empty,
                            ["__user_prompt"] = originalUserPrompt ?? string.Empty
                        };

                        _logger.LogWithContext(LogLevel.Information, featureCtx, "Execute start");
                        var exec = executePlan?.Invoke(perPlan, effectiveRunId, reqId);
                        result.Execution = exec;
                        if (exec == null || !exec.Success)
                        {
                            PopulateFailureContext(result, exec, ti, effectiveRunId);
                            _logger.LogWithContext(LogLevel.Error, featureCtx, "Gate proceed=false reason=execution failed");
                            result.LlmMs = llmSw.ElapsedMilliseconds;
                            buildOp.MarkFailure(null, "Execution failed", userVisible: true);
                            return result;
                        }
                        foreach (var step in plan.Steps)
                        {
                            executedSteps.Add(step.DeepClone());
                        }
                        result.ExecutedPlan = new JObject
                        {
                            ["steps"] = executedSteps,
                            ["features"] = tasks.DeepClone(),
                            ["description"] = result.Description ?? string.Empty,
                            ["user_prompt"] = originalUserPrompt ?? string.Empty
                        };

                            // Gate Task0 before proceeding to Task1+
                            if (ti == 0)
                            {
                                if (!ValidateBaseGeometry(effectiveRunId))
                                {
                                    result.Success = false;
                                    result.Error = "Task0 gating failed: base geometry not found after rebuild";
                                    PopulateFailureContext(result, exec, ti, effectiveRunId);
                                    _logger.LogWithContext(LogLevel.Error, featureCtx, "Gate proceed=false reason=Task0 base geometry missing");
                                    result.LlmMs = llmSw.ElapsedMilliseconds;
                                    buildOp.MarkFailure(null, result.Error, userVisible: true);
                                    return result;
                                }
                                _logger.LogWithContext(LogLevel.Information, featureCtx, "Gate proceed=true reason=Task0 geometry validated");
                            }
                            else
                            {
                                _logger.LogWithContext(LogLevel.Information, featureCtx, "Gate proceed=true reason=feature complete");
                            }

                            modelFacts = ModelStateProvider.Capture(_swApp, emitLogs: false);
                            result.ModelState = modelFacts;
                            _logger.LogWithContext(LogLevel.Debug, featureCtx, $"ModelState updated={(modelFacts != null)}");
                        }

                        result.Success = true;
                        result.LlmMs = llmSw.ElapsedMilliseconds;
                        buildOp.MarkSuccess();
                        return result;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.Error = ex.Message;
                        _logger.LogException(context, ex, "Build orchestrator failed", userVisible: true);
                        buildOp.MarkFailure(ex, "Unhandled build failure", userVisible: true);
                        return result;
                    }
                }
            }
        }

        private bool ValidateBaseGeometry(string runId)
        {
            try
            {
                var doc = _swApp?.ActiveDoc as IModelDoc2;
                if (doc == null) return false;
                bool rebuildOk = false;
                try { rebuildOk = doc.ForceRebuild3(false); } catch { rebuildOk = false; }
                if (!rebuildOk)
                {
                    DiagnosticLogWriter.LogLine(runId, null, "BuildOrchestrator", "ERROR", "Gate rebuild_ok=false");
                    return false;
                }

                var state = ModelStateProvider.Capture(_swApp, emitLogs: false);
                if (state == null) return false;
                var bodies = state["bodies"] as JArray;
                var totalFaces = state["total_faces"]?.Value<int>() ?? 0;
                var hasBodies = bodies != null && bodies.Count > 0;
                var hasFaces = totalFaces > 0;
                var features = state["features"] as JArray;
                var hasBoss = features != null && features.Any(f =>
                {
                    var name = f?["name"]?.ToString() ?? string.Empty;
                    var type = f?["type"]?.ToString() ?? string.Empty;
                    return name.StartsWith("Boss-Extrude", StringComparison.OrdinalIgnoreCase)
                           || type.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0;
                });
                _logger.LogInformation("Gate check bodies={HasBodies} faces={TotalFaces} boss={HasBoss}", hasBodies, totalFaces, hasBoss);
                return hasBodies || hasFaces || hasBoss;
            }
            catch { return false; }
        }

        private static void NormalizeTaskParams(JObject task)
        {
            if (task == null) return;
            var paramsObj = task["params"] as JObject;
            if (paramsObj == null) return;
            if (paramsObj["op"] == null && paramsObj["type"] != null)
                paramsObj["op"] = paramsObj["type"];
        }

        private static void EnsureTaskIndexes(JArray tasks)
        {
            if (tasks == null) return;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i] is JObject task)
                    task["index"] = i;
            }
        }

        private static int CountFewShotExamples(string fewShotExamples)
        {
            if (string.IsNullOrWhiteSpace(fewShotExamples))
                return 0;

            return Regex.Matches(fewShotExamples, @"\bInput:", RegexOptions.IgnoreCase).Count;
        }

        private static bool HasMaterialTask(JArray tasks)
        {
            if (tasks == null) return false;
            foreach (var token in tasks)
            {
                var task = token as JObject;
                if (task == null) continue;
                var featureType = task.Value<string>("feature_type") ?? string.Empty;
                if (featureType.Equals("material", StringComparison.OrdinalIgnoreCase)
                    || featureType.Equals("set_material", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void AppendMaterialTaskFromPrompt(JArray tasks, string userPrompt, LoggingContext logContext)
        {
            if (tasks == null || HasMaterialTask(tasks)) return;
            if (!MaterialIntentParser.TryExtractMaterial(userPrompt, out var material)) return;

            var dependsOn = new JArray();
            if (tasks.Count > 0)
                dependsOn.Add(tasks.Count - 1);

            tasks.Add(new JObject
            {
                ["feature_type"] = "material",
                ["role"] = "dependent",
                ["intent"] = $"set material {material}",
                ["material"] = material,
                ["depends_on"] = dependsOn
            });

            try
            {
                _logger.LogWithContext(LogLevel.Information, logContext, $"Appended synthetic material task from prompt material={material}");
            }
            catch { }
        }

        private void PopulateFailureContext(BuildResult result, StepExecutionResult exec, int taskIndex, string runId)
        {
            result.Success = false;
            result.FailedTaskIndex = taskIndex;
            result.Execution = exec;
            result.FailedModelState = ModelStateProvider.Capture(_swApp, emitLogs: false);
            if (exec == null || exec.Log == null || exec.Log.Count == 0)
                return;

            var last = exec.Log.LastOrDefault(l => l?["success"]?.Value<bool>() == false) ?? exec.Log.LastOrDefault();
            if (last != null)
            {
                result.FailedStepIndex = last["step"]?.Value<int>();
                result.LastOp = last["op"]?.ToString();
            }
            try
            {
                var bodies = result.FailedModelState?["bodies"] as JArray;
                var totalFaces = result.FailedModelState?["total_faces"]?.Value<int>() ?? 0;
                _logger.LogWithContext(LogLevel.Error, new LoggingContext { CorrelationId = runId, Operation = "FailureContext" },
                    $"Failure context failedTaskIndex={result.FailedTaskIndex} failedStepIndex={result.FailedStepIndex} lastOp={result.LastOp} bodies={(bodies == null ? 0 : bodies.Count)} total_faces={totalFaces}");
            }
            catch { }
        }

        private string GetSessionId()
        {
            try
            {
                return System.Environment.MachineName;
            }
            catch
            {
                return "session";
            }
        }

        private string GetDocumentId()
        {
            try
            {
                var doc = _swApp?.ActiveDoc as IModelDoc2;
                return doc?.GetTitle() ?? "unknown_doc";
            }
            catch
            {
                return "unknown_doc";
            }
        }

        private static string BuildClarificationMessage(JObject clarification)
        {
            if (clarification == null) return null;
            try
            {
                var questions = clarification["questions"] as JArray;
                if (questions != null && questions.Count > 0)
                    return string.Join(" ", questions.Values<string>());
            }
            catch { }
            try
            {
                var question = clarification.Value<string>("question");
                if (!string.IsNullOrWhiteSpace(question))
                    return question;
            }
            catch { }
            return clarification.ToString();
        }

        private static string NormalizeUserPrompt(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
                return userPrompt ?? string.Empty;

            return Regex.Replace(
                userPrompt,
                @"\b(clyinder|cilinder|cylnder|cylider|cyclinder)\b",
                "cylinder",
                RegexOptions.IgnoreCase);
        }
    }
}
