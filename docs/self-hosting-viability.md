# Scrapscript Self-Hosting Interpreter — Viability Report

## Experiment Results

### Experiment 1 — Character-level text access

| Snippet | Result |
|---|---|
| `text/split "" "hello"` | `["hello"]` — **FAIL**: C#'s `.Split("")` returns the original string intact |
| `text/chars "hello"` | `["h", "e", "l", "l", "o"]` — **PASS** |
| `text/at` | Returns `#just "c"` / `#nothing` — exists |

**Verdict: No missing primitive.** The plan's concern was prescient, but `text/chars` (line 253 in `Builtins.cs`) already solves it. A scrapscript lexer can call `text/chars src` to get a `[text]` character list and walk it with case functions.

---

### Experiment 2 — Recursive variant type definition

```
x
; x : v = #cons { head = #int 1, tail = #nil }
; v : #int int #nil #cons { head : v, tail : v }
```
**Result**: `#cons { head = #int 1, tail = #nil }` — **PASS at runtime.**

Two important findings:

1. **`[T]` is now a valid type expression.** `IsTypeAtomStart` and `ParseTypeAtom` in `Parser.cs` were extended to accept `[T]` syntax, converting it to `TList` during type checking. You can write `v : #list [v]` in a type annotation. Self-referential named types using records or bare name references (`v : #nil #cons { head : v, tail : v }`) also work.

2. **Multi-payload variants support both space-separated vars and list patterns.** Both `#node l r` and `#node [l, r]` are valid and equivalent. A self-hosting parser may use whichever style is clearest.

Additional verification — recursive tree type with depth function:
```
(depth (#node (#node #leaf #leaf) #leaf))
; depth = | #leaf -> 0 | #node l r -> 1 + (depth l)
; tree : #leaf #node tree tree
```
**Result**: `2` — **PASS**

---

### Experiment 3 — Functional parser combinator style

```
(parse-int [#int 42, #plus])
; parse-int = | [#int n] ++ rest -> { result = #int n, rest = rest }
              | _                -> { result = #error, rest = tokens }
; tokens = [#int 42, #plus]
```
**Result**: `{ rest = [#plus], result = #int 42 }` — **PASS**

The `[#int n] ++ rest` list prefix pattern works exactly as needed. State threading via `{ result, rest }` records is ergonomic and correct.

---

### Experiment 4 — Mutual recursion (eval / apply / lookup)

A complete mini evaluator was run:
```
(eval (#apply { fn = #lam { param = "x", body = #var "x" }, arg = #int 5 }) [])
; eval   = expr -> env -> ( | #int n -> ... | #var name -> ... | #apply ... | #lam ... ) expr
; apply  = fn -> arg -> ( | #closure ... -> eval b ([{ name=p, val=arg }] ++ e) ) fn
; lookup = name -> | [] -> #nothing
                   | [{ name = n, val = v }] ++ rest ->
                       (| #true -> v | #false -> lookup name rest) (n == name)
```
**Result**: `#int 5` — **PASS**

Key syntactic constraint discovered: **`==` cannot appear in a case pattern.** The equality test must be done as an expression, then matched: `(n == name)` produces `#true` or `#false`, which is then dispatched with a nested case function. This is idiomatic and clean once you know it.

---

### Experiment 5 — Functional environment lookup with parent-chain traversal

```
(lookup "y" (#frame { name = "x", val = #int 42,
              parent = #frame { name = "y", val = #int 99, parent = #empty } }))
; lookup = name -> | #empty -> #nothing
                   | #frame { name = n, val = v, parent = p } ->
                       (| #true -> #just v | #false -> lookup name p) (n == name)
```
**Result**: `#just #int 99` — **PASS**

Functional linked-list environment with correct parent-chain traversal works fully.

---

## Missing Primitives

| Primitive | Status | Impact |
|---|---|---|
| `text/chars` — char list from string | **Already exists** (`Builtins.cs:253`) | Lexer fully unblocked |
| `text/at` — char at index | **Already exists** (`Builtins.cs:245`) | Alternative to `text/chars` |
| `[T]` in type expression syntax | **Fixed** — `ParseTypeAtom` now handles `LBracket` | Type annotations on `value : #list [value]` now work |
| `text/split ""` giving chars | **Broken** (C# semantics) | Not needed — `text/chars` supersedes |

There are no remaining primitive gaps. `[T]` list syntax in type expressions is now supported.

---

## Verdict — Self-hosting is viable

All five core capabilities required for a meta-circular interpreter are present and confirmed:

| Capability | Confirmed |
|---|---|
| Variant pattern dispatch (AST nodes) | yes (Exp 3, 4) |
| Mutual recursion in where-clauses | yes (Exp 4) |
| Closures capturing environments | yes (Exp 4 — `#closure { ..., env = env }`) |
| Parser combinator state threading | yes (Exp 3) |
| Functional environment lookup | yes (Exp 5) |
| Character-level text access for lexer | yes (`text/chars`) |
| Recursive type values | yes (Exp 2, runtime) |

There are no remaining open risks for typed self-reference. `[value]` is now valid in type annotations, so a fully type-annotated meta-circular interpreter can use list types freely.

---

## Effort Estimate

### Lexer (~100–150 lines)
`text/chars src` yields a `[text]` character list. A case function scans it, accumulating chars into tokens, returning `[token]`. Token types are represented as variants: `#int n`, `#ident t`, `#lparen`, `#plus`, etc. The main complexity is multi-character operators and quoted strings. Feasible with recursive descent over the character list.

### Parser (~200–350 lines)
Recursive descent in parser combinator style, each production returning `{ result = ast, rest = tokens }`. Design consideration: **left recursion** (e.g., function application `f x y z`) requires an iterative approach — collect spine using `list/fold` rather than mutual left-recursive calls. AST nodes with multiple children can use space-separated payloads (`#binary op left right`) or record payloads (`#binary { op = op, left = left, right = right }`). All grammar rules are otherwise straightforward case functions.

### Evaluator (~150–250 lines)
The mini evaluator from Exp 4 is the skeleton. Adding `#binop`, `#list`, `#record`, `#where`, `#case` node types is additive. The environment (`[]` of records) and closure (`#closure { param, body, env }`) representations are already validated. The biggest addition is implementing where-clause mutual recursion (a `#rec-frame` approach).

### Total PoC (unannotated)
**~500–800 lines** of scrapscript for a lexer + parser + evaluator covering the core scrapscript subset. Type annotations can be added incrementally; the `[T]` type expression syntax is now supported.
