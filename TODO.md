# Scrapscript C# Implementation TODO

## Progress

- [x] **Step 0: Solution scaffold** — `Scrapscript.sln`, `Scrapscript.Core`, `Scrapscript.Repl`, `Scrapscript.Tests`
- [x] **Step 1: Lexer** — `Token.cs`, `Lexer.cs`, `LexerTests.cs` (all pass)
- [x] **Step 2: AST + Parser** — `Ast.cs`, `Parser.cs`, `ParserTests.cs` (all pass)
- [x] **Step 3: ScrapValue types** — `ScrapValue.cs`, `ScrapEnv.cs`
- [x] **Step 4: Evaluator core** — `Evaluator.cs`, `EvalTests.cs` (all pass)
- [x] **Step 5: Pattern matching** — implemented in `Evaluator.cs` (tested via EvalTests)
- [x] **Step 6: Built-in functions** — `Builtins.cs` (to-float, round, ceil, floor, bytes/to-utf8-text, list/first, list/length, text/length, text/repeat, list/repeat, maybe/default, string/join, dict/get)
- [x] **Step 7: REPL** — `Program.cs` (basic read-eval-print loop with multi-line support)
- [ ] **Step 8: Content addressability** (stretch) — SHA1 hashing, flat binary format, hash refs

## Test Status

```
Passed!  - Failed: 0, Passed: 119, Skipped: 0, Total: 119
```

## Key Design Decisions

- Lambda bodies / case arm bodies use `ParsePipe()` (not `ParseWhere()`) so `;` at the outer level is not consumed. Nested where-clauses inside lambdas must be parenthesized: `(x ; x = 1)`.
- Where-clause binding evaluation uses a retry loop for forward references (mutual recursion support).
- Negative integer literals: lexer emits `[Minus, Int]`; parser handles unary minus in `ParseAtom`.
- `ScrapList` overrides `Equals` for structural (sequence) comparison of `ImmutableList<ScrapValue>`.

## Spec Examples Covered

From section 14 of scrapscript-spec.txt:
- [x] Hello world: `"hello world"` → `"hello world"`
- [x] Arithmetic: `1 + 1` → `2`, `3 * 5` → `15`
- [x] Text concat: `"hello" ++ " " ++ "world"` → `"hello world"`
- [x] Where-clauses: `a + b + c ; a = 1 ; b = 2 ; c = 3` → `6`
- [x] Functions: `f 1 2 ; f = a -> b -> a + b` → `3`
- [x] Pattern matching: `f "b" ; f = | "a" -> 1 | "b" -> 2 | _ -> 0` → `2`
- [x] Function composition: `(f >> id >> g) 7` → `"kitten"`
- [x] Bytes: `bytes/to-utf8-text ~~aGVsbG8gd29ybGQ=` → `"hello world"`
- [x] Bytes with append: `bytes/to-utf8-text <| ~~aGVsbG8gd29ybGQ= +< ~21` → `"hello world!"`

## Next Steps (if continuing)

1. **Improve REPL**: persist bindings across lines (session environment)
2. **Text interpolation**: handle backtick interpolation in strings
3. **Type checking**: enforce list homogeneity, record field type consistency
4. **Content addressability**: SHA1 hash of values, `$sha1~~...` references
5. **Float display**: improve float formatting
6. **More builtins**: `list/repeat`, `dict/get` with int keys, etc.
