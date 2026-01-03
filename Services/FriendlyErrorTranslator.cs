using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    /// <summary>
    /// Translates technical error details into simple, user-friendly English messages.
    /// Converts structured error objects, JSON validation responses, and exception messages
    /// into plain language that non-technical users can understand and act upon.
    /// </summary>
    public static class FriendlyErrorTranslator
    {
        private static readonly Dictionary<string, string> CommonErrorMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Dimension errors
            { "missing dimensions", "Please specify the size (e.g., 100mm x 50mm x 75mm)" },
            { "missing dimension", "Please specify dimensions (e.g., width x height x depth)" },
            { "dimension required", "Please tell me the measurements you want" },
            { "no dimensions", "I need the size - like '100mm x 100mm x 100mm'" },
            { "dimensions not specified", "Please tell me how big you want it (e.g., 100 x 50 x 75 mm)" },
            
            // Shape/geometry errors
            { "invalid shape", "That shape isn't recognized. Try: box, cylinder, sphere, cone, wedge" },
            { "unknown geometry", "I don't recognize that shape. Try: cube, cylinder, sphere, cone" },
            { "invalid geometry", "The shape you described isn't supported. Try simple shapes: box, cylinder" },
            
            // Parameter errors
            { "invalid parameter", "That value isn't quite right. Please check your input" },
            { "bad parameter", "I can't understand that value. Please try again" },
            { "parameter mismatch", "The shape needs different information - please describe it differently" },
            
            // Constraint/feature errors
            { "invalid constraint", "That constraint can't be applied. Try describing it more simply" },
            { "constraint failed", "I couldn't apply that constraint. Try a simpler description" },
            { "feature failed", "That feature couldn't be created. Please simplify your request" },
            
            // Radius/diameter errors
            { "missing radius", "Please specify the radius or diameter (e.g., 'radius 20mm')" },
            { "missing diameter", "Please tell me the diameter or radius" },
            { "invalid radius", "The radius value doesn't seem right. Try: 'radius 20mm'" },
            
            // Height/depth errors
            { "missing height", "Please specify the height (e.g., 'height 100mm')" },
            { "missing depth", "Please tell me how deep you want it" },
            { "missing length", "Please specify the length or height" },
            
            // Sketch errors
            { "sketch failed", "I had trouble creating the sketch. Please try a simpler shape" },
            { "sketch invalid", "The sketch couldn't be created. Please describe it more clearly" },
            { "invalid sketch", "I can't create that sketch. Try: 'box 100x100x100mm'" },
            
            // Part creation errors
            { "part creation failed", "I couldn't create the part. Please try a simpler design" },
            { "model failed", "The model couldn't be created. Please check your description" },
            { "solidworks error", "SolidWorks had an issue. Please try a different design" },
            
            // LLM/AI errors
            { "llm error", "The AI didn't respond properly. Please try again or simplify your request" },
            { "model timeout", "The AI took too long to respond. Please try a simpler request" },
            { "api error", "Connection issue with the AI. Please try again" },
            
            // Generic fallbacks
            { "error", "Something went wrong. Please try again or describe it differently" },
            { "failed", "That didn't work. Please try with a simpler description" },
            { "invalid", "I didn't understand that. Please try again" },
        };

        /// <summary>
        /// Translates a structured error object (JSON with 'valid', 'issue', 'suggestion' fields) 
        /// or raw error text into a user-friendly message.
        /// </summary>
        public static string TranslateError(object errorObject)
        {
            if (errorObject == null)
                return "Something went wrong. Please try again.";

            // Handle JObject (from JSON parsing)
            if (errorObject is JObject jobj)
                return TranslateJsonError(jobj);

            // Handle raw string errors
            string errorText = errorObject.ToString() ?? string.Empty;
            return TranslateErrorText(errorText);
        }

        /// <summary>
        /// Translates a JSON validation response with 'valid', 'issue', and 'suggestion' fields.
        /// </summary>
        private static string TranslateJsonError(JObject errorObj)
        {
            try
            {
                bool valid = errorObj["valid"]?.Value<bool>() ?? true;
                if (valid) return null; // No error

                string issue = errorObj["issue"]?.Value<string>() ?? "";
                string suggestion = errorObj["suggestion"]?.Value<string>() ?? "";

                // Build friendly message from suggestion first, then issue
                if (!string.IsNullOrWhiteSpace(suggestion))
                    return TranslateErrorText(suggestion);

                if (!string.IsNullOrWhiteSpace(issue))
                    return TranslateErrorText(issue);

                return "Please check your input and try again.";
            }
            catch
            {
                return "Something went wrong with your input. Please try again.";
            }
        }

        /// <summary>
        /// Translates raw error text using keyword matching and fallback logic.
        /// </summary>
        public static string TranslateErrorText(string errorText)
        {
            if (string.IsNullOrWhiteSpace(errorText))
                return "Something went wrong. Please try again.";

            errorText = errorText.Trim();

            // Check for exact matches first
            if (CommonErrorMappings.TryGetValue(errorText, out var mapped))
                return mapped;

            // Check for substring matches (case-insensitive)
            var matchedKey = CommonErrorMappings.Keys.FirstOrDefault(key =>
                errorText.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matchedKey != null)
                return CommonErrorMappings[matchedKey];

            // Keyword-based translation for variations
            if (ContainsAny(errorText, "dimension", "size", "measure"))
                return "Please specify dimensions (e.g., 100mm x 50mm x 75mm)";

            if (ContainsAny(errorText, "radius", "diameter"))
                return "Please specify the radius or diameter (e.g., 'radius 20mm')";

            if (ContainsAny(errorText, "height", "depth", "length"))
                return "Please specify the height or length (e.g., 'height 100mm')";

            if (ContainsAny(errorText, "shape", "geometry", "feature"))
                return "Please describe the shape more clearly (e.g., 'cube 100x100x100mm')";

            if (ContainsAny(errorText, "constraint", "sketch"))
                return "Please try a simpler description of your design";

            if (ContainsAny(errorText, "parse", "format", "syntax"))
                return "I didn't understand the format. Please try again more clearly";

            if (ContainsAny(errorText, "timeout", "llm", "ai", "model"))
                return "The AI service took too long. Please try a simpler request";

            if (ContainsAny(errorText, "solidworks", "application", "api"))
                return "SolidWorks had an issue. Please try a different design";

            // If nothing matches, provide a generic helpful message
            // Truncate if very long
            if (errorText.Length > 80)
                return "Something went wrong. Please try a simpler description.";

            // For short messages, echo them but prefix with user-friendly context
            return $"I couldn't complete that: {errorText}. Please try again.";
        }

        /// <summary>
        /// Creates a user-friendly summary of what went wrong and how to fix it.
        /// </summary>
        public static string CreateActionableMessage(string errorText, string originalPrompt = null)
        {
            string translated = TranslateErrorText(errorText);

            if (!string.IsNullOrWhiteSpace(originalPrompt))
                return $"{translated}\n\nYou wrote: '{originalPrompt}'";

            return translated;
        }

        /// <summary>
        /// Checks if the text contains any of the given keywords (case-insensitive).
        /// </summary>
        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return keywords.Any(kw => 
                text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Extracts and translates the most important information from a complex error message.
        /// </summary>
        public static string SimplifyComplexError(string complexError)
        {
            if (string.IsNullOrWhiteSpace(complexError))
                return "Something went wrong. Please try again.";

            // Try to parse as JSON first
            try
            {
                var jobj = JObject.Parse(complexError);
                return TranslateJsonError(jobj) ?? TranslateErrorText(complexError);
            }
            catch
            {
                // Not JSON, treat as raw text
            }

            // Extract first meaningful line from multi-line errors
            var lines = complexError.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
                return TranslateErrorText(lines[0]);

            return TranslateErrorText(complexError);
        }
    }
}
