# Scrapscript C# Implementation TODO

## Status: 261/261 tests passing

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

- [x] **Comparison operators** — `==`, `!=`, `<`, `>`, `<=`, `>=`; return `bool` (`#true`/`#false`)
- [x] **Division operator** — `/` for int (truncating) and float
- [x] **Duplicate literal arms** — redundant `| 0 -> … | 0 -> …` and `| "a" -> … | "a" -> …` detection
- [x] **REPL session environment** — bindings (`name = expr`) persist across lines; expressions evaluated against accumulated session

- [x] **Modulo operator** — `%` for int and float
- [x] **Row polymorphism** — `r.field` constrains `r` to `{ field: 't | ... }`; multiple field accesses merge via row variables; open records unify correctly with closed record literals

## Next

- [ ] **Annotation-guided inference for recursive bindings** — use declared type as placeholder
- [ ] **Content addressability** — SHA1 hashing, flat binary format, hash refs (`$sha1~~…`)
- [x] **Negation operator** — `NegExpr` AST node; `-x` and `-(f x)` work for int and float
- [x] **`list/map`, `list/filter`, `list/fold`** — polymorphic higher-order list builtins with full type inference
