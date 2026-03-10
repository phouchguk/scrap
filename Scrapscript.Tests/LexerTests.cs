using Scrapscript.Core.Lexer;
using Xunit;

namespace Scrapscript.Tests;

public class LexerTests
{
    private static List<Token> Lex(string src) => new Lexer(src).Tokenize();
    private static TokenType[] Types(string src) => Lex(src).Select(t => t.Type).ToArray();
    private static string[] Texts(string src) => Lex(src).Select(t => t.Text).ToArray();

    [Fact] public void Integer() => Assert.Equal([TokenType.Int, TokenType.Eof], Types("42"));
    [Fact] public void NegativeInteger() => Assert.Equal([TokenType.Minus, TokenType.Int, TokenType.Eof], Types("-1"));
    [Fact] public void Float() => Assert.Equal([TokenType.Float, TokenType.Eof], Types("3.14"));
    [Fact] public void Text() { var toks = Lex("\"hello\""); Assert.Equal(TokenType.Text, toks[0].Type); Assert.Equal("hello", toks[0].Text); }
    [Fact] public void Hole() => Assert.Equal([TokenType.Hole, TokenType.Eof], Types("()"));
    [Fact] public void HexByte() { var toks = Lex("~FF"); Assert.Equal(TokenType.HexByte, toks[0].Type); Assert.Equal("FF", toks[0].Text); }
    [Fact] public void Base64Bytes() { var toks = Lex("~~aGVsbG8="); Assert.Equal(TokenType.Base64Bytes, toks[0].Type); Assert.Equal("aGVsbG8=", toks[0].Text); }
    [Fact] public void Identifier() { var toks = Lex("foo"); Assert.Equal(TokenType.Identifier, toks[0].Type); Assert.Equal("foo", toks[0].Text); }
    [Fact] public void ModulePath() { var toks = Lex("list/first"); Assert.Equal(TokenType.Identifier, toks[0].Type); Assert.Equal("list/first", toks[0].Text); }
    [Fact] public void HashTag() { var toks = Lex("#true"); Assert.Equal(TokenType.HashTag, toks[0].Type); Assert.Equal("true", toks[0].Text); }
    [Fact] public void HashRef() { var toks = Lex("$sha1~~abc123"); Assert.Equal(TokenType.HashRef, toks[0].Type); }
    [Fact] public void Wildcard() => Assert.Equal([TokenType.Wildcard, TokenType.Eof], Types("_"));
    [Fact] public void OperatorPlus() => Assert.Equal(TokenType.Plus, Lex("+")[0].Type);
    [Fact] public void OperatorPlusPlus() => Assert.Equal(TokenType.PlusPlus, Lex("++")[0].Type);
    [Fact] public void OperatorPlusLt() => Assert.Equal(TokenType.PlusLt, Lex("+<")[0].Type);
    [Fact] public void OperatorGtPlus() => Assert.Equal(TokenType.GtPlus, Lex(">+")[0].Type);
    [Fact] public void OperatorGtGt() => Assert.Equal(TokenType.GtGt, Lex(">>")[0].Type);
    [Fact] public void OperatorPipeGt() => Assert.Equal(TokenType.PipeGt, Lex("|>")[0].Type);
    [Fact] public void OperatorLtPipe() => Assert.Equal(TokenType.LtPipe, Lex("<|")[0].Type);
    [Fact] public void OperatorArrow() => Assert.Equal(TokenType.Arrow, Lex("->")[0].Type);
    [Fact] public void OperatorFatArrow() => Assert.Equal(TokenType.FatArrow, Lex("=>")[0].Type);
    [Fact] public void OperatorColonColon() => Assert.Equal(TokenType.ColonColon, Lex("::")[0].Type);
    [Fact] public void OperatorColon() => Assert.Equal(TokenType.Colon, Lex(":")[0].Type);
    [Fact] public void OperatorSemicolon() => Assert.Equal(TokenType.Semicolon, Lex(";")[0].Type);
    [Fact] public void OperatorPipe() => Assert.Equal(TokenType.Pipe, Lex("|")[0].Type);
    [Fact] public void OperatorDotDot() => Assert.Equal(TokenType.DotDot, Lex("..")[0].Type);
    [Fact] public void OperatorDot() => Assert.Equal(TokenType.Dot, Lex(".")[0].Type);
    [Fact] public void Brackets() => Assert.Equal([TokenType.LBracket, TokenType.RBracket, TokenType.Eof], Types("[]"));
    [Fact] public void Braces() => Assert.Equal([TokenType.LBrace, TokenType.RBrace, TokenType.Eof], Types("{}"));
    [Fact] public void Comma() => Assert.Equal(TokenType.Comma, Lex(",")[0].Type);

    [Fact]
    public void SimpleExpression()
    {
        var types = Types("1 + 2");
        Assert.Equal([TokenType.Int, TokenType.Plus, TokenType.Int, TokenType.Eof], types);
    }

    [Fact]
    public void TextEscape()
    {
        var toks = Lex("\"hello\\nworld\"");
        Assert.Equal("hello\nworld", toks[0].Text);
    }

    [Fact]
    public void WhereClause()
    {
        var types = Types("x ; x = 1");
        Assert.Equal([TokenType.Identifier, TokenType.Semicolon, TokenType.Identifier, TokenType.Equals, TokenType.Int, TokenType.Eof], types);
    }
}
