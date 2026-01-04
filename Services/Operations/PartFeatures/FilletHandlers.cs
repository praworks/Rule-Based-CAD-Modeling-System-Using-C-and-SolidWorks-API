using System;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "fillet" operation - adds fillet to edges
    /// </summary>
    public class FilletHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                double rawRadiusMm = step.Value<double?>("radius") ?? step.Value<double?>("r") ?? 0;
                double radiusMeters = PartFeatureHelpers.ToMeters(rawRadiusMm);
                if (radiusMeters <= 0)
                    return OperationResult.CreateFailure("Fillet radius must be > 0");

                AddinStatusLogger.Log("FilletHandler", $"Applying Radius: {rawRadiusMm}mm ({radiusMeters}m)");

                try { model.ForceRebuild3(false); } catch { }
                try { model.ClearSelection2(true); } catch { }

                SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;
                SelectData selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }

                int edgeCount = 0;
                IFeature filletFeat = null;

                var part = (IPartDoc)model;
                if (part == null) return OperationResult.CreateFailure("Not a part document");

                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies != null && bodies.Length > 0)
                {
                    var liveBody = bodies[bodies.Length - 1] as IBody2;
                    if (liveBody != null)
                    {
                        var edges = (object[])liveBody.GetEdges();
                        if (edges != null)
                        {
                            foreach (var eObj in edges)
                            {
                                try { ((dynamic)eObj).Select4(true, selData); } catch { try { ((dynamic)eObj).Select2(true, selData); } catch { } }
                                edgeCount++;
                            }
                        }
                    }
                }

                if (edgeCount == 0)
                    return OperationResult.CreateFailure("No edges found to fillet.");

                // Try batch FeatureFillet3
                try
                {
                    dynamic dynFeatMgr = model.FeatureManager;
                    var f = dynFeatMgr.FeatureFillet3(3, radiusMeters, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    if (f != null) filletFeat = f as IFeature;
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                {
                    AddinStatusLogger.Log("FilletHandler", $"FeatureFillet3 not available on this SolidWorks version: {bindEx.Message}");
                }
                catch (Exception ex)
                {
                    AddinStatusLogger.Log("FilletHandler", $"FeatureFillet3 error: {ex.Message}");
                }

                // Fallback to legacy FeatureFillet2 if needed
                if (filletFeat == null)
                {
                    try
                    {
                        int status = model.FeatureFillet2(radiusMeters, true, true, false, 0, 1, new double[] { radiusMeters });
                        AddinStatusLogger.Log("FilletHandler", $"Legacy FeatureFillet2 returned status={status}");
                        if (status == 0)
                            return OperationResult.CreateFailure($"Fillet feature creation failed (selected {edgeCount} edges)");
                    }
                    catch (Exception ex)
                    {
                        return OperationResult.CreateFailure($"Fillet API call failed: {ex.Message}");
                    }
                }

                try { model.ForceRebuild3(false); } catch { }
                model.ClearSelection2(true);

                return OperationResult.CreateSuccess(stillInSketch: false, data: new { edgeCount, radiusMm = rawRadiusMm, featureName = filletFeat?.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"fillet failed: {ex.Message}");
            }
        }

    }
}
