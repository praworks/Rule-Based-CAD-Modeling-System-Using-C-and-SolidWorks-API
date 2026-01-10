using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    public class TangentArcHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw tangent arc");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                var fromXmm = step.Value<double?>("from_x");
                var fromYmm = step.Value<double?>("from_y");
                var toXmm = step.Value<double?>("to_x");
                var toYmm = step.Value<double?>("to_y");
                var rmm = step.Value<double?>("radius");

                if (fromXmm == null || fromYmm == null || toXmm == null || toYmm == null || rmm == null)
                    return OperationResult.CreateFailure("tangent_arc requires from_x,from_y,to_x,to_y and radius (all in mm)");

                double x1 = ToMeters(fromXmm.Value);
                double y1 = ToMeters(fromYmm.Value);
                double x2 = ToMeters(toXmm.Value);
                double y2 = ToMeters(toYmm.Value);
                double r = ToMeters(rmm.Value);

                var dx = x2 - x1;
                var dy = y2 - y1;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= 1e-9)
                    return OperationResult.CreateFailure("from and to points are identical");

                if (r <= d / 2.0)
                    return OperationResult.CreateFailure("radius too small for the chord length");

                var mx = (x1 + x2) / 2.0;
                var my = (y1 + y2) / 2.0;
                var h = Math.Sqrt(r * r - (d / 2.0) * (d / 2.0));
                var ux = -dy / d;
                var uy = dx / d;
                var dir = (step.Value<string>("direction") ?? "ccw").ToLowerInvariant();
                double cx = mx + (dir == "cw" ? -ux * h : ux * h);
                double cy = my + (dir == "cw" ? -uy * h : uy * h);

                double sx = x1;
                double sy = y1;
                double ex = x2;
                double ey = y2;

                var arc = sketchMgr.CreateArc(cx, cy, 0, sx, sy, 0, ex, ey, 0, 0);
                if (arc == null)
                    return OperationResult.CreateFailure("Failed to create tangent arc");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"tangent_arc failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
