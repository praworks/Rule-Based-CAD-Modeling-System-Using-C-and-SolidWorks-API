using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "sweep" operation - creates a sweep feature (profile along path)
    /// </summary>
    public class SweepHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                // Sweep expects profile then path to be pre-selected by caller. Close sketch if still editing.
                bool autoCloseSketch = step.Value<bool?>("close_sketch") ?? true;
                if (inSketch && autoCloseSketch && sketchMgr != null)
                {
                    try { sketchMgr.InsertSketch(true); inSketch = false; } catch { }
                }

                bool merge = step.Value<bool?>("merge") ?? true;
                double twistDeg = step.Value<double?>("twist_deg") ?? 0.0;
                double twistRad = twistDeg * Math.PI / 180.0;

                var feat = TryInvokeSweep(featMgr, merge, twistRad);
                if (feat == null)
                {
                    return OperationResult.CreateFailure("Sweep failed: ensure profile is selected first, then path, and they are valid for sweep");
                }

                try { model.ForceRebuild3(false); AddinStatusLogger.Log("SweepHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }
                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name, merge, twistDeg });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"sweep failed: {ex.Message}");
            }
        }

        private IFeature TryInvokeSweep(IFeatureManager featMgr, bool merge, double twistRad)
        {
            var t = featMgr.GetType();

            // Modern signature (InsertProtrusionSwept4) with most options exposed
            var mi = t.GetMethod("InsertProtrusionSwept4", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                try
                {
                    var obj = mi.Invoke(featMgr, new object[]
                    {
                        true,   // Boss
                        false,  // Thin
                        false,  // Keep normal constant
                        false,  // Twist along path
                        0,      // FeatureScope (0 = auto)
                        true,   // AutoSelect bodies
                        0.0,    // ThinWallType
                        0.0,    // Thickness1
                        0.0,    // Thickness2
                        true,   // Direction
                        0,      // PathAlignmentType
                        merge,  // Merge result
                        0,      // GuideCurvesOption
                        0,      // TwistControlType
                        twistRad, // Twist angle (radians)
                        false,  // Display curvature combs
                        0.0,    // Start tangency
                        0.0     // End tangency
                    }) as IFeature;
                    if (obj != null) return obj;
                }
                catch { }
            }

            // Older fallback with limited options
            mi = t.GetMethod("InsertProtrusionSwept2", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                try
                {
                    var obj = mi.Invoke(featMgr, new object[] { true, false, false, false, 0, true }) as IFeature;
                    if (obj != null) return obj;
                }
                catch { }
            }

            // Minimal legacy API
            mi = t.GetMethod("InsertProtrusionSwept", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                try
                {
                    var obj = mi.Invoke(featMgr, new object[] { true, false, false }) as IFeature;
                    if (obj != null) return obj;
                }
                catch { }
            }

            return null;
        }
    }
}
