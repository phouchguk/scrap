using System.Collections.Immutable;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Parser;
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.Serialization;

namespace Scrapscript.Core.TypeChecker;

public class TypeInferrer
{
    private int _nextVar = 0;
    private readonly LocalYard? _yard;

    public TypeInferrer(LocalYard? yard = null) { _yard = yard; }

    private TVar Fresh() => new TVar($"'t{_nextVar++}");

    // Instance wrapper that passes Fresh to Substitution.Unify for row-polymorphism support
    private Substitution Unify(ScrapType t1, ScrapType t2) => Substitution.Unify(t1, t2, Fresh);

    // ── Public entry point ────────────────────────────────────────────────────

    public static void Check(Expr expr, TypeEnv env, LocalYard? yard = null)
    {
        var inferrer = new TypeInferrer(yard);
        inferrer.Infer(env, expr);
    }

    // ── Core inference ────────────────────────────────────────────────────────

    // Returns (type, substitution)
    public (ScrapType, Substitution) Infer(TypeEnv env, Expr expr)
    {
        return expr switch
        {
            IntLit => (TInt.Instance, Substitution.Empty),
            FloatLit => (TFloat.Instance, Substitution.Empty),
            TextLit => (TText.Instance, Substitution.Empty),
            BytesLit => (TBytes.Instance, Substitution.Empty),
            HoleLit => (THole.Instance, Substitution.Empty),

            Var v => InferVar(env, v),
            HashRef r => InferHashRef(r),

            ListExpr l => InferList(env, l),
            RecordExpr r => InferRecord(env, r),
            RecordAccess ra => InferRecordAccess(env, ra),

            WhereExpr w => InferWhere(env, w),
            LambdaExpr la => InferLambda(env, la),
            CaseExpr ce => InferCase(env, ce),
            ApplyExpr a => InferApply(env, a),
            BinOpExpr b => InferBinOp(env, b),
            NegExpr n   => InferNeg(env, n),

            ConstructorExpr c => InferConstructor(env, c),
            TypeAnnotation ta => InferTypeAnnotation(env, ta),
            TypeDefExpr => (THole.Instance, Substitution.Empty),

            _ => throw new TypeCheckError($"Cannot type-check: {expr}")
        };
    }

    // ── Var ───────────────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferVar(TypeEnv env, Var v)
    {
        if (!env.TryLookup(v.Name, out var scheme))
            throw new TypeCheckError($"Unbound variable: {v.Name}");
        return (scheme!.Instantiate(Fresh), Substitution.Empty);
    }

    // ── HashRef ───────────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferHashRef(HashRef r)
    {
        if (_yard is null) return (Fresh(), Substitution.Empty);
        var flat = _yard.Pull(r.Ref);
        if (flat is null) return (Fresh(), Substitution.Empty);
        return (TypeFromValue(FlatDecoder.Decode(flat)), Substitution.Empty);
    }

    private ScrapType TypeFromValue(ScrapValue v) => v switch
    {
        ScrapInt    => TInt.Instance,
        ScrapFloat  => TFloat.Instance,
        ScrapText   => TText.Instance,
        ScrapBytes  => TBytes.Instance,
        ScrapHole   => THole.Instance,
        ScrapList l => l.Items.Count == 0 ? new TList(Fresh()) : new TList(TypeFromValue(l.Items[0])),
        ScrapRecord r => new TRecord(r.Fields.ToImmutableDictionary(kv => kv.Key, kv => TypeFromValue(kv.Value))),
        _ => Fresh()
    };

    // ── List ──────────────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferList(TypeEnv env, ListExpr l)
    {
        if (l.Items.Count == 0)
            return (new TList(Fresh()), Substitution.Empty);

        var (itemType, s) = Infer(env, l.Items[0]);
        foreach (var item in l.Items.Skip(1))
        {
            var (t, s2) = Infer(env.ApplySubst(s), item);
            var s3 = Unify(itemType.Apply(s2), t);
            s = s.Compose(s2).Compose(s3);
            itemType = itemType.Apply(s);
        }
        return (new TList(itemType.Apply(s)), s);
    }

