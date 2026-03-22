using Scrapscript.Core.Eval;
using Scrapscript.Core.TypeChecker;

namespace Scrapscript.Core.Platforms;

public class ConsolePlatform(TextWriter? output = null) : IPlatform
{
    private readonly TextWriter _out = output ?? Console.Out;

    public ScrapType InputType  => THole.Instance;
    public ScrapType OutputType => TText.Instance;
    public void RegisterTypes(TypeEnv env) { }

    public void Run(ScrapInterpreter interpreter, string source)
    {
        interpreter.CheckAgainstPlatform(source, this);
        var program = interpreter.Eval(source, typeCheck: false);
        var result = interpreter.Apply(program, new ScrapHole());
        PlatformTypes.RuntimeCheck(result, OutputType, interpreter.TypeEnv);
        _out.Write(((ScrapText)result).Value + "\n");
    }
}
