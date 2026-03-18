# Recursive Closures in the Self-Hosted Evaluator

## The Problem

The self-hosted evaluator (`eval.ss`) uses a persistent linked-list environment. When
evaluating a where-clause binding like:

```
fib = | 0 -> 0 | 1 -> 1 | n -> fib (n-1) + fib (n-2)
```

`extend-env` evaluates the case function in the *current* env, producing a closure
that captures that env. That env doesn't yet contain `fib`, so recursive calls fail.
The C# evaluator avoids this by mutating a shared `childEnv` — all closures share the
same dictionary, so by the time `fib` is called, the dictionary has been updated.

---

## Option 1 — Mutable Reference Cells (Clojure-style)

Add `ref/new`, `ref/get`, `ref/set` builtins backed by a new `ScrapRef` value type.

`extend-env` would:
1. Create a ref cell per binding: `cell = ref/new #nil`
2. Build a rec-env where each name maps to its cell
3. Evaluate each closure in rec-env (closures capture the cells)
4. `ref/set cell closure` to patch each cell

`env-lookup` would auto-deref refs:
```
env-lookup = name -> env ->
  (| [] -> #error
   | [{ name = n, val = v }] ++ rest ->
       (| #true -> deref v
        | #false -> env-lookup name rest
       ) (n == name)
  ) env
```

**Pros:** Simple, explicit, works for mutual recursion.
**Cons:** Introduces observable mutable state into the evaluator. Purity is broken —
`ref/set` is a side effect. Once refs exist as a builtin, nothing stops users from
using them for general mutable state.

---

## Option 2 — Haskell-style Hidden Mutation (Thunks)

Haskell's `let` is letrec by default. GHC implements this with *thunks* — heap-
allocated unevaluated expressions. When a thunk is forced, it evaluates itself and
patches the heap cell in place ("black hole" optimisation). Mutation happens, but
below the abstraction boundary and is not observable from Haskell code.

Translated to Scrapscript: a `ScrapThunk` value type holds an unevaluated expression
+ env. `env-lookup` forces thunks on read. `extend-env` stores thunks (not evaluated
closures) for each binding. The mutation (forcing a thunk and caching the result) is
invisible to Scrapscript programs.

**Pros:** Transparent to users. Preserves observable purity. Standard technique.
**Cons:** Significant C# runtime work. Thunks interact with TCO in non-obvious ways.
Adds complexity throughout the evaluator (every value access may need to force).

---

## Option 3 — Z Combinator (Purely Functional, Strict)

The Z combinator is the strict-language variant of the Y combinator:

```
Z = f -> (x -> f (v -> x x v)) (x -> f (v -> x x v))
```

The eta-expansion `v -> x x v` delays self-application, preventing infinite regress
under strict evaluation. Recursive functions are written as:

```
fib = Z (fib -> | 0 -> 0 | 1 -> 1 | n -> fib (n-1) + fib (n-2))
```

This is entirely pure. No mutation anywhere.

**Implementation path A — explicit:** Users write `Z (f -> ...)` themselves. No
changes to `extend-env`. Add `Z` (or `fix`) as a named builtin or stdlib function.

**Implementation path B — automatic:** `extend-env` detects that a binding's value
is a lambda/case-fn and automatically wraps it with `fix`. Users write normal
where-clauses; the evaluator transforms them. `fix` itself is a builtin implemented
in C# via a self-referential C# closure (no Scrapscript mutation needed):

```csharp
// fix f = f (fix f)  -- but strict, so eta-expand:
// fix f x = f (fix f) x
env.Set("fix", new ScrapBuiltin("fix", f => {
    ScrapValue Fix(ScrapValue arg) =>
        Evaluator.ApplyFunction(Evaluator.ApplyFunction(f, new ScrapBuiltin("fix-self", Fix)), arg);
    return new ScrapBuiltin("fix(f)", Fix);
}));
```

**Pros:** Fully pure. No new value types. Small C# change. Path B is transparent
to users writing where-clauses.
**Cons:** Path A changes how users write recursive code. Path B wrapping all
lambdas unconditionally adds overhead and may interact with non-recursive bindings
in unexpected ways (though it's still correct — `fix (f -> expr-not-using-f)` = `expr`).

---

## Recommendation

**Option 3, path B** is the right default choice:
- Preserves purity
- Transparent to users of the self-hosted evaluator
- Minimal C# change (~15 lines in `Builtins.cs`)
- Small change to `extend-env` in `eval.ss`

Option 1 (refs) is worth adding as a general-purpose builtin for programs that
*want* mutable state (file I/O, accumulators, etc.), but should not be the mechanism
behind `extend-env`.

Option 2 is the "right" theoretical answer for a lazy language but expensive to
retrofit onto a strict runtime.

---

## Affected Tests (once implemented)

These Stage 3 / full-pipeline tests could be added:

```
EvalMutualRec    even 4 (where even/odd are mutually recursive)  →  #tag "true"
FullPipelineFib  "fib 6 ; fib = | 0 -> 0 | 1 -> 1 | n -> fib (n-1) + fib (n-2)"  →  #int 8
```
