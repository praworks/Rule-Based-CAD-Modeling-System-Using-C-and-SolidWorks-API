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
                if (diamMm <= 0)
                    return OperationResult.CreateFailure("Hole diameter must be > 0");
                double radius = PartFeatureHelpers.ToMeters(diamMm) / 2.0;
                string target = (step.Value<string>("target") ?? string.Empty).Trim().ToLowerInvariant();
                if (target == "all_corners")
                    return OperationResult.CreateFailure("target=all_corners is not supported by hole handler yet; use explicit coordinates or center.");

                double xMm = step.Value<double?>("x") ?? 0.0;
                double yMm = step.Value<double?>("y") ?? 0.0;
                if (target == "center")
                {
                    xMm = 0.0;
                    yMm = 0.0;
                }

                double x = PartFeatureHelpers.ToMeters(xMm);
                double y = PartFeatureHelpers.ToMeters(yMm);

                double depth = 0.0;
                bool isThroughAll = true;
                if (step.ContainsKey("depth"))
                {
                    depth = PartFeatureHelpers.ToMeters(step.Value<double>("depth"));
                    isThroughAll = false;
                }

                string faceHint = step.Value<string>("face")
                                ?? step.Value<string>("target_face")
                                ?? step.Value<string>("on_face")
                                ?? string.Empty;

                // Ensure we are not in a stale sketch before creating a feature-owned sketch.
                if (inSketch)
                {
                    try { sketchMgr.InsertSketch(true); } catch { }
                }

                try { model.ClearSelection2(true); } catch { }
                bool faceSelected = false;
                if (!string.IsNullOrWhiteSpace(faceHint))
                    faceSelected = TrySelectFace(model, faceHint);
                if (!faceSelected)
                    faceSelected = TrySelectLargestFace(model);
                if (!faceSelected)
                    return OperationResult.CreateFailure("Could not determine a face/plane for hole sketch.");

                try { sketchMgr.InsertSketch(true); } catch (Exception ex) { return OperationResult.CreateFailure($"Failed to start sketch for hole: {ex.Message}"); }

                object circle = null;
                try { circle = sketchMgr.CreateCircleByRadius(x, y, 0.0, radius); }
                catch (Exception ex) { return OperationResult.CreateFailure($"Failed to create hole sketch circle: {ex.Message}"); }
                if (circle == null)
                    return OperationResult.CreateFailure("Failed to create hole sketch circle.");

                // Exit sketch so cut can consume the profile.
                try { sketchMgr.InsertSketch(true); } catch (Exception ex) { return OperationResult.CreateFailure($"Failed to end hole sketch: {ex.Message}"); }
                EnsureSketchSelected(model);

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

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name, diameterMm = diamMm, throughAll = isThroughAll });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"hole failed: {ex.Message}");
            }
        }

        private static bool TrySelectFace(IModelDoc2 model, string faceHint)
        {
            try
            {
                var faceStep = new JObject { ["face"] = faceHint ?? string.Empty };
                var result = new FaceHandler().Execute(faceStep, model, null, null, false);
                return result != null && result.Success;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySelectLargestFace(IModelDoc2 model)
        {
            try
            {
                var part = model as IPartDoc;
                if (part == null) return false;
                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies == null || bodies.Length == 0) return false;
                var body = bodies[bodies.Length - 1] as IBody2;
                if (body == null) return false;
                var faces = (object[])body.GetFaces();
                if (faces == null || faces.Length == 0) return false;

                int bestIndex = -1;
                double bestArea = double.MinValue;
                for (int i = 0; i < faces.Length; i++)
                {
                    try
                    {
                        var area = (double)((dynamic)faces[i]).GetArea();
                        if (area > bestArea)
                        {
                            bestArea = area;
                            bestIndex = i;
                        }
                    }
                    catch { }
                }
                if (bestIndex < 0) return false;

                try { model.ClearSelection2(true); } catch { }
                var selMgr = (SelectionMgr)model.SelectionManager;
                var selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }
                try { ((dynamic)faces[bestIndex]).Select4(true, selData); }
                catch { try { ((dynamic)faces[bestIndex]).Select2(true, selData); } catch { } }

                return selMgr.GetSelectedObjectCount2(-1) > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureSketchSelected(IModelDoc2 model)
        {
            try
            {
                var selMgr = (SelectionMgr)model.SelectionManager;
                if (selMgr.GetSelectedObjectCount2(-1) > 0) return;

                Feature lastSketch = null;
                var feature = model.FirstFeature();
                while (feature != null)
                {
                    try
                    {
                        var type = feature.GetTypeName2() ?? string.Empty;
                        if (type.IndexOf("sketch", StringComparison.OrdinalIgnoreCase) >= 0)
                            lastSketch = feature;
                    }
                    catch { }

                    try { feature = feature.GetNextFeature(); }
                    catch { break; }
                }

                if (lastSketch == null) return;
                var selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }
                try { ((dynamic)lastSketch).Select4(false, selData); }
                catch { try { ((dynamic)lastSketch).Select2(false, 0); } catch { } }
            }
            catch { }
        }
    }
}
