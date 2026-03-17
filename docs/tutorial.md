# Building the Scrapscript Interpreter from Scratch

A complete, progressive guide to implementing the Scrapscript language — lexer, parser,
evaluator, pattern matching, where-clauses, builtins, do notation, content-addressable
storage, HTTP platform, and Hindley-Milner type inference.

**Audience:** Anyone who knows programming. Pseudocode examples are typed and readable
regardless of host language. C# source files are cited when exact behavior matters.

**Structure:** 6 parts, 19 chapters. Parts 1–3 build the core interpreter. Part 4 adds
storage and platform support. Part 5 (type inference) depends only on Parts 1–3 and can
be done in parallel with Part 4. Part 6 wires everything together.

**Estimated effort:** ~42 hours of implementation work.

---

## Dependency graph

```
Ch1 → Ch2 → Ch3 → Ch4 → Ch5 → Ch6 → Ch7 → Ch8 → Ch9 → Ch10 → Ch11 → Ch12
                   │                                                     │
                  Ch15 → Ch16 → Ch17 → Ch18                           Ch13
                                                                       Ch14
```

---

## Part 1 — Foundations

### Chapter 1 — Token Representation

**Level:** Beginner | **Effort:** ~0.5 h

#### Motivation

Before we can scan source text we need something to scan *into*. A token is the smallest
meaningful unit in a program — numbers, names, operators, punctuation. Defining these data
structures first lets us think about what the lexer produces without tangling scanning
logic into the picture.

#### Data structures

```
enum TokenKind {
    // Literals
    Int, Float, Text,
    HexByte,        // ~FF  (a single hex byte)
    Base64Bytes,    // ~~...  (base-64 encoded bytes)
    Hole,           // ()  (the unit value / absence)

    // Names
    Identifier,     // any name, including module paths like list/first
    HashTag,        // #tagname
    HashRef,        // $sha1~~<hex>  (content-addressed reference)

    // Operators
    Plus, Minus, Star, Slash, Percent,
    PlusPlus,       // ++  (concat)
    PlusLt,         // +<  (list append)
    GtPlus,         // >+  (list cons)
    GtGt,           // >>  (function compose)
    PipeGt,         // |>  (forward pipe)
    LtPipe,         // <|  (backward pipe)
    LtMinus,        // <-  (do-notation bind)
    Arrow,          // ->  (lambda)
    FatArrow,       // =>  (type parameter)
    ColonColon,     // ::  (variant constructor in type namespace)
    Colon,          // :   (type annotation)
    Semicolon,      // ;   (where-clause separator)
    Equals,         // =
    EqEq,           // ==
    NotEq,          // !=
    Lt, Gt, LtEq, GtEq,
    Pipe,           // |   (case arm)
    DotDot,         // ..  (record spread)

    // Punctuation
    LParen, RParen, LBracket, RBracket, LBrace, RBrace, Comma, Dot,

    // Special
    Wildcard,       // _  (only when standing alone as a complete token)
    Eof,
}

struct Token {
    kind    : TokenKind
    text    : string     // raw lexeme (for identifiers, numbers, strings)
    line    : int
    col     : int
}
```

Key points:
- `Hole` is `()` — scanned as a two-character sequence, not as `LParen` + `RParen`.
- `Wildcard` is `_` only when the next character is *not* an identifier character
  (so `_name` becomes an `Identifier`).
- `HashTag` stores the name *without* the leading `#`.
- `HashRef` stores everything after `$` (e.g. `sha1~~abc123`).

#### Milestone

Construct tokens by hand and pretty-print them:

```
Token(Int,   "42",  1, 1)
Token(Plus,  "+",   1, 4)
Token(Ident, "x",   1, 6)
Token(Eof,   "",    1, 7)
```

---

### Chapter 2 — The Lexer

**Level:** Intermediate | **Effort:** ~3 h

#### Motivation

The lexer turns a raw string into a flat list of tokens. It is the only part of the
pipeline that deals with individual characters. Everything above it works on tokens.

#### Scanner state

```
struct Lexer {
    src  : string
    pos  : int   // current character index
    line : int   // 1-based line counter
    col  : int   // 1-based column counter
}

function current(lx) -> char:
    if lx.pos < len(lx.src): return lx.src[lx.pos]
    return '\0'

function peek(lx, offset=1) -> char:
    if lx.pos + offset < len(lx.src): return lx.src[lx.pos + offset]
    return '\0'

function advance(lx, count=1):
    repeat count times:
        if current(lx) == '\n':
            lx.line++
            lx.col = 1
        else:
            lx.col++
        lx.pos++
```

#### Main loop

```
function tokenize(src) -> list<Token>:
    lx = Lexer(src, 0, 1, 1)
    tokens = []
    while lx.pos < len(lx.src):
        skip_whitespace(lx)                // blanks, tabs, newlines (no comment syntax)
        if lx.pos >= len(lx.src): break
        tok = read_token(lx)
        if tok != null: tokens.append(tok)
    tokens.append(Token(Eof, "", lx.line, lx.col))
    return tokens
```

Scrapscript has **no comment syntax** in the current implementation — whitespace is
simply consumed and discarded.

#### Scanning each token kind

```
function read_token(lx) -> Token:
    line, col = lx.line, lx.col
    c = current(lx)

    // Text literal "..."
    if c == '"': return read_text(lx, line, col)

    // Base64 bytes ~~<base64>
    if c == '~' and peek(lx) == '~':
        advance(lx, 2)
        b64 = read_base64(lx)
        return Token(Base64Bytes, b64, line, col)

    // Hex byte ~HH
    if c == '~':
        advance(lx)
        hex = read_hex_digits(lx)
        return Token(HexByte, hex, line, col)

    // #tag
    if c == '#':
        advance(lx)
        name = read_identifier(lx)       // allows leading digit: #2d
        return Token(HashTag, name, line, col)

    // $sha1~~...  (hash reference)
    if c == '$':
        advance(lx)
        rest = read_until_delimiter(lx)  // until whitespace, ), ], }, ,, ;
        return Token(HashRef, rest, line, col)

    // Number
    if is_digit(c): return read_number(lx, line, col)

    // Multi-character operators (try longest match first)
    match c:
        '+':
            if peek(lx) == '+': advance(lx,2); return Token(PlusPlus, "++", line, col)
            if peek(lx) == '<': advance(lx,2); return Token(PlusLt,   "+<", line, col)
            advance(lx); return Token(Plus, "+", line, col)
        '-':
            if peek(lx) == '>': advance(lx,2); return Token(Arrow, "->", line, col)
            advance(lx); return Token(Minus, "-", line, col)
        '>':
            if peek(lx) == '+': advance(lx,2); return Token(GtPlus, ">+", line, col)
            if peek(lx) == '>': advance(lx,2); return Token(GtGt,   ">>", line, col)
            if peek(lx) == '=': advance(lx,2); return Token(GtEq,   ">=", line, col)
            advance(lx); return Token(Gt, ">", line, col)
        '|':
            if peek(lx) == '>': advance(lx,2); return Token(PipeGt, "|>", line, col)
            advance(lx); return Token(Pipe, "|", line, col)
        '<':
            if peek(lx) == '|': advance(lx,2); return Token(LtPipe,  "<|", line, col)
            if peek(lx) == '=': advance(lx,2); return Token(LtEq,    "<=", line, col)
            if peek(lx) == '-': advance(lx,2); return Token(LtMinus, "<-", line, col)
            advance(lx); return Token(Lt, "<", line, col)
        '=':
            if peek(lx) == '>': advance(lx,2); return Token(FatArrow, "=>", line, col)
            if peek(lx) == '=': advance(lx,2); return Token(EqEq,     "==", line, col)
            advance(lx); return Token(Equals, "=", line, col)
        '!':
            if peek(lx) == '=': advance(lx,2); return Token(NotEq, "!=", line, col)
            error("Unexpected '!'")
        ':':
            if peek(lx) == ':': advance(lx,2); return Token(ColonColon, "::", line, col)
            advance(lx); return Token(Colon, ":", line, col)
        ';': advance(lx); return Token(Semicolon, ";", line, col)
        '.':
            if peek(lx) == '.': advance(lx,2); return Token(DotDot, "..", line, col)
            advance(lx); return Token(Dot, ".", line, col)
        '(':
            if peek(lx) == ')': advance(lx,2); return Token(Hole, "()", line, col)
            advance(lx); return Token(LParen, "(", line, col)
        ')': advance(lx); return Token(RParen, ")", line, col)
        '[': advance(lx); return Token(LBracket, "[", line, col)
        ']': advance(lx); return Token(RBracket, "]", line, col)
        '{': advance(lx); return Token(LBrace, "{", line, col)
        '}': advance(lx); return Token(RBrace, "}", line, col)
        ',': advance(lx); return Token(Comma, ",", line, col)

    // Wildcard: bare _ not followed by identifier chars
    if c == '_' and not is_ident_char(peek(lx)):
        advance(lx); return Token(Wildcard, "_", line, col)

    // Identifier (including _prefixed names)
    if is_ident_start(c) or c == '_':
        name = read_identifier(lx)
        return Token(Identifier, name, line, col)

    error("Unexpected character: " + c)
```

#### Reading a text literal

```
function read_text(lx, line, col) -> Token:
    advance(lx)                         // skip opening "
    sb = ""
    while current(lx) != '"' and lx.pos < len(lx.src):
        if current(lx) == '\\':
            advance(lx)
            match current(lx):
                'n': sb += '\n'
                't': sb += '\t'
                '"': sb += '"'
                '\\': sb += '\\'
                else: sb += '\\' + current(lx)
            advance(lx)
        else:
            sb += current(lx)
            advance(lx)
    if lx.pos >= len(lx.src):
        error("Unterminated string at " + line + ":" + col)
    advance(lx)                         // skip closing "
    return Token(Text, sb, line, col)
```

#### Reading a number

```
function read_number(lx, line, col) -> Token:
    sb = ""
    while is_digit(current(lx)):
        sb += current(lx); advance(lx)
    if current(lx) == '.' and is_digit(peek(lx)):
        sb += '.'; advance(lx)
        while is_digit(current(lx)):
            sb += current(lx); advance(lx)
        return Token(Float, sb, line, col)
    return Token(Int, sb, line, col)
```

#### Character predicates

```
is_ident_start(c) = is_letter(c) or c == '_'
is_ident_char(c)  = is_letter(c) or is_digit(c) or c in {'-', '_', '/'}
is_hex_digit(c)   = c in '0'..'9' or c in 'a'..'f' or c in 'A'..'F'
is_base64_char(c) = is_letter(c) or is_digit(c) or c in {'+', '/', '='}
```

