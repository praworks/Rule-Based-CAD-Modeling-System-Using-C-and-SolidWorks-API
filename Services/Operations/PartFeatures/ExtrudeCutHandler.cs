using System;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "extrude_cut" operation - creates a cut extrusion.
    /// </summary>
    public class ExtrudeCutHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                // --- FIX START: Auto-Select Last Sketch if Selection is Empty ---
                var selMgr = (SelectionMgr)model.SelectionManager;

                // If the AI plan called "sketch_end" right before this, the selection count may be 0.
                // Try to re-select the most-recent sketch so the cut-extrude has a valid sketch selected.
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
                                    lastSketch = f; // keep latest sketch seen
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
                        AddinStatusLogger.Log("ExtrudeCutHandler", $"Auto-selected last sketch '{lastSketch.Name}' because selection was empty.");
                    }
                    else
                    {
                        // Fallback: try the last feature added (legacy behavior)
                        try
                        {
                            var lastFeature = model.Extension.GetLastFeatureAdded() as Feature;
                            if (lastFeature != null)
                            {
                                try { ((dynamic)lastFeature).Select4(false, selData); } catch { try { ((dynamic)lastFeature).Select2(false, 0); } catch { } }
                                AddinStatusLogger.Log("ExtrudeCutHandler", $"Auto-selected last feature '{lastFeature.Name}' because selection was empty.");
                            }
                        }
                        catch { }
                    }
                }
                // --- FIX END ---

                // Log selection diagnostics before attempting the cut
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
                AddinStatusLogger.Log("ExtrudeCutHandler", $"Selection before cut: count={selCount}, types=[{string.Join(",", selTypes)}]");

                if (selCount == 0)
                {
                    return OperationResult.CreateFailure("Extrude cut failed: no sketch selected after auto-select.");
                }

                // Extra diagnostic: attempt to log selected object names where possible
                try
                {
                    for (int i = 1; i <= selCount; i++)
                    {
                        try
                        {
                            var obj = selMgr.GetSelectedObject6(i, -1);
                            string name = null;
                            try { name = ((dynamic)obj).Name; } catch { try { name = obj?.ToString(); } catch { name = "<unknown>"; } }
                            AddinStatusLogger.Log("ExtrudeCutHandler", $"Selected object[{i}] name={name}");
                        }
                        catch { }
                    }
                }
                catch { }

                double depth = PartFeatureHelpers.ToMeters(step.Value<double?>("depth") ?? 0);
                
                // Use FeatureCut4 for robust cut extrusion with proper cut-specific options
                var feat = featMgr.FeatureCut4(
                    true,   // Sd: single direction
                    false,  // Flip
                    false,  // Dir2
                    (int)swEndConditions_e.swEndCondBlind,  // T1: end condition
                    0,      // T2
                    depth,  // D1: depth
                    0.0,    // D2
                    false,  // Draft
                    false,  // BackDraft
                    false,  // Draft2
                    false,  // BackDraft2
                    0.0,    // DraftAng
                    0.0,    // DraftAng2
                    false,  // OffsetReverse1
                    false,  // OffsetReverse2
                    false,  // TranslateSurface1
                    false,  // TranslateSurface2
                    false,  // NormalCut
                    true,   // UseFeat (apply feature)
                    true,   // UseAutoSelect (let SW pick correct bodies/profile)
                    false,  // AssemblyFeatureScope
                    true,   // AutoSelect - smarter body/sketch selection
                    false,  // Rematerialize
                    0,      // StartOffset
                    0.0,    // StartOffset2
                    false,  // reserved boolean
                    true    // OptimizeGeometry
                );

                if (feat == null)
                    return OperationResult.CreateFailure("Extrude cut operation failed");

                try { model.ForceRebuild3(false); AddinStatusLogger.Log("ExtrudeCutHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"extrude_cut failed: {ex.Message}");
            }
        }
    }
}