using System;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services.Operations.PartFeatures
{
    internal static class PartFeatureHelpers
    {
        public static double ToMeters(double mm) => mm / 1000.0;
    }
    /// <summary>
    /// Handler for "extrude" operation - creates an extrusion (boss or cut)
    /// </summary>
    // ExtrudeHandler moved to ExtrudeHandler.cs

    /// <summary>
    /// Handler for "revolve" operation - creates a revolve feature (profile around axis)
    /// </summary>
    // RevolveHandler moved to RevolveHandler.cs

    /// <summary>
    /// Handler for "sweep" operation - creates a sweep feature (profile along path)
    /// </summary>
    // SweepHandler moved to SweepHandler.cs

    /// <summary>
    /// Handler for "loft" operation - creates a loft feature (blending multiple profiles)
    /// </summary>
    // LoftHandler moved to LoftHandler.cs

    /// <summary>
    /// Handler for "fillet" operation - adds fillet to edges
    /// </summary>
    // FilletHandler moved to FilletHandlers.cs

    /// <summary>
    /// Handler for "chamfer" operation - adds chamfer to edges
    /// </summary>
    // ChamferHandler moved to ChamferHandler.cs

    /// <summary>
    /// Handler for "hole" operation - creates a hole at specified location
    /// </summary>
    // HoleHandler moved to HoleHandler.cs

    /// <summary>
    /// Handler for "pocket" operation - creates a pocket (recessed feature)
    /// </summary>
    // PocketHandler moved to PocketHandler.cs
}