Note that `-` and `/` are valid inside identifiers, enabling module-path names like
`list/first` and kebab-case names like `to-float`.

> **Design decision — unary minus:** The lexer always emits `-` as a plain `Minus`
> token, even when it directly precedes a digit. The parser detects unary minus in its
> atom rule. This keeps the lexer simple and avoids the ambiguity between negation
> (`-x`) and subtraction (`a - b`). See Chapter 3.

#### Milestone

```
tokenize("x = 1 + 2")
→ [Identifier("x"), Equals("="), Int("1"), Plus("+"), Int("2"), Eof]

tokenize("#true")
→ [HashTag("true"), Eof]

tokenize("list/first [1, 2]")
→ [Identifier("list/first"), LBracket, Int("1"), Comma, Int("2"), RBracket, Eof]
```

---

### Chapter 3 — The AST and Parser

**Level:** Intermediate-Advanced | **Effort:** ~6 h

#### Motivation

The parser turns the flat token list into a tree that captures the program's structure.
We define two tree types: `Expr` (expressions that compute values) and `Pattern`
(structures that match and destructure values). A third type, `TypeExpr`, represents
type annotations but is only used for the type-checker; the evaluator ignores it.

#### Expression nodes

```
union Expr {
    // Literals
    IntLit(value: int)
    FloatLit(value: float)
    TextLit(value: string)
    BytesLit(value: bytes)
    HoleLit

    // References
    Var(name: string)
    HashRef(ref: string)               // $sha1~~...

    // Collections
    ListExpr(items: list<Expr>)
    RecordExpr(fields: list<(string, Expr)>, spread: string?)

    // Access
    RecordAccess(record: Expr, field: string)

    // Where-clause
    WhereExpr(body: Expr, bindings: list<Binding>)

    // Functions
    LambdaExpr(param: Pattern, body: Expr)
    CaseExpr(arms: list<CaseArm>)

    // Application / operators
    ApplyExpr(fn: Expr, arg: Expr)
    BinOpExpr(op: string, left: Expr, right: Expr)
    NegExpr(operand: Expr)

    // Type-system extras (evaluated to Hole at runtime)
    TypeAnnotation(value: Expr, type: TypeExpr)
    TypeDefExpr(name: string, type: TypeExpr)
    ConstructorExpr(typeExpr: Expr, variant: string)
}

struct Binding  { pattern: Pattern; value: Expr }
struct CaseArm  { pattern: Pattern; body: Expr  }
```

#### Pattern nodes

```
union Pattern {
    WildcardPat                                     // _
    VarPat(name: string)                            // x
    IntPat(value: int)                              // 42
    TextPat(prefix: string, rest: string?)          // "hello" or "hello" ++ rest
    BytesPat(value: bytes)                          // ~FF
    ListPat(items: list<Pattern>, tail: string?)    // [a, b] or [a, b] ++ tail
    ConsPat(head: Pattern, tail: Pattern)           // h >+ t
    RecordPat(fields: list<(string, Pattern)>,
              spread: string?)                      // {x=px, ..rest}
    VariantPat(tag: string, payload: Pattern?)      // #just p or #nothing
    HolePat                                         // ()
}
```

#### Precedence table (low → high)

| Level | Operator(s) | Associativity |
|-------|-------------|---------------|
| 1 | `;` (where)                | special (list of bindings) |
| 2 | `\|>` `<\|` (pipe)         | left / right |
| 3 | `>>` (compose)             | left |
| 4 | `==` `!=` `<` `>` `<=` `>=` | left |
| 5 | `++` (concat)              | left |
| 6 | `>+` (cons)                | left |
| 7 | `+<` (append)              | left |
| 8 | `+` `-`                    | left |
| 9 | `*` `/` `%`                | left |
| 10 | `:` (type annotation)     | non-assoc |
| 11 | `->` (lambda) `\|` (case) | right |
| 12 | application (juxtaposition) | left |
| 13 | `::` (type constructor)   | left |
| 14 | `.` (record access)       | left |

#### Recursive descent

```
parse_program() -> Expr:
    expr = parse_where()
    expect(Eof)
    return expr

// Level 1: where-clause
parse_where() -> Expr:
    body = parse_pipe()
    if current != Semicolon: return body
    bindings = []
    while current == Semicolon:
        consume()            // ;
        pat = parse_pattern()
        if current == Colon:
            consume()        // :
            name = pat as VarPat or error
            typedef = parse_type_expr()
            if current == Equals:
                consume()    // =
                val = parse_pipe()
                bindings.append(Binding(pat, TypeAnnotation(val, typedef)))
            else:
                bindings.append(Binding(pat, TypeDefExpr(name, typedef)))
        else:
            expect(Equals)
            val = parse_pipe()
            bindings.append(Binding(pat, val))
    return WhereExpr(body, bindings)

// Level 2: forward pipe |>
parse_pipe() -> Expr:
    left = parse_right_pipe()
    while current == PipeGt:
        consume()
        right = parse_right_pipe()
        left = ApplyExpr(right, left)   // left |> right  ==>  right(left)
    return left

// Level 2: backward pipe <|
parse_right_pipe() -> Expr:
    right = parse_compose()
    if current == LtPipe:
        consume()
        left = parse_right_pipe()       // right-recursive
        return ApplyExpr(right, left)   // right <| left  ==>  right(left)
    return right

// Level 3: compose >>
parse_compose() -> Expr:
    left = parse_comparison()
    while current == GtGt:
        consume()
        right = parse_comparison()
        left = BinOpExpr(">>", left, right)
    return left

// Level 4: comparisons
parse_comparison() -> Expr:
    left = parse_concat()
    while current in {EqEq, NotEq, Lt, Gt, LtEq, GtEq}:
        op = consume().text
        right = parse_concat()
        left = BinOpExpr(op, left, right)
    return left

// Levels 5–9: concat, cons, append, add/sub, mul/div — same pattern as comparison.
// parse_concat → parse_cons → parse_append → parse_addsub → parse_muldiv

// Level 10: type annotation
parse_type_annotation() -> Expr:
    expr = parse_lambda()
    if current == Colon:
        consume()
        t = parse_type_expr()
        return TypeAnnotation(expr, t)
    return expr

// Level 11: lambda and case
parse_lambda() -> Expr:
    // Case function: | pat -> body | pat -> body ...
    if current == Pipe:
        arms = []
        while current == Pipe:
            consume()               // |
            pat = parse_pattern()
            expect(Arrow)
            body = parse_pipe()     // ← parse_pipe, NOT parse_where
            arms.append(CaseArm(pat, body))
        return CaseExpr(arms)

    expr = parse_application()
    if current == Arrow:
        consume()               // ->
        body = parse_pipe()     // ← parse_pipe, NOT parse_where
        pat = expr_to_pattern(expr)
        return LambdaExpr(pat, body)
    return expr

// Level 12: application (left-associative)
parse_application() -> Expr:
    fn = parse_constructor()
    while is_atom_start():
        arg = parse_constructor()
        fn = ApplyExpr(fn, arg)
    return fn

// Level 13: type constructor ::
parse_constructor() -> Expr:
    expr = parse_record_access()
    if current == ColonColon:
        consume()
        variant = consume().text   // identifier
        return ConstructorExpr(expr, variant)
    return expr

// Level 14: record access .field
parse_record_access() -> Expr:
    expr = parse_atom()
    while current == Dot:
        consume()
        field = expect(Identifier).text
        expr = RecordAccess(expr, field)
    return expr

// Atoms
parse_atom() -> Expr:
    match current:
        Int:         return IntLit(consume().text as int)
        Float:       return FloatLit(consume().text as float)
        Text:        return TextLit(consume().text)
        Hole:        return HoleLit
        HexByte:     return BytesLit([parse_hex(consume().text)])
        Base64Bytes: return BytesLit(base64_decode(consume().text))
        HashRef:     return HashRef(consume().text)
        HashTag:     consume(); return ConstructorExpr(Var("__variant__"), tok.text)
        Identifier when tok.text == "do":
                     consume(); return parse_do()
        Identifier:  consume(); return Var(tok.text)
        Wildcard:    consume(); return Var("_")
        LParen:      return parse_parenthesized()   // calls parse_where inside
        LBracket:    return parse_list()
        LBrace:      return parse_record()
        Minus:       // unary minus
            consume()
            if current == Int:   return IntLit(-consume().text)
            if current == Float: return FloatLit(-consume().text)
            return NegExpr(parse_atom())
        else: error("Unexpected token: " + current)
```

> **Design decision — where-clause scoping:** Lambda bodies and case arm bodies call
> `parse_pipe()`, *not* `parse_where()`. This means a semicolon inside a lambda body
> belongs to the *outer* where-clause, not the inner one. If you want a where-clause
> *inside* a lambda, you must parenthesize it:
>
> ```
> // Works:
> x -> (y ; y = 1)
>
> // Doesn't work as intended — the ; is parsed at the outer level:
> x -> y ; y = 1
> ```
>
> Parentheses call `parse_where()` directly, so they always allow full where-clauses.
> This rule keeps the grammar simple and unambiguous.

#### Do-notation desugaring

Do-notation is a syntactic shorthand for chained monadic binds. It is desugared *at
parse time* into ordinary function applications. No new evaluator code is needed.

```
parse_do() -> Expr:
    steps = []
    while true:
        if current == Identifier and peek() == LtMinus:
            name = consume()    // identifier
            consume()           // <-
            expr = parse_pipe()
            steps.append((VarPat(name), expr))
            expect(Comma)
        else if current == Wildcard and peek() == LtMinus:
            consume()           // _
            consume()           // <-
            expr = parse_pipe()
            steps.append((WildcardPat, expr))
            expect(Comma)
        else:
            // Final expression — desugar right-to-left
            final_expr = parse_pipe()
            result = final_expr
            for (pat, e) in reverse(steps):
                result = ApplyExpr(
                    ApplyExpr(Var("bind"), e),
                    LambdaExpr(pat, result))
            return result
```

The transformation:

```
do x <- e1, y <- e2, body
  ⟹  bind e1 (x -> bind e2 (y -> body))
```

`bind` must be in scope at runtime — it is user-supplied or platform-provided.

#### ExprToPattern: one grammar for both

Left-hand sides of `->` are parsed *as expressions* using the full precedence table, then
converted to patterns afterward. This avoids duplicating precedence rules.

