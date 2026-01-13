using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

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
            public int? FailedTaskIndex { get; set; }
            public int? FailedStepIndex { get; set; }
            public string LastOp { get; set; }
            public JObject FailedModelState { get; set; }
            public long LlmMs { get; set; }
        }

        private readonly ISldWorks _swApp;
        private readonly IGoodFeedbackStore _goodStore;
        private readonly IStepStore _stepStore;

        public BuildOrchestrator(ISldWorks swApp, IGoodFeedbackStore goodStore, IStepStore stepStore)
        {
            _swApp = swApp;
            _goodStore = goodStore;
            _stepStore = stepStore;
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

            var llmSw = Stopwatch.StartNew();
            var classifyTimeout = (settings?.ClassifyTimeoutSeconds ?? 25);
            var decomposeTimeout = (settings?.DecomposeTimeoutSeconds ?? 120);
            var expandTimeout = (settings?.ExpandTimeoutSeconds ?? 120);
            var fewShotEnabled = settings?.FewShotEnabled ?? useFewShot;
            try
            {
                var providerPriority = settings?.ProviderPriority ?? string.Join(",", ProviderRouter.GetFallbackOrder());
                DiagnosticLogWriter.LogLine(runId, null, "BuildOrchestrator", "INFO",
                    $"STEP 5 Settings loaded provider_priority={providerPriority} classify_timeout={classifyTimeout}s decompose_timeout={decomposeTimeout}s expand_timeout={expandTimeout}s few_shot={fewShotEnabled}");
            }
            catch { }
            var classifyReqId = Guid.NewGuid().ToString("N");
            DiagnosticLogWriter.StartSection(runId, "CLASSIFY");
            DiagnosticLogWriter.LogLine(runId, classifyReqId, "BuildOrchestrator", "INFO", "STEP 6 Classify start");
            var classify = LlmPlanService.ClassifyAndDescribe(userPrompt, categories, runId, classifyReqId, classifyTimeout);
            result.Category = classify?.Category ?? "Unknown";
            result.Description = classify?.Description ?? string.Empty;

            var decomposeReqId = Guid.NewGuid().ToString("N");
            DiagnosticLogWriter.StartSection(runId, "DECOMPOSE");
            DiagnosticLogWriter.LogLine(runId, decomposeReqId, "BuildOrchestrator", "INFO", "STEP 7 Decompose start");
            var tasks = LlmPlanService.DecomposeByFeature(userPrompt, runId, decomposeReqId, decomposeTimeout);
            if (tasks == null || tasks.Count == 0)
            {
                result.Success = false;
                result.Error = "Decompose returned no tasks";
                result.FeatureTasks = tasks ?? new JArray();
                DiagnosticLogWriter.LogLine(runId, decomposeReqId, "BuildOrchestrator", "ERROR", "Decompose returned no tasks");
                result.LlmMs = llmSw.ElapsedMilliseconds;
                return result;
            }

            result.FeatureTasks = tasks;
            DiagnosticLogWriter.StartSection(runId, "EXECUTE");
            JObject modelFacts = null;
            for (int ti = 0; ti < tasks.Count; ti++)
            {
                var task = tasks[ti] as JObject ?? new JObject();
                NormalizeTaskParams(task);
                var reqId = Guid.NewGuid().ToString("N");
                var featureType = task.Value<string>("feature_type") ?? "feature";
                var intent = task.Value<string>("intent") ?? string.Empty;
                DiagnosticLogWriter.FeatureHeader(runId, ti, featureType);
                DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", $"STEP 8 Feature start index={ti} feature_type={featureType} intent={intent}");

                var fewShot = fewShotEnabled ? FewShotSelector.SelectFeatureFewShot(task, _goodStore, _stepStore, maxFewShotCount) : null;
                DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", $"STEP 8.1 FewShot selected length={(fewShot ?? string.Empty).Length}");

                DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", $"STEP 8.2 Build prompt model_state={(modelFacts != null)}");
                DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", "STEP 8.3 Expand start");
                var plan = LlmPlanService.PlanFeatureSubtask(task, modelFacts, fewShot, runId, reqId, expandTimeout);
                if (plan == null || plan.Steps == null || plan.Steps.Count == 0)
                {
                    result.Success = false;
                    result.Error = "Feature task expansion failed";
                    result.FailedTaskIndex = ti;
                    DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "ERROR", "Feature task expansion failed");
                    result.LlmMs = llmSw.ElapsedMilliseconds;
                    return result;
                }

                var perPlan = new JObject
                {
                    ["steps"] = plan.Steps,
                    ["__llm_prompt"] = "feature_plan",
                    ["__llm_raw"] = string.Empty,
                    ["__user_prompt"] = userPrompt ?? string.Empty
                };

                DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", "STEP 8.4 Execute start");
                var exec = executePlan?.Invoke(perPlan, runId, reqId);
                result.Execution = exec;
                if (exec == null || !exec.Success)
                {
                    PopulateFailureContext(result, exec, ti, runId);
                    DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "ERROR", "Gate proceed=false reason=execution failed");
                    result.LlmMs = llmSw.ElapsedMilliseconds;
                    return result;
                }

                // Gate Task0 before proceeding to Task1+
                if (ti == 0)
                {
                    if (!ValidateBaseGeometry(runId))
                    {
                        result.Success = false;
                        result.Error = "Task0 gating failed: base geometry not found after rebuild";
                        PopulateFailureContext(result, exec, ti, runId);
                        DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "ERROR", "Gate proceed=false reason=Task0 base geometry missing");
                        result.LlmMs = llmSw.ElapsedMilliseconds;
                        return result;
                    }
                    DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", "Gate proceed=true reason=Task0 geometry validated");
                }
                else
                {
                    DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", "Gate proceed=true reason=feature complete");
                }

                modelFacts = ModelStateProvider.Capture(_swApp, emitLogs: false);
                result.ModelState = modelFacts;
                DiagnosticLogWriter.LogLine(runId, reqId, "BuildOrchestrator", "INFO", $"STEP 8.5 ModelState updated={(modelFacts != null)}");
            }

            result.Success = true;
            result.LlmMs = llmSw.ElapsedMilliseconds;
            return result;
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
                DiagnosticLogWriter.LogLine(runId, null, "BuildOrchestrator", "INFO", $"Gate check bodies={hasBodies} faces={totalFaces} boss={hasBoss}");
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
                DiagnosticLogWriter.LogLine(runId, null, "BuildOrchestrator", "ERROR",
                    $"Failure context failedTaskIndex={result.FailedTaskIndex} failedStepIndex={result.FailedStepIndex} lastOp={result.LastOp} bodies={(bodies == null ? 0 : bodies.Count)} total_faces={totalFaces}");
            }
            catch { }
        }
    }
}
