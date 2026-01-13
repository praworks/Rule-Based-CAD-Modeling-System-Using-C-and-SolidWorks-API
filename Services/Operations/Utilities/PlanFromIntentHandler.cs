using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Utilities
{
    /// <summary>
    /// Handler for "plan_from_intent" operation.
    /// </summary>
    public class PlanFromIntentHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            if (model == null)
                return OperationResult.CreateFailure("Model not initialized");

            var intent = step.Value<string>("intent") ?? step.Value<string>("text") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(intent))
                return OperationResult.CreateFailure("Missing intent field");

            AddinStatusLogger.Log("PlanFromIntent", "plan_from_intent is not supported in StepExecutor; use orchestration to expand intent.");
            return OperationResult.CreateFailure("plan_from_intent requires orchestration (LLM planning not allowed in executor)");
        }
    }
}
