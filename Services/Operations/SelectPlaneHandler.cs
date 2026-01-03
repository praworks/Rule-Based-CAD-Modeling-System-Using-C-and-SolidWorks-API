using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations
{
    internal class SelectPlaneHandler : IOperationHandler
    {
        public OperationResult Execute(JObject stepData, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch, out bool inSketchAfter)
        {
            inSketchAfter = inSketch;
            try
            {
                if (model == null)
                    return OperationResult.Fail("Model not initialized (call new_part first)");

                string name = stepData.Value<string>("name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    return OperationResult.Fail("Missing plane name");

                var sel = model.Extension.SelectByID2(name, "PLANE", 0, 0, 0, false, 0, null, 0);
                if (!sel)
                {
                    var candidates = new System.Collections.Generic.List<string>();
                    var n = name.Trim();
                    if (!string.IsNullOrEmpty(n))
                    {
                        candidates.Add(n);
                        if (!n.EndsWith(" Plane", System.StringComparison.OrdinalIgnoreCase)) 
                            candidates.Add(n + " Plane");
                        if (n.IndexOf("xy", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("x-y", System.StringComparison.OrdinalIgnoreCase) >= 0) 
                            candidates.Add("Top Plane");
                        if (n.IndexOf("xz", System.StringComparison.OrdinalIgnoreCase) >= 0) 
                            candidates.Add("Front Plane");
                        if (n.IndexOf("yz", System.StringComparison.OrdinalIgnoreCase) >= 0) 
                            candidates.Add("Right Plane");

                        foreach (var cand in candidates)
                        {
                            sel = model.Extension.SelectByID2(cand, "PLANE", 0, 0, 0, false, 0, null, 0);
                            if (sel) break;
                        }
                    }
                    if (!sel)
                        return OperationResult.Fail($"Could not select plane '{name}'");
                }

                return OperationResult.Ok();
            }
            catch (System.Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }
    }
}
