using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public class BangleCircularSweep
{
    public static void Main()
    {
        // Attach / launch SOLIDWORKS
        var swApp = Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")) as SldWorks;
        if (swApp == null) throw new Exception("Failed to start SOLIDWORKS.");
        swApp.Visible = true;

        // New part
        var model = (ModelDoc2)swApp.NewDocument(
            swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart),
            (int)swDwgPaperSizes_e.swDwgPaperA0size,
            0, 0);

        model = (ModelDoc2)swApp.ActiveDoc;
        if (model == null) throw new Exception("No active document.");

        var ext = model.Extension;
        var skMgr = model.SketchManager;
        var featMgr = model.FeatureManager;

        // -----------------------------
        // 1) Create PATH sketch: Ø30mm circle on Top Plane (R=15mm = 0.015m)
        // -----------------------------
        model.ClearSelection2(true);
        if (!ext.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
            throw new Exception("Failed to select Top Plane.");

        skMgr.InsertSketch(true);
        skMgr.CreateCircleByRadius(0, 0, 0, 0.015);
        skMgr.InsertSketch(true); // exit sketch

        // -----------------------------
        // 2) Create Swept Boss/Base using Circular Profile (no profile sketch)
        //    Set circular diameter directly (Ø10mm = 0.010m)
        // -----------------------------
        model.ClearSelection2(true);

        // For circular profile sweep, select the path using Mark = 4 :contentReference[oaicite:0]{index=0}
        bool selPath = ext.SelectByID2("Sketch1", "SKETCH", 0, 0, 0, false, 4, null, 0);
        if (!selPath)
        {
            DumpSelectionState(model);
            throw new Exception("Failed to select path sketch (Sketch1).");
        }

        // Create sweep feature definition and set circular profile options :contentReference[oaicite:1]{index=1}
        var swSweep = (SweepFeatureData)featMgr.CreateDefinition((int)swFeatureNameID_e.swFmSweep);
        if (swSweep == null) throw new Exception("CreateDefinition(swFmSweep) returned null.");

        // Circular profile (no separate profile sketch)
        swSweep.CircularProfile = true;
        swSweep.CircularProfileDiameter = 0.010; // meters (Ø10mm)

        // Create the feature
        Feature feat = featMgr.CreateFeature(swSweep);
        if (feat == null)
        {
            DumpSelectionState(model);
            throw new Exception("Failed to create circular-profile sweep feature.");
        }

        model.ViewZoomtofit2();
    }

    static void DumpSelectionState(ModelDoc2 model)
    {
        try
        {
            var selMgr = (SelectionMgr)model.SelectionManager;
            int count = 0;
            try { count = selMgr.GetSelectedObjectCount2(-1); }
            catch { try { count = selMgr.GetSelectedObjectCount(); } catch { count = 0; } }

            Console.WriteLine($"[SelectionDump] Selected count: {count}");
            for (int i = 1; i <= count; i++)
            {
                int type = selMgr.GetSelectedObjectType3(i, -1);
                int mark = 0;
                try { mark = selMgr.GetSelectedObjectMark(i); } catch { }
                object obj = null;
                try { obj = selMgr.GetSelectedObject6(i, -1); }
                catch { try { obj = selMgr.GetSelectedObject(i); } catch { obj = null; } }

                Console.WriteLine($"  [{i}] Type={type}, Mark={mark}, Obj={(obj == null ? "(null)" : obj.ToString())}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DumpSelectionState failed: " + ex.Message);
        }
    }
}
