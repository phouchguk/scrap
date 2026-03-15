using Scrapscript.Core.Builtins;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;
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