```
expr_to_pattern(expr) -> Pattern:
    match expr:
        Var("_")              → WildcardPat
        Var(name)             → VarPat(name)
        IntLit(n)             → IntPat(n)
        TextLit(s)            → TextPat(s, null)
        BytesLit(b)           → BytesPat(b)
        HoleLit               → HolePat
        ListExpr(items)       → ListPat(map(expr_to_pattern, items), null)
        BinOpExpr(">+", h, t) → ConsPat(expr_to_pattern(h), expr_to_pattern(t))
        BinOpExpr("++", l, r) → (see concat pattern logic below)
        RecordExpr(fields, sp)→ RecordPat(map(f→(f.name, expr_to_pattern(f.val)), fields), sp)
        ApplyExpr(ConstructorExpr(_, tag), arg) → VariantPat(tag, expr_to_pattern(arg))
        ConstructorExpr(_, tag) → VariantPat(tag, null)
        else                  → error("Cannot use as pattern: " + expr)

// "hello" ++ rest  →  TextPat("hello", "rest")
// [a, b]  ++ tail  →  ListPat([a, b], "tail")
concat_to_pattern(left, right):
    if left is TextPat and right is VarPat:
        return TextPat(left.prefix, right.name)
    if left is ListPat and right is VarPat:
        return ListPat(left.items, right.name)
    error("Invalid ++ pattern")
```

#### Milestone

Parse each of these and verify the resulting AST:

```
// Simple binary expression
parse("1 + 2 * 3")
→ BinOpExpr("+", IntLit(1), BinOpExpr("*", IntLit(2), IntLit(3)))

// Lambda
parse("x -> x + 1")
→ LambdaExpr(VarPat("x"), BinOpExpr("+", Var("x"), IntLit(1)))

// Case function
parse("| 0 -> 1 | n -> n * 2")
→ CaseExpr([CaseArm(IntPat(0), IntLit(1)), CaseArm(VarPat("n"), BinOpExpr("*", Var("n"), IntLit(2)))])

// Do-notation
parse("do x <- [1,2], y <- [3,4], [x * y]")
→ ApplyExpr(ApplyExpr(Var("bind"), ListExpr([IntLit(1),IntLit(2)])),
     LambdaExpr(VarPat("x"),
       ApplyExpr(ApplyExpr(Var("bind"), ListExpr([IntLit(3),IntLit(4)])),
         LambdaExpr(VarPat("y"), ListExpr([BinOpExpr("*",Var("x"),Var("y"))])))))
```

---

## Part 2 — Core Evaluation

### Chapter 4 — Value Representation

**Level:** Beginner | **Effort:** ~1 h

#### Motivation

Every Scrapscript expression evaluates to a value. Defining values as a closed union
type makes the evaluator exhaustively checkable and keeps display/equality logic in one
place.

#### Value types

```
union ScrapValue {
    Int(value: int)
    Float(value: float)
    Text(value: string)
    Bytes(value: bytes)
    Hole                                    // the unit value ()
    List(items: persistent-list<ScrapValue>)
    Record(fields: persistent-map<string, ScrapValue>)
    Variant(tag: string, payload: ScrapValue?)

    // Functions
    Function(params: list<Pattern>, body: Expr, env: Env)
    CaseFunction(arms: list<(Pattern, Expr)>, env: Env)

    // Builtins (host-language functions)
    Builtin1(name: string, apply: ScrapValue -> ScrapValue)
    Builtin2(name: string, apply: ScrapValue -> ScrapValue -> ScrapValue)
    BuiltinPartial(name: string, first: ScrapValue,
                   apply: ScrapValue -> ScrapValue -> ScrapValue)
}
```

Use persistent (immutable, structural-sharing) data structures for `List` and `Record`
so that you can extend them cheaply without full copies.

#### Equality

Two values are equal if:
- They have the same type and the same content.
- For `List`: element-by-element recursion.
- For `Bytes`: byte-by-byte comparison (not pointer equality!).
- Functions are *never* equal to each other (no structural equality for closures).

#### Display

```
display(v) -> string:
    match v:
        Int(n)      → str(n)
        Float(f)    → if f is whole: f with ".0"; else f normally
        Text(s)     → '"' + escape(s) + '"'
        Bytes(b)    → if len(b)==1: "~XX"; else "~~<base64>"
        Hole        → "()"
        List(items) → "[" + join(", ", map(display, items)) + "]"
        Record(f)   → "{ " + join(", ", map(kv→kv.key+" = "+display(kv.val), f)) + " }"
        Variant(t, null)    → "#" + t
        Variant(t, payload) → "#" + t + " " + display(payload)
        Function    → "<function>"
        ...
```

#### Milestone

Manually construct each value type and verify `display` output:

```
display(Int(42))                              → "42"
display(Text("hello"))                        → "\"hello\""
display(List([Int(1), Int(2)]))               → "[1, 2]"
display(Record({"x": Int(3), "y": Int(4)}))  → "{ x = 3, y = 4 }"
display(Variant("just", Int(5)))              → "#just 5"
display(Variant("nothing", null))             → "#nothing"
```

---

### Chapter 5 — The Environment

**Level:** Beginner | **Effort:** ~0.5 h

#### Motivation

An environment maps names to values. Environments form a parent chain: a child env
is created for each new scope (lambda application, where-clause), and name lookup
walks the chain. This naturally implements lexical scoping.

#### Data structure and operations

```
struct Env {
    bindings : map<string, ScrapValue>
    parent   : Env?
}

function lookup(env, name) -> ScrapValue:
    if name in env.bindings: return env.bindings[name]
    if env.parent != null:   return lookup(env.parent, name)
    throw NameError("Unbound variable: " + name)

function extend(env, name, value) -> Env:
    child = Env({name: value}, parent=env)
    return child

function extend_many(env, pairs: list<(string, ScrapValue)>) -> Env:
    child = Env({}, parent=env)
    for (name, value) in pairs:
        child.bindings[name] = value
    return child
```

The where-clause evaluator (Chapter 9) also needs a mutable `set` operation on an
existing env — this is the *one* place where mutation is needed. All other operations
create new child envs.

```
function set(env, name, value):    // mutates env directly (for where-clauses)
    env.bindings[name] = value
```

#### Milestone

```
base = Env({"x": Int(1)}, null)
child = extend(base, "y", Int(2))
lookup(child, "x")  → Int(1)   // found in parent
lookup(child, "y")  → Int(2)   // found in child
lookup(child, "z")  → NameError
```

---

### Chapter 6 — Core Expression Evaluation

**Level:** Intermediate | **Effort:** ~2 h

#### Motivation

Now we can write the central `eval` function. It recurses over the AST, returning a
`ScrapValue` for each node.

#### eval dispatch

```
function eval(expr, env) -> ScrapValue:
    match expr:
        IntLit(n)   → Int(n)
        FloatLit(f) → Float(f)
        TextLit(s)  → Text(s)
        BytesLit(b) → Bytes(b)
        HoleLit     → Hole

        Var(name)   → lookup(env, name)
        HashRef(r)  → pull_from_yard(r)   // see Chapter 13

        ListExpr(items)          → eval_list(items, env)
        RecordExpr(fields, sp)   → eval_record(fields, sp, env)
        RecordAccess(rec, field) → eval_record_access(rec, field, env)

        WhereExpr(body, bindings) → eval_where(body, bindings, env)
        LambdaExpr(param, body)   → Function([param], body, env)
        CaseExpr(arms)            → CaseFunction(arms, env)

        ApplyExpr(fn, arg)        → eval_apply(fn, arg, env)
        BinOpExpr(op, l, r)       → eval_binop(op, l, r, env)
        NegExpr(operand)          → eval_neg(operand, env)

        ConstructorExpr(_, tag)   → Variant(tag, null)
        TypeAnnotation(val, _)    → eval(val, env)   // ignore type at runtime
        TypeDefExpr               → Hole             // no-op at runtime
```

#### Binary operators

```
function eval_binop(op, left_expr, right_expr, env):
    // Compose is special: doesn't evaluate eagerly
    if op == ">>":
        f = eval(left_expr, env)
        g = eval(right_expr, env)
        return Builtin1("compose", arg -> apply_value(g, apply_value(f, arg)))

    left  = eval(left_expr, env)
    right = eval(right_expr, env)

    match op:
        "+"  → add(left, right)
        "-"  → sub(left, right)
        "*"  → mul(left, right)
        "/"  → div(left, right)
        "%"  → mod(left, right)
        "++" → concat(left, right)
        "+<" → append(left, right)
        ">+" → cons(left, right)
        "==" → bool_variant(left == right)
        "!=" → bool_variant(left != right)
        "<"  → order_op(left, right, r → r < 0)
        ">"  → order_op(left, right, r → r > 0)
        "<=" → order_op(left, right, r → r <= 0)
        ">=" → order_op(left, right, r → r >= 0)

function bool_variant(b) = if b: Variant("true", null) else Variant("false", null)

function add(l, r):
    match (l, r):
        (Int(a), Int(b))     → Int(a + b)
        (Float(a), Float(b)) → Float(a + b)
        else                 → TypeError("cannot add " + display(l) + " and " + display(r))

// sub, mul, div, mod: same pattern

function concat(l, r):
    match (l, r):
        (Text(a), Text(b))   → Text(a + b)
        (List(a), List(b))   → List(a ++ b)
        (Bytes(a), Bytes(b)) → Bytes(a ++ b)
        else                 → TypeError(...)

function cons(l, r):
    // l >+ r  — prepend l to list r
    match r:
        List(items) → List([l] ++ items)
        else        → TypeError("right side of >+ must be a list")

function append(l, r):
    // l +< r  — append r to list l
    match l:
        List(items) → List(items ++ [r])
        Bytes(a) when r is Bytes(b) → Bytes(a ++ b)
        else        → TypeError(...)

function order_op(l, r, pred):
    cmp = compare(l, r)   // returns -1, 0, or 1
    return bool_variant(pred(cmp))
```

#### Milestone

```
eval("1 + 2 * 3")   → Int(7)
eval('"hello" ++ " world"')  → Text("hello world")
eval("1 >+ [2, 3]") → List([Int(1), Int(2), Int(3)])
eval("[1, 2] +< 3") → List([Int(1), Int(2), Int(3)])
eval("1 == 1")      → Variant("true", null)
eval("1 == 2")      → Variant("false", null)
```

---

### Chapter 7 — Function Application and Closures

**Level:** Intermediate | **Effort:** ~2 h

#### Motivation

Functions are first-class values. A `Function` value captures its *closure* — the
environment at the point of definition. When applied, it creates a new child env from
the closure, binds the argument to the parameter, and evaluates the body.

#### apply_value

