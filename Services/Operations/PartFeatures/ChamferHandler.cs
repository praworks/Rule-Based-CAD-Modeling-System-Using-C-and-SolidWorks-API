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


                // 4. Apply Chamfer (The Fix)

                // We use InsertFeatureChamfer because it is stable and supports "Distance-Distance" clearly.

                // Use dynamic to avoid compile-time overload mismatches across SW versions.

                // 4 = swFeatureChamferType_DistanceDistance

                // 1 = Equal Distance (True)

                // 0 = Flip (False)

                try

                {

                    dynamic dynFeatMgr2 = featMgr;

                    // Use swFeatureChamferDistanceAngle (1) for Distance-Angle chamfer with 45 degrees

                    double angleInRadians = 45.0 * (Math.PI / 180.0); // 45 degrees = 0.7854 radians

                    var f2 = dynFeatMgr2.InsertFeatureChamfer(1, 1, distance, angleInRadians, 0, 0, 0, 0);

                    if (f2 != null) chamferFeat = f2 as IFeature;

                }

                catch

                {

                    // If dynamic call fails, leave chamferFeat null so fallback can run

                }


                if (chamferFeat == null)

                {

                    // Fallback: Sometimes selection fails silently, check if an error occurred

                    return OperationResult.CreateFailure("SolidWorks failed to create the chamfer feature (returned null).");

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