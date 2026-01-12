using System;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "extrude" operation - creates an extrusion (boss or cut)
    /// Renamed to `ExtrudeBossHandler` to clarify this handler creates boss extrudes.
    /// </summary>
    public class ExtrudeBossHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                // Ensure a sketch is selected before extruding to avoid dialog hangs.
                var selMgr = (SelectionMgr)model.SelectionManager;
                if (selMgr.GetSelectedObjectCount2(-1) == 0)
                {
                    try { model.ClearSelection2(true); } catch { }

                    SelectData selData = null;
                    try { selData = selMgr.CreateSelectData(); selData.Mark = 1; } catch { }

                    Feature lastSketch = null;
                    try
                    {
                        var f = model.FirstFeature();
                        while (f != null)
                        {
                            try
                            {
                                var tname = f.GetTypeName2();
                                if (!string.IsNullOrEmpty(tname) && tname.ToLower().Contains("sketch"))
                                {
                                    lastSketch = f;
                                }
                            }
                            catch { }
                            try { f = f.GetNextFeature(); } catch { break; }
                        }
                    }
                    catch { }

                    if (lastSketch != null)
                    {
                        try { ((dynamic)lastSketch).Select4(false, selData); } catch { try { ((dynamic)lastSketch).Select2(false, 0); } catch { } }
                        AddinStatusLogger.Log("ExtrudeBossHandler", $"Auto-selected last sketch '{lastSketch.Name}' because selection was empty.");
                    }
                    else
                    {
                        try
                        {
                            var lastFeature = model.Extension.GetLastFeatureAdded() as Feature;
                            if (lastFeature != null)
                            {
                                try { ((dynamic)lastFeature).Select4(false, selData); } catch { try { ((dynamic)lastFeature).Select2(false, 0); } catch { } }
                                AddinStatusLogger.Log("ExtrudeBossHandler", $"Auto-selected last feature '{lastFeature.Name}' because selection was empty.");
                            }
                        }
                        catch { }
                    }
                }

                int selCount = 0;
                var selTypes = new List<string>();
                try
                {
                    selCount = selMgr.GetSelectedObjectCount2(-1);
                    for (int i = 1; i <= selCount; i++)
                    {
                        try { selTypes.Add(selMgr.GetSelectedObjectType3(i, -1).ToString()); } catch { }
                    }
                }
                catch { }
                AddinStatusLogger.Log("ExtrudeBossHandler", $"Selection before extrude: count={selCount}, types=[{string.Join(",", selTypes)}]");
                if (selCount == 0)
                {
                    return OperationResult.CreateFailure("Extrude failed: no sketch selected after auto-select.");
                }

                double depth = PartFeatureHelpers.ToMeters(step.Value<double?>("depth") ?? 0);
                bool isBoss = (step.Value<string>("type") ?? "boss").ToLowerInvariant() == "boss";

                var feat = featMgr.FeatureExtrusion2(isBoss,
                    false, false,
                    (int)swEndConditions_e.swEndCondBlind,
                    (int)swEndConditions_e.swEndCondBlind,
                    depth, 0,
                    false, false, false, false, 0, 0,
                    false, false, false, false, true, false, false,
                    (int)swStartConditions_e.swStartSketchPlane, 0, false);

                if (feat == null)
                    return OperationResult.CreateFailure("Extrude operation failed");

                // Force rebuild so the feature tree and bodies are updated
                try { model.ForceRebuild3(false); AddinStatusLogger.Log("ExtrudeBossHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"extrude failed: {ex.Message}");
            }
        }
    }
}
