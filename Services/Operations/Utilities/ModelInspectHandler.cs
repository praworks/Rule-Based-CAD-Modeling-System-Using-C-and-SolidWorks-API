using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using System.Windows.Forms;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.Utilities
{
    /// <summary>
    /// Handler for "model_inspect" operation - captures current model state for LLM context.
    /// Stores the facts in the result data so subsequent operations can use them.
    /// </summary>
    public class ModelInspectHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                // Log whether there's an active document and identify it for debugging
                try
                {
                    if (model == null)
                    {
                        AddinStatusLogger.Log("ModelInspect", "No active SolidWorks document (model == null)");
                        return OperationResult.CreateFailure("Model not initialized");
                    }
                    else
                    {
                        string path = "(unsaved)";
                        try { path = model.GetPathName(); } catch { }
                        string title = "(unknown)";
                        try { title = model.GetTitle(); } catch { }
                        AddinStatusLogger.Log("ModelInspect", $"Active document present: title={title} path={path}");
                    }
                }
                catch { }

                // Quick unconditional selection logging to aid debugging: log selection
                // count and the first selected object type so users can verify the add-in
                // sees the SolidWorks selection at handler invocation.
                try
                {
                    var selMgr = model.SelectionManager as ISelectionMgr;
                    int selCount = selMgr?.GetSelectedObjectCount2(-1) ?? 0;
                    if (selCount > 0)
                    {
                        int firstType = 0;
                        try { firstType = selMgr.GetSelectedObjectType3(1, -1); } catch { }
                        var msg = $"Selection present: count={selCount} firstType={firstType}";
                        AddinStatusLogger.Log("ModelInspect", msg);
                        try { MessageBox.Show(msg, "AICAD Selection", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
                    }
                    else
                    {
                        AddinStatusLogger.Log("ModelInspect", "No selection present at inspection time");
                    }
                }
                catch (Exception ex)
                {
                    try { AddinStatusLogger.Log("ModelInspect", $"Selection check failed: {ex.Message}"); } catch { }
                }

                var facts = ModelInspector.InspectModel(model, emitLogs: true);
                
                // Log a summary for user visibility
                try
                {
                    var summary = ModelInspector.GetGeometrySummary(model);
                    AddinStatusLogger.Log("ModelInspect", summary);
                }
                catch { }

                // Capture current selection (if any) and include in facts so LLMs
                // can reason about a user-selected face/feature.
                try
                {
                    var selMgr = model.SelectionManager as ISelectionMgr;
                    if (selMgr != null)
                    {
                        int selCount = selMgr.GetSelectedObjectCount2(-1);
                        if (selCount > 0)
                        {
                            for (int i = 1; i <= selCount; i++)
                            {
                                try
                                {
                                    int selType = selMgr.GetSelectedObjectType3(i, -1);
                                    var obj = selMgr.GetSelectedObject6(i, -1);

                                    // If the selection is a face, record a simple descriptor.
                                    if (selType == (int)swSelectType_e.swSelFACES)
                                    {
                                        var selected = new JObject();
                                        selected["selectionIndex"] = i;
                                        selected["selectionType"] = selType;
                                        // store minimal selection metadata for planner use
                                        facts["selected_face"] = selected;
                                        try { AddinStatusLogger.Log("ModelInspect", $"Selected face index={i} type={selType}"); } catch { }
                                        break;
                                    }

                                    // If no face found yet, store the first selected object generically
                                    if (i == 1)
                                    {
                                        var selected = new JObject();
                                        selected["selectionIndex"] = i;
                                        selected["selectionType"] = selType;
                                        facts["selected_object"] = selected;
                                        try { AddinStatusLogger.Log("ModelInspect", $"Selected object index={i} type={selType}"); } catch { }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { AddinStatusLogger.Log("ModelInspect", $"Failed to capture selection: {ex.Message}"); } catch { }
                }

                // Store facts in a shared context accessible by subsequent handlers
                // Use a static dictionary keyed by model title for now
                ModelContextStore.SetFacts(model.GetTitle(), facts);

                return OperationResult.CreateSuccess(stillInSketch: inSketch, data: facts);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"model_inspect failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Simple in-memory store for model facts, keyed by model title.
    /// Allows handlers to share context across operations.
    /// </summary>
    public static class ModelContextStore
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JObject> _store 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        public static void SetFacts(string modelTitle, JObject facts)
        {
            if (string.IsNullOrWhiteSpace(modelTitle)) return;
            _store[modelTitle] = facts;
        }

        public static JObject GetFacts(string modelTitle)
        {
            if (string.IsNullOrWhiteSpace(modelTitle)) return null;
            _store.TryGetValue(modelTitle, out var facts);
            return facts;
        }

        public static void Clear(string modelTitle)
        {
            if (string.IsNullOrWhiteSpace(modelTitle)) return;
            _store.TryRemove(modelTitle, out _);
        }
    }
}
