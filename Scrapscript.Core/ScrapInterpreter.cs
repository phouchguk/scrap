using Scrapscript.Core.Builtins;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;
using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core;

public class ScrapInterpreter
{
    private readonly ScrapEnv _globalEnv;
    private readonly TypeEnv _typeEnv;

    public ScrapInterpreter()
    {
        _globalEnv = BuiltinEnv.Create();
        _typeEnv = BuiltinTypes.Create();
    }

    public ScrapValue Eval(string source, bool typeCheck = true)
    {
        var ast = Parse(source);
        if (typeCheck)
            TypeInferrer.Check(ast, _typeEnv);
        return Evaluator.Eval(ast, _globalEnv);
    }

    // Type-check only, return the inferred type as a string
    public string TypeOf(string source)
    {
        var ast = Parse(source);
        var inferrer = new TypeInferrer();
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
        return Evaluator.Eval(ast, env);
    }
}
