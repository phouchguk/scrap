using System.Collections.Immutable;
using Scrapscript.Core;
using Scrapscript.Core.Eval;
using Xunit;

namespace Scrapscript.Tests;

public class SelfHostingTests
{
    private static readonly string SelfHostingDir =
        Path.Combine("..", "..", "..", "..", "self-hosting");

    private static ScrapValue RunFile(string file, string testExpr, bool typeCheck = false)
    {
        var src = File.ReadAllText(Path.Combine(SelfHostingDir, file));
        return new ScrapInterpreter().Eval($"{testExpr} ; mod = ({src})", typeCheck);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScrapVariant Variant(string tag, ScrapValue? payload = null)
        => new ScrapVariant(tag, payload);

    private static ScrapList List(params ScrapValue[] items)
        => new ScrapList(items.ToImmutableList());

    private static ScrapInt Int(long v) => new ScrapInt(v);
    private static ScrapText Text(string v) => new ScrapText(v);
    private static ScrapFloat Float(double v) => new ScrapFloat(v);

    // ── Stage 1: Lexer ────────────────────────────────────────────────────────

    [Fact]
    public void LexInt()
    {
        var result = RunFile("lexer.ss", "mod.lex \"42\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        var tok = Assert.IsType<ScrapVariant>(list.Items[0]);
        Assert.Equal("int", tok.Tag);
        Assert.Equal(Int(42), tok.Payload);
    }

    [Fact]
    public void LexNegInt()
    {
        var result = RunFile("lexer.ss", "mod.lex \"-1\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(Variant("minus"), list.Items[0]);
        var tok = Assert.IsType<ScrapVariant>(list.Items[1]);
        Assert.Equal("int", tok.Tag);
        Assert.Equal(Int(1), tok.Payload);
    }

    [Fact]
    public void LexFloat()
    {
        var result = RunFile("lexer.ss", "mod.lex \"3.14\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        var tok = Assert.IsType<ScrapVariant>(list.Items[0]);
        Assert.Equal("float", tok.Tag);
        Assert.Equal(Float(3.14), tok.Payload);
    }

    [Fact]
    public void LexText()
    {
        var result = RunFile("lexer.ss", "mod.lex \"\\\"hi\\\"\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        var tok = Assert.IsType<ScrapVariant>(list.Items[0]);
        Assert.Equal("text", tok.Tag);
        Assert.Equal(Text("hi"), tok.Payload);
    }

    [Fact]
    public void LexIdent()
    {
        var result = RunFile("lexer.ss", "mod.lex \"foo\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        var tok = Assert.IsType<ScrapVariant>(list.Items[0]);
        Assert.Equal("ident", tok.Tag);
        Assert.Equal(Text("foo"), tok.Payload);
    }

    [Fact]
    public void LexTag()
    {
        var result = RunFile("lexer.ss", "mod.lex \"#nothing\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        var tok = Assert.IsType<ScrapVariant>(list.Items[0]);
        Assert.Equal("tag", tok.Tag);
        Assert.Equal(Text("nothing"), tok.Payload);
    }

    [Fact]
    public void LexOperators()
    {
        var result = RunFile("lexer.ss", "mod.lex \"1 + 2\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(Variant("int", Int(1)), list.Items[0]);
        Assert.Equal(Variant("plus"),        list.Items[1]);
        Assert.Equal(Variant("int", Int(2)), list.Items[2]);
    }

    [Fact]
    public void LexArrow()
    {
        var result = RunFile("lexer.ss", "mod.lex \"->\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        Assert.Equal(Variant("arrow"), list.Items[0]);
    }

    [Fact]
    public void LexPlusPlus()
    {
        var result = RunFile("lexer.ss", "mod.lex \"++\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        Assert.Equal(Variant("plus-plus"), list.Items[0]);
    }

    [Fact]
    public void LexGtPlus()
    {
        var result = RunFile("lexer.ss", "mod.lex \">+\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        Assert.Equal(Variant("gt-plus"), list.Items[0]);
    }

    [Fact]
    public void LexEqEq()
    {
        var result = RunFile("lexer.ss", "mod.lex \"==\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(1, list.Items.Count);
        Assert.Equal(Variant("eq-eq"), list.Items[0]);
    }

    [Fact]
    public void LexComment()
    {
        var result = RunFile("lexer.ss", "mod.lex \"1 -- x\\n2\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(Variant("int", Int(1)), list.Items[0]);
        Assert.Equal(Variant("int", Int(2)), list.Items[1]);
    }

    [Fact]
    public void LexSemiWhere()
    {
        var result = RunFile("lexer.ss", "mod.lex \"x ; x = 1\"");
        var list = Assert.IsType<ScrapList>(result);
        Assert.Equal(5, list.Items.Count);
        Assert.Equal(Variant("ident", Text("x")), list.Items[0]);
        Assert.Equal(Variant("semi"),              list.Items[1]);
        Assert.Equal(Variant("ident", Text("x")), list.Items[2]);
        Assert.Equal(Variant("eq"),               list.Items[3]);
        Assert.Equal(Variant("int", Int(1)),      list.Items[4]);
    }

    // ── Stage 2: Parser ───────────────────────────────────────────────────────

    [Fact]
    public void ParseInt()
    {
        // parse [#int 42] → #lit-int 42
        var result = RunFile("parser.ss", "mod.parse [#int 42]");
        Assert.Equal(Variant("lit-int", Int(42)), result);
    }

    [Fact]
    public void ParseVar()
    {
        // parse [#ident "x"] → #var "x"
        var result = RunFile("parser.ss", "mod.parse [#ident \"x\"]");
        Assert.Equal(Variant("var", Text("x")), result);
    }

    [Fact]
    public void ParseApp()
    {
        // parse [#ident "f", #int 1] → #app { fn = #var "f", arg = #lit-int 1 }
        var result = RunFile("parser.ss", "mod.parse [#ident \"f\", #int 1]");
        var v = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("app", v.Tag);
        var rec = Assert.IsType<ScrapRecord>(v.Payload);
        Assert.Equal(Variant("var", Text("f")),  rec.Fields["fn"]);
        Assert.Equal(Variant("lit-int", Int(1)), rec.Fields["arg"]);
    }

    [Fact]
    public void ParseAdd()
    {
        // parse [#int 1, #plus, #int 2] → #binop { op="+", left=#lit-int 1, right=#lit-int 2 }
        var result = RunFile("parser.ss",
            "mod.parse [#int 1, #plus, #int 2]");
        var v = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("binop", v.Tag);
        var rec = Assert.IsType<ScrapRecord>(v.Payload);
        Assert.Equal(Text("+"),              rec.Fields["op"]);
        Assert.Equal(Variant("lit-int", Int(1)), rec.Fields["left"]);
        Assert.Equal(Variant("lit-int", Int(2)), rec.Fields["right"]);
    }

    [Fact]
    public void ParseLambda()
    {
        // parse [#ident "x", #arrow, #ident "x"] → #lam { param=#var "x", body=#var "x" }
        var result = RunFile("parser.ss",
            "mod.parse [#ident \"x\", #arrow, #ident \"x\"]");
        var v = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("lam", v.Tag);
        var rec = Assert.IsType<ScrapRecord>(v.Payload);
        Assert.Equal(Variant("var", Text("x")), rec.Fields["param"]);
        Assert.Equal(Variant("var", Text("x")), rec.Fields["body"]);
    }

    [Fact]
    public void ParseWhere()
    {
        // parse [#ident "x", #semi, #ident "x", #eq, #int 1]
        // → #where { body=#var "x", binds=[{name="x", val=#lit-int 1}] }
        var result = RunFile("parser.ss",
            "mod.parse [#ident \"x\", #semi, #ident \"x\", #eq, #int 1]");
        var v = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("where", v.Tag);
        var rec = Assert.IsType<ScrapRecord>(v.Payload);
        Assert.Equal(Variant("var", Text("x")), rec.Fields["body"]);
        var binds = Assert.IsType<ScrapList>(rec.Fields["binds"]);
        Assert.Equal(1, binds.Items.Count);
        var bind = Assert.IsType<ScrapRecord>(binds.Items[0]);
        Assert.Equal(Text("x"),                 bind.Fields["name"]);
        Assert.Equal(Variant("lit-int", Int(1)), bind.Fields["val"]);
    }

    [Fact]
    public void ParseList()
    {
        // parse [#lbracket, #int 1, #comma, #int 2, #rbracket]
        // → #list [#lit-int 1, #lit-int 2]
        var result = RunFile("parser.ss",
            "mod.parse [#lbracket, #int 1, #comma, #int 2, #rbracket]");
        var v = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("list", v.Tag);
        var items = Assert.IsType<ScrapList>(v.Payload);
        Assert.Equal(2, items.Items.Count);
        Assert.Equal(Variant("lit-int", Int(1)), items.Items[0]);
        Assert.Equal(Variant("lit-int", Int(2)), items.Items[1]);
    }

    // ── Stage 3: Evaluator ────────────────────────────────────────────────────

    [Fact]
    public void EvalInt()
    {
        var result = RunFile("eval.ss", "mod.eval (#lit-int 42) []");
        Assert.Equal(Variant("int", Int(42)), result);
    }

    [Fact]
    public void EvalFloat()
    {
        var result = RunFile("eval.ss", "mod.eval (#lit-float 3.14) []");
        Assert.Equal(Variant("float", Float(3.14)), result);
    }

    [Fact]
    public void EvalText()
    {
        var result = RunFile("eval.ss", "mod.eval (#lit-text \"hello\") []");
        Assert.Equal(Variant("text", Text("hello")), result);
    }

    [Fact]
    public void EvalVar()
    {
        // env has x=5; eval #var "x" → #int 5
        var result = RunFile("eval.ss",
            "mod.eval (#var \"x\") [{ name = \"x\", val = #int 5 }]");
        Assert.Equal(Variant("int", Int(5)), result);
    }

    [Fact]
    public void EvalAdd()
    {
        var result = RunFile("eval.ss",
            "mod.eval (#binop { op = \"+\", left = #lit-int 1, right = #lit-int 2 }) []");
        Assert.Equal(Variant("int", Int(3)), result);
    }

    [Fact]
    public void EvalSub()
    {
        var result = RunFile("eval.ss",
            "mod.eval (#binop { op = \"-\", left = #lit-int 10, right = #lit-int 3 }) []");
        Assert.Equal(Variant("int", Int(7)), result);
    }

    [Fact]
    public void EvalMul()
    {
        var result = RunFile("eval.ss",
            "mod.eval (#binop { op = \"*\", left = #lit-int 3, right = #lit-int 4 }) []");
        Assert.Equal(Variant("int", Int(12)), result);
    }

    [Fact]
    public void EvalLambdaIdentity()
    {
        // (x -> x) applied to #int 5
        var result = RunFile("eval.ss",
            "mod.apply (mod.eval (#lam { param = #var \"x\", body = #var \"x\" }) []) (#int 5)");
        Assert.Equal(Variant("int", Int(5)), result);
    }

    [Fact]
    public void EvalWhere()
    {
        // x ; x = 1  →  #int 1
        var result = RunFile("eval.ss",
            "mod.eval (#where { body = #var \"x\", binds = [{ name = \"x\", val = #lit-int 1 }] }) []");
        Assert.Equal(Variant("int", Int(1)), result);
    }

    [Fact]
    public void EvalWhereMultiple()
    {
        // a + b ; a = 1 ; b = 2  →  #int 3
        var result = RunFile("eval.ss", """
            mod.eval (#where {
              body = #binop { op = "+", left = #var "a", right = #var "b" },
              binds = [{ name = "a", val = #lit-int 1 }, { name = "b", val = #lit-int 2 }]
            }) []
            """);
        Assert.Equal(Variant("int", Int(3)), result);
    }

    [Fact]
    public void EvalCase()
    {
        // (| #lit-int 0 -> #tag-lit "yes" | #var "_" -> #tag-lit "no") (#int 0) → #tag "yes"
        var result = RunFile("eval.ss", """
            mod.apply
              (mod.eval (#case [
                { pat = #lit-int 0, body = #tag-lit "yes" },
                { pat = #var "_",   body = #tag-lit "no"  }
              ]) [])
              (#int 0)
            """);
        Assert.Equal(Variant("tag", Text("yes")), result);
    }

    [Fact]
    public void EvalCaseWildcard()
    {
        var result = RunFile("eval.ss", """
            mod.apply
              (mod.eval (#case [
                { pat = #lit-int 0, body = #tag-lit "yes" },
                { pat = #var "_",   body = #tag-lit "no"  }
              ]) [])
              (#int 99)
            """);
        Assert.Equal(Variant("tag", Text("no")), result);
    }

    [Fact]
    public void EvalCurriedAdd()
    {
        // (a -> b -> a + b) 2 3 → #int 5
        var result = RunFile("eval.ss", """
            mod.apply
              (mod.apply
                (mod.eval (#lam {
                  param = #var "a",
                  body = #lam {
                    param = #var "b",
                    body = #binop { op = "+", left = #var "a", right = #var "b" }
                  }
                }) [])
                (#int 2))
              (#int 3)
            """);
        Assert.Equal(Variant("int", Int(5)), result);
    }

    [Fact]
    public void EvalList()
    {
        // #list [#lit-int 1, #lit-int 2] → #cons {head=#int 1, tail=#cons {head=#int 2, tail=#nil}}
        var result = RunFile("eval.ss",
            "mod.eval (#list [#lit-int 1, #lit-int 2]) []");
        var outer = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("cons", outer.Tag);
        var outerRec = Assert.IsType<ScrapRecord>(outer.Payload);
        Assert.Equal(Variant("int", Int(1)), outerRec.Fields["head"]);
        var inner = Assert.IsType<ScrapVariant>(outerRec.Fields["tail"]);
        Assert.Equal("cons", inner.Tag);
        var innerRec = Assert.IsType<ScrapRecord>(inner.Payload);
        Assert.Equal(Variant("int", Int(2)), innerRec.Fields["head"]);
        Assert.Equal(Variant("nil"),         innerRec.Fields["tail"]);
    }

    [Fact]
    public void EvalRecord()
    {
        // { x = 1 }  →  #record [{name="x", val=#int 1}]
        var result = RunFile("eval.ss",
            "mod.eval (#record [{ name = \"x\", val = #lit-int 1 }]) []");
        var v = Assert.IsType<ScrapVariant>(result);
        Assert.Equal("record", v.Tag);
        var fields = Assert.IsType<ScrapList>(v.Payload);
        Assert.Equal(1, fields.Items.Count);
        var field = Assert.IsType<ScrapRecord>(fields.Items[0]);
        Assert.Equal(Text("x"),              field.Fields["name"]);
        Assert.Equal(Variant("int", Int(1)), field.Fields["val"]);
    }

    [Fact]
    public void EvalEqTrue()
    {
        var result = RunFile("eval.ss",
            "mod.eval (#binop { op = \"==\", left = #lit-int 1, right = #lit-int 1 }) []");
        Assert.Equal(Variant("tag", Text("true")), result);
    }

    [Fact]
    public void EvalEqFalse()
    {
        var result = RunFile("eval.ss",
            "mod.eval (#binop { op = \"==\", left = #lit-int 1, right = #lit-int 2 }) []");
        Assert.Equal(Variant("tag", Text("false")), result);
    }

    // ── Full pipeline (lex → parse → eval) ────────────────────────────────────

    [Fact]
    public void FullPipelineAdd()
    {
        // "1 + 2" → #int 3
        var lexSrc   = File.ReadAllText(Path.Combine(SelfHostingDir, "lexer.ss"));
        var parseSrc = File.ReadAllText(Path.Combine(SelfHostingDir, "parser.ss"));
        var evalSrc  = File.ReadAllText(Path.Combine(SelfHostingDir, "eval.ss"));
        var program = """
            (e-mod.eval (p-mod.parse (l-mod.lex "1 + 2")) [])
            ; l-mod = (LEXER)
            ; p-mod = (PARSER)
            ; e-mod = (EVAL)
            """
            .Replace("LEXER",  lexSrc)
            .Replace("PARSER", parseSrc)
            .Replace("EVAL",   evalSrc);
        var result = new ScrapInterpreter().Eval(program, typeCheck: false);
        Assert.Equal(Variant("int", Int(3)), result);
    }

    [Fact]
    public void FullPipelineLambda()
    {
        // "(x -> x) 5" → #int 5  (via full lex→parse→eval pipeline)
        var lexSrc   = File.ReadAllText(Path.Combine(SelfHostingDir, "lexer.ss"));
        var parseSrc = File.ReadAllText(Path.Combine(SelfHostingDir, "parser.ss"));
        var evalSrc  = File.ReadAllText(Path.Combine(SelfHostingDir, "eval.ss"));
        var program = """
            (e-mod.eval (p-mod.parse (l-mod.lex "(x -> x) 5")) [])
            ; l-mod = (LEXER)
            ; p-mod = (PARSER)
            ; e-mod = (EVAL)
            """
            .Replace("LEXER",  lexSrc)
            .Replace("PARSER", parseSrc)
            .Replace("EVAL",   evalSrc);
        var result = new ScrapInterpreter().Eval(program, typeCheck: false);
        Assert.Equal(Variant("int", Int(5)), result);
    }

    [Fact]
    public void FullPipelineWhere()
    {
        // "a + b ; a = 3 ; b = 4" → #int 7
        var lexSrc   = File.ReadAllText(Path.Combine(SelfHostingDir, "lexer.ss"));
        var parseSrc = File.ReadAllText(Path.Combine(SelfHostingDir, "parser.ss"));
        var evalSrc  = File.ReadAllText(Path.Combine(SelfHostingDir, "eval.ss"));
        var program = """
            (e-mod.eval (p-mod.parse (l-mod.lex "a + b ; a = 3 ; b = 4")) [])
            ; l-mod = (LEXER)
            ; p-mod = (PARSER)
            ; e-mod = (EVAL)
            """
            .Replace("LEXER",  lexSrc)
            .Replace("PARSER", parseSrc)
            .Replace("EVAL",   evalSrc);
        var result = new ScrapInterpreter().Eval(program, typeCheck: false);
        Assert.Equal(Variant("int", Int(7)), result);
    }
}
