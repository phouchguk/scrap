namespace Scrapscript.Core.Lexer;

public enum TokenType
{
    // Literals
    Int,
    Float,
    Text,
    HexByte,
    Base64Bytes,
    Hole,       // ()

    // Identifier-like
    Identifier,  // plain name or module path (e.g. list/first)
    HashTag,     // #identifier  (tagged variant / pattern)
    HashRef,     // $sha1~~...
    ScrapRef,    // user/name@n  (detected as identifier with @ and digits)

    // Operators
    Plus,        // +
    Minus,       // -
    Star,        // *
    PlusPlus,    // ++
    PlusLt,      // +<
    GtPlus,      // >+
    GtGt,        // >>
    PipeGt,      // |>
    LtPipe,      // <|
    Arrow,       // ->
    FatArrow,    // =>
    ColonColon,  // ::
    Colon,       // :
    Semicolon,   // ;
    Equals,      // =
    EqEq,        // ==
    NotEq,       // !=
    Lt,          // <
    Gt,          // >
    LtEq,        // <=
    GtEq,        // >=
    Pipe,        // |
    DotDot,      // ..
    At,          // @
    Dollar,      // $
    TildeTilde,  // ~~
    Tilde,       // ~
    Backtick,    // `

    // Punctuation
    LParen,      // (
    RParen,      // )
    LBracket,    // [
    RBracket,    // ]
    LBrace,      // {
    RBrace,      // }
    Comma,       // ,
    Dot,         // .

    // Special
    Wildcard,    // _
    Newline,
    Eof,
}

public record Token(TokenType Type, string Text, int Line, int Col)
{
    public override string ToString() => $"{Type}({Text}) at {Line}:{Col}";
}
