using System.Collections.Immutable;

namespace Scrapscript.Core.TypeChecker;

public static class BuiltinTypes
{
    public static TypeEnv Create()
    {
        var env = new TypeEnv();

        // Built-in type definitions
        env.AddTypeDef(new TypeDef("maybe",
            ImmutableList.Create("a"),
            ImmutableList.Create(
                new VariantDef("just",    ImmutableList.Create<ScrapType>(new TVar("a"))),
                new VariantDef("nothing", ImmutableList<ScrapType>.Empty))));

        env.AddTypeDef(new TypeDef("result",
            ImmutableList.Create("a", "b"),
            ImmutableList.Create(
                new VariantDef("ok",  ImmutableList.Create<ScrapType>(new TVar("a"))),
                new VariantDef("err", ImmutableList.Create<ScrapType>(new TVar("b"))))));

        env.AddTypeDef(new TypeDef("bool",
            ImmutableList<string>.Empty,
            ImmutableList.Create(
                new VariantDef("true",  ImmutableList<ScrapType>.Empty),
                new VariantDef("false", ImmutableList<ScrapType>.Empty))));

        // Helper to build curried function type: a -> b -> c
        static ScrapType Func(params ScrapType[] types) =>
            types.Reverse().Aggregate((acc, t) => (ScrapType)new TFunc(t, acc));

        var a = new TVar("a");
        var listA = new TList(a);
        var maybeA = new TName("maybe", ImmutableList.Create<ScrapType>(a));

        // Type conversion
        env.Bind("to-float",  Scheme([], new TFunc(TInt.Instance, TFloat.Instance)));
        env.Bind("round",     Scheme([], new TFunc(TFloat.Instance, TInt.Instance)));
        env.Bind("ceil",      Scheme([], new TFunc(TFloat.Instance, TInt.Instance)));
        env.Bind("floor",     Scheme([], new TFunc(TFloat.Instance, TInt.Instance)));

        // Bytes
        env.Bind("bytes/to-utf8-text", Scheme([], new TFunc(TBytes.Instance, TText.Instance)));

        // List module — polymorphic
        env.Bind("list/first",  Scheme(["a"], new TFunc(listA, maybeA)));
        env.Bind("list/length", Scheme(["a"], new TFunc(listA, TInt.Instance)));
        env.Bind("list/repeat", Scheme(["a"], Func(TInt.Instance, a, listA)));

        var b2 = new TVar("b");
        var listB2 = new TList(b2);
        var bool_ = new TName("bool");
        env.Bind("list/map",    Scheme(["a", "b"], Func(new TFunc(a, b2), listA, listB2)));
        env.Bind("list/filter", Scheme(["a"],       Func(new TFunc(a, bool_), listA, listA)));
        env.Bind("list/fold",   Scheme(["a", "b"],  Func(new TFunc(b2, new TFunc(a, b2)), b2, listA, b2)));

        // Text module
        env.Bind("text/length", Scheme([], new TFunc(TText.Instance, TInt.Instance)));
        env.Bind("text/repeat", Scheme([], Func(TInt.Instance, TText.Instance, TText.Instance)));

        // Maybe module — polymorphic
        env.Bind("maybe/default", Scheme(["a"], Func(a, maybeA, a)));

        // String/join
        env.Bind("string/join",
            Scheme([], Func(TText.Instance, new TList(TText.Instance), TText.Instance)));

        // Dict/get — key is text, dict is record (opaque), returns maybe
        var b = new TVar("b");
        env.Bind("dict/get",
            Scheme(["b"], Func(TText.Instance, new TVar("__dict__"), new TName("maybe", ImmutableList.Create<ScrapType>(b)))));

        // int/float math
        var numVar = new TVar("n");
        env.Bind("abs", Scheme(["n"], new TFunc(numVar, numVar)));
        env.Bind("min", Scheme(["n"], Func(numVar, numVar, numVar)));
        env.Bind("max", Scheme(["n"], Func(numVar, numVar, numVar)));

        // list module (continued)
        env.Bind("list/reverse", Scheme(["a"], new TFunc(listA, listA)));
        env.Bind("list/sort",    Scheme(["a"], new TFunc(listA, listA)));
        env.Bind("list/zip",     Scheme(["a", "b"], Func(listA, listB2, new TList(new TList(new TVar("a"))))));

        // text module (continued)
        env.Bind("text/trim",     Scheme([], new TFunc(TText.Instance, TText.Instance)));
        env.Bind("text/split",    Scheme([], Func(TText.Instance, TText.Instance, new TList(TText.Instance))));
        env.Bind("text/to-upper", Scheme([], new TFunc(TText.Instance, TText.Instance)));
        env.Bind("text/to-lower", Scheme([], new TFunc(TText.Instance, TText.Instance)));

        // Boolean values
        var boolType = new TName("bool");
        env.Bind("true",  Scheme([], boolType));
        env.Bind("false", Scheme([], boolType));

        return env;

        static TypeScheme Scheme(string[] vars, ScrapType type) =>
            new TypeScheme(vars.ToImmutableList(), type);
    }
}
