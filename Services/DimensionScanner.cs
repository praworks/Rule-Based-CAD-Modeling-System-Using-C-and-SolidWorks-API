using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services
{
    /// <summary>
    /// Attempts multiple defensive strategies to locate and serialize dimensions
    /// from a SolidWorks model. Uses reflection to tolerate different interop
    /// surface variations across SolidWorks versions.
    /// </summary>
    public static class DimensionScanner
    {
        public static JArray ScanDimensions(IModelDoc2 model, bool emitLogs = true)
        {
            var results = new JArray();
            if (model == null) return results;

            try
            {
                // Strategy 1: try direct collection-returning methods on model
                var dims = TryGetArrayFromMethod(model, "GetDisplayDimensions")
                           ?? TryGetArrayFromMethod(model, "GetDimensions")
                           ?? TryGetArrayFromMethod(model, "GetAnnotations");

                if (dims != null)
                {
                    foreach (var d in dims)
                    {
                        var j = SerializeDimensionObject(d);
                        if (j != null) results.Add(j);
                    }
                }
                else
                {
                    // Strategy 2: try iterator-style API (GetFirstDisplayDimension / GetFirstDimension)
                    var first = TryInvokeMethod(model, "GetFirstDisplayDimension") ?? TryInvokeMethod(model, "GetFirstDimension");
                    if (first != null)
                    {
                        var cur = first;
                        while (cur != null)
                        {
                            var j = SerializeDimensionObject(cur);
                            if (j != null) results.Add(j);
                            cur = TryInvokeMethod(cur, "GetNext") ?? TryInvokeMethod(cur, "GetNextDisplayDimension");
                        }
                    }
                    else
                    {
                        // Strategy 3: walk features and inspect each for attached dimensions
                        try
                        {
                            var feat = model.FirstFeature();
                            while (feat != null)
                            {
                                object dimsFromFeat = TryGetArrayFromMethod(feat, "GetDisplayDimensions")
                                                       ?? TryGetArrayFromMethod(feat, "GetDimensions");
                                if (dimsFromFeat is object[] arr)
                                {
                                    foreach (var d in arr)
                                    {
                                        var j = SerializeDimensionObject(d);
                                        if (j != null) results.Add(j);
                                    }
                                }
                                feat = feat.GetNextFeature();
                            }
                        }
                        catch { /* best-effort */ }
                    }
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Log("ModelInspector", $"Dimension scan failed: {ex.Message}");
            }

            if (emitLogs)
                AddinStatusLogger.Log("ModelInspector", $"Found {results.Count} dimensions");
            return results;
        }

        private static object TryInvokeMethod(object target, string methodName)
        {
            if (target == null) return null;
            try
            {
                var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                    return mi.Invoke(target, null);
            }
            catch { }
            return null;
        }

        private static object[] TryGetArrayFromMethod(object target, string methodName)
        {
            var o = TryInvokeMethod(target, methodName);
            if (o is object[] arr) return arr;
            // sometimes returns System.Array
            if (o is Array a)
            {
                var list = new List<object>();
                foreach (var v in a) list.Add(v);
                return list.ToArray();
            }
            return null;
        }

        private static JObject SerializeDimensionObject(object dimObj)
        {
            if (dimObj == null) return null;
            var jo = new JObject();
            try
            {
                // Attempt common calls/properties via reflection
                jo["type"] = dimObj.GetType().Name;

                // Name
                var name = TryCallString(dimObj, "GetName") ?? TryCallString(dimObj, "Name") ?? TryCallString(dimObj, "get_Name");
                if (!string.IsNullOrEmpty(name)) jo["name"] = name;

                // Value/system value
                var val = TryCallDouble(dimObj, "GetSystemValue3") ?? TryCallDouble(dimObj, "GetSystemValue") ?? TryCallDouble(dimObj, "GetValue") ?? TryCallDouble(dimObj, "Value");
                if (val.HasValue) jo["system_value"] = val.Value;

                // Text / display string
                var text = TryCallString(dimObj, "GetDisplayValue") ?? TryCallString(dimObj, "GetDimensionText") ?? TryCallString(dimObj, "GetText") ?? dimObj.ToString();
                if (!string.IsNullOrEmpty(text)) jo["text"] = text;

                // Anchor/parent feature if available
                var parent = TryCallString(dimObj, "GetFeatureName") ?? TryCallString(dimObj, "FeatureName");
                if (!string.IsNullOrEmpty(parent)) jo["parent_feature"] = parent;

                // If it's an annotation/dimension, try to get attached geometry hints
                var refName = TryCallString(dimObj, "GetReferenceGeometry") ?? TryCallString(dimObj, "GetReferences");
                if (!string.IsNullOrEmpty(refName)) jo["refs"] = refName;
            }
            catch { return null; }
            return jo;
        }

        private static string TryCallString(object target, string methodName)
        {
            try
            {
                var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    var o = mi.Invoke(target, null);
                    if (o != null) return o.ToString();
                }
                // Try property
                var pi = target.GetType().GetProperty(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null)
                {
                    var o = pi.GetValue(target);
                    if (o != null) return o.ToString();
                }
            }
            catch { }
            return null;
        }

        private static double? TryCallDouble(object target, string methodName)
        {
            try
            {
                var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    var o = mi.Invoke(target, null);
                    if (o is double d) return d;
                    if (o is float f) return (double)f;
                    if (o is int i) return (double)i;
                    if (o != null && double.TryParse(o.ToString(), out var parsed)) return parsed;
                }
                var pi = target.GetType().GetProperty(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null)
                {
                    var o = pi.GetValue(target);
                    if (o is double d2) return d2;
                    if (o != null && double.TryParse(o.ToString(), out var parsed2)) return parsed2;
                }
            }
            catch { }
            return null;
        }
    }
}
