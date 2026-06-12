# Handoff — ScadBundler Live (web-impl status)

Running status of the **ScadBundler Live** web companion build (the docs in this folder; protocol in
[IMPLEMENTATION-KICKOFF.md](IMPLEMENTATION-KICKOFF.md)). One living file, updated per slice. A cold session
should be able to resume from here with no other context.

---

## ▶ Next session — start here

**W0 + W1 are done, green, and committed.** Build **W2 — Dependency UX & friendly errors**
([slices/Slice-W2-Dependency-UX.md](slices/Slice-W2-Dependency-UX.md)). W1 stood up the shell + happy-path
bundle; W2 makes the page **smart and forgiving**. Build order remaining: **W2 → W3** (W4 deferred — do not
build).

First things for W2 (the controller + facade already expose everything you need):
- **Missing-reference drop targets** (`MissingRow`): render one ⚠ row per `ProjectAnalysis.Missing` with
  its `NeededBy`; dropping the file anywhere re-analyzes (the existing `DropZone.Ingest` path already
  feeds `WorkspaceController.AddOrReplace`).
- **Entry-point override**: when `InferredRoot` is `null`, list `EntryPointCandidates` and call
  `WorkspaceController.SetRoot(virtualPath)` on click (already implemented on the controller). Add a
  "★ main" re-designate affordance on every file row.
- **`MainFileEditor`**: debounced `<textarea>` → add `WorkspaceController.EditMainFile(newText)` (NOT yet
  on the controller — deferred from W1; it replaces the root upload and re-analyzes). The current
  `AddOrReplace` keys by `UploadedFile.Name`, so "edit" = re-add the root's `Name` with new text.
- **`ProblemsPanel`** with the UI-only friendly-code map (Slice-W2 §3); SB4001 is already filtered out of
  `ProjectAnalysis.Diagnostics` — do not reintroduce it.
- **`StructureTree`** (read-only) + **`ConflictPicker`** for `ProjectAnalysis.Ambiguous` (re-add the chosen
  file with `Name = rawPath`).

> Ask the user before deciding the **W3 hosting target** and before adding **any** dependency to
> `ScadBundler.Core`.

---

## Slice W1 — done (2026-06-12)

The **Blazor WebAssembly shell + happy-path bundle MVP**: drop a complete multi-file project (folder /
loose files / `.zip`) and get a copyable, downloadable single file — wired entirely to the W0 facade,
**byte-identical to the CLI**. Core untouched; no Core dependency added.

### Projects added (both wired into `ScadBundler.sln`)
- **`web/ScadBundler.Web`** — `Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`, refs `ScadBundler.Core`
  only. `PublishTrimmed` + `InvariantGlobalization` on (Design §5). Explicit `Program.Main` (no top-level
  statements) registers `WorkspaceController` (scoped). `GenerateDocumentationFile=false` (thin shell, not
  a library surface). Packages: `Microsoft.AspNetCore.Components.WebAssembly` (+ `.DevServer`,
  `PrivateAssets=all`).
- **`tests/ScadBundler.Web.Tests`** — `bunit.web` 1.40.0 + xUnit; refs the web app. **15 tests.**

### Files of note (`web/ScadBundler.Web`)
- `State/WorkspaceController.cs` — the single state owner (Design §3.2): `AddOrReplace`/`Remove`/`SetRoot`/
  `SetOptions` → `Recompute()` = `ProjectAnalyzer.Analyze(Uploads, root)` → gate on `Root != null` &&
  `Missing`+`Ambiguous` empty → `WebBundler.Bundle`. Fires `Changed`. (`EditMainFile` deferred to W2.)
- `Ingestion/ZipIngestion.cs` — BCL `ZipArchive` → `UploadedFile`s (`.scad` only, dirs skipped, paths
  preserved). `Ingestion/IngestItem.cs` + `IngestItemReader` — the managed boundary the JS hands files to
  (`text` verbatim, `zip` Base64-decoded + expanded; malformed items skipped, never thrown).
