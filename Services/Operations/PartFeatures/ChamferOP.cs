using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    internal static class ChamferOP
    {
        public static AICAD.Services.Operations.OperationResult ExecuteChamfer(JObject step, IModelDoc2 model, IFeatureManager featMgr)
        {
            try
            {
                if (model == null)
                    return AICAD.Services.Operations.OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return AICAD.Services.Operations.OperationResult.CreateFailure("Feature manager not available");

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
                var edgeList = new System.Collections.Generic.List<object>();
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
                                edgeList.Add(eObj);
                            }
                        }
                    }
                }

                edgeCount = edgeList.Count;
                if (edgeCount == 0)
                    return AICAD.Services.Operations.OperationResult.CreateFailure("No edges found to chamfer.");

                // Try newer FeatureChamfer API (FeatureManager)
                // Try batch FeatureChamfer3 (if supported) by selecting all edges first
                try
                {
                    // select all edges with the same selection mark
                    foreach (var eObj in edgeList)
                    {
                        try { ((dynamic)eObj).Select4(true, selData); } catch { try { ((dynamic)eObj).Select2(true, selData); } catch { } }
                    }

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

                // Fallback to legacy ModelDoc API if needed - try several common signatures to maximize compatibility
                if (chamferFeat == null)
                {
                    bool anyCreated = false;

                    // Fallback: try per-edge legacy calls. Some SolidWorks versions expect a single selected edge per call.
                    foreach (var eObj in edgeList)
                    {
                        try { model.ClearSelection2(true); } catch { }
                        var singleSel = selMgr.CreateSelectData();
                        try { singleSel.Mark = 1; } catch { }
                        try { ((dynamic)eObj).Select4(true, singleSel); } catch { try { ((dynamic)eObj).Select2(true, singleSel); } catch { } }

                        var perAttempts = new[] {
                            new { Desc = "FeatureChamfer2(distance, bool, int, int, int)", Call = (Func<dynamic,int>)(m => (int)m.FeatureChamfer2(distance, true, 0, 0, 0)) },
                            new { Desc = "FeatureChamfer2(distance, distance, int, int, int)", Call = (Func<dynamic,int>)(m => (int)m.FeatureChamfer2(distance, distance, 0, 0, 0)) },
                            new { Desc = "FeatureChamfer2(distance, bool)", Call = (Func<dynamic,int>)(m => (int)m.FeatureChamfer2(distance, true)) },
                            new { Desc = "FeatureChamfer2(distance, distance)", Call = (Func<dynamic,int>)(m => (int)m.FeatureChamfer2(distance, distance)) }
                        };

                        dynamic dynModel = model;
                        foreach (var at in perAttempts)
                        {
                            try
                            {
                                int status = at.Call(dynModel);
                                AddinStatusLogger.Log("ChamferOP", $"Per-edge attempt {at.Desc} returned status={status}");
                                if (status != 0)
                                {
                                    anyCreated = true;
                                    break;
                                }
                            }
                            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException bindEx)
                            {
                                AddinStatusLogger.Log("ChamferOP", $"Per-edge attempt {at.Desc} not available: {bindEx.Message}");
                            }
                            catch (Exception ex)
                            {
                                AddinStatusLogger.Log("ChamferOP", $"Per-edge attempt {at.Desc} threw: {ex.Message}");
                            }
                        }

                        if (anyCreated)
                        {
                            // keep going to try chamfering other edges as well
                            continue;
                        }
                    }

                    if (!anyCreated)
                        return AICAD.Services.Operations.OperationResult.CreateFailure("Chamfer API not available or failed on this SolidWorks version");
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
