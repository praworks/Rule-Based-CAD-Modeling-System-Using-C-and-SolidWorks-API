using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using AICAD.Services.Operations;

namespace AICAD.Services
{
    internal class StepExecutionResult
    {
        public bool Success { get; set; }
        public List<JObject> Log { get; } = new List<JObject>();
        public bool CreatedNewPart { get; set; }
        public string ModelTitle { get; set; }
        // If the executor encountered a request for user clarification (from LLM),
        // this will contain the structured clarification object (e.g. { clarification_needed: "..." }).
        public JObject Clarification { get; set; }
        /// <summary>Validation results for each step (post-execution geometry checks)</summary>
        public List<ExecutionValidator.ValidationResult> Validations { get; } = new List<ExecutionValidator.ValidationResult>();
        /// <summary>Overall validation report</summary>
        public JObject ValidationReport { get; set; }
    }

    internal static class StepExecutor
    {
        private static readonly OperationRegistry _operationRegistry = OperationRegistry.CreateDefault();

        private static JObject BuildLlmRepairContext(JObject plan, int stepIndex, string op, string error, object data)
        {
            var ctx = new JObject
            {
                ["error"] = error ?? string.Empty,
                ["message"] = error ?? string.Empty,
                ["step_index"] = stepIndex,
                ["op"] = op ?? string.Empty
            };

            try
            {
                if (data != null)
                    ctx["data"] = JToken.FromObject(data);
            }
            catch { }

            try
            {
                var llmRaw = plan?["__llm_raw"]?.ToString();
                var llmPrompt = plan?["__llm_prompt"]?.ToString();
                var userPrompt = plan?["__user_prompt"]?.ToString();
                var thinking = plan?["thinking"]?.ToString();
                if (!string.IsNullOrWhiteSpace(llmRaw)) ctx["llm_raw"] = llmRaw;
                if (!string.IsNullOrWhiteSpace(llmPrompt)) ctx["llm_prompt"] = llmPrompt;
                if (!string.IsNullOrWhiteSpace(userPrompt)) ctx["user_prompt"] = userPrompt;
                if (!string.IsNullOrWhiteSpace(thinking)) ctx["thinking"] = thinking;
            }
            catch { }

            return ctx;
        }

        /// <summary>
        /// Execute a plan with multiple steps using the operation handler registry
        /// </summary>
        public static StepExecutionResult Execute(JObject plan, ISldWorks swApp, Action<int, string, int?> progressCallback = null, bool continueOnError = false, bool preservePartsOnErrorOverride = false, string runId = null, string requestId = null)
        {
            var result = new StepExecutionResult();
            // Preserve newly-created parts on error for interactive diagnostics by default.
            // Set environment variable AICAD_PRESERVE_PARTS_ON_ERROR=0 (Process or User) to disable,
            // or =1 to explicitly enable.
            bool preservePartsOnError = true;
            try
            {
                // honor explicit override first (force enable)
                if (preservePartsOnErrorOverride) preservePartsOnError = true;
                var env = System.Environment.GetEnvironmentVariable("AICAD_PRESERVE_PARTS_ON_ERROR", System.EnvironmentVariableTarget.Process)
                          ?? System.Environment.GetEnvironmentVariable("AICAD_PRESERVE_PARTS_ON_ERROR", System.EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    if (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase)) preservePartsOnError = true;
                    if (env == "0" || env.Equals("false", StringComparison.OrdinalIgnoreCase)) preservePartsOnError = false;
                }
            }
            catch { }
            try { AddinStatusLogger.Log("StepExecutor", $"run={runId} req={requestId} Execute invoked with plan keys={string.Join(",", plan?.Properties().Select(p=>p.Name) ?? new string[0])}"); } catch { }
            if (swApp == null)
            {
        result.Log.Add(new JObject { ["step"] = -1, ["op"] = "init", ["success"] = false, ["error"] = "SOLIDWORKS app not available" });
                result.Success = false;
                return result;
            }

