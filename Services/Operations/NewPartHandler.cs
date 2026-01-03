using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations
{
    internal class NewPartHandler : IOperationHandler
    {
        public OperationResult Execute(JObject stepData, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch, out bool inSketchAfter)
        {
            inSketchAfter = false;
            try
            {
                var swApp = (ISldWorks)System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application");
                if (swApp == null)
                    return OperationResult.Fail("SolidWorks application not available");

                var newModel = (IModelDoc2)swApp.NewPart();
                if (newModel == null)
                    return OperationResult.Fail("Failed to create new part (check default template)");

                int actErr = 0;
                swApp.ActivateDoc3(newModel.GetTitle(), true, (int)SolidWorks.Interop.swconst.swRebuildOptions_e.swRebuildAll, ref actErr);
                
                return OperationResult.Ok();
            }
            catch (System.Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }
    }
}
