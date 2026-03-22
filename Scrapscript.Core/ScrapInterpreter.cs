using Scrapscript.Core.Builtins;
using Scrapscript.Core.Compiler;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;
using Scrapscript.Core.Platforms;
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core;

public class ScrapInterpreter
{
    private readonly ScrapEnv _globalEnv;
    private readonly TypeEnv _typeEnv;
    private readonly LocalYard? _yard;
    private readonly LocalMap? _map;

    public ScrapInterpreter(LocalYard? yard = null, LocalMap? map = null)
    {
        _globalEnv = BuiltinEnv.Create();
        _typeEnv = BuiltinTypes.Create();
        _yard = yard ?? new LocalYard();
        _map = map;
    }

    public TypeEnv TypeEnv => _typeEnv;

    public void CheckAgainstPlatform(string source, IPlatform platform)
    {
        // Platform types go in a child env so they take precedence over builtins
        // (e.g. http-result's #ok is found before result's #ok)
        var checkEnv = new TypeEnv(_typeEnv);
        platform.RegisterTypes(checkEnv);
        var ast = Parse(source);
        var inferrer = new TypeInferrer(_yard);
        var (inferredType, subst) = inferrer.Infer(checkEnv, ast);
        var resolved = inferredType.Apply(subst);
        var expected = new TFunc(platform.InputType, platform.OutputType);
        try { Substitution.Unify(resolved, expected); }
        catch (TypeCheckError)
        {
            throw new TypeCheckError(
                $"Program type '{resolved}' is incompatible with platform expecting '{expected}'");
        }
    }

    public ScrapValue Eval(string source, bool typeCheck = true, DateTimeOffset? asOf = null)
    {
        var ast = Parse(source);
        if (typeCheck)
            TypeInferrer.Check(ast, _typeEnv, _yard);
        return Evaluator.Eval(ast, _globalEnv, _yard, _map, asOf);
    }

    // Type-check only, return the inferred type as a string
    public string TypeOf(string source)
    {
        var ast = Parse(source);
        var inferrer = new TypeInferrer(_yard);
        var (type, subst) = inferrer.Infer(_typeEnv, ast);
        return type.Apply(subst).ToString()!;
    }

    // Compile source to a JS expression string. Pass includeRuntime=true to prepend the runtime.
    public string CompileToJs(string source, bool includeRuntime = true)
    {
        var ast = Parse(source);
        var expr = new JsCompiler().Compile(ast);
        return includeRuntime ? JsCompiler.Runtime + expr : expr;
    }

    public ScrapValue Apply(ScrapValue fn, ScrapValue arg)
        => Evaluator.ApplyFunction(fn, arg, _yard);

    private static Expr Parse(string source)
    {
        var tokens = new Lexer.Lexer(source).Tokenize();
        return new Parser.Parser(tokens).ParseProgram();
    }

    public static ScrapValue EvalWithEnv(string source, ScrapEnv env)
    {
        var tokens = new Lexer.Lexer(source).Tokenize();
        var ast = new Parser.Parser(tokens).ParseProgram();
        return Evaluator.Eval(ast, env, null);
    }
}

