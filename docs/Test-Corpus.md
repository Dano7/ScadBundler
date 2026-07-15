# ScadBundler Test Corpus

**Status**: Seed. This establishes the corpus **conventions** (layout, naming, notation, validation method) and provides representative cases for every slice — including a binding case for each locked design decision. It is not exhaustive; each slice expands its own section as it is specified. The decision-proving cases (§6) are authoritative and must pass.

**Why this doc exists**: golden-master tests captured early catch regressions and double as executable proof of the design decisions in [AST-Reference.md](AST-Reference.md), [Spec.md](Spec.md), and [Diagnostics.md](Diagnostics.md). A case here is precise enough that an AI implementer can build the fixture and assert the result without further clarification.

---

## 1. Test Categories

| Category | Slice | Input | Expected artifact |
|---|---|---|---|
| Lexer | 1 | `.scad` source | token stream (`.tokens`) |
| Parser — statements | 2 | `.scad` source | AST (`.ast`) |
| Parser — expressions | 3 | `.scad` source | AST (`.ast`) |
| Semantic | 4 | `.scad` source | diagnostics (`.diag`) |
| Loader & Inliner (bundling) | 5 | multi-file project | bundled `.scad` + `.diag` |
| Emitter | 6 | `.scad` source | formatted `.scad` (golden) |
| Integration (test-only) | — | bundled output | render equivalence vs official OpenSCAD |

Two distinct notions of "correct":
- **Golden master** — exact text equality (line-ending–normalized) against a checked-in expected file. Used by lexer/parser/semantic/emitter unit tests.
- **Render equivalence** — the bundled output renders identically in the official OpenSCAD C++ engine. Used by the integration harness only (never shipped; see Constitution). Gates verification items V1–V3.

---

## 2. On-Disk Fixture Layout

```
tests/
  ScadBundler.Tests/              # unit tests (xUnit), drive the cases below
  Corpus/
    slice1-lexer/<id>/      input.scad           expected.tokens
    slice2-parser/<id>/     input.scad           expected.ast
    slice3-expr/<id>/       input.scad           expected.ast
    slice4-semantic/<id>/   input.scad           expected.diag
    slice5-bundle/<id>/     main.scad  <lib>.scad  options.txt  expected.scad  expected.diag
    slice6-emit/<id>/       input.scad           expected.scad
    integration/<id>/       main.scad  [<lib>.scad …]   # no expected.* — the expectation is render equivalence
  ScadBundler.IntegrationTests/     # the differential harness (env-gated: OPENSCAD_EXE or the default
                                    # install; skips itself when OpenSCAD is absent)
```

- `options.txt` (bundle cases): the CLI args after `bundle main.scad`, one token per line (e.g. `--on-collision`, `prefix`). Empty/absent = defaults.
- Missing `expected.diag` = "no diagnostics expected".
- The runner normalizes line endings (`\r\n`→`\n`) and trailing newline before comparing.

---

## 3. Naming Convention

`<slice-prefix>-<NNN>[-slug]` — e.g. `B-002-use-defs-only`, `S-001-member-access`.

| Prefix | Category |
|---|---|
| `L-` | Lexer |
| `P-` | Parser (statements) |
| `E-` | Parser (expressions) |
| `S-` | Semantic |
| `B-` | Bundle (loader+inliner) |
| `EM-` | Emitter |

---

## 4. Notation

**Tokens** (`expected.tokens`) — one per line, `<Kind> <line>:<col> <lexeme>` where `<Kind>` is the `TokenKind` enum member name and `<line>:<col>` is the token span's start (1-based). The `Eof` line has no lexeme. The **authoritative** `TokenKind` set is the enum **finalized in [Slice-1-Lexer.md](slices/Slice-1-Lexer.md) §6** — the `L-` cases below use those exact names (e.g. `Identifier`, `Assign`, `Semicolon`, `Eof`).
> Note: `*` and `%` lex as `Star`/`Percent`; whether they mean operator or modifier is the parser's call by position. Special variables (`$fn`, …) lex as `Identifier` with the `$` included.

