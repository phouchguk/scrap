# Scrapscript C# Implementation TODO

## Status: 340/340 tests passing

## Completed

- [x] Lexer — `Token.cs`, `Lexer.cs`
- [x] AST + Parser — `Ast.cs`, `Parser.cs`
- [x] Evaluator + pattern matching — `Evaluator.cs`, `ScrapValue.cs`, `ScrapEnv.cs`
- [x] Built-ins — `Builtins.cs`: to-float, round, ceil, floor, bytes/to-utf8-text, list/first, list/length, list/repeat, list/map, list/filter, list/fold, list/reverse, list/sort, list/zip, list/range, text/length, text/repeat, text/trim, text/split, text/at, text/chars, text/slice, text/contains, text/starts-with, text/to-upper, text/to-lower, maybe/default, string/join, dict/get, abs, min, max, int/to-text, float/to-text, text/to-int
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
- [x] **`scrapscript eval` subcommand** — evaluate an expression from the CLI with full scrapyard and map support; `--t=` flag for point-in-time map reads
- [x] **Hash-ref type inference** — type checker looks up hash refs in the scrapyard and infers the stored value's type rather than treating them as opaque
- [x] **Scrap maps** — named versioned bindings (`name@version`) stored in `~/.scrap/map/`; `map init`, `map commit`, `map history` CLI commands; `MapRef` AST node and evaluator support
- [x] **Platforms** — `IPlatform` interface, `ConsolePlatform` (`run --platform=console`), `HttpPlatform` (`run --platform=http`); `ScrapInterpreter.Apply`
- [x] **Do notation** — `do x <- e, y <- e, final` sugar desugaring to `bind` calls; `<-` lexer token; no new AST node needed

## Possible next steps

- [ ] **`bind` availability for `do` notation** — currently `bind` must be defined manually in every program that uses `do`. Two options to decide between:
  - **Builtin `bind`**: hardcoded for `#pure` (unwrap) and terminals (`#ok`/`#notfound`/`#error` short-circuit); not extensible to new effect tags
  - **Platform-injected `bind`**: each platform pre-loads `bind` (and helpers like `query`) into the interpreter env before eval, tuned to its own effect set; user program has no boilerplate
  - Could do both: builtin handles `#pure`/terminals, platforms shadow it with a richer version for their specific effects


- [ ] **HTTP platform: richer request input** — currently the handler only receives the path as a `ScrapText`. Extend to pass a record `{ path = "...", query = { key = "val", ... }, body = "..." }` so handlers can read query params (`?foo=bar`) and POST form/JSON bodies without changing the platform contract
- [ ] **HTTP platform: SQL execution** — implement the `#query` effect in `HttpPlatform` (or a new `HttpDbPlatform`): after eval the dispatch loop checks for `#query { sql, then }`, runs the query against a real database (e.g. SQLite via `Microsoft.Data.Sqlite`), converts the result row to a Scrapscript record, and calls `then` — repeating until a terminal variant is reached. Connects naturally with `do` notation and the platform-injected `bind` decision above.
- [ ] **Platform/network scrapyard** — push/pull over HTTP to a remote yard
- [ ] **More builtins** — `dict/keys`, `list/flatten`, `text/ends-with`, etc.
