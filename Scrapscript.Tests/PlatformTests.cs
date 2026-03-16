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
    public void HttpPlatform_OkVariant_Returns200()
    {
        var interp = new ScrapInterpreter();
        var fn = interp.Eval("""_ -> #ok "Hello" """, typeCheck: false);
        var (status, body) = HttpPlatform.Dispatch(interp, fn, "/");
        Assert.Equal(200, status);
        Assert.Equal("Hello", body);
    }

    [Fact]
    public void HttpPlatform_NotfoundVariant_Returns404()
    {
        var interp = new ScrapInterpreter();
        var fn = interp.Eval("""_ -> #notfound "gone" """, typeCheck: false);
        var (status, body) = HttpPlatform.Dispatch(interp, fn, "/missing");
        Assert.Equal(404, status);
        Assert.Equal("gone", body);
    }

    [Fact]
    public void HttpPlatform_CaseFunctionRoutes()
    {
        var interp = new ScrapInterpreter();
        var fn = interp.Eval("""| "/" -> #ok "home" | _ -> #notfound "nope" """, typeCheck: false);
        var (s1, b1) = HttpPlatform.Dispatch(interp, fn, "/");
        var (s2, b2) = HttpPlatform.Dispatch(interp, fn, "/other");
        Assert.Equal((200, "home"), (s1, b1));
        Assert.Equal((404, "nope"), (s2, b2));
    }

    [Fact]
    public void HttpPlatform_BadResponse_ThrowsScrapTypeError()
    {
        var interp = new ScrapInterpreter();
        var fn = interp.Eval("_ -> 42", typeCheck: false);
        Assert.Throws<ScrapTypeError>(() => HttpPlatform.Dispatch(interp, fn, "/"));
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
