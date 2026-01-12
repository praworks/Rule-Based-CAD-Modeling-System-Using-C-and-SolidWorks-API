using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public class BangleSweep
{
    public static void Main()
    {
        SldWorks swApp = Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")) as SldWorks;
        swApp.Visible = true;

        // New part
        ModelDoc2 model = (ModelDoc2)swApp.NewDocument(
            swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart),
            (int)swDwgPaperSizes_e.swDwgPaperA0size,
            0, 0);

        model = (ModelDoc2)swApp.ActiveDoc;
        var featMgr = model.FeatureManager;
        var skMgr = model.SketchManager;
        var ext = model.Extension;

        // ---------- 1) Create PATH sketch: Ø30mm circle on Top Plane ----------
        // Select Top Plane
        ext.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0);
        skMgr.InsertSketch(true);

        // Circle by radius (meters): radius 15mm = 0.015m
        skMgr.CreateCircleByRadius(0, 0, 0, 0.015);

        skMgr.InsertSketch(true); // exit sketch

        // Get the path sketch feature for later use
        Feature pathSketch = (Feature)model.FeatureByPositionReverse(0); // last feature (the sketch)

        // ---------- 2) Create PROFILE plane perpendicular to curve at a point ----------
        // Select path sketch (commonly "Sketch1" right after creation)
        ext.SelectByID2("Sketch1", "SKETCH", 0, 0, 0, false, 0, null, 0);
        // Create plane normal to curve at start point (plane creation shown in SW API patterns)
        // In many cases, simpler: use "CreatePlanePerCurveAndPassPoint3" as in SW API workflows.
        // Note: this uses current selection context; ensure a curve+point is selected in a more robust version.
        // open the path sketch (Sketch1), create a point on the circle perimeter
        ext.SelectByID2("Sketch1", "SKETCH", 0,0,0, false, 0, null, 0);
        skMgr.InsertSketch(true);            // enter sketch
        SketchPoint skPoint = (SketchPoint)skMgr.CreatePoint(0.015, 0, 0); // place a sketch point at +X (15mm)
        skMgr.InsertSketch(true);            // exit sketch

        // Now select the actual sketch segment and the sketch point objects.
        // Get the sketch's segments (returns object[] of SketchSegment)
        var swSketch = (Sketch)pathSketch.GetSpecificFeature2(); // may need to cast appropriately
        object[] segs = (object[])swSketch.GetSketchSegments();
        if (segs == null || segs.Length == 0) throw new Exception("No sketch segments found.");

        SketchSegment seg = (SketchSegment)segs[0];

        // Clear previous selections then select the curve segment and the point
        model.ClearSelection2(true);
        bool selSeg = seg.Select4(false, null);           // select the curve segment
        bool selPt  = skPoint.Select4(true, null);        // add the point to the selection

        if (!selSeg || !selPt)
        {
            DumpSelectionState(model);
            throw new Exception("Failed to select segment or point before creating plane.");
        }

        Entity planeEnt = (Entity)model.CreatePlanePerCurveAndPassPoint3(true, true);
        if (planeEnt == null) { DumpSelectionState(model); throw new Exception("plane creation failed"); }
        planeEnt.Select4(false, null);

        // ---------- 3) Create PROFILE sketch: Ø10mm circle on that plane ----------
        planeEnt.Select4(false, null);
        skMgr.InsertSketch(true);

        // Profile radius 5mm = 0.005m
        skMgr.CreateCircleByRadius(0, 0, 0, 0.005);

        skMgr.InsertSketch(true); // exit sketch

        // ---------- 4) Sweep (select profile + path then create sweep boss) ----------
        model.ClearSelection2(true);

        // Typical names after above: profile sketch might be "Sketch2", path sketch "Sketch1"
        bool sel1 = ext.SelectByID2("Sketch2", "SKETCH", 0, 0, 0, false, 1, null, 0); // profile
        bool sel2 = ext.SelectByID2("Sketch1", "SKETCH", 0, 0, 0, true,  4, null, 0); // path

        if (!sel1 || !sel2)
            throw new Exception("Failed to select profile/path sketches. Use traversal-based selection for robustness.");

        // Dump selection manager state for diagnostics
        DumpSelectionState(model);

        // Create swept boss/base using selected profile and path
        // InsertProtrusionSwept3 inserts a swept boss/base from selected entities.
        featMgr.InsertProtrusionSwept3(
            false, false,
            (int)swTwistControlType_e.swTwistControlFollowPath,
            false, false,
            0, 0,
            false, // IsThinBody
            0.0,   // Thickness1
            0.0,   // Thickness2
            0,     // ThinType
            0,     // PathAlign
            false, // Merge
            false, // UseFeatScope
            false, // UseAutoSelect
            0.0,   // TwistAngle
            false  // BMergeSmoothFaces
        );

        model.ViewZoomtofit2();
    }

    static void DumpSelectionState(ModelDoc2 model)
    {
        try
        {
            var selMgr = (SelectionMgr)model.SelectionManager;
            int count = 0;
            try { count = selMgr.GetSelectedObjectCount2(-1); } catch { try { count = selMgr.GetSelectedObjectCount(); } catch { count = 0; } }
            Console.WriteLine($"[SelectionDump] Selected count: {count}");
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    int type = selMgr.GetSelectedObjectType3(i, -1);
                    int mark = 0;
                    try { mark = selMgr.GetSelectedObjectMark(i); } catch { }
                    object obj = null;
                    try { obj = selMgr.GetSelectedObject6(i, -1); } catch { try { obj = selMgr.GetSelectedObject(i); } catch { obj = null; } }
                    string oname = (obj == null) ? "(null)" : obj.ToString();
                    Console.WriteLine($"  [{i}] Type={type}, Mark={mark}, Obj={oname}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{i}] (failed to query): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DumpSelectionState failed: " + ex.Message);
        }
    }
}
