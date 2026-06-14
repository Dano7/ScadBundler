// included: shares scope with the root; tube() reads the private constant FUDGE.
FUDGE = 1;
module tube(d, w) {
    difference() {
        cylinder(h = w, d = d);
        cylinder(h = w, d = d - w - FUDGE);
    }
}

// A `$`-special-variable default, read through DYNAMIC scope by ribbed()'s loop (never passed as a
// parameter). The static reference model can't see that edge — special-variable reads bind to no symbol —
// so tree-shaking must keep this default anyway; dropping it makes `$ribs` undef and changes the geometry,
// which the byte-identical-CSG differential catches. Regression for the BOSL2 `$tags_shown`/`$transform`
// minify/obfuscate breakage.
$ribs = 6;
module ribbed(d) {
    for (i = [0:$ribs - 1])
        rotate([0, 0, i * 360 / $ribs]) translate([d / 2, 0, 0]) cube([1, 1, wall]);
}
