Sketching priority list (user focus)

1. L-Brackets and Gussets
	- Create with sheet metal flange so flat pattern honors K-factor and bend allowance.
	- Ensure parameters capture thickness, leg lengths, bend radius, and relief type.
	- Operation logic: Base Flange tab; add Edge Flanges per leg; set relief type; optional cut-extrude holes.

2. Hex Bolts and Machine Screws
	- Use helical curves for threads when custom; otherwise reference toolbox seeds.
	- Expose length, major diameter, pitch, head style, and drive type.
	- Operation logic: Revolve shank + head; Helix + Sweep-Cut for modeled threads or Cosmetic Thread; configs for lengths.

3. Hex Nuts and Flange Nuts
	- Model with cosmetic threads for performance in large assemblies.
	- Capture size, thickness, across-flats, and flange diameter.
	- Operation logic: Extrude hex prism; add chamfers; tap/cosmetic thread; revolve flange lip if needed.

4. Flat and Spring Washers
	- Simple extrusions; drive OD/ID and thickness from bolt diameter.
	- Parameterize per stack-up distance needs.
	- Operation logic: Extrude ring; for spring washer use Helix + Sweep-Cut for split and torsion angle.

5. Mounting Plates
	- Plate with Hole Wizard patterns; enable Smart Component behavior.
	- Inputs: plate size, thickness, pattern type, and hardware associations.
	- Operation logic: Extrude base plate; Hole Wizard feature with pattern; add Smart Component references to bolts/nuts.

6. Bushings and Spacers
	- Revolve-based lathe parts; include material presets (porcelain/composite).
	- Control OD/ID/length and chamfers.
	- Operation logic: Revolve profile; add fillets/chamfers; assign material; optional key chamfer for press fit.

7. Shaft Keys (Parallel and Woodruff)
	- Sized to keyways; support limit mates to keep in slot during motion.
	- Parameters: width, height, length/arc, corner radius.
	- Operation logic: Extrude block (parallel) or Revolve segment (woodruff); add fillets; place mate reference midplane.

8. U-Bolts
	- Sweep over U-path with circular profile; size to pipe/busbar diameters.
	- Expose leg length, bend radius, thread length, and thread size.
	- Operation logic: Sketch U-path; Sweep circular profile; Cut-Extrude thread relief; cosmetic thread or Sweep-Cut ends.

9. Clevis Pins and Cotter Pins
	- Library parts with mate references for drag-and-drop alignment.
	- Define pin diameter, grip length, head style, and hole size for cotter.
	- Operation logic: Revolve pin body and head; Cut-Extrude cross-hole; add mate references on head face and axis.

10. Threaded Rods (Studs)
	 - Cylindrical with threaded appearance for lightweight models.
	 - Parameters: diameter, pitch, length, end treatments.
	 - Operation logic: Extrude cylinder; cosmetic thread over length; optional Sweep-Cut real thread for detailed configs.

Capability Matrix

| Item | Capable |
| --- | --- |
| 1. L‑Brackets and Gussets | No |
| 2. Hex Bolts and Machine Screws | No |
| 3. Hex Nuts and Flange Nuts | No |
| 4. Flat and Spring Washers | No |
| 5. Mounting Plates | No |
| 6. Bushings and Spacers | Yes |
| 7. Shaft Keys (Parallel/Woodruff) | Yes |
| 8. U‑Bolts | No |
| 9. Clevis Pins and Cotter Pins | Yes |
| 10. Threaded Rods (Studs) | Yes |

See full, ongoing coverage here: [docs/FeatureCoverage.md](docs/FeatureCoverage.md)
