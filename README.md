# Scrapscript — C# Implementation

A from-scratch implementation of [Scrapscript](https://scrapscript.org) in C# / .NET 10.

---

## What is Scrapscript?

Scrapscript is a small, purely functional language with a radical idea at its core: every expression is a **content-addressable scrap**. Values are serialized to a canonical binary format, SHA1-hashed, and can be stored and retrieved by that hash. The same value always has the same hash, everywhere, forever.

The syntax is clean and expression-oriented — no statements, no mutation, no side effects. Where-clauses put the headline first and the definitions after, like a mathematical proof:

```
a + b + c
; a = 1
; b = 2
; c = 3
```

## Features

### Language
- **All primitive types** — `int`, `float`, `text`, `bytes`, `()` (hole)
- **Lists** — `[1, 2, 3]`, cons `>+`, append `+<`, concat `++`
- **Records** — `{ x = 1, y = 2 }`, field access `.x`, spread `{ ..base, x = 3 }`
- **Variants** — `#true`, `#false`, `#just 42`, user-defined tagged unions
- **Type definitions** — `shape : #circle float #rect float float`
- **Functions** — curried lambdas `a -> b -> a + b`
- **Pattern matching** — case functions with int, text-prefix, list-cons, record, and variant patterns
- **Where-clauses** — scoped bindings with mutual recursion support
- **Operators** — `+`, `-`, `*`, `/`, `%`, `++`, `+<`, `>+`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `|>`, `<|`, `>>`
- **Function composition** — `f >> g`
- **Recursive functions** — including mutual recursion across where-bindings
- **Do notation** — `do x <- e, y <- e, final` desugars to `bind` calls for monadic sequencing

### Type System
A full **Hindley-Milner type checker** (Algorithm W) runs before evaluation:
- Type inference for all expression forms
- Let-polymorphism — `id` can be used at multiple types in the same scope
- Named generic types — `maybe`, `result`, `bool`, user-defined parameterized types
- Row polymorphism — record field access constrains open record types
- Type annotation enforcement — `: type` on bindings is checked against the inferred type
- Exhaustiveness checking for variant pattern matches
- Redundant arm detection
- Annotation-guided inference for recursive bindings

### JavaScript Compiler
Compiles Scrapscript to JavaScript via an AST-walking emitter:
- Integers compile to BigInt (`42n`), floats to JS numbers
- Where-clauses compile to IIFEs with topological sort for mutual recursion
- Variants compile to `{_tag, _val}` objects
- Pattern matching compiles to JS conditionals and destructuring
- Full runtime embedded in output (all builtins included)
- `ScrapInterpreter.CompileToJs(src)` — entry point

```sh
dotnet run --project Scrapscript.Repl -- compile 'list/map (n -> n * n) [1,2,3]'
# [1n, 4n, 9n]   (via node)
```

### Content Addressability
- **Flat binary encoding** — msgpack-compatible canonical serialization for all value types
- **SHA1 hashing** — deterministic hash refs of the form `sha1~~<40 hex chars>`
- **Local scrapyard** — filesystem store at `~/.scrap/yard/` with git-style sharding
- **Hash ref evaluation** — `$sha1~~...` expressions resolved at runtime from the yard
- **Hash-ref type inference** — the type checker looks up stored values and infers their types

### Scrap Maps
Named, versioned bindings stored in `~/.scrap/map/`:
- `name@version` syntax — `MapRef` AST node, evaluated against the local map store
- `map init`, `map commit`, `map history` CLI commands
- Point-in-time reads via `--t=` flag on `eval`

### Platforms
An `IPlatform` interface lets Scrapscript programs drive effectful interactions:
- **`ConsolePlatform`** — `run --platform=console`, handles `#print` / `#read` effects
- **`HttpPlatform`** — `run --platform=http`, serves a Scrapscript function as an HTTP handler
- The platform dispatch loop repeatedly evaluates the program's effect variants until a terminal value is reached

### Builtins
`abs`, `min`, `max`, `to-float`, `round`, `ceil`, `floor`,
`int/to-text`, `float/to-text`,
`list/first`, `list/length`, `list/map`, `list/filter`, `list/fold`,
`list/repeat`, `list/reverse`, `list/sort`, `list/zip`, `list/range`, `list/flatten`,
`text/length`, `text/repeat`, `text/trim`, `text/split`, `text/at`, `text/chars`,
`text/slice`, `text/contains`, `text/starts-with`, `text/ends-with`,
`text/to-upper`, `text/to-lower`, `text/to-int`,
`string/join`, `bytes/to-utf8-text`,
`maybe/default`,
`dict/get`, `dict/set`, `dict/keys`

---

## Quick Tour

```
-- Arithmetic and where-clauses
3 * 5
-- 15

result
; a = 10
; b = 7
; result = a - b
-- 3

-- Curried functions and partial application
add5 10
; add  = a -> b -> a + b
; add5 = add 5
-- 15

-- Pattern matching
describe 3
; describe = | 0 -> "none" | 1 -> "one" | _ -> "many"
-- "many"

-- Text prefix patterns
greet "hello Alice"
; greet = | "hello " ++ name -> "hi " ++ name | _ -> "?"
-- "hi Alice"

-- Recursive list patterns
sum [1, 2, 3, 4, 5]
; sum = | [] -> 0 | h >+ t -> h + sum t
-- 15

-- Records
point.x + point.y
; point = { x = 3, y = 4 }
-- 7

-- Variants and tagged unions
describe (#just 42)
; describe = | #nothing -> "empty" | #just n -> "got " ++ int/to-text n
-- "got 42"

-- Higher-order functions
list/map (n -> n * n) [1, 2, 3, 4, 5]
-- [1, 4, 9, 16, 25]

list/filter (n -> n % 2 == 0) [1, 2, 3, 4, 5]
-- [2, 4]

list/fold (acc -> n -> acc + n) 0 [1, 2, 3, 4, 5]
-- 15

-- Mutual recursion
list/map even [0, 1, 2, 3, 4]
; even = | 0 -> #true  | n -> odd  (n - 1)
; odd  = | 0 -> #false | n -> even (n - 1)
-- [#true, #false, #true, #false, #true]

-- Function composition
(double >> add1) 5
; double = n -> n * 2
; add1   = n -> n + 1
-- 11

-- Do notation
do
  x <- #pure 10,
  y <- #pure 32,
  #pure (x + y)
-- #pure 42
```

### Type checker in action

```
-- Infers types
1 + 2          -- int
3.14           -- float
"hello"        -- text
x -> x         -- ('t0 -> 't0)
x -> x + 1    -- (int -> int)
[1, 2, 3]     -- list(int)

-- Catches mistakes before running
1 + 1.0        -- Type error: cannot unify int and float
[1, "a"]       -- Type error: heterogeneous list
```

### Content-addressable scrapyard

```sh
-- Push a value into the local yard
dotnet run --project Scrapscript.Repl -- push "3 * 5"
# $sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b

-- That's the number 15, encoded as a single byte: 0x0F
dotnet run --project Scrapscript.Repl -- flat "3 * 5"
# 0F

-- Pull it back by hash
dotnet run --project Scrapscript.Repl -- pull sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b
# 15

-- Reference it in an expression
dotnet run --project Scrapscript.Repl -- eval '$sha1~~c7255dc48b42d44f6c0676d6009051b7e1aa885b + 1'
# 16
```

The same value always produces the same hash. `3 * 5` and `15` are the same scrap.

---

## Getting Started

**Prerequisites:** .NET 10 SDK

```sh
git clone <this-repo>
cd Scrap
dotnet build
dotnet test
```

### REPL

```sh
dotnet run --project Scrapscript.Repl
```

```
Scrapscript REPL  (Ctrl+C to exit)
> 1 + 1
2
> f = x -> x * x
defined: f
> f 7
49
> list/map f [1, 2, 3, 4, 5]
[1, 4, 9, 16, 25]
```

Bindings (`name = expr`) persist across lines. Expressions are evaluated in the accumulated session environment.

### CLI Subcommands

```sh
-- Evaluate an expression
dotnet run --project Scrapscript.Repl -- eval '1 + 1'

-- Compile to JavaScript
dotnet run --project Scrapscript.Repl -- compile 'list/map (n -> n * n) [1,2,3]'

-- Run with a platform
dotnet run --project Scrapscript.Repl -- run --platform=http 'req -> #send { status = 200, body = "hello" }'

-- Scrapyard operations
dotnet run --project Scrapscript.Repl -- yard init
dotnet run --project Scrapscript.Repl -- push '"hello, world"'
dotnet run --project Scrapscript.Repl -- flat "[1, 2, 3]"
dotnet run --project Scrapscript.Repl -- pull sha1~~<hash>

-- Map operations
dotnet run --project Scrapscript.Repl -- map init mylib
dotnet run --project Scrapscript.Repl -- map commit mylib 'add = a -> b -> a + b'
dotnet run --project Scrapscript.Repl -- map history mylib
```

Use the `SCRAP_YARD` environment variable to override the default yard location.

---

## Project Structure

```
Scrapscript.sln
├── Scrapscript.Core/
│   ├── Lexer/            Token.cs, Lexer.cs
│   ├── Parser/           Ast.cs, Parser.cs
│   ├── Eval/             ScrapValue.cs, ScrapEnv.cs, Evaluator.cs
│   ├── TypeChecker/      ScrapType.cs, Substitution.cs, TypeEnv.cs,
│   │                     TypeInferrer.cs, BuiltinTypes.cs
│   ├── Compiler/         JsCompiler.cs
│   ├── Serialization/    FlatEncoder.cs, FlatDecoder.cs
│   ├── Scrapyard/        LocalYard.cs
│   ├── Builtins/         Builtins.cs
│   └── ScrapInterpreter.cs   (public API entry point)
├── Scrapscript.Repl/     Program.cs
└── Scrapscript.Tests/    LexerTests.cs, ParserTests.cs, EvalTests.cs,
                          TypeCheckerTests.cs, CompilerTests.cs,
                          FlatEncoderTests.cs
```

### Embedding the interpreter

```csharp
using Scrapscript.Core;
using Scrapscript.Core.Eval;

var interpreter = new ScrapInterpreter();

// Evaluate with type checking (default)
var result = interpreter.Eval("list/map (n -> n * n) [1, 2, 3]");
// result is ScrapList([ScrapInt(1), ScrapInt(4), ScrapInt(9)])

// Infer the type of an expression
var type = interpreter.TypeOf("a -> b -> a + b");
// "('t0 -> ('t0 -> 't0))"

// Compile to JavaScript
var js = interpreter.CompileToJs("list/map (n -> n * n) [1, 2, 3]");
```

With a scrapyard:

```csharp
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.Serialization;

var yard = new LocalYard();
yard.Init();

var hash = yard.Push(FlatEncoder.Encode(new ScrapInt(42)));
// "sha1~~df58248c414f342c81e056b40bee12d17a08bf61"

var interpreter = new ScrapInterpreter(yard);
interpreter.Eval($"${hash} + 1");
// ScrapInt(43)
```

---

## Test Coverage

451 tests across lexer, parser, evaluator, type checker, JS compiler, flat encoder, and scrapyard integration.

```sh
dotnet test
# Passed! - Failed: 0, Passed: 451
```

---

## Spec

The language is documented at [scrapscript.org](https://scrapscript.org). This implementation includes a local copy of the spec at `scrapscript-spec.txt`. Additional documentation:

- [`docs/scrapyard.md`](docs/scrapyard.md) — deep dive on content addressability, the flat binary format, and the scrapyard CLI
