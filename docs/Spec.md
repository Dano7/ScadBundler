# ScadBundler Software Specification

## Functional Requirements
- Parse any valid OpenSCAD file matching official behavior.
- Produce single-file output that renders identically in OpenSCAD.
- Support major libraries: BOSL2, NopSCADlib, dotSCAD, etc.
- Preserve formatting intent and Customizer compatibility.
- Handle `include` vs `use` semantics correctly.
- Aggregate licenses and provide transformation summary.

## Non-Functional Requirements
- Performance: <2 seconds for typical projects.
- Reliability: 100% syntactically valid output.
- Test Coverage: ≥95%.
- Dependencies: Minimal (no ANTLR, no runtime C++ interop).

## Testing Strategy
- Unit tests for lexer, parser rules, semantic passes, emitter.
- Golden-master tests on real-world projects.
- Integration tests (test-only harness) comparing against official OpenSCAD.

## Out of Scope (v1)
- Full semantic type checking beyond collision detection.
- Code formatting beyond pretty-print basics.
- GUI (handled by separate "ScadBundler Live" project).
