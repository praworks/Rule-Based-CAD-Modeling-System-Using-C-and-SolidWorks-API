using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Validation results for each step (post-execution geometry checks)</summary>
        public List<ExecutionValidator.ValidationResult> Validations { get; } = new List<ExecutionValidator.ValidationResult>();
        /// <summary>Overall validation report</summary>
        public JObject ValidationReport { get; set; }
    }

    internal static class StepExecutor
    {
        private static readonly OperationRegistry _operationRegistry = OperationRegistry.CreateDefault();

        /// <summary>
        /// Execute a plan with multiple steps using the operation handler registry
        /// </summary>
        public static StepExecutionResult Execute(JObject plan, ISldWorks swApp, Action<int, string, int?> progressCallback = null, bool continueOnError = false)
        {
            var result = new StepExecutionResult();
            try { AddinStatusLogger.Log("StepExecutor", $"Execute: invoked with plan keys={string.Join(",", plan?.Properties().Select(p=>p.Name) ?? new string[0])}"); } catch { }
            if (swApp == null)
            {
        result.Log.Add(new JObject { ["step"] = -1, ["op"] = "init", ["success"] = false, ["error"] = "SOLIDWORKS app not available" });
                result.Success = false;
                return result;
            }

                try
            {
                var steps = plan.ContainsKey("steps") && plan["steps"] is JArray ? (JArray)plan["steps"] : new JArray();
                    try { AddinStatusLogger.Log("StepExecutor", $"Execute: resolved {steps.Count} steps"); } catch { }
                IModelDoc2 model = null;
                ISketchManager sketchMgr = null;
                IFeatureManager featMgr = null;
                bool inSketch = false;

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
                    string op = s.Value<string>("op") ?? string.Empty;
                    var log = new JObject { ["step"] = i, ["op"] = op };
                    
                    // VALIDATION: Capture model state BEFORE execution
                    JObject beforeSnapshot = null;
                    try { if (model != null) beforeSnapshot = ModelInspector.InspectModel(model); } catch { }

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
                        try { AddinStatusLogger.Log("StepExecutor", $"Step {i}: starting op='{op}'"); } catch { }

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
                            throw new Exception($"Unknown op '{op}' (not registered)");

                        // Execute the operation through its handler
                        var opResult = handler.Execute(s, model, sketchMgr, featMgr, inSketch);
                        if (!opResult.Success)
                            throw new Exception(opResult.ErrorMessage ?? "Operation failed");

                        // Update sketch state if handler changed it
                        inSketch = opResult.InSketch;
                        log["success"] = true;

                        // VALIDATION: Capture model state AFTER execution and validate
                        JObject afterSnapshot = null;
                        try { if (model != null) afterSnapshot = ModelInspector.InspectModel(model); } catch { }
                        
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
                        log["success"] = false;
                        log["error"] = ex.Message;
                        result.Log.Add(log);
                        result.Success = false;
                        try { AddinStatusLogger.Error("StepExecutor", $"Step {i} failed op='{op}'", ex); } catch { }
                        
                        // If continueOnError is enabled, log this failure but process next step
                        if (!continueOnError)
                        {
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
                    try { AddinStatusLogger.Log("StepExecutor", $"Step {i}: completed op='{op}' success={log.Value<bool?>("success")}" ); } catch { }
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