```
function apply_value(fn, arg) -> ScrapValue:
    match fn:
        Function(params, body, closure):
            bound = match_pattern(params[0], arg, closure)  // Chapter 8
            if bound == null: throw MatchError("pattern match failed for arg: " + display(arg))
            new_env = extend_many(closure, bound)
            return eval(body, new_env)

        CaseFunction(arms, closure):
            for (pat, body) in arms:
                bound = match_pattern(pat, arg, closure)
                if bound != null:
                    new_env = extend_many(closure, bound)
                    return eval(body, new_env)
            throw MatchError("no case arm matched: " + display(arg))

        Builtin1(_, apply):
            return apply(arg)

        Builtin2(_, apply):
            // First application — return a partial
            return BuiltinPartial("...", arg, apply)

        BuiltinPartial(_, first, apply):
            return apply(first, arg)

        Variant(tag, null):
            // Applying a no-payload variant: give it a payload
            return Variant(tag, arg)

        Variant(tag, existing_payload):
            // Applying a variant that already has a payload: accumulate args in a list
            match existing_payload:
                List(items) → Variant(tag, List(items ++ [arg]))
                else        → Variant(tag, List([existing_payload, arg]))

        else: throw TypeError("cannot apply non-function: " + display(fn))

function eval_apply(apply_expr, env):
    fn  = eval(apply_expr.fn,  env)
    arg = eval(apply_expr.arg, env)
    return apply_value(fn, arg)
```

#### Currying

Every lambda takes exactly one argument. Multi-argument functions are nested lambdas:

```
a -> b -> a + b
```

is actually `LambdaExpr(VarPat("a"), LambdaExpr(VarPat("b"), BinOp("+", Var("a"), Var("b"))))`.

Partial application is free: applying `(a -> b -> a + b)` to `3` returns a new
`Function` that still holds `b -> a + b` with `a=3` in its closure.

#### Milestone

```
eval("(a -> b -> a + b) 3 4")         → Int(7)
eval("(a -> b -> a + b) 3")           → Function(...)   // partial
eval("(x -> x * x) 5")                → Int(25)
eval("(| #just n -> n | #nothing -> 0) #just 42")  → Int(42)
```

---

## Part 3 — Advanced Evaluation

### Chapter 8 — Pattern Matching

**Level:** Intermediate-Advanced | **Effort:** ~4 h

#### Motivation

Pattern matching is used in two places: lambda parameter binding and case arms. Both
go through `match_pattern`. A successful match returns a list of `(name, value)` pairs
to add to the environment. Failure returns `null` (no exception — caller decides what
to do).

#### match_pattern

```
function match_pattern(pat, value, env) -> list<(string, ScrapValue)>?:
    bindings = []
    if match_into(pat, value, bindings, env):
        return bindings
    return null

function match_into(pat, value, bindings, env) -> bool:
    match pat:
        WildcardPat:
            return true                      // matches anything, binds nothing

        VarPat(name):
            bindings.append((name, value))
            return true

        HolePat:
            return value is Hole

        IntPat(n):
            return value is Int(m) and m == n

        TextPat(prefix, null):
            return value is Text(s) and s == prefix

        TextPat(prefix, rest_name):
            if value is not Text(s): return false
            if not s.starts_with(prefix): return false
            bindings.append((rest_name, Text(s[len(prefix):])))
            return true

        BytesPat(b):
            return value is Bytes(v) and v == b   // byte-by-byte equality

        ListPat(items, null):
            if value is not List(vs): return false
            if len(vs) != len(items): return false
            for i in 0..len(items):
                if not match_into(items[i], vs[i], bindings, env): return false
            return true

        ListPat(items, tail_name):
            if value is not List(vs): return false
            if len(vs) < len(items): return false
            for i in 0..len(items):
                if not match_into(items[i], vs[i], bindings, env): return false
            bindings.append((tail_name, List(vs[len(items):])))
            return true

        ConsPat(head_pat, tail_pat):
            if value is not List(vs) or len(vs) == 0: return false
            if not match_into(head_pat, vs[0], bindings, env): return false
            return match_into(tail_pat, List(vs[1:]), bindings, env)

        RecordPat(fields, null):
            if value is not Record(rec): return false
            if len(rec) != len(fields): return false   // exact match
            for (field, pat) in fields:
                if field not in rec: return false
                if not match_into(pat, rec[field], bindings, env): return false
            return true

        RecordPat(fields, spread):
            if value is not Record(rec): return false
            for (field, pat) in fields:
                if field not in rec: return false
                if not match_into(pat, rec[field], bindings, env): return false
            // Bind remaining fields to spread name
            remaining = rec without keys in fields
            bindings.append((spread, Record(remaining)))
            return true

        VariantPat(tag, null):
            return value is Variant(t, null) and t == tag

        VariantPat(tag, payload_pat):
            if value is not Variant(t, p): return false
            if t != tag: return false
            if p == null: return false
            return match_into(payload_pat, p, bindings, env)
```

#### Nested patterns and case arms

Case arms are tried in order. The first that matches wins. If none match, a
`MatchError` is thrown.

```
// Case function that deconstructs a list
| [] -> 0
| h >+ t -> h + sum t
; sum = | [] -> 0 | h >+ t -> h + sum t

// Record pattern with spread
| {name=n, ..rest} -> "name=" ++ n
```

#### Milestone

```
eval("(| #just n -> n | #nothing -> 0) #nothing")  → Int(0)
eval("(| h >+ t -> h) [1, 2, 3]")                  → Int(1)
eval("(| {x=a, y=b} -> a + b) {x=3, y=4}")         → Int(7)
eval('(| "hello" ++ rest -> rest | _ -> "") "hello world"')
    → Text(" world")
```

---

### Chapter 9 — Where-Clauses and Mutual Recursion

**Level:** Intermediate-Advanced | **Effort:** ~2 h

#### Motivation

A where-clause lets you write the *result expression first*, then define the helpers it
uses below it. This is the "goal-first" style that makes Scrapscript programs read top-
to-bottom in terms of intent. Where-clauses also support *mutual recursion*: two
bindings that reference each other.

```
even_odd = even 10
; even = | 0 -> #true  | n -> odd  (n - 1)
; odd  = | 0 -> #false | n -> even (n - 1)
```

#### eval_where

Naive sequential evaluation fails for mutual recursion: `even` tries to look up `odd`
before `odd` is defined. The solution is a *retry loop*.

```
function eval_where(body, bindings, env) -> ScrapValue:
    // Create a single child env for all bindings.
    // Closures will capture this env by reference — so lambdas defined here
    // will see each other once all bindings are added.
    child_env = Env({}, parent=env)

    remaining = bindings
    prev_count = -1

    while len(remaining) > 0 and len(remaining) != prev_count:
        prev_count = len(remaining)
        deferred = []

        for binding in remaining:
            try:
                val = eval(binding.value, child_env)
                bound = match_pattern(binding.pattern, val, child_env)
                if bound == null: throw MatchError(...)
                for (name, bval) in bound:
                    set(child_env, name, bval)   // mutate child_env directly
            catch NameError:
                deferred.append(binding)

        remaining = deferred

    if len(remaining) > 0:
        // Force the error by re-evaluating the first unresolvable binding
        eval(remaining[0].value, child_env)

    return eval(body, child_env)
```

> **Design decision — closure capture by reference:** A lambda value stores a reference
> to `child_env` (not a copy). When `eval_where` later calls `set(child_env, "fact", ...)`
> to add `fact` to the env, any previously-created closure that references `child_env`
> will see `fact` on the *next* lookup. This makes self-recursive lambdas work without
> any `letrec` magic: by the time the lambda is *called*, its own name is already in
> the env.

> **Why the retry loop?** On the first pass, `even` tries to look up `odd` (not yet
> added) and throws `NameError`. `even` is deferred. After `odd` is added, the second
> pass successfully evaluates `even` (which closes over `child_env` which now contains
> `odd`). The loop terminates when either all bindings succeed or no progress is made
> (genuine undefined reference error).

#### Milestone

```
eval("result ; result = even 10
    ; even = | 0 -> #true  | n -> odd  (n - 1)
    ; odd  = | 0 -> #false | n -> even (n - 1)")
→ Variant("true", null)

eval("fact 5
    ; fact = n -> fact_helper n 1
    ; fact_helper = | 0 acc -> acc | n acc -> fact_helper (n-1) (n*acc)")
→ Int(120)
```

---

### Chapter 10 — Built-in Functions

**Level:** Beginner-Intermediate | **Effort:** ~3 h

#### Motivation

Many operations cannot be expressed in Scrapscript itself (string length, float
rounding, etc.) — they need to call into the host language. Builtins are registered in
the *base environment* before any user code runs.

#### Builtin registry

