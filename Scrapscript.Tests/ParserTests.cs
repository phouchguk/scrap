using Scrapscript.Core.Lexer;
using Scrapscript.Core.Parser;
using Xunit;

namespace Scrapscript.Tests;

public class ParserTests
{
    private static Expr Parse(string src)
    {
        var tokens = new Lexer(src).Tokenize();
        return new Parser(tokens).ParseProgram();
    }

    [Fact] public void ParseInt() => Assert.IsType<IntLit>(Parse("42"));
    [Fact] public void ParseNegativeInt() => Assert.Equal(new IntLit(-1), Parse("-1"));
    [Fact] public void ParseFloat() => Assert.IsType<FloatLit>(Parse("3.14"));
    [Fact] public void ParseText() => Assert.Equal(new TextLit("hello"), Parse("\"hello\""));
    [Fact] public void ParseHole() => Assert.IsType<HoleLit>(Parse("()"));
    [Fact] public void ParseVar() => Assert.Equal(new Var("x"), Parse("x"));

    [Fact]
    public void ParseAddition()
    {
        var expr = Parse("1 + 2");
        Assert.Equal(new BinOpExpr("+", new IntLit(1), new IntLit(2)), expr);
    }

    [Fact]
    public void ParseMultiplication()
    {
        var expr = Parse("3 * 5");
        Assert.Equal(new BinOpExpr("*", new IntLit(3), new IntLit(5)), expr);
    }

    [Fact]
    public void ParsePrecedence()
    {
        // 1 + 2 * 3  =>  1 + (2 * 3)
        var expr = Parse("1 + 2 * 3");
        var expected = new BinOpExpr("+", new IntLit(1), new BinOpExpr("*", new IntLit(2), new IntLit(3)));
        Assert.Equal(expected, expr);
    }

    [Fact]
    public void ParseWhere()
    {
        var expr = Parse("x ; x = 1");
        var where = Assert.IsType<WhereExpr>(expr);
        Assert.Equal(new Var("x"), where.Body);
        Assert.Single(where.Bindings);
        Assert.Equal(new VarPat("x"), where.Bindings[0].Pattern);
        Assert.Equal(new IntLit(1), where.Bindings[0].Value);
    }

    [Fact]
    public void ParseWhereMultiple()
    {
        var expr = Parse("a + b ; a = 1 ; b = 2");
        var where = Assert.IsType<WhereExpr>(expr);
        Assert.Equal(2, where.Bindings.Count);
    }

    [Fact]
    public void ParseLambda()
    {
        var expr = Parse("x -> x");
        var lambda = Assert.IsType<LambdaExpr>(expr);
        Assert.Equal(new VarPat("x"), lambda.Param);
        Assert.Equal(new Var("x"), lambda.Body);
    }

    [Fact]
    public void ParseApplication()
    {
        var expr = Parse("f 1");
        var app = Assert.IsType<ApplyExpr>(expr);
        Assert.Equal(new Var("f"), app.Fn);
        Assert.Equal(new IntLit(1), app.Arg);
    }

    [Fact]
    public void ParseCurried()
    {
        // f 1 2  =>  (f 1) 2
        var expr = Parse("f 1 2");
        var app = Assert.IsType<ApplyExpr>(expr);
        var inner = Assert.IsType<ApplyExpr>(app.Fn);
        Assert.Equal(new Var("f"), inner.Fn);
    }

