using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Platforms;
using Xunit;

namespace Scrapscript.Tests;

public class PlatformTests
{
    [Fact]
    public void ConsolePlatform_PrintsText()
    {
        var output = new StringWriter();
        var platform = new ConsolePlatform(output);
        platform.Run(new ScrapInterpreter(), """_ -> "hello" """);
        Assert.Equal("hello\n", output.ToString());
    }

    [Fact]
    public void ConsolePlatform_WithWhereBindings()
    {
        var output = new StringWriter();
        var platform = new ConsolePlatform(output);
        platform.Run(new ScrapInterpreter(), """_ -> "hello, " ++ name ; name = "world" """);
        Assert.Equal("hello, world\n", output.ToString());
    }

    [Fact]
    public void ConsolePlatform_NonTextOutput_ThrowsScrapTypeError()
    {
        var platform = new ConsolePlatform(TextWriter.Null);
        Assert.Throws<ScrapTypeError>(() =>
            platform.Run(new ScrapInterpreter(), "_ -> 42"));
    }

    [Fact]
    public void ScrapInterpreter_Apply_WorksForLambda()
    {
        var interpreter = new ScrapInterpreter();
        var fn = interpreter.Eval("x -> x + 1", typeCheck: false);
        var result = interpreter.Apply(fn, new ScrapInt(5));
        Assert.Equal(new ScrapInt(6), result);
    }
}
