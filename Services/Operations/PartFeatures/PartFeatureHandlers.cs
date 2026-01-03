using System;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    internal static class PartFeatureHelpers
    {
        public static double ToMeters(double mm) => mm / 1000.0;
    }
    /// <summary>
    /// Handler for "extrude" operation - creates an extrusion (boss or cut)
    /// </summary>
    public class ExtrudeHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

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
                try { model.ForceRebuild3(false); AddinStatusLogger.Log("ExtrudeHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"extrude failed: {ex.Message}");
            }
            }
        }

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

                // TODO: Implement revolve (requires axis selection and profile sketch)
                return OperationResult.CreateFailure("Revolve operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"revolve failed: {ex.Message}");
            }
        }
    }

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

                // TODO: Implement sweep (requires profile and path sketches)
                return OperationResult.CreateFailure("Sweep operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"sweep failed: {ex.Message}");
            }
        }
    }

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

                // TODO: Implement loft (requires multiple profile sketches)
                return OperationResult.CreateFailure("Loft operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"loft failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "fillet" operation - adds fillet to edges
    /// </summary>
    public class FilletHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                double rawRadiusMm = step.Value<double?>("radius") ?? step.Value<double?>("r") ?? 0;
                double radiusMeters = PartFeatureHelpers.ToMeters(rawRadiusMm);
                if (radiusMeters <= 0)
                    return OperationResult.CreateFailure("Fillet radius must be > 0");

                AddinStatusLogger.Log("FilletHandler", $"Applying Radius: {rawRadiusMm}mm ({radiusMeters}m)");

                try { model.ForceRebuild3(false); } catch { }
                try { model.ClearSelection2(true); } catch { }

                SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;
                SelectData selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }

                int edgeCount = 0;
                IFeature filletFeat = null;

                var part = (IPartDoc)model;
                if (part == null) return OperationResult.CreateFailure("Not a part document");

                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies != null && bodies.Length > 0)
                {
                    var liveBody = bodies[bodies.Length - 1] as IBody2;
                    if (liveBody != null)
                    {
                        var edges = (object[])liveBody.GetEdges();
                        if (edges != null)
                        {
                            foreach (var eObj in edges)
                            {
                                try { ((dynamic)eObj).Select4(true, selData); } catch { try { ((dynamic)eObj).Select2(true, selData); } catch { } }
                                edgeCount++;
                            }
                        }
                    }
                }

                if (edgeCount == 0)
                    return OperationResult.CreateFailure("No edges found to fillet.");

                // Try batch FeatureFillet3
                try
                {
                    dynamic dynFeatMgr = model.FeatureManager;
                    var f = dynFeatMgr.FeatureFillet3(3, radiusMeters, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    if (f != null) filletFeat = f as IFeature;
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                {
                    AddinStatusLogger.Log("FilletHandler", $"FeatureFillet3 not available on this SolidWorks version: {bindEx.Message}");
                }
                catch (Exception ex)
                {
                    AddinStatusLogger.Log("FilletHandler", $"FeatureFillet3 error: {ex.Message}");
                }

                // Fallback to legacy FeatureFillet2 if needed
                if (filletFeat == null)
                {
                    try
                    {
                        int status = model.FeatureFillet2(radiusMeters, true, true, false, 0, 1, new double[] { radiusMeters });
                        AddinStatusLogger.Log("FilletHandler", $"Legacy FeatureFillet2 returned status={status}");
                        if (status == 0)
                            return OperationResult.CreateFailure($"Fillet feature creation failed (selected {edgeCount} edges)");
                    }
                    catch (Exception ex)
                    {
                        return OperationResult.CreateFailure($"Fillet API call failed: {ex.Message}");
                    }
                }

                try { model.ForceRebuild3(false); } catch { }
                model.ClearSelection2(true);

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { edgeCount, radiusMm = rawRadiusMm, featureName = filletFeat?.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"fillet failed: {ex.Message}");
            }
        }

    }

    /// <summary>
    /// Handler for "chamfer" operation - adds chamfer to edges
    /// </summary>
    public class ChamferHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                double rawDistMm = step.Value<double?>("distance") ?? step.Value<double?>("d") ?? step.Value<double?>("dist") ?? 0;
                double distance = PartFeatureHelpers.ToMeters(rawDistMm);
                if (distance <= 0)
                    return OperationResult.CreateFailure("Chamfer distance must be > 0");

                AddinStatusLogger.Log("ChamferHandler", $"Applying Chamfer: {rawDistMm}mm ({distance}m)");

                try { model.ForceRebuild3(false); } catch { }
                try { model.ClearSelection2(true); } catch { }

                SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;
                SelectData selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }

                int edgeCount = 0;
                IFeature chamferFeat = null;

                var part = (IPartDoc)model;
                if (part == null) return OperationResult.CreateFailure("Not a part document");

                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies != null && bodies.Length > 0)
                {
                    var liveBody = bodies[bodies.Length - 1] as IBody2;
                    if (liveBody != null)
                    {
                        var edges = (object[])liveBody.GetEdges();
                        if (edges != null)
                        {
                            foreach (var eObj in edges)
                            {
                                try { ((dynamic)eObj).Select4(true, selData); } catch { try { ((dynamic)eObj).Select2(true, selData); } catch { } }
                                edgeCount++;
                            }
                        }
                    }
                }

                if (edgeCount == 0)
                    return OperationResult.CreateFailure("No edges found to chamfer.");

                // Try newer FeatureChamfer API (FeatureManager)
                try
                {
                    dynamic dynFeatMgr = model.FeatureManager;
                    try
                    {
                        var f = dynFeatMgr.FeatureChamfer3(0, distance, 0, 0, 0, 0, 0);
                        if (f != null) chamferFeat = f as IFeature;
                    }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                    {
                        AddinStatusLogger.Log("ChamferHandler", $"FeatureChamfer3 not available: {bindEx.Message}");
                    }
                    catch (Exception ex)
                    {
                        AddinStatusLogger.Log("ChamferHandler", $"FeatureChamfer3 error: {ex.Message}");
                    }
                }
                catch (Exception) { }

                // Fallback to legacy ModelDoc API if needed
                if (chamferFeat == null)
                {
                    try
                    {
                        // Many SolidWorks installations expose FeatureChamfer2 on the model as a legacy helper that returns an int status
                        dynamic dynModel = model;
                        try
                        {
                            int status = dynModel.FeatureChamfer2(distance, true, 0, 0, 0);
                            AddinStatusLogger.Log("ChamferHandler", $"Legacy FeatureChamfer2 returned status={status}");
                            if (status == 0)
                                return OperationResult.CreateFailure($"Chamfer feature creation failed (selected {edgeCount} edges)");
                        }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                        {
                            AddinStatusLogger.Log("ChamferHandler", $"Legacy FeatureChamfer2 not available: {bindEx.Message}");
                            return OperationResult.CreateFailure("Chamfer API not available on this SolidWorks version");
                        }
                    }
                    catch (Exception ex)
                    {
                        return OperationResult.CreateFailure($"Chamfer API call failed: {ex.Message}");
                    }
                }

                try { model.ForceRebuild3(false); } catch { }
                model.ClearSelection2(true);

                // Delegate to ChamferOP helper
                return ChamferOP.ApplyChamfer(step, model, featMgr);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"chamfer failed: {ex.Message}");
            }
        }

    }

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

                // TODO: Implement hole (typically: sketch circle + extrude as cut)
                // Parameters: x, y, diameter, depth (or through_all)
                return OperationResult.CreateFailure("Hole operation not yet implemented - use sketch circle + extrude cut");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"hole failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "pocket" operation - creates a pocket (recessed feature)
    /// </summary>
    public class PocketHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                double depth = PartFeatureHelpers.ToMeters(step.Value<double?>("depth") ?? 0);

                // TODO: Implement pocket (similar to extrude but as cut operation)
                return OperationResult.CreateFailure("Pocket operation not yet fully implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"pocket failed: {ex.Message}");
            }
        }

    }
}
