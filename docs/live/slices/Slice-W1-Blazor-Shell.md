# Slice W1 — Blazor Shell + Bundle MVP

**Status**: **done (2026-06-12)** — see [../handoff.md](../handoff.md) for the running status.
**Project**: `web/ScadBundler.Web/` (new Blazor WebAssembly app, .NET 10) + `tests/ScadBundler.Web.Tests/`.
**Depends on**: [Slice-W0](Slice-W0-Workspace-Facade.md) (the facade it drives).
**Read with**: [../Design.md](../Design.md) §3–§4 (components, state, interop), [../Spec.md](../Spec.md) §3.

The first end-to-end milestone: a real page where dropping a complete multi-file project yields a
downloadable single file. UX polish, missing-file handling, and options come in W2/W3 — W1 proves the
**happy path** through the shell.

---

## 1. Goal

Stand up the Blazor WASM project and the minimal UI that takes a complete set of files and produces a
bundle the user can copy or download — wired entirely to the W0 facade, byte-identical to the CLI.

**Non-goals (this slice):** missing-reference drop targets, editable main file, friendly error
explanations (W2); the options panel, a11y/responsive polish, deploy pipeline (W3); any preview (W4).

---

## 2. Deliverables

- **`web/ScadBundler.Web`** — `Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`, referencing
  `ScadBundler.Core`. `Program.cs` registers `WorkspaceController` (scoped). Added to `ScadBundler.sln`.
- **`wwwroot/index.html`** — renders the branded shell + blurb *immediately*; Blazor's default loading
  splash replaced; `interop.js` linked.
- **Components** (Design §3.1): `Landing`, `EngineStatus`, `DropZone` (accepts **folder / files / `.zip`**),
  `FileList` (read-only statuses for now), `OutputPanel`. `WorkspaceController` (Design §3.2) holds state
  and calls `ProjectAnalyzer` + `WebBundler`.
- **Ingestion** (Design §4): the drop zone accepts all three structure-preserving modes from the start —
  loose files (`InputFile` + `DataTransfer.files`), a folder (`<input webkitdirectory>` button + the
  drag-drop entries API), and a `.zip` (unzipped in managed code via BCL `System.IO.Compression.ZipArchive`
  — **no JS lib**). Each produces `UploadedFile`s whose `Name` carries the relative path. This is what
  makes references resolve unambiguously (Spec §3.2).
- **`wwwroot/interop.js`** — file + folder drop (`DataTransfer.files`; folders via
  `webkitGetAsEntry()` → recursive `readEntries()` for relative paths), clipboard copy, blob download
  (Design §4).
- **`tests/ScadBundler.Web.Tests`** — bUnit smoke tests (see §5).

---

## 3. Behavior (this slice)

1. Page paints instantly (shell + blurb); `EngineStatus` shows "loading…" until the runtime is ready,
   then "ready" and the drop zone activates.
2. The user drops / chooses a **folder, loose files, or a `.zip`**. `DropZone` reads each to text (folder
   and zip entries keep their relative path in `UploadedFile.Name`) and calls
   `WorkspaceController.AddOrReplace`.
3. The controller calls `ProjectAnalyzer.Analyze(uploads)`; `FileList` shows the inferred entry point
   (★ badge) and each file with a ✓/⚠/ⓕ status icon (statuses are display-only in W1).
4. If the dependency set is complete (no non-font ⚠), the controller calls `WebBundler.Bundle`;
   `OutputPanel` shows the bundle text + a stats line and enables **Copy** and **Download**.
5. **Copy** writes to the clipboard; **Download** saves `<rootstem>.bundled.scad` (matches the CLI's
   default output name shape).
6. Re-dropping more files re-runs the cycle live.

If the set is **incomplete** in W1, show a simple "still need N file(s)" message and keep output disabled
(the rich missing-file UX is W2).

---

## 4. Scope (In / Out)

**In:** the project scaffold + solution wiring; the shell/blurb; the single drop zone with **all three
ingestion modes** (folder, files, `.zip`) — these are foundational (they determine whether structure is
present), so they ship in W1; read-only file list with statuses; live happy-path bundle; copy + download;
the JS interop file.

**Out:** the read-only structure tree + the basename-conflict picker (W2); missing-file drop targets,
main-file editing/promotion, problems panel (W2); options, deploy, preview.

---

## 5. Test plan

- **bUnit**: `FileList` renders the entry-point badge + correct status icons for a given `ProjectAnalysis`;
  `OutputPanel` enables Copy/Download iff `WebBundleResult.Ok && Text.Length > 0`; the controller produces
  a non-empty bundle for a complete in-memory set.
- **Manual acceptance**: `dotnet run --project web/ScadBundler.Web`; verify all three ingestion modes —
  drop a **folder**, drop **loose files**, and drop a **`.zip`** of a real project
  (`C:\git\dan\SCAD\ForkedHolder.scad` + its libs); confirm each bundles and the download **opens in
  OpenSCAD identically** to the CLI's `ForkedHolder.bundled.scad`.
- **Ingestion unit test**: a `.zip` byte array → `UploadedFile`s with correct relative `Name`s (the zip
  reader is plain BCL, so this is testable without a browser).
- **Parity check**: the in-browser bundle text equals the CLI output for the same inputs (inherited from
  W0, re-confirmed manually here).

---

## 6. Exit criteria

- [x] `dotnet build` zero-warning; the app runs via `dotnet run` and paints the shell before the runtime
      finishes loading. *(Verified: dev server serves the branded `#app` shell + "Engine loading…" before
      the WASM runtime; runtime then boots with no console errors.)*
- [x] All three ingestion modes work — **folder**, **loose files**, and **`.zip`** — with folder/zip
      preserving relative paths. *(`interop.js` entries-API walk + `webkitdirectory` picker keep relative
      paths; `.zip` unzipped in managed BCL `ZipArchive`. Zip + loose paths covered by unit tests.)*
- [x] Dropping a complete multi-file project produces a bundle; **Copy** and **Download** work. *(Controller
      bundle path covered; Copy/Download wired to `interop.js` and gated by bUnit tests.)*
- [x] The downloaded bundle is byte-identical to the CLI's output for the same inputs. *(Inherited from W0
      `BundleParityTests`; re-proven on the real `ForkedHolder.scad` tree via a throwaway loose-upload
      parity check — 21 845 bytes identical, incl. the case-mismatched `include <forkedholderlib.scad>`.)*
- [x] bUnit smoke tests pass (file-list rendering, output gating). *(15 web tests.)*
- [x] `ScadBundler.Core` untouched except for the W0 `Workspace/` additions; no Core dependency added.