- `Components/`: `Landing` (static blurb), `EngineStatus`, `DropZone` (+ `[JSInvokable] Ingest`), `FileList`
  (★ badge, ✓/⚠/ⓕ icons, "still needed" rows), `OutputPanel` (Copy/Download gated on `Ok && Text>0`,
  download named `<rootstem>.bundled.scad`). `App.razor` composes them + subscribes to `Changed`.
- `wwwroot/index.html` (branded `#app` shell paints **before** the runtime), `wwwroot/interop.js`,
  `wwwroot/css/app.css`.

### Key decisions / deviations (spec edits made this slice)
1. **Unified JS ingestion** instead of the original "pick files = managed `InputFile`" sketch. Blazor's
   `InputFile`/`IBrowserFile` does **not** expose `webkitRelativePath`, so folder picks *require* a JS
   shim anyway; rather than split the path, **all** picking + dropping go through one `interop.js`
   (programmatic hidden `<input>` for picks; the `webkitGetAsEntry`/`readEntries` walk for drops). JS reads
   `.scad` to text and `.zip` to Base64, then calls one `[JSInvokable] DropZone.Ingest(IngestItem[])`;
   unzipping is **managed** (BCL). Still **no JS library**, facade unchanged. → **Design §4 updated** (table
   + a "W1 implementation note" with the trade-off: file text crosses the JS↔WASM boundary as a string —
   negligible at maker scale).
2. **`EngineStatus` "loading" lives in `index.html`, not the component.** In a WASM app the runtime is
   ready by the time any component renders, so the Blazor `EngineStatus` always shows "ready"; the
   pre-boot "Engine loading…" is the static `#app` shell that Blazor replaces. Satisfies "paint shell
   before runtime."
3. **No Core `Workspace` aggregator added** (Spec §5.6). The web `WorkspaceController` plays that role over
   the pure facade functions; the optional Core aggregator stays unbuilt (not needed for anything).

### Quality / verification
- **Build: 0 warnings.** **Tests: 763 green** (Core 691, CLI 23, Integration 34, **Web 15** [+15]).
- **Real-world byte-identical parity re-proven** on `C:\git\dan\SCAD\ForkedHolder.scad` + its 4 libs via a
  *throwaway* loose-upload test (deleted after): facade output == disk/CLI output, **21 845 bytes** each,
  `Missing=0 Ambiguous=0`, `FilesInlined=4`. This exercises the case-insensitive basename inference
  (`include <forkedholderlib.scad>` → `ForkedHolderLib.scad`, `<cleatarray.scad>` → `CleatArray.scad`) in
  the hardest (structure-less) mode.
- **App boots:** `dotnet run --project web/ScadBundler.Web --urls http://localhost:5219` serves the shell
  + `_framework/blazor.webassembly.js` + `interop.js` (all 200); the WASM runtime boots with **no console
  errors**; the branded shell paints before boot.

### Gotchas the next session must know
- **`webkitGetAsEntry()` must be called synchronously** on the `DataTransferItem`s *before* any `await`
  (the items list is emptied after the handler yields) — `interop.js` snapshots entries first. If you add
  more drop handling in W2, preserve that ordering.
- **`MissingRow` drop targets (W2)** can reuse the same `scadLive.registerDropZone` machinery, or just let
  drops anywhere on the main zone resolve them (the facade re-analyzes regardless of where the file landed).
- **Manual run / preview**: the dev server is `dotnet run --project web/ScadBundler.Web --urls
  http://localhost:5219`. (A `.claude/launch.json` was used transiently for the preview tool and removed;
  recreate it if you want the managed preview again.) The preview tool's **screenshot timed out** twice
  this session even though the app booted cleanly (console-verified) — prefer `preview_console_logs` /
  curl over screenshots if it hangs.
- **bUnit version**: pinned `bunit.web` 1.40.0 (classic `Bunit.TestContext` API). The `bunit` 2.x
  metapackage exists (2.7.2) but renames the context type — don't "upgrade" it casually.

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
