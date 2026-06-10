using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "loft" operation - creates a loft feature (blending multiple profiles)
    /// </summary>
    public class LoftHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                if (inSketch && sketchMgr != null)
                {
                    try { sketchMgr.InsertSketch(true); } catch { }
                }

                var profiles = GetMostRecentClosedProfiles(model);
                if (profiles.Count < 2)
                    return OperationResult.CreateFailure("Loft requires two closed sketch profiles");

                model.ClearSelection2(true);

                if (!SelectProfile(model, profiles[profiles.Count - 2], append: false))
                    return OperationResult.CreateFailure("Could not select the first loft profile");
                if (!SelectProfile(model, profiles[profiles.Count - 1], append: true))
                    return OperationResult.CreateFailure("Could not select the second loft profile");

                var feat = featMgr.InsertProtrusionBlend2(
                    false,
                    true,
                    false,
                    1.0,
                    0,
                    0,
                    0.0,
                    0.0,
                    true,
                    true,
                    false,
                    0.0,
                    0.0,
                    0,
                    true,
                    true,
                    true,
                    0);

                if (feat == null)
                {
                    feat = featMgr.InsertProtrusionBlend(
                        false,
                        true,
                        false,
                        1.0,
                        0,
                        0,
                        0.0,
                        0.0,
                        true,
                        true,
                        false,
                        0.0,
                        0.0,
                        0,
                        true,
                        true,
                        true);
                }

                if (feat == null)
                    return OperationResult.CreateFailure("SolidWorks failed to create the loft feature from the selected profiles");

                try
                {
                    model.ForceRebuild3(false);
                    model.GraphicsRedraw2();
                }
                catch { }

                return OperationResult.CreateSuccess(
                    stillInSketch: false,
                    data: new { featureName = feat.Name, profileCount = 2 });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"loft failed: {ex.Message}");
            }
        }

        private static List<IFeature> GetMostRecentClosedProfiles(IModelDoc2 model)
        {
            var profiles = new List<IFeature>();
            try
            {
                var feat = model.FirstFeature();
                while (feat != null)
                {
                    string typeName = string.Empty;
                    try { typeName = feat.GetTypeName2() ?? string.Empty; } catch { }

                    if (typeName.Equals("ProfileFeature", StringComparison.OrdinalIgnoreCase)
                        || typeName.Equals("3DProfileFeature", StringComparison.OrdinalIgnoreCase)
                        || typeName.Equals("Sketch", StringComparison.OrdinalIgnoreCase))
                    {
                        profiles.Add(feat);
                    }

                    feat = feat.GetNextFeature();
                }
            }
            catch { }

            return profiles;
        }

        private static bool SelectProfile(IModelDoc2 model, IFeature feature, bool append)
        {
            if (model == null || feature == null)
                return false;

            try
            {
                if (model.Extension.SelectByID2(feature.Name, "SKETCH", 0, 0, 0, append, 1, null, 0))
                    return true;
            }
            catch { }

            try
            {
                var specific = feature.GetSpecificFeature2();
                if (specific is ISketch sketch)
                {
                    var segments = sketch.GetSketchSegments() as object[];
                    if (segments != null && segments.Length > 0)
                    {
                        var any = false;
                        for (var i = 0; i < segments.Length; i++)
                        {
                            if (segments[i] is ISketchSegment seg)
                            {
                                try
                                {
                                    any |= ((IEntity)seg).Select2(append || i > 0, 1);
                                }
                                catch { }
                            }
                        }

                        if (any)
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
