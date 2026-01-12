using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Professional Handler for "sweep" operation. 
    /// Supports both standard Sketch Profiles and modern Circular Profiles.
    /// </summary>
    public class SweepHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null || featMgr == null)
                    return OperationResult.CreateFailure("SolidWorks Model or Feature Manager is not initialized.");

                // 1. Close active sketch to ensure the Path is available for feature selection
                if (inSketch && sketchMgr != null)
                {
                    sketchMgr.InsertSketch(true);
                }

                // 2. Identify Sweep Type (Circular vs. Sketch)
                // Logic: If 'type' is 'circular' or a 'diameter' is provided, use Circular Profile mode.
                string sweepType = step.Value<string>("type") ?? string.Empty;
                double? diameter = step.Value<double?>("diameter") ?? step.Value<double?>("circular_diameter");
                bool isCircular = sweepType.Equals("circular", StringComparison.OrdinalIgnoreCase) || diameter.HasValue;

                if (isCircular)
                {
                    return CreateCircularSweep(model, featMgr, diameter ?? 10.0);
                }

                // 3. Standard Sketch Profile Logic (Legacy Fallback)
                bool merge = step.Value<bool?>("merge") ?? true;
                double twistDeg = step.Value<double?>("twist_deg") ?? 0.0;
                double twistRad = twistDeg * Math.PI / 180.0;

                var feat = TryInvokeStandardSweep(featMgr, merge, twistRad);
                
                if (feat == null)
                {
                    return OperationResult.CreateFailure("Sweep failed: Ensure Profile (Mark 1) and Path (Mark 4) are selected.");
                }

                model.ForceRebuild3(false);
                return OperationResult.CreateSuccess(stillInSketch: false, data: new { featureName = feat.Name, method = "SketchProfile" });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"Sweep Operation Exception: {ex.Message}");
            }
        }

        private OperationResult CreateCircularSweep(IModelDoc2 model, IFeatureManager featMgr, double diameterMm)
        {
            // Ensure we are not inside a sketch edit before selecting path entities.
            try
            {
                var sm = model?.SketchManager;
                if (sm?.ActiveSketch != null)
                {
                    sm.InsertSketch(true);
                }
            }
            catch { }

            // Note: The Path must be selected with Mark 4 for a Sweep operation.
            // If the user just finished a sketch, it remains selected, but we clear and re-select for safety.
            model.ClearSelection2(true);

            // Enumerate features to find the most recent sketch-like feature (ProfileFeature / 3DProfileFeature / Sketch).
            IFeature pathSketch = null;
            try
            {
                var feat = model.FirstFeature();
                IFeature candidate = null;
                while (feat != null)
                {
                    string tname = string.Empty;
                    string fname = string.Empty;
                    try { tname = feat.GetTypeName2() ?? string.Empty; } catch { tname = string.Empty; }
                    try { fname = feat.Name ?? string.Empty; } catch { fname = string.Empty; }

                    bool isSketchFeat =
                        tname.Equals("ProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                        tname.Equals("3DProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                        tname.Equals("Sketch", StringComparison.OrdinalIgnoreCase);

                    if (isSketchFeat)
                    {
                        // Prefer the most recently encountered sketch; break early if name contains "path".
                        candidate = feat;
                        if (fname.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            candidate = feat;
                            break;
                        }
                    }
                    feat = feat.GetNextFeature();
                }

                // If none found by name, attempt explicit common-name lookup (Sketch1)
                if (candidate == null)
                {
                    try
                    {
                        var f = model.FirstFeature();
                        while (f != null)
                        {
                            string n = string.Empty;
                            try { n = f.Name ?? string.Empty; } catch { n = string.Empty; }
                            if (string.Equals(n, "Sketch1", StringComparison.OrdinalIgnoreCase))
                            {
                                candidate = f; break;
                            }
                            f = f.GetNextFeature();
                        }
                    }
                    catch { }
                }

                pathSketch = candidate;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Log("SweepHandler", $"Selection enumeration failed: {ex.Message}");
            }

            if (pathSketch == null)
            {
                return OperationResult.CreateFailure("No path sketch feature found. Create/select a path sketch and retry.");
            }

            bool pathSelected = SelectPathSketchMark4(model, pathSketch);
            LogSelectionTypes(model, "After path selection");

            if (!pathSelected)
            {
                return OperationResult.CreateFailure("Could not select path sketch entities with Mark 4.");
            }

            // Create circular profile sweep. Prefer ISweepFeatureData2 properties when present.
            try
            {
                // Create simple circular-profile sweep as in Prototype Testing/Program.cs
                var defObj = featMgr.CreateDefinition((int)swFeatureNameID_e.swFmSweep);
                var sweepData = defObj as ISweepFeatureData;
                if (sweepData == null)
                {
                    AddinStatusLogger.Log("SweepHandler", "CreateDefinition did not return ISweepFeatureData.");
                }
                else
                {
                    // Ensure we exited any sketch edit
                    try { model.SketchManager?.InsertSketch(true); } catch { }

                    // Set circular-profile mode and diameter (meters)
                    try
                    {
                        sweepData.CircularProfile = true;
                        sweepData.CircularProfileDiameter = diameterMm / 1000.0;
                    }
                    catch (Exception exSet)
                    {
                        AddinStatusLogger.Log("SweepHandler", "Failed to set circular profile properties: " + exSet.Message);
                    }

                    // Create feature
                    try
                    {
                        var feat = featMgr.CreateFeature(sweepData);
                        if (feat != null)
                        {
                            model.ForceRebuild3(false);
                            return OperationResult.CreateSuccess(stillInSketch: false, data: new { diameter = diameterMm, method = "CircularProfileCreateFeature" });
                        }
                        AddinStatusLogger.Log("SweepHandler", "CreateFeature returned null for circular-profile sweep.");
                    }
                    catch (Exception exCreate)
                    {
                        AddinStatusLogger.Log("SweepHandler", "CreateFeature threw: " + exCreate.Message);
                    }
                    finally
                    {
                        try { /* if interface has ReleaseSelectionAccess */ sweepData.ReleaseSelectionAccess(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Log("SweepHandler", $"Circular-profile CreateDefinition/CreateFeature attempt failed: {ex.Message}");
            }

            // Legacy fallback: create a circular profile sketch and use standard sweep
            try
            {
                AddinStatusLogger.Log("SweepHandler", "Attempting to create temporary circular profile sketch for legacy sweep");
                bool profOk = TryCreateAndSelectCircularProfile(model, diameterMm);
                if (profOk)
                {
                    AddinStatusLogger.Log("SweepHandler", "Temporary circular profile created; attempting legacy swept insert");
                    var legacyFeat = TryInvokeStandardSweep(featMgr, true, 0.0);
                    if (legacyFeat != null)
                    {
                        model.ForceRebuild3(false);
                        return OperationResult.CreateSuccess(stillInSketch: false, data: new { diameter = diameterMm, method = "LegacyProfile+Sweep" });
                    }
                }
            }
            catch (Exception exLast)
            {
                AddinStatusLogger.Log("SweepHandler", "Legacy profile fallback failed: " + exLast.Message);
            }

            return OperationResult.CreateFailure("Circular sweep not supported in this SolidWorks build. Try creating profile and path sketches manually.");
        }

        private IFeature TryInvokeStandardSweep(IFeatureManager featMgr, bool merge, double twistRad)
        {
            // Uses your existing reflection logic to handle different SolidWorks API versions
            var t = featMgr.GetType();
            var mi = t.GetMethod("InsertProtrusionSwept4", BindingFlags.Public | BindingFlags.Instance);
            
            if (mi != null)
            {
                return mi.Invoke(featMgr, new object[] { true, false, false, false, 0, true, 0.0, 0.0, 0.0, true, 0, merge, 0, 0, twistRad, false, 0.0, 0.0 }) as IFeature;
            }
            return null;
        }

        private static bool SelectPathSketchMark4(IModelDoc2 model, IFeature pathFeat)
        {
            if (model == null || pathFeat == null) return false;

            try { model.ClearSelection2(true); } catch { }

            try
            {
                var sketchObj = pathFeat.GetSpecificFeature2();
                if (sketchObj is ISketch sk)
                {
                    // Select all segments with Mark 4
                    try
                    {
                        var segs = sk.GetSketchSegments() as object[];
                        if (segs != null && segs.Length > 0)
                        {
                            bool any = false;
                            foreach (var s in segs)
                            {
                                if (s is ISketchSegment seg)
                                {
                                    try { any |= ((IEntity)seg).Select2(true, 4); } catch { }
                                }
                            }
                            if (any) return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Fallback to name-based selection
            try
            {
                return model.Extension.SelectByID2(pathFeat.Name, "SKETCH", 0, 0, 0, false, 4, null, 0);
            }
            catch { return false; }
        }

        private static void LogSelectionTypes(IModelDoc2 model, string label)
        {
            try
            {
                var sel = model.SelectionManager;
                int selCount = sel?.GetSelectedObjectCount2(-1) ?? 0;
                AddinStatusLogger.Log("SweepHandler", $"Before AccessSelections: selCount={selCount}");
                if (selCount == 0)
                    return;
                try { selCount = sel.GetSelectedObjectCount2(-1); } catch { selCount = 0; }
                AddinStatusLogger.Log("SweepHandler", $"{label}: count={selCount}");
                for (int i = 1; i <= selCount; i++)
                {
                    try
                    {
                        int type = sel.GetSelectedObjectType3(i, -1);
                        AddinStatusLogger.Log("SweepHandler", $"{label}: index={i} type={type}");
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void TryInvokeSetSelections(object def, IModelDoc2 model)
        {
            try
            {
                var selMgr = model.SelectionManager;
                var m = def.GetType().GetMethod("SetSelections", BindingFlags.Public | BindingFlags.Instance);
                if (m != null)
                {
                    var parms = m.GetParameters();
                    if (parms.Length == 2)
                        m.Invoke(def, new object[] { selMgr, model });
                    else if (parms.Length == 3)
                        m.Invoke(def, new object[] { selMgr, model, null });
                }
            }
            catch { }
        }

        private static void TryInvokeAccessSelections(object def, IModelDoc2 model)
        {
            try
            {
                var m = def.GetType().GetMethod("AccessSelections", BindingFlags.Public | BindingFlags.Instance);
                if (m != null)
                {
                    var parms = m.GetParameters();
                    if (parms.Length == 2)
                        m.Invoke(def, new object[] { model, null });
                    else if (parms.Length == 1)
                        m.Invoke(def, new object[] { model });
                }
            }
            catch { }
        }

        private static void TryAssignPath(object def, IFeature pathSketch)
        {
            if (def == null || pathSketch == null) return;
            try
            {
                var t = def.GetType();
                var prop = t.GetProperty("Path") ?? t.GetProperty("PathFeature") ?? t.GetProperty("PathSketch");
                if (prop != null && prop.CanWrite)
                {
                    var pType = prop.PropertyType;
                    if (pType.IsArray)
                    {
                        var arr = Array.CreateInstance(pType.GetElementType(), 1);
                        arr.SetValue(pathSketch, 0);
                        prop.SetValue(def, arr);
                    }
                    else
                    {
                        prop.SetValue(def, pathSketch);
                    }
                }
            }
            catch { }
        }

        private static bool TryCreateAndSelectCircularProfile(IModelDoc2 model, double diameterMm)
        {
            if (model == null) return false;
            try
            {
                // Select Front Plane and create sketch
                try { model.ClearSelection2(true); } catch { }
                if (!model.Extension.SelectByID2("Front Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
                    return false;

                var skMgr = model.SketchManager;
                if (skMgr == null) return false;

                skMgr.InsertSketch(true);
                double r = (diameterMm / 2.0) / 1000.0; // convert mm to meters
                skMgr.CreateCircleByRadius(0.0, 0.0, 0.0, r);
                skMgr.InsertSketch(true);

                // Find and select the newly created sketch as Mark1 (profile)
                try { model.ClearSelection2(true); } catch { }
                var f = model.FirstFeature();
                IFeature lastSketch = null;
                while (f != null)
                {
                    string tn = string.Empty;
                    try { tn = f.GetTypeName2() ?? string.Empty; } catch { }
                    if (tn.Equals("Sketch", StringComparison.OrdinalIgnoreCase) ||
                        tn.Equals("ProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                        tn.Equals("3DProfileFeature", StringComparison.OrdinalIgnoreCase))
                    {
                        lastSketch = f;
                    }
                    f = f.GetNextFeature();
                }

                if (lastSketch != null)
                {
                    return model.Extension.SelectByID2(lastSketch.Name, "SKETCH", 0, 0, 0, false, 1, null, 0);
                }
            }
            catch { }
            return false;
        }
    }
}
