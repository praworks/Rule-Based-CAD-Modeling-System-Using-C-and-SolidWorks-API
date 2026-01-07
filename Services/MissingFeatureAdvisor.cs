using System;
using System.Collections.Generic;

namespace AICAD.Services
{
    internal static class MissingFeatureAdvisor
    {
        private static readonly Dictionary<string, string> _opHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "base_flange", "Sheet Metal base flange not implemented — add a handler to create sheet metal parts with K-factor and reliefs." },
            { "edge_flange", "Sheet Metal edge flange not implemented — add handler for edge flanges and bend parameters." },
            { "sheet_metal", "Sheet Metal operations missing — implement Base/Edge Flange and flatten support." },
            { "hole_wizard", "Hole Wizard not implemented — add a handler to drive standard holes and patterns for mounting plates." },
            { "pattern_linear", "Linear pattern not implemented — add handler to pattern features for hole arrays and bolt circles (or circular pattern)." },
            { "pattern_circular", "Circular pattern not implemented — add handler to pattern features around an axis." },
            { "helix", "Helix creation not implemented — add handler to generate helix by pitch/height/diameter for threads and springs." },
            { "cosmetic_thread", "Cosmetic thread not implemented — add handler to apply lightweight threads to bolts/nuts/studs." }
        };

        public static string AdviseForUnknownOp(string op)
        {
            if (string.IsNullOrWhiteSpace(op)) return null;
            if (_opHints.TryGetValue(op.Trim(), out var msg)) return msg;
            // Soft guesses for common aliases
            var ol = op.Trim().ToLowerInvariant();
            if (ol.Contains("flange")) return "Sheet Metal flange operation missing — implement Base/Edge Flange handlers.";
            if (ol.Contains("wizard")) return "Hole Wizard missing — implement a handler to drive standard hole features.";
            if (ol.Contains("pattern")) return "Feature pattern missing — implement linear/circular pattern handlers.";
            if (ol.Contains("helix")) return "Helix missing — implement helix handler for threads/springs.";
            if (ol.Contains("thread")) return "Threading missing — implement cosmetic thread and optional sweep-cut threads.";
            return null;
        }

        public static string AdviseForFailure(string op, string error)
        {
            var hint = AdviseForUnknownOp(op);
            if (!string.IsNullOrWhiteSpace(hint)) return hint;

            var e = (error ?? string.Empty).ToLowerInvariant();
            if (e.Contains("not yet implemented"))
                return "Operation not implemented — register a handler for this feature in OperationRegistry.";
            if (e.Contains("ensure profile") && e.Contains("axis"))
                return "Revolve needs a profile and axis/centerline preselected in the same sketch.";
            if (e.Contains("profile") && e.Contains("path") && e.Contains("sweep"))
                return "Sweep needs profile then path preselected; close sketch if still editing.";
            return null;
        }
    }
}