    // ── Record ────────────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferRecord(TypeEnv env, RecordExpr r)
    {
        var s = Substitution.Empty;
        var fields = ImmutableDictionary<string, ScrapType>.Empty;

        if (r.Spread != null)
        {
            if (!env.TryLookup(r.Spread, out var baseScheme))
                throw new TypeCheckError($"Unbound variable in spread: {r.Spread}");
            var baseType = baseScheme!.Instantiate(Fresh).Apply(s);
            if (baseType is TRecord baseRec)
                fields = baseRec.Fields;
            else if (baseType is not TVar)
                throw new TypeCheckError($"Cannot spread non-record type '{baseType}'");
        }

        foreach (var (field, valExpr) in r.Fields)
        {
            var (t, s2) = Infer(env.ApplySubst(s), valExpr);
            s = s.Compose(s2);
            fields = fields.SetItem(field, t.Apply(s));
        }
        return (new TRecord(fields), s);
    }

    // ── Record access ─────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferRecordAccess(TypeEnv env, RecordAccess ra)
    {
        var (recType, s) = Infer(env, ra.Record);
        recType = recType.Apply(s);

        if (recType is TRecord r)
        {
            if (!r.Fields.TryGetValue(ra.Field, out var fieldType))
                throw new TypeCheckError($"Record has no field '{ra.Field}'");
            return (fieldType.Apply(s), s);
        }

        if (recType is TOpenRecord or)
        {
            if (or.Fields.TryGetValue(ra.Field, out var fieldType))
                return (fieldType.Apply(s), s);
            // Field not yet known — extend via the row var
            var freshField = Fresh();
            var freshRow   = Fresh();
            var sExtend = Unify(new TVar(or.RowVar),
                new TOpenRecord(ImmutableDictionary<string, ScrapType>.Empty.Add(ra.Field, freshField), freshRow.Name));
            s = s.Compose(sExtend);
            return (freshField, s);
        }

        if (recType is TVar)
        {
            // Constrain this variable to be a record with at least this field
            var freshField = Fresh();
            var freshRow   = Fresh();
            var sUnify = Unify(recType,
                new TOpenRecord(ImmutableDictionary<string, ScrapType>.Empty.Add(ra.Field, freshField), freshRow.Name));
            s = s.Compose(sUnify);
            return (freshField, s);
        }

        throw new TypeCheckError($"Cannot access field '{ra.Field}' on {recType}");
    }

    // ── Where ─────────────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferWhere(TypeEnv env, WhereExpr w)
    {
        var childEnv = new TypeEnv(env);
        var s = Substitution.Empty;

        // First pass: register all type definitions
        foreach (var binding in w.Bindings)
        {
            if (binding.Value is TypeDefExpr tde)
            {
                var typeDef = env.ExtractTypeDef(tde.Name, tde.TypeDef);
                childEnv.AddTypeDef(typeDef);
                childEnv.BindMono(tde.Name, THole.Instance);
            }
        }

        // Second pass: assign type placeholders to all value bindings (for mutual recursion).
        // For annotated bindings, use the declared type as the placeholder so that recursive
        // self-references see the precise annotated type during body inference rather than an
        // unconstrained type variable. For non-annotated bindings, use a fresh type variable.
        var placeholders = new Dictionary<string, ScrapType>();
        foreach (var binding in w.Bindings)
        {
            if (binding.Value is TypeDefExpr) continue;
            if (binding.Pattern is VarPat vp)
            {
                ScrapType placeholder;
                if (binding.Value is TypeAnnotation ta)
                {
                    var freeNames = CollectFreeTypeNames(ta.TypeDef, childEnv);
                    placeholder = childEnv.ConvertTypeExpr(ta.TypeDef, freeNames);
                }
                else
                {
                    placeholder = Fresh();
                }
                placeholders[vp.Name] = placeholder;
                childEnv.BindMono(vp.Name, placeholder);
            }
        }

        // Third pass: infer each value binding and unify with its placeholder
        foreach (var binding in w.Bindings)
        {
            if (binding.Value is TypeDefExpr) continue;

            var (valType, s2) = Infer(childEnv.ApplySubst(s), binding.Value);
            s = s.Compose(s2);

            // Bind pattern variables
            var patBindings = InferPat(binding.Pattern, valType.Apply(s), childEnv, s);
            foreach (var (name, patType) in patBindings)
            {
                // Unify with placeholder if one exists
                if (placeholders.TryGetValue(name, out var placeholder))
                {
                    var s3 = Unify(placeholder.Apply(s), patType.Apply(s));
                    s = s.Compose(s3);
                }
                // Generalize and rebind
                var scheme = childEnv.Generalize(patType, s);
                childEnv.Bind(name, scheme);
            }
        }

        // Infer body
        var (bodyType, sBody) = Infer(childEnv.ApplySubst(s), w.Body);
        return (bodyType.Apply(sBody), s.Compose(sBody));
    }

