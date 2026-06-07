# ScadBundler Constitution

This document defines the non-negotiable principles for the ScadBundler project.

## Core Values
- **No half-measures**: We build a robust, production-grade tool, not another naïve concatenator.
- **Lean, clean, and simple**: No over-engineering. Solve the real problem well; resist scope creep and unnecessary abstraction.
- **Idiomatic, modern C#**: Target .NET 10.0 (latest LTS). Use records, pattern matching, source generators where beneficial, minimal dependencies.
- **High Code Quality**: Clean architecture, domain-driven design for the AST, strict static analysis (Roslyn analyzers, EditorConfig), no warnings.
- **High Test Coverage**: ≥95% line coverage. Unit tests for every parser rule, semantic pass, and emitter edge case. Integration tests (non-shipped) validate against official OpenSCAD.
- **True Compiler Pipeline**: Lexer → Parser → AST → Semantic Analysis → Symbol Resolution → Inlining/Deduplication → Pretty Emitter. No regex/text hacks in core path.
- **Correctness First**: Output must be semantically equivalent and syntactically valid OpenSCAD. Preserve Customizer comments, licenses, formatting intent.
- **Parser Decision**: Hand-written recursive descent parser (with precedence climbing for expressions). No ANTLR4 or other generator tools in the main codebase.
- **Documentation is a first-class deliverable**: Design docs, specs, and slice plans are project outputs alongside code — not afterthoughts.

## Technical Constraints
- **Language**: C# 13+ on .NET 10.0.
- **No Runtime Interop**: Official OpenSCAD C++ parser may be used *only* for integration testing harnesses (separate project or test-only).
- **Parser Strategy**: Hand-written recursive descent. Prefer full control, debuggability, and performance.
- **Output**: Single, clean, well-formatted `.scad` file with aggregated headers.

## Development Standards
- Git flow with conventional commits.
- PRs require passing tests + review.
- Documentation-driven: Every major feature has design docs.
- Community-friendly: MIT license, excellent README, CLI + library mode.
- **Web Companion**: Designed to enable a separate, independent project "ScadBundler Live" — a clean web UI for less technical users.
- **AI-assisted implementation**: Each development slice is intended to be implementable in a single AI coding session. Slice documentation must be precise enough — with acceptance criteria, AST definitions, grammar rules, and test cases — that a cold AI assistant can implement and self-verify the milestone with minimal back-and-forth. This also serves a secondary goal: comparing results across different AI assistants.

This constitution is immutable without unanimous maintainer consensus.