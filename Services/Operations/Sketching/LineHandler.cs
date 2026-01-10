using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
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

                var x1mm = step.Value<double?>("x1");
                var y1mm = step.Value<double?>("y1");
                var x2mm = step.Value<double?>("x2");
                var y2mm = step.Value<double?>("y2");

                if (x1mm == null || y1mm == null || x2mm == null || y2mm == null)
                    return OperationResult.CreateFailure("line requires numeric x1,y1,x2,y2 coordinates");

                double x1 = ToMeters(x1mm.Value);
                double y1 = ToMeters(y1mm.Value);
                double x2 = ToMeters(x2mm.Value);
                double y2 = ToMeters(y2mm.Value);

                var dx = x2 - x1;
                var dy = y2 - y1;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 1e-6)
                    return OperationResult.CreateFailure("line coordinates specify zero-length segment");

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
}