    // ── Lambda ────────────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferLambda(TypeEnv env, LambdaExpr la)
    {
        var paramType = Fresh();
        var bodyEnv = new TypeEnv(env);

        // Bind pattern variables with fresh types
        BindPatternVars(la.Param, paramType, bodyEnv);

        var (bodyType, s) = Infer(bodyEnv, la.Body);
        return (new TFunc(paramType.Apply(s), bodyType.Apply(s)), s);
    }

    // ── Type annotation ───────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferTypeAnnotation(TypeEnv env, TypeAnnotation ta)
    {
        var (inferredType, s) = Infer(env, ta.Value);
        var freeNames = CollectFreeTypeNames(ta.TypeDef, env);
        var declaredType = env.ConvertTypeExpr(ta.TypeDef, freeNames);
        var sUnify = Unify(inferredType.Apply(s), declaredType);
        return (inferredType.Apply(s).Apply(sUnify), s.Compose(sUnify));
    }

    private static readonly HashSet<string> KnownPrimitives =
        new() { "int", "float", "text", "bytes", "bool" };

    private List<string> CollectFreeTypeNames(TypeExpr typeExpr, TypeEnv env)
    {
        var names = new HashSet<string>();
        GatherNames(typeExpr, names);
        return names
            .Where(n => !KnownPrimitives.Contains(n) && env.LookupTypeDef(n) == null)
            .ToList();
    }

    private static void GatherNames(TypeExpr typeExpr, HashSet<string> names)
    {
        switch (typeExpr)
        {
            case NamedType n:      names.Add(n.Name); break;
            case FuncType f:       GatherNames(f.From, names); GatherNames(f.To, names); break;
            case ApplyType a:      GatherNames(a.Fn, names); GatherNames(a.Arg, names); break;
            case GenericType g:    GatherNames(g.Body, names); break;
            case RecordTypeExpr r: foreach (var (_, t) in r.Fields) GatherNames(t, names); break;
            case ListTypeExpr l: GatherNames(l.ElementType, names); break;
        }
    }

