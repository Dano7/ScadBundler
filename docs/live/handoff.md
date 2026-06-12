# Handoff — ScadBundler Live (web-impl status)

Running status of the **ScadBundler Live** web companion build (the docs in this folder; protocol in
[IMPLEMENTATION-KICKOFF.md](IMPLEMENTATION-KICKOFF.md)). One living file, updated per slice. A cold session
should be able to resume from here with no other context.

---

## ▶ Next session — start here

**W0 (Workspace facade) is done, green, and committed.** Build **W1 — Blazor shell + bundle MVP**
([slices/Slice-W1-Blazor-Shell.md](slices/Slice-W1-Blazor-Shell.md)). The keystone logic is finished and
covered; W1 is a thin shell over it. Build order remaining: **W1 → W2 → W3** (W4 deferred — do not build).

First things for W1:
- Add `web/ScadBundler.Web` (Blazor WASM, .NET 10) **and** `tests/ScadBundler.Web.Tests` (bUnit) to
  `ScadBundler.sln` (they are **not** wired yet).
- Drive everything through the facade: `ProjectAnalyzer.Analyze(uploads, root?)` → gate on
  `Missing` **and** `Ambiguous` both empty → `WebBundler.Bundle(fs, root, opts)` (Design §3.2/§3.3).
- The optional stateful **`Workspace` aggregator** (Spec §5.6 / Slice-W0 deliverables) was **deferred to
  W1** (the slice permits this) — add it in `Workspace/` if the `WorkspaceController` wants to wrap it;
  it is pure sugar over `Analyze`/`Bundle`, not required for coverage.
- **Manual acceptance** target: `C:\git\dan\SCAD\ForkedHolder.scad` + its libs. Note its `include
  <forkedholderlib.scad>` is lower-case while the file on disk is `ForkedHolderLib.scad`; the analyzer's
  **case-insensitive basename matching** (below) handles this, so a loose-file/zip upload still resolves.

> Ask the user before deciding the **W3 hosting target** and before adding **any** dependency to
> `ScadBundler.Core`.

---

## Slice W0 — done (2026-06-12)

The browser-free **Core/Workspace facade** — the "WASM/JSON API" the roadmap promised. All logic
(entry-point inference, dependency/missing report, layout inference, bundling) lives here, covered to the
Constitution bar with **byte-identical CLI parity**. No new compiler logic; no new `SBxxxx` codes; Core
stays dependency-free.

### Files added (`src/ScadBundler.Core/Workspace/`)
- `UploadedFile.cs`, `ReferenceOrigin.cs`, `DiagnosticDto.cs`, `DependencyModels.cs`
  (`DependencyNode`/`DependencyTree`/`MissingReference`/`AmbiguousReference`), `ProjectAnalysis.cs`,
  `WebBundleOptions.cs`, `BundleStats.cs`, `WebBundleResult.cs` — the plain, JSON-serializable DTOs.
- `InMemoryFileSystem.cs` — `IFileSystem` over a virtual `/`-rooted tree; **dumb / exact-path / Ordinal**
  (`AddFile`/`RemoveFile`/`Contains`/`Files`). All smart resolution lives in the analyzer.
- `ProjectAnalyzer.cs` — `Analyze(uploads, explicitRoot?)`: layout (basename) inference, entry-point
  inference, dependency tree, missing/ambiguous, SB4001-filtered diagnostics. Never throws.
- `WebBundler.cs` — `Bundle(fs, root, options)`: mirrors `BundleCommand`'s option mapping, runs
  `Bundler.Bundle(root, opts, fs)` (**IFileSystem overload**) + `Emitter.Emit`, projects diagnostics +
  stats. Error-gates `Text=""`/`Ok=false`.

### Tests added (`tests/ScadBundler.Core.Tests/Workspace/`)
`InMemoryFileSystemTests` · `ProjectAnalyzerTests` · `WebBundlerTests` · `BundleParityTests`
(disk-fixture parity across Normal/Minify/Obfuscate + no-license/strip).

