using System.Collections.Immutable;
using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Xunit;

namespace Scrapscript.Tests;

public class EvalTests
{
    private static ScrapValue Eval(string src) => new ScrapInterpreter().Eval(src);
    private static ScrapInt Int(long v) => new ScrapInt(v);
    private static ScrapFloat Float(double v) => new ScrapFloat(v);
    private static ScrapText Text(string v) => new ScrapText(v);

    // ── Literals ──────────────────────────────────────────────────────────────

    [Fact] public void EvalInt() => Assert.Equal(Int(42), Eval("42"));
    [Fact] public void EvalNegativeInt() => Assert.Equal(Int(-1), Eval("-1"));
    [Fact] public void EvalFloat() => Assert.Equal(Float(3.14), Eval("3.14"));
    [Fact] public void EvalText() => Assert.Equal(Text("hello"), Eval("\"hello\""));
    [Fact] public void EvalHole() => Assert.IsType<ScrapHole>(Eval("()"));

    // ── Arithmetic ────────────────────────────────────────────────────────────

    [Fact] public void AddInts() => Assert.Equal(Int(2), Eval("1 + 1"));
    [Fact] public void SubInts() => Assert.Equal(Int(1), Eval("3 - 2"));
    [Fact] public void MulInts() => Assert.Equal(Int(15), Eval("3 * 5"));
    [Fact] public void AddFloats() => Assert.Equal(Float(2.0), Eval("1.0 + 1.0"));
    [Fact] public void MulPrecedence() => Assert.Equal(Int(7), Eval("1 + 2 * 3"));

    [Fact]
    public void TypeMixError()
    {
        Assert.Throws<ScrapTypeError>(() => Eval("1 + 1.0"));
    }

    // ── Where-clauses ─────────────────────────────────────────────────────────

    [Fact] public void WhereSimple() => Assert.Equal(Int(1), Eval("x ; x = 1"));
    [Fact] public void WhereMultiple() => Assert.Equal(Int(6), Eval("a + b + c ; a = 1 ; b = 2 ; c = 3"));
    [Fact] public void WhereNested() => Assert.Equal(Int(350), Eval("200 + (x ; x = 150)"));
    [Fact] public void WhereMutualRef() => Assert.Equal(Int(3), Eval("f 1 ; f = a -> a + x ; x = 2"));

    // ── Functions ─────────────────────────────────────────────────────────────

    [Fact] public void LambdaIdentity() => Assert.Equal(Int(5), Eval("(x -> x) 5"));
    [Fact] public void LambdaCurried() => Assert.Equal(Int(3), Eval("f 1 2 ; f = a -> b -> a + b"));
    [Fact] public void PartialApplication() { var res = Eval("add1 5 ; add1 = add 1 ; add = a -> b -> a + b"); Assert.Equal(Int(6), res); }

    // ── Case functions (pattern matching) ────────────────────────────────────

    [Fact]
    public void CaseOnInt()
    {
        var res = Eval("f 0 ; f = | 0 -> \"zero\" | 1 -> \"one\" | _ -> \"other\"");
        Assert.Equal(Text("zero"), res);
    }

    [Fact]
    public void CaseWildcard()
    {
        var res = Eval("f 99 ; f = | 0 -> \"zero\" | _ -> \"other\"");
        Assert.Equal(Text("other"), res);
    }

    [Fact]
    public void CaseTextPrefix()
    {
        var res = Eval("f \"hello world\" ; f = | \"hello \" ++ rest -> rest | _ -> \"\"");
        Assert.Equal(Text("world"), res);
    }

    [Fact]
    public void CaseListCons()
    {
        var res = Eval("f [1, 2, 3] ; f = | h >+ t -> h | [] -> -1");
        Assert.Equal(Int(1), res);
    }

    [Fact]
    public void CaseListSlice()
    {
        var res = Eval("f [1, 2, 3, 4] ; f = | [x, y] ++ rest -> x + y | _ -> 0");
        Assert.Equal(Int(3), res);
    }

    [Fact]
    public void CaseRecord()
    {
        var res = Eval("f { a = 1, b = 2 } ; f = | { a = a, b = b } -> a + b");
        Assert.Equal(Int(3), res);
    }

    // ── Operators ─────────────────────────────────────────────────────────────

