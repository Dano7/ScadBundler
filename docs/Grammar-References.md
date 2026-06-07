# Grammar References for ScadBundler

## Primary Grammar Sources

### 1. RapCAD openscad.bnf (Recommended starting point)
- URL: https://raw.githubusercontent.com/GilesBathgate/RapCAD/master/doc/openscad.bnf
- Description: Clean BNF grammar covering core OpenSCAD syntax.
- Usage: Reference for recursive descent rule structure.

### 2. BelfrySCAD openscad_parser (Most comprehensive)
- Repo: https://github.com/BelfrySCAD/openscad_parser
- PEG grammar (~420 rules) + full Python AST.
- Excellent for AST node shapes and real-world semantics.

### 3. tree-sitter-openscad
- Repo: https://github.com/openscad/tree-sitter-openscad (or forks)
- Modern grammar.js definition.

### 4. Official Language Manual
- https://en.wikibooks.org/wiki/OpenSCAD_User_Manual/The_OpenSCAD_Language
- Prose reference with examples, semantics, Customizer details.

### 5. OpenSCAD Cheatsheet
- https://openscad.org/cheatsheet/

## C++ Parser Reference
- Official repo: https://github.com/openscad/openscad
- Use for behavior verification only (integration tests), not direct code porting.

## Notes
- Use these for parser rule design, test cases, and edge case coverage.
- AI assistants can work directly with BNF/PEG snippets.