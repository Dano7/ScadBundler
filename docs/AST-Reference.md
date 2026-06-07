# ScadBundler AST Reference

**Status**: Authoritative. This document is the single source of truth for the ScadBundler Abstract Syntax Tree. Where [Parser-Planning.md](Parser-Planning.md) or other docs sketch node shapes, *this* document overrides them.

**Purpose**: Define every AST node — record name, field names, field types, and nullability — precisely enough that an AI assistant can implement the full tree and its visitor in one shot, and that a parser can be written to populate it without further design decisions.

**Grammar basis**: Node shapes follow the OpenSCAD language as described in the [Official Language Manual](https://en.wikibooks.org/wiki/OpenSCAD_User_Manual/The_OpenSCAD_Language) and cross-checked against the references in [Grammar-References.md](Grammar-References.md) (RapCAD BNF, BelfrySCAD PEG, tree-sitter-openscad). Operator precedence is defined in [Parser-Planning.md](Parser-Planning.md), not here.

---

## Table of Contents
1. [Design Principles](#1-design-principles)
2. [Foundational Types](#2-foundational-types)
3. [Base Node & Trivia](#3-base-node--trivia)
4. [Root Node](#4-root-node)
5. [Statements](#5-statements)
6. [Expressions](#6-expressions)
7. [List Comprehensions](#7-list-comprehensions)
8. [Supporting Nodes](#8-supporting-nodes)
9. [Enums](#9-enums)
10. [Polymorphic Keyword Map](#10-polymorphic-keyword-map)
11. [Customizer Representation](#11-customizer-representation)
12. [Concrete Node Index](#12-concrete-node-index)
13. [Visitor Pattern](#13-visitor-pattern)
14. [Worked Examples](#14-worked-examples)
15. [Design Decisions & Rationale](#15-design-decisions--rationale)
16. [Open Questions](#16-open-questions)
17. [Suggested File Layout](#17-suggested-file-layout)

---

## 1. Design Principles

- **Immutable records.** Every node is a C# `record` (or `readonly record struct` for value-like helpers). Transformations produce new trees via `with` expressions; nodes are never mutated.
- **Closed hierarchy.** All node types live in the core library and derive from `AstNode`. The base types (`Statement`, `Expression`) are `abstract record`; concrete leaf nodes are `sealed record`. This makes the set of nodes knowable for exhaustive `switch` and visitor generation.
- **Parse-only tree.** The AST captures *syntax*. It does **not** carry resolved symbols, resolved include paths, types, or dedup decisions. Those live in side tables produced by later passes (semantic analyzer, source loader, inliner), keyed by node identity or `SourceSpan`. This keeps the AST pure and reusable.
- **Round-trip fidelity.** Nodes retain enough raw information (raw number text, raw string text, explicit parentheses, comment trivia) that the emitter can reproduce author intent. Pretty-printing style is the emitter's job; *preserving meaning and Customizer/license comments* is the AST's job.
- **Source provenance on every node.** Every node knows its `SourceSpan`, which includes the originating `SourceFile`. After inlining, nodes from many files coexist in one tree; provenance must survive.

---

## 2. Foundational Types

```csharp
/// A loaded source file. One instance per physical file read.
public sealed record SourceFile(string Path, string Text);

/// A position in a source file.
/// Offset is 0-based char index into SourceFile.Text.
/// Line and Column are 1-based for human-facing diagnostics.
public readonly record struct SourcePosition(int Offset, int Line, int Column);

/// A half-open span [Start, End) within a single file.
public readonly record struct SourceSpan(SourceFile File, SourcePosition Start, SourcePosition End);
```

> **Note**: A span never crosses files. A node synthesized by a transform (not present in any source) uses the span of the node it was derived from, or a sentinel; see [Open Questions](#16-open-questions).

---

## 3. Base Node & Trivia

```csharp
/// Base of all AST nodes.
public abstract record AstNode
{
    /// The source range this node covers. Set via init at construction.
    public required SourceSpan Span { get; init; }

    /// Comments/whitespace attached before this node (e.g. a Customizer label
    /// line, a license header, a section banner). Empty when none.
    public IReadOnlyList<Trivia> LeadingTrivia { get; init; } = [];

    /// Comments attached after this node on the same line
    /// (e.g. a Customizer inline annotation `// [0:100]`). Empty when none.
    public IReadOnlyList<Trivia> TrailingTrivia { get; init; } = [];
}
```

> Positional `record` parameters (e.g. `NumberLiteral(double Value, string RawText)`) define the node's syntactic fields. The base members (`Span`, `LeadingTrivia`, `TrailingTrivia`) are set with an object initializer:
> `new NumberLiteral(1.0, "1") { Span = span }`.

### Trivia

Trivia is non-semantic source text (comments, significant whitespace) that the parser attaches to the nearest node so the emitter can reproduce it. Trivia is **not** visited as part of the main tree walk.

```csharp
public abstract record Trivia
{
    public required SourceSpan Span { get; init; }
}

/// A comment. Text is the full raw comment INCLUDING delimiters
/// (`// ...` or `/* ... */`), so it can be re-emitted verbatim.
public sealed record CommentTrivia(string Text, CommentKind Kind) : Trivia;

/// Captured run of whitespace, used only to preserve blank lines between
/// statements. The emitter may collapse or honor this per its style config.
public sealed record WhitespaceTrivia(string Text) : Trivia;
```

See [§11 Customizer Representation](#11-customizer-representation) for how Customizer metadata is recovered from `CommentTrivia`.

---

## 4. Root Node

```csharp
/// The parsed contents of one .scad file, or the final bundled output.
public sealed record ScadFile(
    SourceFile Source,
    IReadOnlyList<Statement> Statements
) : AstNode;
```

---

## 5. Statements

```csharp
public abstract record Statement : AstNode;
```

### File inclusion

```csharp
/// `include <path>` — pulls in all definitions AND executes the file's
/// top-level statements at this point.
/// RawPath is the text between < and > (no angle brackets), e.g. "MCAD/gears.scad".
public sealed record IncludeStatement(string RawPath) : Statement;

/// `use <path>` — imports only module and function DEFINITIONS from the file;
/// does NOT execute its top-level statements and does NOT propagate that file's
/// own include/use.
public sealed record UseStatement(string RawPath) : Statement;
```

> Path resolution (search paths, `OPENSCADPATH`, cycle detection) is performed by the SourceLoader and recorded in a side table keyed by the statement node. The AST stores only the raw path. See [§15](#15-design-decisions--rationale).

### Definitions

```csharp
/// `module Name(Parameters) Body`. Body is typically a BlockStatement but the
/// grammar permits any single statement.
public sealed record ModuleDefinition(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    Statement Body
) : Statement;

/// `function Name(Parameters) = Body;`
public sealed record FunctionDefinition(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    Expression Body
) : Statement;
```

### Assignment

```csharp
/// `Name = Value;` at file scope or inside a block.
/// At file scope and before the first definition, these are also Customizer
/// parameters (see §11).
public sealed record AssignmentStatement(string Name, Expression Value) : Statement;
```

### Module instantiation (the workhorse)

```csharp
/// A call to a module: `Modifiers Name(Arguments) Child`.
/// Covers built-ins (cube, translate, union, ...), user modules, and the
/// statement forms of `echo(...)`, `assert(...)`, `children(...)`, `group()`.
///
/// Child encodes what follows the `)`:
///   - null                          → terminated by `;`            e.g. `cube(5);`
///   - a single ModuleInstantiation  → chained child                e.g. `translate(...) cube(5);`
///   - a BlockStatement              → braced children              e.g. `union() { a(); b(); }`
///
/// Modifiers are listed outer→inner as written; e.g. `#%cube();` → [Highlight, Background].
public sealed record ModuleInstantiation(
    IReadOnlyList<InstantiationModifier> Modifiers,
    string Name,
    IReadOnlyList<Argument> Arguments,
    Statement? Child
) : Statement;
```

### Blocks & control flow

OpenSCAD's control structures are syntactically module instantiations, but we model them as dedicated nodes because semantic analysis and inlining treat them specially.

```csharp
/// `{ Statements }`
public sealed record BlockStatement(IReadOnlyList<Statement> Statements) : Statement;

/// `if (Condition) Then` with optional `else Else`.
/// Else may itself be an IfStatement to represent `else if`.
public sealed record IfStatement(
    Expression Condition,
    Statement Then,
    Statement? Else
) : Statement;

/// `for (Bindings) Body` — Bindings iterate as a cartesian product when multiple.
public sealed record ForStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;

/// `intersection_for (Bindings) Body`
public sealed record IntersectionForStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;

/// `let (Bindings) Body` — statement form (geometry scope).
public sealed record LetStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;
```

### Edge-case statements

```csharp
/// A lone `;`. Retained for fidelity; the emitter MAY elide it.
public sealed record EmptyStatement() : Statement;

/// Deprecated `assign(Bindings) Body`. Supported for legacy input; the inliner
/// SHOULD rewrite to `let` on output. (Deferred to a later slice — see Open Questions.)
public sealed record AssignStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;
```

---

## 6. Expressions

```csharp
public abstract record Expression : AstNode;
```

### Literals

```csharp
/// RawText preserves the author's lexical form ("1", "1.0", "1e3", ".5")
/// so the emitter can round-trip it; Value is the parsed double.
public sealed record NumberLiteral(double Value, string RawText) : Expression;

/// Value is the decoded string (escapes resolved); RawText includes the
/// surrounding quotes and original escape sequences.
public sealed record StringLiteral(string Value, string RawText) : Expression;

public sealed record BooleanLiteral(bool Value) : Expression;     // true | false

public sealed record UndefLiteral() : Expression;                 // undef
```

### References

```csharp
/// A variable or function/module name reference.
/// Special variables ($fn, $fa, $fs, $t, $children, $preview, $vpr, ...) are
/// Identifiers whose Name begins with '$'. No separate node type.
public sealed record Identifier(string Name) : Expression;
```

### Collections

```csharp
/// `[Elements]`. Elements may include comprehension generators
/// (ForComprehension, IfComprehension, LetComprehension, EachExpression),
/// which are valid ONLY inside a vector context (enforced by the semantic pass).
/// An empty vector `[]` has zero Elements.
public sealed record VectorExpression(IReadOnlyList<Expression> Elements) : Expression;

/// `[Start : End]` (Step null) or `[Start : Step : End]`.
public sealed record RangeExpression(
    Expression Start,
    Expression? Step,
    Expression End
) : Expression;
```

### Operators

```csharp
public sealed record BinaryExpression(
    BinaryOperator Operator,
    Expression Left,
    Expression Right
) : Expression;

/// Prefix unary: -x, +x, !x. Note: `-5` parses as Unary(Negate, NumberLiteral 5),
/// NOT as a negative literal.
public sealed record UnaryExpression(
    UnaryOperator Operator,
    Expression Operand
) : Expression;

/// Ternary `Condition ? Then : Else`.
public sealed record ConditionalExpression(
    Expression Condition,
    Expression Then,
    Expression Else
) : Expression;

/// Author-written grouping `( Inner )`. Retained to preserve intent and to let
/// the emitter avoid recomputing parenthesization in the common case.
public sealed record ParenthesizedExpression(Expression Inner) : Expression;
```

### Access & calls

```csharp
/// `Target[Index]`
public sealed record IndexExpression(Expression Target, Expression Index) : Expression;

/// `Target.Member` — Member is "x", "y", or "z".
public sealed record MemberExpression(Expression Target, string Member) : Expression;

/// `Callee(Arguments)`. Callee is usually an Identifier, but may be any
/// expression to support immediately-invoked function literals, e.g.
/// `(function (x) x + 1)(5)`.
public sealed record FunctionCallExpression(
    Expression Callee,
    IReadOnlyList<Argument> Arguments
) : Expression;
```

### Special expression forms

```csharp
/// `let (Bindings) Body` — expression form.
public sealed record LetExpression(
    IReadOnlyList<Binding> Bindings,
    Expression Body
) : Expression;

/// `assert(Arguments) Body` — Body is the expression the assert guards.
/// Arguments are (condition) or (condition, message).
public sealed record AssertExpression(
    IReadOnlyList<Argument> Arguments,
    Expression Body
) : Expression;

/// `echo(Arguments) Body` — echoes then evaluates Body.
public sealed record EchoExpression(
    IReadOnlyList<Argument> Arguments,
    Expression Body
) : Expression;

/// Anonymous function literal: `function (Parameters) Body`.
public sealed record FunctionLiteral(
    IReadOnlyList<Parameter> Parameters,
    Expression Body
) : Expression;
```

---

## 7. List Comprehensions

Comprehension generators are modeled as `Expression` subtypes that are only legal as elements of a `VectorExpression`. This unifies the common case (`[1, 2, 3]`) with the generator case (`[1, 2, for (i=[0:3]) i, 5]`) — both are just a `VectorExpression` whose `Elements` happen to include generators. Nesting (`for (a) for (b) expr`) is represented by a generator whose `Body` is another generator.

```csharp
/// `for (Bindings) Body` inside `[...]`. Multiple bindings = cartesian product.
public sealed record ForComprehension(
    IReadOnlyList<Binding> Bindings,
    Expression Body
) : Expression;

/// `if (Condition) Then` inside `[...]`, optionally `else Else`.
/// Without Else it acts as a FILTER; with Else it selects between two yields.
public sealed record IfComprehension(
    Expression Condition,
    Expression Then,
    Expression? Else
) : Expression;

/// `let (Bindings) Body` inside `[...]`.
public sealed record LetComprehension(
    IReadOnlyList<Binding> Bindings,
    Expression Body
) : Expression;

/// `each Value` — flattens Value (a list) into the surrounding vector.
public sealed record EachExpression(Expression Value) : Expression;
```

> **Constraint**: `ForComprehension`, `IfComprehension`, `LetComprehension`, and `EachExpression` are syntactically valid only as direct or nested elements of a `VectorExpression`. The parser accepts them only in that position; the semantic analyzer emits a diagnostic if one appears elsewhere.

---

## 8. Supporting Nodes

These are `AstNode`s (they carry spans and trivia) but are neither statements nor expressions.

```csharp
/// A formal parameter in a module/function/function-literal definition.
/// DefaultValue is null when the parameter has no default.
public sealed record Parameter(string Name, Expression? DefaultValue) : AstNode;

/// An argument in a call. Name is null for positional args, set for named
/// args (`cube(size = 5)`).
public sealed record Argument(string? Name, Expression Value) : AstNode;

/// A `Name = Value` binding used by let/for/assign and their comprehension forms.
/// (Distinct from AssignmentStatement, which is a top-level/block statement.)
public sealed record Binding(string Name, Expression Value) : AstNode;
```

---

## 9. Enums

```csharp
public enum CommentKind { Line, Block }            // // ...   /* ... */

public enum InstantiationModifier
{
    Disable,     // *  — treat subtree as if commented out
    Root,        // !  — render only this subtree
    Highlight,   // #  — render highlighted (debug)
    Background   // %  — render transparent, excluded from geometry
}

public enum UnaryOperator { Negate, Plus, Not }    // -  +  !

public enum BinaryOperator
{
    // arithmetic
    Add, Subtract, Multiply, Divide, Modulo, Power,   // +  -  *  /  %  ^
    // comparison
    Less, LessEqual, Greater, GreaterEqual, Equal, NotEqual,  // <  <=  >  >=  ==  !=
    // logical
    And, Or                                            // &&  ||
}
```

---

## 10. Polymorphic Keyword Map

Several OpenSCAD keywords map to different nodes depending on syntactic context. This table is the disambiguation contract for the parser:

| Keyword | Statement context | Expression context | Inside `[ ... ]` (comprehension) |
|---|---|---|---|
| `if`   | `IfStatement` | use ternary `?:` → `ConditionalExpression` | `IfComprehension` |
| `for`  | `ForStatement` | — | `ForComprehension` |
| `intersection_for` | `IntersectionForStatement` | — | — |
| `let`  | `LetStatement` | `LetExpression` | `LetComprehension` |
| `each` | — | — | `EachExpression` |
| `assert` | `ModuleInstantiation` (name `assert`) | `AssertExpression` | — |
| `echo`   | `ModuleInstantiation` (name `echo`) | `EchoExpression` | — |
| `function` | `FunctionDefinition` (named) | `FunctionLiteral` (anonymous) | — |
| `assign` | `AssignStatement` (deprecated) | — | — |

---

## 11. Customizer Representation

The Customizer model is **not** separate AST nodes — it is an *interpretation* of trivia attached to top-level `AssignmentStatement`s. This keeps the parse tree clean while making all Customizer data recoverable.

**Recognition rules** (per the OpenSCAD manual):
- Customizer parameters are the `AssignmentStatement`s in the file's top-level scope that appear **before the first `ModuleDefinition` or `FunctionDefinition`**.
- A **section** is introduced by a block comment `/* [Section Title] */` (a `CommentTrivia` in `LeadingTrivia`). It groups subsequent parameters until the next section comment.
- `/* [Hidden] */` hides all subsequent parameters from the Customizer UI.
- A **label/description** is a line comment immediately above the assignment (`LeadingTrivia`).
- An **inline annotation** is a trailing line comment on the same line as the assignment (`TrailingTrivia`), constraining the control:
  - `// [max]` or `// [min:max]` or `// [min:max:step]` → numeric slider/spinner
  - `// [a, b, c]` → dropdown of values
  - `// [a:Label A, b:Label B]` → labeled dropdown
  - quoted-string value + `// [8]` → max length, etc.

A derived, tooling-facing projection (built by a Customizer pass, **not** part of the core AST) may look like:

```csharp
/// DERIVED, not a parse node. Produced by interpreting trivia on a top-level
/// AssignmentStatement. Lives in the tooling layer for ScadBundler Live.
public sealed record CustomizerParameter(
    string Name,
    Expression DefaultValue,
    string? Section,        // null = default/ungrouped
    string? Description,    // from the label line comment
    string? RawAnnotation,  // the raw `[ ... ]` annotation text, if any
    bool Hidden
);
```

> The bundler MUST preserve all Customizer trivia by default (`--preserve-comments` is on by default per [UX.md](UX.md)). `--minify` is the only mode permitted to drop it.

---

## 12. Concrete Node Index

Every concrete node, grouped. This list is exhaustive — it doubles as the set of visitor methods (§13) and the set of types a `switch` must handle.

**Root (1):** `ScadFile`

**Statements (13):** `IncludeStatement`, `UseStatement`, `ModuleDefinition`, `FunctionDefinition`, `AssignmentStatement`, `ModuleInstantiation`, `BlockStatement`, `IfStatement`, `ForStatement`, `IntersectionForStatement`, `LetStatement`, `EmptyStatement`, `AssignStatement`

**Expressions (20):** `NumberLiteral`, `StringLiteral`, `BooleanLiteral`, `UndefLiteral`, `Identifier`, `VectorExpression`, `RangeExpression`, `BinaryExpression`, `UnaryExpression`, `ConditionalExpression`, `ParenthesizedExpression`, `IndexExpression`, `MemberExpression`, `FunctionCallExpression`, `LetExpression`, `AssertExpression`, `EchoExpression`, `FunctionLiteral`, `ForComprehension`, `IfComprehension` *(plus `LetComprehension`, `EachExpression`)*

**Supporting (3):** `Parameter`, `Argument`, `Binding`

**Trivia (2):** `CommentTrivia`, `WhitespaceTrivia`

> Total concrete node types: **41** (1 root + 13 statements + 22 expressions + 3 supporting + 2 trivia). The four comprehension generators are counted among expressions.

---

## 13. Visitor Pattern

A generic visitor with one method per concrete node. Because the hierarchy is closed and sealed, this can be hand-written or **source-generated** (preferred — see Constitution's stance on source generators) to stay in sync as nodes are added.

```csharp
public interface IAstVisitor<out TResult>
{
    TResult Visit(ScadFile node);

    // statements
    TResult Visit(IncludeStatement node);
    TResult Visit(UseStatement node);
    TResult Visit(ModuleDefinition node);
    TResult Visit(FunctionDefinition node);
    TResult Visit(AssignmentStatement node);
    TResult Visit(ModuleInstantiation node);
    TResult Visit(BlockStatement node);
    TResult Visit(IfStatement node);
    TResult Visit(ForStatement node);
    TResult Visit(IntersectionForStatement node);
    TResult Visit(LetStatement node);
    TResult Visit(EmptyStatement node);
    TResult Visit(AssignStatement node);

    // expressions
    TResult Visit(NumberLiteral node);
    TResult Visit(StringLiteral node);
    TResult Visit(BooleanLiteral node);
    TResult Visit(UndefLiteral node);
    TResult Visit(Identifier node);
    TResult Visit(VectorExpression node);
    TResult Visit(RangeExpression node);
    TResult Visit(BinaryExpression node);
    TResult Visit(UnaryExpression node);
    TResult Visit(ConditionalExpression node);
    TResult Visit(ParenthesizedExpression node);
    TResult Visit(IndexExpression node);
    TResult Visit(MemberExpression node);
    TResult Visit(FunctionCallExpression node);
    TResult Visit(LetExpression node);
    TResult Visit(AssertExpression node);
    TResult Visit(EchoExpression node);
    TResult Visit(FunctionLiteral node);
    TResult Visit(ForComprehension node);
    TResult Visit(IfComprehension node);
    TResult Visit(LetComprehension node);
    TResult Visit(EachExpression node);

    // supporting
    TResult Visit(Parameter node);
    TResult Visit(Argument node);
    TResult Visit(Binding node);
}

/// Accept dispatches to the matching Visit overload.
public abstract record AstNode
{
    public abstract TResult Accept<TResult>(IAstVisitor<TResult> visitor);
}
```

> Most transforms (inliner, renamer, minifier) are better written as a **rewriting visitor** that returns `AstNode` and rebuilds changed subtrees with `with`. A `Unit`/`void` variant or a `record`-returning base rewriter should be provided. Exact rewriter base class is a Slice-2/3 implementation detail; the `IAstVisitor<TResult>` contract above is the fixed interface.

---

## 14. Worked Examples

Notation: `NodeName { field = value, ... }`; lists in `[ ... ]`; `Span`/trivia omitted for brevity.

### 14.1 Module instantiation with named arg
```scad
cube([10, 20, 30], center = true);
```
```
ModuleInstantiation {
  Modifiers = [],
  Name = "cube",
  Arguments = [
    Argument { Name = null, Value = VectorExpression { Elements = [
      NumberLiteral { Value=10, RawText="10" },
      NumberLiteral { Value=20, RawText="20" },
      NumberLiteral { Value=30, RawText="30" } ] } },
    Argument { Name = "center", Value = BooleanLiteral { Value = true } }
  ],
  Child = null            // terminated by ';'
}
```

### 14.2 Transform chain (children)
```scad
translate([0, 0, 5]) rotate([0, 0, 45]) cube(10);
```
```
ModuleInstantiation { Name="translate", Arguments=[ Argument{ Value=VectorExpression[0,0,5] } ],
  Child = ModuleInstantiation { Name="rotate", Arguments=[ Argument{ Value=VectorExpression[0,0,45] } ],
    Child = ModuleInstantiation { Name="cube", Arguments=[ Argument{ Value=NumberLiteral 10 } ],
      Child = null } } }
```

### 14.3 Module definition with default + braced body
```scad
module washer(d = 5, h = 2) {
    cylinder(d = d, h = h);
}
```
```
ModuleDefinition {
  Name = "washer",
  Parameters = [
    Parameter { Name="d", DefaultValue = NumberLiteral 5 },
    Parameter { Name="h", DefaultValue = NumberLiteral 2 }
  ],
  Body = BlockStatement { Statements = [
    ModuleInstantiation { Name="cylinder", Arguments=[
      Argument{ Name="d", Value=Identifier "d" },
      Argument{ Name="h", Value=Identifier "h" } ], Child=null }
  ] }
}
```

### 14.4 Function definition with ternary
```scad
function clamp(x, lo, hi) = x < lo ? lo : (x > hi ? hi : x);
```
```
FunctionDefinition {
  Name = "clamp",
  Parameters = [ Parameter{Name="x"}, Parameter{Name="lo"}, Parameter{Name="hi"} ],
  Body = ConditionalExpression {
    Condition = BinaryExpression { Operator=Less, Left=Identifier "x", Right=Identifier "lo" },
    Then = Identifier "lo",
    Else = ParenthesizedExpression { Inner = ConditionalExpression {
      Condition = BinaryExpression { Operator=Greater, Left=Identifier "x", Right=Identifier "hi" },
      Then = Identifier "hi",
      Else = Identifier "x" } }
  }
}
```

### 14.5 include / use
```scad
include <BOSL2/std.scad>
use <helpers.scad>
```
```
IncludeStatement { RawPath = "BOSL2/std.scad" }
UseStatement     { RawPath = "helpers.scad" }
```

### 14.6 List comprehension with filter
```scad
squares = [for (i = [0 : 5]) if (i % 2 == 0) i * i];
```
```
AssignmentStatement {
  Name = "squares",
  Value = VectorExpression { Elements = [
    ForComprehension {
      Bindings = [ Binding { Name="i", Value = RangeExpression {
        Start=NumberLiteral 0, Step=null, End=NumberLiteral 5 } } ],
      Body = IfComprehension {
        Condition = BinaryExpression { Operator=Equal,
          Left = BinaryExpression { Operator=Modulo, Left=Identifier "i", Right=NumberLiteral 2 },
          Right = NumberLiteral 0 },
        Then = BinaryExpression { Operator=Multiply, Left=Identifier "i", Right=Identifier "i" },
        Else = null }            // filter form
    }
  ] }
}
```

### 14.7 Customizer parameter with trivia
```scad
/* [Dimensions] */
// Outer diameter of the part
diameter = 20; // [5:50]
```
```
AssignmentStatement {
  Name = "diameter",
  Value = NumberLiteral { Value=20, RawText="20" },
  LeadingTrivia = [
    CommentTrivia { Kind=Block, Text="/* [Dimensions] */" },
    CommentTrivia { Kind=Line,  Text="// Outer diameter of the part" }
  ],
  TrailingTrivia = [
    CommentTrivia { Kind=Line,  Text="// [5:50]" }
  ]
}
```
The Customizer pass derives:
`CustomizerParameter { Name="diameter", Section="Dimensions", Description="Outer diameter of the part", RawAnnotation="[5:50]", Hidden=false }`.

### 14.8 if / else if / else
```scad
if (n == 0) a();
else if (n == 1) b();
else c();
```
```
IfStatement {
  Condition = BinaryExpression{ Equal, Identifier "n", NumberLiteral 0 },
  Then = ModuleInstantiation{ Name="a", Child=null },
  Else = IfStatement {
    Condition = BinaryExpression{ Equal, Identifier "n", NumberLiteral 1 },
    Then = ModuleInstantiation{ Name="b", Child=null },
    Else = ModuleInstantiation{ Name="c", Child=null }
  }
}
```

---

## 15. Design Decisions & Rationale

These choices are fixed for cross-implementation consistency (one of the AI-comparison goals). Deviations should be raised as Open Questions, not made silently.

1. **Comprehension generators are `Expression` subtypes, legal only inside vectors.** Unifies `[1,2,3]` and `[for(...) ...]` under one `VectorExpression` and makes nesting natural. The alternative (a separate `VectorElement` union) complicates the overwhelmingly common plain-list case.
2. **Control flow (`if`/`for`/`let`) are dedicated statement nodes**, not generic `ModuleInstantiation`s, even though OpenSCAD's grammar treats them as module instantiations. Dedicated nodes give the semantic analyzer and inliner clean, typed access to conditions/bindings.
3. **`echo`/`assert`/`children` as statements ARE `ModuleInstantiation`s** (no dedicated nodes). They behave like ordinary module calls at statement level; their *expression* forms (`echo(...) x`, `assert(...) x`) get dedicated nodes because they wrap a value.
4. **Raw text retained on numbers and strings.** Preserves `1.0` vs `1`, scientific notation, and exact escapes — required for faithful round-tripping and to avoid surprising diffs in bundled output.
5. **`ParenthesizedExpression` is kept** rather than re-derived from precedence. Preserves author intent (a stated value) and lets the emitter avoid a class of precedence bugs. The emitter still inserts parentheses where a transform makes them necessary.
6. **AST is parse-only; resolution lives in side tables.** Include resolution, symbol binding, and dedup decisions are pass outputs keyed by node, not fields on nodes. Keeps the tree immutable, cacheable, and reusable across the CLI and the future Live web service.
7. **Trivia carries comments; the emitter owns formatting.** Only comments (incl. Customizer/license) and optional blank-line markers are preserved structurally. Indentation/brace style is regenerated by the emitter per its config — this is why we are a *bundler*, not a formatter.
8. **`Binding` vs `AssignmentStatement` are distinct types** despite identical shape, because they occupy different grammatical positions (let/for binding vs. statement) and visitors/analyzers treat them differently.

---

## 16. Open Questions

Resolve these during Slice 1–3 implementation; each needs a decision recorded back here.

1. **Synthetic-node spans.** What `SourceSpan` do nodes created by transforms (e.g. a renamed identifier, an `assign`→`let` rewrite) carry? Options: span of the origin node; a dedicated `SourceFile.Synthetic` sentinel; nullable span. *Leaning:* reuse origin span + a separate "synthesized" side-table flag, to keep `Span` non-null.
2. **Blank-line fidelity.** Is `WhitespaceTrivia` actually needed, or is "preserve at most one blank line between top-level statements" a sufficient emitter rule with no trivia? *Leaning:* emitter rule; drop `WhitespaceTrivia` if Slice 6 confirms it's unnecessary.
3. **`assign` support.** Implement `AssignStatement` now or reject deprecated `assign` with a diagnostic and a suggested `let` rewrite? *Leaning:* parse it, warn, rewrite on emit — but may defer past v1.
4. **`use` of a file's special variables / top-level `$fn`.** Confirm `use` excludes top-level assignments including special-variable defaults (manual is subtle here). Affects the inliner more than the AST, but worth a parser test.
5. **Number representation.** Is `double` sufficient, or do we need to preserve integer-ness for emit? `RawText` covers emit fidelity, so `double` should suffice — confirm against BOSL2 corpus.
6. **Member access surface.** Confirm `.x/.y/.z` is the complete set (no arbitrary `.member`). If arbitrary, `MemberExpression.Member` stays `string` (already does); if fixed, add validation in the semantic pass.

---

## 17. Suggested File Layout

For the core library (`src/ScadBundler.Core/Ast/`), to aid the one-shot implementer:

```
Ast/
  SourceFile.cs            // SourceFile, SourcePosition, SourceSpan
  Trivia.cs                // Trivia, CommentTrivia, WhitespaceTrivia, CommentKind
  AstNode.cs               // AstNode base + Accept
  ScadFile.cs              // root
  Statements.cs            // all Statement records
  Expressions.cs           // all Expression records (incl. comprehensions)
  Support.cs               // Parameter, Argument, Binding
  Enums.cs                 // InstantiationModifier, Unary/BinaryOperator
  IAstVisitor.cs           // visitor interface (consider source-generating)
```

> Grouping by category (not one-file-per-record) keeps the tree navigable and matches the "lean, clean" value. Split only if a file grows unwieldy.

---

*Cross-references: [Constitution.md](Constitution.md) (principles), [Parser-Planning.md](Parser-Planning.md) (precedence, parsing strategy), [Grammar-References.md](Grammar-References.md) (source grammars), [Spec.md](Spec.md) (`include`/`use` semantics), [Development-Slices.md](Development-Slices.md) (slice plan).*
