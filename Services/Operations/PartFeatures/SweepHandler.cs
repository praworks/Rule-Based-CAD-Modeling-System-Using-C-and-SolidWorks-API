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
            // Note: The Path must be selected with Mark 4 for a Sweep operation.
            // If the user just finished a sketch, it remains selected, but we clear and re-select for safety.
            model.ClearSelection2(true);

            // Diagnostic: log current selection state
            try
            {
                var sel = model.SelectionManager;
                int selCount = 0;
                try { selCount = sel.GetSelectedObjectCount2(-1); } catch { selCount = 0; }
                AddinStatusLogger.Log("SweepHandler", $"Current selection count: {selCount}");
                for (int i = 1; i <= selCount; i++)
                {
                    try
                    {
                        int type = sel.GetSelectedObjectType3(i, -1);
                        AddinStatusLogger.Log("SweepHandler", $"Selected index {i}: type={type}");
                    }
                    catch { }
                }
            }
            catch { }

            // Attempt to ensure a valid Path sketch (Mark 4) is selected. Prefer a sketch named with 'path', otherwise pick the most recent sketch.
            bool pathSelected = false;
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

                    if (string.Equals(tname, "Sketch", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fname.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            candidate = feat;
                            break;
                        }
                        if (candidate == null)
                            candidate = feat; // keep first sketch as fallback
                    }
                    feat = feat.GetNextFeature();
                }

                if (candidate != null)
                {
                    try
                    {
                        AddinStatusLogger.Log("SweepHandler", $"Auto-selecting sketch '{candidate.Name}' as path.");
                        pathSelected = model.Extension.SelectByID2(candidate.Name, "SKETCH", 0, 0, 0, false, 4, null, 0);
                        AddinStatusLogger.Log("SweepHandler", $"SelectByID2 returned {pathSelected}");
                    }
                    catch (Exception ex)
                    {
                        AddinStatusLogger.Log("SweepHandler", $"Auto-select attempt failed: {ex.Message}");
                        pathSelected = false;
                    }
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Log("SweepHandler", $"Selection enumeration failed: {ex.Message}");
                pathSelected = false;
            }

            // API: InsertSweep variants differ across SW versions; use reflection to call if available.
            IFeature swFeat = null;
            try
            {
                var fmType = featMgr.GetType();
                var miSweep = fmType.GetMethod("InsertSweep", BindingFlags.Public | BindingFlags.Instance);
                if (miSweep != null)
                {
                    // many InsertSweep overloads exist; try a common signature with many parameters and ProfileProp = 1
                    try
                    {
                        swFeat = miSweep.Invoke(featMgr, new object[] { true, false, 0, 0, 0, false, false, false, 0, 0, 0, false, false, false, 1 }) as IFeature;
                    }
                    catch { }
                }
                // fallback: try older/protrusion swept API (may already be supported elsewhere)
                if (swFeat == null)
                {
                    var miAlt = fmType.GetMethod("InsertProtrusionSwept4", BindingFlags.Public | BindingFlags.Instance);
                    if (miAlt != null)
                    {
                        try
                        {
                            swFeat = miAlt.Invoke(featMgr, new object[] { true, false, false, false, 0, true, 0.0, 0.0, 0.0, true, 0, true, 0, 0, 0.0, false, 0.0, 0.0 }) as IFeature;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (swFeat == null)
            {
                if (!pathSelected)
                    return OperationResult.CreateFailure("InsertSweep failed. No valid Path sketch was selected. Select a single sketch on the Front Plane (Mark 4) and retry.");
                return OperationResult.CreateFailure("InsertSweep failed. Verify that a valid Path sketch is selected with Mark 4.");
            }

            // Get the feature definition to set the numeric diameter via reflection (avoid depending on specific COM type)
            try
            {
                var def = swFeat.GetDefinition();
                if (def == null)
                    return OperationResult.CreateFailure("Could not retrieve sweep feature definition.");

                var defType = def.GetType();
                var prop = defType.GetProperty("CircularProfileDiameter");
                if (prop != null && prop.CanWrite)
                {
                    // API expects meters
                    prop.SetValue(def, diameterMm / 1000.0);
                }
                else
                {
                    // property not found — still attempt to modify definition unchanged
                }

                bool modifyStatus = swFeat.ModifyDefinition(def, model, null);
                if (modifyStatus)
                {
                    return OperationResult.CreateSuccess(stillInSketch: false, data: new { diameter = diameterMm, method = "CircularProfile" });
                }
                return OperationResult.CreateFailure("Could not apply circular diameter to sweep definition.");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"Circular sweep setup failed: {ex.Message}");
            }
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
    }
}
