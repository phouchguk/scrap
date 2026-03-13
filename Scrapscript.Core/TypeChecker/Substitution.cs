namespace Scrapscript.Core.TypeChecker;

public class TypeCheckError(string message) : Exception(message);

public class Substitution
{
    public static readonly Substitution Empty = new([]);
    private readonly Dictionary<string, ScrapType> _map;

    public Substitution(Dictionary<string, ScrapType> map) => _map = map;

    public bool TryGet(string name, out ScrapType? type) => _map.TryGetValue(name, out type);

    // Compose: apply s2 to all values in this, then merge (this has priority)
    public Substitution Compose(Substitution s2)
    {
        var result = _map.ToDictionary(kv => kv.Key, kv => kv.Value.Apply(s2));
        foreach (var (k, v) in s2._map)
            result.TryAdd(k, v);
        return new Substitution(result);
    }

    // ── Unification ───────────────────────────────────────────────────────────

    public static Substitution Unify(ScrapType t1, ScrapType t2)
    {
        if (t1 == t2) return Empty;
        if (t1 is TVar v1) return BindVar(v1.Name, t2);
        if (t2 is TVar v2) return BindVar(v2.Name, t1);

        return (t1, t2) switch
        {
            (TList l1, TList l2) => Unify(l1.Item, l2.Item),

            (TFunc f1, TFunc f2) =>
                Unify(f1.From, f2.From).ThenUnify(f1.To, f2.To),

            (TRecord r1, TRecord r2) => UnifyRecords(r1, r2),

            (TName n1, TName n2) when n1.Name == n2.Name && n1.Args.Count == n2.Args.Count =>
                n1.Args.Zip(n2.Args).Aggregate(Empty, (s, pair) =>
                    s.Compose(Unify(pair.First.Apply(s), pair.Second.Apply(s)))),

            _ => throw new TypeCheckError($"Type mismatch: expected {t1}, got {t2}")
        };
    }

    private Substitution ThenUnify(ScrapType t1, ScrapType t2) =>
        Compose(Unify(t1.Apply(this), t2.Apply(this)));

    private static Substitution UnifyRecords(TRecord r1, TRecord r2)
    {
        var s = Empty;
        // All fields in r1 must exist in r2 with matching types
        foreach (var (k, v) in r1.Fields)
        {
            if (!r2.Fields.TryGetValue(k, out var v2))
                throw new TypeCheckError($"Record missing field '{k}'");
            s = s.Compose(Unify(v.Apply(s), v2.Apply(s)));
        }
        // If not doing spread, check no extra fields either
        // (record patterns may be partial, so we allow r2 to have extra fields)
        return s;
    }

    private static Substitution BindVar(string name, ScrapType type)
    {
        if (type is TVar v && v.Name == name) return Empty;
        if (type.FreeVars().Contains(name))
            throw new TypeCheckError($"Infinite type: {name} occurs in {type}");
        return new Substitution(new Dictionary<string, ScrapType> { [name] = type });
    }
}
