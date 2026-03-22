using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Platforms;
using Scrapscript.Core.TypeChecker;
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
    public void ConsolePlatform_NonTextOutput_ThrowsTypeCheckError()
    {
        var platform = new ConsolePlatform(TextWriter.Null);
        Assert.Throws<TypeCheckError>(() =>
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

    // ── Platform type contract tests ──────────────────────────────────────────

    [Fact]
    public void ConsolePlatform_TypeCheck_AcceptsTextOutput()
    {
        var interp = new ScrapInterpreter();
        interp.CheckAgainstPlatform("""_ -> "hello" """, new ConsolePlatform());
        // no exception = pass
    }

    [Fact]
    public void ConsolePlatform_TypeCheck_RejectsIntOutput()
    {
        var interp = new ScrapInterpreter();
        var ex = Assert.Throws<TypeCheckError>(() =>
            interp.CheckAgainstPlatform("_ -> 42", new ConsolePlatform()));
        Assert.Contains("int", ex.Message);
        Assert.Contains("text", ex.Message);
    }

    [Fact]
    public void HttpPlatform_TypeCheck_AcceptsFullHandler()
    {
        var interp = new ScrapInterpreter();
        interp.CheckAgainstPlatform(
            """| "/" -> #ok "home" | "/about" -> #ok "about" | _ -> #notfound "nope" """,
            new HttpPlatform());
        // no exception = pass
    }

    [Fact]
    public void HttpPlatform_TypeCheck_RejectsWrongPayloadType()
    {
        var interp = new ScrapInterpreter();
        Assert.Throws<TypeCheckError>(() =>
            interp.CheckAgainstPlatform("_ -> #ok 42", new HttpPlatform()));
    }

    [Fact]
    public void HttpPlatform_TypeCheck_RejectsIntReturn()
    {
        var interp = new ScrapInterpreter();
        var ex = Assert.Throws<TypeCheckError>(() =>
            interp.CheckAgainstPlatform("_ -> 42", new HttpPlatform()));
        Assert.Contains("http-result", ex.Message);
    }

    [Fact]
    public void PlatformTypes_RuntimeCheck_AcceptsCorrectVariant()
    {
        var interp = new ScrapInterpreter();
        new HttpPlatform().RegisterTypes(interp.TypeEnv);
        var outputType = new HttpPlatform().OutputType;
        PlatformTypes.RuntimeCheck(new ScrapVariant("ok", new ScrapText("hello")), outputType, interp.TypeEnv);
        // no exception = pass
    }

    [Fact]
    public void PlatformTypes_RuntimeCheck_RejectsWrongVariantPayload()
    {
        var interp = new ScrapInterpreter();
        new HttpPlatform().RegisterTypes(interp.TypeEnv);
        var outputType = new HttpPlatform().OutputType;
        Assert.Throws<ScrapTypeError>(() =>
            PlatformTypes.RuntimeCheck(new ScrapVariant("ok", new ScrapInt(42)), outputType, interp.TypeEnv));
    }

    [Fact]
    public void PlatformTypes_RuntimeCheck_RejectsUnknownTag()
    {
        var interp = new ScrapInterpreter();
        new HttpPlatform().RegisterTypes(interp.TypeEnv);
        var outputType = new HttpPlatform().OutputType;
        Assert.Throws<ScrapTypeError>(() =>
            PlatformTypes.RuntimeCheck(new ScrapVariant("redirect", new ScrapText("/home")), outputType, interp.TypeEnv));
    }
}
