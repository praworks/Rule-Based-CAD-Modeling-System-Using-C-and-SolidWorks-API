using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services
{
    internal class StepExecutionResult
    {
        public bool Success { get; set; }
    public List<JObject> Log { get; } = new List<JObject>();
    public bool CreatedNewPart { get; set; }
    public string ModelTitle { get; set; }
    }

    internal static class StepExecutor
    {
        // Supported ops: new_part, select_plane{name}, sketch_begin, sketch_end,
        // rectangle_center{cx,cy,w,h}, circle_center{cx,cy,r|diameter}, extrude{depth,type?:boss}
    public static StepExecutionResult Execute(JObject plan, ISldWorks swApp, Action<int, string, int?> progressCallback = null)
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
                        switch (op)
                        {
                            case "new_part":
                                // Create a new PART document explicitly to avoid template-related crashes
                                model = (IModelDoc2)swApp.NewPart();
                                if (model == null) throw new Exception("Failed to create new part (check default template)");
                                {
                                    int actErr = 0; swApp.ActivateDoc3(model.GetTitle(), true, (int)swRebuildOptions_e.swRebuildAll, ref actErr);
                                }
                                sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                                result.CreatedNewPart = true;
                                result.ModelTitle = model.GetTitle();
                                log["success"] = true;
                                break;
                            case "select_plane":
                                RequireModel(model);
                                {
                                    string name = s.Value<string>("name") ?? "Front Plane";
                                    bool sel = false;
                                    // Try the requested name first
                                    try { AddinStatusLogger.Log("StepExecutor", $"Selecting plane: '{name}'"); } catch { }
                                    sel = model.Extension.SelectByID2(name, "PLANE", 0, 0, 0, false, 0, null, 0);
                                    // If initial selection fails, try common SolidWorks plane names and simple mappings
                                    if (!sel)
                                    {
                                        var candidates = new System.Collections.Generic.List<string>();
                                        // normalize
                                        var n = (name ?? string.Empty).Trim();
                                        if (!string.IsNullOrEmpty(n))
                                        {
                                            candidates.Add(n);
                                            if (!n.EndsWith(" Plane", StringComparison.OrdinalIgnoreCase)) candidates.Add(n + " Plane");
                                            // common plane name mappings
                                            if (n.IndexOf("xy", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("x-y", StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add("Top Plane");
                                            if (n.IndexOf("xz", StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add("Front Plane");
                                            if (n.IndexOf("yz", StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add("Right Plane");
                                        }
                                        candidates.AddRange(new[] { "Top Plane", "Front Plane", "Right Plane" });
                                        foreach (var cand in candidates)
                                        {
                                            try { AddinStatusLogger.Log("StepExecutor", $"Trying plane candidate: '{cand}'"); } catch { }
                                            if (string.IsNullOrWhiteSpace(cand)) continue;
                                            sel = model.Extension.SelectByID2(cand, "PLANE", 0, 0, 0, false, 0, null, 0);
                                            if (sel) break;
                                        }
                                    }
                                    if (!sel) throw new Exception($"Could not select plane '{name}' (tried common alternatives)");
                                    log["success"] = true;
                                }
                                break;
                            case "sketch_begin":
                                RequireModel(model);
                                sketchMgr.InsertSketch(true);
                                inSketch = true;
                                log["success"] = true;
                                break;
                            case "rectangle_center":
                                RequireModel(model);
                                if (!inSketch) throw new Exception("Not in sketch");
                                {
                                    double cx = ToM(s.Value<double?>("cx") ?? 0);
                                    double cy = ToM(s.Value<double?>("cy") ?? 0);
                                    double w = ToM(s.Value<double?>("w") ?? 0);
                                    double h = ToM(s.Value<double?>("h") ?? 0);
                                    var rect = sketchMgr.CreateCenterRectangle(cx, cy, 0, cx + w / 2.0, cy + h / 2.0, 0);
                                    if (rect == null) throw new Exception("Failed to create rectangle");
                                    log["success"] = true;
                                }
                                break;
                            case "circle_center":
                                RequireModel(model);
                                if (!inSketch) throw new Exception("Not in sketch");
                                {
                                    double cx = ToM(s.Value<double?>("cx") ?? 0);
                                    double cy = ToM(s.Value<double?>("cy") ?? 0);
                                    double r = s["r"] != null ? ToM(s.Value<double>("r")) : (s["diameter"] != null ? ToM(s.Value<double>("diameter")) / 2.0 : 0);
                                    if (r <= 0) throw new Exception("Missing r or diameter > 0");
                                    var circ = sketchMgr.CreateCircleByRadius(cx, cy, 0, r);
                                    if (circ == null) throw new Exception("Failed to create circle");
                                    log["success"] = true;
                                }
                                break;
                            case "sketch_end":
                                RequireModel(model);
                                sketchMgr.InsertSketch(true);
                                inSketch = false;
                                log["success"] = true;
                                break;
                            case "extrude":
                                RequireModel(model);
                                {
                                    double depth = ToM(s.Value<double?>("depth") ?? 0);
                                    bool boss = (s.Value<string>("type") ?? "boss").ToLowerInvariant() == "boss";
                                    var feat = featMgr.FeatureExtrusion2(boss,
                                        false,
                                        false,
                                        (int)swEndConditions_e.swEndCondBlind,
                                        (int)swEndConditions_e.swEndCondBlind,
                                        depth,
                                        0,
                                        false,
                                        false,
                                        false,
                                        false,
                                        0,
                                        0,
                                        false,
                                        false,
                                        false,
                                        false,
                                        true,
                                        false,
                                        false,
                                        (int)swStartConditions_e.swStartSketchPlane,
                                        0,
                                        false);
                                    if (feat == null) throw new Exception("Extrude failed");
                                    log["success"] = true;
                                }
                                break;
                            default:
                                throw new Exception($"Unknown op '{op}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        log["success"] = false;
                        log["error"] = ex.Message;
                        result.Log.Add(log);
                        result.Success = false;
                        try { AddinStatusLogger.Error("StepExecutor", $"Step {i} failed op='{op}'", ex); } catch { }
                        return result; // stop at first failure
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

                result.Success = true;
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
