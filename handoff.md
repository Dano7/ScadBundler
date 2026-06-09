# Handoff — Start Here (Slice 6: Emitter & CLI)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1 (lexer), 2 (AST + parser), 3 (comprehensions + functional exprs), 4 (semantic analysis), and 5 (loader + inliner) are complete and committed.** Your job this session is **Slice 6 — the `Emitter` + CLI**: a deterministic pretty-printer that renders the bundled `ScadFile` to valid OpenSCAD (preserving comments/Customizer/license trivia, precedence-correct parens, `--minify`), plus the `scadbundler` CLI that runs the whole pipeline and packs as a global tool. **This completes the pipeline** `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter`.

---

## 🔴 DO THIS FIRST: cold code review of Slice 5

**Before writing any Slice 6 code, do a fresh, critical review of the Slice 5 implementation.** You did not write it this session — read it as a reviewer would. Slice 6 consumes the bundled `ScadFile` and makes the `B-*` reference outputs **exact goldens**, so any inliner shape bug surfaces in your goldens. Review goals:

- **The inliner.** `src/ScadBundler.Core/Inlining/Inliner.cs`: the six phases in `Run.Execute` — Phase A `FlattenIncludes` (document order, `use`/font hoisted out, defensive splice-stack guard), `DiscoverUses` (root include-closure → used-file closure, font dedup), Phase B `GatherUseImports` (each used file + its include-closure contributes all module/function defs + `PrivateConstants`, in declaration order), Phases C/D `ResolveCollisions`/`ResolveGroup`/`Deduplicate` (per-`(kind,name)` group: structural dedup of modules/functions SB5005; identity-only dedup of variable diamonds; then `Auto`/`Prefix`/`KeepFirst`/`KeepLast`/`Error`), `NamespaceRep` (stem sanitize + `UniqueName` suffixing + reference rewrite via `ISemanticModel.ReferencesTo`), and Phase F `Assemble` (`winnerNodes`/`emitted` gate; fonts → use-imports → include-flattened root, each `BundleRewriter`-rewritten).
- **The rewriter.** `BundleRewriter.cs`: one immutable-rebuild pass applying renames (keyed by **original-node identity**, AST-Reference §15.6) and normalization (`assign`→`let` SB5001, `child`→`children` SB5002, deprecated built-ins preserved SB5003). Synthetic nodes reuse origin spans/trivia.
- **The structural key.** `StructuralKey.cs`: the span/trivia-free content key driving dedup. Confirm it distinguishes what should differ (bodies, number `RawText`) and ignores what shouldn't (span, trivia, blank lines).
- **The loader.** `Loading/SourceLoader.cs`: search-path order, cache-by-absolute-path (diamonds load once), post-order caching + active-stack cycle detection (SB4002), font pass-through, never-throw (`IFileSystem` seam; `DiskFileSystem` prod, `InMemoryFileSystem`/`FaultyFileSystem` in tests).
- **Tests.** `Inlining/Slice5BundleTests` (B-001..B-007 + dedup/strategies/font), `Slice5CorpusTests` (disk goldens), `Slice5EdgeCoverageTests`, `StructuralKeyTests`, `BundleRewriterTests`, `BundlerTests`, `Loading/SourceLoaderTests`; helpers `BundleHelper`, `InMemoryFileSystem`, `FaultyFileSystem`, `RichScad`.

Optional: run `/code-review` on the last commit. Record anything non-trivial, then proceed.

---

## Current state

