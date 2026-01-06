using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using AICAD.Services;

namespace AICAD.Services.Operations.Utilities
{
    /// <summary>
    /// Handler for "set_units" and related operations produced by LLMs.
    /// Accepts keys: "units", "unit", "unit_string", "value".
    /// </summary>
    public class SetUnitsHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                // Extract unit string from common field names
                string unit = step.Value<string>("units") ??
                              step.Value<string>("unit") ??
                              step.Value<string>("unit_string") ??
                              step.Value<string>("value") ??
                              string.Empty;

                if (string.IsNullOrWhiteSpace(unit))
                    return OperationResult.CreateFailure("Missing units value");

                // Get ISldWorks instance. Some interop versions don't expose GetApplication on IModelDoc2,
                // so fall back to obtaining the running COM object for SolidWorks.
                ISldWorks swApp = null;
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { swApp = null; }
                if (swApp == null)
                {
                    // As a best-effort fallback, try to get application via model if the method exists at runtime
                    try
                    {
                        var mi = ((object)model).GetType().GetMethod("GetApplication");
                        if (mi != null)
                        {
                            var appObj = mi.Invoke(model, null);
                            swApp = appObj as ISldWorks;
                        }
                    }
                    catch { swApp = null; }
                }
                if (swApp == null)
                    return OperationResult.CreateFailure("Could not obtain SolidWorks application instance");

                bool ok = UnitManager.SetUnits(swApp, unit);
                if (!ok)
                    return OperationResult.CreateFailure("SetUnits returned failure (no active document?)");

                return OperationResult.CreateSuccess(stillInSketch: inSketch, data: new { units = unit });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"set_units failed: {ex.Message}");
            }
        }
    }
}