### Quality
- **Build: 0 warnings.** **Tests: 748 green** (Core **691** [+61], CLI 23, Integration 34) — baseline was 687.
- **Coverage on `Workspace/`: 98.99% line** (≥95% bar). The few uncovered lines are defensive guards
  (`ReachableCount` no-edge `continue`, `ResolveRef` empty-path, a `foreach` brace in `WebBundler`).
- **Bundle parity proven byte-identical** to the same `Bundler`+`Emitter` over a real temp-dir disk
  fixture, across all three profiles (`BundleParityTests`).

### Key decisions / deviations (and the spec edits that record them)
1. **Basename matching is case-insensitive (`OrdinalIgnoreCase`)** in the analyzer — makers reference with
   sloppy case (ForkedHolder). The alias is still placed at the *exact* loader-resolved path, so the
   bundle resolves precisely what the analysis predicted; `InMemoryFileSystem` itself stays exact/Ordinal.
   → **Spec §6.3 updated.**
2. **Absolute references** can't be satisfied by basename placement (the alias would need an absolute
   home) → reported in `Missing` rather than silently dropped. `ClassifyUnresolved` now sends any
   unresolved-reaching-classification with <2 candidates to `Missing`. → **Spec §6.3 updated.**
3. Aliases are placed at `Combine(includerDir, rawPath)` (canonicalized) — the general form of the spec's
   `"/proj/" + rawPath`, identical when the includer sits at the project root. → **Spec §6.3 clarified.**
4. **Path identity in projections:** the **root** file's `DiagnosticDto.File` and `DependencyNode.VirtualPath`
   are the canonical `/proj/...` path; **included** files keep the loader's display path = the *raw include
   path* (e.g. `lib.scad`). `EntryPointCandidates`/`Root`/`InferredRoot` are all canonical `/proj/...`.
5. `ProjectAnalysis.Diagnostics` = loader diagnostics **+ a `SemanticAnalyzer.Analyze` pass** (surfaces
   SB3xxx), SB4001-filtered, source-ordered. When `Root` is `null`, `Diagnostics` is empty and `Missing`
   comes from a raw reference scan.
6. **Stats:** `Renames` = count(SB5004); `Normalizations` = count(SB5001)+count(SB5002); `DefinitionsRemoved`
   = the tree-shaken count parsed from the **SB5009** summary message (its only public surface; 0 when no
   profile ran); `FilesInlined` = distinct non-root files in the load graph (as `--verbose`); `OutputBytes`
   = UTF-8 length of `Text`.
7. The optional **`Workspace` aggregator deferred to W1** (see Next session).

### Exit criteria — all met
- [x] Zero-warning build; `dotnet test` green.
- [x] ≥95% line coverage on `Workspace/` (98.99%).
- [x] Entry-point inference: single / ambiguous / cyclic / geometry-tiebreak.
- [x] Missing enumeration correct incl. fonts excluded and SB4001 filtered from `Diagnostics`.
- [x] Layout inference: flat **and** foldered/zip resolve; diamond loads once; basename ambiguity surfaces
      as `AmbiguousReference` with its candidate set.
- [x] Bundle parity byte-identical to the CLI across Normal/Minify/Obfuscate.
- [x] No new `SBxxxx` codes; Core stays dependency-free and WASM-clean.

### Gotchas the next session must know
- `WebBundler` double-loads (one `SourceLoader.Load` for `FilesInlined`, plus `Bundler.Bundle`'s internal
  load) — fine for maker-scale inputs; revisit only if BOSL2-scale perf matters (a documented stretch).
- The dependency tree expands a diamond's shared file under **each** parent (it is a tree, not a DAG view);
  the load graph still loads it once (`Stats.FilesInlined` reflects the once-count). Cycle back-edges are
  emitted as resolved leaves (the loader nulls true-cycle targets, so the walk can't recurse forever).
- Coverage check: `dotnet test … --collect:"XPlat Code Coverage"` then filter the cobertura `class`
  entries by `filename -match 'Workspace'` (the PS snippet used this session).
