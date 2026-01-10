using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    public class ConstraintHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to add constraint");

                return OperationResult.CreateFailure("Constraint operation not yet implemented");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"constraint failed: {ex.Message}");
            }
        }
    }
}
