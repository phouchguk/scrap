using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.Serialization;
using Xunit;

namespace Scrapscript.Tests;

public class MapTests : IDisposable
{
    private readonly string _yardRoot;
    private readonly string _mapRoot;
    private readonly LocalYard _yard;
    private readonly LocalMap _map;

    public MapTests()
    {
        _yardRoot = Path.Combine(Path.GetTempPath(), $"scrap-yard-{Guid.NewGuid():N}");
        _mapRoot  = Path.Combine(Path.GetTempPath(), $"scrap-map-{Guid.NewGuid():N}");
        _yard = new LocalYard(_yardRoot);
        _map  = new LocalMap(_mapRoot);
        _yard.Init();
        _map.Init();
    }

    public void Dispose()
    {
        if (Directory.Exists(_yardRoot)) Directory.Delete(_yardRoot, recursive: true);
        if (Directory.Exists(_mapRoot))  Directory.Delete(_mapRoot,  recursive: true);
    }

    // ── LocalMap.Commit / Resolve ─────────────────────────────────────────────

    [Fact]
    public void Commit_ReturnsVersionedLabel()
    {
        var hashRef = _yard.Push(FlatEncoder.Encode(new ScrapInt(1)));
        var label = _map.Commit("ns/thing", hashRef);
        Assert.Equal("ns/thing@0", label);
    }

