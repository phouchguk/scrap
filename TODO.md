# Scrapscript C# Implementation TODO

## Status: 299/299 tests passing

## Completed

- [x] Lexer — `Token.cs`, `Lexer.cs`
- [x] AST + Parser — `Ast.cs`, `Parser.cs`
- [x] Evaluator + pattern matching — `Evaluator.cs`, `ScrapValue.cs`, `ScrapEnv.cs`
- [x] Built-ins — `Builtins.cs`: to-float, round, ceil, floor, bytes/to-utf8-text, list/first, list/length, list/repeat, list/map, list/filter, list/fold, list/reverse, list/sort, list/zip, text/length, text/repeat, text/trim, text/split, text/to-upper, text/to-lower, maybe/default, string/join, dict/get, abs, min, max
- [x] REPL — `Program.cs`
- [x] Hindley-Milner type checker — `TypeChecker/`: ScrapType, Substitution, TypeEnv, TypeInferrer, BuiltinTypes
  - Inference for all expression forms
  - Let-polymorphism (generalization at where-bindings)
  - Named/generic types (`maybe`, `result`, `bool`, user-defined)
  - Exhaustiveness checking for variant matches
  - Redundant arm detection
  - Record spread constraint
  - Type annotation enforcement (`: type` on bindings)
  - **Annotation-guided inference for recursive bindings** — declared type used as placeholder, so recursive self-references see the precise annotated type during body inference
- [x] Comparison operators — `==`, `!=`, `<`, `>`, `<=`, `>=`; return `bool` (`#true`/`#false`)
- [x] Division operator — `/` for int (truncating) and float
- [x] Modulo operator — `%` for int and float
- [x] Negation operator — `NegExpr` AST node; `-x` and `-(f x)` work for int and float
- [x] Duplicate literal arms — redundant `| 0 -> … | 0 -> …` and `| "a" -> … | "a" -> …` detection
- [x] REPL session environment — bindings (`name = expr`) persist across lines; expressions evaluated against accumulated session
- [x] Row polymorphism — `r.field` constrains `r` to `{ field: 't | ... }`; multiple field accesses merge via row variables; open records unify correctly with closed record literals
- [x] Content addressability — flat binary format (msgpack-compatible), SHA1 hashing, hash refs (`$sha1~~…`), local scrapyard at `~/.scrap/yard/`

## Possible next steps

- [ ] **Platform/network scrapyard** — push/pull over HTTP to a remote yard
- [ ] **`scrapscript eval` subcommand** — evaluate an expression (with hash ref support) from the CLI without entering the REPL
- [ ] **Type inference for hash refs** — look into the yard to infer the stored value's type, rather than treating `$sha1~~…` as opaque
- [ ] **More builtins** — `int/to-text`, `float/to-text`, `text/contains`, `text/starts-with`, `list/range`, `dict/keys`, etc.
