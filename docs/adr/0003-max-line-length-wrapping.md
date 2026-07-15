# ADR 0003 — `--max-line-length`: hard line wrapping for hardened output

- **Status:** Accepted — 2026-07-15 · **Implemented** — 2026-07-15
- **Deciders:** Dan Olsen (power-user field report) + Claude
- **Affects:** [UX.md](../UX.md) (Primary Options + Minify section), `Emitting/EmitOptions` +
  `Emitting/Emitter` (the `MaxLineLength` seam reserved in Slice 6 becomes real), the CLI, the web
  facade (`WebBundleOptions`/`WebBundler`/`OptionsPanel`), and the integration harness
  (`DifferentialAssert` now emits with the CLI's exact `EmitOptions`).

## Context

`--minify` packs each definition onto a single line — a whole module body becomes one line, and a
bundled data table becomes one very long assignment. A power user reports that some online platforms'
custom `.scad` parsers read files line-by-line into fixed buffers, and these single lines **overflow
the buffer**, breaking the upload even though the file is valid OpenSCAD. (OpenSCAD itself has no
line-length limit; this is purely third-party-parser robustness.)

Newlines are ordinary inter-token whitespace to OpenSCAD's lexer, so breaking a long line at a token
boundary is free: the token stream — and therefore the AST, and therefore the CSG — is unchanged.
The cost is one `\n` per break (~1% size on a typical minified bundle at a 256 limit).

Slice 6 already reserved the seam: `EmitOptions.MaxLineLength` existed as an advisory (unimplemented)
setting "reserved for a future wrapping pass". This ADR makes it real.

### Ground truth: what the Customizer reads off *lines*

Wrapping must not break Customizer parity ([Slice 7's correctness bar](../slices/Slice-7-Minify-Obfuscate.md)).
OpenSCAD's parameter extraction (`src/core/customizer/CommentParser.cc` in the checked-out source)
is line-based in three ways, each of which constrains where a break may go:

1. **`getLineToStop`** — parameter collection stops at the first line containing a `{` (outside
   strings/comments); a top-level assignment on/after that line is not a parameter. Wrapping never
   *reorders* statements, and moving a `{` to a *later* line can only move the cutoff down past lines
   that contain no top-level assignments (a wrapped header's own continuation lines), so the collected
   parameter set is unchanged.
2. **`getComment(fulltext, firstLine)`** — a parameter's inline annotation (`// [1:5]`) is read from
   **the line the assignment starts on**. If a break lands inside an annotated assignment, the `;` and
   the annotation move to a later line and the annotation is silently lost. **Therefore the emitter
   never breaks inside a top-level assignment that will emit a trailing comment.** Unannotated
   assignments are safe to wrap: their annotation is empty either way, and the description
   (line above, `getDescription`) and group (`collectGroups`, comment position) are unaffected.
3. **`collectGroups`** — a group marker `/* [Section] */` only counts if single-line. Breaks are only
   ever inserted between tokens, never inside comment trivia, so group markers are untouched.

## Decision

Implement **hard token-boundary wrapping** in the `Emitter`, controlled by `EmitOptions.MaxLineLength`:

- **`MaxLineLength = 0` ⇒ no wrapping** (the `EmitOptions` default — pretty output and every existing
  golden are byte-identical to before).
- **`MaxLineLength = n > 0` ⇒ no emitted line exceeds `n` characters**, unless a single unbreakable
  run (a string literal, an `include <path>`, a comment line) is itself longer than `n`.

Expose it as **`--max-line-length <n>`** in the CLI (and the web facade's Advanced options), with a
**profile-dependent default**:

| Mode | Default | Rationale |
|---|---|---|
| `--minify` / `--obfuscate` | **256** (`EmitOptions.DefaultHardenedMaxLineLength`) | Hardened output *synthesizes* its long lines; capping them costs ~1% size and removes a real interop failure. |
| plain bundle | **0** (off) | Pretty output keeps the author's line structure; authors' files already work on these platforms. Wrapping is opt-in. |
| explicit `--max-line-length n` | `n` everywhere | `0` = unlimited — the maximal-minification escape hatch. |

### Mechanism (deterministic, single-pass)

The emitter records a **break opportunity** — the last position where a newline is lexically safe —
and, when a token append pushes the current line past the limit, inserts the break at that recorded
position (greedy wrap: break at the last safe point at or before the limit). Opportunities are
recorded only *between* tokens:

- after every binary/assignment/ternary operator (`WriteOperator`),
- after every list separator comma (vectors, arguments, parameters, bindings) and C-style-`for` semicolon,
- between packed statements and after `{` inside a minified block.

They are **never** recorded inside string/number raw text, inside `include`/`use` paths, inside
comment trivia, or anywhere within a top-level assignment that will emit a trailing comment (rule 2
above — the Customizer annotation must stay on the assignment's first line).

In pretty mode (wrapping is opt-in there) a continuation line is indented two levels past the
statement's own indent, and the separator's trailing space is dropped at the break so no line ends in
whitespace. In minify mode the break is a bare `\n`.

Determinism and idempotence are preserved: output is a pure function of AST + options, and re-parsing
wrapped output yields the identical AST, so a second emit wraps at the same points
(`Emit(Parse(Emit(ast))) == Emit(ast)`; the SB6001 structural round-trip self-check still holds).

### Proof

`DifferentialAssert` (the OpenSCAD integration harness) previously emitted every bundle with default
`EmitOptions`; it now mirrors the CLI's exact option mapping — so the `T-001-harden` differential
fixture renders the **actual wrapped minified text** and proves it byte-identical CSG / identical
ECHO / no new warnings against the official binary. The fixture gained a long top-level data table
and a long-bodied module so the 256 default demonstrably triggers.

## Alternatives considered

- **Post-process the emitted text (split long lines with a regex/scanner).** Rejected: the
  Constitution bans text hacks in the core path for exactly this reason — a text pass cannot know it
  is inside a string, an `include` path, or an annotated parameter line without re-lexing. The
  emitter already knows; wrapping belongs there.
- **Wrap only at statement boundaries (unpack long statements onto multiple lines).** Rejected: the
  overflowing lines are *single statements* (a packed module body, a data table); statement-level
  wrapping wouldn't bound them. Token-boundary wrapping bounds every line except unbreakable atoms.
- **A strict guarantee (split strings with concatenation, etc.).** Rejected: decomposing a string
  literal (`"ab"` → `str("a","b")`) changes the AST and is an obfuscation transform, not an emit
  concern; a >256-char string literal is rare and the overflow guarantee degrades gracefully
  (documented as "unless a single token exceeds the limit").
- **Default the limit on for pretty output too (e.g. 100, the old advisory value).** Rejected: it
  would rewrap author-formatted code and churn every golden for no compatibility gain. The old
  advisory default of 100 was never observable; the new default of 0 keeps pretty output
  byte-identical.
- **A default lower/higher than 256.** 128 saves nothing further (buffers that small are not
  observed) and wraps more; 1024 may still overflow conservative fixed buffers. 256 matches the
  field report and costs ~1% — and it's a knob, not a constant.

## Consequences

- Minified/obfuscated bundles are compatible with line-buffered third-party parsers out of the box;
  users who want every last byte pass `--max-line-length 0`.
- `EmitOptions.MaxLineLength`'s default changes 100 → 0. No behavior change (100 was advisory/inert),
  but code that read the value sees the new default.
- The integration harness now renders hardened fixtures from the same text the CLI writes —
  strictly stronger differential coverage than before (it previously pretty-printed hardened ASTs).
- The web facade gains the same knob (`WebBundleOptions.MaxLineLength`, `null` = profile default),
  keeping Live↔CLI byte-parity.

## Implementation pointers

- Wrapping engine: [Emitter.cs](../../src/ScadBundler.Core/Emitting/Emitter.cs) (`AllowBreak`/
  `WrapIfNeeded`, the `_suppressBreaks` guard on annotated top-level assignments);
  [EmitOptions.cs](../../src/ScadBundler.Core/Emitting/EmitOptions.cs) (`MaxLineLength`,
  `DefaultHardenedMaxLineLength`).
- CLI: [BundleCommand.cs](../../src/ScadBundler/BundleCommand.cs) (`--max-line-length` + usage text +
  profile-dependent default).
- Web: [WebBundleOptions.cs](../../src/ScadBundler.Core/Workspace/WebBundleOptions.cs),
  [WebBundler.cs](../../src/ScadBundler.Core/Workspace/WebBundler.cs), `OptionsPanel.razor`.
- Proof: [DifferentialAssert.cs](../../tests/ScadBundler.IntegrationTests/TestSupport/DifferentialAssert.cs)
  (CLI-exact emit), `tests/Corpus/integration/T-001-harden` (long-line content),
  `EmitterTests` ("Line wrapping" section), `CliTests`, `BundleParityTests`.
