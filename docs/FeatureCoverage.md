Feature Coverage Matrix (SolidWorks vs Program)

Sketching

| Feature | Implemented | Handler(s) | Notes |
| --- | --- | --- | --- |
| Sketch Begin/End | Yes | `Sketching.SketchBeginHandler`, `Sketching.SketchEndHandler` | Toggles sketch mode |
| Line | Yes | `Sketching.LineHandler` | Basic segment |
| Center Rectangle | Yes | `Sketching.RectangleCenterHandler` | By center, width/height |
| Circle (by center) | Yes | `Sketching.CircleCenterHandler` | Radius/diameter |
| Arc (center, start/end) | Yes | `Sketching.ArcHandler` | Uses center + angles |
| Dimension | Yes | `Sketching.DimensionHandler` | Anchors two dims; supports auto-dimension |
| Auto/Fully Define Sketch | Yes | `Sketching.DimensionHandler` (auto) | Invokes `FullyDefineSketch` |
| Constraints/Relations | No | — | Horizontal/Vertical/Perpendicular etc. |
| Ellipse/Spline/Slot/Polygon | No | — | Not yet supported |
| Centerline/Text | No | — | Not yet supported |

Part Features

| Feature | Implemented | Handler(s) | Notes |
| --- | --- | --- | --- |
| Extruded Boss/Base | Yes | `PartFeatures.ExtrudeBossHandler` | Blind depth |
| Extruded Cut | Yes | `PartFeatures.ExtrudeCutHandler` | Robust selection + `FeatureCut4` |
| Revolved Boss/Cut | Yes | `PartFeatures.RevolveHandler` | Reflection fallbacks; needs profile + axis |
| Sweep (Boss) | Yes | `PartFeatures.SweepHandler` | Profile then path; twist angle optional |
| Sweep Cut | No | — | Could extend sweep handler or add separate |
| Loft (Boss/Cut) | No | `PartFeatures.LoftHandler` | Stubbed, not implemented |
| Fillet | Yes | `PartFeatures.FilletHandler` | Batch fillet with fallbacks |
| Chamfer | Yes | `PartFeatures.ChamferHandler` | Distance-angle (45°) via InsertFeatureChamfer |
| Hole (simple cut) | Yes | `PartFeatures.HoleHandler` | Sketch circle + cut; not Hole Wizard |
| Hole Wizard | No | — | Needed for standard hole types |
| Pattern (Linear/Circular) | No | — | Needed for hole arrays/bolt circles |
| Mirror Feature/Body | No | — | Not implemented |
| Shell | No | — | Not implemented |
| Draft | No | — | Not implemented |
| Rib/Dome/Wrap/Boundary | No | — | Not implemented |
| Combine/Split/Move-Copy/Scale | No | — | Not implemented |
| Thicken/Delete/Replace Face | No | — | Not implemented |
| Pocket | No | `PartFeatures.PocketHandler` | Stubbed, not implemented |

Sheet Metal

| Feature | Implemented | Handler(s) | Notes |
| --- | --- | --- | --- |
| Base Flange/Tab | No | — | K-factor, thickness, reliefs |
| Edge Flange | No | — | Bend radius/angle, relief |
| Miter Flange/Hem/Jog | No | — | Not implemented |
| Sketched Bend | No | — | Not implemented |
| Convert to Sheet Metal | No | — | Not implemented |
| Gusset/Forming Tools | No | — | Not implemented |
| Flat Pattern | No | — | Not implemented |

Threads & Springs

| Feature | Implemented | Handler(s) | Notes |
| --- | --- | --- | --- |
| Helix/Spiral | No | — | Required for modeled threads/springs |
| Cosmetic Thread | No | — | For performance on fasteners |
| Thread Cut (Sweep-Cut) | No | — | Could re-use sweep with thread profile |

Utilities & Selection

| Feature | Implemented | Handler(s) | Notes |
| --- | --- | --- | --- |
| New Part | Yes | `Utilities.NewPartHandler` | Auto-created if omitted |
| Select Plane | Yes | `Utilities.SelectPlaneHandler` | Named planes |
| Select Face | Yes | `PartFeatures.FaceHandler` | Robust selection and diagnostics |
| Set Units/Material | Yes | `Utilities.SetUnitsHandler`, `Utilities.SetMaterialHandler` | Units and material |
| Zoom to Fit/Description | Yes | `Utilities.ZoomToFitHandler`, `Utilities.DescriptionHandler` | Quality-of-life |
| Model Inspect | Yes | `Utilities.ModelInspectHandler` | Captures model state for LLM context |
| Plan From Intent | Yes | `Utilities.PlanFromIntentHandler` | LLM generates next steps from intent + model facts |

Planned Handlers (to reach parity for plan items)

- Sheet Metal: BaseFlange, EdgeFlange (K‑factor, reliefs), Flatten
- Threads: Helix, CosmeticThread, optional Sweep‑Cut thread
- Hole Wizard + Patterns: standard holes, linear/circular patterns, Smart Component hooks
