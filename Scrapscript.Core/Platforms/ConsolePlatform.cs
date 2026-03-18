using Scrapscript.Core.Eval;

namespace Scrapscript.Core.Platforms;

public class ConsolePlatform(TextWriter? output = null) : IPlatform
{
    private readonly TextWriter _out = output ?? Console.Out;

    public void Run(ScrapInterpreter interpreter, string source)
    {
        var program = interpreter.Eval(source, typeCheck: false);
        var result = interpreter.Apply(program, new ScrapHole());

        if (result is ScrapText text)
            _out.Write(text.Value + "\n");
        else
            throw new ScrapTypeError(
                $"Console platform expects text output, got: {result.Display()}");
    }
}
