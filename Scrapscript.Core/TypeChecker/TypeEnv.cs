using System.Collections.Immutable;
using Scrapscript.Core.Parser;

namespace Scrapscript.Core.TypeChecker;

// ── Type definition for a named type ─────────────────────────────────────────

public record VariantDef(string Tag, ImmutableList<ScrapType> PayloadTypes);

public record TypeDef(
    string Name,
    ImmutableList<string> TypeParams,
    ImmutableList<VariantDef> Variants);

// ── Type environment ──────────────────────────────────────────────────────────

public class TypeEnv
{
    private readonly Dictionary<string, TypeScheme> _vars = new();
    private readonly Dictionary<string, TypeDef> _types = new();
    private readonly TypeEnv? _parent;

    public TypeEnv(TypeEnv? parent = null) => _parent = parent;

    // Variable bindings

    public TypeScheme Lookup(string name)
    {
        if (_vars.TryGetValue(name, out var scheme)) return scheme;
        if (_parent != null) return _parent.Lookup(name);
        throw new TypeCheckError($"Unbound variable: {name}");
    }

    public bool TryLookup(string name, out TypeScheme? scheme)
    {
        if (_vars.TryGetValue(name, out scheme)) return true;
        if (_parent != null) return _parent.TryLookup(name, out scheme);
        scheme = null;
        return false;
    }

    public void Bind(string name, TypeScheme scheme) => _vars[name] = scheme;
    public void BindMono(string name, ScrapType type) => Bind(name, TypeScheme.Mono(type));

    // Type definitions

    public void AddTypeDef(TypeDef def) => _types[def.Name] = def;

    public TypeDef? LookupTypeDef(string name)
    {
        if (_types.TryGetValue(name, out var def)) return def;
        return _parent?.LookupTypeDef(name);
    }

    // Look up which named type owns a given variant tag
    public TypeDef? FindTypeForTag(string tag)
    {
        foreach (var def in _types.Values)
            if (def.Variants.Any(v => v.Tag == tag))
                return def;
        return _parent?.FindTypeForTag(tag);
    }

    // All free type variables in the environment (for generalization)
    public HashSet<string> FreeVars()
    {
        var result = new HashSet<string>();
        foreach (var scheme in _vars.Values)
            foreach (var v in scheme.Type.FreeVars())
                if (!scheme.Quantified.Contains(v))
                    result.Add(v);
        if (_parent != null)
            result.UnionWith(_parent.FreeVars());
        return result;
    }

    public TypeEnv ApplySubst(Substitution s)
    {
        var env = new TypeEnv(_parent?.ApplySubst(s));
        foreach (var (name, scheme) in _vars)
            env._vars[name] = new TypeScheme(scheme.Quantified, scheme.Type.Apply(s));
        foreach (var (name, def) in _types)
            env._types[name] = def;
        return env;
    }

    public TypeScheme Generalize(ScrapType type, Substitution s)
    {
        var resolved = type.Apply(s);
        var envFreeVars = ApplySubst(s).FreeVars();
        var quantified = resolved.FreeVars()
            .Except(envFreeVars)
            .ToImmutableList();
        return new TypeScheme(quantified, resolved);
    }

    // ── Convert AST TypeExpr → ScrapType ─────────────────────────────────────

    public ScrapType ConvertTypeExpr(TypeExpr typeExpr, IReadOnlyList<string> typeParams)
    {
        return typeExpr switch
        {
            NamedType { Name: "int" } => TInt.Instance,
            NamedType { Name: "float" } => TFloat.Instance,
            NamedType { Name: "text" } => TText.Instance,
            NamedType { Name: "bytes" } => TBytes.Instance,
            NamedType { Name: "bool" } => new TName("bool"),
            NamedType n when typeParams.Contains(n.Name) => new TVar(n.Name),
            NamedType n => new TName(n.Name),
            FuncType f => new TFunc(
                ConvertTypeExpr(f.From, typeParams),
                ConvertTypeExpr(f.To, typeParams)),
            RecordTypeExpr r => new TRecord(r.Fields
                .ToImmutableDictionary(
                    f => f.Field,
                    f => ConvertTypeExpr(f.Type, typeParams))),
            ListTypeExpr l => new TList(ConvertTypeExpr(l.ElementType, typeParams)),
            ApplyType a => ApplyGenericType(a, typeParams),
            VariantType vt => RegisterInlineVariantType(vt, typeParams),
            _ => throw new TypeCheckError($"Cannot convert type expression: {typeExpr}")
        };
    }

    private TName RegisterInlineVariantType(VariantType vt, IReadOnlyList<string> typeParams)
    {
        var name = "$anon_" + string.Join("_", vt.Variants.Select(v => v.Tag));
        if (LookupTypeDef(name) == null)
        {
            var variantDefs = vt.Variants.Select(v =>
            {
                var payloads = v.Payload == null
                    ? ImmutableList<ScrapType>.Empty
                    : ImmutableList.Create(ConvertTypeExpr(v.Payload, typeParams));
                return new VariantDef(v.Tag, payloads);
            }).ToImmutableList();
            AddTypeDef(new TypeDef(name, ImmutableList<string>.Empty, variantDefs));
        }
        return new TName(name);
    }

    private ScrapType ApplyGenericType(ApplyType a, IReadOnlyList<string> typeParams)
    {
        // ApplyType chains: ApplyType(ApplyType(fn, arg1), arg2) etc.
        var args = new List<ScrapType>();
        TypeExpr current = a;
        while (current is ApplyType at)
        {
            args.Insert(0, ConvertTypeExpr(at.Arg, typeParams));
            current = at.Fn;
        }
        // current is the base type name
        var baseName = current is NamedType n ? n.Name : throw new TypeCheckError("Expected named type");
        return new TName(baseName, args.ToImmutableList());
    }

    // Extract a TypeDef from a TypeDefExpr's TypeExpr (which may be generic x => y => ...)
    public TypeDef ExtractTypeDef(string name, TypeExpr typeExpr)
    {
        var typeParams = new List<string>();
        var body = typeExpr;

        // Unwrap generic parameters: x => y => z => ...
        while (body is GenericType g)
        {
            typeParams.Add(g.Param);
            body = g.Body;
        }

        if (body is not VariantType vt)
            throw new TypeCheckError($"Type definition for '{name}' must declare variants");

        var variants = vt.Variants.Select(v =>
        {
            var payloads = new List<ScrapType>();
            if (v.Payload != null)
                CollectPayloads(v.Payload, typeParams, payloads);
            return new VariantDef(v.Tag, payloads.ToImmutableList());
        }).ToImmutableList();

        return new TypeDef(name, typeParams.ToImmutableList(), variants);
    }

    private void CollectPayloads(TypeExpr typeExpr, List<string> typeParams, List<ScrapType> payloads)
    {
        // ApplyType chains represent multiple payloads: ApplyType(ApplyType(null, int), int) = [int, int]
        if (typeExpr is ApplyType at)
        {
            CollectPayloads(at.Fn, typeParams, payloads);
            payloads.Add(ConvertTypeExpr(at.Arg, typeParams));
        }
        else
        {
            payloads.Add(ConvertTypeExpr(typeExpr, typeParams));
        }
    }
}
