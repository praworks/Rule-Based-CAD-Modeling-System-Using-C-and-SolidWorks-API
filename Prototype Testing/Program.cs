// SolidWorks 2024 example: create a 30mm diameter path circle sketch, then create a Sweep Boss/Base
// using a *circular profile* of 10mm diameter (no separate profile sketch).
//
// IMPORTANT:
// 1) SolidWorks API sweep creation has a few method/interface variations across versions/templates.
// 2) The most reliable way to match your exact UI choices is: do it once manually → Tools > Macro > Record,
//    then compare/merge with this code. The sketch creation below is stable; the sweep call may need
//    minor adjustment to your environment.
//
// References (COM):
// - SolidWorks 2024 Type Library
// - SolidWorks Constant type library
//
// Units: SolidWorks API uses meters.
// 30mm = 0.03m (diameter), radius = 0.015m
// 10mm = 0.01m (diameter)

using System;
using System.Runtime.InteropServices;
using SldWorks;
using SwConst;

public class MakeSweepCircularProfile
{
    public static void Main()
    {
        SldWorks.SldWorks swApp = null;
        ModelDoc2 model = null;

        try
        {
            swApp = (SldWorks.SldWorks)Marshal.GetActiveObject("SldWorks.Application");
            model = swApp.ActiveDoc as ModelDoc2;
            if (model == null) throw new Exception("No active SolidWorks document.");

            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new Exception("Active document is not a Part.");

            // ---------- 1) Create path sketch: 30mm diameter circle on Front Plane ----------
            const double pathDia = 0.03;   // meters
            double pathRad = pathDia / 2.0;

            // Select Front Plane
            bool ok = model.Extension.SelectByID2("Front Plane", "PLANE", 0, 0, 0, false, 0, null, 0);
            if (!ok) throw new Exception("Failed to select Front Plane.");

            var skMgr = model.SketchManager;
            skMgr.InsertSketch(true);

            // Create a circle centered at origin, radius = 0.015m (15mm)
            // CreateCircleByRadius(x, y, z, radius)
            skMgr.CreateCircleByRadius(0, 0, 0, pathRad);

            skMgr.InsertSketch(true); // exit sketch

            // Name the sketch so we can select it reliably
            // (Newest sketch is usually selected in FeatureManager; to keep it deterministic,
            //  we’ll select by the default name "Sketch1" if it's the first sketch.)
            // If your part already has sketches, change "Sketch1" accordingly.
            const string pathSketchName = "Sketch1";

            // ---------- 2) Create Sweep Boss/Base using circular profile diameter 10mm ----------
            const double profileDia = 0.01; // meters

            // Select the path sketch (for sweep path)
            ok = model.Extension.SelectByID2(pathSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
            if (!ok)
                throw new Exception($"Failed to select path sketch '{pathSketchName}'. Rename/select the correct sketch.");

            // Approach A (preferred when available): create a sweep feature definition and set circular profile diameter.
            // This avoids needing a separate profile sketch.
            //
            // Depending on your installed API, the exact interface/property names may differ slightly.
            // Using dynamic keeps it flexible; if a property name differs, record a macro once and align names.
            var featMgr = model.FeatureManager;
            dynamic sweepDef = featMgr.CreateDefinition((int)swFeatureNameID_e.swFmSweep);

            // Common sweep-def settings:
            // - Use selected sketch as path
            // - Use circular profile and set diameter
            //
            // Property names can vary; these are the ones most commonly exposed for circular-profile sweeps.
            // If one line throws at runtime, record a macro for a circular-profile sweep and copy the names.

            sweepDef.Path = null; // path comes from selection
            sweepDef.CircularProfile = true;
            sweepDef.CircularProfileDiameter = profileDia;

            // Typical options you may want:
            // sweepDef.Merge = true; // merge result with existing body when applicable
            // sweepDef.KeepNormalConstant = false;

            Feature sweepFeat = featMgr.CreateFeature(sweepDef) as Feature;
            if (sweepFeat == null)
                throw new Exception("Failed to create sweep feature. Record a macro and adjust sweep definition property names.");

            model.EditRebuild3();
        }
        catch (COMException comEx)
        {
            Console.WriteLine("COM error: " + comEx.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
