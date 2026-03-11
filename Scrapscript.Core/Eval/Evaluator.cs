using System.Collections.Immutable;
using Scrapscript.Core.Parser;

namespace Scrapscript.Core.Eval;

public class ScrapTypeError(string message) : Exception(message);
public class ScrapNameError(string message) : Exception(message);
public class ScrapMatchError(string message) : Exception(message);

public class Evaluator
{
    public static ScrapValue Eval(Expr expr, ScrapEnv env)
    {
        return expr switch
        {
            IntLit i => new ScrapInt(i.Value),
            FloatLit f => new ScrapFloat(f.Value),
            TextLit t => new ScrapText(t.Value),
            BytesLit b => new ScrapBytes(b.Value),
            HoleLit => new ScrapHole(),

            Var v => env.Lookup(v.Name),
            HashRef r => throw new ScrapNameError($"Hash references not supported: ${r.Ref}"),

            ListExpr l => EvalList(l, env),
            RecordExpr r => EvalRecord(r, env),
            RecordAccess ra => EvalRecordAccess(ra, env),

            WhereExpr w => EvalWhere(w, env),
            LambdaExpr la => ScrapFunction.Lambda(la.Param, la.Body, env),
            CaseExpr ce => new ScrapCaseFunction(
                ce.Arms.Select(a => (a.Pattern, a.Body)).ToList(), env),

            ApplyExpr a => EvalApply(a, env),
            BinOpExpr b => EvalBinOp(b, env),

            ConstructorExpr c => EvalConstructor(c, env),
            TypeAnnotation ta => Eval(ta.Value, env),  // ignore type annotations at runtime
            TypeDefExpr => new ScrapHole(),             // type definitions are no-ops at runtime

            _ => throw new ScrapTypeError($"Cannot evaluate: {expr}")
        };
    }

    private static ScrapValue EvalList(ListExpr l, ScrapEnv env)
    {
        var items = l.Items.Select(item => Eval(item, env)).ToImmutableList();
        return new ScrapList(items);
    }

    private static ScrapValue EvalRecord(RecordExpr r, ScrapEnv env)
    {
        var fields = ImmutableDictionary<string, ScrapValue>.Empty;

        if (r.Spread != null)
        {
            var base_ = env.Lookup(r.Spread);
            if (base_ is not ScrapRecord baseRec)
                throw new ScrapTypeError($"Spread operator requires a record, got {base_.GetType().Name}");
            fields = baseRec.Fields;
        }

        foreach (var (field, valExpr) in r.Fields)
        {
            var val = Eval(valExpr, env);
            fields = fields.SetItem(field, val);
        }

        return new ScrapRecord(fields);
    }

    private static ScrapValue EvalRecordAccess(RecordAccess ra, ScrapEnv env)
    {
        var rec = Eval(ra.Record, env);
        if (rec is not ScrapRecord r)
            throw new ScrapTypeError($"Field access on non-record: {rec.Display()}");
        if (!r.Fields.TryGetValue(ra.Field, out var val))
            throw new ScrapNameError($"Record has no field '{ra.Field}'");
        return val;
    }

    private static ScrapValue EvalWhere(WhereExpr w, ScrapEnv env)
    {
        // Create a mutable child env for mutual recursion
        var childEnv = new ScrapEnv(env);

        // Retry loop: handle forward references by deferring bindings that fail
        // on ScrapNameError, retrying until stable.
        var remaining = new List<Binding>(w.Bindings);
        int prevCount = -1;

        while (remaining.Count > 0 && remaining.Count != prevCount)
        {
            prevCount = remaining.Count;
            var deferred = new List<Binding>();

            foreach (var binding in remaining)
            {
                try
                {
                    var val = Eval(binding.Value, childEnv);
                    var bound = MatchPattern(binding.Pattern, val, childEnv);
                    if (bound == null)
                        throw new ScrapMatchError($"Pattern binding failed: {binding.Pattern}");
                    foreach (var (name, bVal) in bound)
                        childEnv.Set(name, bVal);
                }
                catch (ScrapNameError)
                {
                    deferred.Add(binding);
                }
            }

            remaining = deferred;
        }

        if (remaining.Count > 0)
        {
            // Force error for the first unresolvable binding
            Eval(remaining[0].Value, childEnv);
        }

        return Eval(w.Body, childEnv);
    }

    private static ScrapValue EvalApply(ApplyExpr a, ScrapEnv env)
    {
        var fn = Eval(a.Fn, env);
        var arg = Eval(a.Arg, env);
        return ApplyFunction(fn, arg);
    }

