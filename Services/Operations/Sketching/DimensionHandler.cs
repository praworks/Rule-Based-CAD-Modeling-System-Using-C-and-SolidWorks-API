using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations.Sketching
{
    /// <summary>
    /// Handler for "dimension" operation - adds dimension to sketch geometry
    /// </summary>
    public class DimensionHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                // Support an "auto_dimension" variant which invokes SolidWorks' FullyDefineSketch
                var opName = (step.Value<string>("op") ?? "dimension").ToLowerInvariant();
                if (opName == "auto_dimension" || opName == "auto-dimension" || opName == "autodimension")
                {
                    if (!inSketch)
                        return OperationResult.CreateFailure("Cannot auto-dimension: Not currently in sketch mode");
                    if (sketchMgr == null)
                        return OperationResult.CreateFailure("Cannot auto-dimension: Sketch manager not available");

                    try
                    {
                        // Call FullyDefineSketch with all required parameters (interop signature differs by SW version)
                        // Signature in referenced interop: FullyDefineSketch(bool, bool, int, bool, int, object, int, object, int, int)
                        // Use sensible defaults for integer flags where the precise meaning varies across versions.
                        int dimStatus = sketchMgr.FullyDefineSketch(true, true, 1, true, 1, null, 1, null, 0, 0);
                        // We don't rely on the returned integer value for success/failure here; treat as success if no exception.
                        return OperationResult.CreateSuccess(stillInSketch: true);
                    }
                    catch (Exception ex)
                    {
                        return OperationResult.CreateFailure($"auto_dimension failed: {ex.Message}");
                    }
                }

                if (!inSketch)
                    return OperationResult.CreateFailure("Must be in sketch mode to add dimension");
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                // Support simple rectangle dimensioning: caller supplies cx, cy, w, h (in mm)
                // We'll place two sketch dimensions (width and height) at sensible locations
                double cx = step.Value<double?>("cx") ?? double.NaN;
                double cy = step.Value<double?>("cy") ?? double.NaN;
                double w = step.Value<double?>("w") ?? step.Value<double?>("width") ?? double.NaN;
                double h = step.Value<double?>("h") ?? step.Value<double?>("height") ?? double.NaN;

                if (double.IsNaN(cx) || double.IsNaN(cy) || double.IsNaN(w) || double.IsNaN(h))
                {
                    return OperationResult.CreateFailure("Dimension requires cx, cy, w and h (in mm)");
                }

                // Convert to meters (SolidWorks API uses meters)
                double mcx = ToMeters(cx);
                double mcy = ToMeters(cy);
                double mw = ToMeters(w);
                double mh = ToMeters(h);

                // Calculate edge midpoints for placing dimensions
                double leftX = mcx - mw / 2.0;
                double rightX = mcx + mw / 2.0;
                double bottomY = mcy - mh / 2.0;
                double topY = mcy + mh / 2.0;

                // Place width dimension near top edge center
                double widthDimX = mcx;
                double widthDimY = topY + (mh * 0.1); // offset above

                // Place height dimension near right edge center
                double heightDimX = rightX + (mw * 0.1); // offset to right
                double heightDimY = mcy;

                object widthDim = null;
                object heightDim = null;

                int createdCount = 0;
                int setCount = 0;
                var details = new Newtonsoft.Json.Linq.JArray();

                // Best-effort: try to create sketch/display dimensions using common
                // interop entry points via reflection so this compiles against
                // multiple SolidWorks interop versions. `AddDimension2` on the
                // active model is commonly available and will create a display
                // dimension anchored to the nearest geometry.
                object TryCreateDisplayDimension(double px, double py)
                {
                    try
                    {
                        var addDim = ((object)model).GetType().GetMethod("AddDimension2");
                        if (addDim != null)
                        {
                            // z=0 for sketch plane
                            var disp = addDim.Invoke(model, new object[] { px, py, 0.0 });
                            if (disp != null) createdCount++;
                            return disp;
                        }
                    }
                    catch { }
                    return null;
                }

                // Create dimensions at the chosen anchor points
                widthDim = TryCreateDisplayDimension(widthDimX, widthDimY);
                heightDim = TryCreateDisplayDimension(heightDimX, heightDimY);

                // Try to set the numeric values on created dimension objects via reflection
                bool TrySetDimensionValue(object dimObj, double meters)
                {
                    if (dimObj == null) return false;
                    try
                    {
                        var dimType = ((object)dimObj).GetType();
                        // Some interop objects expose the IDimension directly, others expose a display wrapper
                        // which has a GetDimension() method. Try to get the underlying dimension object first.
                        object underlying = null;
                        try
                        {
                            var getDim = dimType.GetMethod("GetDimension");
                            if (getDim != null) underlying = getDim.Invoke(dimObj, null);
                        }
                        catch { underlying = null; }

                        var target = underlying ?? dimObj;
                        var targetType = target.GetType();

                        var setSys = targetType.GetMethod("SetSystemValue3") ?? targetType.GetMethod("SetSystemValue");
                        if (setSys != null)
                        {
                            try { setSys.Invoke(target, new object[] { meters, 0, 0 }); return true; } catch { return false; }
                        }

                        var prop = targetType.GetProperty("SystemValue");
                        if (prop != null && prop.CanWrite)
                        {
                            try { prop.SetValue(target, meters); return true; } catch { return false; }
                        }
                    }
                    catch { }
                    return false;
                }

                var wSet = TrySetDimensionValue(widthDim, mw);
                var hSet = TrySetDimensionValue(heightDim, mh);
                if (widthDim != null || heightDim != null)
                {
                    if (widthDim != null) details.Add(new Newtonsoft.Json.Linq.JObject { ["anchor"] = "width", ["created"] = true, ["setValue"] = wSet });
                    else details.Add(new Newtonsoft.Json.Linq.JObject { ["anchor"] = "width", ["created"] = false, ["setValue"] = false });
                    if (heightDim != null) details.Add(new Newtonsoft.Json.Linq.JObject { ["anchor"] = "height", ["created"] = true, ["setValue"] = hSet });
                    else details.Add(new Newtonsoft.Json.Linq.JObject { ["anchor"] = "height", ["created"] = false, ["setValue"] = false });
                }
                if (wSet) setCount++;
                if (hSet) setCount++;

                var data = new Newtonsoft.Json.Linq.JObject
                {
                    ["createdCount"] = createdCount,
                    ["setCount"] = setCount,
                    ["details"] = details
                };

                // Success only if we actually created or set at least one dimension
                bool treatedSuccess = (createdCount > 0) || (setCount > 0);
                if (treatedSuccess)
                    return OperationResult.CreateSuccess(stillInSketch: true, data: data);
                else
                {
                    var fail = OperationResult.CreateFailure("No dimensions were created or value-set by the handler");
                    fail.Data = data;
                    return fail;
                }
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"dimension failed: {ex.Message}");
            }
        }

        private static double ToMeters(double mm) => mm / 1000.0;
    }
}
