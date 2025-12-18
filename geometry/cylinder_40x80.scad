// Cylinder: 40 mm diameter, 80 mm height
// $fn controls the facet count for smoothness when exporting to STL
$fn = 128;

// Create cylinder (OpenSCAD uses millimeters by default)
// d = diameter, h = height
cylinder(h = 80, d = 40, center = false);