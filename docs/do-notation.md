# Do Notation

Scrapscript is purely functional — functions have no side effects. But platforms
(HTTP, console, etc.) need to perform effects like database queries. The question
is: how does a Scrapscript program *describe* what it wants done without actually
doing it?

The answer is **effect descriptions**: instead of performing an effect, a function
returns a data structure that *describes* the effect. The platform interprets that
structure and executes the real work.

This document traces how we got to `do` notation: the problem it solves, why
`bind` alone is painful, why `>>=` is better but still awkward, and how `do`
makes chained effects read naturally.

---

## The effect-description pattern

The HTTP platform applies a Scrapscript function to a request path and expects
one of three terminal variants back:

```
| "/" -> #ok "Hello"
| _   -> #notfound "Not found"
```

To add a database query, the function can't just *run* SQL — it's pure. Instead
it returns a `#query` variant that carries the SQL and a **continuation**: a
function that takes the query result and produces the next step.

```
| "/" -> #query { sql = "SELECT name FROM users WHERE id = 1"
               , then = user -> #ok ("Hello, " ++ user.name) }
| _   -> #notfound "Not found"
```

The platform loop becomes:

1. Call the function → get a variant
2. If `#ok`/`#notfound`/`#error` → write the HTTP response, done
3. If `#query { sql, then }` → run the SQL, call `then` with the result, go to 1

The Scrapscript program never performs I/O. It only builds a tree of nested
continuations. The platform is the only thing that executes effects.

---

## The problem: nested continuations are ugly

When you need two queries, the nesting becomes painful:

```
| "/" ->
    #query { sql = "SELECT id FROM sessions WHERE token = ..."
           , then = session ->
               #query { sql = "SELECT name FROM users WHERE id = " ++ session.id
                      , then = user -> #ok user.name }}
```

Every additional query adds another level of indentation. The actual logic
(`#ok user.name`) is buried inside layers of boilerplate.

---

## Step 1: the `bind` helper

The repetition is always the same shape: run a query, name the result, do
something with it. We can abstract that as `bind`:

```
query = sql -> #query { sql = sql, then = row -> #pure row }

bind = m -> f ->
    | #pure x -> f x
    | #query { sql = sql, then = k } ->
          #query { sql = sql, then = row -> bind (k row) f }
    | x -> x
```

- `query sql` wraps a SQL string in a `#query` effect with an identity
  continuation (`#pure` just marks "this is a plain value, not an effect").
- `bind m f` sequences two steps: run `m`, take its result, pass it to `f`.
  If `m` is a terminal (`#ok`, `#notfound`, `#error`), it short-circuits and
  `f` is never called.

The two-query example becomes:

```
| "/" ->
    bind (query "SELECT id FROM sessions WHERE token = ...") (session ->
    bind (query "SELECT name FROM users WHERE id = " ++ session.id) (user ->
    #ok user.name))
```

Better — no more manual `{ sql = ..., then = ... }` everywhere. But there is
still rightward drift: each `bind` indents the rest of the computation.

---

## Step 2: `>>=` as an infix alias

If `bind` were a right-associative infix operator, the chain could be written
horizontally:

```
query "SELECT id FROM sessions WHERE token = ..."    >>= (session ->
query "SELECT name FROM users WHERE id = " ++ session.id >>= (user ->
#ok user.name))
```

Parentheses around the lambdas are still required to keep the parser from
misreading the arrow. And if each step needs its own line to be readable, the
`>>=` at the end of each line looks like noise.

This is how Haskell code looked before `do` notation was introduced.

---

## Step 3: `do` notation

`do` is syntactic sugar built into the Scrapscript parser. It desugars directly
to `bind` calls — no new runtime machinery, no new evaluator rules. `bind` just
needs to be in scope.

```
| "/" ->
    do session <- query "SELECT id FROM sessions WHERE token = ...",
       user    <- query "SELECT name FROM users WHERE id = " ++ session.id,
       #ok user.name
```

Each line reads as "name gets the result of this effect, then...". The final
line is the return value of the whole block.

The desugar is mechanical:

```
do x <- e1, y <- e2, final
```

becomes

```
bind e1 (x -> bind e2 (y -> final))
```

### Side-effect steps (no binding)

Use `_` when you don't need the result:

```
do _ <- query "INSERT INTO log VALUES (...)",
   result <- query "SELECT ...",
   #ok result.name
```

### Short-circuiting

`bind` passes terminal variants through unchanged without calling the
continuation. So if any step produces `#notfound` or `#error`, the rest of
the chain is skipped:

```
do session <- query "SELECT ...",   -- returns #notfound "no session"
   user    <- query "...",          -- never runs
   #ok user.name                    -- never runs
-- result: #notfound "no session"
```

### Full example

```
handler = path ->
    do session <- query "SELECT user_id FROM sessions WHERE token = " ++ path,
       user    <- query "SELECT name, role FROM users WHERE id = " ++ session.user_id,
       #ok ("Welcome, " ++ user.name)
; bind = m -> f ->
    | #pure x -> f x
    | #query { sql = sql, then = k } ->
          #query { sql = sql, then = row -> bind (k row) f }
    | x -> x
; query = sql -> #query { sql = sql, then = row -> #pure row }
```

The platform receives the outermost `#query`, runs the SQL, feeds the result
into `then`, receives the next `#query`, and so on until it hits `#ok`.

---

## Why this approach

- **Pure**: the Scrapscript program never performs I/O. It only builds a
  description of what should happen. The platform is the sole executor of
  effects.
- **Testable**: you can test the handler by inspecting the returned effect tree,
  without a real database.
- **Extensible**: adding new effects (HTTP calls, file reads, cache lookups)
  means adding new variant tags to `bind` and to the platform's interpreter
  loop. The `do` syntax and the rest of the program are unchanged.
- **No magic**: `bind` is an ordinary Scrapscript function defined in userspace.
  `do` is purely a parser transformation.
