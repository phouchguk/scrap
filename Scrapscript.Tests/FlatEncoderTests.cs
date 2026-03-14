using System.Collections.Immutable;
using Scrapscript.Core.Eval;
using Scrapscript.Core.Serialization;
using Xunit;

namespace Scrapscript.Tests;

public class FlatEncoderTests
{
    private static byte[] Encode(ScrapValue v) => FlatEncoder.Encode(v);
    private static ScrapValue Decode(byte[] b) => FlatDecoder.Decode(b);
    private static void RoundTrip(ScrapValue v)
    {
        var enc = Encode(v);
        Assert.Equal(enc, Encode(Decode(enc)));
        Assert.Equal(v, Decode(enc));
    }

    // ── Specific byte checks ─────────────────────────────────────────────────

    [Fact] public void Int15() => Assert.Equal(new byte[] { 0x0F }, Encode(new ScrapInt(15)));
    [Fact] public void Int0() => Assert.Equal(new byte[] { 0x00 }, Encode(new ScrapInt(0)));
    [Fact] public void Int127() => Assert.Equal(new byte[] { 0x7F }, Encode(new ScrapInt(127)));
    [Fact] public void IntNeg1() => Assert.Equal(new byte[] { 0xFF }, Encode(new ScrapInt(-1)));
    [Fact] public void IntNeg32() => Assert.Equal(new byte[] { 0xE0 }, Encode(new ScrapInt(-32)));
    [Fact] public void Hole() => Assert.Equal(new byte[] { 0xC0 }, Encode(new ScrapHole()));
    [Fact] public void TextHi() => Assert.Equal(new byte[] { 0xA2, 0x68, 0x69 }, Encode(new ScrapText("hi")));
    [Fact] public void TextEmpty() => Assert.Equal(new byte[] { 0xA0 }, Encode(new ScrapText("")));

    // ── Round-trip tests ─────────────────────────────────────────────────────

    [Fact] public void RoundTripInt0() => RoundTrip(new ScrapInt(0));
    [Fact] public void RoundTripIntPos() => RoundTrip(new ScrapInt(42));
    [Fact] public void RoundTripIntMax() => RoundTrip(new ScrapInt(127));
    [Fact] public void RoundTripIntNeg() => RoundTrip(new ScrapInt(-1));
    [Fact] public void RoundTripIntNeg32() => RoundTrip(new ScrapInt(-32));
    [Fact] public void RoundTripIntLarge() => RoundTrip(new ScrapInt(1_000_000_000L));
    [Fact] public void RoundTripIntNegLarge() => RoundTrip(new ScrapInt(-1_000_000_000L));
    [Fact] public void RoundTripFloat() => RoundTrip(new ScrapFloat(3.14));
    [Fact] public void RoundTripFloatZero() => RoundTrip(new ScrapFloat(0.0));
    [Fact] public void RoundTripHole() => RoundTrip(new ScrapHole());
    [Fact] public void RoundTripText() => RoundTrip(new ScrapText("hello world"));
    [Fact] public void RoundTripTextEmpty() => RoundTrip(new ScrapText(""));
    [Fact] public void RoundTripBytes() => RoundTrip(new ScrapBytes(new byte[] { 1, 2, 3 }));
    [Fact] public void RoundTripBytesEmpty() => RoundTrip(new ScrapBytes(Array.Empty<byte>()));

    [Fact]
    public void RoundTripList()
    {
        var list = new ScrapList(ImmutableList.Create<ScrapValue>(
            new ScrapInt(1), new ScrapInt(2), new ScrapInt(3)));
        RoundTrip(list);
    }

    [Fact]
    public void RoundTripListEmpty()
    {
        RoundTrip(new ScrapList(ImmutableList<ScrapValue>.Empty));
    }

    [Fact]
    public void RoundTripRecord()
    {
        var rec = new ScrapRecord(ImmutableDictionary<string, ScrapValue>.Empty
            .Add("x", new ScrapInt(1))
            .Add("y", new ScrapInt(2)));
        // ScrapRecord doesn't override Equals for ImmutableDictionary, so only check byte round-trip
        var enc = Encode(rec);
        Assert.Equal(enc, Encode(Decode(enc)));
    }

    [Fact]
    public void RoundTripVariantNoPayload()
    {
        RoundTrip(new ScrapVariant("true", null));
    }

    [Fact]
    public void RoundTripVariantWithPayload()
    {
        RoundTrip(new ScrapVariant("just", new ScrapInt(42)));
    }

    [Fact]
    public void RoundTripNestedList()
    {
        var inner = new ScrapList(ImmutableList.Create<ScrapValue>(new ScrapInt(1), new ScrapInt(2)));
        var outer = new ScrapList(ImmutableList.Create<ScrapValue>(inner, new ScrapText("x")));
        RoundTrip(outer);
    }

    [Fact]
    public void RecordFieldsAreSorted()
    {
        // Encode record with fields in different orders — bytes should be identical
        var r1 = new ScrapRecord(ImmutableDictionary<string, ScrapValue>.Empty
            .Add("a", new ScrapInt(1)).Add("b", new ScrapInt(2)));
        var r2 = new ScrapRecord(ImmutableDictionary<string, ScrapValue>.Empty
            .Add("b", new ScrapInt(2)).Add("a", new ScrapInt(1)));
        Assert.Equal(Encode(r1), Encode(r2));
    }

    [Fact]
    public void EncodeBuiltinThrows()
    {
        var b = new ScrapBuiltin("test", _ => new ScrapHole());
        Assert.Throws<InvalidOperationException>(() => Encode(b));
    }
}
