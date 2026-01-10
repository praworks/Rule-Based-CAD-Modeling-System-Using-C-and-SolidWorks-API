using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
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
}
