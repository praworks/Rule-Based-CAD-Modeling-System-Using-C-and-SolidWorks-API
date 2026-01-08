using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "revolve" operation - creates a revolve feature (profile around axis)
    /// </summary>
    public class RevolveHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                // Revolve assumes a profile sketch and an axis/centerline are already defined and pre-selected.
                // If the caller is still editing the sketch, close it so the feature can be created.
                bool autoCloseSketch = step.Value<bool?>("close_sketch") ?? true;
                if (inSketch && autoCloseSketch && sketchMgr != null)
                {
                    try { sketchMgr.InsertSketch(true); inSketch = false; } catch { }
                }

                double angleDeg = step.Value<double?>("angle_deg") ?? step.Value<double?>("angle") ?? 360.0;
                double angleRad = angleDeg * Math.PI / 180.0;
                bool merge = step.Value<bool?>("merge") ?? true;
                bool thin = step.Value<bool?>("thin") ?? false;
                double thinThk = PartFeatureHelpers.ToMeters(step.Value<double?>("thin_thickness") ?? 0.0);

                var feat = TryInvokeRevolve(featMgr, angleRad, merge, thin, thinThk);
                if (feat == null)
                {
                    return OperationResult.CreateFailure("Revolve failed: ensure profile and axis/centerline are preselected in the same sketch");
                }

                try { model.ForceRebuild3(false); AddinStatusLogger.Log("RevolveHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }
                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name, angleDeg });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"revolve failed: {ex.Message}");
            }
        }

        private IFeature TryInvokeRevolve(IFeatureManager featMgr, double angleRad, bool merge, bool thin, double thinThk)
        {
            var t = featMgr.GetType();

            // Preferred SolidWorks signature (SW 2018+): FeatureRevolve2(bool,bool,bool,bool,bool,double,double,bool,bool,double,double,bool,bool,bool,double,double,bool,bool)
            var mi = t.GetMethod("FeatureRevolve2", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                try
                {
                    var obj = mi.Invoke(featMgr, new object[]
                    {
                        false, // FlipDir1
                        false, // FlipDir2
                        false, // BothDirections
                        thin,  // Thin
                        false, // ThinType (mid-plane default)
                        thinThk,
                        thinThk,
                        false, // DraftOutward1
                        false, // DraftOutward2
                        0.0,   // DraftAngle1
                        0.0,   // DraftAngle2
                        merge, // Merge
                        true,  // FeatureScope
                        true,  // AutoSelect
                        angleRad, // Angle1 (radians)
                        angleRad, // Angle2
                        false, // Dir1MidPlane
                        false  // Dir2MidPlane
                    }) as IFeature;
                    if (obj != null) return obj;
                }
                catch { }
            }

            // Fallback to older InsertRevolve2 signature if present
            mi = t.GetMethod("InsertRevolve2", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                try
                {
                    var obj = mi.Invoke(featMgr, new object[]
                    {
                        false, false, false, false, false,
                        0.0, 0.0,
                        false, false,
                        0.0, 0.0,
                        merge,
                        angleRad,
                        angleRad
                    }) as IFeature;
                    if (obj != null) return obj;
                }
                catch { }
            }

            // Last-chance minimal API
            mi = t.GetMethod("InsertRevolve", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                try
                {
                    var obj = mi.Invoke(featMgr, new object[] { angleRad, angleRad, merge }) as IFeature;
                    if (obj != null) return obj;
                }
                catch { }
            }

            return null;
        }
    }
}
