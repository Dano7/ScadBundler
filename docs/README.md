# ScadBundler

A robust, AST-based OpenSCAD file bundler/inliner for single-file platforms like Thingiverse.

**No half-measures.** Hand-written parser, high-quality C# implementation.

**Companion Project**: [ScadBundler Live](https://github.com/.../scad-bundler-live) — A user-friendly web interface for drag-and-drop bundling.

## Quick Start
```bash
dotnet tool install --global ScadBundler
scadbundler bundle myproject.scad -o bundled.scad
```

See [Constitution.md](Constitution.md), [Design.md](Design.md), [Spec.md](Spec.md), and [UX.md](UX.md) for project philosophy.
