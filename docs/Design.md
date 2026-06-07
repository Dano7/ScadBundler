# ScadBundler Design Document

## High-Level Architecture
ScadBundler follows a classic compiler pipeline:

1. **Source Loader** — Recursively resolve files via `include`/`use`, search paths, cycle detection.
2. **Lexer** — Hand-written token scanner with precise source locations.
3. **Parser** — Recursive descent parser building immutable AST.
4. **Semantic Analyzer** — Symbol table construction, scope resolution, collision detection.
5. **Inliner / Transformer** — Flatten dependencies, deduplicate modules/functions, handle renaming.
6. **Emitter** — Pretty-printer with configurable style (indentation, line length, brace placement).

## Key Design Decisions
- **AST**: Rich, typed record hierarchy optimized for transformations (Visitor pattern).
- **Deduplication**: Content + signature hashing with optional namespace prefixing.
- **Customizer Support**: Special handling for `/* [ ... ] */` comments and parameters.
- **Error Handling**: Rich diagnostics with recovery where safe.
- **Performance**: Zero-allocation lexer paths where possible, caching for repeated libraries.

## Extensibility
- Visitor-based transforms for future features (minification, dead-code elimination).
- Plugin model (post-v1).
- **Web Support**: Core library will be designed for easy consumption in web contexts (WASM via .NET, or clean JSON API) to power the independent "ScadBundler Live" web application.

## Challenges & Mitigations
- Module name collisions → Configurable resolution strategies.
- Global side effects → Preserve declaration order.
- Large libraries (BOSL2) → Streaming/emission with low memory footprint.