- **Slices 1–5 done:** `dotnet build` zero-warning (warnings-as-errors), `dotnet test` green (**380 tests**). Coverage: `Lexing/`≈98%, `Parsing/`≈99%, `Semantics/` 100%, **`Loading/`≈98.8%, `Inlining/`≈99.6%**.
- Branch is **`Claude_implementation`**. Last feature commit: `feat(inliner): implement Slice 5 …`.
- `src/ScadBundler.Core/`: `Text/`, `Trivia/`, `Diagnostics/`, `Lexing/`, `Ast/`, `Parsing/`, `Semantics/`, **`Loading/`** (`SourceLoader` + `IFileSystem`/`DiskFileSystem` + the `LoadGraph` seam), **`Inlining/`** (`Bundler`, `Inliner`, `BundleRewriter`, `StructuralKey`, `BundleOptions`, `BundleResult`). `Emitting/` and the `src/ScadBundler` CLI do **not** exist yet.
- **Entry point:** `Bundler.Bundle(rootPath, options)` (disk + `OPENSCADPATH`) and `Bundler.Bundle(rootPath, options, IFileSystem)` (test seam) → `BundleResult(ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics)`. **Slice 6 renders `Bundled` to text.**
- **InternalsVisibleTo** for `ScadBundler.Core.Tests` is now set (Slice 5 added it to white-box-test `StructuralKey`/`BundleRewriter`).
- **Diagnostics:** SB1001–SB5005 are in `DiagnosticCode.cs` and `docs/Diagnostics.md`. **SB6001 is catalogued in the doc but NOT yet in `DiagnosticCode.cs`** — add it there (with XML docs) before use.

## What to read, in order

1. **`docs/slices/Slice-6-Emitter-CLI.md`** — your primary spec (the `Emitter` API §4, default formatting §5 that **locks the goldens**, design notes §6, the CLI §7, `EM-001`/`EM-002`).
2. **`docs/AST-Reference.md`** — node shapes, trivia model (§3, §7), `RawText` (§15.9), the Customizer example (§14.7 → `EM-001`).
3. **`docs/Parser-Planning.md`** — the precedence/associativity table (the **same** one the parser uses) for minimal-parenthesization of synthesized subtrees.
4. **`docs/UX.md`** — the CLI surface (`scadbundler bundle`, options, exit codes).
5. **`docs/Test-Corpus.md`** — `EM-001`/`EM-002`; this slice turns the `B-*` reference outputs into **exact `expected.scad` goldens** (the `slice5-bundle/` fixtures already hold the sources; add `expected.scad`).
6. **`docs/Diagnostics.md`** — add **SB6001** to `DiagnosticCode.cs` before using it.

## Slice 6 seam (inliner → emitter → CLI)

- **`Emitter.Emit(ScadFile, EmitOptions?)` → `string`** — deterministic visitor over the AST. Numbers/strings via `RawText`; author `ParenthesizedExpression` preserved; **insert minimal parens** around any child whose precedence is lower than its parent's (or equal on the associativity-sensitive side) so re-parse is identical (needed for synthesized rename/normalize nodes). Leading `CommentTrivia` on its own line at the node's indent; trailing same-line trivia after two spaces; `BlankLineBefore` → one blank line. **Self-check (debug/tests):** re-parse the output and assert structural round-trip; failure ⇒ **SB6001**.
- **The bundled AST carries provenance, not layout.** Inlined nodes keep their **origin file's `SourceSpan`** (so diagnostics still point at real sources) — do **not** drive layout off spans; regenerate everything from node structure + trivia + `BlankLineBefore`. The root `ScadFile.Source` is the root file's.
- **CLI** (`src/ScadBundler`): parse args → `BundleOptions` + `EmitOptions` → `Bundler.Bundle` → print diagnostics (severity-grouped, source-ordered) → `Emitter.Emit` → write to `-o`/stdout. Exit `0`/`1` (any Error diagnostic)/`2` (bad args). Pack as `dotnet tool` (`scadbundler`).

## Watch items inherited from Slice 5 (read before trusting edge cases)