**AST** (`expected.ast`) — shown in this doc using the readable notation from [AST-Reference.md](AST-Reference.md) §14 (`NodeName { field = value }`). The on-disk canonical serialization format is **finalized in Slice 2** (built alongside the parser, rendered by the test harness's `AstDump`). It is a deterministic indented tree isomorphic to the §14 notation:

- One node per line; **two-space** indentation per depth. The root (`ScadFile`) is at depth 0 and its statements at depth 1 (no `Statements:` label on the root).
- The node header is its type name plus scalar fields inline: strings quoted and escaped (`Name="cube"`, `RawText="0xFF"`), doubles invariant (`Value=255`), enums by name (`Operator=Add`), and modifier lists as `Modifiers=[Highlight, Background]` (omitted when empty). `BlankLineBefore=true` is appended only when set; spans and comment trivia are omitted.
- A single child-node field is rendered on its own line as `FieldName: <child header>` with the child's own children indented one level deeper (`Left:`, `Body:`, `Condition:`, `Start:`, …). A **nullable** structural child renders `FieldName: null` when absent (`Child:`, `Else:`, `Step:`); an *optional* child (`Parameter.DefaultValue`, `assert`/`echo` `Body`) is omitted entirely when absent.
- A list field renders `FieldName:` followed by its elements indented one level (unlabeled), or `FieldName: []` when empty (`Arguments:`, `Parameters:`, `Bindings:`, `Elements:`, `Statements:`).

Worked goldens live under `tests/Corpus/slice2-parser/<id>/expected.ast`; the harness regenerates them with `BLESS_AST=1`. `Span`/trivia are omitted unless a case is specifically under test (trivia/blank-line are covered by inline unit tests, with `BlankLineBefore` also surfaced in the dump — see P-003).

**Diagnostics** (`expected.diag`) — one per line, sorted by position, exact message from [Diagnostics.md](Diagnostics.md):
```
SBnnnn <ERROR|WARNING|INFO> <line>:<col> <message>
```
> **Multi-file (bundle) cases** include the file before the position, since a bundle's diagnostics span several files: `SBnnnn <SEV> <file>:<line>:<col> <message>` (paths are rendered relative to the case directory). The Slice-5 `B-` goldens use this form.

**Bundle cases** — each lists **Assertions** (binding; derived from locked decisions) and a **Reference output**, now locked as the exact `expected.scad` golden at `tests/Corpus/slice5-bundle/<id>/expected.scad` (Slice 6 fixed emitter formatting, so whitespace **is** binding alongside presence/absence/rewrite of constructs). The Slice-6 corpus runner regenerates every golden from current emitter output under `BLESS_EMIT=1`.

---

## 5. Corpus by Slice

### Slice 1 — Lexer (`L-`)

**L-001 — numbers, operators, assignment**
```scad
x = 1 + 2.5e3;
```
```
Identifier 1:1 x
Assign 1:3 =
Number 1:5 1
Plus 1:7 +
Number 1:9 2.5e3
Semicolon 1:14 ;
Eof 2:1
```

**L-002 — string escapes, special var, line comment**
```scad
$fn = 100; // smooth
```
```
Identifier 1:1 $fn
Assign 1:5 =
Number 1:7 100
Semicolon 1:10 ;
Eof 2:1
```
> Comment text is attached as trivia, not emitted as a token.

**L-003 (error) — unterminated string** → SB1001
```scad
s = "abc
```
```
SB1001 ERROR 1:5 Unterminated string literal.
```

**L-004 — hex literal** (lexer accepts `0x…`; value parsed to double, `RawText` preserved)
```scad
n = 0xFF;
```
```
Identifier 1:1 n
Assign 1:3 =
Number 1:5 0xFF
Semicolon 1:9 ;
Eof 2:1
```
> The resulting `NumberLiteral` has `Value=255, RawText="0xFF"`.

---

### Slice 2 — Parser: statements (`P-`)

Canonical statement examples (module def, function def, if/else-if, include/use, transform chains) live in [AST-Reference.md](AST-Reference.md) §14.2–14.8 and are part of this corpus by reference. Additional cases:

**P-001 — modifier stacking**
```scad
#%cube(1);
```
```
ModuleInstantiation {
  Modifiers = [Highlight, Background],
  Name = "cube",
  Arguments = [ Argument { Name=null, Value=NumberLiteral{Value=1, RawText="1"} } ],
  Child = null
}
```

**P-002 — empty vector & range assignment**
```scad
a = [];
r = [0 : 2 : 10];
```
```
AssignmentStatement { Name="a", Value = VectorExpression { Elements = [] } }
AssignmentStatement { Name="r", Value = RangeExpression {
  Start=NumberLiteral 0, Step=NumberLiteral 2, End=NumberLiteral 10 } }
```

**P-003 — blank-line preservation flag**
```scad
a = 1;

b = 2;
```
```
AssignmentStatement { Name="a", Value=NumberLiteral 1, BlankLineBefore=false }
AssignmentStatement { Name="b", Value=NumberLiteral 2, BlankLineBefore=true }
```

---

### Slice 3 — Parser: expressions (`E-`)

> The authoritative precedence/associativity table is owned by [Parser-Planning.md](Parser-Planning.md) (now locked, translated from OpenSCAD `parser.y`). E-001–E-003 cover basic precedence; E-004–E-008 pin the non-obvious gotchas.

**E-001 — multiplicative binds tighter than additive**
```scad
v = a + b * c;
```
```
AssignmentStatement { Name="v", Value =
  BinaryExpression { Operator=Add, Left=Identifier "a",
    Right = BinaryExpression { Operator=Multiply, Left=Identifier "b", Right=Identifier "c" } } }
```

**E-002 — `&&` binds tighter than `||`**
```scad
v = a || b && c;
```
```
BinaryExpression { Operator=Or, Left=Identifier "a",
  Right = BinaryExpression { Operator=And, Left=Identifier "b", Right=Identifier "c" } }
```

**E-003 — ternary is right-associative**
```scad
v = a ? b : c ? d : e;
```
```
ConditionalExpression { Condition=Identifier "a", Then=Identifier "b",
  Else = ConditionalExpression { Condition=Identifier "c", Then=Identifier "d", Else=Identifier "e" } }
```

**E-004 — `^` is right-associative** *(gotcha #2)* — `2 ^ 3 ^ 2` = `2^(3^2)` = 512
```scad
v = 2 ^ 3 ^ 2;
```
```
BinaryExpression { Operator=Power, Left=NumberLiteral 2,
  Right = BinaryExpression { Operator=Power, Left=NumberLiteral 3, Right=NumberLiteral 2 } }
```

**E-005 — `^` binds tighter than unary minus** *(gotcha #1)* — `-2 ^ 2` = `-(2^2)` = -4
```scad
v = -2 ^ 2;
```
```
UnaryExpression { Operator=Negate,
  Operand = BinaryExpression { Operator=Power, Left=NumberLiteral 2, Right=NumberLiteral 2 } }
```

**E-006 — `^`'s right operand may be unary** *(gotcha #3)*
```scad
v = 2 ^ -1;
```
```
BinaryExpression { Operator=Power, Left=NumberLiteral 2,
  Right = UnaryExpression { Operator=Negate, Operand=NumberLiteral 1 } }
```

**E-007 — unary stacks (right-associative)** *(gotcha #4)*
```scad
v = !!a;
```
```
UnaryExpression { Operator=Not, Operand = UnaryExpression { Operator=Not, Operand=Identifier "a" } }
```

**E-008 — bitwise/shift precedence: `&` tighter than `|`, both looser than `+`** *(gotcha #6)*
`a | b & c + d` → `+`(lvl 9) tighter than `&`(lvl 7) tighter than `|`(lvl 6) → `a | (b & (c + d))`
```scad
v = a | b & c + d;
```
```
BinaryExpression { Operator=BitwiseOr, Left=Identifier "a",
  Right = BinaryExpression { Operator=BitwiseAnd, Left=Identifier "b",
    Right = BinaryExpression { Operator=Add, Left=Identifier "c", Right=Identifier "d" } } }
```

**E-009 — let-comprehension vs trailing-let** *(Slice 3; the trailing-`let` rule)*
```scad
a = [let (n = 3) for (i = [0:n]) i];
b = [let (n = 3) n];
```
```
a → VectorExpression[ LetComprehension { Bindings=[Binding "n"=3],
      Body=ForComprehension { Bindings=[Binding "i"=RangeExpression 0..n], Body=Identifier "i" } } ]
b → VectorExpression[ LetExpression { Bindings=[Binding "n"=3], Body=Identifier "n" } ]
```

**E-010 — anonymous function literal** *(Slice 3)*
```scad
dbl = function (x) x * 2;
```
```
AssignmentStatement { Name="dbl", Value = FunctionLiteral {
  Parameters=[Parameter { Name="x" }],
  Body = BinaryExpression { Operator=Multiply, Left=Identifier "x", Right=NumberLiteral 2 } } }
```

**E-011 — C-style for comprehension** *(Slice 3)*
```scad
xs = [for (i = 0; i < 5; i = i + 1) i];
```
```
VectorExpression[ ForCComprehension {
  Init=[ Binding "i"=NumberLiteral 0 ],
  Condition = BinaryExpression { Operator=Less, Left=Identifier "i", Right=NumberLiteral 5 },
  Update=[ Binding "i"=BinaryExpression { Operator=Add, Left=Identifier "i", Right=NumberLiteral 1 } ],
  Body = Identifier "i" } ]
```

**E-012 — assert expression, with and without body** *(Slice 3; `expr_or_empty`)*
```scad
p = assert(n > 0) n;
q = assert(n > 0);
```
```
p → AssertExpression { Arguments=[ Argument { Value=(n > 0) } ], Body=Identifier "n" }
q → AssertExpression { Arguments=[ Argument { Value=(n > 0) } ], Body=null }
```

---

### Slice 4 — Semantic (`S-`)

**S-001 — member access is accepted** *(proves member access is never statically validated)*
```scad
v = [1, 2, 3];
comp = v.x;        // vector component
swizzle = v.w;     // experimental swizzle / undef — not an error
r = [0:1:10];
range_member = r.begin;  // range member
metrics = v.advance;     // arbitrary object member (textmetrics/fontmetrics)
```
No diagnostics (`expected.diag` absent). OpenSCAD's grammar accepts `call '.' TOK_ID` for any identifier and resolves member validity at runtime; an unmatched member yields `undef`, never a compile-time error. (Formerly proved the retired SB3001.)

**S-002 — comprehension generator outside a vector** *(proves SB3002)*
```scad
bad = [each [1, 2, 3] : 5];
```
```
SB3002 ERROR 1:8 'each' generator is only valid inside a list comprehension '[ ... ]'.
```
Companion positive: `ok = [each [1, 2, 3]];` → no diagnostics.

> **Note on the input form.** The bare `bad = each [1, 2, 3];` is rejected by the **parser** (`each` is not in the `expr` first-set → SB2005), so it never reaches the analyzer as a generator — the position restriction is enforced one layer earlier. The clean-parsing way to land a generator in a non-vector AST position is the range-start above (`[gen : end]` parses to a `RangeExpression` whose `Start` is the generator), which is exactly where SB3002 fires. The analyzer's guard is otherwise **defensive** — it matters for the synthesized/rewritten ASTs the Slice-5 inliner builds — and is additionally proven on a hand-constructed tree (`SemanticValidationTests.Generator_OutsideVector_ConstructedAst_Reports_SB3002`).

---

### Slice 5 — Loader & Inliner / bundling (`B-`)

**B-001 — `include` brings in everything and executes top-level geometry**
- `lib.scad`
  ```scad
  WALL = 2;
  module box() cube(WALL);
  cube(99);
  ```
- `main.scad`
  ```scad
  include <lib.scad>
  box();
  ```
- **Assertions**: output contains `WALL = 2;`, the `box` module, `cube(99);`, and `box();`; no `include` statement remains.
- **Reference output**
  ```scad
  WALL = 2;
  module box() cube(WALL);
  cube(99);
  box();
  ```

**B-002 — `use` imports definitions only; preserves referenced private constants** *(gates V2)*
- `lib.scad`
  ```scad
  $fn = 64;
  WALL = 2;
  UNUSED = 5;
  module box() cube(WALL);
  cube(99);
  ```
- `main.scad`
  ```scad
  use <lib.scad>
  box();
  ```
- **Assertions** (binding):
  - contains the `box` module definition
  - contains `WALL = 2;` (referenced by `box` → preserved as private constant)
  - does **not** contain `$fn = 64;` (top-level special var dropped)
  - does **not** contain `UNUSED = 5;` (unreferenced top-level var dropped)
  - does **not** contain `cube(99);` (top-level geometry dropped)
  - contains `box();`; no `use` statement remains
- **Reference output**
  ```scad
  WALL = 2;
  module box() cube(WALL);
  box();
  ```

**B-003 — `assign` normalized to `let`** *(proves SB5001, gates V3)*
- `main.scad`
  ```scad
  assign(a = 1, b = 2) translate([a, b, 0]) cube(1);
  ```
- **Assertions**: output contains `let(a = 1, b = 2)`; output contains no `assign`.
- **Reference output**
  ```scad
  let(a = 1, b = 2) translate([a, b, 0]) cube(1);
  ```
- **Diagnostics**
  ```
  SB5001 WARNING 1:1 'assign' is deprecated; rewritten to 'let'. (Behavior preserved.)
  ```

**B-004 — `child` normalized to `children`** *(proves SB5002, gates V1)*
- `main.scad`
  ```scad
  module wrapper() {
      child();
      child(1);
  }
  ```
- **Assertions**: `child();` → `children(0);`; `child(1);` → `children(1);`; no token `child` followed by `(` remains.
- **Reference output**
  ```scad
  module wrapper() {
      children(0);
      children(1);
  }
  ```
- **Diagnostics**
  ```
  SB5002 WARNING 2:5 'child(...)' is deprecated; rewritten to 'children(...)'.
  SB5002 WARNING 3:5 'child(...)' is deprecated; rewritten to 'children(...)'.
  ```

**B-005 — deprecated built-in preserved** *(proves SB5003)*
- `main.scad`
  ```scad
  import_stl("part.stl");
  ```
- **Assertions**: output still contains `import_stl("part.stl");` unchanged.
- **Diagnostics**
  ```
  SB5003 INFO 1:1 'import_stl' is deprecated in OpenSCAD; preserved unchanged. Consider migrating to its modern equivalent.
  ```

**B-006 — definition collision between two `use`d libraries** *(default for `use` = namespace/prefix)*
- `gear_a.scad` → `module gear() cube(1);`
- `gear_b.scad` → `module gear() sphere(1);`
- `main.scad`
  ```scad
  use <gear_a.scad>
  use <gear_b.scad>
  gear();
  ```
- **Assertions**: both module bodies are emitted under distinct (namespaced) names; the top-level `gear()` binds to the **last-`use`d** library (gear_b → `sphere`), matching OpenSCAD's use-lookup order (`SourceFile.cc` inserts each `use` at the front of `usedlibs`); a namespacing diagnostic is reported.
- **Reference output** (illustrative prefix scheme)
  ```scad
  module gear_a__gear() cube(1);
  module gear_b__gear() sphere(1);
  gear_b__gear();
  ```
- **Diagnostics** (one `SB5004` per namespaced definition, sorted by source position):
  ```
  SB5004 WARNING gear_a.scad:1:1 'gear' from 'gear_a.scad' renamed to 'gear_a__gear' to resolve a collision.
  SB5004 WARNING gear_b.scad:1:1 'gear' from 'gear_b.scad' renamed to 'gear_b__gear' to resolve a collision.
  ```

**B-007 — `include` duplicate definitions are last-wins** *(matches OpenSCAD; SB3004)*
- `a.scad` → `module part() cube(1);`
- `b.scad` → `module part() sphere(1);`
- `main.scad`
  ```scad
  include <a.scad>
  include <b.scad>
  part();
  ```
- **Assertions** (default for `include` preserves OpenSCAD semantics = last-wins): `part()` resolves to the **later** definition (b → `sphere`); the earlier is dropped; an SB3004 warning is emitted. (With `--on-collision prefix`, both are kept and namespaced instead, as in B-006.)
- **Reference output**
  ```scad
  module part() sphere(1);
  part();
  ```
- **Diagnostics**
  ```
  SB3004 WARNING b.scad:1:1 module 'part' is redefined; the last definition wins.
  ```

---

### Slice 6 — Emitter (`EM-`)

**EM-001 — Customizer trivia survives a round trip**
Input = the example in [AST-Reference.md](AST-Reference.md) §14.7 (`/* [Dimensions] */`, label line, `// [5:50]`).
- **Assertion**: after parse→emit (default options, `--preserve-comments` on), all three comments are present and still attached to `diameter` (section banner + label leading, annotation trailing).

**EM-002 — idempotence**
For every `slice6-emit` and `slice5-bundle` golden, `emit(parse(expected)) == expected`. Pretty-printing must be a fixed point.

### Slice 7 — Minifier & Obfuscator (`T-`)

**T-001-harden — hardening render equivalence** (`integration/T-001-harden/`)
A root with prologue params (incl. a string), an echo'd string, an included private constant, a namespaced
`use` library, an unused (tree-shakeable) module, and long-line content (a data table + a packed module
body that each minify to > 256 chars, so the default `--max-line-length` wrapping demonstrably fires —
ADR 0003).
- **Assertion**: bundling with `--minify` *and* with `--obfuscate` each renders **byte-identical CSG**,
  emits identical `ECHO:`, and adds no new warnings against the official binary (the Tier-1 proof) — from
  the exact text the CLI writes (minified + wrapped; `DifferentialAssert` mirrors the CLI's emit-option
  mapping). Run by `HardeningDifferentialTests`; self-skips without OpenSCAD.
- Unit-level behaviors (determinism, avalanche, Customizer aliasing, tree-shaking, string decomposition,
  indirection, decoys, semantic no-op) live in `tests/ScadBundler.Core.Tests/Transforming/`.

---

## 6. Decision-Proving Cases (cross-reference)

Each locked decision maps to at least one binding case and, where behavioral, an integration verification item.

| Decision (source) | Case | Integration |
|---|---|---|
| `include` = full inline + execute (Spec) | B-001 | — |
| `use` = defs only + private constants (Spec) | B-002 | V2 |
| `assign`→`let` (Spec, Diag SB5001) | B-003 | V3 |
| `child()`→`children(0)`, `child(n)`→`children(n)` (Diag SB5002) | B-004 | V1 |
| deprecated built-ins preserved (Diag SB5003) | B-005 | — |
| member access accepted, not validated (AST §15.11) | S-001 | — |
| comprehension only in vectors (AST §7, Diag SB3002) | S-002 | — |
| modifier stacking as list (AST §5) | P-001 | — |
| blank-line via `BlankLineBefore` (AST §15.7) | P-003 | — |
| numbers are `double` + `RawText`, incl. hex (AST §15.9) | L-001, L-004, P-002 | — |
| operator precedence/associativity (Parser-Planning, from `parser.y`) | E-001–E-008 | — |
| comprehension & functional-expr grammar (from `parser.y`) | E-009–E-012 | — |
| file resolution order + cycle detection (Spec, from `parsersettings.cc`) | B-001/B-002; modulecache fixtures | — |
| include = last-wins, use = namespace (Spec, from `LocalScope`/`ScopeContext`) | B-006, B-007 | V2 |

---

## 7. Coverage Map / TODO

- [x] Conventions, layout, notation, validation method
- [x] One binding case per locked decision (§6)
- [ ] Lexer: full token-kind battery, every escape, every numeric form, all error codes
- [ ] Parser: one case per grammar production (tie to RapCAD BNF rules)
- [x] Expressions: precedence/associativity **gotchas** pinned (E-004–E-008, from `parser.y`); exhaustive per-level battery still TODO
- [ ] Semantic: duplicate-definition, undefined-symbol, arity cases
- [ ] Bundle: cycle detection, search-path/`OPENSCADPATH`, dedup (identical module hashing), all `--on-collision` strategies, license aggregation
- [x] Emitter: per-node formatting, precedence parenthesization, `--minify`, brace/indent style, Customizer trivia (EM-001), idempotence (EM-002), and the `B-001`..`B-007` exact goldens (`slice6-emit/` + `slice5-bundle/*/expected.scad`); line-length wrapping + license-header aggregation remain post-v1
- [ ] Real-world golden masters: small slices of BOSL2 / NopSCADlib / dotSCAD
- [x] Adopt OpenSCAD `tests/data/modulecache-tests` + `examples/` — the positive modulecache roots (8) and three example files are now differentially rendered by the integration harness; the broader examples sweep stays in `ParserRealWorldTests` (error-path modulecache fixtures excluded by design: SB4002 makes cycles hard errors where OpenSCAD silently tolerates them)
- [x] Integration harness wired for V1–V3 (`tests/ScadBundler.IntegrationTests`, fixtures `tests/Corpus/integration/V-00*`; all three verified against OpenSCAD 2021.01 — byte-identical CSG, no new warnings)

---

*Cross-references: [AST-Reference.md](AST-Reference.md), [Spec.md](Spec.md), [Diagnostics.md](Diagnostics.md), [Development-Slices.md](Development-Slices.md), [Grammar-References.md](Grammar-References.md).*
