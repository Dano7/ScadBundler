# Parser Planning for ScadBundler

## Overall Parser Architecture
- Hand-written recursive descent.
- Precedence climbing / Pratt parser for expressions.
- Immutable record-based AST.
- Diagnostic collection for errors.

## Key References
- Use Grammar-References.md
- Study BelfrySCAD AST for C# record inspiration.

## AST Node Hierarchy (Draft)
- AstNode (base)
  - Statement
    - ModuleDeclaration
    - FunctionDeclaration
    - VariableAssignment
    - IncludeUseStatement
    - ...
  - Expression (with subclasses)
  - Literal, Identifier, etc.

## Error Handling Strategy
- Collect diagnostics instead of throwing early.
- Context-aware messages.

## Slice Integration
- Slice 2 will implement core rules based on RapCAD BNF.