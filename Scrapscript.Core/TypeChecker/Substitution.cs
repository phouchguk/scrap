using System.Collections.Immutable;

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

    // fresh: factory for fresh type variables, needed for TOpenRecord+TOpenRecord unification
    public static Substitution Unify(ScrapType t1, ScrapType t2, Func<TVar>? fresh = null)
    {
        if (t1 == t2) return Empty;
        if (t1 is THole || t2 is THole) return Empty;
        if (t1 is TVar v1) return BindVar(v1.Name, t2);
        if (t2 is TVar v2) return BindVar(v2.Name, t1);

        return (t1, t2) switch
        {
            (TList l1, TList l2) => Unify(l1.Item, l2.Item, fresh),

            (TFunc f1, TFunc f2) =>
                Unify(f1.From, f2.From, fresh).ThenUnify(f1.To, f2.To, fresh),

            (TRecord r1, TRecord r2) => UnifyRecords(r1, r2, fresh),

            (TOpenRecord or, TRecord r)        => UnifyOpenWithClosed(or, r, fresh),
            (TRecord r,     TOpenRecord or)    => UnifyOpenWithClosed(or, r, fresh),
            (TOpenRecord or1, TOpenRecord or2) => UnifyOpenRecords(or1, or2, fresh),

            (TName n1, TName n2) when n1.Name == n2.Name && n1.Args.Count == n2.Args.Count =>
                n1.Args.Zip(n2.Args).Aggregate(Empty, (s, pair) =>
                    s.Compose(Unify(pair.First.Apply(s), pair.Second.Apply(s), fresh))),

            _ => throw new TypeCheckError($"Type mismatch: expected {t1}, got {t2}")
        };
    }

    private Substitution ThenUnify(ScrapType t1, ScrapType t2, Func<TVar>? fresh = null) =>
        Compose(Unify(t1.Apply(this), t2.Apply(this), fresh));

    private static Substitution UnifyRecords(TRecord r1, TRecord r2, Func<TVar>? fresh)
    {
        var s = Empty;
        foreach (var (k, v) in r1.Fields)
        {
            if (!r2.Fields.TryGetValue(k, out var v2))
                throw new TypeCheckError($"Record missing field '{k}'");
            s = s.Compose(Unify(v.Apply(s), v2.Apply(s), fresh));
        }
        return s;
    }

    // All open-record fields must exist in the closed record with matching types.
    // Row var is bound to THole, causing TOpenRecord.Apply to collapse to TRecord.
    private static Substitution UnifyOpenWithClosed(TOpenRecord or, TRecord r, Func<TVar>? fresh)
    {
        var s = Empty;
        foreach (var (field, openType) in or.Fields)
        {
            if (!r.Fields.TryGetValue(field, out var closedType))
                throw new TypeCheckError($"Record is missing required field '{field}'");
            s = s.Compose(Unify(openType.Apply(s), closedType.Apply(s), fresh));
        }
        s = s.Compose(BindVar(or.RowVar, THole.Instance));
        return s;
    }

    // Unify two open records: unify shared fields; route unique fields through the other's row var.
    // A fresh shared row variable is created for the combined tail.
    private static Substitution UnifyOpenRecords(TOpenRecord or1, TOpenRecord or2, Func<TVar>? fresh)
    {
        var s = Empty;

        foreach (var field in or1.Fields.Keys.Intersect(or2.Fields.Keys))
        {
            var su = Unify(or1.Fields[field].Apply(s), or2.Fields[field].Apply(s), fresh);
            s = s.Compose(su);
        }

        var only1 = or1.Fields
            .Where(kv => !or2.Fields.ContainsKey(kv.Key))
            .ToImmutableDictionary(kv => kv.Key, kv => kv.Value.Apply(s));
        var only2 = or2.Fields
            .Where(kv => !or1.Fields.ContainsKey(kv.Key))
            .ToImmutableDictionary(kv => kv.Key, kv => kv.Value.Apply(s));

        // Shared tail: use fresh() if available, otherwise fall back to or1's row var
        var sharedRow = fresh != null ? fresh().Name : or1.RowVar;

        // or1's row extends to include or2's unique fields (and the shared tail)
        if (only2.Count > 0 || sharedRow != or1.RowVar)
            s = s.Compose(BindVar(or1.RowVar, new TOpenRecord(only2, sharedRow)));

        // or2's row extends to include or1's unique fields (and the shared tail)
        if (only1.Count > 0 || or2.RowVar != sharedRow)
            s = s.Compose(BindVar(or2.RowVar, new TOpenRecord(only1, sharedRow)));

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
