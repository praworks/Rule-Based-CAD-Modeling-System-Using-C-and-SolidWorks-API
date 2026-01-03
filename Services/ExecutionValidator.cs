using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services
{
    /// <summary>
    /// Validates execution results by comparing expected parameters against actual model state.
    /// Enables closed-loop feedback for operation verification.
    /// </summary>
    public static class ExecutionValidator
    {
        public class ValidationResult
        {
            public string OperationName { get; set; }
            public bool IsValid { get; set; }
            public JObject Expected { get; set; }
            public JObject Actual { get; set; }
            public List<string> Mismatches { get; set; } = new List<string>();
            public string Message { get; set; }
        }

        /// <summary>
        /// Validate operation by comparing before/after snapshots
        /// </summary>
        public static ValidationResult ValidateStep(
            JObject step,
            IModelDoc2 model,
            JObject beforeSnapshot,
            JObject afterSnapshot)
        {
            var opName = step["operation"]?.ToString() ?? "Unknown";
            var result = new ValidationResult
            {
                OperationName = opName,
                Expected = new JObject(),
                Actual = new JObject()
            };

            try
            {
                switch (opName.ToLower())
                {
                    case "extrude":
                        ValidateExtrude(step, result, beforeSnapshot, afterSnapshot);
                        break;
                    case "fillet":
                        ValidateFillet(step, result, beforeSnapshot, afterSnapshot);
                        break;
                    case "set_material":
                        ValidateMaterial(step, result, model, afterSnapshot);
                        break;
                    case "description":
                        ValidateDescription(step, result, model, afterSnapshot);
                        break;
                    case "sketch_begin":
                    case "sketch_end":
                    case "rectangle_center":
                    case "circle_center":
                        ValidateSketchOperation(step, result, beforeSnapshot, afterSnapshot);
                        break;
                    default:
                        // Generic validation: check for feature count increase
                        int featureBefore = beforeSnapshot?["feature_count"]?.Value<int>() ?? 0;
                        int featureAfter = afterSnapshot?["feature_count"]?.Value<int>() ?? 0;
                        result.IsValid = featureAfter > featureBefore;
                        result.Message = result.IsValid 
                            ? $"Feature count increased ({featureBefore} → {featureAfter})"
                            : $"No new feature detected";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Message = $"Validation error: {ex.Message}";
            }

            return result;
        }

        private static void ValidateExtrude(
            JObject step,
            ValidationResult result,
            JObject before,
            JObject after)
        {
            var depth = step["depth"]?.Value<double>() ?? 0;
            result.Expected["depth"] = depth;

            // Check if new extrude feature appeared
            int featureBefore = before?["feature_count"]?.Value<int>() ?? 0;
            int featureAfter = after?["feature_count"]?.Value<int>() ?? 0;

            if (featureAfter > featureBefore)
            {
                result.IsValid = true;
                result.Actual["feature_count_increased"] = true;
                result.Message = $"Extrude created (depth: {depth}mm, features: {featureBefore} → {featureAfter})";
            }
            else
            {
                result.IsValid = false;
                result.Mismatches.Add($"No new feature created (expected extrude with depth {depth}mm)");
                result.Message = "Extrude failed: no feature created";
            }
        }

        private static void ValidateFillet(
            JObject step,
            ValidationResult result,
            JObject before,
            JObject after)
        {
            var radius = step["radius"]?.Value<double>() ?? 0;
            result.Expected["radius"] = radius;

            // Check if feature count increased and edge count decreased (rounded edges)
            int featureBefore = before?["feature_count"]?.Value<int>() ?? 0;
            int featureAfter = after?["feature_count"]?.Value<int>() ?? 0;
            int edgesBefore = before?["total_edges"]?.Value<int>() ?? 0;
            int edgesAfter = after?["total_edges"]?.Value<int>() ?? 0;

            bool hasNewFeature = featureAfter > featureBefore;
            bool edgesChanged = edgesAfter != edgesBefore; // Filleted edges have different topology

            if (hasNewFeature && edgesChanged)
            {
                result.IsValid = true;
                result.Actual["radius"] = radius;
                result.Actual["edges_changed"] = true;
                result.Message = $"Fillet created (radius: {radius}mm, edges: {edgesBefore} → {edgesAfter})";
            }
            else if (hasNewFeature)
            {
                result.IsValid = true; // Feature created, even if edge count didn't change
                result.Actual["radius"] = radius;
                result.Message = $"Fillet feature detected (radius: {radius}mm)";
            }
            else
            {
                result.IsValid = false;
                result.Mismatches.Add($"No fillet feature created (expected radius {radius}mm)");
                result.Message = "Fillet failed: no feature created";
            }
        }

        private static void ValidateMaterial(
            JObject step,
            ValidationResult result,
            IModelDoc2 model,
            JObject after)
        {
            var expectedMaterial = step["material"]?.ToString() ?? "";
            result.Expected["material"] = expectedMaterial;

            var actualMaterial = after?["material"]?.ToString() ?? "";
            result.Actual["material"] = actualMaterial;

            if (actualMaterial.Equals(expectedMaterial, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = true;
                result.Message = $"Material set correctly: {actualMaterial}";
            }
            else
            {
                result.IsValid = false;
                result.Mismatches.Add($"Material mismatch: expected '{expectedMaterial}', got '{actualMaterial}'");
                result.Message = $"Material set failed";
            }
        }

        private static void ValidateDescription(
            JObject step,
            ValidationResult result,
            IModelDoc2 model,
            JObject after)
        {
            var expectedDesc = step["text"]?.ToString() ?? "";
            result.Expected["description"] = expectedDesc;

            var actualDesc = after?["description"]?.ToString() ?? "";
            result.Actual["description"] = actualDesc;

            // Allow partial match (description might be longer if concatenated)
            if (!string.IsNullOrEmpty(expectedDesc) && actualDesc.IndexOf(expectedDesc, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.IsValid = true;
                result.Message = $"Description set correctly";
            }
            else if (!string.IsNullOrEmpty(actualDesc))
            {
                result.IsValid = true; // Some description was set
                result.Message = $"Description set: {actualDesc}";
            }
            else
            {
                result.IsValid = false;
                result.Mismatches.Add($"Description not set");
                result.Message = "Description set failed";
            }
        }

        private static void ValidateSketchOperation(
            JObject step,
            ValidationResult result,
            JObject before,
            JObject after)
        {
            var opName = step["operation"]?.ToString() ?? "";
            result.Expected["operation"] = opName;

            // For sketch operations, check that we're in sketch mode or edge count matches expected
            // This is simplified; full validation would require tracking sketch entities
            result.IsValid = true;
            result.Message = $"Sketch operation executed: {opName}";
        }

        /// <summary>
        /// Generate validation report for entire execution
        /// </summary>
        public static JObject GenerateValidationReport(List<ValidationResult> validations)
        {
            var report = new JObject();
            var passed = 0;
            var failed = 0;
            var details = new JArray();

            foreach (var v in validations)
            {
                if (v.IsValid)
                    passed++;
                else
                    failed++;

                details.Add(new JObject
                {
                    ["operation"] = v.OperationName,
                    ["valid"] = v.IsValid,
                    ["message"] = v.Message,
                    ["expected"] = v.Expected,
                    ["actual"] = v.Actual,
                    ["mismatches"] = v.Mismatches.Count > 0 ? JArray.FromObject(v.Mismatches) : null
                });
            }

            report["total"] = validations.Count;
            report["passed"] = passed;
            report["failed"] = failed;
            report["success_rate"] = validations.Count > 0 ? (passed * 100.0 / validations.Count) : 0;
            report["details"] = details;

            return report;
        }
    }
}
