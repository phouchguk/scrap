using Scrapscript.Core.Builtins;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;

namespace Scrapscript.Core;

public class ScrapInterpreter
{
    private readonly ScrapEnv _globalEnv;

    public ScrapInterpreter()
    {
        _globalEnv = BuiltinEnv.Create();
    }

    public ScrapValue Eval(string source)
    {
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var ast = parser.ParseProgram();
        return Evaluator.Eval(ast, _globalEnv);
    }

    public static ScrapValue EvalWithEnv(string source, ScrapEnv env)
    {
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var ast = parser.ParseProgram();
        return Evaluator.Eval(ast, env);
    }
}
