using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Robust face selection helper and operation handler.
    /// Supports selecting by explicit face id, common aliases (top/front/right),
    /// numeric index into the model's face list, and picking the nearest face
    /// on a given standard plane. Logs diagnostics when selection fails.
    /// </summary>
    public class FaceHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                string faceId = step.Value<string>("id") ?? step.Value<string>("face_id") ?? step.Value<string>("face") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(faceId))
                    return OperationResult.CreateFailure("Missing face id");

                // Try direct selection by id first
                SafeClearSelection(model);
                bool sel = false;
                try { sel = model.Extension.SelectByID2(faceId, "FACE", 0, 0, 0, false, 0, null, 0); } catch { sel = false; }
                if (sel) return OperationResult.CreateSuccess();

                // Alias fallbacks: top/front/right maps to planes; if plane selected, pick nearest face on that plane
                var fid = faceId.Trim().ToLowerInvariant();
                if (fid.Contains("top"))
                {
                    if (TrySelectExtremeFaceOnPlane(model, PlaneKind.Top, true, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("bottom"))
                {
                    if (TrySelectExtremeFaceOnPlane(model, PlaneKind.Top, false, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("xy") || fid.Contains("x-y"))
                {
                    if (TrySelectNearestFaceOnPlane(model, PlaneKind.Top, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("front"))
                {
                    if (TrySelectExtremeFaceOnPlane(model, PlaneKind.Front, true, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("back"))
                {
                    if (TrySelectExtremeFaceOnPlane(model, PlaneKind.Front, false, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("xz"))
                {
                    if (TrySelectNearestFaceOnPlane(model, PlaneKind.Front, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("right"))
                {
                    if (TrySelectExtremeFaceOnPlane(model, PlaneKind.Right, true, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("left"))
                {
                    if (TrySelectExtremeFaceOnPlane(model, PlaneKind.Right, false, out string chosen)) return OperationResult.CreateSuccess();
                }
                if (fid.Contains("yz"))
                {
                    if (TrySelectNearestFaceOnPlane(model, PlaneKind.Right, out string chosen)) return OperationResult.CreateSuccess();
                }

                // Numeric index fallback: try to interpret faceId as integer index into faces array
                var digits = System.Text.RegularExpressions.Regex.Replace(faceId, "[^0-9]", "");
                if (int.TryParse(digits, out int idx))
                {
                    try
                    {
                        var part = model as IPartDoc;
                        if (part != null)
                        {
                            var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                            if (bodies != null && bodies.Length > 0)
                            {
                                var liveBody = bodies[bodies.Length - 1] as IBody2;
                                if (liveBody != null)
                                {
                                    var faces = (object[])liveBody.GetFaces();
                                    if (faces != null && faces.Length > 0)
                                    {
                                        int tryIdx = Math.Max(0, idx - 1);
                                        if (tryIdx >= 0 && tryIdx < faces.Length)
                                        {
                                            SafeClearSelection(model);
                                            var selMgr = (SelectionMgr)model.SelectionManager;
                                            var selData = selMgr.CreateSelectData();
                                            try { selData.Mark = 1; } catch { }
                                            try
                                            {
                                                var fObj = faces[tryIdx];
                                                try { ((dynamic)fObj).Select4(true, selData); } catch { try { ((dynamic)fObj).Select2(true, selData); } catch { } }
                                                var curSel = model.SelectionManager.GetSelectedObjectCount2(-1);
                                                if (curSel > 0) return OperationResult.CreateSuccess();
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // If we reach here, selection failed — log diagnostics about faces
                LogFaceDiagnostics(model, faceId);

                return OperationResult.CreateFailure($"Could not select face '{faceId}'");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"select_face failed: {ex.Message}");
            }
        }

        private enum PlaneKind { Top, Front, Right }

        private bool TrySelectNearestFaceOnPlane(IModelDoc2 model, PlaneKind plane, out string chosenFaceId)
        {
            chosenFaceId = null;
            try
            {
                // Attempt to select the plane first (best-effort)
                string planeName = plane == PlaneKind.Top ? "Top Plane" : plane == PlaneKind.Front ? "Front Plane" : "Right Plane";
                SafeClearSelection(model);
                bool planeOk = false;
                try { planeOk = model.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0); } catch { planeOk = false; }

                // Gather faces and choose nearest by projected coordinate along plane normal
                var part = model as IPartDoc;
                if (part == null) return false;
                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies == null || bodies.Length == 0) return false;
                var liveBody = bodies[bodies.Length - 1] as IBody2;
                if (liveBody == null) return false;
                var faces = (object[])liveBody.GetFaces();
                if (faces == null || faces.Length == 0) return false;

                // Standard SOLIDWORKS planes:
                // Front Plane -> XY (normal Z), Top Plane -> XZ (normal Y), Right Plane -> YZ (normal X)
                int coordIndex = plane == PlaneKind.Top ? 1 : plane == PlaneKind.Front ? 2 : 0;

                double bestDist = double.MaxValue;
                int bestIdx = -1;
                for (int i = 0; i < faces.Length; i++)
                {
                    try
                    {
                        var f = faces[i];
                        // Try to get bounding box for centroid
                        double[] box = null;
                        try { box = (double[])((dynamic)f).GetBox(); } catch { }
                        if (box != null && box.Length >= 6)
                        {
                            double cx = (box[0] + box[3]) / 2.0;
                            double cy = (box[1] + box[4]) / 2.0;
                            double cz = (box[2] + box[5]) / 2.0;
                            double coord = coordIndex == 0 ? cx : coordIndex == 1 ? cy : cz;
                            double dist = Math.Abs(coord); // prefer faces near origin plane
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestIdx = i;
                            }
                        }
                    }
                    catch { }
                }

                if (bestIdx >= 0)
                {
                    SafeClearSelection(model);
                    var selMgr = (SelectionMgr)model.SelectionManager;
                    var selData = selMgr.CreateSelectData();
                    try { selData.Mark = 1; } catch { }
                    try
                    {
                        var fObj = faces[bestIdx];
                        try { ((dynamic)fObj).Select4(true, selData); } catch { try { ((dynamic)fObj).Select2(true, selData); } catch { } }
                        var curSel = model.SelectionManager.GetSelectedObjectCount2(-1);
                        if (curSel > 0)
                        {
                            chosenFaceId = $"face_index_{bestIdx}";
                            return true;
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySelectExtremeFaceOnPlane(IModelDoc2 model, PlaneKind plane, bool preferPositive, out string chosenFaceId)
        {
            chosenFaceId = null;
            try
            {
                var part = model as IPartDoc;
                if (part == null) return false;
                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies == null || bodies.Length == 0) return false;
                var liveBody = bodies[bodies.Length - 1] as IBody2;
                if (liveBody == null) return false;
                var faces = (object[])liveBody.GetFaces();
                if (faces == null || faces.Length == 0) return false;

                int coordIndex = plane == PlaneKind.Top ? 1 : plane == PlaneKind.Front ? 2 : 0;
                double bestCoord = preferPositive ? double.MinValue : double.MaxValue;
                int bestIdx = -1;

                for (int i = 0; i < faces.Length; i++)
                {
                    try
                    {
                        var f = faces[i];
                        double[] box = null;
                        try { box = (double[])((dynamic)f).GetBox(); } catch { }
                        if (box == null || box.Length < 6) continue;

                        double cx = (box[0] + box[3]) / 2.0;
                        double cy = (box[1] + box[4]) / 2.0;
                        double cz = (box[2] + box[5]) / 2.0;
                        double coord = coordIndex == 0 ? cx : coordIndex == 1 ? cy : cz;

                        if ((preferPositive && coord > bestCoord) || (!preferPositive && coord < bestCoord))
                        {
                            bestCoord = coord;
                            bestIdx = i;
                        }
                    }
                    catch { }
                }

                if (bestIdx < 0) return false;

                SafeClearSelection(model);
                var selMgr = (SelectionMgr)model.SelectionManager;
                var selData = selMgr.CreateSelectData();
                try { selData.Mark = 1; } catch { }
                try
                {
                    var fObj = faces[bestIdx];
                    try { ((dynamic)fObj).Select4(true, selData); } catch { try { ((dynamic)fObj).Select2(true, selData); } catch { } }
                    var curSel = model.SelectionManager.GetSelectedObjectCount2(-1);
                    if (curSel > 0)
                    {
                        chosenFaceId = $"face_index_{bestIdx}";
                        return true;
                    }
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void SafeClearSelection(IModelDoc2 model)
        {
            try { model.ClearSelection2(true); } catch { }
        }

        private void LogFaceDiagnostics(IModelDoc2 model, string faceId)
        {
            try
            {
                var part = model as IPartDoc;
                if (part == null) return;
                var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                if (bodies == null || bodies.Length == 0) return;
                var liveBody = bodies[bodies.Length - 1] as IBody2;
                if (liveBody == null) return;
                var faces = (object[])liveBody.GetFaces();
                int fcount = faces?.Length ?? 0;
                AddinStatusLogger.Log("FaceHandler", $"Face selection failed for '{faceId}'. Body has {fcount} faces.");

                int limit = Math.Min(10, fcount);
                for (int i = 0; i < limit; i++)
                {
                    try
                    {
                        var f = faces[i];
                        double area = 0;
                        try { area = (double)((dynamic)f).GetArea(); } catch { }
                        string boxDesc = "";
                        try
                        {
                            var box = (double[])((dynamic)f).GetBox();
                            if (box != null && box.Length >= 6)
                            {
                                var cx = (box[0] + box[3]) / 2.0;
                                var cy = (box[1] + box[4]) / 2.0;
                                var cz = (box[2] + box[5]) / 2.0;
                                boxDesc = $" center=({cx:F1},{cy:F1},{cz:F1})";
                            }
                        }
                        catch { }
                        AddinStatusLogger.Log("FaceHandler", $" Face[{i}] area={area:F3} mm^2{boxDesc}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Log("FaceHandler", "Failed to gather face diagnostics: " + ex.Message);
            }
        }
    }
}