    [Fact]
    public void ParseList()
    {
        var expr = Parse("[1, 2, 3]");
        var list = Assert.IsType<ListExpr>(expr);
        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public void ParseEmptyList()
    {
        var expr = Parse("[]");
        var list = Assert.IsType<ListExpr>(expr);
        Assert.Empty(list.Items);
    }

    [Fact]
    public void ParseRecord()
    {
        var expr = Parse("{ a = 1, b = \"x\" }");
        var rec = Assert.IsType<RecordExpr>(expr);
        Assert.Equal(2, rec.Fields.Count);
    }

    [Fact]
    public void ParseRecordSpread()
    {
        var expr = Parse("{ ..g, a = 2 }");
        var rec = Assert.IsType<RecordExpr>(expr);
        Assert.Equal("g", rec.Spread);
        Assert.Single(rec.Fields);
    }

    [Fact]
    public void ParseRecordAccess()
    {
        var expr = Parse("rec.a");
        var access = Assert.IsType<RecordAccess>(expr);
        Assert.Equal(new Var("rec"), access.Record);
        Assert.Equal("a", access.Field);
    }

    [Fact]
    public void ParsePipe()
    {
        // x |> f  =>  f x
        var expr = Parse("x |> f");
        var app = Assert.IsType<ApplyExpr>(expr);
        Assert.Equal(new Var("f"), app.Fn);
        Assert.Equal(new Var("x"), app.Arg);
    }

    [Fact]
    public void ParseReversePipe()
    {
        // f <| x  =>  f x
        var expr = Parse("f <| x");
        var app = Assert.IsType<ApplyExpr>(expr);
        Assert.Equal(new Var("f"), app.Fn);
        Assert.Equal(new Var("x"), app.Arg);
    }

    [Fact]
    public void ParseCaseFunction()
    {
        var expr = Parse("| 1 -> \"one\" | _ -> \"other\"");
        var caseExpr = Assert.IsType<CaseExpr>(expr);
        Assert.Equal(2, caseExpr.Arms.Count);
    }

    [Fact]
    public void ParseVariantConstructor()
    {
        var expr = Parse("#true");
        var ctor = Assert.IsType<ConstructorExpr>(expr);
        Assert.Equal("true", ctor.Variant);
    }

    [Fact]
    public void ParseTypeColonColon()
    {
        var expr = Parse("scoop::chocolate");
        var ctor = Assert.IsType<ConstructorExpr>(expr);
        Assert.Equal("chocolate", ctor.Variant);
    }

    [Fact]
    public void ParseHexBytes()
    {
        var expr = Parse("~FF");
        var bytes = Assert.IsType<BytesLit>(expr);
        Assert.Equal(new byte[] { 0xFF }, bytes.Value);
    }

    [Fact]
    public void ParseBase64Bytes()
    {
        var expr = Parse("~~aGVsbG8=");
        var bytes = Assert.IsType<BytesLit>(expr);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("hello"), bytes.Value);
    }

    // ── Do notation ───────────────────────────────────────────────────────────

    [Fact]
    public void Do_NoBindSteps_ReturnsBodyDirectly()
    {
        // do expr  →  expr  (no bind calls)
        var expr = Parse("do 42");
        Assert.Equal(new IntLit(42), expr);
    }

    [Fact]
    public void Do_SingleBind_DesugarsToBindCall()
    {
        // do x <- foo, bar  →  bind foo (x -> bar)
        var expr = Parse("do x <- foo, bar");
        var outer = Assert.IsType<ApplyExpr>(expr);
        var inner = Assert.IsType<ApplyExpr>(outer.Fn);
        Assert.Equal(new Var("bind"), inner.Fn);
        Assert.Equal(new Var("foo"), inner.Arg);
        var lam = Assert.IsType<LambdaExpr>(outer.Arg);
        Assert.Equal(new VarPat("x"), lam.Param);
        Assert.Equal(new Var("bar"), lam.Body);
    }

    [Fact]
    public void Do_TwoBinds_DesugarsToNestedBindCalls()
    {
        // do x <- a, y <- b, x  →  bind a (x -> bind b (y -> x))
        var expr = Parse("do x <- a, y <- b, x");
        var outer = Assert.IsType<ApplyExpr>(expr);
        var outerFn = Assert.IsType<ApplyExpr>(outer.Fn);
        Assert.Equal(new Var("bind"), outerFn.Fn);
        Assert.Equal(new Var("a"), outerFn.Arg);
        var outerLam = Assert.IsType<LambdaExpr>(outer.Arg);
        Assert.Equal(new VarPat("x"), outerLam.Param);
        // inner: bind b (y -> x)
        var inner = Assert.IsType<ApplyExpr>(outerLam.Body);
        var innerFn = Assert.IsType<ApplyExpr>(inner.Fn);
        Assert.Equal(new Var("bind"), innerFn.Fn);
        Assert.Equal(new Var("b"), innerFn.Arg);
        var innerLam = Assert.IsType<LambdaExpr>(inner.Arg);
        Assert.Equal(new VarPat("y"), innerLam.Param);
        Assert.Equal(new Var("x"), innerLam.Body);
    }

    [Fact]
    public void Do_WildcardBind_ParsesCorrectly()
    {
        // do _ <- effect, result  →  bind effect (_ -> result)
        var expr = Parse("do _ <- effect, result");
        var outer = Assert.IsType<ApplyExpr>(expr);
        var inner = Assert.IsType<ApplyExpr>(outer.Fn);
        Assert.Equal(new Var("bind"), inner.Fn);
        var lam = Assert.IsType<LambdaExpr>(outer.Arg);
        Assert.IsType<WildcardPat>(lam.Param);
    }
}
