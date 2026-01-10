using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
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
}