    // ── Case function ─────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferCase(TypeEnv env, CaseExpr ce)
    {
        var argType = (ScrapType)Fresh();
        var resultType = (ScrapType)Fresh();
        var s = Substitution.Empty;

        foreach (var arm in ce.Arms)
        {
            // Infer pattern type and bindings
            var armEnv = new TypeEnv(env.ApplySubst(s));
            BindPatternVars(arm.Pattern, argType.Apply(s), armEnv);

            // Unify pattern's constrained type with argType
            var patConstraint = PatternType(arm.Pattern, argType.Apply(s), env.ApplySubst(s));
            if (patConstraint != null)
            {
                var sUnify = Unify(argType.Apply(s), patConstraint.Apply(s));
                s = s.Compose(sUnify);
            }

            // Infer body type
            var (bodyType, sBody) = Infer(armEnv.ApplySubst(s), arm.Body);
            s = s.Compose(sBody);

            // All bodies must have the same result type
            var sResult = Unify(resultType.Apply(s), bodyType.Apply(s));
            s = s.Compose(sResult);
            resultType = resultType.Apply(s);
        }

        // Part 2: if argType is still TVar after processing all arms,
        // infer it from VariantPat arms or error on unknown tags with wildcards.
        var resolvedArgType = argType.Apply(s);
        if (resolvedArgType is TVar)
        {
            var envS = env.ApplySubst(s);
            bool hasWildcard = ce.Arms.Any(a => a.Pattern is WildcardPat or VarPat);
            var variantArms = ce.Arms
                .Where(a => a.Pattern is VariantPat)
                .Select(a => (VariantPat)a.Pattern)
                .ToList();

            if (variantArms.Count > 0)
            {
                if (hasWildcard)
                {
                    // Wildcard + unknown variant tag → error (arm is unreachable dead code)
                    foreach (var vp in variantArms)
                        if (envS.FindTypeForTag(vp.Tag) == null)
                            throw new TypeCheckError($"Unknown variant '#{vp.Tag}': declare its type before using it in a pattern");
                    // All tags known — argType stays TVar (polymorphic)
                }
                else
                {
                    // No wildcard: collect tags (with payload arities) into a closed synthetic type
                    var tags = variantArms.Select(vp => vp.Tag).ToList();
                    var syntheticName = "$anon_" + string.Join("_", tags);
                    var syntheticDef = envS.LookupTypeDef(syntheticName);
                    if (syntheticDef == null)
                    {
                        var variantDefs = variantArms.Select(vp =>
                        {
                            // Infer payload arity from the pattern
                            var arity = vp.Payload switch
                            {
                                null => 0,
                                ListPat lp => lp.Items.Count,
                                _ => 1
                            };
                            var payloadTypes = Enumerable.Range(0, arity)
                                .Select(_ => (ScrapType)Fresh())
                                .ToImmutableList();
                            return new VariantDef(vp.Tag, payloadTypes);
                        }).ToImmutableList();
                        syntheticDef = new TypeDef(syntheticName, ImmutableList<string>.Empty, variantDefs);
                        envS.AddTypeDef(syntheticDef);
                        env.AddTypeDef(syntheticDef);
                    }
                    var sNew = Unify(resolvedArgType, new TName(syntheticName, ImmutableList<ScrapType>.Empty));
                    s = s.Compose(sNew);
                }
            }
        }

        CheckRedundantArms(ce);
        CheckExhaustiveness(ce, argType.Apply(s), env.ApplySubst(s));
        return (new TFunc(argType.Apply(s), resultType.Apply(s)), s);
    }

    private static void CheckRedundantArms(CaseExpr ce)
    {
        // Track tags whose earlier arm has a catch-all payload (no payload, wildcard, or var)
        var catchAllTags = new HashSet<string>();
        var seenInts  = new HashSet<long>();
        var seenTexts = new HashSet<string>();
        foreach (var arm in ce.Arms)
        {
            switch (arm.Pattern)
            {
                case VariantPat vp:
                    if (catchAllTags.Contains(vp.Tag))
                        throw new TypeCheckError($"Redundant pattern: '#{vp.Tag}' is already covered by an earlier arm");
                    if (vp.Payload == null || vp.Payload is WildcardPat or VarPat)
                        catchAllTags.Add(vp.Tag);
                    break;
                case IntPat ip:
                    if (!seenInts.Add(ip.Value))
                        throw new TypeCheckError($"Redundant pattern: '{ip.Value}' is already covered by an earlier arm");
                    break;
                case TextPat tp when tp.RestName == null:
                    if (!seenTexts.Add(tp.Prefix))
                        throw new TypeCheckError($"Redundant pattern: '\"{tp.Prefix}\"' is already covered by an earlier arm");
                    break;
            }
        }
    }