    [Fact] public void TextConcat() => Assert.Equal(Text("hello world"), Eval("\"hello\" ++ \" \" ++ \"world\""));
    [Fact] public void ListAppend() => Assert.Equal(new ScrapList(new ScrapValue[] { Int(1), Int(2), Int(3), Int(4) }.ToImmutableList()), Eval("[1, 2, 3] +< 4"));
    [Fact] public void ListCons() => Assert.Equal(new ScrapList(new ScrapValue[] { Int(0), Int(1), Int(2) }.ToImmutableList()), Eval("0 >+ [1, 2]"));

    [Fact]
    public void PipeOperator()
    {
        var res = Eval("1 |> (x -> x + 1)");
        Assert.Equal(Int(2), res);
    }

    [Fact]
    public void ReversePipe()
    {
        var res = Eval("(x -> x + 1) <| 1");
        Assert.Equal(Int(2), res);
    }

    [Fact]
    public void Compose()
    {
        var res = Eval("(f >> g) 7 ; f = x -> x + 1 ; g = x -> x * 2");
        Assert.Equal(Int(16), res);
    }

    // ── Records ───────────────────────────────────────────────────────────────

    [Fact]
    public void RecordAccess()
    {
        var res = Eval("rec.a ; rec = { a = 1, b = \"x\" }");
        Assert.Equal(Int(1), res);
    }

    [Fact]
    public void RecordSpread()
    {
        var res = Eval("{ ..g, a = 2 }.a ; g = { a = 1, b = \"x\" }");
        Assert.Equal(Int(2), res);
    }

