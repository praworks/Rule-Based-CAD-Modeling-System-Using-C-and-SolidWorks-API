using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
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

                double depth = ToMeters(step.Value<double?>("depth") ?? 0);
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

                return OperationResult.CreateSuccess(stillInSketch: false, data: feat);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"extrude failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }

    /// <summary>
    /// Handler for "revolve" operation - creates a revolved feature
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

                double radius = ToMeters(step.Value<double?>("radius") ?? step.Value<double?>("r") ?? 0);
                if (radius <= 0)
                    return OperationResult.CreateFailure("Fillet radius must be > 0");

                // Get all bodies and count edges
                var part = (IPartDoc)model;
                if (part == null)
                    return OperationResult.CreateFailure("Not a part document");

                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false);
                if (bodies == null || bodies.Length == 0)
                    return OperationResult.CreateFailure("No solid bodies found");

                int edgeCount = 0;
                model.ClearSelection2(true);

                foreach (IBody2 body in bodies)
                {
                    if (body == null) continue;
                    var edges = (object[])body.GetEdges();
                    if (edges == null) continue;

                    foreach (var eObj in edges)
                    {
                        var edgeDyn = eObj as dynamic;
                        bool sel = false;
                        try { sel = edgeDyn?.Select4(true, null) ?? false; } catch { }
                        if (!sel)
                        {
                            try { sel = edgeDyn?.Select2(true, null) ?? false; } catch { }
                        }
                        if (sel) edgeCount++;
                    }
                }

                if (edgeCount == 0)
                    return OperationResult.CreateFailure("No edges found to fillet (selection API not available)");

                // Attempt fillet using IModelDoc2.FeatureFillet2 (7 params, returns int status)
                int status = 0;
                try
                {
                    status = model.FeatureFillet2(radius, true, true, false, 0, 1, new double[] { radius });
                }
                catch (Exception ex)
                {
                    return OperationResult.CreateFailure($"Fillet API call failed: {ex.Message}");
                }

                model.ClearSelection2(true);

                if (status == 0)
                    return OperationResult.CreateFailure($"Fillet feature creation failed (selected {edgeCount} edges)");

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { edgeCount, radiusMm = step.Value<double?>("radius"), status });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"fillet failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
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

                double distance = ToMeters(step.Value<double?>("distance") ?? step.Value<double?>("d") ?? 0);
                if (distance <= 0)
                    return OperationResult.CreateFailure("Chamfer distance must be > 0");

                // TODO: Implement chamfer edge selection
                return OperationResult.CreateFailure("Chamfer operation not yet fully implemented - requires edge selection logic");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"chamfer failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
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

                double depth = ToMeters(step.Value<double?>("depth") ?? 0);

                // TODO: Implement pocket (similar to extrude but as cut operation)
                return OperationResult.CreateFailure("Pocket operation not yet fully implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"pocket failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