    public static ScrapValue ApplyFunction(ScrapValue fn, ScrapValue arg)
    {
        return fn switch
        {
            ScrapFunction f => ApplyLambda(f, arg),
            ScrapCaseFunction cf => ApplyCaseFunction(cf, arg),
            ScrapBuiltin b => b.Apply(arg),
            ScrapBuiltin2 b2 => new ScrapBuiltinPartial(b2.Name, arg, b2.Apply),
            ScrapBuiltinPartial bp => bp.Apply(bp.First, arg),
            // Variant constructor: applying args accumulates into payload
            ScrapVariant { Payload: null } sv => new ScrapVariant(sv.Tag, arg),
            ScrapVariant sv => sv.Payload switch
            {
                ScrapList l => new ScrapVariant(sv.Tag, new ScrapList(l.Items.Add(arg))),
                var p => new ScrapVariant(sv.Tag, new ScrapList(ImmutableList.Create(p, arg)))
            },
            _ => throw new ScrapTypeError($"Cannot apply non-function: {fn.Display()}")
        };
    }

    private static ScrapValue ApplyLambda(ScrapFunction f, ScrapValue arg)
    {
        var param = f.Params[0];
        var bound = MatchPattern(param, arg, f.Closure);
        if (bound == null)
            throw new ScrapMatchError($"Pattern match failed for argument: {arg.Display()}");
        var newEnv = f.Closure.ExtendMany(bound);
        return Eval(f.Body, newEnv);
    }

    private static ScrapValue ApplyCaseFunction(ScrapCaseFunction cf, ScrapValue arg)
    {
        foreach (var (pat, body) in cf.Arms)
        {
            var bound = MatchPattern(pat, arg, cf.Closure);
            if (bound != null)
            {
                var newEnv = cf.Closure.ExtendMany(bound);
                return Eval(body, newEnv);
            }
        }
        throw new ScrapMatchError($"No matching case for: {arg.Display()}");
    }

    private static ScrapValue EvalBinOp(BinOpExpr b, ScrapEnv env)
    {
        // Short-circuit for >+ (cons) to avoid evaluating right as head
        if (b.Op == ">>")
        {
            var f = Eval(b.Left, env);
            var g = Eval(b.Right, env);
            // Return a composed function
            return new ScrapBuiltin($"({b.Left}>>{b.Right})", arg =>
                ApplyFunction(g, ApplyFunction(f, arg)));
        }

        var left = Eval(b.Left, env);
        var right = Eval(b.Right, env);

        return b.Op switch
        {
            "+" => Add(left, right),
            "-" => Sub(left, right),
            "*" => Mul(left, right),
            "++" => Concat(left, right),
            "+<" => Append(left, right),
            ">+" => Cons(left, right),
            _ => throw new ScrapTypeError($"Unknown operator: {b.Op}")
        };
    }

    private static ScrapValue Add(ScrapValue l, ScrapValue r) => (l, r) switch
    {
        (ScrapInt a, ScrapInt b) => new ScrapInt(a.Value + b.Value),
        (ScrapFloat a, ScrapFloat b) => new ScrapFloat(a.Value + b.Value),
        _ => throw new ScrapTypeError($"Type error: cannot add {l.Display()} and {r.Display()}")
    };

    private static ScrapValue Sub(ScrapValue l, ScrapValue r) => (l, r) switch
    {
        (ScrapInt a, ScrapInt b) => new ScrapInt(a.Value - b.Value),
        (ScrapFloat a, ScrapFloat b) => new ScrapFloat(a.Value - b.Value),
        _ => throw new ScrapTypeError($"Type error: cannot subtract {l.Display()} and {r.Display()}")
    };

    private static ScrapValue Mul(ScrapValue l, ScrapValue r) => (l, r) switch
    {
        (ScrapInt a, ScrapInt b) => new ScrapInt(a.Value * b.Value),
        (ScrapFloat a, ScrapFloat b) => new ScrapFloat(a.Value * b.Value),
        _ => throw new ScrapTypeError($"Type error: cannot multiply {l.Display()} and {r.Display()}")
    };

    private static ScrapValue Concat(ScrapValue l, ScrapValue r) => (l, r) switch
    {
        (ScrapText a, ScrapText b) => new ScrapText(a.Value + b.Value),
        (ScrapList a, ScrapList b) => new ScrapList(a.Items.AddRange(b.Items)),
        (ScrapBytes a, ScrapBytes b) => new ScrapBytes(a.Value.Concat(b.Value).ToArray()),
        _ => throw new ScrapTypeError($"Type error: cannot concat {l.Display()} and {r.Display()}")
    };

    private static ScrapValue Append(ScrapValue l, ScrapValue r) => (l, r) switch
    {
        (ScrapList list, _) => new ScrapList(list.Items.Add(r)),
        (ScrapBytes a, ScrapBytes b) => new ScrapBytes(a.Value.Concat(b.Value).ToArray()),
        _ => throw new ScrapTypeError($"Type error: +< requires a list, got {l.Display()}")
    };

    private static ScrapValue Cons(ScrapValue l, ScrapValue r) => r switch
    {
        ScrapList list => new ScrapList(list.Items.Insert(0, l)),
        _ => throw new ScrapTypeError($"Type error: >+ requires a list on the right, got {r.Display()}")
    };