                try
            {
                var steps = plan.ContainsKey("steps") && plan["steps"] is JArray ? (JArray)plan["steps"] : new JArray();
                    try { AddinStatusLogger.Log("StepExecutor", $"run={runId} req={requestId} Execute resolved {steps.Count} steps"); } catch { }

                // Flatten feature-wrapped steps: some LLMs return an array of feature objects
                // where each feature contains its own 'steps' array. Convert those into a
                // flat sequence of executable step objects so the executor sees ops.
                try
                {
                    for (int idx = 0; idx < steps.Count; )
                    {
                        var item = steps[idx];
                        if (item != null && item.Type == JTokenType.Object)
                        {
                            var obj = (JObject)item;
                            if (obj.Property("steps") != null && obj["steps"] is JArray innerArr)
                            {
                                // splice inner steps in-place, replacing the wrapper
                                steps.RemoveAt(idx);
                                for (int j = innerArr.Count - 1; j >= 0; j--)
                                {
                                    steps.Insert(idx, innerArr[j]);
                                }
                                // do not advance idx so newly inserted items are processed
                                continue;
                            }
                        }
                        idx++;
                    }
                }
                catch { }
                // Control LLM repair via env var: AICAD_ENABLE_LLM_REPAIR (default on).
                bool enableLlmRepair = true;
                try
                {
                    var envRepair = System.Environment.GetEnvironmentVariable("AICAD_ENABLE_LLM_REPAIR", System.EnvironmentVariableTarget.Process)
                                    ?? System.Environment.GetEnvironmentVariable("AICAD_ENABLE_LLM_REPAIR", System.EnvironmentVariableTarget.User);
                    if (!string.IsNullOrWhiteSpace(envRepair) &&
                        (envRepair == "0" || envRepair.Equals("false", StringComparison.OrdinalIgnoreCase)))
                    {
                        enableLlmRepair = false;
                    }
                }
                catch { }

                // Declare document/context variables early for execution context.
                IModelDoc2 model = null;
                ISketchManager sketchMgr = null;
                IFeatureManager featMgr = null;
                bool inSketch = false;

                bool perStepCanonicalize = false;
                if (enableLlmRepair)
                {
                    try
                    {
                        var envCanon = System.Environment.GetEnvironmentVariable("AICAD_PER_STEP_CANONICALIZE", System.EnvironmentVariableTarget.Process)
                                        ?? System.Environment.GetEnvironmentVariable("AICAD_PER_STEP_CANONICALIZE", System.EnvironmentVariableTarget.User);
                        if (!string.IsNullOrWhiteSpace(envCanon) && (envCanon == "1" || envCanon.Equals("true", StringComparison.OrdinalIgnoreCase))) perStepCanonicalize = true;
                    }
                    catch { }
                }
                // Pre-validation removed: execute steps as provided and let handlers decide.
                // Track retries per-step to avoid infinite clarification loops
                var retryCounts = new Dictionary<int, int>();

                // Auto create part if first op isn't explicit new_part
                if (steps.Count == 0 || !HasNewPart(steps))
                {
                    // If there's already an active model, reuse it to avoid creating duplicates
                    model = (IModelDoc2)swApp.ActiveDoc;
                    if (model == null)
                    {
                        // Create a brand-new PART document; avoid NewDocument with unspecified template which can crash
                        model = (IModelDoc2)swApp.NewPart();
                        if (model == null)
                        {
                            result.Log.Add(new JObject { ["step"] = 0, ["op"] = "new_part", ["success"] = false, ["error"] = "Failed to create new part (check default template)" });
                            result.Success = false;
                            return result;
                        }
                        result.CreatedNewPart = true;
                        result.ModelTitle = model.GetTitle();
                        result.Log.Add(new JObject { ["step"] = 0, ["op"] = "new_part", ["success"] = true });
                    }
                    int actErr = 0; swApp.ActivateDoc3(model.GetTitle(), true, (int)swRebuildOptions_e.swRebuildAll, ref actErr);
                    sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                }

                for (int i = 0; i < steps.Count; i++)
                {
                    var raw = steps[i];
                    var s = NormalizeStep(raw);
                    // If per-step canonicalization is enabled, ask the ClarificationService to
                    // canonicalize/repair this single step before execution. This allows LLM
                    // canonicalization for every step (not just failures). The env var
                    // AICAD_PER_STEP_CANONICALIZE controls this behaviour.
                    if (perStepCanonicalize)
                    {
                        try
                        {
                            var replacement = ClarificationService.ClarifySingleStep(s);
                            if (replacement != null)
                            {
                                if (replacement is JArray arr)
                                {
                                    // splice returned array in-place, replacing the current step
                                    steps.RemoveAt(i);
                                    for (int ri = arr.Count - 1; ri >= 0; ri--)
                                    {
                                        steps.Insert(i, arr[ri]);
                                    }
                                    // retry processing at this index (which is now the first inserted item)
                                    i--;
                                    continue;
                                }
                                else if (replacement is JObject obj)
                                {
                                    steps[i] = obj;
                                    s = NormalizeStep(steps[i]);
                                }
                            }
                        }
                        catch (Exception exCanon)
                        {
                            try { AddinStatusLogger.Log("StepExecutor", $"Per-step canonicalization failed for index {i}: {exCanon.Message}"); } catch { }
                        }
                    }
                    string op = s.Value<string>("op") ?? string.Empty;
                    var opLower = (op ?? string.Empty).ToLowerInvariant();
                    var log = new JObject { ["step"] = i, ["op"] = op };
                    var sw = Stopwatch.StartNew();
                    
                    // VALIDATION: Capture model state BEFORE execution
                    JObject beforeSnapshot = null;
                    try { if (model != null) beforeSnapshot = ModelInspector.InspectModel(model, emitLogs: false); } catch { }

                    try
                    {
                        // Report progress before executing this step: overall percent and current op
                        var beforePct = (int)(i * 100 / Math.Max(1, steps.Count));
                        try { progressCallback?.Invoke(beforePct, op, i); } catch { }
                    }
                    catch { }
                    // Validate operation is present
                    if (string.IsNullOrWhiteSpace(op))
                    {
                        log["success"] = false;
                        // Include raw step for diagnostics when possible. Use JsonConvert to avoid runtime method binding on JToken.ToString(Formatting).
                        try { log["error"] = "Missing or empty 'op' field; raw=" + (raw == null ? "<null>" : Newtonsoft.Json.JsonConvert.SerializeObject(raw, Newtonsoft.Json.Formatting.None)); } catch { log["error"] = "Missing or empty 'op' field"; }
                        result.Log.Add(log);
                        result.Success = false;
                        try { AddinStatusLogger.Error("StepExecutor", $"Step {i} missing op"); } catch { }
                        return result; // stop at first failure
                    }
                    try
                    {

                        // Handle new_part inline to ensure model exists before other handlers
                        if (string.Equals(op, "new_part", StringComparison.OrdinalIgnoreCase))
                        {
                            if (model == null)
                            {
                                model = (IModelDoc2)swApp.NewPart();
                                if (model == null)
                                    throw new Exception("Failed to create new part (check default template)");
                                result.CreatedNewPart = true;
                                result.ModelTitle = model.GetTitle();
                                int actErr = 0; swApp.ActivateDoc3(model.GetTitle(), true, (int)swRebuildOptions_e.swRebuildAll, ref actErr);
                                sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                            }
                            log["success"] = true;
                            result.Log.Add(log);
                            sw.Stop();
                            try { AddinStatusLogger.Log("StepExecutor", $"Step {i}: op='{op}' completed success={log.Value<bool?>("success")} elapsed={sw.ElapsedMilliseconds}ms"); } catch { }
                            continue;
                        }

                        // Ensure we have a model if new_part was omitted or already processed
                        if (model == null)
                        {
                            model = (IModelDoc2)swApp.ActiveDoc;
                            if (model == null)
                            {
                                model = (IModelDoc2)swApp.NewPart();
                                if (model == null)
                                    throw new Exception("Failed to create new part (check default template)");
                                result.CreatedNewPart = true;
                                result.ModelTitle = model.GetTitle();
                            }
                            int actErr2 = 0; swApp.ActivateDoc3(model.GetTitle(), true, (int)swRebuildOptions_e.swRebuildAll, ref actErr2);
                            sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                        }

                        // Look up handler in registry
                        var handler = _operationRegistry.Get(op);
                        if (handler == null)
                        {
                            // Attempt a one-time clarification with the LLM to correct the op if possible.
                            var rc = retryCounts.ContainsKey(i) ? retryCounts[i] : 0;
                            try
                            {
                                var hint = MissingFeatureAdvisor.AdviseForUnknownOp(op);
                                if (!string.IsNullOrWhiteSpace(hint)) AddinStatusLogger.Log("FeatureAdvice", hint);
                            }
                            catch { }
                            if (enableLlmRepair && rc < 1)
                            {
                                try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Step {i}: unknown op '{op}' — requesting LLM correction (retry {rc + 1}/1)"); } catch { }
                                try
                                {
                                    var clarified = ClarificationService.ClarifySingleStep(
                                        s,
                                        BuildLlmRepairContext(plan, i, op, $"Unknown op '{op}'", null));
                                    if (clarified != null)
                                    {
                                        steps[i] = clarified;
                                        retryCounts[i] = rc + 1;
                                        try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Step {i}: applied LLM-corrected step; will retry"); } catch { }
                                        i--; // retry this index
                                        continue;
                                    }
                                    else
                                    {
                                        var exNo = new Exception($"Unknown op '{op}' (not registered)");
                                        try { exNo.Data["llm_prompt"] = ClarificationService.LastPromptUsed; } catch { }
                                        try { exNo.Data["llm_reply"] = ClarificationService.LastRawReply; } catch { }
                                        throw exNo;
                                    }
                                }
                                catch (Exception exClar) { try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Step {i}: LLM clarification failed: {exClar.Message}"); } catch { } }
                            }

                            throw new Exception($"Unknown op '{op}' (not registered)");
                        }

                        // Execute the operation through its handler
                        OperationResult opResult = null;
                        try
                        {
                            opResult = handler.Execute(s, model, sketchMgr, featMgr, inSketch);
                        }
                        catch (Exception handlerEx)
                        {
                            // Self-heal on handler exception for dimension-like ops
                            if (enableLlmRepair && (opLower == "dimension" || opLower.StartsWith("dimension")))
                            {
                                var rc = retryCounts.ContainsKey(i) ? retryCounts[i] : 0;
                                if (rc < 1)
                                {
                                    try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Step {i}: handler threw '{handlerEx.Message}' — requesting LLM repair (attempt {rc + 1}/1)"); } catch { }
                                    var clarified = ClarificationService.ClarifySingleStep(
                                        s,
                                        BuildLlmRepairContext(plan, i, op, handlerEx.Message, null));
                                    if (clarified != null)
                                    {
                                        if (clarified is JArray arr)
                                        {
                                            var replaceArr = new List<JToken>();
                                            foreach (var el in arr) replaceArr.Add(el);
                                            if (replaceArr.Count > 0)
                                            {
                                                steps[i] = replaceArr[0];
                                                for (int ins = 1; ins < replaceArr.Count; ins++) steps.Insert(i + ins, replaceArr[ins]);
                                            }
                                        }
                                        else
                                        {
                                            steps[i] = clarified;
                                        }
                                        retryCounts[i] = rc + 1;
                                        try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Applied LLM repair for index {i}; will retry"); } catch { }
                                        i--; // retry this index
                                        continue;
                                    }
                                    else
                                    {
                                        var exNo = new Exception(handlerEx.Message, handlerEx);
                                        try { exNo.Data["llm_prompt"] = ClarificationService.LastPromptUsed; } catch { }
                                        try { exNo.Data["llm_reply"] = ClarificationService.LastRawReply; } catch { }
                                        throw exNo;
                                    }
                                }
                            }
                            throw;
                        }

                        if (!opResult.Success)
                        {
                            // Self-heal when a dimension handler reports a failure (e.g., missing cx/cy/w/h)
                            if (opLower == "dimension" || opLower.StartsWith("dimension"))
                            {
                                var rc = retryCounts.ContainsKey(i) ? retryCounts[i] : 0;
                                if (rc < 1)
                                {
                                    try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Step {i}: dimension handler reported failure '{opResult.ErrorMessage}' — requesting LLM repair (attempt {rc + 1}/1)"); } catch { }
                                    var clarified = ClarificationService.ClarifySingleStep(
                                        s,
                                        BuildLlmRepairContext(plan, i, op, opResult.ErrorMessage, opResult.Data));
                                    if (clarified != null)
                                    {
                                        // If LLM returned an array, splice it; if object, replace.
                                        if (clarified is JArray arr)
                                        {
                                            var replaceArr = new List<JToken>();
                                            foreach (var el in arr) replaceArr.Add(el);
                                            if (replaceArr.Count > 0)
                                            {
                                                steps[i] = replaceArr[0];
                                                for (int ins = 1; ins < replaceArr.Count; ins++) steps.Insert(i + ins, replaceArr[ins]);
                                            }
                                        }
                                        else
                                        {
                                            steps[i] = clarified;
                                        }
                                        retryCounts[i] = rc + 1;
                                        try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Applied LLM repair for index {i}; will retry"); } catch { }
                                        i--; // retry this index
                                        continue;
                                    }
                                    else
                                    {
                                        var exNo = new Exception(opResult.ErrorMessage ?? "Operation failed");
                                        try { exNo.Data["llm_prompt"] = ClarificationService.LastPromptUsed; } catch { }
                                        try { exNo.Data["llm_reply"] = ClarificationService.LastRawReply; } catch { }
                                        throw exNo;
                                    }
                                }
                            }
                            try
                            {
                                var hint = MissingFeatureAdvisor.AdviseForFailure(op, opResult.ErrorMessage);
                                if (!string.IsNullOrWhiteSpace(hint)) AddinStatusLogger.Log("FeatureAdvice", hint);
                            }
                            catch { }
                            throw new Exception(opResult.ErrorMessage ?? "Operation failed");
                        }

                        // Update sketch state if handler changed it
                        inSketch = opResult.InSketch;
                        log["success"] = true;
                        // Attach any structured data returned by the handler (e.g., created/set counts)
                        try
                        {
                            if (opResult.Data != null)
                            {
                                log["data"] = Newtonsoft.Json.Linq.JToken.FromObject(opResult.Data);
                            }
                        }
                        catch { }

                        // Special handling for plan_from_intent: inject returned steps into execution
                        if (opLower == "plan_from_intent" && opResult.Data != null)
                        {
                            try
                            {
                                var dataObj = Newtonsoft.Json.Linq.JToken.FromObject(opResult.Data);
                                // If the planner returned a clarification request instead of steps,
                                // surface it to the caller by setting result.Clarification and returning.
                                if (dataObj["clarification_needed"] != null)
                                {
                                    // Attach clarification to the result and log it for UI consumption
                                    var clar = new JObject { ["step"] = i, ["op"] = op, ["clarification"] = dataObj["clarification_needed"] };
                                    result.Log.Add(clar);
                                    result.Clarification = dataObj.Type == JTokenType.Object ? (JObject)dataObj : dataObj as JObject;
                                    result.Success = false;
                                    AddinStatusLogger.Log("StepExecutor", $"Plan requested clarification at step {i}");
                                    return result; // halt execution so UI can surface clarification to user
                                }
                                if (dataObj["steps"] is JArray generatedSteps && generatedSteps.Count > 0)
                                {
                                    AddinStatusLogger.Log("StepExecutor", $"Splicing {generatedSteps.Count} LLM-generated steps at index {i + 1}");
                                    // Insert generated steps after the current step
                                    for (int gi = 0; gi < generatedSteps.Count; gi++)
                                    {
                                        steps.Insert(i + 1 + gi, generatedSteps[gi]);
                                    }
                                }
                            }
                            catch (Exception spliceEx)
                            {
                                AddinStatusLogger.Error("StepExecutor", "Failed to splice plan_from_intent steps", spliceEx);
                            }
                        }

                        // If a dimension handler reports no created/set counts, request clarification and retry (self-heal)
                        try
                        {
                            int createdCount = -1, setCount = -1;
                            if (opResult.Data != null)
                            {
                                try
                                {
                                    var jt = Newtonsoft.Json.Linq.JToken.FromObject(opResult.Data);
                                    createdCount = jt["createdCount"]?.Value<int>() ?? jt["created"]?.Value<int>() ?? -1;
                                    setCount = jt["setCount"]?.Value<int>() ?? jt["set"]?.Value<int>() ?? -1;
                                }
                                catch { }
                            }
                            if (enableLlmRepair && (opLower == "dimension" || opLower.StartsWith("dimension")) && createdCount == 0 && setCount == 0)
                            {
                                var rc = retryCounts.ContainsKey(i) ? retryCounts[i] : 0;
                                if (rc < 1)
                                {
                                    try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Step {i}: dimension handler made no changes — requesting LLM repair (attempt {rc + 1}/1)"); } catch { }
                                    var clarified = ClarificationService.ClarifySingleStep(
                                        s,
                                        BuildLlmRepairContext(plan, i, op, "No dimensions were created or value-set by the handler", opResult.Data));
                                    if (clarified != null)
                                    {
                                        // If LLM returned an array, splice it; if object, replace.
                                        if (clarified is JArray arr)
                                        {
                                            var replaceArr = new List<JToken>();
                                            foreach (var el in arr) replaceArr.Add(el);
                                            if (replaceArr.Count > 0)
                                            {
                                                steps[i] = replaceArr[0];
                                                for (int ins = 1; ins < replaceArr.Count; ins++) steps.Insert(i + ins, replaceArr[ins]);
                                            }
                                        }
                                        else
                                        {
                                            steps[i] = clarified;
                                        }
                                        retryCounts[i] = rc + 1;
                                        try { AddinStatusLogger.Log("StepExecutor", $"[SelfHeal] Applied LLM repair for index {i}; will retry"); } catch { }
                                        i--; // retry this index
                                        continue;
                                    }
                                    else
                                    {
                                        var exNo = new Exception("No dimensions were created or value-set by the handler");
                                        try { exNo.Data["llm_prompt"] = ClarificationService.LastPromptUsed; } catch { }
                                        try { exNo.Data["llm_reply"] = ClarificationService.LastRawReply; } catch { }
                                        throw exNo;
                                    }
                                }
                                // If we get here, clarification did not produce replacement or already retried; fail the step
                                throw new Exception("No dimensions were created or value-set by the handler");
                            }
                        }
                        catch (Exception exClar)
                        {
                            // bubble up as normal exception below
                            throw exClar;
                        }

                        // VALIDATION: Capture model state AFTER execution and validate
                        JObject afterSnapshot = null;
                        try { if (model != null) afterSnapshot = ModelInspector.InspectModel(model, emitLogs: false); } catch { }
                        
                        if (beforeSnapshot != null && afterSnapshot != null)
                        {
                            try
                            {
                                var validation = ExecutionValidator.ValidateStep(s, model, beforeSnapshot, afterSnapshot);
                                result.Validations.Add(validation);
                                if (!validation.IsValid)
                                {
                                    log["validation_warning"] = validation.Message;
                                }
                            }
                            catch (Exception valEx)
                            {
                                log["validation_error"] = valEx.Message;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        log["success"] = false;
                        log["error"] = ex.Message;
                        try
                        {
                            if (ex.Data != null)
                            {
                                if (ex.Data.Contains("llm_prompt")) log["llm_prompt"] = ex.Data["llm_prompt"]?.ToString();
                                if (ex.Data.Contains("llm_reply")) log["llm_reply"] = ex.Data["llm_reply"]?.ToString();
                            }
                        }
                        catch { }
                        result.Log.Add(log);
                        result.Success = false;
                        // Log completion/info first, then emit the error-level message so the status line appears before the error entry
                        try { AddinStatusLogger.Log("StepExecutor", $"Step {i}: op='{op}' completed success={log.Value<bool?>("success")} elapsed={sw.ElapsedMilliseconds}ms"); } catch { }
                        try { AddinStatusLogger.Error("StepExecutor", $"Step {i} failed op='{op}'", ex); } catch { }

                        // If continueOnError is enabled, log this failure but process next step
                        if (!continueOnError)
                        {
                            // If we created a new part for this plan and execution failed,
                            // close the newly-created document so partial/unsaved models don't persist.
                            try
                            {
                                if (result.CreatedNewPart && model != null)
                                {
                                    try
                                    {
                                        var title = model.GetTitle();
                                        if (preservePartsOnError)
                                        {
                                            AddinStatusLogger.Log("StepExecutor", $"Preserving newly created part '{title}' due to AICAD_PRESERVE_PARTS_ON_ERROR");
                                        }
                                        else
                                        {
                                            swApp.CloseDoc(title);
                                            AddinStatusLogger.Log("StepExecutor", $"Closed newly created part '{title}' due to error");
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }

                            return result; // ORIGINAL: stop at first failure
                        }
                        else
                        {
                            // NEW: Continue to next step instead of aborting
                            try { AddinStatusLogger.Log("StepExecutor", $"Continuing to next step despite failure (continueOnError=true)"); } catch { }
                            continue;
                        }
                    }
                    result.Log.Add(log);
                    try
                    {
                        // Report progress after completing this step
                        var afterPct = (int)((i + 1) * 100 / Math.Max(1, steps.Count));
                        try { progressCallback?.Invoke(afterPct, op, i); } catch { }
                    }
                    catch { }
                    sw.Stop();
                    try { AddinStatusLogger.Log("StepExecutor", $"Step {i}: op='{op}' completed success={log.Value<bool?>("success")} elapsed={sw.ElapsedMilliseconds}ms"); } catch { }
                }

                // Check if continueOnError mode: success if ANY step succeeded
                if (continueOnError)
                {
                    var anySuccess = result.Log.Any(l => l["success"]?.Value<bool>() == true);
                    result.Success = anySuccess;
                    try { AddinStatusLogger.Log("StepExecutor", $"continueOnError mode: {result.Log.Count} steps, {result.Log.Count(l => l["success"]?.Value<bool>() == true)} succeeded"); } catch { }
                }
                else
                {
                    result.Success = true;
                }

                // VALIDATION: Generate validation report
                if (result.Validations.Count > 0)
                {
                    result.ValidationReport = ExecutionValidator.GenerateValidationReport(result.Validations);
                    try { AddinStatusLogger.Log("StepExecutor", $"Validation report: {result.ValidationReport["passed"]}/{result.ValidationReport["total"]} passed"); } catch { }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Log.Add(new JObject { ["step"] = -1, ["op"] = "exception", ["success"] = false, ["error"] = ex.Message });
                result.Success = false;
                try { AddinStatusLogger.Error("StepExecutor", "Unhandled exception executing plan", ex); } catch { }
                // If a new part was created during execution and we hit an unhandled exception,
                // close the new part so the user does not retain a partially-created model.
                try
                {
                    if (result.CreatedNewPart && swApp != null)
                    {
                        try
                        {
                            if (swApp.ActiveDoc != null)
                            {
                                var t = swApp.ActiveDoc.GetTitle();
                                if (preservePartsOnError)
                                {
                                    AddinStatusLogger.Log("StepExecutor", $"Preserving newly created part '{t}' due to AICAD_PRESERVE_PARTS_ON_ERROR (unhandled exception)");
                                }
                                else
                                {
                                    swApp.CloseDoc(t);
                                    AddinStatusLogger.Log("StepExecutor", $"Closed newly created part '{t}' due to unhandled exception");
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                return result;
            }
        }

    private static bool HasNewPart(JArray steps)
        {
            foreach (var s in steps)
            {
        var jo = NormalizeStep(s);
        if ((jo.Value<string>("op") ?? string.Empty) == "new_part") return true;
            }
            return false;
        }

        // Accept either JObject steps or compact string steps like "select_plane{name='XY'}" or "new_part"
        private static JObject NormalizeStep(JToken step)
        {
            if (step == null) return new JObject();
            if (step.Type == JTokenType.Object)
            {
                // Normalize common alternate field names produced by some LLMs
                var jo = (JObject)step;
                // map 'operation' -> 'op' if present
                try
                {
                    if (jo.Property("op") == null)
                    {
                        var opProp = jo.Property("operation") ?? jo.Property("Operation");
                        if (opProp != null)
                        {
                            jo["op"] = opProp.Value;
                        }
                    }
                }
                catch { }
                try
                {
                    var op = (jo.Value<string>("op") ?? string.Empty).Trim();
                    if (op.Equals("dimension", StringComparison.OrdinalIgnoreCase))
                    {
                        jo["op"] = "auto_dimension";
                        if (jo["cx"] == null) jo["cx"] = 0;
                        if (jo["cy"] == null) jo["cy"] = 0;
                        if (jo["w"] == null) jo["w"] = 10;
                        if (jo["h"] == null) jo["h"] = 10;
                    }
                }
                catch { }
                return jo;
            }
            if (step.Type == JTokenType.String || step.Type == JTokenType.Integer || step.Type == JTokenType.Float)
            {
                var s = step.ToString();
                s = s.Trim();
                if (string.IsNullOrEmpty(s)) return new JObject();
                var jo = new JObject();
                var braceIndex = s.IndexOf('{');
                if (braceIndex < 0)
                {
                    jo["op"] = s;
                    return jo;
                }
                var op = s.Substring(0, braceIndex).Trim();
                jo["op"] = op;
                var end = s.LastIndexOf('}');
                if (end <= braceIndex) return jo;
                var inner = s.Substring(braceIndex + 1, end - braceIndex - 1).Trim();
                // split by commas not inside quotes (simple approach)
                var parts = inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var kv = p.Split(new[] { '=' }, 2);
                    if (kv.Length != 2) continue;
                    var key = kv[0].Trim();
                    var val = kv[1].Trim().Trim('"').Trim('\'');
                    // try parse number
                    if (double.TryParse(val, out var num)) jo[key] = num;
                    else jo[key] = val;
                }
                return jo;
            }
            // fallback
            return new JObject();
        }

        private static void RequireModel(IModelDoc2 model)
        {
            if (model == null) throw new Exception("Model not initialized (call new_part first)");
        }

        private static double ToM(double mm) => mm / 1000.0;
    }
}
