using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

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
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                var facts = ModelInspector.InspectModel(model, emitLogs: true);
                
                // Log a summary for user visibility
                try
                {
                    var summary = ModelInspector.GetGeometrySummary(model);
                    AddinStatusLogger.Log("ModelInspect", summary);
                }
                catch { }

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