    private static ScrapValue EvalConstructor(ConstructorExpr c, ScrapEnv env)
    {
        // #tag or type::variant
        if (c.TypeExpr is Var v && v.Name == "__variant__")
        {
            // This is a #tag expression used as a value
            // Return a variant with no payload (or a function that takes payload)
            return new ScrapVariant(c.Variant, null);
        }
        // type::variant — evaluate type, apply variant
        // At runtime, we just create the variant; type checking is optional
        return new ScrapVariant(c.Variant, null);
    }

    // ── Pattern matching ──────────────────────────────────────────────────────

    /// Returns a list of (name, value) bindings if pattern matches, null if no match.
    public static List<(string, ScrapValue)>? MatchPattern(Pattern pat, ScrapValue val, ScrapEnv env)
    {
        var bindings = new List<(string, ScrapValue)>();
        if (MatchInto(pat, val, bindings, env))
            return bindings;
        return null;
    }

    private static bool MatchInto(Pattern pat, ScrapValue val, List<(string, ScrapValue)> bindings, ScrapEnv env)
    {
        return pat switch
        {
            WildcardPat => true,
            VarPat v => Bind(v.Name, val, bindings),
            HolePat => val is ScrapHole,
            IntPat i => val is ScrapInt si && si.Value == i.Value,
            TextPat t => MatchText(t, val, bindings),
            BytesPat b => val is ScrapBytes sb && sb.Value.SequenceEqual(b.Value),
            ListPat l => MatchList(l, val, bindings, env),
            ConsPat c => MatchCons(c, val, bindings, env),
            RecordPat r => MatchRecord(r, val, bindings, env),
            VariantPat v => MatchVariant(v, val, bindings, env),
            _ => false
        };
    }

    private static bool Bind(string name, ScrapValue val, List<(string, ScrapValue)> bindings)
    {
        bindings.Add((name, val));
        return true;
    }

    private static bool MatchText(TextPat t, ScrapValue val, List<(string, ScrapValue)> bindings)
    {
        if (val is not ScrapText st) return false;
        if (t.RestName == null)
            return st.Value == t.Prefix;
        if (!st.Value.StartsWith(t.Prefix)) return false;
        bindings.Add((t.RestName, new ScrapText(st.Value[t.Prefix.Length..])));
        return true;
    }

    private static bool MatchList(ListPat l, ScrapValue val, List<(string, ScrapValue)> bindings, ScrapEnv env)
    {
        if (val is not ScrapList sl) return false;
        if (l.Tail == null)
        {
            if (sl.Items.Count != l.Items.Count) return false;
            for (int i = 0; i < l.Items.Count; i++)
                if (!MatchInto(l.Items[i], sl.Items[i], bindings, env)) return false;
            return true;
        }
        else
        {
            if (sl.Items.Count < l.Items.Count) return false;
            for (int i = 0; i < l.Items.Count; i++)
                if (!MatchInto(l.Items[i], sl.Items[i], bindings, env)) return false;
            bindings.Add((l.Tail, new ScrapList(sl.Items.Skip(l.Items.Count).ToImmutableList())));
            return true;
        }
    }

    private static bool MatchCons(ConsPat c, ScrapValue val, List<(string, ScrapValue)> bindings, ScrapEnv env)
    {
        if (val is not ScrapList sl || sl.Items.Count == 0) return false;
        if (!MatchInto(c.Head, sl.Items[0], bindings, env)) return false;
        return MatchInto(c.Tail, new ScrapList(sl.Items.Skip(1).ToImmutableList()), bindings, env);
    }

    private static bool MatchRecord(RecordPat r, ScrapValue val, List<(string, ScrapValue)> bindings, ScrapEnv env)
    {
        if (val is not ScrapRecord sr) return false;

        foreach (var (field, pat) in r.Fields)
        {
            if (!sr.Fields.TryGetValue(field, out var fieldVal)) return false;
            if (!MatchInto(pat, fieldVal, bindings, env)) return false;
        }

        if (r.Spread != null)
        {
            // Bind spread to record with remaining fields
            var remaining = sr.Fields;
            foreach (var (field, _) in r.Fields)
                remaining = remaining.Remove(field);
            bindings.Add((r.Spread, new ScrapRecord(remaining)));
        }
        else
        {
            // Exact match: no extra fields allowed
            if (sr.Fields.Count != r.Fields.Count) return false;
        }

        return true;
    }

    private static bool MatchVariant(VariantPat v, ScrapValue val, List<(string, ScrapValue)> bindings, ScrapEnv env)
    {
        if (val is not ScrapVariant sv) return false;
        if (sv.Tag != v.Tag) return false;
        if (v.Payload == null) return sv.Payload == null;
        if (sv.Payload == null) return false;
        return MatchInto(v.Payload, sv.Payload, bindings, env);
    }
}
