// V-004 — two real-world BOSL2 hazards in one bundle, both verified byte-identical against the
// official binary:
//   1. A use'd builtin wrapper (builtins.scad's `_translate`, where `translate` = the builtin) is
//      overridden by an include'd module (transforms.scad's `module translate`). Naive flattening
//      drops both into one scope, so the wrapper's bare `translate` binds to the override → infinite
//      recursion. The inliner frees the builtin name by renaming the include-origin override.
//   2. A self-recursive anonymous function literal in a let-binding. A function literal is a closure
//      resolved at call time, so its own name is in scope inside its body; the analyzer must not warn.
include <transforms.scad>

// f(4) = 1 + 1 + 1 + 1 + f(0) = 4 — exercises the recursive literal; count drives the geometry below.
count = let(f = function(n) n <= 0 ? 0 : 1 + f(n - 1)) f(4);

translate([count, 0, 0]) cube(1);
