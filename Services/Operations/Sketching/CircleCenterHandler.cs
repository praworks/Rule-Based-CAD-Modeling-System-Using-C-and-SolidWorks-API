using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
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

                object circ = sketchMgr.CreateCircleByRadius(cx, cy, 0, r);
                if (circ == null)
                    return OperationResult.CreateFailure("Failed to create circle");

                try
                {
                    var isConstruction = step.Value<bool?>("construction") ?? step.Value<bool?>("construction_circle") ?? false;
                    if (isConstruction)
                    {
                        try
                        {
                            var mi = circ.GetType().GetMethod("SetConstruction");
                            if (mi != null)
                            {
                                mi.Invoke(circ, new object[] { true });
                            }
                            else
                            {
                                var pi = circ.GetType().GetProperty("Construction");
                                if (pi != null && pi.CanWrite) pi.SetValue(circ, true);
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                return OperationResult.CreateSuccess(stillInSketch: true);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"circle_center failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
