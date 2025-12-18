using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidWorks.TaskpaneCalculator.Services
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
    public static StepExecutionResult Execute(JObject plan, ISldWorks swApp)
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
                        model = (IModelDoc2)swApp.NewDocument("", (int)swDwgPaperSizes_e.swDwgPaperA4size, 0, 0) ?? (IModelDoc2)swApp.NewPart();
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
                    if (model == null)
                    {
                        result.Log.Add(new JObject { ["step"] = 0, ["op"] = "new_part", ["success"] = false, ["error"] = "Failed to create new part (check default template)" });
                        result.Success = false;
                        return result;
                    }
                    int actErr = 0; swApp.ActivateDoc3(model.GetTitle(), true, (int)swRebuildOptions_e.swRebuildAll, ref actErr);
                    sketchMgr = model.SketchManager; featMgr = model.FeatureManager;
                }

        for (int i = 0; i < steps.Count; i++)
                {
                    var s = (JObject)steps[i];
                    string op = s.Value<string>("op") ?? string.Empty;
                    var log = new JObject { ["step"] = i, ["op"] = op };
                    try
                    {
            try { AddinStatusLogger.Log("StepExecutor", $"Step {i}: starting op='{op}'"); } catch { }
                        switch (op)
                        {
                            case "new_part":
                                model = (IModelDoc2)swApp.NewDocument("", (int)swDwgPaperSizes_e.swDwgPaperA4size, 0, 0) ?? (IModelDoc2)swApp.NewPart();
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
                                    bool sel = model.Extension.SelectByID2(name, "PLANE", 0, 0, 0, false, 0, null, 0);
                                    if (!sel) throw new Exception($"Could not select plane '{name}'");
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
        if (s is JObject jo && (jo.Value<string>("op") ?? string.Empty) == "new_part") return true;
            }
            return false;
        }

        private static void RequireModel(IModelDoc2 model)
        {
            if (model == null) throw new Exception("Model not initialized (call new_part first)");
        }

        private static double ToM(double mm) => mm / 1000.0;
    }
}