    [Fact]
    public void RecordSpreadPreservesOtherFields()
    {
        var res = Eval("{ ..g, a = 2 }.b ; g = { a = 1, b = \"x\" }");
        Assert.Equal(Text("x"), res);
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyList()
    {
        var res = Eval("[]");
        var list = Assert.IsType<ScrapList>(res);
        Assert.Empty(list.Items);
    }

    [Fact]
    public void ListWithItems()
    {
        var res = Eval("[1, 2, 3]");
        var list = Assert.IsType<ScrapList>(res);
        Assert.Equal(3, list.Items.Count);
    }

    // ── Variants ──────────────────────────────────────────────────────────────

    [Fact]
    public void TagVariant()
    {
        var res = Eval("#true");
        var variant = Assert.IsType<ScrapVariant>(res);
        Assert.Equal("true", variant.Tag);
        Assert.Null(variant.Payload);
    }

    [Fact]
    public void MatchVariant()
    {
        var res = Eval("f #true ; f = | #true -> 1 | #false -> 0");
        Assert.Equal(Int(1), res);
    }

    [Fact]
    public void TypeDefVariantConstructor()
    {
        var res = Eval("scoop::chocolate ; scoop : #vanilla #chocolate #strawberry");
        var variant = Assert.IsType<ScrapVariant>(res);
        Assert.Equal("chocolate", variant.Tag);
        Assert.Null(variant.Payload);
    }

    [Fact]
    public void TypeDefVariantWithPayload()
    {
        var res = Eval("c::radius 4 ; c : #radius int");
        var variant = Assert.IsType<ScrapVariant>(res);
        Assert.Equal("radius", variant.Tag);
        Assert.Equal(Int(4), variant.Payload);
    }

    [Fact]
    public void TypeDefVariantMultiPayload()
    {
        var res = Eval("point::two-d 3 4 ; point : #two-d int int #three-d int int int");
        var variant = Assert.IsType<ScrapVariant>(res);
        Assert.Equal("two-d", variant.Tag);
        var list = Assert.IsType<ScrapList>(variant.Payload);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(Int(3), list.Items[0]);
        Assert.Equal(Int(4), list.Items[1]);
    }

    [Fact]
    public void TypeDefVariantDigitLeadingTag()
    {
        var res = Eval("point::2d 3 4 ; point : #2d int int #3d int int int");
        var variant = Assert.IsType<ScrapVariant>(res);
        Assert.Equal("2d", variant.Tag);
        var list = Assert.IsType<ScrapList>(variant.Payload);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(Int(3), list.Items[0]);
        Assert.Equal(Int(4), list.Items[1]);
    }

    // ── Bytes ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BytesLiteral()
    {
        var res = Eval("~FF");
        var bytes = Assert.IsType<ScrapBytes>(res);
        Assert.Equal(new byte[] { 0xFF }, bytes.Value);
    }

    [Fact]
    public void BytesConcat()
    {
        var res = Eval("~00 ++ ~FF");
        var bytes = Assert.IsType<ScrapBytes>(res);
        Assert.Equal(new byte[] { 0x00, 0xFF }, bytes.Value);
    }

    // ── Built-ins ─────────────────────────────────────────────────────────────

    [Fact] public void ToFloat() => Assert.Equal(Float(3.0), Eval("to-float 3"));
    [Fact] public void Round() => Assert.Equal(Int(4), Eval("round 3.5"));
    [Fact] public void Ceil() => Assert.Equal(Int(4), Eval("ceil 3.1"));
    [Fact] public void Floor() => Assert.Equal(Int(3), Eval("floor 3.9"));
    [Fact] public void BytesToUtf8() => Assert.Equal(Text("hello world"), Eval("bytes/to-utf8-text ~~aGVsbG8gd29ybGQ="));
    [Fact] public void ListFirst() => Assert.Equal(new ScrapVariant("just", Int(1)), Eval("list/first [1, 2, 3]"));
    [Fact] public void ListFirstEmpty() => Assert.Equal(new ScrapVariant("nothing", null), Eval("list/first []"));
    [Fact] public void ListLength() => Assert.Equal(Int(3), Eval("list/length [1, 2, 3]"));
    [Fact] public void TextLength() => Assert.Equal(Int(5), Eval("text/length \"hello\""));
    [Fact] public void StringJoin() => Assert.Equal(Text("a, b, c"), Eval("string/join \", \" [\"a\", \"b\", \"c\"]"));
    [Fact] public void MaybeDefault() => Assert.Equal(Int(5), Eval("maybe/default 0 (list/first [5])"));
    [Fact] public void MaybeDefaultEmpty() => Assert.Equal(Int(0), Eval("maybe/default 0 (list/first [])"));

    // ── Person/greet example from spec ───────────────────────────────────────

    [Fact]
    public void PersonGreetRon()
    {
        var src =
            "greet <| person::ron 3 " +
            "; greet : person -> text = " +
            "  | #cowboy -> \"howdy\" " +
            "  | #ron n -> \"hi \" ++ text/repeat n \"a\" ++ \"ron\" " +
            "  | #parent #m -> \"hey mom\" " +
            "  | #parent #f -> \"greetings father\" " +
            "  | #friend n -> \"yo\" |> list/repeat n |> string/join \" \" " +
            "  | #stranger \"felicia\" -> \"bye\" " +
            "  | #stranger name -> \"hello \" ++ name " +
            "; person : #cowboy #ron int #parent (#m #f) #friend int #stranger text";
        Assert.Equal(Text("hi aaaron"), Eval(src));
    }

    // ── Full worked examples from spec ────────────────────────────────────────

    [Fact] public void HelloWorld() => Assert.Equal(Text("hello world"), Eval("\"hello world\""));
    [Fact] public void Arithmetic1() => Assert.Equal(Int(2), Eval("1 + 1"));
    [Fact] public void Arithmetic2() => Assert.Equal(Int(15), Eval("3 * 5"));
    [Fact] public void TextConcatSpec() => Assert.Equal(Text("hello world"), Eval("\"hello\" ++ \" \" ++ \"world\""));

    [Fact]
    public void WhereClauseSpec()
    {
        var res = Eval("a + b + c ; a = 1 ; b = 2 ; c = 3");
        Assert.Equal(Int(6), res);
    }

    [Fact]
    public void FunctionSpec()
    {
        var res = Eval("f 1 2 ; f = a -> b -> a + b");
        Assert.Equal(Int(3), res);
    }

    [Fact]
    public void PatternMatchingFunctionSpec()
    {
        var res = Eval("f \"b\" ; f = | \"a\" -> 1 | \"b\" -> 2 | \"c\" -> 3 | x -> 0");
        Assert.Equal(Int(2), res);
    }

    [Fact]
    public void FunctionCompositionSpec()
    {
        var res = Eval(
            "(f >> (x -> x) >> g) 7 " +
            "; f = | 7 -> \"cat\" | 4 -> \"dog\" | _ -> \"shark\" " +
            "; g = | \"cat\" -> \"kitten\" | \"dog\" -> \"puppy\" | a -> \"baby \" ++ a");
        Assert.Equal(Text("kitten"), res);
    }

    [Fact]
    public void BytesToUtf8WithAppend()
    {
        // bytes/to-utf8-text <| ~~aGVsbG8gd29ybGQ= +< ~21  -- "hello world!"
        var res = Eval("bytes/to-utf8-text <| ~~aGVsbG8gd29ybGQ= +< ~21");
        Assert.Equal(Text("hello world!"), res);
    }
}
