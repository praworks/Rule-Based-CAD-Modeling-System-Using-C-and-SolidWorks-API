using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    /// <summary>
    /// Handler for "sketch_begin" operation - starts sketch editing mode
    /// </summary>
    public class SketchBeginHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                sketchMgr.InsertSketch(true);
                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"sketch_begin failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "sketch_end" operation - finishes sketch editing and creates sketch feature
    /// </summary>
    public class SketchEndHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Not currently in sketch mode");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                sketchMgr.InsertSketch(true);
                return OperationResult.CreateSuccess(stillInSketch: false);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"sketch_end failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "rectangle_center" operation - draws rectangle centered at (cx, cy)
    /// </summary>
    public class RectangleCenterHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw rectangle");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                double cx = ToMeters(step.Value<double?>("cx") ?? 0);
                double cy = ToMeters(step.Value<double?>("cy") ?? 0);
                double w = ToMeters(step.Value<double?>("w") ?? 0);
                double h = ToMeters(step.Value<double?>("h") ?? 0);

                if (w <= 0 || h <= 0)
                    return OperationResult.CreateFailure("Rectangle width and height must be > 0");

                double x1 = cx - w / 2.0;
                double y1 = cy - h / 2.0;
                double x2 = cx + w / 2.0;
                double y2 = cy + h / 2.0;

                var rect = sketchMgr.CreateCenterRectangle(cx, cy, 0, x2, y2, 0);
                if (rect == null)
                    return OperationResult.CreateFailure("Failed to create rectangle");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"rectangle_center failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }

    /// <summary>
    /// Handler for "circle_center" operation - draws circle at (cx, cy) with given radius or diameter
    /// </summary>
    public class CircleCenterHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw circle");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                double cx = ToMeters(step.Value<double?>("cx") ?? 0);
                double cy = ToMeters(step.Value<double?>("cy") ?? 0);
                double r = step["r"] != null ? 
                    ToMeters(step.Value<double>("r")) : 
                    (step["diameter"] != null ? 
                        ToMeters(step.Value<double>("diameter")) / 2.0 : 0);

                if (r <= 0)
                    return OperationResult.CreateFailure("Circle radius or diameter must be > 0");

                var circ = sketchMgr.CreateCircleByRadius(cx, cy, 0, r);
                if (circ == null)
                    return OperationResult.CreateFailure("Failed to create circle");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"circle_center failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }

    /// <summary>
    /// Handler for "line" operation - draws line segment between two points
    /// </summary>
    public class LineHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw line");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                double x1 = ToMeters(step.Value<double?>("x1") ?? 0);
                double y1 = ToMeters(step.Value<double?>("y1") ?? 0);
                double x2 = ToMeters(step.Value<double?>("x2") ?? 0);
                double y2 = ToMeters(step.Value<double?>("y2") ?? 0);

                var line = sketchMgr.CreateLine(x1, y1, 0, x2, y2, 0);
                if (line == null)
                    return OperationResult.CreateFailure("Failed to create line");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"line failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }

    /// <summary>
    /// Handler for "arc" operation - draws arc (not yet fully specified)
    /// </summary>
    public class ArcHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw arc");

                // TODO: Implement arc creation based on parameters
                // Common parameters: center (cx, cy), radius, start_angle, end_angle
                return OperationResult.CreateFailure("Arc operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"arc failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "dimension" operation - adds dimension to sketch geometry
    /// </summary>
    public class DimensionHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to add dimension");

                // TODO: Implement dimension constraint
                return OperationResult.CreateFailure("Dimension operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"dimension failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "constraint" operation - adds constraint to sketch geometry
    /// </summary>
    public class ConstraintHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to add constraint");

                // TODO: Implement sketch constraints (horizontal, vertical, perpendicular, etc.)
                return OperationResult.CreateFailure("Constraint operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"constraint failed: {ex.Message}");
            }
        }
    }
}
