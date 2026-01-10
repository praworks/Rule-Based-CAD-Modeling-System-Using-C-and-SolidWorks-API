using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    public class UPathHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw upath");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                double cx = ToMeters(step.Value<double?>("cx") ?? 0);
                double cy = ToMeters(step.Value<double?>("cy") ?? 0);
                double width = ToMeters(step.Value<double?>("width") ?? 20);
                double height = ToMeters(step.Value<double?>("height") ?? 40);
                double radius = step.Value<double?>("radius") != null ? ToMeters(step.Value<double>("radius")) : Math.Min(width / 2.0, height / 4.0);

                double topY = cy + height / 2.0;
                double bottomY = cy - height / 2.0 + radius;
                double leftX = cx - width / 2.0;
                double rightX = cx + width / 2.0;

                var l1 = sketchMgr.CreateLine(leftX, topY, 0, leftX, bottomY + radius, 0);
                if (l1 == null) return OperationResult.CreateFailure("Failed to create left leg of U path");

                double acx = cx;
                double acy = bottomY;

                double sx = leftX;
                double sy = bottomY + radius;
                double ex = rightX;
                double ey = bottomY + radius;

                var arc = sketchMgr.CreateArc(acx, acy, 0, sx, sy, 0, ex, ey, 0, 0);
                if (arc == null) return OperationResult.CreateFailure("Failed to create bottom arc of U path");

                var l2 = sketchMgr.CreateLine(rightX, bottomY + radius, 0, rightX, topY, 0);
                if (l2 == null) return OperationResult.CreateFailure("Failed to create right leg of U path");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"upath failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
