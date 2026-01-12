using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "thread" operation - creates a modeled thread using SolidWorks Thread feature.
    /// Requires a cylindrical face to be selected; if none selected, selects the first cylindrical face found.
    /// </summary>
    public class ThreadHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");

                var diameterMm = step.Value<double?>("diameter") ?? step.Value<double?>("major_diameter") ?? 10.0;
                var pitchMm = step.Value<double?>("pitch") ?? 1.5;
                var lengthMm = step.Value<double?>("length") ?? step.Value<double?>("thread_length") ?? 100.0;
                var handedness = (step.Value<string>("handedness") ?? "right").ToLowerInvariant();
                var isRightHand = !handedness.StartsWith("left", StringComparison.OrdinalIgnoreCase);
                var isInternal = (step.Value<string>("type") ?? "external").StartsWith("internal", StringComparison.OrdinalIgnoreCase);
                var size = step.Value<string>("size");
                if (string.IsNullOrWhiteSpace(size))
                {
                    size = $"M{diameterMm:0.###}x{pitchMm:0.###}";
                }

                if (!EnsureCylindricalFaceSelected(model))
                {
                    return OperationResult.CreateFailure("Thread failed: no cylindrical face selected");
                }

                // Create sweep-thread feature definition via FeatureManager (SW 2024).
                object def = null;
                try
                {
                    int threadFeatureId = -1;
                    try { threadFeatureId = (int)(swFeatureNameID_e)Enum.Parse(typeof(swFeatureNameID_e), "swFmSweepThread"); } catch { }
                    if (threadFeatureId >= 0) { try { def = featMgr.CreateDefinition(threadFeatureId); } catch { } }
                }
                catch { }
                if (def == null)
                {
                    return OperationResult.CreateFailure("Thread feature definition unavailable (check SW version/API)");
                }

                // Attempt to set common properties using late binding to avoid hard dependency on specific API versions.
                TryInvoke(def, "InitializeThreadData");
                TrySet(def, "ThreadMethod", (int)swThreadMethod_e.swThreadMethod_Cut);
                TrySet(def, "EndCondition", (int)swThreadEndCondition_e.swThreadEndCondition_Blind);
                TrySet(def, "BlindDepth", PartFeatureHelpers.ToMeters(lengthMm));
                TrySet(def, "Diameter", PartFeatureHelpers.ToMeters(diameterMm));
                TrySet(def, "Pitch", PartFeatureHelpers.ToMeters(pitchMm));
                TrySet(def, "RightHanded", isRightHand);
                TrySet(def, "Type", ResolveThreadProfilePath());
                TrySet(def, "Size", size);
                TrySet(def, "NumberOfStarts", 1);
                TrySet(def, "MultipleStart", false);
                TrySet(def, "ReverseDirection", false);
                TrySet(def, "Offset", true);
                TrySet(def, "OffsetDistance", PartFeatureHelpers.ToMeters(0.0));
                TrySet(def, "TrimStartFace", true);
                TrySet(def, "TrimEndFace", true);
                TrySet(def, "MirrorProfile", false);
                TrySet(def, "MirrorType", (int)swThreadMirrorType_e.swThreadMirrorType_Horizontally);
                TrySet(def, "Revolutions", pitchMm > 0 ? (lengthMm / pitchMm) : 0.0);
                TrySet(def, "IsInternal", isInternal);
                TrySet(def, "Internal", isInternal);

                IFeature feat = null;
                try { feat = featMgr.CreateFeature(def); } catch { }
                if (feat == null)
                {
                    return OperationResult.CreateFailure("Thread feature creation failed");
                }

                try { model.ForceRebuild3(false); } catch { }
                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"thread failed: {ex.Message}");
            }
        }

        private static void TrySet(object target, string prop, object value)
        {
            try
            {
                var t = target.GetType();
                var p = t.GetProperty(prop);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(target, value);
                    return;
                }
                var f = t.GetField(prop);
                if (f != null) f.SetValue(target, value);
            }
            catch { }
        }

        private static void TryInvoke(object target, string method)
        {
            try
            {
                var t = target.GetType();
                var m = t.GetMethod(method);
                if (m != null) m.Invoke(target, null);
            }
            catch { }
        }

        private static string ResolveThreadProfilePath()
        {
            try
            {
                var env = System.Environment.GetEnvironmentVariable("AICAD_THREAD_PROFILE_PATH", System.EnvironmentVariableTarget.Process)
                          ?? System.Environment.GetEnvironmentVariable("AICAD_THREAD_PROFILE_PATH", System.EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(env)) return env;
            }
            catch { }
            return @"C:\ProgramData\SolidWorks\SOLIDWORKS 2024\thread profiles\Metric Die.SLDLFP";
        }

        private static bool EnsureCylindricalFaceSelected(IModelDoc2 model)
        {
            try
            {
                var selMgr = (SelectionMgr)model.SelectionManager;
                if (selMgr != null && selMgr.GetSelectedObjectCount2(-1) > 0)
                    return true;
            }
            catch { }

            try { model.ClearSelection2(true); } catch { }

                try
                {
                    object[] bodies = null;
                    // Prefer PartDoc.GetBodies2 if available on this interop version.
                    try
                    {
                        var part = model as PartDoc;
                        if (part != null)
                        {
                            bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                        }
                        else
                        {
                            // If the model is not a PartDoc, we cannot enumerate bodies reliably on this interop version.
                            return false;
                        }
                    }
                    catch { }

                    if (bodies == null) return false;
                    foreach (var b in bodies)
                    {
                        var body = b as Body2;
                        if (body == null) continue;
                        var face = body.GetFirstFace();
                        while (face != null)
                        {
                            try
                            {
                                var surf = face.GetSurface() as Surface;
                                if (surf != null && surf.IsCylinder())
                                {
                                    try { ((dynamic)face).Select4(false, null); } catch { try { ((dynamic)face).Select2(false, 0); } catch { } }
                                    AddinStatusLogger.Log("ThreadHandler", "Auto-selected cylindrical face for thread feature.");
                                    return true;
                                }
                            }
                            catch { }
                            try { face = face.GetNextFace(); } catch { break; }
                        }
                    }
                }
                catch { }

            return false;
        }
    }
}
