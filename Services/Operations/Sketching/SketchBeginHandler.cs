using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
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
}
