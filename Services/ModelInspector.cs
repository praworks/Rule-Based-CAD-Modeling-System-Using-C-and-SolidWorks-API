using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services
{
    /// <summary>
    /// Inspects a SolidWorks model to extract geometry facts.
    /// Used for post-execution validation in closed-loop workflows.
    /// </summary>
    public static class ModelInspector
    {
        /// <summary>
        /// Inspect model and return all geometry facts
        /// </summary>
        public static JObject InspectModel(IModelDoc2 model)
        {
            if (model == null)
                return new JObject { ["error"] = "Model not available" };

            var result = new JObject();
            try
            {
                result["title"] = model.GetTitle() ?? "Untitled";

                // Enumerate features to detect geometry creation between validation snapshots
                var features = new JArray();
                int featureCount = 0;
                try
                {
                    var skipTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "HistoryFolder", "CommentsFolder", "NotesFolder", "LightsFolder",
                        "MaterialFolder", "RefPlane", "OriginProfileFeature", "RefPoint",
                        "RefAxis", "SolidBodyFolder", "SurfaceBodyFolder", "SheetMetal",
                        "ProfileFeature"
                    };

                    var feat = model.FirstFeature();
                    while (feat != null)
                    {
                        string type = string.Empty;
                        string name = string.Empty;
                        try { type = feat.GetTypeName2() ?? string.Empty; } catch { type = string.Empty; }
                        try { name = feat.Name ?? string.Empty; } catch { name = string.Empty; }
                        bool suppressed = false;
                        try { suppressed = feat.IsSuppressed(); } catch { suppressed = false; }

                        if (!suppressed && !skipTypes.Contains(type))
                        {
                            featureCount++;
                            features.Add(new JObject { ["name"] = name, ["type"] = type });
                            AddinStatusLogger.Log("ModelInspector", $"Counted feature: {name} (type={type})");
                        }
                        else if (!suppressed)
                        {
                            AddinStatusLogger.Log("ModelInspector", $"Skipped feature: {name} (type={type}, skipped=true)");
                        }

                        feat = feat.GetNextFeature();
                    }
                }
                catch (Exception enumEx) 
                { 
                    AddinStatusLogger.Log("ModelInspector", $"Feature enumeration failed: {enumEx.Message}");
                }
                result["feature_count"] = featureCount;
                result["features"] = features;
                AddinStatusLogger.Log("ModelInspector", $"Enumerated {featureCount} features (non-folder/non-suppressed)");

                // Query body geometry for edge/face counts
                var partDoc = model as IPartDoc;
                if (partDoc != null)
                {
                    var bodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
                    if (bodies != null && bodies.Length > 0)
                    {
                        var bodyInfo = new JArray();
                        int totalEdges = 0, totalFaces = 0;

                        foreach (IBody2 body in bodies)
                        {
                            if (body == null) continue;
                            var edges = (object[])body.GetEdges();
                            var faces = (object[])body.GetFaces();
                            int edgeCount = edges?.Length ?? 0;
                            int faceCount = faces?.Length ?? 0;
                            totalEdges += edgeCount;
                            totalFaces += faceCount;
                            bodyInfo.Add(new JObject { ["edge_count"] = edgeCount, ["face_count"] = faceCount });
                        }

                        result["bodies"] = bodyInfo;
                        result["total_edges"] = totalEdges;
                        result["total_faces"] = totalFaces;
                    }
                }

                // Query custom properties and resolved material name (configuration-aware)
                try
                {
                    var custMgr = model.Extension.CustomPropertyManager[""];
                    if (custMgr != null)
                    {
                        string material = "", matResolved = "";
                        string description = "", descResolved = "";
                        string mass = "", massResolved = "";
                        string weight = "", weightResolved = "";
                        custMgr.Get2("Material", out material, out matResolved);
                        custMgr.Get2("Description", out description, out descResolved);
                        custMgr.Get2("Mass", out mass, out massResolved);
                        custMgr.Get2("Weight", out weight, out weightResolved);
                        // Prefer resolved material name (user-facing) when available
                        result["material"] = string.IsNullOrWhiteSpace(matResolved) ? (material ?? "") : matResolved;
                        result["material_resolved"] = matResolved ?? "";
                        result["description"] = description ?? "";
                        result["mass"] = mass ?? "";
                        result["weight"] = weight ?? "";
                    }
                }
                catch { }

                result["success"] = true;
            }
            catch (Exception ex)
            {
                result["error"] = ex.Message;
                result["success"] = false;
            }
            return result;
        }

        /// <summary>
        /// Get summary of actual geometry
        /// </summary>
        public static string GetGeometrySummary(IModelDoc2 model)
        {
            var facts = InspectModel(model);
            if (facts["error"] != null)
                return $"Inspection error: {facts["error"]}";

            var lines = new List<string>();
            lines.Add($"Model: {facts["title"]}");
            if (facts["total_edges"] != null)
                lines.Add($"Total edges: {facts["total_edges"]}");
            if (facts["total_faces"] != null)
                lines.Add($"Total faces: {facts["total_faces"]}");
            if (!string.IsNullOrEmpty((string)facts["material"]))
                lines.Add($"Material: {facts["material"]}");
            if (!string.IsNullOrEmpty((string)facts["description"]))
                lines.Add($"Description: {facts["description"]}");
            if (!string.IsNullOrEmpty((string)facts["weight"]))
                lines.Add($"Weight: {facts["weight"]}");
            return string.Join("\n", lines);
        }
    }
}