- **Latent Slice 4 bug (cross-`include` last-wins resolution).** `SemanticAnalyzer.LookupOwnOrIncluded` returns the **first** matching `include`-merged scope, but OpenSCAD flat-scope is **last**-wins. Slice 5's **`Auto`** path is insulated (it resolves include duplicates *structurally* by document order, never via `Resolve`), so `B-007` and the default pipeline are correct. But `--on-collision prefix|keep-first|keep-last` rewrite cross-`include`-duplicate **references** via `ISemanticModel.ReferencesTo`, which can bind a call to the *earlier* duplicate. The emitter doesn't touch this, but if you exercise non-default strategies on include collisions, expect a possible mis-bind. A fix (resolve include-merge references against the inliner's document-order flatten) is filed as a separate task.
- **Bundle diagnostics include the file.** The `B-*` golden format is `SBnnnn <SEV> <file>:<line>:<col> <message>` (paths relative to the case dir); see `Slice5CorpusTests` and Test-Corpus §4. Keep this when you add CLI diagnostic rendering.
- **`Error` strategy + transitive `use`.** `CollisionStrategy.Error` currently emits the same collision **warnings** as `Auto` and returns an **empty** `ScadFile` (no dedicated error-severity code was catalogued). Transitive `use` import is a deliberate **superset** (every reachable used file contributes *all* its defs, not only referenced ones — matches `B-006`, may over-import for deep library chains; a V2 tree-shake). Neither blocks Slice 6.
- **Font `use` survives as a `UseStatement`** in the bundle (can't inline a binary font) — the emitter must render it (`use <Arial.ttf>`).

## Conventions carried over (so the build stays green)

- **Strict build:** `Directory.Build.props` sets net10.0, nullable, `TreatWarningsAsErrors`, analyzers `latest-Recommended`, `GenerateDocumentationFile`. Every **public** Core member needs XML docs (CS1591). Watch **CA1859** (private helpers returning one concrete type must declare it) and **CA1822** (mark non-instance helpers `static`) — both bit Slices 4 and 5 mid-build. xUnit analyzers reject `Assert.Single(coll.Where(...))` — use `Assert.Single(coll, predicate)`.
- **Immutable AST + reference-keyed side tables:** transforms build new nodes via `with`; side tables use `ReferenceEqualityComparer.Instance`. The emitter is read-only over the AST — no rewriting.
- **Golden corpus:** turn the `B-001`..`B-007` reference outputs into exact `tests/Corpus/slice5-bundle/<id>/expected.scad`, and add `tests/Corpus/slice6-emit/<id>/` cases. Reuse the corpus runner pattern (`Slice4CorpusTests`/`Slice5CorpusTests`) and `RichScad` for whole-tree formatting coverage.
- **Diagnostics:** add SB6001 to `DiagnosticCode.cs` (with XML docs) before use; messages must match `docs/Diagnostics.md` exactly.

## Non-negotiables (Constitution)

Hand-written passes — **no parser generators / ANTLR / regex** in the core path. **Deterministic, idempotent** emitter (`Emit(Parse(Emit(ast))) == Emit(ast)`). Output must be **semantically equivalent** to input (re-parse round-trips structurally; SB6001 guards it). **≥95% line coverage** of `Emitting/`; CLI covered by integration tests. No runtime interop with OpenSCAD's C++ (reference/fixtures only).

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~EmitterTests"   # once it exists
dotnet test --collect:"XPlat Code Coverage"
# after Slice 6:
# dotnet run --project src/ScadBundler -- bundle main.scad -o bundled.scad
# dotnet tool install --global ScadBundler
```

## Workflow / repo conventions

- Commits on `Claude_implementation`, **conventional commits**, ending with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Commit when a unit is done; don't push unless asked.
- `.gitattributes` forces **LF** everywhere.
- If you find a genuine spec gap/ambiguity, **fix the spec too** — this project's whole point is one-shot, spec-driven implementation. (Slice 5 did this: it pinned the multi-file bundle-diagnostic format `file:line:col` in Test-Corpus §4 and assigned `B-006`'s collision code to `SB5004`.)

## After Slice 6

Slice 6 is the last core slice — it closes the pipeline. Remaining post-v1 (see `docs/Development-Slices.md`): the WASM/JSON API and the "ScadBundler Live" web companion; real-world golden masters (BOSL2/NopSCADlib/dotSCAD); the integration harness (V1–V3) against official OpenSCAD.