```
function create_builtin_env() -> Env:
    env = Env({}, null)

    // --- Type conversion ---
    set(env, "to-float", Builtin1("to-float", v → match v:
        Int(n)   → Float(float(n))
        Float(f) → Float(f)
        else     → TypeError("to-float: expected int")))

    set(env, "round", Builtin1("round", v → match v:
        Float(f) → Int(round(f))
        else     → TypeError("round: expected float")))

    set(env, "ceil", Builtin1("ceil",   v → Float→Int(ceil(v.value))))
    set(env, "floor", Builtin1("floor", v → Float→Int(floor(v.value))))

    set(env, "abs", Builtin1("abs", v → match v:
        Int(n)   → Int(abs(n))
        Float(f) → Float(abs(f))
        else     → TypeError(...)))

    set(env, "min", Builtin2("min", (a, b) → if a <= b: a else b))
    set(env, "max", Builtin2("max", (a, b) → if a >= b: a else b))

    // --- Bytes ---
    set(env, "bytes/to-utf8-text", Builtin1("bytes/to-utf8-text",
        v → match v: Bytes(b) → Text(utf8_decode(b)) else → TypeError(...)))

    // --- List module ---
    set(env, "list/length", Builtin1("list/length",
        v → match v: List(items) → Int(len(items)) else → TypeError(...)))

    set(env, "list/first", Builtin1("list/first", v → match v:
        List(items) when len(items) > 0 → Variant("just", items[0])
        List(_)                         → Variant("nothing", null)
        else                            → TypeError(...)))

    set(env, "list/repeat", Builtin2("list/repeat",
        (n, v) → List(repeat(v, n.value))))

    set(env, "list/reverse", Builtin1("list/reverse",
        v → match v: List(items) → List(reverse(items)) else → TypeError(...)))

    set(env, "list/sort", Builtin1("list/sort",
        v → match v: List(items) → List(sort(items)) else → TypeError(...)))

    // Higher-order list functions use apply_value to call the user function
    set(env, "list/map", Builtin1("list/map", f →
        Builtin1("list/map(f)", lst → match lst:
            List(items) → List(map(item → apply_value(f, item), items))
            else        → TypeError(...))))

    set(env, "list/filter", Builtin1("list/filter", f →
        Builtin1("list/filter(f)", lst → match lst:
            List(items) → List(filter(item →
                apply_value(f, item) == Variant("true", null), items))
            else        → TypeError(...))))

    set(env, "list/fold", Builtin1("list/fold", f →
        Builtin1("list/fold(f)", init →
            Builtin1("list/fold(f,init)", lst → match lst:
                List(items) → fold_left((acc, item) →
                    apply_value(apply_value(f, acc), item), init, items)
                else        → TypeError(...)))))

    set(env, "list/zip", Builtin2("list/zip",
        (a, b) → match (a, b):
            (List(la), List(lb)) → List(zip_with((x,y) → List([x,y]), la, lb))
            else → TypeError(...)))

    // --- Text module ---
    set(env, "text/length", Builtin1("text/length",
        v → match v: Text(s) → Int(len(s)) else → TypeError(...)))

    set(env, "text/repeat", Builtin2("text/repeat",
        (n, t) → match (n,t): (Int(k), Text(s)) → Text(repeat(s, k)) else → TypeError(...)))

    set(env, "text/trim", Builtin1("text/trim",
        v → match v: Text(s) → Text(trim(s)) else → TypeError(...)))

    set(env, "text/split", Builtin2("text/split",
        (sep, str) → match (sep, str):
            (Text(s), Text(t)) → List(map(Text, split(t, s)))
            else → TypeError(...)))

    set(env, "text/to-upper", Builtin1("text/to-upper",
        v → match v: Text(s) → Text(to_upper(s)) else → TypeError(...)))

    set(env, "text/to-lower", Builtin1("text/to-lower",
        v → match v: Text(s) → Text(to_lower(s)) else → TypeError(...)))

    // --- Maybe module ---
    set(env, "maybe/default", Builtin2("maybe/default", (def, m) → match m:
        Variant("just", v)  → v
        Variant("nothing", null) → def
        else → TypeError(...)))

    // --- Strings / dict ---
    set(env, "string/join", Builtin2("string/join", (sep, lst) → match (sep, lst):
        (Text(s), List(items)) → Text(join(s, map(i → i.value, items)))
        else → TypeError(...)))

    set(env, "dict/get", Builtin2("dict/get", (key, dict) → match (key, dict):
        (Text(k), Record(r)) →
            if k in r: Variant("just", r[k]) else Variant("nothing", null)
        else → TypeError(...)))

    // Boolean convenience values
    set(env, "true",  Variant("true",  null))
    set(env, "false", Variant("false", null))

    return env
```

#### Key pattern: curried builtins

`list/map f lst` involves two applications. We register `list/map` as a `Builtin1`.
When applied to `f` it returns *another* `Builtin1` that has `f` closed over. This
mirrors how lambdas curry: no special "builtin-with-arity-N" machinery is needed.

#### Milestone

```
eval("list/map (x -> x * 2) [1, 2, 3]")
    → List([Int(2), Int(4), Int(6)])

eval("list/filter (x -> x > 2) [1, 2, 3, 4]")
    → List([Int(3), Int(4)])

eval("list/fold (acc -> x -> acc + x) 0 [1, 2, 3, 4]")
    → Int(10)

eval("maybe/default 99 #just 42")  → Int(42)
eval("maybe/default 99 #nothing")  → Int(99)
```

---

### Chapter 11 — Composition and Pipes

**Level:** Beginner-Intermediate | **Effort:** ~0.5 h

#### Motivation

Pipes and composition let you chain functions without deeply nested parentheses.
They require no new data structures — just special cases in `eval_binop`.

#### Pipes: desugared at parse time

`left |> right` is parsed as `ApplyExpr(right, left)` — *right* applied to *left*.
`right <| left` is the same thing. The parser handles both, so the evaluator sees only
a normal application node.

#### Composition: lazily closed over

`f >> g` is desugared at *eval time* (not parse time) because we need the actual values
of `f` and `g`:

```
function eval_binop(">>", f_expr, g_expr, env):
    f = eval(f_expr, env)
    g = eval(g_expr, env)
    return Builtin1("compose(" + display(f) + "," + display(g) + ")", arg →
        apply_value(g, apply_value(f, arg)))
```

The result is a fresh builtin that applies `f` then `g`. It is a first-class value and
can be passed around, stored, or composed further.

#### Milestone

```
eval("(x -> x + 1) >> (x -> x * 2) |> 3")  → Int(8)
// Step by step: 3 |> (add1 >> double)
//             = (add1 >> double) 3
//             = double (add1 3)
//             = double 4
//             = 8

eval("3 |> (x -> x * x)")  → Int(9)
```

---

### Chapter 12 — Do Notation

**Level:** Intermediate | **Effort:** ~1 h

#### Motivation

Do-notation provides a readable syntax for sequencing computations that produce values
in a context (lists, maybe, IO, etc.). The key insight is that it is **pure syntax
sugar**: the desugaring in Chapter 3 means the evaluator never sees a `do` node.

#### How the desugaring works at runtime

```
do x <- e1,
   y <- e2,
   final
```

desugars to:

```
bind e1 (x -> bind e2 (y -> final))
```

`bind` is just a regular name in scope — it can be defined by the user or provided by
a platform.

#### Example: maybe monad

```
// bind for Maybe: if #nothing, short-circuit; otherwise apply the function
bind_maybe = m -> f -> | #just v -> f v | #nothing -> #nothing <| m
; bind = bind_maybe

result =
    do x <- #just 3,
       y <- #just 4,
       #just (x + y)
; bind = bind_maybe
```

Desugared:

```
bind (#just 3) (x ->
    bind (#just 4) (y ->
        #just (x + y)))
```

#### Example: list monad

```
// bind for list: apply f to each element, flatten results
bind_list = lst -> f -> list/fold (acc -> x -> acc ++ f x) [] lst

result =
    do x <- [1, 2],
       y <- [10, 20],
       [x + y]
; bind = bind_list
```

Desugared:

```
bind [1, 2] (x -> bind [10, 20] (y -> [x + y]))
→ [11, 21, 12, 22]
```

#### Milestone

```
// With bind = bind_list:
eval("do x <- [1,2], y <- [3,4], [x*y]")
→ List([Int(3), Int(4), Int(6), Int(8)])

// With bind = bind_maybe:
eval("do x <- #just 3, y <- #nothing, #just (x + y)")
→ Variant("nothing", null)
```

---

## Part 4 — Extended Features

### Chapter 13 — Content-Addressable Storage (Scrapyard)

**Level:** Advanced | **Effort:** ~3 h

#### Motivation

Scrapscript programs can reference values by their *content hash* — a SHA1 of the
value's serialized form. This is called the Scrapyard. It enables:

- Sharing values across programs without copying.
- Referring to a specific version of a value immutably.
- Caching and deduplication for free.

A hash reference looks like `$sha1~~<hex>` in source code.

#### Flat encoding (msgpack subset)

We need a deterministic binary serialization for any `ScrapValue`. The encoding is a
subset of MessagePack.

```
function encode(value) -> bytes:
    match value:
        Hole:         return [0xC0]

        Int(n):
            if 0 <= n <= 127:    return [byte(n)]
            if -32 <= n < 0:     return [0xE0 | (n + 32)]
            else:                return [0xD3] ++ int64_be(n)

        Float(f):     return [0xCB] ++ float64_be(f)

        Text(s):
            b = utf8_encode(s)
            if len(b) <= 31:     return [0xA0 | len(b)] ++ b
            if len(b) <= 255:    return [0xD9, len(b)] ++ b
            else:                return [0xDA] ++ uint16_be(len(b)) ++ b

        Bytes(data):
            if len(data) <= 255: return [0xC4, len(data)] ++ data
            else:                return [0xC5] ++ uint16_be(len(data)) ++ data

        List(items):
            n = len(items)
            header = if n <= 15: [0x90 | n] else [0xDC] ++ uint16_be(n)
            return header ++ concat(map(encode, items))

        Record(fields):
            sorted = sort_by_key(fields)              // sort keys lexicographically!
            n = len(sorted)
            header = if n <= 15: [0x80 | n] else [0xDE] ++ uint16_be(n)
            body = concat(map((k,v) → encode(Text(k)) ++ encode(v), sorted))
            return header ++ body

        Variant(tag, payload):
            tag_bytes = utf8_encode(tag)
            payload_bytes = if payload != null: encode(payload) else []
            ext_data = [len(tag_bytes)] ++ tag_bytes ++ payload_bytes
            return [0xC7, len(ext_data), 0x00] ++ ext_data

        Function | CaseFunction | Builtin...:
            error("Cannot encode functions")
```

**Important:** Record fields must be sorted lexicographically before encoding.
Two records with the same fields in different orders must produce the same bytes.

#### Content hash

```
function content_hash(value) -> string:
    flat = encode(value)
    sha1 = sha1_hash(flat)
    hex  = to_hex_lower(sha1)
    return "sha1~~" + hex
```

#### Scrapyard interface

```
interface Scrapyard {
    push(flat: bytes) -> string     // stores flat bytes, returns hash ref
    pull(hash_ref: string) -> bytes?  // retrieves flat bytes by ref
    contains(hash_ref: string) -> bool
}
```

#### Filesystem implementation

Files are stored under `~/.scrap/yard/sha1/<2-char-shard>/<remaining-hex>`:

```
function hash_ref_to_path(root, hash_ref) -> path:
    if not hash_ref.starts_with("sha1~~"): return null
    hex = hash_ref["sha1~~".length:]
    return root + "/sha1/" + hex[0:2] + "/" + hex[2:]

function push(root, flat) -> string:
    hash_ref = content_hash_from_bytes(flat)  // sha1 + hex encode
    path = hash_ref_to_path(root, hash_ref)
    mkdir_p(dirname(path))
    write_file(path, flat)
    return hash_ref

function pull(root, hash_ref) -> bytes?:
    path = hash_ref_to_path(root, hash_ref)
    if path == null or not file_exists(path): return null
    return read_file(path)
```

#### Evaluator integration

In `eval(HashRef(ref), env)`:

```
case HashRef(ref):
    if yard == null: throw NameError("No scrapyard configured")
    flat = yard.pull(ref)
    if flat == null: throw NameError("Hash not found: $" + ref)
    return decode(flat)
```

#### Milestone

