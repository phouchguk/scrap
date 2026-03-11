namespace Scrapscript.Core.Parser;

// ── Expressions ──────────────────────────────────────────────────────────────

public abstract record Expr;

// Literals
public record IntLit(long Value) : Expr;
public record FloatLit(double Value) : Expr;
public record TextLit(string Value) : Expr;
public record BytesLit(byte[] Value) : Expr
{
    public virtual bool Equals(BytesLit? other) =>
        other is not null && Value.SequenceEqual(other.Value);
    public override int GetHashCode() =>
        Value.Aggregate(0, (h, b) => HashCode.Combine(h, b));
}
public record HoleLit : Expr;

// Identifiers / references
public record Var(string Name) : Expr;
public record HashRef(string Ref) : Expr;   // $sha1~~...

// Constructor: type::variant  (e.g. scoop::chocolate, #true)
public record ConstructorExpr(Expr TypeExpr, string Variant) : Expr;

// Compound
public record ListExpr(List<Expr> Items) : Expr;
public record RecordExpr(List<(string Field, Expr Value)> Fields, string? Spread) : Expr;
public record RecordAccess(Expr Record, string Field) : Expr;

// Where: expr ; name = val ; name2 = val2 ... ; name : type ...
public record WhereExpr(Expr Body, List<Binding> Bindings) : Expr;
public record Binding(Pattern Pattern, Expr Value);

// Type definition expression (used for ; name : variants)
// At runtime evaluates to a ScrapHole; provides a namespace for :: constructors.
public record TypeDefExpr(string Name, TypeExpr TypeDef) : Expr;

// Functions
public record LambdaExpr(Pattern Param, Expr Body) : Expr;
// Case function: | pat -> body | pat -> body ...
public record CaseExpr(List<CaseArm> Arms) : Expr;
public record CaseArm(Pattern Pattern, Expr Body);

// Application
public record ApplyExpr(Expr Fn, Expr Arg) : Expr;

// Binary operations
public record BinOpExpr(string Op, Expr Left, Expr Right) : Expr;

// Pipe: left |> right  becomes ApplyExpr(right, left)
// Handled in parser, no separate node needed.

// Type annotation: expr : type
public record TypeAnnotation(Expr Value, TypeExpr TypeDef) : Expr;

// ── Patterns ─────────────────────────────────────────────────────────────────

public abstract record Pattern;

public record WildcardPat : Pattern;
public record VarPat(string Name) : Pattern;
public record IntPat(long Value) : Pattern;
public record TextPat(string Prefix, string? RestName) : Pattern; // "prefix" ++ rest or just "prefix"
public record BytesPat(byte[] Value) : Pattern
{
    public virtual bool Equals(BytesPat? other) =>
        other is not null && Value.SequenceEqual(other.Value);
    public override int GetHashCode() =>
        Value.Aggregate(0, (h, b) => HashCode.Combine(h, b));
}
public record ListPat(List<Pattern> Items, string? Tail) : Pattern;   // [a, b] ++ tail or [a, b]
public record ConsPat(Pattern Head, Pattern Tail) : Pattern;           // head >+ tail
public record RecordPat(List<(string Field, Pattern Pat)> Fields, string? Spread) : Pattern;
public record VariantPat(string Tag, Pattern? Payload) : Pattern;      // #tag payload?
public record HolePat : Pattern;

// ── Type Expressions ──────────────────────────────────────────────────────────

public abstract record TypeExpr;

public record NamedType(string Name) : TypeExpr;
public record GenericType(string Param, TypeExpr Body) : TypeExpr;    // x =>
public record VariantType(List<(string Tag, TypeExpr? Payload)> Variants) : TypeExpr;
public record FuncType(TypeExpr From, TypeExpr To) : TypeExpr;
public record ApplyType(TypeExpr Fn, TypeExpr Arg) : TypeExpr;
