using System.Collections.Immutable;
using Scrapscript.Core;
using Scrapscript.Core.Eval;

namespace Scrapscript.Tests;

public class ReplFileTests
{
    // Mirrors the ResolveSource logic in Program.cs
    private static string ResolveSource(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : path;

    private static string ExamplesDir =>
        Path.Combine(AppContext.BaseDirectory, "examples");

    [Fact]
    public void FibonacciFileLoadsAndEvaluates()
    {
        var path = Path.Combine(ExamplesDir, "fibonacci.ss");
        var src = ResolveSource(path);

        var result = new ScrapInterpreter().Eval(src, typeCheck: false);

        var list = Assert.IsType<ScrapList>(result);
        var expected = ImmutableList.Create<ScrapValue>(
            new ScrapInt(0), new ScrapInt(1), new ScrapInt(1), new ScrapInt(2),
            new ScrapInt(3), new ScrapInt(5), new ScrapInt(8), new ScrapInt(13),
            new ScrapInt(21), new ScrapInt(34), new ScrapInt(55));
        Assert.Equal(new ScrapList(expected), list);
    }

    [Fact]
    public void ResolveSourceReturnsFileContentsWhenFileExists()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "1 + 1");
            Assert.Equal("1 + 1", ResolveSource(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ResolveSourceReturnsInputWhenNotAFile()
    {
        var src = "1 + 1";
        Assert.Equal(src, ResolveSource(src));
    }
}
