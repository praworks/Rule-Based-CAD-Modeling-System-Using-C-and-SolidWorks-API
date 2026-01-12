using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using System.Windows.Forms;
using SolidWorks.Interop.swconst;

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
                // Log whether there's an active document and identify it for debugging
                try
                {
                    if (model == null)
                        AddinStatusLogger.Log("PlanFromIntent", "No active SolidWorks document (model == null)");
                    else
                    {
                        string path = "(unsaved)";
                        try { path = model.GetPathName(); } catch { }
                        string title = "(unknown)";
                        try { title = model.GetTitle(); } catch { }
                        AddinStatusLogger.Log("PlanFromIntent", $"Active document present: title={title} path={path}");
                    }
                }
                catch { }

                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                var intent = step.Value<string>("intent") ?? step.Value<string>("text") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(intent))
                    return OperationResult.CreateFailure("Missing intent field");

                // Log selection state immediately so users can confirm the add-in
                // detected the SolidWorks selection when planning from intent.
                try
                {
                    var selMgrQuick = model.SelectionManager as ISelectionMgr;
                    int selCountQuick = selMgrQuick?.GetSelectedObjectCount2(-1) ?? 0;
                    if (selCountQuick > 0)
                    {
                        int firstTypeQuick = 0;
                        try { firstTypeQuick = selMgrQuick.GetSelectedObjectType3(1, -1); } catch { }
                        var msg = $"Selection present at planning: count={selCountQuick} firstType={firstTypeQuick}";
                        AddinStatusLogger.Log("PlanFromIntent", msg);
                        try { MessageBox.Show(msg, "AICAD Selection", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
                    }
                    else
                    {
                        AddinStatusLogger.Log("PlanFromIntent", "No selection present at planning time");
                    }
                }
                catch (Exception ex)
                {
                    AddinStatusLogger.Log("PlanFromIntent", $"Selection quick-check failed: {ex.Message}");
                }

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

                    // Additionally, capture current selection from the active model and expose
                    // it to the LLM so prompts like "make 4 holes on this face" can be resolved.
                    try
                    {
                        var selMgr = model.SelectionManager as ISelectionMgr;
                        int selCount = selMgr?.GetSelectedObjectCount2(-1) ?? 0;
                        if (selCount > 0)
                        {
                            if (facts == null) facts = new JObject();

                            for (int i = 1; i <= selCount; i++)
                            {
                                try
                                {
                                    int selType = selMgr.GetSelectedObjectType3(i, -1);
                                    if (selType == (int)swSelectType_e.swSelFACES)
                                    {
                                        var selected = new JObject();
                                        selected["selectionIndex"] = i;
                                        selected["selectionType"] = selType;
                                        facts["selected_face"] = selected;
                                        AddinStatusLogger.Log("PlanFromIntent", $"Selected face detected index={i}");
                                        break;
                                    }
                                    // record first generic selection if not a face
                                    if (i == 1)
                                    {
                                        var selected = new JObject();
                                        selected["selectionIndex"] = i;
                                        selected["selectionType"] = selType;
                                        facts["selected_object"] = selected;
                                        AddinStatusLogger.Log("PlanFromIntent", $"Selected object detected index={i} type={selType}");
                                    }
                                }
                                catch { }
                            }

                            // persist the enriched facts
                            ModelContextStore.SetFacts(model.GetTitle(), facts);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddinStatusLogger.Log("PlanFromIntent", $"Failed to read selection: {ex.Message}");
                    }
                }

                // Ask LLM to plan steps based on intent + facts
                try
                {
                    if (facts != null)
                    {
                        try { AddinStatusLogger.Log("LLM-Facts", Newtonsoft.Json.JsonConvert.SerializeObject(facts, Newtonsoft.Json.Formatting.None)); } catch { }
                    }
                    else
                    {
                        try { AddinStatusLogger.Log("LLM-Facts", "(no facts supplied)"); } catch { }
                    }
                }
                catch { }

                var planResult = ClarificationService.PlanFromIntent(intent, facts);

                if (planResult == null)
                    return OperationResult.CreateFailure("LLM did not return valid steps for intent");

                // If the planner returned a clarification object, forward it to the caller
                if (planResult.Type == Newtonsoft.Json.Linq.JTokenType.Object && planResult["clarification_needed"] != null)
                {
                    AddinStatusLogger.Log("PlanFromIntent", "Clarification required for intent");
                    return OperationResult.CreateSuccess(stillInSketch: inSketch, data: new { clarification = planResult, intent });
                }

                // Expect an array of steps otherwise
                var steps = planResult as Newtonsoft.Json.Linq.JArray;
                if (steps == null || steps.Count == 0)
                    return OperationResult.CreateFailure("LLM did not return valid steps for intent");

                AddinStatusLogger.Log("PlanFromIntent", $"Generated {steps.Count} steps from intent: {intent}");
                return OperationResult.CreateSuccess(stillInSketch: inSketch, data: new { steps, intent });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"plan_from_intent failed: {ex.Message}");
            }
        }
    }
}
