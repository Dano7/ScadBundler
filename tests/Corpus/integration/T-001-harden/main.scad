// T-001 — hardening differential fixture.
// Minifying or obfuscating this bundle must render byte-identical CSG, emit identical ECHO, and add
// no new warnings (exercises: prologue params incl. a string, an echo'd string, an included private
// constant, a namespaced `use` library, an unused/tree-shakeable module, and a dynamically-scoped
// `$`-special-variable default that tree-shaking must NOT drop — see inc.scad).
// Also exercises Customizer trivia (group marker, description, inline annotation) that must survive a
// comment-stripping emit and still be valid OpenSCAD (the header above is hoisted away from the param).
/* [Dimensions] */
// Wall thickness in mm
wall = 2;          // [1:5]
size = 10;
part_name = "widget";
include <inc.scad>
use <used.scad>

module box(d) { cube([d, d, wall]); }
module unused_helper(n) { sphere(r = n); }   // never called -> tree-shaken away

echo("name:", part_name);
box(size);
translate([0, 0, wall]) tube(size, wall);
translate([size * 2, 0, 0]) scaled_block(size);
translate([0, size * 2, 0]) ribbed(size);    // reads the $ribs special-variable default via dynamic scope

// Long-line content (ADR 0003): a data table and a packed module body that each minify to > 256
// characters, so the hardened emit's default --max-line-length wrapping demonstrably triggers and the
// differential proves the wrapped text still renders byte-identical CSG. (This assignment sits below
// the first '{', so it is not a Customizer parameter — in the original or the bundle.)
profile = [[0, 0], [40, 0], [40, 3], [38, 3], [36, 5], [34, 3], [32, 5], [30, 3], [28, 5], [26, 3],
           [24, 5], [22, 3], [20, 5], [18, 3], [16, 5], [14, 3], [12, 5], [10, 3], [8, 5], [6, 3],
           [4, 5], [2, 3], [1, 6], [3, 8], [5, 6], [7, 8], [9, 6], [11, 8], [13, 6], [15, 8],
           [17, 6], [19, 8], [21, 6], [23, 8], [25, 6], [27, 8], [29, 6], [31, 8], [33, 6], [0, 9]];

module ridge_plate(w) {
    linear_extrude(height = wall) polygon(points = profile);
    translate([0, 12, 0]) cube([w, 1, 1]);
    translate([0, 14, 0]) cube([w, 1, 2]);
    translate([0, 16, 0]) cube([w, 1, 3]);
    translate([0, 18, 0]) cube([w, 1, 4]);
    translate([0, 20, 0]) cube([w, 1, 5]);
    translate([0, 22, 0]) cube([w, 1, 6]);
    translate([0, 24, 0]) cube([w, 1, 7]);
}

translate([0, -40, 0]) ridge_plate(size * 3);
