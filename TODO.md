# Scrapscript C# Implementation TODO

## Status: 533/533 tests passing

## Completed

- [x] Lexer — `Token.cs`, `Lexer.cs`
- [x] AST + Parser — `Ast.cs`, `Parser.cs`
- [x] Evaluator + pattern matching — `Evaluator.cs`, `ScrapValue.cs`, `ScrapEnv.cs`
- [x] Built-ins — `Builtins.cs`: to-float, round, ceil, floor, bytes/to-utf8-text, list/first, list/length, list/repeat, list/map, list/filter, list/fold, list/reverse, list/sort, list/zip, list/range, list/flatten, text/length, text/repeat, text/trim, text/split, text/at, text/chars, text/slice, text/contains, text/starts-with, text/ends-with, text/to-upper, text/to-lower, maybe/default, string/join, dict/get, dict/keys, dict/set, abs, min, max, int/to-text, float/to-text, text/to-int
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
- [x] **Platforms** — `IPlatform` interface with compile-time type contracts (`InputType`/`OutputType`); `ConsolePlatform` (`run --platform=console`, programs are `() -> text`); `HttpPlatform` (`run --platform=http`, programs are `text -> http-response` where `http-response : #send { status = int, body = text }`); platform dispatch loop pattern established

## Towards Elm-style "no runtime errors"

These are the remaining gaps between our type safety and Elm's guarantee. In Elm, if it compiles it won't throw at runtime. These items close that gap:

- [x] **`TRecord` in the type system** — `HttpPlatform` now registers `#send { status : int, body : text }` using a real `TRecord` payload; `PlatformTypes.RuntimeCheck` handles `TRecord` by checking each field; `#send { body = "hi" }` (missing `status`) is now a compile-time error.

- [x] **Exhaustiveness for anonymous variants** — `ConvertTypeExpr(VariantType)` now registers a synthetic `TypeDef` (`$anon_...`) instead of returning an opaque hole. Inline variant annotations like `f : ((#foo #bar) -> int) = | #foo -> 1` now catch missing arms at compile time.

- [ ] **Remove defensive runtime type checks from the evaluator** — `Evaluator.cs` still has many `throw new ScrapTypeError(...)` guards for cases that should be unreachable if the type checker is doing its job (e.g. applying a non-function, adding non-numbers). These are currently needed because the type checker has gaps. Once `TRecord` and exhaustiveness are complete, audit these and remove the ones the type checker now covers — making the evaluator prove, not just hope, they're unreachable.

- [ ] **Seal the `typeCheck: false` escape hatch** — make `typeCheck` internal or remove it from the public `Eval` API. Platforms already call `CheckAgainstPlatform` then `Eval(typeCheck: false)` (correct: don't type-check twice). The risk is external callers skipping type checking. Fix: remove the parameter from the public API and have the type checker run unconditionally, with platforms using a separate internal entry point.

## Possible next steps

- [ ] **HTTP platform: richer request input** — currently the handler only receives the path as a `ScrapText`. Extend to pass a record `{ path = "...", query = { key = "val", ... }, body = "..." }` so handlers can read query params (`?foo=bar`) and POST form/JSON bodies without changing the platform contract
- [ ] **HTTP platform: effect loop** — implement the `#query { sql, then }` effect: after eval, if the result is `#query`, run the SQL against a database, apply `then` to the result rows, and loop until a `#send` terminal is reached. This is the full platform dispatch loop described in the original spec.
- [ ] **Platform/network scrapyard** — push/pull over HTTP to a remote yard
- [ ] **More builtins** — additional built-ins as needed
