using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    public class ConstructionLineHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to draw construction line");
                if (sketchMgr == null)
                    return OperationResult.CreateFailure("Sketch manager not available");

                double x1 = ToMeters(step.Value<double?>("x1") ?? 0);
                double y1 = ToMeters(step.Value<double?>("y1") ?? 0);
                double x2 = ToMeters(step.Value<double?>("x2") ?? 0);
                double y2 = ToMeters(step.Value<double?>("y2") ?? 0);

                var line = sketchMgr.CreateCenterLine(x1, y1, 0, x2, y2, 0);
                if (line == null)
                    return OperationResult.CreateFailure("Failed to create construction line");

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"construction_line failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
