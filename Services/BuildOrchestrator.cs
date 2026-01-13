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

        public BuildResult Run(string userPrompt, IReadOnlyCollection<string> categories, bool useFewShot, int maxFewShotCount, string runId, Func<JObject, string, string, StepExecutionResult> executePlan)
        {
            var result = new BuildResult();
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                result.Success = false;
                result.Error = "Empty prompt";
                return result;
            }

            var llmSw = Stopwatch.StartNew();
            var classifyReqId = Guid.NewGuid().ToString("N");
            var classify = LlmPlanService.ClassifyAndDescribe(userPrompt, categories, runId, classifyReqId, 25);
            result.Category = classify?.Category ?? "Unknown";
            result.Description = classify?.Description ?? string.Empty;

            var decomposeReqId = Guid.NewGuid().ToString("N");
            AddinStatusLogger.Log("BuildOrchestrator", $"run={runId} req={decomposeReqId} stage=decompose_start");
            var tasks = LlmPlanService.DecomposeByFeature(userPrompt, runId, decomposeReqId);
            if (tasks == null || tasks.Count == 0)
            {
                result.Success = false;
                result.Error = "Decompose returned no tasks";
                result.FeatureTasks = tasks ?? new JArray();
                result.LlmMs = llmSw.ElapsedMilliseconds;
                return result;
            }

            result.FeatureTasks = tasks;
            JObject modelFacts = null;
            for (int ti = 0; ti < tasks.Count; ti++)
            {
                var task = tasks[ti] as JObject ?? new JObject();
                NormalizeTaskParams(task);
                var reqId = Guid.NewGuid().ToString("N");
                var featureType = task.Value<string>("feature_type") ?? "feature";
                var intent = task.Value<string>("intent") ?? string.Empty;
                AddinStatusLogger.Log("BuildOrchestrator", $"run={runId} req={reqId} feature_index={ti} feature_type={featureType} intent={intent}");

                var fewShot = useFewShot ? FewShotSelector.SelectFeatureFewShot(task, _goodStore, _stepStore, maxFewShotCount) : null;
                AddinStatusLogger.Log("BuildOrchestrator", $"run={runId} req={reqId} feature_index={ti} fewshot_len={(fewShot ?? string.Empty).Length}");

                var plan = LlmPlanService.PlanFeatureSubtask(task, modelFacts, fewShot, runId, reqId);
                if (plan == null || plan.Steps == null || plan.Steps.Count == 0)
                {
                    result.Success = false;
                    result.Error = "Feature task expansion failed";
                    result.FailedTaskIndex = ti;
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

                var exec = executePlan?.Invoke(perPlan, runId, reqId);
                result.Execution = exec;
                if (exec == null || !exec.Success)
                {
                    PopulateFailureContext(result, exec, ti, runId);
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
                        result.LlmMs = llmSw.ElapsedMilliseconds;
                        return result;
                    }
                }

                modelFacts = ModelStateProvider.Capture(_swApp, emitLogs: false);
                result.ModelState = modelFacts;
                AddinStatusLogger.Log("BuildOrchestrator", $"run={runId} req={reqId} feature_index={ti} model_state_updated={(modelFacts != null)}");
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
                    AddinStatusLogger.Log("BuildOrchestrator", $"run={runId} gate=task0 rebuild_ok=false");
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
                AddinStatusLogger.Log("BuildOrchestrator", $"run={runId} gate=task0 bodies={hasBodies} faces={totalFaces} boss={hasBoss}");
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
                AddinStatusLogger.Log("BuildOrchestrator",
                    $"run={runId} failedTaskIndex={result.FailedTaskIndex} failedStepIndex={result.FailedStepIndex} lastOp={result.LastOp} bodies={(bodies == null ? 0 : bodies.Count)} total_faces={totalFaces}");
            }
            catch { }
        }
    }
}
