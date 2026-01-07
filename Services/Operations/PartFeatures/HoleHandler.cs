using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "hole" operation - creates a hole at specified location
    /// </summary>
    public class HoleHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null || sketchMgr == null)
                    return OperationResult.CreateFailure("Feature or sketch manager not available");

                // Read parameters (units: input expected in mm)
                double diamMm = step.Value<double?>("diameter") ?? step.Value<double?>("diam") ?? 5.0;
                double radius = PartFeatureHelpers.ToMeters(diamMm) / 2.0;
                double x = PartFeatureHelpers.ToMeters(step.Value<double?>("x") ?? 0.0);
                double y = PartFeatureHelpers.ToMeters(step.Value<double?>("y") ?? 0.0);

                double depth = 0.0;
                bool isThroughAll = true;
                if (step.ContainsKey("depth"))
                {
                    depth = PartFeatureHelpers.ToMeters(step.Value<double>("depth"));
                    isThroughAll = false;
                }

                // Create a quick sketch circle at (x,y)
                bool startedSketch = false;
                if (!inSketch)
                {
                    try { sketchMgr.InsertSketch(true); startedSketch = true; } catch { }
                }

                try
                {
                    sketchMgr.CreateCircleByRadius(x, y, 0.0, radius);
                }
                catch (Exception) { }

                if (startedSketch)
                {
                    try { sketchMgr.InsertSketch(true); } catch { }
                }

                // Determine end condition
                int endCondition = isThroughAll ? (int)swEndConditions_e.swEndCondThroughAll : (int)swEndConditions_e.swEndCondBlind;

                // Use FeatureCut4 to create a robust cut feature with cut-specific options
                var feat = featMgr.FeatureCut4(
                    true,   // Sd: single direction
                    false,  // Flip
                    false,  // Dir2
                    endCondition, // T1
                    0,      // T2
                    depth,  // D1
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
                    false,  // NormalCut (set true for sheet metal if required)
                    true,   // UseFeat (apply feature)
                    true,   // UseAutoSelect (let SW pick correct bodies/profile)
                    false,  // AssemblyFeatureScope
                    true,   // AutoSelect - smarter body selection
                    false,  // Rematerialize
                    0,      // StartOffset (int)
                    0.0,    // StartOffset2 (double)
                    false,  // reserved boolean (interop expects an extra bool here)
                    true    // OptimizeGeometry - enable for complex cuts
                );

                if (feat == null)
                    return OperationResult.CreateFailure("Hole/cut operation failed");

                try { model.ForceRebuild3(false); AddinStatusLogger.Log("HoleHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"hole failed: {ex.Message}");
            }
        }
    }
}
