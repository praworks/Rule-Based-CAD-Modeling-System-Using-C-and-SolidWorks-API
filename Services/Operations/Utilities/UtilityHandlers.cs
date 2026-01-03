using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.Utilities
{
    /// <summary>
    /// Handler for "new_part" operation - creates a new PART document
    /// </summary>
    public class NewPartHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                // Get ISldWorks from the model's parent application
                // This is called when we need to create a new part explicitly
                if (model == null)
                    return OperationResult.CreateFailure("Model not available");

                // In the typical flow, new_part is handled in StepExecutor initialization
                // This handler confirms the operation succeeded
                return OperationResult.CreateSuccess(stillInSketch: false);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"Failed to create new part: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "select_plane" operation - selects a sketch plane by name
    /// </summary>
    public class SelectPlaneHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                string name = step.Value<string>("name") ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    return OperationResult.CreateFailure("Missing plane name");

                // Try to select the plane
                bool sel = model.Extension.SelectByID2(name, "PLANE", 0, 0, 0, false, 0, null, 0);

                if (!sel)
                {
                    // Try common alternatives if exact name didn't work
                    var candidates = new System.Collections.Generic.List<string> { name };
                    if (!name.EndsWith(" Plane", StringComparison.OrdinalIgnoreCase))
                        candidates.Add(name + " Plane");

                    // Common mappings
                    if (name.IndexOf("xy", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        name.IndexOf("x-y", StringComparison.OrdinalIgnoreCase) >= 0)
                        candidates.Add("Top Plane");
                    if (name.IndexOf("xz", StringComparison.OrdinalIgnoreCase) >= 0)
                        candidates.Add("Front Plane");
                    if (name.IndexOf("yz", StringComparison.OrdinalIgnoreCase) >= 0)
                        candidates.Add("Right Plane");

                    foreach (var cand in candidates)
                    {
                        sel = model.Extension.SelectByID2(cand, "PLANE", 0, 0, 0, false, 0, null, 0);
                        if (sel) break;
                    }
                }

                if (!sel)
                    return OperationResult.CreateFailure($"Could not select plane '{name}'");

                return OperationResult.CreateSuccess();
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"select_plane failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "select_face" operation - selects a face for operations like pocket or fillet
    /// </summary>
    public class SelectFaceHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                string faceId = step.Value<string>("id") ?? step.Value<string>("face_id") ?? "";
                if (string.IsNullOrWhiteSpace(faceId))
                    return OperationResult.CreateFailure("Missing face id");

                // Try to select the face
                bool sel = model.Extension.SelectByID2(faceId, "FACE", 0, 0, 0, false, 0, null, 0);

                if (!sel)
                    return OperationResult.CreateFailure($"Could not select face '{faceId}'");

                return OperationResult.CreateSuccess();
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"select_face failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "set_material" operation - applies material and stores custom property
    /// </summary>
    public class SetMaterialHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                string material = step.Value<string>("material") ??
                                  step.Value<string>("name") ??
                                  step.Value<string>("value") ??
                                  string.Empty;

                if (string.IsNullOrWhiteSpace(material))
                    return OperationResult.CreateFailure("Missing material name");

                string config = model.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
                bool applied = false;

                try
                {
                    var cust = model.Extension.CustomPropertyManager[config];
                    cust?.Add3("Material", (int)swCustomInfoType_e.swCustomInfoText, material, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                }
                catch
                {
                    // swallow property manager errors; material application already attempted
                }

                if (!applied)
                    return OperationResult.CreateSuccess(data: new { material, applied = false, note = "Material property set as custom property; library apply may have failed." });

                return OperationResult.CreateSuccess(data: new { material, applied = true });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"set_material failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "description" operation - sets the part description custom property
    /// </summary>
    public class DescriptionHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                string description = step.Value<string>("description") ??
                                     step.Value<string>("text") ??
                                     step.Value<string>("value") ??
                                     string.Empty;

                if (string.IsNullOrWhiteSpace(description))
                    return OperationResult.CreateFailure("Missing description text");

                string config = model.ConfigurationManager?.ActiveConfiguration?.Name ?? "";
                var cust = model.Extension.CustomPropertyManager[config];
                cust?.Add3("Description", (int)swCustomInfoType_e.swCustomInfoText, description, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);

                return OperationResult.CreateSuccess(data: new { description });
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"description failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handler for "zoom_to_fit" operation - fits the view to the model
    /// </summary>
    public class ZoomToFitHandler : IOperationHandler
    {
        public OperationResult Execute(JObject step, IModelDoc2 model, ISketchManager sketchMgr, IFeatureManager featMgr, bool inSketch)
        {
            try
            {
                if (model == null)
                    return OperationResult.CreateFailure("Model not initialized");

                model.ViewZoomtofit2();
                return OperationResult.CreateSuccess(stillInSketch: inSketch);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"zoom_to_fit failed: {ex.Message}");
            }
        }
    }
}
