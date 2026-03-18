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
        private const string PreferredSwUnitsKey = "PreferredSwUnitSystem";

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
            try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"STEP 8.4 Execute invoked with plan keys={string.Join(",", plan?.Properties().Select(p=>p.Name) ?? new string[0])}"); } catch { }
            if (swApp == null)
            {
        result.Log.Add(new JObject { ["step"] = -1, ["op"] = "init", ["success"] = false, ["error"] = "SOLIDWORKS app not available" });
                result.Success = false;
                return result;
            }

                try
            {
                var steps = plan.ContainsKey("steps") && plan["steps"] is JArray ? (JArray)plan["steps"] : new JArray();
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Execute resolved {steps.Count} steps"); } catch { }

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
                // Declare document/context variables early for execution context.
                IModelDoc2 model = null;
                ISketchManager sketchMgr = null;
                IFeatureManager featMgr = null;
                bool inSketch = false;

                // Pre-validation removed: execute steps as provided and let handlers decide.
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
                    ApplyPreferredDocumentUnits(swApp, runId, requestId);
                    sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                }

                for (int i = 0; i < steps.Count; i++)
                {
                    var raw = steps[i];
                    var s = NormalizeStep(raw);
                    string op = s.Value<string>("op") ?? string.Empty;
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
                        try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "ERROR", $"Step missing op globalStepIndex={i}"); } catch { }
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
                                ApplyPreferredDocumentUnits(swApp, runId, requestId);
                                sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                            }
                            log["success"] = true;
                            result.Log.Add(log);
                            sw.Stop();
                            try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Step result globalStepIndex={i} op={op} success={log.Value<bool?>("success")} elapsedMs={sw.ElapsedMilliseconds}"); } catch { }
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
                            ApplyPreferredDocumentUnits(swApp, runId, requestId);
                            sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                        }

                        // Look up handler in registry
                        var handler = _operationRegistry.Get(op);
                        if (handler == null)
                        {
                            try
                            {
                                var hint = MissingFeatureAdvisor.AdviseForUnknownOp(op);
                                if (!string.IsNullOrWhiteSpace(hint)) DiagnosticLogWriter.LogLine(runId, requestId, "FeatureAdvice", "INFO", hint);
                            }
                            catch { }
                            var allowedPreview = string.Join(", ", _operationRegistry.GetRegisteredOperations().Take(30));
                            var ex = new Exception($"Unknown op '{op}' (not registered). Allowed ops (first 30): {allowedPreview}");
                            ex.Data["llm_prompt"] = "Allowed ops preview: " + allowedPreview;
                            throw ex;
                        }

                        // Execute the operation through its handler
                        OperationResult opResult = null;
                        try
                        {
                            opResult = handler.Execute(s, model, sketchMgr, featMgr, inSketch);
                        }
                        catch (Exception handlerEx)
                        {
                            throw;
                        }

                        if (!opResult.Success)
                        {
                            try
                            {
                                var hint = MissingFeatureAdvisor.AdviseForFailure(op, opResult.ErrorMessage);
                                if (!string.IsNullOrWhiteSpace(hint)) DiagnosticLogWriter.LogLine(runId, requestId, "FeatureAdvice", "INFO", hint);
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
                        try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "ERROR", $"Step result globalStepIndex={i} op={op} success={log.Value<bool?>("success")} elapsedMs={sw.ElapsedMilliseconds} error={ex.Message}"); } catch { }

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
                                            DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Preserving newly created part '{title}' due to AICAD_PRESERVE_PARTS_ON_ERROR");
                                        }
                                        else
                                        {
                                            swApp.CloseDoc(title);
                                            DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Closed newly created part '{title}' due to error");
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
                            try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", "Continuing to next step despite failure (continueOnError=true)"); } catch { }
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
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Step result globalStepIndex={i} op={op} success={log.Value<bool?>("success")} elapsedMs={sw.ElapsedMilliseconds}"); } catch { }
                }

                // Check if continueOnError mode: success if ANY step succeeded
                if (continueOnError)
                {
                    var anySuccess = result.Log.Any(l => l["success"]?.Value<bool>() == true);
                    result.Success = anySuccess;
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"continueOnError mode: {result.Log.Count} steps, {result.Log.Count(l => l["success"]?.Value<bool>() == true)} succeeded"); } catch { }
                }
                else
                {
                    result.Success = true;
                }

                // VALIDATION: Generate validation report
                if (result.Validations.Count > 0)
                {
                    result.ValidationReport = ExecutionValidator.GenerateValidationReport(result.Validations);
                    try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Validation report: {result.ValidationReport["passed"]}/{result.ValidationReport["total"]} passed"); } catch { }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Log.Add(new JObject { ["step"] = -1, ["op"] = "exception", ["success"] = false, ["error"] = ex.Message });
                result.Success = false;
                try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "ERROR", "Unhandled exception executing plan: " + ex.Message); } catch { }
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
                                    DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Preserving newly created part '{t}' due to AICAD_PRESERVE_PARTS_ON_ERROR (unhandled exception)");
                                }
                                else
                                {
                                    swApp.CloseDoc(t);
                                    DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Closed newly created part '{t}' due to unhandled exception");
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

        private static void ApplyPreferredDocumentUnits(ISldWorks swApp, string runId, string requestId)
        {
            try
            {
                var preferredUnits = SettingsManager.GetString(PreferredSwUnitsKey, "MMGS");
                if (!UnitManager.SetUnits(swApp, preferredUnits))
                {
                    DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Preferred units '{preferredUnits}' were not applied because there was no active document.");
                    return;
                }

                DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "INFO", $"Applied preferred document units: {preferredUnits}");
            }
            catch (Exception ex)
            {
                try { DiagnosticLogWriter.LogLine(runId, requestId, "StepExecutor", "WARN", $"Failed to apply preferred document units: {ex.Message}"); } catch { }
            }
        }

        private static double ToM(double mm) => mm / 1000.0;
    }
}
