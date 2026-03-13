# Scrapscript C# Implementation TODO

## Status: 187/187 tests passing

## Completed

- [x] Lexer — `Token.cs`, `Lexer.cs`
- [x] AST + Parser — `Ast.cs`, `Parser.cs`
- [x] Evaluator + pattern matching — `Evaluator.cs`, `ScrapValue.cs`, `ScrapEnv.cs`
- [x] Built-ins — `Builtins.cs`: to-float, round, ceil, floor, bytes/to-utf8-text, list/first, list/length, list/repeat, text/length, text/repeat, maybe/default, string/join, dict/get
- [x] REPL — `Program.cs`
- [x] Hindley-Milner type checker — `TypeChecker/`: ScrapType, Substitution, TypeEnv, TypeInferrer, BuiltinTypes
  - Inference for all expression forms
  - Let-polymorphism (generalization at where-bindings)
  - Named/generic types (`maybe`, `result`, `bool`, user-defined)
  - Exhaustiveness checking for variant matches
  - Redundant arm detection
  - Record spread constraint
  - Type annotation enforcement (`: type` on bindings)

## Next

- [ ] **Comparison operators** — `==`, `!=`, `<`, `>`, `<=`, `>=`
  - Lex, parse (below arithmetic, above pipe), eval (returns `#true`/`#false`)
  - Type check: both sides same type → `bool`; ordering ops require int/float/text
  - Bool exhaustiveness (matching on `#true`/`#false`) already works via existing TypeDef
- [ ] **Division operator** — `/` (int and float)
- [ ] **Duplicate literal arms** — redundant `| 0 -> … | 0 -> …` detection
- [ ] **Row polymorphism for records** — `r.field` on unknown record constrains `r` rather than returning fresh type
- [ ] **Annotation-guided inference for recursive bindings** — use declared type as placeholder
- [ ] **Content addressability** — SHA1 hashing, flat binary format, hash refs (`$sha1~~…`)
- [ ] **REPL improvements** — persist bindings across lines (session environment)
