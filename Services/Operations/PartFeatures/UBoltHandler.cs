using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    /// <summary>
    /// Handler for "u_bolt" operation - creates a U-bolt body via a swept circular profile.
    /// </summary>
    public class UBoltHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");
                if (featMgr == null)
                    return OperationResult.CreateFailure("Feature manager not available");
                if (sketchMgr == null)
                    sketchMgr = model.SketchManager;

                if (inSketch)
                {
                    try { sketchMgr.InsertSketch(true); } catch { }
                }

                var dims = ReadDimensions(step);
                if (dims.RodDiameterMm <= 0 || dims.LegLengthMm <= 0 || dims.SpacingMm <= 0)
                    return OperationResult.CreateFailure("U-bolt requires rod_diameter, leg_length, and spacing > 0");

                if (dims.AdjustedInsideRadius)
                {
                    AddinStatusLogger.Log("UBoltHandler",
                        $"Inside radius adjusted to {dims.InsideRadiusMm:F3}mm to match spacing={dims.SpacingMm:F3}mm and diameter={dims.RodDiameterMm:F3}mm.");
                }

                var planeName = NormalizePlaneName(step.Value<string>("plane") ?? step.Value<string>("plane_name"));
                if (!SelectPlane(model, planeName))
                    return OperationResult.CreateFailure($"Could not select plane '{planeName}' for U-bolt sketch");

                sketchMgr.InsertSketch(true);

                var centerlineRadiusM = PartFeatureHelpers.ToMeters(dims.CenterlineRadiusMm);
                var legLengthM = PartFeatureHelpers.ToMeters(dims.LegLengthMm);

                var left = sketchMgr.CreateLine(-centerlineRadiusM, 0, 0, -centerlineRadiusM, legLengthM, 0);
                var right = sketchMgr.CreateLine(centerlineRadiusM, 0, 0, centerlineRadiusM, legLengthM, 0);

                var arc = CreateBottomArc(sketchMgr, centerlineRadiusM);

                if (left == null || right == null || arc == null)
                    return OperationResult.CreateFailure("Failed to create U-bolt sketch geometry");

                sketchMgr.InsertSketch(true);

                var pathSketch = GetLastSketchFeature(model);
                if (pathSketch == null)
                    return OperationResult.CreateFailure("No U-bolt path sketch feature found");

                if (!SelectPathSketchMark4(model, pathSketch))
                    return OperationResult.CreateFailure("Could not select U-bolt path sketch for sweep");

                var defObj = featMgr.CreateDefinition((int)swFeatureNameID_e.swFmSweep);
                var sweepData = defObj as ISweepFeatureData;
                if (sweepData == null)
                    return OperationResult.CreateFailure("Sweep definition not available");

                try
                {
                    sweepData.CircularProfile = true;
                    sweepData.CircularProfileDiameter = PartFeatureHelpers.ToMeters(dims.RodDiameterMm);
                }
                catch (Exception exSet)
                {
                    return OperationResult.CreateFailure("Failed to set sweep profile: " + exSet.Message);
                }

                var feat = featMgr.CreateFeature(sweepData);
                if (feat == null)
                    return OperationResult.CreateFailure("U-bolt sweep failed");

                try { model.ForceRebuild3(false); } catch { }

                var data = new
                {
                    featureName = feat.Name,
                    rodDiameterMm = dims.RodDiameterMm,
                    legLengthMm = dims.LegLengthMm,
                    spacingMm = dims.SpacingMm,
                    insideRadiusMm = dims.InsideRadiusMm,
                    centerlineRadiusMm = dims.CenterlineRadiusMm,
                    plane = planeName
                };
                return OperationResult.CreateSuccess(stillInSketch: false, data: data);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"u_bolt failed: {ex.Message}");
            }
        }

        private static (double RodDiameterMm, double LegLengthMm, double SpacingMm, double InsideRadiusMm, double CenterlineRadiusMm, bool AdjustedInsideRadius) ReadDimensions(JObject step)
        {
            double rodDiameterMm = ReadDouble(step, "rod_diameter", "diameter", "d") ?? 10.0;
            double legLengthMm = ReadDouble(step, "leg_length", "leg_len", "length") ?? 50.0;
            double spacingMm = ReadDouble(step, "spacing", "leg_spacing", "inside_spacing") ?? double.NaN;
            double insideRadiusMm = ReadDouble(step, "inside_bend_radius", "bend_radius", "inside_radius") ?? double.NaN;

            bool spacingProvided = !double.IsNaN(spacingMm) && spacingMm > 0;
            bool insideProvided = !double.IsNaN(insideRadiusMm) && insideRadiusMm > 0;
            bool adjusted = false;

            if (!spacingProvided && insideProvided)
                spacingMm = insideRadiusMm * 2.0;
            if (!insideProvided && spacingProvided)
                insideRadiusMm = spacingMm / 2.0;
            if (!spacingProvided && !insideProvided)
            {
                spacingMm = 40.0;
                insideRadiusMm = spacingMm / 2.0;
            }

            var centerlineRadiusMm = (spacingMm + rodDiameterMm) / 2.0;
            var impliedInside = centerlineRadiusMm - (rodDiameterMm / 2.0);
            if (insideProvided && Math.Abs(impliedInside - insideRadiusMm) > 0.01)
            {
                insideRadiusMm = impliedInside;
                adjusted = true;
            }

            return (rodDiameterMm, legLengthMm, spacingMm, insideRadiusMm, centerlineRadiusMm, adjusted);
        }

        private static double? ReadDouble(JObject step, params string[] keys)
        {
            if (step == null || keys == null) return null;
            foreach (var key in keys)
            {
                var val = step.Value<double?>(key);
                if (val.HasValue) return val.Value;
            }
            return null;
        }

        private static object CreateBottomArc(ISketchManager sketchMgr, double radiusM)
        {
            if (sketchMgr == null || radiusM <= 0) return null;
            double cx = 0.0;
            double cy = 0.0;
            double startRad = Math.PI;
            double endRad = 2.0 * Math.PI;
            double sx = cx + radiusM * Math.Cos(startRad);
            double sy = cy + radiusM * Math.Sin(startRad);
            double ex = cx + radiusM * Math.Cos(endRad);
            double ey = cy + radiusM * Math.Sin(endRad);
            return sketchMgr.CreateArc(cx, cy, 0, sx, sy, 0, ex, ey, 0, 0);
        }

        private static string NormalizePlaneName(string planeName)
        {
            if (string.IsNullOrWhiteSpace(planeName)) return "Front Plane";
            var trimmed = planeName.Trim();
            if (trimmed.Equals("Top", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Top Plane", StringComparison.OrdinalIgnoreCase))
                return "Top Plane";
            if (trimmed.Equals("Right", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Right Plane", StringComparison.OrdinalIgnoreCase))
                return "Right Plane";
            if (trimmed.Equals("Front", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Front Plane", StringComparison.OrdinalIgnoreCase))
                return "Front Plane";
            return "Front Plane";
        }

        private static bool SelectPlane(IModelDoc2 model, string planeName)
        {
            if (model == null || string.IsNullOrWhiteSpace(planeName)) return false;
            try { model.ClearSelection2(true); } catch { }
            try
            {
                return model.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0);
            }
            catch { return false; }
        }

        private static IFeature GetLastSketchFeature(IModelDoc2 model)
        {
            if (model == null) return null;
            try
            {
                var last = model.Extension.GetLastFeatureAdded() as IFeature;
                if (IsSketchFeature(last)) return last;
            }
            catch { }

            IFeature candidate = null;
            try
            {
                var feat = model.FirstFeature();
                while (feat != null)
                {
                    if (IsSketchFeature(feat))
                        candidate = feat;
                    feat = feat.GetNextFeature();
                }
            }
            catch { }
            return candidate;
        }

        private static bool IsSketchFeature(IFeature feat)
        {
            if (feat == null) return false;
            try
            {
                var tname = feat.GetTypeName2() ?? string.Empty;
                return tname.Equals("ProfileFeature", StringComparison.OrdinalIgnoreCase)
                    || tname.Equals("3DProfileFeature", StringComparison.OrdinalIgnoreCase)
                    || tname.Equals("Sketch", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
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
            }
            catch { }

            try
            {
                return model.Extension.SelectByID2(pathFeat.Name, "SKETCH", 0, 0, 0, false, 4, null, 0);
            }
            catch { return false; }
        }
    }
}