```
yard = LocalYard("~/.scrap/yard")
v = Int(42)
flat = encode(v)
ref = yard.push(flat)       // → "sha1~~..."
flat2 = yard.pull(ref)      // → same bytes
v2 = decode(flat2)          // → Int(42)

// Now use in source code:
eval("$" + ref, env_with_yard)   // → Int(42)
```

---

### Chapter 14 — Platforms

**Level:** Intermediate | **Effort:** ~2 h

#### Motivation

A Scrapscript program is just a function. What that function *does* depends on the
platform it runs on. A platform receives the evaluated function and decides how to
invoke it. This separation means the language itself has no I/O primitives — the
platform provides them.

#### Platform interface

```
interface Platform {
    run(interpreter: Interpreter, source: string)
}
```

#### ConsolePlatform

The simplest platform: evaluate the program and print the result.

```
class ConsolePlatform implements Platform {
    run(interp, source):
        value = interp.eval(source)
        print(display(value))
}
```

#### HttpPlatform

An HTTP server platform. The user writes a function that:
- Receives a `Text` value containing the request path.
- Returns a variant: `#ok "body"`, `#notfound "message"`, or `#error "message"`.

```
class HttpPlatform(port: int = 8080) implements Platform {
    run(interp, source):
        fn = interp.eval(source)
        start_http_listener(port)
        loop:
            (path, respond) = wait_for_request()
            try:
                (status, body) = dispatch(interp, fn, path)
                respond(status, body)
            catch e:
                respond(500, e.message)

    dispatch(interp, fn, path) -> (int, string):
        result = interp.apply(fn, Text(path))
        match result:
            Variant("ok",       Text(body)) → (200, body)
            Variant("notfound", Text(body)) → (404, body)
            Variant("error",    Text(body)) → (500, body)
            else → throw TypeError("unexpected response: " + display(result))
}
```

#### Milestone — HTTP handler

```
// Source code for the handler function
source = """
| "/" -> #ok "Welcome!"
| "/hello" -> #ok "Hello, world!"
| _ -> #notfound "Not found"
"""

platform = HttpPlatform(8080)
interp   = Interpreter()
platform.run(interp, source)
// Now: GET /hello → 200 "Hello, world!"
// GET /unknown → 404 "Not found"
```

---

## Part 5 — Type Inference

*This part can be done in parallel with Part 4. It depends only on Parts 1–3.*

### Chapter 15 — Type Representation

**Level:** Intermediate | **Effort:** ~1 h

#### Motivation

The type inference engine uses its own value type, separate from `ScrapValue`. These
types represent the *shape* of values, not the values themselves. Type variables (`TVar`)
are placeholders that get filled in during unification.

#### Type nodes

```
union ScrapType {
    TInt                    // the int type
    TFloat                  // float
    TText                   // text
    TBytes                  // bytes
    THole                   // ()
    TList(item: ScrapType)  // list(T)

    TRecord(fields: map<string, ScrapType>)
    // "Open" record: has at least these fields, plus unknown fields via row variable
    TOpenRecord(fields: map<string, ScrapType>, row_var: string)

    TFunc(from: ScrapType, to: ScrapType)   // A → B

    TVar(name: string)      // type variable, e.g. 't0
    TName(name: string, args: list<ScrapType>)  // named type, e.g. maybe(int)
}
```

#### Type scheme (for let-polymorphism)

A `TypeScheme` wraps a type with a list of universally-quantified variable names:

```
struct TypeScheme {
    quantified : list<string>   // ∀ these variables
    type       : ScrapType
}

// Monomorphic (no quantified vars)
mono(t) = TypeScheme([], t)

// Instantiate: replace all quantified vars with fresh type variables
instantiate(scheme, fresh_fn) -> ScrapType:
    if scheme.quantified is empty: return scheme.type
    subst = map from each q in scheme.quantified to fresh_fn()
    return apply_subst(subst, scheme.type)
```

#### free_vars: which type variables appear free in a type?

```
free_vars(t) -> set<string>:
    match t:
        TInt | TFloat | TText | TBytes | THole → {}
        TList(item)     → free_vars(item)
        TRecord(fields) → union(map(free_vars, fields.values))
        TOpenRecord(fields, row) → union(map(free_vars, fields.values)) ∪ {row}
        TFunc(a, b)     → free_vars(a) ∪ free_vars(b)
        TVar(name)      → {name}
        TName(_, args)  → union(map(free_vars, args))
```

#### Milestone

Represent `list/map` as a TypeScheme:

```
// ∀a b. (a → b) → list(a) → list(b)
TypeScheme(["a", "b"],
    TFunc(TFunc(TVar("a"), TVar("b")),
          TFunc(TList(TVar("a")),
                TList(TVar("b")))))
```

---

### Chapter 16 — Unification and Substitution

**Level:** Advanced | **Effort:** ~3 h

#### Motivation

Unification is the core algorithm that constrains type variables. Given two types, it
finds the *most general substitution* (a mapping from type vars to types) that makes
them equal. This is the heart of Hindley-Milner inference.

#### Substitution

```
struct Substitution {
    map : dict<string, ScrapType>   // var name → type
}

EMPTY = Substitution({})

// Apply substitution to a type: replace any TVar whose name is in the map
apply_subst(s, t) -> ScrapType:
    match t:
        TVar(name):
            if name in s.map:
                return apply_subst(s, s.map[name])  // keep applying until stable
            return t
        TFunc(a, b)         → TFunc(apply_subst(s,a), apply_subst(s,b))
        TList(item)         → TList(apply_subst(s, item))
        TRecord(fields)     → TRecord(map_values(f→apply_subst(s,f), fields))
        TOpenRecord(f, row) → (see below — more complex)
        else                → t      // primitives are unaffected

// Compose: apply s1 to all values of s2, then merge
compose(s1, s2) -> Substitution:
    result = {k: apply_subst(s2, v) for k,v in s1.map}
    for k,v in s2.map:
        if k not in result: result[k] = v
    return Substitution(result)
```

#### Unification

```
unify(t1, t2) -> Substitution:
    if t1 == t2: return EMPTY
    if t1 is THole or t2 is THole: return EMPTY   // Hole unifies with anything
    if t1 is TVar(name): return bind_var(name, t2)
    if t2 is TVar(name): return bind_var(name, t1)

    match (t1, t2):
        (TList(a), TList(b)) → unify(a, b)

        (TFunc(a1,b1), TFunc(a2,b2)):
            s1 = unify(a1, a2)
            s2 = unify(apply_subst(s1,b1), apply_subst(s1,b2))
            return compose(s1, s2)

        (TRecord(f1), TRecord(f2)):
            s = EMPTY
            for k in f1.keys:
                if k not in f2: error("Record missing field: " + k)
                s = compose(s, unify(apply_subst(s,f1[k]), apply_subst(s,f2[k])))
            return s

        (TOpenRecord(f,rv), TRecord(r)):    → unify_open_with_closed(f,rv, r)
        (TRecord(r), TOpenRecord(f,rv)):    → unify_open_with_closed(f,rv, r)
        (TOpenRecord(f1,rv1), TOpenRecord(f2,rv2)):  → unify_open_records(f1,rv1,f2,rv2)

        (TName(n1,a1), TName(n2,a2)) when n1==n2 and len(a1)==len(a2):
            s = EMPTY
            for (p,q) in zip(a1, a2):
                s = compose(s, unify(apply_subst(s,p), apply_subst(s,q)))
            return s

        else: error("Type mismatch: expected " + str(t1) + ", got " + str(t2))

// bind_var: create a substitution {name → type}, with occurs check
bind_var(name, type) -> Substitution:
    if type is TVar(n) and n == name: return EMPTY    // trivial: 'a = 'a
    if name in free_vars(type):
        error("Infinite type: " + name + " occurs in " + str(type))
    return Substitution({name: type})
```

#### Open record unification

When an open record `{x: int | r}` is unified with a closed record `{x: int, y: text}`:

```
unify_open_with_closed(open_fields, row_var, closed_fields):
    s = EMPTY
    for (field, open_type) in open_fields:
        if field not in closed_fields:
            error("Record missing required field: " + field)
        s = compose(s, unify(apply_subst(s, open_type),
                             apply_subst(s, closed_fields[field])))
    // Close the row variable (bind to THole, which collapses TOpenRecord to TRecord)
    s = compose(s, bind_var(row_var, THole))
    return s
```

#### Milestone

```
unify(TFunc(TVar("a"), TVar("a")), TFunc(TInt, TVar("b")))
→ Substitution({"a": TInt, "b": TInt})

unify(TList(TVar("a")), TList(TInt))
→ Substitution({"a": TInt})

unify(TInt, TText)
→ error("Type mismatch: expected int, got text")

// Occurs check:
unify(TVar("a"), TFunc(TVar("a"), TInt))
→ error("Infinite type: 'a occurs in ('a -> int)")
```

---

### Chapter 17 — Algorithm W

**Level:** Advanced | **Effort:** ~5 h

#### Motivation

Algorithm W is the core of Hindley-Milner type inference. It traverses the AST,
generates type constraints, and solves them via unification. The result is the most
general (most polymorphic) type for any expression.

#### Type environment

```
struct TypeEnv {
    bindings : map<string, TypeScheme>
    parent   : TypeEnv?
}

lookup(env, name) -> TypeScheme?
bind_mono(env, name, type):    // non-polymorphic binding
    env.bindings[name] = mono(type)
bind(env, name, scheme):
    env.bindings[name] = scheme

apply_subst_env(env, s) -> TypeEnv:
    // Apply s to every type in env.bindings
    // (used between inference steps to keep env up to date)

generalize(env, type, s) -> TypeScheme:
    // Free vars in type that are NOT free in env — these can be generalized
    type_applied = apply_subst(s, type)
    free_in_env  = union(map(tv → free_vars(tv.type), env.bindings.values))
    to_quantify  = free_vars(type_applied) - free_in_env
    return TypeScheme(list(to_quantify), type_applied)
```

#### infer(env, expr) → (ScrapType, Substitution)

