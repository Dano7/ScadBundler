# ScadBundler

A robust, AST-based OpenSCAD file bundler/inliner for single-file platforms like Thingiverse.

**No half-measures.** Hand-written parser, high-quality C# implementation.

**Companion Project**: **ScadBundler Live** — a browser (Blazor WebAssembly) UI for drag-and-drop bundling, running the same Core pipeline locally (output is byte-identical to this CLI). **Try it: <https://dano7.github.io/ScadBundler/>**. Spec/design/slice docs: **[live/](live/)**.

## Quick Start

**No install — use the web version** in your browser: **<https://dano7.github.io/ScadBundler/>** (best for smaller projects; it runs entirely via WebAssembly, so large/library-heavy projects — think BOSL2-scale, a dozen+ files or several megabytes of source — will be noticeably slower there than on the CLI below, though it will still get the job done).

**Fast local tool** (recommended for large libraries — runs natively and reads your `OPENSCADPATH`):

- **Windows:** grab a portable build from the [latest release](https://github.com/Dano7/ScadBundler/releases/latest) (no .NET needed), or `winget install ScadBundler` once the winget package is published.
- **macOS / Linux:** download the portable build for your platform from the [latest release](https://github.com/Dano7/ScadBundler/releases/latest).
- **Already have .NET?** `dotnet tool install --global ScadBundler`

```bash
scadbundler bundle myproject.scad -o bundled.scad
```

Per-platform details (and the one-time Windows SmartScreen note): **[Install.md](Install.md)**.

## Documentation

Start with **[Design.md](Design.md)** — architecture overview + the full document map. Key references: [Constitution.md](Constitution.md) (principles), [Spec.md](Spec.md) (semantics), [AST-Reference.md](AST-Reference.md), [Parser-Planning.md](Parser-Planning.md) (precedence), [Diagnostics.md](Diagnostics.md), [Builtins-Reference.md](Builtins-Reference.md), [Test-Corpus.md](Test-Corpus.md), [UX.md](UX.md). Implementation plan + per-slice specs: [Development-Slices.md](Development-Slices.md) → [slices/](slices/). Real-world test findings & follow-ups: [Real-World-Validation.md](Real-World-Validation.md).

**ScadBundler Live** (web companion — live at <https://dano7.github.io/ScadBundler/>): [live/](live/) — [Spec](live/Spec.md), [Design](live/Design.md), [Development-Slices](live/Development-Slices.md) → [slices/](live/slices/).
