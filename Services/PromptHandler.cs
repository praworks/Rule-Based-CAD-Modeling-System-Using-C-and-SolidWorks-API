using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    public static class PromptHandler
    {
        public const string DefaultTemplatePath = "Config/PromptTemplates.json";
        public const string DEFAULT_SYSTEM_PROMPT =
            "You are a CAD planning agent for SOLIDWORKS. " +
            "Convert user requests into step plan JSON with a top-level 'steps' array. " +
            "Supported ops: new_part; select_plane{name}; select_face{id}; sketch_begin; rectangle_center{cx,cy,w,h}; circle_center{cx,cy,r|diameter}; line; arc; dimension; constraint; sketch_end; extrude{depth}; extrude_cut{depth}; revolve; sweep; loft; fillet; chamfer; hole; pocket; thread{diameter,pitch,length,handedness,type}; set_material{material}; description{text}; zoom_to_fit. " +
            "CRITICAL: Use extrude_cut (separate op) for cuts, NOT extrude with type='cut'. Use select_face with id='top'/'front'/'right', NOT numeric IDs. " +
            "For plane selection, use ONLY these exact plane names: 'Top Plane', 'Front Plane', or 'Right Plane'. " +
            "For auto_dimension on circles, use radius or diameter field, NOT w/h. For rectangles, copy cx, cy, w, h values. " +
            "Units are millimeters. Output ONLY raw JSON - no markdown, no extra text.";

        public static string BuildRefineSystemPrompt()
        {
            return "You are a prompt refinement assistant for a CAD system. Your job is to take brief user input and expand it into a clear, detailed CAD specification.\n\n" +
                   "Rules:\n" +
                   "- If dimensions are missing, suggest reasonable defaults (e.g., 50mm for width/height, 100mm for depth)\n" +
                   "- ALWAYS add explicit auto-dimension instructions (use op:\"auto_dimension\") for any sketch geometry you produce (e.g., horizontal and vertical dimensions for rectangles, with numeric values in mm)\n" +
                   "- Always specify units (millimeters)\n" +
                   "- Clarify shape type (box, cylinder, etc.)\n" +
                   "- Fix grammar and spelling\n" +
                   "- Expand abbreviations\n" +
                   "- Keep it concise but complete\n\n" +
                   "Example:\n" +
                   "Input: 'box'\n" +
                   "Output: 'Create a rectangular box with width 50mm, height 50mm, and depth 100mm'\n\n" +
                   "Input: 'cyl r=20'\n" +
                   "Output: 'Create a cylinder with radius 20mm and height 100mm'\n\n" +
                   "Now refine this user input:";
        }

        public static string BuildErrorAnalysisPrompt(string summary)
        {
            return "Analyze this error and give 2 concise troubleshooting steps (non-sensitive):\n\n" +
                   (summary ?? string.Empty);
        }

        public static string BuildClassificationPrompt(string userPrompt, IEnumerable<string> categories)
        {
            var list = categories == null ? string.Empty : string.Join(", ", categories);
            return "Classify the CAD request into one of these categories: " + list + ".\n" +
                   "Return ONLY the category name (no extra text).\n\n" +
                   "Request: " + (userPrompt ?? string.Empty);
        }

        public static string BuildClassificationAndDescriptionPrompt(string userPrompt, IEnumerable<string> categories)
        {
            var list = categories == null ? string.Empty : string.Join(", ", categories);
            return "Classify the CAD request into one of these categories: " + list + ".\n" +
                   "Also provide a short 2-5 word description.\n" +
                   "Return ONLY a JSON object with keys \"category\" and \"description\".\n\n" +
                   "Request: " + (userPrompt ?? string.Empty);
        }

        public static string BuildClarificationLocalSystemPrompt()
        {
            return "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text. " +
                   "For dimension operations, you MUST copy the cx, cy, w, h values from the rectangle.";
        }

        public static string BuildTaskpaneLocalSystemPrompt()
        {
            return "You are a CAD planning agent for SOLIDWORKS.\n\n" +
                   "Return ONLY a single JSON OBJECT with keys 'thinking' (string) and 'steps' (array). " +
                   "'thinking' must describe geometric reasoning before the plan: plane selection, coordinate math, constraints, and why dimensions are chosen. " +
                   "'steps' must follow the plan schema.\n\n" +
                   "CRITICAL RULES:\n" +
                   "1. Extrusion cuts: use separate op 'extrude_cut' with 'depth'. NEVER use op 'extrude' with type='cut'.\n" +
                   "2. Face selection: use select_face with id='top'/'front'/'right' (NOT numeric IDs).\n" +
                   "3. Plane selection: use ONLY these exact plane names: 'Top Plane', 'Front Plane', or 'Right Plane'.\n" +
                   "4. Circle dimensions: for circle sketches, use op 'auto_dimension' with 'r' or 'diameter' (NOT w/h).\n\n" +
                   "Rectangles: copy cx, cy, w, h from rectangle_center into auto_dimension. Units are millimeters.";
        }

        public static string NormalizeCategory(string category, IEnumerable<string> categories)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Unknown";
            var trimmed = category.Trim();
            if (categories != null)
            {
                foreach (var c in categories)
                {
                    if (string.Equals(trimmed, c, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            return "Unknown";
        }

        public static PromptTemplateConfig LoadTemplateConfig()
        {
            var path = Environment.GetEnvironmentVariable("AICAD_PROMPT_TEMPLATES")
                       ?? DefaultTemplatePath;

            if (!File.Exists(path))
                return PromptTemplateConfig.Empty;

            try
            {
                var json = File.ReadAllText(path);
                var root = JObject.Parse(json);
                var cfg = new PromptTemplateConfig();
                cfg.CommonPreamble = root.Value<string>("common_preamble") ?? string.Empty;
                cfg.UnknownTemplate = root.Value<string>("unknown_template") ?? string.Empty;
                var arr = root["categories"] as JArray;
                if (arr != null)
                {
                    foreach (var item in arr)
                    {
                        var name = item?["name"]?.ToString();
                        var template = item?["template"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(template))
                            cfg.Categories[name] = template;
                    }
                }
                return cfg;
            }
            catch
            {
                return PromptTemplateConfig.Empty;
            }
        }

        public static string BuildTemplatePrompt(string template, string userText, string commonPreamble)
        {
            if (string.IsNullOrWhiteSpace(template))
                return userText ?? string.Empty;
            return template.Replace("{user_request}", userText ?? string.Empty)
                           .Replace("{common_preamble}", commonPreamble ?? string.Empty);
        }

        public static string BuildMissingPrompt(string systemPrompt, JArray missing)
        {
            // Strong directive: return only a JSON array. If required numeric values are missing,
            // choose safe defaults (cx=0, cy=0, w=100, h=100) rather than asking questions.
            // Additionally: ALWAYS include explicit dimension steps for any sketch geometry you create.
            return (systemPrompt ?? string.Empty) + "\n\n" +
                   "INSTRUCTIONS:\n" +
                   "- You MUST reply with a single JSON ARRAY only (no surrounding text, no commentary).\n" +
                   "- Each element must be a complete step object matching the SolidWorks plan schema.\n" +
                   "- For rectangle geometry, include numeric fields for the shape and prefer using the auto-dimension operator: \"op\":\"auto_dimension\" (or \"auto-dimension\"). Include numeric fields such as \"cx\", \"cy\", \"w\", \"h\" (all in mm).\n" +
                   "- ALWAYS include appropriate \"auto_dimension\" steps (op:\"auto_dimension\") for any sketch geometry you create (e.g., horizontal and vertical dimensions for rectangles with a numeric \"value\" in mm).\n" +
                "- If any numeric values are missing, do NOT ask questions — fill sensible defaults: cx=0, cy=0, w=100, h=100.\n" +
                "- Do NOT emit any natural-language question or explanation. Output JSON ONLY.\n\n" +
                   "Provide corrected steps for the following missing entries (same order):\n" +
                   (missing == null ? "[]" : missing.ToString());
        }

        public static string BuildSingleStepPrompt(string systemPrompt, JObject step, object handlerData)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- Reply with a single JSON OBJECT only (no commentary).\n");
            sb.AppendLine("- The object must be a valid plan step. For dimension steps include numeric fields: cx, cy, w, h (mm).\n");
            sb.AppendLine("- ALWAYS use op:'auto_dimension' (NOT 'dimension') for sketch dimension steps.\n");
            sb.AppendLine("- If you need numeric values, do NOT ask questions — supply sensible defaults: cx=0, cy=0, w=100, h=100.\n");
            sb.AppendLine("- Do NOT include any natural-language text; output JSON only.\n");
            sb.AppendLine("Original step:");
            sb.AppendLine(step == null ? "{}" : step.ToString());
            if (handlerData != null)
            {
                sb.AppendLine("Handler data:");
                try { sb.AppendLine(JToken.FromObject(handlerData).ToString()); } catch { sb.AppendLine(handlerData.ToString()); }
            }
            return sb.ToString();
        }

        public static string BuildDescriptionPrompt(string userPrompt)
        {
            userPrompt = userPrompt ?? string.Empty;
            return "Convert this CAD instruction into a simple 2-5 word description.\n" +
                   "Examples:\n" +
                   "- Input: 'create 100mm cube and apply chamfer' -> Output: Cube with chamfer\n" +
                   "- Input: 'make rectangular plate 200x150mm with 4 holes' -> Output: Plate with holes\n" +
                   "- Input: 'design cylinder diameter 50mm height 100mm' -> Output: Cylinder\n\n" +
                   "Now convert this:\n" +
                   $"Input: '{userPrompt}'\n" +
                   "Output:";
        }

        public static string BuildRefinePrompt(string systemPrompt, string rawPrompt)
        {
            var sys = systemPrompt ?? string.Empty;
            var raw = rawPrompt ?? string.Empty;
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(sys))
            {
                sb.Append(sys.Trim());
                sb.Append("\n\n");
            }
            sb.Append("User input: ").Append(raw).Append("\n\nRefined prompt:");
            return sb.ToString();
        }

        public static string BuildFinalPlanPrompt(string userText, string fewShotExamples, bool forceLocalOnly)
        {
            var text = userText ?? string.Empty;
            var examples = fewShotExamples ?? string.Empty;
            var userPrompt = (string.IsNullOrWhiteSpace(examples) ? string.Empty : examples + "\n\n") + "User request: ";

            if (forceLocalOnly)
            {
                return userPrompt + text +
                       "\n\nFORMAT:\nReturn a single JSON OBJECT with keys 'thinking' and 'steps'. Output ONLY the JSON object.";
            }

            return userPrompt + text +
                   "\n\nFORMAT:\nReturn a single JSON OBJECT with keys 'thinking' (string) and 'steps' (array).\n" +
                   "- 'thinking' must explain geometric reasoning: plane selection, coordinate math, constraints, and dimension choices.\n" +
                   "- 'steps' must be executable ops as per schema, prefer op:'auto_dimension' for sketch dimensions where applicable.\n" +
                   "Output ONLY the JSON object (no markdown fences).\n";
        }

        public static string BuildIntentPrompt(string systemPrompt, string intent, JObject facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- The user wants to modify an existing SolidWorks model based on their intent.");
            sb.AppendLine("- You are provided with the current model state (features, geometry, etc.).");
            sb.AppendLine("- Generate a JSON ARRAY of steps to fulfill the user's request.");
            sb.AppendLine("- Output ONLY the JSON array — no markdown, no extra text.");
            sb.AppendLine("- Use operations that work on the existing model (select faces, sketch, cut, etc.).");
            sb.AppendLine("- For dice pips: select face by id (top/bottom/left/right/front/back), sketch circles at calculated positions, extrude_cut shallow depth.\n");

            if (facts != null)
            {
                sb.AppendLine("CURRENT MODEL STATE:");
                sb.AppendLine(facts.ToString());
                sb.AppendLine();
            }

            sb.AppendLine("USER INTENT:");
            sb.AppendLine(intent ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("Generate the steps array now:");

            return sb.ToString();
        }

        public static string BuildIntentPromptWithCoT(string systemPrompt, string intent, JObject facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("FORMAT:");
            sb.AppendLine("Return a single JSON OBJECT with:");
            sb.AppendLine("{\"thinking\": string, \"steps\": [ ... ]}");
            sb.AppendLine("- 'thinking' must explain geometric reasoning prior to steps: plane selection, coordinate math, constraints, and why dimensions are chosen.");
            sb.AppendLine("- 'steps' must be executable ops for SolidWorks as per schema.");
            sb.AppendLine("- Output ONLY the JSON object — no markdown fences, no extra text.\n");

            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- The user wants to modify an existing SolidWorks model based on their intent.");
            sb.AppendLine("- You are provided with the current model state (features, geometry, etc.).");
            sb.AppendLine("- Ensure 'auto_dimension' is used for sketch dimensions where applicable, copying numeric fields when required.");
            sb.AppendLine("- Use operations that work on the existing model (select faces, sketch, cut, etc.).\n");

            if (facts != null)
            {
                sb.AppendLine("CURRENT MODEL STATE:");
                sb.AppendLine(facts.ToString());
                sb.AppendLine();
            }

            sb.AppendLine("USER INTENT:");
            sb.AppendLine(intent ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("Return the JSON object now:");

            return sb.ToString();
        }

        public static string BuildThreadSubtaskPrompt(string systemPrompt, JObject threadStep, JObject facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- This is a subtask to apply threads to an already-created part.");
            sb.AppendLine("- Generate ONLY the steps needed to apply thread features.");
            sb.AppendLine("- Output ONLY a JSON ARRAY of steps (no extra text).");
            sb.AppendLine("- Use select_face and op:'thread' with diameter/pitch/length; prefer provided thread parameters.");
            sb.AppendLine("- Do NOT recreate base geometry; assume the base part already exists.");

            if (facts != null)
            {
                sb.AppendLine("CURRENT MODEL STATE:");
                sb.AppendLine(facts.ToString());
                sb.AppendLine();
            }

            sb.AppendLine("THREAD REQUEST:");
            sb.AppendLine(threadStep == null ? "{}" : threadStep.ToString());
            sb.AppendLine();
            sb.AppendLine("Generate the thread-only steps array now:");

            return sb.ToString();
        }

        public static string BuildThreadSubtaskPromptFromRequest(string systemPrompt, string userRequest, JObject facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- This is a subtask to apply threads to an already-created part.");
            sb.AppendLine("- Generate ONLY the steps needed to apply thread features.");
            sb.AppendLine("- Output ONLY a JSON ARRAY of steps (no extra text).");
            sb.AppendLine("- Use select_face and op:'thread' with diameter/pitch/length parsed from the request.");
            sb.AppendLine("- Do NOT recreate base geometry; assume the shaft already exists.");

            if (facts != null)
            {
                sb.AppendLine("CURRENT MODEL STATE:");
                sb.AppendLine(facts.ToString());
                sb.AppendLine();
            }

            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(userRequest ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("Generate the thread-only steps array now:");

            return sb.ToString();
        }

        public static string BuildThreadbarShaftPrompt(string userRequest, string commonPreamble)
        {
            var sb = new StringBuilder();
            sb.AppendLine((commonPreamble ?? string.Empty));
            sb.AppendLine();
            sb.AppendLine("User request: " + (userRequest ?? string.Empty));
            sb.AppendLine();
            sb.AppendLine("Constraints:");
            sb.AppendLine("- Interpret as a cylindrical shaft only (no threads in this call).");
            sb.AppendLine("- Require length and diameter; assume mm.");
            sb.AppendLine("- Use sketch + extrude to create the shaft.");
            sb.AppendLine("- Use 'auto_dimension' for all sketch dimensions, including cx/cy/w/h.");
            sb.AppendLine();
            sb.AppendLine("Output ONLY the JSON object.");
            return sb.ToString();
        }

        public static string BuildFeatureDecomposePrompt(string systemPrompt, string userRequest)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- Decompose the request into ordered feature tasks.");
            sb.AppendLine("- Output ONLY a JSON ARRAY of objects, no extra text.");
            sb.AppendLine("- Each object must include: feature_type, intent, and params (object).");
            sb.AppendLine("- feature_type examples: base, hole, fillet, chamfer, thread, cut, pattern.");
            sb.AppendLine("- Keep tasks minimal and executable in sequence.");
            sb.AppendLine();
            sb.AppendLine("USER REQUEST:");
            sb.AppendLine(userRequest ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("Return the feature task array now:");
            return sb.ToString();
        }

        public static string BuildFeaturePlanPrompt(string systemPrompt, JObject featureTask, JObject facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine((systemPrompt ?? string.Empty) + "\n");
            sb.AppendLine("FORMAT:");
            sb.AppendLine("Return a single JSON OBJECT with:");
            sb.AppendLine("{\"thinking\": string, \"steps\": [ ... ]}");
            sb.AppendLine("- Output ONLY the JSON object (no extra text).");
            sb.AppendLine("- 'thinking' must briefly explain the reasoning for this feature task.");
            sb.AppendLine("- 'steps' must be executable for SolidWorks and match schema.");
            sb.AppendLine();
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- Plan ONLY this feature task. Do not add other features.");
            sb.AppendLine();
            if (facts != null)
            {
                sb.AppendLine("CURRENT MODEL STATE:");
                sb.AppendLine(facts.ToString());
                sb.AppendLine();
            }
            sb.AppendLine("FEATURE TASK:");
            sb.AppendLine(featureTask == null ? "{}" : featureTask.ToString());
            sb.AppendLine();
            sb.AppendLine("Return the steps array now:");
            return sb.ToString();
        }
    }

    public sealed class PromptTemplateConfig
    {
        public static readonly PromptTemplateConfig Empty = new PromptTemplateConfig();
        public Dictionary<string, string> Categories { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string CommonPreamble { get; set; } = string.Empty;
        public string UnknownTemplate { get; set; } = string.Empty;
    }
}
