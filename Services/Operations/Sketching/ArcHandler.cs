using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    public class ArcHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw arc");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                double cx = ToMeters(step.Value<double?>("cx") ?? 0);
                double cy = ToMeters(step.Value<double?>("cy") ?? 0);
                double r = ToMeters(step.Value<double?>("r") ?? step.Value<double?>("radius") ?? 0);
                double startDeg = step.Value<double?>("start_angle") ?? step.Value<double?>("start") ?? 0;
                double endDeg = step.Value<double?>("end_angle") ?? step.Value<double?>("end") ?? 90;

                if (r <= 0)
                    return OperationResult.CreateFailure("Arc radius must be > 0");

                double startRad = startDeg * Math.PI / 180.0;
                double endRad = endDeg * Math.PI / 180.0;

                double sx = cx + r * Math.Cos(startRad);
                double sy = cy + r * Math.Sin(startRad);
                double ex = cx + r * Math.Cos(endRad);
                double ey = cy + r * Math.Sin(endRad);

                var arc = sketchMgr.CreateArc(cx, cy, 0, sx, sy, 0, ex, ey, 0, 0);
                if (arc == null)
                    return OperationResult.CreateFailure("Failed to create arc");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"arc failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