    private void CheckExhaustiveness(CaseExpr ce, ScrapType argType, TypeEnv env)
    {
        if (argType is not TName named) return;
        var typeDef = env.LookupTypeDef(named.Name);
        if (typeDef == null) return;
        if (ce.Arms.Any(a => a.Pattern is WildcardPat or VarPat)) return;

        var covered = ce.Arms
            .Where(a => a.Pattern is VariantPat)
            .Select(a => ((VariantPat)a.Pattern).Tag)
            .ToHashSet();

        var missing = typeDef.Variants
            .Select(v => v.Tag)
            .Where(tag => !covered.Contains(tag))
            .ToList();

        if (missing.Count > 0)
        {
            var typeName = named.Name.StartsWith("$anon_")
                ? "(" + string.Join(" ", typeDef.Variants.Select(v => "#" + v.Tag)) + ")"
                : $"'{named.Name}'";
            throw new TypeCheckError(
                $"Non-exhaustive match on {typeName}: missing {string.Join(", ", missing.Select(t => "#" + t))}");
        }
    }

    // ── Application ───────────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferApply(TypeEnv env, ApplyExpr a)
    {
        var (fnType, s1) = Infer(env, a.Fn);
        // Keep a reference to the arg env — InferConstructor may register synthetic
        // inline types (e.g. ($anon_a_b_c) on it via AddTypeDef.
        var argEnv = env.ApplySubst(s1);
        var (argType, s2) = Infer(argEnv, a.Arg);
        var retType = Fresh();
        var s3 = Unify(fnType.Apply(s2), new TFunc(argType, retType));
        var s = s1.Compose(s2).Compose(s3);
        // Re-check exhaustiveness now that the argument type is concrete.
        // This catches (#a #b #c)::a |> | #a -> 1 | #b -> 2, where the case
        // expression is inferred before the constructor registers the inline type.
        if (a.Fn is CaseExpr ce)
            CheckExhaustiveness(ce, argType.Apply(s), argEnv);
        return (retType.Apply(s), s);
    }

    // ── Binary operators ──────────────────────────────────────────────────────

    private (ScrapType, Substitution) InferBinOp(TypeEnv env, BinOpExpr b)
    {
        if (b.Op == ">>")
        {
            // f >> g : (a -> b) -> (b -> c) -> (a -> c)
            var (tf, sf) = Infer(env, b.Left);
            var (tg, sg) = Infer(env.ApplySubst(sf), b.Right);
            var a = Fresh(); var bv = Fresh(); var c = Fresh();
            var s1 = Unify(tf.Apply(sg), new TFunc(a, bv));
            var s2 = Unify(tg.Apply(s1), new TFunc(bv.Apply(s1), c));
            var s = sf.Compose(sg).Compose(s1).Compose(s2);
            return (new TFunc(a.Apply(s), c.Apply(s)), s);
        }

        var (tl, sl) = Infer(env, b.Left);
        var (tr, sr) = Infer(env.ApplySubst(sl), b.Right);
        var sAll = sl.Compose(sr);

        return b.Op switch
        {
            "+" or "-" or "*" or "/" or "%" => InferArith(tl, tr, sAll),
            "++" => InferConcat(tl, tr, sAll),
            "+<" => InferAppend(tl, tr, sAll),
            ">+" => InferCons(tl, tr, sAll),
            "==" or "!=" => InferEquality(tl, tr, sAll),
            "<" or ">" or "<=" or ">=" => InferComparison(tl, tr, sAll),
            _ => throw new TypeCheckError($"Unknown operator: {b.Op}")
        };
    }

    private (ScrapType, Substitution) InferNeg(TypeEnv env, NegExpr n)
    {
        var (t, s) = Infer(env, n.Operand);
        var resolved = t.Apply(s);
        if (resolved is not TInt and not TFloat and not TVar)
            throw new TypeCheckError($"Negation requires int or float, got {resolved}");
        return (resolved, s);
    }

    private (ScrapType, Substitution) InferArith(ScrapType tl, ScrapType tr, Substitution s)
    {
        // Both operands must be the same numeric type (int or float)
        var sUnify = Unify(tl.Apply(s), tr.Apply(s));
        s = s.Compose(sUnify);
        var unified = tl.Apply(s);
        if (unified is not TInt and not TFloat and not TVar)
            throw new TypeCheckError($"Arithmetic requires int or float, got {unified}");
        return (unified, s);
    }

    private (ScrapType, Substitution) InferConcat(ScrapType tl, ScrapType tr, Substitution s)
    {
        // ++ works on text, list, or bytes — both sides must match
        var sUnify = Unify(tl.Apply(s), tr.Apply(s));
        s = s.Compose(sUnify);
        var unified = tl.Apply(s);
        if (unified is not TText and not TList and not TBytes and not TVar)
            throw new TypeCheckError($"++ requires text, list, or bytes, got {unified}");
        return (unified, s);
    }

    private (ScrapType, Substitution) InferAppend(ScrapType tl, ScrapType tr, Substitution s)
    {
        // list +< element  — or bytes +< bytes
        var tlResolved = tl.Apply(s);
        if (tlResolved is TBytes)
        {
            var sUnify = Unify(tr.Apply(s), TBytes.Instance);
            return (TBytes.Instance, s.Compose(sUnify));
        }
        // list +< element
        var itemType = Fresh();
        var s1 = Unify(tlResolved, new TList(itemType));
        s = s.Compose(s1);
        var s2 = Unify(tr.Apply(s), itemType.Apply(s));
        s = s.Compose(s2);
        return (new TList(itemType.Apply(s)), s);
    }

    private (ScrapType, Substitution) InferEquality(ScrapType tl, ScrapType tr, Substitution s)
    {
        var sUnify = Unify(tl.Apply(s), tr.Apply(s));
        return (new TName("bool"), s.Compose(sUnify));
    }

    private (ScrapType, Substitution) InferComparison(ScrapType tl, ScrapType tr, Substitution s)
    {
        var sUnify = Unify(tl.Apply(s), tr.Apply(s));
        s = s.Compose(sUnify);
        var unified = tl.Apply(s);
        if (unified is not TInt and not TFloat and not TText and not TVar)
            throw new TypeCheckError($"Ordering comparison requires int, float, or text, got {unified}");
        return (new TName("bool"), s);
    }

    private (ScrapType, Substitution) InferCons(ScrapType tl, ScrapType tr, Substitution s)
    {
        // element >+ list
        var itemType = Fresh();
        var s1 = Unify(tr.Apply(s), new TList(itemType));
        s = s.Compose(s1);
        var s2 = Unify(tl.Apply(s), itemType.Apply(s));
        s = s.Compose(s2);
        return (new TList(itemType.Apply(s)), s);
    }

    // ── Constructor (type::variant or #tag) ───────────────────────────────────

    private (ScrapType, Substitution) InferConstructor(TypeEnv env, ConstructorExpr c)
    {
        // #tag standalone (no named type)
        if (c.TypeExpr is Var v && v.Name == "__variant__")
        {
            // 1. Check for exact one-variant synthetic ($anon_tag) — highest priority
            var syntheticName = "$anon_" + c.Variant;
            var syntheticDef = env.LookupTypeDef(syntheticName);
            if (syntheticDef != null)
                return MakeConstructorType(syntheticDef, c.Variant, env);

            // 2. Check for any registered type containing this tag (named or multi-variant $anon_*)
            var namedDef = env.FindTypeForTag(c.Variant);
            if (namedDef != null)
                return MakeConstructorType(namedDef, c.Variant, env);

            // 3. Unknown tag — register a new one-variant synthetic type
            var newDef = new TypeDef(syntheticName, ImmutableList<string>.Empty,
                ImmutableList.Create(new VariantDef(c.Variant, ImmutableList<ScrapType>.Empty)));
            env.AddTypeDef(newDef);
            return MakeConstructorType(newDef, c.Variant, env);
        }

        // type::variant — look up the type
        if (c.TypeExpr is Var typeVar)
        {
            var typeDef = env.LookupTypeDef(typeVar.Name);
            if (typeDef == null)
                throw new TypeCheckError($"Unknown type: {typeVar.Name}");
            if (!typeDef.Variants.Any(v => v.Tag == c.Variant))
                throw new TypeCheckError($"Type '{typeVar.Name}' has no variant '{c.Variant}'");
            return MakeConstructorType(typeDef, c.Variant, env);
        }

        // inline type::variant — e.g. (#a #b #c)::a
        // The LHS parses as an Expr tree; reconstruct a TypeDef from it.
        var inlineVariants = new List<(string Tag, TypeExpr? Payload)>();
        if (TryCollectInlineVariants(c.TypeExpr, inlineVariants) && inlineVariants.Count > 0)
        {
            var syntheticName = "$anon_" + string.Join("_", inlineVariants.Select(v => v.Tag));
            var existing = env.LookupTypeDef(syntheticName);
            if (existing == null)
            {
                var variantDefs = inlineVariants.Select(v =>
                {
                    var payloadTypes = v.Payload == null
                        ? ImmutableList<ScrapType>.Empty
                        : ImmutableList.Create(env.ConvertTypeExpr(v.Payload, []));
                    return new VariantDef(v.Tag, payloadTypes);
                }).ToImmutableList();
                existing = new TypeDef(syntheticName, ImmutableList<string>.Empty, variantDefs);
                env.AddTypeDef(existing);
            }
            if (!existing.Variants.Any(v => v.Tag == c.Variant))
                throw new TypeCheckError($"Inline type has no variant '{c.Variant}'");
            return MakeConstructorType(existing, c.Variant, env);
        }

        return (Fresh(), Substitution.Empty);
    }

    // Walk an Expr tree produced by parsing (#a #b #c) or (#radius int) and
    // extract a flat list of variant definitions. Returns false if the expression
    // doesn't look like an inline type.
    private static bool TryCollectInlineVariants(Expr expr, List<(string Tag, TypeExpr? Payload)> variants)
    {
        switch (expr)
        {
            // Single nullary variant: #tag
            case ConstructorExpr { TypeExpr: Var { Name: "__variant__" } } c:
                variants.Add((c.Variant, null));
                return true;

            case ApplyExpr apply:
                // Two cases:
                // 1. Chain of nullary variants: (#a #b #c) → ApplyExpr(ApplyExpr(#a,#b),#c)
                //    Right side is itself a nullary constructor
                if (apply.Arg is ConstructorExpr { TypeExpr: Var { Name: "__variant__" } } rightCtor)
                {
                    if (!TryCollectInlineVariants(apply.Fn, variants)) return false;
                    variants.Add((rightCtor.Variant, null));
                    return true;
                }
                // 2. Single variant with payload: (#radius int) → ApplyExpr(#radius, Var("int"))
                if (apply.Fn is ConstructorExpr { TypeExpr: Var { Name: "__variant__" } } tagCtor)
                {
                    var payload = TryConvertExprToTypeExpr(apply.Arg);
                    if (payload == null) return false;
                    variants.Add((tagCtor.Variant, payload));
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    private static TypeExpr? TryConvertExprToTypeExpr(Expr expr) => expr switch
    {
        Var v => new NamedType(v.Name),
        _ => null
    };

    // Build the type of a constructor: either TName(T) if no payload,
    // or TFunc(payload1, TFunc(payload2, ... TName(T)))
    private (ScrapType, Substitution) MakeConstructorType(TypeDef typeDef, string tag, TypeEnv env)
    {
        // Instantiate type parameters with fresh type variables
        var freshParams = typeDef.TypeParams
            .ToDictionary(p => p, _ => (ScrapType)Fresh());
        var subst = new Substitution(freshParams);

        var resultType = (ScrapType)new TName(
            typeDef.Name,
            typeDef.TypeParams.Select(p => freshParams[p]).ToImmutableList());

        var variant = typeDef.Variants.First(v => v.Tag == tag);

        // Build curried constructor type from right to left
        ScrapType type = resultType;
        for (int i = variant.PayloadTypes.Count - 1; i >= 0; i--)
            type = new TFunc(variant.PayloadTypes[i].Apply(subst), type);

        return (type, Substitution.Empty);
    }

    // ── Pattern inference ─────────────────────────────────────────────────────

    // Returns list of (name, type) bindings introduced by the pattern
    private List<(string, ScrapType)> InferPat(Pattern pat, ScrapType matchType, TypeEnv env, Substitution s)
    {
        var bindings = new List<(string, ScrapType)>();
        CollectPatBindings(pat, matchType.Apply(s), env, s, bindings);
        return bindings;
    }

    private void CollectPatBindings(Pattern pat, ScrapType matchType, TypeEnv env, Substitution s, List<(string, ScrapType)> bindings)
    {
        switch (pat)
        {
            case WildcardPat:
                break;
            case VarPat vp:
                bindings.Add((vp.Name, matchType));
                break;
            case HolePat:
                break; // matches THole
            case IntPat:
                break; // matches TInt
            case TextPat tp:
                if (tp.RestName != null)
                    bindings.Add((tp.RestName, TText.Instance));
                break;
            case BytesPat:
                break;
            case ListPat lp:
                var itemType = matchType is TList lt ? lt.Item : Fresh();
                foreach (var item in lp.Items)
                    CollectPatBindings(item, itemType, env, s, bindings);
                if (lp.Tail != null)
                    bindings.Add((lp.Tail, new TList(itemType)));
                break;
            case ConsPat cp:
                var headType = matchType is TList lt2 ? lt2.Item : Fresh();
                CollectPatBindings(cp.Head, headType, env, s, bindings);
                CollectPatBindings(cp.Tail, new TList(headType), env, s, bindings);
                break;
            case RecordPat rp:
                var fields = matchType is TRecord rt ? rt.Fields
                    : matchType is TOpenRecord ort ? ort.Fields
                    : ImmutableDictionary<string, ScrapType>.Empty;
                foreach (var (field, fieldPat) in rp.Fields)
                {
                    var fieldType = fields.TryGetValue(field, out var ft) ? ft : Fresh();
                    CollectPatBindings(fieldPat, fieldType, env, s, bindings);
                }
                if (rp.Spread != null)
                    bindings.Add((rp.Spread, Fresh()));
                break;
            case VariantPat vp:
                // Verify the variant exists in the matched type's definition (if known)
                CheckVariantPat(vp, matchType, env);
                if (vp.Payload is ListPat mlp)
                {
                    // Multi-payload variant: bind each sub-pattern to its own payload type
                    var pts = GetVariantPayloadTypes(vp.Tag, matchType, env, mlp.Items.Count);
                    for (int i = 0; i < mlp.Items.Count; i++)
                        CollectPatBindings(mlp.Items[i], pts[i], env, s, bindings);
                }
                else if (vp.Payload != null)
                {
                    var payloadType = GetVariantPayloadType(vp.Tag, matchType, env);
                    CollectPatBindings(vp.Payload, payloadType, env, s, bindings);
                }
                break;
        }
    }

    private ScrapType? PatternType(Pattern pat, ScrapType matchType, TypeEnv env)
    {
        return pat switch
        {
            IntPat => TInt.Instance,
            TextPat => TText.Instance,
            BytesPat => TBytes.Instance,
            HolePat => THole.Instance,
            ListPat => new TList(Fresh()),
            VariantPat vp => LookupVariantNamedType(vp.Tag, env),
            _ => null
        };
    }

    private ScrapType? LookupVariantNamedType(string tag, TypeEnv env)
    {
        var typeDef = env.FindTypeForTag(tag);
        if (typeDef == null) return null;
        var freshParams = typeDef.TypeParams.ToDictionary(p => p, _ => (ScrapType)Fresh());
        return new TName(typeDef.Name,
            typeDef.TypeParams.Select(p => freshParams[p]).ToImmutableList());
    }

    private void CheckVariantPat(VariantPat vp, ScrapType matchType, TypeEnv env)
    {
        if (matchType is TName named)
        {
            var typeDef = env.LookupTypeDef(named.Name);
            if (typeDef != null && !typeDef.Variants.Any(v => v.Tag == vp.Tag))
                throw new TypeCheckError($"Type '{named.Name}' has no variant '{vp.Tag}'");
        }
        // If matchType is TVar, we can't check yet — skip
    }

    private ScrapType GetVariantPayloadType(string tag, ScrapType matchType, TypeEnv env)
    {
        if (matchType is TName named)
        {
            var typeDef = env.LookupTypeDef(named.Name);
            if (typeDef != null)
            {
                var variant = typeDef.Variants.FirstOrDefault(v => v.Tag == tag);
                if (variant != null && variant.PayloadTypes.Count == 1)
                    return variant.PayloadTypes[0];
            }
        }
        return Fresh();
    }

    private IReadOnlyList<ScrapType> GetVariantPayloadTypes(
        string tag, ScrapType matchType, TypeEnv env, int arity)
    {
        if (matchType is TName named)
        {
            var typeDef = env.LookupTypeDef(named.Name);
            var variant = typeDef?.Variants.FirstOrDefault(v => v.Tag == tag);
            if (variant != null && variant.PayloadTypes.Count == arity)
                return variant.PayloadTypes;
        }
        return Enumerable.Range(0, arity).Select(_ => (ScrapType)Fresh()).ToList();
    }

    private void BindPatternVars(Pattern pat, ScrapType matchType, TypeEnv env)
    {
        var bindings = new List<(string, ScrapType)>();
        CollectPatBindings(pat, matchType, env, Substitution.Empty, bindings);
        foreach (var (name, type) in bindings)
            env.BindMono(name, type);
    }
}
