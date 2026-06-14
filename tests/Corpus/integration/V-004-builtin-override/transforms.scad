// V-004 transforms: OVERRIDES the builtin `translate`, reaching the original via the use'd wrapper
// (BOSL2's transforms.scad pattern). When flattened, the bundle must keep the wrapper's builtin
// `translate` distinct from this override, or the two recurse forever.
use <builtins.scad>

module translate(v) { _translate(v) children(); }
