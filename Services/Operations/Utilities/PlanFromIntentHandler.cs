using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Utilities
{
    /// <summary>
    /// Handler for "plan_from_intent" operation - asks LLM to generate steps based on user intent and current model state.
    /// Returns a JArray of steps that the executor can immediately run.
    /// </summary>
    public class PlanFromIntentHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                var intent = step.Value<string>("intent") ?? step.Value<string>("text") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(intent))
                    return OperationResult.CreateFailure("Missing intent field");

                bool useModelFacts = step.Value<bool?>("use_model_facts") ?? true;
                JObject facts = null;

                if (useModelFacts)
                {
                    // Try to get cached facts first
                    facts = ModelContextStore.GetFacts(model.GetTitle());
                    
                    // If not cached, inspect now
                    if (facts == null)
                    {
                        try
                        {
                            facts = ModelInspector.InspectModel(model, emitLogs: false);
                            ModelContextStore.SetFacts(model.GetTitle(), facts);
                        }
                        catch (Exception inspEx)
                        {
                            AddinStatusLogger.Log("PlanFromIntent", $"Failed to inspect model: {inspEx.Message}");
                        }
                    }
                }

                // Ask LLM to plan steps based on intent + facts
                var steps = ClarificationService.PlanFromIntent(intent, facts);
                
                if (steps == null || steps.Count == 0)
                    return OperationResult.CreateFailure("LLM did not return valid steps for intent");

                AddinStatusLogger.Log("PlanFromIntent", $"Generated {steps.Count} steps from intent: {intent}");

                // Return the steps array so the executor can splice them in
                return OperationResult.CreateSuccess(stillInSketch: inSketch, data: new { steps, intent });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"plan_from_intent failed: {ex.Message}");
            }
        }
    }
}
