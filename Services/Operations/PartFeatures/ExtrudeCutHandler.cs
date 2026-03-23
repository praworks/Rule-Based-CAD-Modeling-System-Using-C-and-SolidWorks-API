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

                var selMgr = (SelectionMgr)model.SelectionManager;
                EnsureSketchSelected(model, selMgr);

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
                bool throughAll = step.Value<bool?>("through_all") ?? false;
                var endConditionToken = step.Value<string>("end_condition") ?? string.Empty;
                if (!throughAll && !string.IsNullOrWhiteSpace(endConditionToken))
                {
                    throughAll = endConditionToken.Equals("through_all", StringComparison.OrdinalIgnoreCase)
                        || endConditionToken.Equals("throughall", StringComparison.OrdinalIgnoreCase)
                        || endConditionToken.Equals("through-all", StringComparison.OrdinalIgnoreCase);
                }
                if (!throughAll && depth <= 0)
                {
                    return OperationResult.CreateFailure("Extrude cut failed: blind cuts require depth > 0 unless through_all is true.");
                }
                int endCondition = throughAll
                    ? (int)swEndConditions_e.swEndCondThroughAll
                    : (int)swEndConditions_e.swEndCondBlind;
                
                // Use FeatureCut4 for robust cut extrusion with proper cut-specific options
                var feat = featMgr.FeatureCut4(
                    true,   // Sd: single direction
                    false,  // Flip
                    false,  // Dir2
                    endCondition,  // T1: end condition
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

        private static void EnsureSketchSelected(IModelDoc2 model, SelectionMgr selMgr)
        {
            try
            {
                if (HasSelectedSketch(selMgr))
                    return;

                try { model.ClearSelection2(true); } catch { }

                SelectData selData = null;
                try { selData = selMgr.CreateSelectData(); selData.Mark = 1; } catch { }

                var lastSketch = FindLastSketch(model);
                if (lastSketch != null)
                {
                    try { ((dynamic)lastSketch).Select4(false, selData); }
                    catch { try { ((dynamic)lastSketch).Select2(false, 0); } catch { } }
                    AddinStatusLogger.Log("ExtrudeCutHandler", $"Auto-selected last sketch '{lastSketch.Name}'.");
                    return;
                }

                try
                {
                    var lastFeature = model.Extension.GetLastFeatureAdded() as Feature;
                    if (lastFeature != null)
                    {
                        try { ((dynamic)lastFeature).Select4(false, selData); }
                        catch { try { ((dynamic)lastFeature).Select2(false, 0); } catch { } }
                        AddinStatusLogger.Log("ExtrudeCutHandler", $"Auto-selected last feature '{lastFeature.Name}' as cut profile fallback.");
                    }
                }
                catch { }
            }
            catch { }
        }

        private static bool HasSelectedSketch(SelectionMgr selMgr)
        {
            try
            {
                var selCount = selMgr.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= selCount; i++)
                {
                    if (selMgr.GetSelectedObjectType3(i, -1) == (int)swSelectType_e.swSelSKETCHES)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static Feature FindLastSketch(IModelDoc2 model)
        {
            try
            {
                Feature lastSketch = null;
                var feature = model.FirstFeature();
                while (feature != null)
                {
                    try
                    {
                        var typeName = feature.GetTypeName2();
                        if (!string.IsNullOrEmpty(typeName) && typeName.IndexOf("sketch", StringComparison.OrdinalIgnoreCase) >= 0)
                            lastSketch = feature;
                    }
                    catch { }

                    try { feature = feature.GetNextFeature(); }
                    catch { break; }
                }

                return lastSketch;
            }
            catch
            {
                return null;
            }
        }
    }
}