    [Fact]
    public void Commit_AutoIncrementsVersion()
    {
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapInt(1)));
        var h2 = _yard.Push(FlatEncoder.Encode(new ScrapInt(2)));
        _map.Commit("ns/x", h1);
        var label = _map.Commit("ns/x", h2);
        Assert.Equal("ns/x@1", label);
    }

    [Fact]
    public void Resolve_Latest_ReturnsHighestVersion()
    {
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapText("Pluto")));
        var h2 = _yard.Push(FlatEncoder.Encode(new ScrapText("Neptune")));
        _map.Commit("planets", h1);
        _map.Commit("planets", h2);
        Assert.Equal(h2, _map.Resolve("planets"));
    }

    [Fact]
    public void Resolve_ByVersion_ReturnsPinnedHash()
    {
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapText("Pluto")));
        var h2 = _yard.Push(FlatEncoder.Encode(new ScrapText("Neptune")));
        _map.Commit("planets", h1);
        _map.Commit("planets", h2);
        Assert.Equal(h1, _map.Resolve("planets", version: 0));
        Assert.Equal(h2, _map.Resolve("planets", version: 1));
    }

    [Fact]
    public void Resolve_ByAsOf_ReturnsLatestBeforeDate()
    {
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapText("Pluto")));
        var h2 = _yard.Push(FlatEncoder.Encode(new ScrapText("Neptune")));
        var t0 = DateTimeOffset.UtcNow.AddSeconds(-2);
        _map.Commit("planets", h1);
        var tMid = DateTimeOffset.UtcNow;
        _map.Commit("planets", h2);
        var tEnd = DateTimeOffset.UtcNow.AddSeconds(2);

        // asOf before the second commit → should get h1
        Assert.Equal(h1, _map.Resolve("planets", asOf: tMid));
        // asOf after both → should get h2
        Assert.Equal(h2, _map.Resolve("planets", asOf: tEnd));
    }

    [Fact]
    public void Resolve_MissingName_ReturnsNull()
    {
        Assert.Null(_map.Resolve("nonexistent"));
    }

    [Fact]
    public void Resolve_MissingVersion_ReturnsNull()
    {
        var h = _yard.Push(FlatEncoder.Encode(new ScrapInt(1)));
        _map.Commit("x", h);
        Assert.Null(_map.Resolve("x", version: 99));
    }

    // ── History ───────────────────────────────────────────────────────────────

    [Fact]
    public void History_ReturnsAllEntriesInOrder()
    {
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapInt(1)));
        var h2 = _yard.Push(FlatEncoder.Encode(new ScrapInt(2)));
        var h3 = _yard.Push(FlatEncoder.Encode(new ScrapInt(3)));
        _map.Commit("seq", h1);
        _map.Commit("seq", h2);
        _map.Commit("seq", h3);

        var hist = _map.History("seq");
        Assert.Equal(3, hist.Count);
        Assert.Equal(0, hist[0].Version);
        Assert.Equal(1, hist[1].Version);
        Assert.Equal(2, hist[2].Version);
    }

    [Fact]
    public void History_Empty_ReturnsEmptyList()
    {
        Assert.Empty(_map.History("no-such-name"));
    }

    // ── Evaluator resolves Var from map ───────────────────────────────────────

    [Fact]
    public void Eval_Var_FallsBackToMap()
    {
        var hashRef = _yard.Push(FlatEncoder.Encode(new ScrapText("hello")));
        _map.Commit("greeting", hashRef);

        var interp = new ScrapInterpreter(_yard, _map);
        var result = interp.Eval("greeting", typeCheck: false);
        Assert.Equal(new ScrapText("hello"), result);
    }

    [Fact]
    public void Eval_Var_WithSlash_FallsBackToMap()
    {
        var hashRef = _yard.Push(FlatEncoder.Encode(new ScrapInt(42)));
        _map.Commit("ns/answer", hashRef);

        var interp = new ScrapInterpreter(_yard, _map);
        var result = interp.Eval("ns/answer", typeCheck: false);
        Assert.Equal(new ScrapInt(42), result);
    }

    [Fact]
    public void Eval_Var_NotInMapOrEnv_ThrowsNameError()
    {
        var interp = new ScrapInterpreter(_yard, _map);
        Assert.Throws<ScrapNameError>(() => interp.Eval("nonexistent", typeCheck: false));
    }

    // ── Evaluator resolves MapRef (versioned) ─────────────────────────────────

    [Fact]
    public void Eval_MapRef_ResolvesVersioned()
    {
        var h0 = _yard.Push(FlatEncoder.Encode(new ScrapText("Pluto")));
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapText("Neptune")));
        _map.Commit("planets", h0);
        _map.Commit("planets", h1);

        var interp = new ScrapInterpreter(_yard, _map);
        Assert.Equal(new ScrapText("Pluto"),   interp.Eval("planets@0", typeCheck: false));
        Assert.Equal(new ScrapText("Neptune"), interp.Eval("planets@1", typeCheck: false));
    }

    [Fact]
    public void Eval_MapRef_VersionNotFound_ThrowsNameError()
    {
        var h = _yard.Push(FlatEncoder.Encode(new ScrapInt(1)));
        _map.Commit("x", h);

        var interp = new ScrapInterpreter(_yard, _map);
        Assert.Throws<ScrapNameError>(() => interp.Eval("x@99", typeCheck: false));
    }

    // ── Time-travel eval via ScrapInterpreter.Eval(asOf:) ────────────────────

    [Fact]
    public void Eval_AsOf_ReturnsVersionCurrentAtThatTime()
    {
        var h0 = _yard.Push(FlatEncoder.Encode(new ScrapText("Pluto")));
        _map.Commit("planets", h0);
        var tMid = DateTimeOffset.UtcNow;
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapText("Neptune")));
        _map.Commit("planets", h1);

        var interp = new ScrapInterpreter(_yard, _map);
        // Time-travel: asOf = tMid → only first commit exists
        var result = interp.Eval("planets", typeCheck: false, asOf: tMid);
        Assert.Equal(new ScrapText("Pluto"), result);
    }

    [Fact]
    public void Eval_AsOf_AfterAllCommits_ReturnsLatest()
    {
        var h0 = _yard.Push(FlatEncoder.Encode(new ScrapText("Pluto")));
        _map.Commit("planets", h0);
        var h1 = _yard.Push(FlatEncoder.Encode(new ScrapText("Neptune")));
        _map.Commit("planets", h1);
        var tEnd = DateTimeOffset.UtcNow.AddSeconds(10);

        var interp = new ScrapInterpreter(_yard, _map);
        var result = interp.Eval("planets", typeCheck: false, asOf: tEnd);
        Assert.Equal(new ScrapText("Neptune"), result);
    }
}
