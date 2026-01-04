using System;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "extrude" operation - creates an extrusion (boss or cut)
    /// </summary>
    public class ExtrudeHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                double depth = PartFeatureHelpers.ToMeters(step.Value<double?>("depth") ?? 0);
                bool isBoss = (step.Value<string>("type") ?? "boss").ToLowerInvariant() == "boss";

                var feat = featMgr.FeatureExtrusion2(isBoss,
                    false, false,
                    (int)swEndConditions_e.swEndCondBlind,
                    (int)swEndConditions_e.swEndCondBlind,
                    depth, 0,
                    false, false, false, false, 0, 0,
                    false, false, false, false, true, false, false,
                    (int)swStartConditions_e.swStartSketchPlane, 0, false);

                if (feat == null)
                    return OperationResult.CreateFailure("Extrude operation failed");

                // Force rebuild so the feature tree and bodies are updated
                try { model.ForceRebuild3(false); AddinStatusLogger.Log("ExtrudeHandler", "Model rebuilt (ForceRebuild3 false)"); } catch { }

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"extrude failed: {ex.Message}");
            }
        }
    }
}
