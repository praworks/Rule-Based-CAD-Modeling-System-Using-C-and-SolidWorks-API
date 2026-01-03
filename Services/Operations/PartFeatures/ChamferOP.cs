using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    internal static class ChamferOP
    {
        public static AICAD.Services.Operations.OperationResult ApplyChamfer(JObject step, IModelDoc2 model, IFeatureManager featMgr)
        {
            try
            {
                if (model == null)
                    return AICAD.Services.Operations.OperationResult.CreateFailure("Model not initialized");

                double rawDistMm = step.Value<double?>("distance") ?? step.Value<double?>("d") ?? step.Value<double?>("dist") ?? 0;
                double distance = PartFeatureHelpers.ToMeters(rawDistMm);
                if (distance <= 0)
                    return AICAD.Services.Operations.OperationResult.CreateFailure("Chamfer distance must be > 0");

                AddinStatusLogger.Log("ChamferOP", $"Applying Chamfer: {rawDistMm}mm ({distance}m)");

                try { model.ForceRebuild3(false); } catch { }
                try { model.ClearSelection2(true); } catch { }

                SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;
                SelectData selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }

                int edgeCount = 0;
                IFeature chamferFeat = null;

                var part = (IPartDoc)model;
                if (part == null) return AICAD.Services.Operations.OperationResult.CreateFailure("Not a part document");

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
                    return AICAD.Services.Operations.OperationResult.CreateFailure("No edges found to chamfer.");

                // Try newer FeatureChamfer API
                try
                {
                    dynamic dynFeatMgr = model.FeatureManager;
                    try
                    {
                        var f = dynFeatMgr.FeatureChamfer3(0, distance, 0, 0, 0, 0, 0);
                        if (f != null) chamferFeat = f as IFeature;
                    }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                    {
                        AddinStatusLogger.Log("ChamferOP", $"FeatureChamfer3 not available: {bindEx.Message}");
                    }
                    catch (Exception ex)
                    {
                        AddinStatusLogger.Log("ChamferOP", $"FeatureChamfer3 error: {ex.Message}");
                    }
                }
                catch (Exception) { }

                // Fallback to legacy API
                if (chamferFeat == null)
                {
                    try
                    {
                        dynamic dynModel = model;
                        try
                        {
                            int status = dynModel.FeatureChamfer2(distance, true, 0, 0, 0);
                            AddinStatusLogger.Log("ChamferOP", $"Legacy FeatureChamfer2 returned status={status}");
                            if (status == 0)
                                return AICAD.Services.Operations.OperationResult.CreateFailure($"Chamfer feature creation failed (selected {edgeCount} edges)");
                        }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                        {
                            AddinStatusLogger.Log("ChamferOP", $"Legacy FeatureChamfer2 not available: {bindEx.Message}");
                            return AICAD.Services.Operations.OperationResult.CreateFailure("Chamfer API not available on this SolidWorks version");
                        }
                    }
                    catch (Exception ex)
                    {
                        return AICAD.Services.Operations.OperationResult.CreateFailure($"Chamfer API call failed: {ex.Message}");
                    }
                }

                try { model.ForceRebuild3(false); } catch { }
                model.ClearSelection2(true);

                return AICAD.Services.Operations.OperationResult.CreateSuccess(stillInSketch: false, data: new { edgeCount, distanceMm = rawDistMm, featureName = chamferFeat?.Name });
            }
            catch (Exception ex)
            {
                return AICAD.Services.Operations.OperationResult.CreateFailure($"chamfer failed: {ex.Message}");
            }
        }
    }
}
