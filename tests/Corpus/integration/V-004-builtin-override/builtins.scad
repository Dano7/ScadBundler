// V-004 builtins: thin wrappers that capture OpenSCAD builtins behind a `use` boundary, so an
// overriding module can still reach the original (BOSL2's builtins.scad pattern). `use`d, never
// included, so inside this file `translate` is always the builtin.
module _translate(v) translate(v) children();
