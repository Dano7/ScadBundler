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
  Integration/              # test-only project, requires OpenSCAD on PATH
    V1-child/  V2-use-consts/  V3-assign-let/
```

- `options.txt` (bundle cases): the CLI args after `bundle main.scad`, one token per line (e.g. `--on-collision`, `rename`). Empty/absent = defaults.
- Missing `expected.diag` = "no diagnostics expected".
- The runner normalizes line endings (`\r\n`→`\n`) and trailing newline before comparing.

---

## 3. Naming Convention

`<slice-prefix>-<NNN>[-slug]` — e.g. `B-002-use-defs-only`, `S-001-member-invalid`.

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

**Tokens** (`expected.tokens`) — one per line, `KIND line:col lexeme`. The `TokenKind` set is *proposed here*, finalized in Slice 1 / [Parser-Planning.md](Parser-Planning.md):
```
ASSIGN, SEMI, COMMA, COLON, DOT, LPAREN, RPAREN, LBRACE, RBRACE, LBRACKET, RBRACKET,
PLUS, MINUS, STAR, SLASH, PERCENT, CARET, LT, LE, GT, GE, EQ, NE, AND, OR, NOT, QUESTION,
BANG, HASH, IDENT, NUMBER, STRING, TRUE, FALSE, UNDEF,
MODULE, FUNCTION, IF, ELSE, FOR, INTERSECTION_FOR, LET, ASSIGN_KW, EACH, INCLUDE, USE, EOF
```
> Note: `*` and `%` lex as `STAR`/`PERCENT`; whether they mean operator or modifier is the parser's call by position. Special variables (`$fn`, …) lex as `IDENT` with the `$` included.

**AST** (`expected.ast`) — shown in this doc using the readable notation from [AST-Reference.md](AST-Reference.md) §14 (`NodeName { field = value }`). The on-disk canonical serialization format is finalized in Slice 2 (built alongside the parser); it must be deterministic and isomorphic to this notation. `Span`/trivia omitted unless under test.

**Diagnostics** (`expected.diag`) — one per line, sorted by position, exact message from [Diagnostics.md](Diagnostics.md):
```
SBnnnn <ERROR|WARNING|INFO> <line>:<col> <message>
```

**Bundle cases** — each lists **Assertions** (binding; derived from locked decisions — these are what the test checks now) and a **Reference output** (illustrative bundle; becomes the exact `expected.scad` golden once Slice 6 locks emitter formatting). Whitespace in reference output is not binding until then; presence/absence/rewrite of constructs **is**.

---

## 5. Corpus by Slice

### Slice 1 — Lexer (`L-`)

**L-001 — numbers, operators, assignment**
```scad
x = 1 + 2.5e3;
```
```
IDENT 1:1 x
ASSIGN 1:3 =
NUMBER 1:5 1
PLUS 1:7 +
NUMBER 1:9 2.5e3
SEMI 1:14 ;
EOF 2:1
```

**L-002 — string escapes, special var, line comment**
```scad
$fn = 100; // smooth
```
```
IDENT 1:1 $fn
ASSIGN 1:5 =
NUMBER 1:7 100
SEMI 1:10 ;
EOF 2:1
```
> Comment text is attached as trivia, not emitted as a token.

**L-003 (error) — unterminated string** → diagnostic (code in `SB1xxx` range, finalized Slice 1)
```scad
s = "abc
```
```
SB1xxx ERROR 1:5 Unterminated string literal.
```

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

> The authoritative precedence/associativity table is owned by [Parser-Planning.md](Parser-Planning.md); a full precedence battery is added here once it is locked. Seed cases use only uncontroversial rules.

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

---

### Slice 4 — Semantic (`S-`)

**S-001 — invalid vector member** *(proves SB3001)*
```scad
v = [1, 2, 3];
bad = v.w;
```
```
SB3001 ERROR 2:9 Invalid member '.w'; only .x, .y, and .z are valid vector components.
```
Companion positive (no diagnostics): `ok = v.x;` → `expected.diag` absent.

**S-002 — comprehension generator outside a vector** *(proves SB3002)*
```scad
bad = each [1, 2, 3];
```
```
SB3002 ERROR 1:7 'each' generator is only valid inside a list comprehension '[ ... ]'.
```
Companion positive: `ok = [each [1, 2, 3]];` → no diagnostics.

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

**B-006 — module name collision across merged files** *(default `--on-collision rename`)*
- `gear_a.scad` → `module gear() cube(1);`
- `gear_b.scad` → `module gear() sphere(1);`
- `main.scad`
  ```scad
  include <gear_a.scad>
  include <gear_b.scad>
  gear();
  ```
- **Assertions**: both module bodies survive under distinct names; the surviving `gear()` call binds to the first definition's name; one rename is reported.
- **Diagnostics**: collision/rename diagnostic — code in `SB5xxx`, **TBD** (see [Diagnostics.md](Diagnostics.md) "To Be Cataloged"). This case is a placeholder until the collision code + default naming scheme are pinned down.

---

### Slice 6 — Emitter (`EM-`)

**EM-001 — Customizer trivia survives a round trip**
Input = the example in [AST-Reference.md](AST-Reference.md) §14.7 (`/* [Dimensions] */`, label line, `// [5:50]`).
- **Assertion**: after parse→emit (default options, `--preserve-comments` on), all three comments are present and still attached to `diameter` (section banner + label leading, annotation trailing).

**EM-002 — idempotence**
For every `slice6-emit` and `slice5-bundle` golden, `emit(parse(expected)) == expected`. Pretty-printing must be a fixed point.

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
| member ∈ {x,y,z} (AST §15.11, Diag SB3001) | S-001 | — |
| comprehension only in vectors (AST §7, Diag SB3002) | S-002 | — |
| modifier stacking as list (AST §5) | P-001 | — |
| blank-line via `BlankLineBefore` (AST §15.7) | P-003 | — |
| numbers are `double` + `RawText` (AST §15.9) | L-001, P-002 | — |

---

## 7. Coverage Map / TODO

- [x] Conventions, layout, notation, validation method
- [x] One binding case per locked decision (§6)
- [ ] Lexer: full token-kind battery, every escape, every numeric form, all error codes
- [ ] Parser: one case per grammar production (tie to RapCAD BNF rules)
- [ ] Expressions: full precedence/associativity battery (after Parser-Planning locks the table)
- [ ] Semantic: duplicate-definition, undefined-symbol, arity cases
- [ ] Bundle: cycle detection, search-path/`OPENSCADPATH`, dedup (identical module hashing), all `--on-collision` strategies, license aggregation
- [ ] Emitter: brace style, line-length wrapping, `--minify`, license header block
- [ ] Real-world golden masters: small slices of BOSL2 / NopSCADlib / dotSCAD
- [ ] Integration harness wired for V1–V3

---

*Cross-references: [AST-Reference.md](AST-Reference.md), [Spec.md](Spec.md), [Diagnostics.md](Diagnostics.md), [Development-Slices.md](Development-Slices.md), [Grammar-References.md](Grammar-References.md).*