```
function infer(env, expr) -> (ScrapType, Substitution):
    match expr:
        IntLit   → (TInt,   EMPTY)
        FloatLit → (TFloat, EMPTY)
        TextLit  → (TText,  EMPTY)
        BytesLit → (TBytes, EMPTY)
        HoleLit  → (THole,  EMPTY)

        Var(name):
            scheme = lookup(env, name) or error("Unbound variable: " + name)
            return (instantiate(scheme, fresh), EMPTY)

        ListExpr(items):
            if items is empty: return (TList(fresh()), EMPTY)
            (item_type, s) = infer(env, items[0])
            for item in items[1:]:
                (t, s2) = infer(apply_subst_env(env, s), item)
                s3 = unify(apply_subst(s2, item_type), t)
                s = compose(compose(s, s2), s3)
                item_type = apply_subst(s, item_type)
            return (TList(apply_subst(s, item_type)), s)

        RecordExpr(fields, spread):
            s = EMPTY
            field_types = {}
            for (name, val_expr) in fields:
                (t, s2) = infer(apply_subst_env(env, s), val_expr)
                s = compose(s, s2)
                field_types[name] = apply_subst(s, t)
            return (TRecord(field_types), s)

        RecordAccess(rec_expr, field):
            (rec_type, s) = infer(env, rec_expr)
            rec_type = apply_subst(s, rec_type)
            if rec_type is TRecord(fields):
                if field not in fields: error("Record has no field: " + field)
                return (apply_subst(s, fields[field]), s)
            if rec_type is TVar(_):
                // Constrain to be an open record with at least this field
                fresh_field = fresh()
                fresh_row   = fresh()
                s2 = unify(rec_type, TOpenRecord({field: fresh_field}, fresh_row.name))
                return (fresh_field, compose(s, s2))
            error("Cannot access field on " + str(rec_type))

        WhereExpr(body, bindings):
            return infer_where(env, body, bindings)

        LambdaExpr(param, body):
            param_type = fresh()
            body_env = TypeEnv(env)
            bind_pattern_vars(param, param_type, body_env)
            (body_type, s) = infer(body_env, body)
            return (TFunc(apply_subst(s, param_type), apply_subst(s, body_type)), s)

        CaseExpr(arms):
            arg_type    = fresh()
            result_type = fresh()
            s = EMPTY
            for arm in arms:
                arm_env = TypeEnv(apply_subst_env(env, s))
                bind_pattern_vars(arm.pattern, apply_subst(s, arg_type), arm_env)
                (body_type, s2) = infer(arm_env, arm.body)
                s = compose(s, s2)
                s3 = unify(apply_subst(s, result_type), apply_subst(s, body_type))
                s = compose(s, s3)
                result_type = apply_subst(s, result_type)
            return (TFunc(apply_subst(s, arg_type), result_type), s)

        ApplyExpr(fn_expr, arg_expr):
            (fn_type, s1) = infer(env, fn_expr)
            (arg_type, s2) = infer(apply_subst_env(env, s1), arg_expr)
            ret_type = fresh()
            s3 = unify(apply_subst(s2, fn_type), TFunc(arg_type, ret_type))
            s = compose(compose(s1, s2), s3)
            return (apply_subst(s, ret_type), s)

        BinOpExpr(op, left, right):
            (tl, sl) = infer(env, left)
            (tr, sr) = infer(apply_subst_env(env, sl), right)
            s = compose(sl, sr)
            match op:
                "+" | "-" | "*" | "/" | "%":
                    // Both sides must be the same numeric type
                    s2 = unify(apply_subst(s,tl), apply_subst(s,tr))
                    s = compose(s, s2)
                    unified = apply_subst(s, tl)
                    if unified is not TInt, TFloat, or TVar: error(...)
                    return (unified, s)
                "++" :
                    s2 = unify(apply_subst(s,tl), apply_subst(s,tr))
                    s = compose(s, s2)
                    return (apply_subst(s, tl), s)
                ">+" :
                    item_t = fresh()
                    s2 = unify(apply_subst(s,tr), TList(item_t))
                    s = compose(s, s2)
                    s3 = unify(apply_subst(s,tl), apply_subst(s,item_t))
                    s = compose(s, s3)
                    return (TList(apply_subst(s, item_t)), s)
                "+<" :
                    item_t = fresh()
                    s2 = unify(apply_subst(s,tl), TList(item_t))
                    s = compose(s, s2)
                    s3 = unify(apply_subst(s,tr), apply_subst(s,item_t))
                    s = compose(s, s3)
                    return (TList(apply_subst(s, item_t)), s)
                "==" | "!=" :
                    s2 = unify(apply_subst(s,tl), apply_subst(s,tr))
                    return (TName("bool"), compose(s, s2))
                "<" | ">" | "<=" | ">=" :
                    s2 = unify(apply_subst(s,tl), apply_subst(s,tr))
                    return (TName("bool"), compose(s, s2))
                ">>" :
                    a = fresh(); b = fresh(); c = fresh()
                    s1 = unify(apply_subst(s,tl), TFunc(a, b))
                    s2 = unify(apply_subst(compose(s,s1),tr), TFunc(apply_subst(s1,b), c))
                    s = compose(compose(s,s1),s2)
                    return (TFunc(apply_subst(s,a), apply_subst(s,c)), s)

        NegExpr(operand):
            (t, s) = infer(env, operand)
            resolved = apply_subst(s, t)
            if resolved is not TInt, TFloat, or TVar: error("Negation requires numeric type")
            return (resolved, s)

        TypeAnnotation(val, typedef):
            (inferred, s) = infer(env, val)
            declared = convert_type_expr(typedef, env)
            s2 = unify(apply_subst(s, inferred), declared)
            return (apply_subst(compose(s,s2), inferred), compose(s,s2))

        ConstructorExpr(_, tag):
            // Look up tag in type environment
            typedef = find_type_for_tag(env, tag)
            if typedef != null: return make_constructor_type(typedef, tag)
            return (fresh(), EMPTY)  // unknown tag — fresh type
```

#### infer_where: let-polymorphism

```
function infer_where(env, body, bindings):
    child_env = TypeEnv(env)
    s = EMPTY

    // Pass 1: register type definitions
    for binding in bindings:
        if binding.value is TypeDefExpr(name, typedef):
            child_env.add_type_def(extract_typedef(name, typedef))
            bind_mono(child_env, name, THole)

    // Pass 2: assign placeholders (for mutual recursion)
    placeholders = {}
    for binding in bindings:
        if binding.value is TypeDefExpr: continue
        if binding.pattern is VarPat(name):
            if binding.value is TypeAnnotation(_, typedef):
                ph = convert_type_expr(typedef, child_env)
            else:
                ph = fresh()
            placeholders[name] = ph
            bind_mono(child_env, name, ph)

    // Pass 3: infer and generalize
    for binding in bindings:
        if binding.value is TypeDefExpr: continue
        (val_type, s2) = infer(apply_subst_env(child_env, s), binding.value)
        s = compose(s, s2)
        for (name, pat_type) in collect_pat_bindings(binding.pattern, val_type):
            if name in placeholders:
                s3 = unify(apply_subst(s, placeholders[name]),
                           apply_subst(s, pat_type))
                s = compose(s, s3)
            scheme = generalize(child_env, pat_type, s)
            bind(child_env, name, scheme)

    (body_type, s_body) = infer(apply_subst_env(child_env, s), body)
    return (apply_subst(s_body, body_type), compose(s, s_body))
```

> **Design decision — polymorphic arithmetic:** `+` is constrained to "the two sides
> must have the same type, and that type must be numeric (or a type variable)." This
> means `a -> b -> a + b` infers as `'t → 't → 't`, not `int → int → int`. The
> trade-off: it's more general, but you lose the guarantee that `+` always means
> integer or float addition. If you want the stricter version, add a constraint that
> the unified type must be `TInt` or `TFloat`.

#### Milestone

```
type_of("x -> x")              → "'t0 → 't0"
type_of("x -> x + 1")          → "'t0 → 't0"  (arithmetic is polymorphic)
type_of("[1, 2, 3]")            → "list(int)"
type_of("x -> [x]")            → "'t0 → list('t0)"
type_of("list/map (x -> x*2) [1,2,3]")  → "list('t)"  // or list(int) with more precision
```

---

### Chapter 18 — Type Environments and Integration

**Level:** Intermediate-Advanced | **Effort:** ~2 h

#### Motivation

We need to:
1. Supply types for all built-in functions.
2. Handle type annotations from user code.
3. Check case arms for redundancy and exhaustiveness.
4. Wire type inference into the interpreter pipeline.

#### Built-in type environment

```
function create_builtin_type_env() -> TypeEnv:
    env = TypeEnv(null)

    // Polymorphic: ∀a. a -> a
    identity_scheme = TypeScheme(["a"], TFunc(TVar("a"), TVar("a")))

    // to-float : int -> float
    bind(env, "to-float", mono(TFunc(TInt, TFloat)))

    // round, ceil, floor : float -> int
    bind(env, "round", mono(TFunc(TFloat, TInt)))
    bind(env, "ceil",  mono(TFunc(TFloat, TInt)))
    bind(env, "floor", mono(TFunc(TFloat, TInt)))

    // abs : ∀a. a -> a  (works for int and float)
    bind(env, "abs", TypeScheme(["a"], TFunc(TVar("a"), TVar("a"))))

    // min, max : ∀a. a -> a -> a
    bind(env, "min", TypeScheme(["a"], TFunc(TVar("a"), TFunc(TVar("a"), TVar("a")))))
    bind(env, "max", TypeScheme(["a"], TFunc(TVar("a"), TFunc(TVar("a"), TVar("a")))))

    // list/map : ∀a b. (a→b) → list(a) → list(b)
    bind(env, "list/map", TypeScheme(["a","b"],
        TFunc(TFunc(TVar("a"), TVar("b")),
              TFunc(TList(TVar("a")), TList(TVar("b"))))))

    // list/filter : ∀a. (a→bool) → list(a) → list(a)
    bind(env, "list/filter", TypeScheme(["a"],
        TFunc(TFunc(TVar("a"), TName("bool")),
              TFunc(TList(TVar("a")), TList(TVar("a"))))))

    // list/fold : ∀a b. (b→a→b) → b → list(a) → b
    bind(env, "list/fold", TypeScheme(["a","b"],
        TFunc(TFunc(TVar("b"), TFunc(TVar("a"), TVar("b"))),
              TFunc(TVar("b"),
                    TFunc(TList(TVar("a")), TVar("b"))))))

    // list/length : ∀a. list(a) → int
    bind(env, "list/length", TypeScheme(["a"],
        TFunc(TList(TVar("a")), TInt)))

    // list/first : ∀a. list(a) → maybe(a)
    // (using TName for maybe)
    bind(env, "list/first", TypeScheme(["a"],
        TFunc(TList(TVar("a")), TName("maybe", [TVar("a")]))))

    // text/length : text -> int
    bind(env, "text/length", mono(TFunc(TText, TInt)))

    // string/join : text -> list(text) -> text
    bind(env, "string/join", mono(
        TFunc(TText, TFunc(TList(TText), TText))))

    // maybe/default : ∀a. a -> maybe(a) -> a
    bind(env, "maybe/default", TypeScheme(["a"],
        TFunc(TVar("a"),
              TFunc(TName("maybe", [TVar("a")]), TVar("a")))))

    // dict/get : ∀a. text -> {..} -> maybe(a)
    bind(env, "dict/get", TypeScheme(["a"],
        TFunc(TText, TFunc(fresh_open_record(), TName("maybe", [TVar("a")])))))

    // bytes/to-utf8-text : bytes -> text
    bind(env, "bytes/to-utf8-text", mono(TFunc(TBytes, TText)))

    // true, false
    bind(env, "true",  mono(TName("bool")))
    bind(env, "false", mono(TName("bool")))

    return env
```

