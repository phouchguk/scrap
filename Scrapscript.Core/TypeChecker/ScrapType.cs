using System.Collections.Immutable;

namespace Scrapscript.Core.TypeChecker;

public abstract record ScrapType
{
    public abstract ScrapType Apply(Substitution s);
    public abstract IEnumerable<string> FreeVars();
}

// ── Primitives ────────────────────────────────────────────────────────────────

public record TInt : ScrapType
{
    public static readonly TInt Instance = new();
    public override ScrapType Apply(Substitution s) => this;
    public override IEnumerable<string> FreeVars() => [];
    public override string ToString() => "int";
}

public record TFloat : ScrapType
{
    public static readonly TFloat Instance = new();
    public override ScrapType Apply(Substitution s) => this;
    public override IEnumerable<string> FreeVars() => [];
    public override string ToString() => "float";
}

public record TText : ScrapType
{
    public static readonly TText Instance = new();
    public override ScrapType Apply(Substitution s) => this;
    public override IEnumerable<string> FreeVars() => [];
    public override string ToString() => "text";
}

public record TBytes : ScrapType
{
    public static readonly TBytes Instance = new();
    public override ScrapType Apply(Substitution s) => this;
    public override IEnumerable<string> FreeVars() => [];
    public override string ToString() => "bytes";
}

public record THole : ScrapType
{
    public static readonly THole Instance = new();
    public override ScrapType Apply(Substitution s) => this;
    public override IEnumerable<string> FreeVars() => [];
    public override string ToString() => "()";
}

// ── Compound ──────────────────────────────────────────────────────────────────

public record TList(ScrapType Item) : ScrapType
{
    public override ScrapType Apply(Substitution s) => new TList(Item.Apply(s));
    public override IEnumerable<string> FreeVars() => Item.FreeVars();
    public override string ToString() => $"list({Item})";
}

public record TRecord(ImmutableDictionary<string, ScrapType> Fields) : ScrapType
{
    public override ScrapType Apply(Substitution s) =>
        new TRecord(Fields.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.Apply(s)));
    public override IEnumerable<string> FreeVars() =>
        Fields.Values.SelectMany(t => t.FreeVars());
    public override string ToString() =>
        $"{{ {string.Join(", ", Fields.Select(kv => $"{kv.Key} : {kv.Value}"))} }}";
}

public record TFunc(ScrapType From, ScrapType To) : ScrapType
{
    public override ScrapType Apply(Substitution s) => new TFunc(From.Apply(s), To.Apply(s));
    public override IEnumerable<string> FreeVars() => From.FreeVars().Concat(To.FreeVars());
    public override string ToString() => $"({From} -> {To})";
}

// ── Type variable (for inference) ─────────────────────────────────────────────

public record TVar(string Name) : ScrapType
{
    public override ScrapType Apply(Substitution s) =>
        s.TryGet(Name, out var t) ? t!.Apply(s) : this;
    public override IEnumerable<string> FreeVars() => [Name];
    public override string ToString() => Name;
}

// ── Named type (refers to a declared type, e.g. scoop, maybe, point) ─────────
// Args holds instantiated type parameters for generic types.

public record TName(string Name, ImmutableList<ScrapType> Args) : ScrapType
{
    public TName(string name) : this(name, ImmutableList<ScrapType>.Empty) { }
    public override ScrapType Apply(Substitution s) =>
        new TName(Name, Args.Select(a => a.Apply(s)).ToImmutableList());
    public override IEnumerable<string> FreeVars() => Args.SelectMany(a => a.FreeVars());
    public override string ToString() =>
        Args.Count == 0 ? Name : $"{Name}({string.Join(", ", Args)})";
}

// ── Type scheme (for let-polymorphism) ───────────────────────────────────────

public record TypeScheme(ImmutableList<string> Quantified, ScrapType Type)
{
    public static TypeScheme Mono(ScrapType t) => new(ImmutableList<string>.Empty, t);

    public ScrapType Instantiate(Func<ScrapType> fresh)
    {
        if (Quantified.Count == 0) return Type;
        var subst = new Substitution(Quantified.ToDictionary(v => v, _ => fresh()));
        return Type.Apply(subst);
    }

    public override string ToString() =>
        Quantified.Count == 0 ? Type.ToString()!
            : $"∀{string.Join(",", Quantified)}. {Type}";
}
