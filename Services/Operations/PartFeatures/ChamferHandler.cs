using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "chamfer" operation - adds chamfer to edges
    /// </summary>
    public class ChamferHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                double rawDistMm = step.Value<double?>("distance") ?? step.Value<double?>("d") ?? step.Value<double?>("dist") ?? 0;
                double distance = PartFeatureHelpers.ToMeters(rawDistMm);
                if (distance <= 0)
                    return OperationResult.CreateFailure("Chamfer distance must be > 0");

                AddinStatusLogger.Log("ChamferHandler", $"Applying Chamfer: {rawDistMm}mm ({distance}m)");

                try { model.ForceRebuild3(false); } catch { }
                try { model.ClearSelection2(true); } catch { }

                SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;
                SelectData selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }

                int edgeCount = 0;
                IFeature chamferFeat = null;

                var part = (IPartDoc)model;
                if (part == null) return OperationResult.CreateFailure("Not a part document");

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
                                Entity ent = (Entity)eObj;
                                ent.Select4(true, selData);
                                edgeCount++;
                            }
                        }
                    }

                }

                if (edgeCount == 0)
                    return OperationResult.CreateFailure("No edges found to chamfer.");

                // 4. Apply Chamfer (Symmetric / Equal Distance)
                // We use the simpler "InsertChamfer" method.
                // It automatically creates a symmetric chamfer when only one distance is provided.
                try
                {
                    // Use dynamic invocation to support different SW interop versions
                    dynamic dynFeatMgr = featMgr;
                    // InsertFeatureChamfer(ChamferType, Distance1, Distance2, Angle, Flip, ...)
                    // 0 = swFeatureChamferUniformDistance (equal distance chamfer)
                    // distance = width (same for both distances for symmetric)
                    var f2 = dynFeatMgr.InsertFeatureChamfer(0, distance, distance, 0, 0, null, null);

                    if (f2 != null)
                    {
                        chamferFeat = f2 as IFeature;
                    }
                    else
                    {
                        return OperationResult.CreateFailure($"SolidWorks failed to create the chamfer. Check if {rawDistMm}mm is too large for these edges.");
                    }
                }
                catch (Exception ex)
                {
                    return OperationResult.CreateFailure($"API Error: {ex.Message}");
                }

                model.ClearSelection2(true);
                return OperationResult.CreateSuccess(stillInSketch: false, data: new { edgeCount, featureName = chamferFeat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"Chamfer handler exception: {ex.Message}");
            }
        }

    }
}