#### Redundancy and exhaustiveness checking for case arms

After type inference for a case expression, check:

**Redundancy:** An arm is redundant if an earlier arm already covers every possible
value it could match. Track which tags, integers, and text values have been covered by
catch-all patterns.

```
check_redundant(arms):
    catch_all_tags = {}
    seen_ints = {}
    seen_texts = {}
    for arm in arms:
        match arm.pattern:
            VariantPat(tag, p) when tag in catch_all_tags:
                error("Redundant pattern: #" + tag + " already covered")
            VariantPat(tag, p) when p is null or p is WildcardPat or p is VarPat:
                catch_all_tags.add(tag)
            IntPat(n) when n in seen_ints:
                error("Redundant pattern: " + n + " already covered")
            IntPat(n):
                seen_ints.add(n)
            TextPat(s, null) when s in seen_texts:
                error("Redundant pattern: \"" + s + "\" already covered")
            TextPat(s, null):
                seen_texts.add(s)
```

**Exhaustiveness:** If the matched type is a named variant type and all arms are
`VariantPat` (no wildcards), check every variant is covered.

```
check_exhaustive(arms, arg_type, env):
    if arg_type is not TName(name): return   // can't check unknown types
    typedef = lookup_type_def(env, name)
    if typedef == null: return
    if any arm.pattern is WildcardPat or VarPat: return  // wildcard covers all
    covered = {arm.pattern.tag for arm in arms if arm.pattern is VariantPat}
    missing = {v.tag for v in typedef.variants} - covered
    if missing not empty:
        error("Non-exhaustive match: missing " + join(", ", map("#"+t, missing)))
```

#### Wiring into the interpreter

```
function interpreter_eval(source, type_check=true) -> ScrapValue:
    tokens = tokenize(source)
    ast    = parse(tokens)
    if type_check:
        inferrer = TypeInferrer()
        inferrer.check(ast, builtin_type_env)   // throws TypeCheckError if type error
    return evaluator.eval(ast, builtin_env)

function interpreter_type_of(source) -> string:
    tokens = tokenize(source)
    ast    = parse(tokens)
    inferrer = TypeInferrer()
    (type, subst) = inferrer.infer(builtin_type_env, ast)
    return display_type(apply_subst(subst, type))
```

#### Milestone

```
// Type error caught before evaluation:
interpreter_eval('"hello" + 1')
→ TypeCheckError("Type mismatch: expected text, got int")

// Correct types:
interpreter_type_of("1 + 2")           → "int"
interpreter_type_of("x -> x")          → "'t0 → 't0"
interpreter_type_of("[1, 2, 3]")        → "list(int)"

// Exhaustiveness:
interpreter_eval("(| #a -> 1) #b", typeCheck=true)
→ TypeCheckError("Non-exhaustive match: missing #b")  // if type is known
```

---

## Part 6 — Integration

### Chapter 19 — The Interpreter API and REPL

**Level:** Beginner | **Effort:** ~1 h

#### Motivation

The last chapter assembles everything into a coherent public API and an interactive
REPL. This is the user-facing surface of the interpreter.

#### Interpreter struct

```
struct Interpreter {
    base_env  : Env       // builtins: to-float, list/map, etc.
    type_env  : TypeEnv   // builtin type schemes
    yard      : Yard?     // content-addressed storage (optional)
    map       : Map?      // named references (optional)
}

function new_interpreter(yard?, map?) -> Interpreter:
    return Interpreter(
        base_env  = create_builtin_env(),
        type_env  = create_builtin_type_env(),
        yard      = yard ?? LocalYard("~/.scrap/yard"),
        map       = map
    )
```

#### eval pipeline

```
function eval(interp, source, type_check=true) -> ScrapValue:
    tokens = tokenize(source)                        // Chapter 2
    ast    = parse(tokens)                           // Chapter 3
    if type_check:
        TypeInferrer.check(ast, interp.type_env)     // Chapters 15–18
    return Evaluator.eval(ast, interp.base_env, interp.yard, interp.map)

function type_of(interp, source) -> string:
    tokens = tokenize(source)
    ast    = parse(tokens)
    (type, s) = TypeInferrer.infer(interp.type_env, ast)
    return display_type(apply_subst(s, type))

function apply(interp, fn, arg) -> ScrapValue:
    return apply_value(fn, arg)    // used by platforms
```

#### Error hierarchy

```
LexError        — unexpected character, unterminated string, etc.
ParseError      — unexpected token, malformed structure
TypeCheckError  — type mismatch, unbound type variable (thrown before eval)
NameError       — unbound variable, hash not found
TypeError       — runtime type error (wrong type for operator or builtin)
MatchError      — pattern match failure
```

All errors should include a message with enough context to diagnose the problem.

#### REPL

```
function run_repl(interp):
    print("Scrapscript REPL. Ctrl-C to exit.")
    env = interp.base_env
    while true:
        print("> ", end="")
        line = read_line()
        if line == "": continue
        try:
            value = eval(interp, line, type_check=false)  // REPL: skip type checking for speed
            print(display(value))
        catch LexError(msg):   print("Lex error: " + msg)
        catch ParseError(msg): print("Parse error: " + msg)
        catch NameError(msg):  print("Name error: " + msg)
        catch TypeError(msg):  print("Type error: " + msg)
        catch MatchError(msg): print("Match error: " + msg)
```

#### Extension points

**Adding a builtin:**
```
set(interp.base_env, "my-fn", Builtin1("my-fn", v → ...))
// Also add to type_env for type checking:
bind(interp.type_env, "my-fn", mono(TFunc(TInt, TText)))
```

**Adding a platform:**
```
class MyPlatform implements Platform {
    run(interp, source):
        fn = interp.eval(source)
        // ... use fn however your platform needs
}
```

**Adding syntax:** Extend the lexer (new token kinds), extend the parser (new AST
nodes), extend the evaluator (new cases in `eval`). Each layer only needs to know about
its own level.

#### Final milestone — the full factorial program

```
source = """
factorial 10
; factorial : int -> int = | 0 -> 1 | n -> n * factorial (n - 1)
"""
eval(interp, source)  → Int(3628800)
```

Run each of the six verification criteria from the Introduction:

1. **Factorial:**
   `eval("factorial 10 ; factorial : int -> int = | 0 -> 1 | n -> n * factorial (n - 1)")`
   → `Int(3628800)`

2. **Type error before eval:**
   `eval('"hello" + 1', typeCheck=true)` → `TypeCheckError` (not a runtime error)

3. **Scrapyard round-trip:**
   ```
   ref = push(yard, encode(Int(42)))   // → "sha1~~..."
   eval("$" + ref, interp)             // → Int(42)
   ```

4. **HTTP platform:**
   ```
   source = "| _ -> #ok \"hello\""
   dispatch(interp, eval(interp, source), "/")  // → (200, "hello")
   ```

5. **Do notation:**
   ```
   source = "do x <- [1,2], y <- [3,4], [x*y] ; bind = ..."
   eval(source)  → List([Int(3), Int(4), Int(6), Int(8)])
   ```

6. **List pipeline:**
   ```
   eval("list/map (x -> x * x) [1, 2, 3, 4, 5]")
   → List([Int(1), Int(4), Int(9), Int(16), Int(25)])
   ```

---

## Summary: Key Design Decisions

| Decision | Chosen approach | Trade-off |
|----------|-----------------|-----------|
| Unary minus | Lexer emits `-` as `Minus`; parser handles negation | Simpler lexer, one ambiguity resolved at parse time |
| Lambda/case body scope | Calls `parse_pipe()` not `parse_where()` | Nested where-clauses inside lambdas need parens |
| Pattern parsing | Parse as expression, then convert | One set of precedence rules for both |
| Mutual recursion | Retry loop in `eval_where` | No need for `letrec`; forward references just work |
| Closure capture | Closures hold reference to child env | Recursive lambdas see their own binding after it's added |
| Arithmetic polymorphism | `+` is `'t → 't → 't`, not `int → int → int` | Allows any numeric type; inferred type less precise |
| Flat encoding | Records sorted by key before hashing | Canonical representation for content addressing |
| Platform design | Language has no I/O; platforms inject it | Clean separation; same program, different behavior |

---

## Source file index

| Topic | File |
|-------|------|
| Token definitions | `Scrapscript.Core/Lexer/Token.cs` |
| Lexer | `Scrapscript.Core/Lexer/Lexer.cs` |
| AST | `Scrapscript.Core/Parser/Ast.cs` |
| Parser | `Scrapscript.Core/Parser/Parser.cs` |
| Value types | `Scrapscript.Core/Eval/ScrapValue.cs` |
| Environment | `Scrapscript.Core/Eval/ScrapEnv.cs` |
| Evaluator | `Scrapscript.Core/Eval/Evaluator.cs` |
| Builtins | `Scrapscript.Core/Builtins/Builtins.cs` |
| Type nodes | `Scrapscript.Core/TypeChecker/ScrapType.cs` |
| Substitution | `Scrapscript.Core/TypeChecker/Substitution.cs` |
| Type inferrer | `Scrapscript.Core/TypeChecker/TypeInferrer.cs` |
| Builtin types | `Scrapscript.Core/TypeChecker/BuiltinTypes.cs` |
| Type env | `Scrapscript.Core/TypeChecker/TypeEnv.cs` |
| Flat encoder | `Scrapscript.Core/Serialization/FlatEncoder.cs` |
| Flat decoder | `Scrapscript.Core/Serialization/FlatDecoder.cs` |
| Scrapyard | `Scrapscript.Core/Scrapyard/LocalYard.cs` |
| HTTP platform | `Scrapscript.Core/Platforms/HttpPlatform.cs` |
| Console platform | `Scrapscript.Core/Platforms/ConsolePlatform.cs` |
| Interpreter API | `Scrapscript.Core/ScrapInterpreter.cs` |
| Tests | `Scrapscript.Tests/EvalTests.cs` |
