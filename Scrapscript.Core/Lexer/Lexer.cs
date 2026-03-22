using System.Text;
using System.Text.RegularExpressions;

namespace Scrapscript.Core.Lexer;

public class LexError(string message) : Exception(message);

public class Lexer(string source)
{
    private readonly string _src = source;
    private int _pos = 0;
    private int _line = 1;
    private int _col = 1;

    private char Current => _pos < _src.Length ? _src[_pos] : '\0';
    private char Peek(int offset = 1) => (_pos + offset) < _src.Length ? _src[_pos + offset] : '\0';

    private void Advance(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (_pos < _src.Length && _src[_pos] == '\n')
            {
                _line++;
                _col = 1;
            }
            else
            {
                _col++;
            }
            _pos++;
        }
    }

    private Token Make(TokenType type, string text, int line, int col) =>
        new Token(type, text, line, col);

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (_pos < _src.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _src.Length) break;

            var tok = ReadToken();
            if (tok != null)
                tokens.Add(tok);
        }
        tokens.Add(new Token(TokenType.Eof, "", _line, _col));
        return tokens;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            char c = Current;
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                Advance();
            }
            else
            {
                break;
            }
        }
    }

    private Token? ReadToken()
    {
        int line = _line, col = _col;
        char c = Current;

        // Text literal
        if (c == '"')
            return ReadText(line, col);

        // Base64 bytes: ~~
        if (c == '~' && Peek() == '~')
        {
            Advance(2);
            var b64 = ReadBase64();
            return Make(TokenType.Base64Bytes, b64, line, col);
        }

        // Hex byte: ~XX
        if (c == '~')
        {
            Advance(); // skip ~
            if (!IsHexDigit(Current))
                throw new LexError($"Expected hex digit after ~ at {line}:{col}");
            var hex = new StringBuilder();
            while (IsHexDigit(Current))
            {
                hex.Append(Current);
                Advance();
            }
            return Make(TokenType.HexByte, hex.ToString(), line, col);
        }

        // Hash tag: #identifier
        if (c == '#')
        {
            Advance();
            // Tag names may start with a digit (e.g. #2d, #3d)
            if (IsIdentStart(Current) || char.IsDigit(Current))
            {
                var name = ReadIdentifier();
                return Make(TokenType.HashTag, name, line, col);
            }
            throw new LexError($"Expected identifier after # at {line}:{col}");
        }

        // Hash ref: $sha1~~...
        if (c == '$')
        {
            Advance();
            var rest = new StringBuilder();
            while (_pos < _src.Length && !char.IsWhiteSpace(Current) && Current != ')' && Current != ']' && Current != '}' && Current != ',' && Current != ';')
            {
                rest.Append(Current);
                Advance();
            }
            return Make(TokenType.HashRef, rest.ToString(), line, col);
        }

        // Number (int or float) — note: '-' is always emitted as Minus; parser handles negation
        if (char.IsDigit(c))
            return ReadNumber(line, col);

        // Operators and punctuation
        switch (c)
        {
            case '+':
                if (Peek() == '+') { Advance(2); return Make(TokenType.PlusPlus, "++", line, col); }
                if (Peek() == '<') { Advance(2); return Make(TokenType.PlusLt, "+<", line, col); }
                Advance(); return Make(TokenType.Plus, "+", line, col);
            case '-':
                if (Peek() == '>') { Advance(2); return Make(TokenType.Arrow, "->", line, col); }
                Advance(); return Make(TokenType.Minus, "-", line, col);
            case '*':
                Advance(); return Make(TokenType.Star, "*", line, col);
            case '/':
                Advance(); return Make(TokenType.Slash, "/", line, col);
            case '%':
                Advance(); return Make(TokenType.Percent, "%", line, col);
            case '>':
                if (Peek() == '+') { Advance(2); return Make(TokenType.GtPlus, ">+", line, col); }
                if (Peek() == '>') { Advance(2); return Make(TokenType.GtGt, ">>", line, col); }
                if (Peek() == '=') { Advance(2); return Make(TokenType.GtEq, ">=", line, col); }
                Advance(); return Make(TokenType.Gt, ">", line, col);
            case '|':
                if (Peek() == '>') { Advance(2); return Make(TokenType.PipeGt, "|>", line, col); }
                Advance(); return Make(TokenType.Pipe, "|", line, col);
            case '<':
                if (Peek() == '|') { Advance(2); return Make(TokenType.LtPipe, "<|", line, col); }
                if (Peek() == '=') { Advance(2); return Make(TokenType.LtEq, "<=", line, col); }
                Advance(); return Make(TokenType.Lt, "<", line, col);
            case '=':
                if (Peek() == '>') { Advance(2); return Make(TokenType.FatArrow, "=>", line, col); }
                if (Peek() == '=') { Advance(2); return Make(TokenType.EqEq, "==", line, col); }
                Advance(); return Make(TokenType.Equals, "=", line, col);
            case '!':
                if (Peek() == '=') { Advance(2); return Make(TokenType.NotEq, "!=", line, col); }
                Advance(); throw new LexError($"Unexpected '!' at {line}:{col}");
            case ':':
                if (Peek() == ':') { Advance(2); return Make(TokenType.ColonColon, "::", line, col); }
                Advance(); return Make(TokenType.Colon, ":", line, col);
            case ';':
                Advance(); return Make(TokenType.Semicolon, ";", line, col);
            case '.':
                if (Peek() == '.') { Advance(2); return Make(TokenType.DotDot, "..", line, col); }
                Advance(); return Make(TokenType.Dot, ".", line, col);
            case '@':
                Advance(); return Make(TokenType.At, "@", line, col);
            case '(':
                // Check for hole: ()
                if (Peek() == ')')
                {
                    Advance(2);
                    return Make(TokenType.Hole, "()", line, col);
                }
                Advance(); return Make(TokenType.LParen, "(", line, col);
            case ')':
                Advance(); return Make(TokenType.RParen, ")", line, col);
            case '[':
                Advance(); return Make(TokenType.LBracket, "[", line, col);
            case ']':
                Advance(); return Make(TokenType.RBracket, "]", line, col);
            case '{':
                Advance(); return Make(TokenType.LBrace, "{", line, col);
            case '}':
                Advance(); return Make(TokenType.RBrace, "}", line, col);
            case ',':
                Advance(); return Make(TokenType.Comma, ",", line, col);
            case '`':
                Advance(); return Make(TokenType.Backtick, "`", line, col);
        }

        // Wildcard
        if (c == '_' && !IsIdentChar(Peek()))
        {
            Advance();
            return Make(TokenType.Wildcard, "_", line, col);
        }

        // Identifier (including wildcard-prefix like _name, module paths, etc.)
        if (IsIdentStart(c) || c == '_')
        {
            var ident = ReadIdentifier();
            return Make(TokenType.Identifier, ident, line, col);
        }

        throw new LexError($"Unexpected character '{c}' at {line}:{col}");
    }

    private Token ReadText(int line, int col)
    {
        Advance(); // skip opening "
        var sb = new StringBuilder();
        while (_pos < _src.Length && Current != '"')
        {
            if (Current == '\\')
            {
                Advance();
                switch (Current)
                {
                    case 'n': sb.Append('\n'); Advance(); break;
                    case 't': sb.Append('\t'); Advance(); break;
                    case '"': sb.Append('"'); Advance(); break;
                    case '\\': sb.Append('\\'); Advance(); break;
                    default:
                        sb.Append('\\');
                        sb.Append(Current);
                        Advance();
                        break;
                }
            }
            else if (Current == '`')
            {
                // Text interpolation: `expr`  — for now, treat as literal delimiter
                // We'll emit the backtick token to signal interpolation
                // Simple approach: include up to next backtick as-is in string token
                // For full support, the parser needs to handle this specially.
                // For now, just collect everything between backticks as part of the string.
                sb.Append('`');
                Advance();
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }
        if (_pos >= _src.Length)
            throw new LexError($"Unterminated string at {line}:{col}");
        Advance(); // skip closing "
        return Make(TokenType.Text, sb.ToString(), line, col);
    }

    private Token ReadNumber(int line, int col)
    {
        var sb = new StringBuilder();
        while (char.IsDigit(Current))
        {
            sb.Append(Current);
            Advance();
        }
        if (Current == '.' && char.IsDigit(Peek()))
        {
            sb.Append('.');
            Advance();
            while (char.IsDigit(Current))
            {
                sb.Append(Current);
                Advance();
            }
            return Make(TokenType.Float, sb.ToString(), line, col);
        }
        return Make(TokenType.Int, sb.ToString(), line, col);
    }

    private string ReadBase64()
    {
        var sb = new StringBuilder();
        while (_pos < _src.Length && IsBase64Char(Current))
        {
            sb.Append(Current);
            Advance();
        }
        return sb.ToString();
    }

    private string ReadIdentifier()
    {
        var sb = new StringBuilder();
        while (_pos < _src.Length && IsIdentChar(Current))
        {
            sb.Append(Current);
            Advance();
        }
        return sb.ToString();
    }

    private static bool IsIdentStart(char c) =>
        char.IsLetter(c) || c == '_';

    private static bool IsIdentChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '/';

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsBase64Char(char c) =>
        char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=';
}
