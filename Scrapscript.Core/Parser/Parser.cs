using Scrapscript.Core.Lexer;

namespace Scrapscript.Core.Parser;

public class ParseError(string message) : Exception(message);

public class Parser(List<Token> tokens)
{
    private int _pos = 0;

    private Token Current => tokens[_pos];
    private Token Peek(int offset = 1) =>
        (_pos + offset) < tokens.Count ? tokens[_pos + offset] : tokens[^1];

    private Token Consume()
    {
        var t = Current;
        _pos++;
        return t;
    }

    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
            throw new ParseError($"Expected {type} but got {Current.Type}({Current.Text}) at {Current.Line}:{Current.Col}");
        return Consume();
    }

    private bool Check(TokenType type) => Current.Type == type;
    private bool Match(TokenType type) { if (Check(type)) { Consume(); return true; } return false; }

    // ── Entry point ──────────────────────────────────────────────────────────

    public Expr ParseProgram()
    {
        var expr = ParseWhere();
        Expect(TokenType.Eof);
        return expr;
    }

    // ── Where (lowest precedence): expr ; x = e ; y = e ... ─────────────────

    private Expr ParseWhere()
    {
        var body = ParsePipe();
        if (!Check(TokenType.Semicolon))
            return body;

        var bindings = new List<Binding>();
        while (Check(TokenType.Semicolon))
        {
            Consume(); // ;
            var pat = ParsePattern();

            if (Current.Type == TokenType.Colon)
            {
                Consume(); // :
                var name = pat is VarPat vp ? vp.Name : throw new ParseError("Type binding requires a simple name");
                var typeDef = ParseTypeExpr();
                if (Current.Type == TokenType.Equals)
                {
                    // Type-annotated value binding: ; name : type = value
                    Consume(); // =
                    var val = ParsePipe();
                    bindings.Add(new Binding(pat, new TypeAnnotation(val, typeDef)));
                }
                else
                {
                    // Pure type definition: ; name : #variant1 #variant2 ...
                    bindings.Add(new Binding(pat, new TypeDefExpr(name, typeDef)));
                }
            }
            else
            {
                Expect(TokenType.Equals);
                var val = ParsePipe();
                bindings.Add(new Binding(pat, val));
            }
        }
        return new WhereExpr(body, bindings);
    }

    // ── Pipe: left |> right ──────────────────────────────────────────────────

    private Expr ParsePipe()
    {
        var left = ParseRightPipe();
        while (Check(TokenType.PipeGt))
        {
            Consume();
            var right = ParseRightPipe();
            // left |> right  =>  right left  (apply right to left)
            left = new ApplyExpr(right, left);
        }
        return left;
    }

    private Expr ParseRightPipe()
    {
        var right = ParseCompose();
        if (Check(TokenType.LtPipe))
        {
            Consume();
            var left = ParseRightPipe();
            // right <| left  =>  right left
            return new ApplyExpr(right, left);
        }
        return right;
    }

    // ── Function composition: f >> g ─────────────────────────────────────────

    private Expr ParseCompose()
    {
        var left = ParseBinOp();
        while (Check(TokenType.GtGt))
        {
            Consume();
            var right = ParseBinOp();
            // f >> g  => x -> g (f x)  — represent as BinOp for now, eval handles it
            left = new BinOpExpr(">>", left, right);
        }
        return left;
    }

    // ── Binary operators ─────────────────────────────────────────────────────

    private Expr ParseBinOp()
    {
        return ParseComparison();
    }

    private Expr ParseComparison()
    {
        var left = ParseConcat();
        while (Check(TokenType.EqEq) || Check(TokenType.NotEq) ||
               Check(TokenType.Lt)   || Check(TokenType.Gt)    ||
               Check(TokenType.LtEq) || Check(TokenType.GtEq))
        {
            var op = Consume().Text;
            var right = ParseConcat();
            left = new BinOpExpr(op, left, right);
        }
        return left;
    }

    private Expr ParseConcat()
    {
        var left = ParseCons();
        while (Check(TokenType.PlusPlus))
        {
            Consume();
            var right = ParseCons();
            left = new BinOpExpr("++", left, right);
        }
        return left;
    }

    private Expr ParseCons()
    {
        var left = ParseAppend();
        while (Check(TokenType.GtPlus))
        {
            Consume();
            var right = ParseAppend();
            left = new BinOpExpr(">+", left, right);
        }
        return left;
    }

    private Expr ParseAppend()
    {
        var left = ParseAddSub();
        while (Check(TokenType.PlusLt))
        {
            Consume();
            var right = ParseAddSub();
            left = new BinOpExpr("+<", left, right);
        }
        return left;
    }

    private Expr ParseAddSub()
    {
        var left = ParseMulDiv();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Consume().Text;
            var right = ParseMulDiv();
            left = new BinOpExpr(op, left, right);
        }
        return left;
    }

    private Expr ParseMulDiv()
    {
        var left = ParseTypeAnnotation();
        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var op = Consume().Text;
            var right = ParseTypeAnnotation();
            left = new BinOpExpr(op, left, right);
        }
        return left;
    }

    // ── Type annotation: expr : type ─────────────────────────────────────────

    private Expr ParseTypeAnnotation()
    {
        var expr = ParseLambda();
        if (Check(TokenType.Colon))
        {
            Consume();
            var typeExpr = ParseTypeExpr();
            return new TypeAnnotation(expr, typeExpr);
        }
        return expr;
    }

    // ── Lambda: pattern -> body ───────────────────────────────────────────────

    private Expr ParseLambda()
    {
        // Case function: | pat -> body | pat -> body ...
        if (Check(TokenType.Pipe))
        {
            var arms = new List<CaseArm>();
            while (Check(TokenType.Pipe))
            {
                Consume(); // |
                var pat = ParsePattern();
                Expect(TokenType.Arrow);
                // Body extends until next | or ; — use ParsePipe (not ParseWhere)
                // Nested where-clauses in case bodies must be parenthesized
                var body = ParsePipe();
                arms.Add(new CaseArm(pat, body));
            }
            return new CaseExpr(arms);
        }

        var expr = ParseApplication();

        if (Check(TokenType.Arrow))
        {
            // The already-parsed expr should be a pattern-like thing.
            Consume(); // ->
            // Lambda body extends to the right, but NOT across ; at the outer level.
            // Nested where-clauses in lambda bodies must be parenthesized.
            var body = ParsePipe();
            var pat = ExprToPattern(expr);
            return new LambdaExpr(pat, body);
        }
        return expr;
    }

    // ── Application (left-associative, highest precedence after atoms) ───────

    private Expr ParseApplication()
    {
        var fn = ParseConstructor();
        while (IsAtomStart())
        {
            var arg = ParseConstructor();
            fn = new ApplyExpr(fn, arg);
        }
        return fn;
    }

    // ── Constructor: expr::variant ────────────────────────────────────────────

    private Expr ParseConstructor()
    {
        var expr = ParseRecordAccess();
        if (Check(TokenType.ColonColon))
        {
            Consume();
            // Variant name may start with digits (e.g. ::2d) — lexed as Int+"d" or just Identifier
            string variant;
            if (Current.Type == TokenType.Int && Peek().Type == TokenType.Identifier)
            {
                var digits = Consume().Text;
                var letters = Consume().Text;
                variant = digits + letters;
            }
            else if (Current.Type == TokenType.Int)
            {
                variant = Consume().Text;
            }
            else
            {
                variant = Expect(TokenType.Identifier).Text;
            }
            return new ConstructorExpr(expr, variant);
        }
        return expr;
    }

    // ── Record access: expr.field ─────────────────────────────────────────────

    private Expr ParseRecordAccess()
    {
        var expr = ParseAtom();
        while (Check(TokenType.Dot))
        {
            Consume();
            var field = Expect(TokenType.Identifier).Text;
            expr = new RecordAccess(expr, field);
        }
        return expr;
    }

    // ── Atoms ─────────────────────────────────────────────────────────────────

    private bool IsAtomStart()
    {
        return Current.Type switch
        {
            TokenType.Int or TokenType.Float or TokenType.Text or
            TokenType.HexByte or TokenType.Base64Bytes or TokenType.Hole or
            TokenType.Identifier or TokenType.HashTag or TokenType.HashRef or
            TokenType.Wildcard or TokenType.LParen or TokenType.LBracket or
            TokenType.LBrace or TokenType.TildeTilde or TokenType.Tilde => true,
            TokenType.Minus when Peek().Type is TokenType.Int or TokenType.Float
                && Current.Col + 1 == Peek().Col => true,
            _ => false
        };
    }

    private Expr ParseAtom()
    {
        var tok = Current;
        switch (tok.Type)
        {
            case TokenType.Int:
                Consume();
                return new IntLit(long.Parse(tok.Text));
            case TokenType.Float:
                Consume();
                return new FloatLit(double.Parse(tok.Text, System.Globalization.CultureInfo.InvariantCulture));
            case TokenType.Text:
                Consume();
                return new TextLit(tok.Text);
            case TokenType.Hole:
                Consume();
                return new HoleLit();
            case TokenType.HexByte:
                Consume();
                return new BytesLit(new[] { Convert.ToByte(tok.Text, 16) });
            case TokenType.Base64Bytes:
                Consume();
                return new BytesLit(Convert.FromBase64String(tok.Text));
            case TokenType.HashRef:
                Consume();
                return new HashRef(tok.Text);
            case TokenType.HashTag:
                Consume();
                // #tag may or may not have a payload — that's handled at application level
                return new ConstructorExpr(new Var("__variant__"), tok.Text);
            case TokenType.Identifier:
                Consume();
                // name@version → MapRef
                if (Check(TokenType.At) && Peek().Type == TokenType.Int)
                {
                    Consume(); // @
                    var versionTok = Consume(); // Int
                    return new MapRef(tok.Text, int.Parse(versionTok.Text));
                }
                return new Var(tok.Text);
            case TokenType.Wildcard:
                Consume();
                return new Var("_");
            case TokenType.LParen:
                return ParseParenthesized();
            case TokenType.LBracket:
                return ParseList();
            case TokenType.LBrace:
                return ParseRecord();
            case TokenType.Minus:
                // Unary minus: -n, -n.f, or -(expr)
                Consume();
                if (Current.Type == TokenType.Int)
                    return new IntLit(-long.Parse(Consume().Text));
                if (Current.Type == TokenType.Float)
                    return new FloatLit(-double.Parse(Consume().Text, System.Globalization.CultureInfo.InvariantCulture));
                // General negation
                return new NegExpr(ParseAtom());
            default:
                throw new ParseError($"Unexpected token {tok.Type}({tok.Text}) at {tok.Line}:{tok.Col}");
        }
    }

    private Expr ParseParenthesized()
    {
        Expect(TokenType.LParen);
        // Check for tuple/grouped type expressions (int -> int) etc.
        var expr = ParseWhere();
        Expect(TokenType.RParen);
        return expr;
    }

    private Expr ParseList()
    {
        Expect(TokenType.LBracket);
        var items = new List<Expr>();
        if (!Check(TokenType.RBracket))
        {
            items.Add(ParseWhere());
            while (Check(TokenType.Comma))
            {
                Consume();
                items.Add(ParseWhere());
            }
        }
        Expect(TokenType.RBracket);
        return new ListExpr(items);
    }

    private Expr ParseRecord()
    {
        Expect(TokenType.LBrace);
        string? spread = null;
        var fields = new List<(string, Expr)>();

        if (Check(TokenType.DotDot))
        {
            Consume();
            spread = Expect(TokenType.Identifier).Text;
            if (Check(TokenType.Comma)) Consume();
        }

        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            var name = Expect(TokenType.Identifier).Text;
            Expect(TokenType.Equals);
            var val = ParseWhere();
            fields.Add((name, val));
            if (Check(TokenType.Comma)) Consume();
        }
        Expect(TokenType.RBrace);
        return new RecordExpr(fields, spread);
    }

    // ── Patterns ─────────────────────────────────────────────────────────────

    private Pattern ParsePattern()
    {
        return ParseConsPattern();
    }

    private Pattern ParseConsPattern()
    {
        var pat = ParseConcatPattern();
        while (Check(TokenType.GtPlus))
        {
            Consume();
            var tail = ParseConcatPattern();
            pat = new ConsPat(pat, tail);
        }
        return pat;
    }

    private Pattern ParseConcatPattern()
    {
        var pat = ParseAtomPattern();
        while (Check(TokenType.PlusPlus))
        {
            Consume();
            // The right side of ++ in a pattern is a variable that captures the rest
            var restPat = ParseAtomPattern();
            if (pat is TextPat tp && restPat is VarPat vp)
                pat = new TextPat(tp.Prefix, vp.Name);
            else if (pat is ListPat lp && restPat is VarPat tvp)
                pat = new ListPat(lp.Items, tvp.Name);
            else if (pat is BytesPat bp && restPat is VarPat bvp)
                // bytes ++ rest: for now just treat as bytes pattern
                pat = new ListPat(new List<Pattern>(), bvp.Name); // placeholder
            else
                throw new ParseError($"Invalid ++ pattern at {Current.Line}:{Current.Col}");
        }
        return pat;
    }

    private Pattern ParseAtomPattern()
    {
        var tok = Current;
        switch (tok.Type)
        {
            case TokenType.Wildcard:
                Consume();
                return new WildcardPat();
            case TokenType.Int:
                Consume();
                return new IntPat(long.Parse(tok.Text));
            case TokenType.Minus:
                // Negative integer pattern: - n
                Consume();
                var numTok = Expect(TokenType.Int);
                return new IntPat(-long.Parse(numTok.Text));
            case TokenType.Text:
                Consume();
                return new TextPat(tok.Text, null);
            case TokenType.HexByte:
                Consume();
                return new BytesPat(new[] { Convert.ToByte(tok.Text, 16) });
            case TokenType.Base64Bytes:
                Consume();
                // Base64 bytes in pattern (for bytes prefix matching)
                return new BytesPat(Convert.FromBase64String(tok.Text));
            case TokenType.Hole:
                Consume();
                return new HolePat();
            case TokenType.HashTag:
                Consume();
                // #tag optionally followed by a payload pattern
                string tag = tok.Text;
                Pattern? payload = null;
                if (IsPatternStart())
                    payload = ParseAtomPattern();
                return new VariantPat(tag, payload);
            case TokenType.Identifier:
                Consume();
                return new VarPat(tok.Text);
            case TokenType.LBracket:
                return ParseListPattern();
            case TokenType.LBrace:
                return ParseRecordPattern();
            case TokenType.LParen:
                Consume();
                var inner = ParsePattern();
                Expect(TokenType.RParen);
                return inner;
            default:
                throw new ParseError($"Expected pattern but got {tok.Type}({tok.Text}) at {tok.Line}:{tok.Col}");
        }
    }

    private bool IsPatternStart()
    {
        return Current.Type switch
        {
            TokenType.Wildcard or TokenType.Int or TokenType.Text or
            TokenType.HexByte or TokenType.Base64Bytes or TokenType.Hole or
            TokenType.HashTag or TokenType.Identifier or TokenType.LBracket or
            TokenType.LBrace or TokenType.LParen or TokenType.Minus => true,
            _ => false
        };
    }

    private Pattern ParseListPattern()
    {
        Expect(TokenType.LBracket);
        var items = new List<Pattern>();
        if (!Check(TokenType.RBracket))
        {
            items.Add(ParsePattern());
            while (Check(TokenType.Comma))
            {
                Consume();
                items.Add(ParsePattern());
            }
        }
        Expect(TokenType.RBracket);
        return new ListPat(items, null);
    }

    private Pattern ParseRecordPattern()
    {
        Expect(TokenType.LBrace);
        string? spread = null;
        var fields = new List<(string, Pattern)>();

        if (Check(TokenType.DotDot))
        {
            Consume();
            spread = Expect(TokenType.Identifier).Text;
            if (Check(TokenType.Comma)) Consume();
        }

        while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
        {
            var name = Expect(TokenType.Identifier).Text;
            Expect(TokenType.Equals);
            var pat = ParsePattern();
            fields.Add((name, pat));
            if (Check(TokenType.Comma)) Consume();
        }
        Expect(TokenType.RBrace);
        return new RecordPat(fields, spread);
    }

    // ── Type expressions ──────────────────────────────────────────────────────

    private TypeExpr ParseTypeExpr()
    {
        // Collect variants or type names / generics
        // Simplified: just parse variant definitions
        var variants = new List<(string, TypeExpr?)>();
        while (Check(TokenType.HashTag) || (Check(TokenType.Identifier) && IsTypeParamStart()))
        {
            if (Check(TokenType.Identifier))
            {
                // generic param: x =>
                var name = Consume().Text;
                Expect(TokenType.FatArrow);
                var body = ParseTypeExpr();
                return new GenericType(name, body);
            }
            if (Check(TokenType.HashTag))
            {
                var tag = Consume().Text;
                // Consume all consecutive type atoms (for multi-arg variants like #two-d int int)
                TypeExpr? payload = null;
                while (IsTypeAtomStart())
                {
                    var atom = ParseTypeAtom();
                    payload = payload == null ? atom : new ApplyType(payload, atom);
                }
                variants.Add((tag, payload));
            }
        }
        if (variants.Count > 0)
            return new VariantType(variants);

        return ParseTypeAtom();
    }

    private bool IsTypeParamStart()
    {
        // x => pattern: identifier followed by =>
        return Peek().Type == TokenType.FatArrow;
    }

    private bool IsTypeAtomStart()
    {
        // HashTag is NOT a type atom — it's a variant constructor, handled in ParseTypeExpr
        return Current.Type switch
        {
            TokenType.Identifier or TokenType.LParen or TokenType.LBrace => true,
            _ => false
        };
    }

    private TypeExpr ParseTypeAtom()
    {
        if (Check(TokenType.LBrace))
        {
            // Record type: { fieldName : type, ... }
            Consume();
            var fields = new List<(string, TypeExpr)>();
            while (!Check(TokenType.RBrace) && !Check(TokenType.Eof))
            {
                var fieldName = Expect(TokenType.Identifier).Text;
                Expect(TokenType.Colon);
                var fieldType = ParseTypeAtom();
                fields.Add((fieldName, fieldType));
                if (Check(TokenType.Comma)) Consume();
            }
            Expect(TokenType.RBrace);
            return new RecordTypeExpr(fields);
        }
        if (Check(TokenType.LParen))
        {
            Consume();
            var inner = ParseTypeExpr();
            // May be a function type: (T -> U)
            if (Check(TokenType.Arrow))
            {
                Consume();
                var ret = ParseTypeExpr();
                Expect(TokenType.RParen);
                return new FuncType(inner, ret);
            }
            Expect(TokenType.RParen);
            return inner;
        }
        if (Check(TokenType.Identifier))
        {
            var name = Consume().Text;
            // Handle function type: A -> B
            if (Check(TokenType.Arrow))
            {
                Consume();
                var ret = ParseTypeAtom();
                return new FuncType(new NamedType(name), ret);
            }
            return new NamedType(name);
        }
        throw new ParseError($"Expected type expression at {Current.Line}:{Current.Col}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Pattern ExprToPattern(Expr expr)
    {
        return expr switch
        {
            Var v when v.Name == "_" => new WildcardPat(),
            Var v => new VarPat(v.Name),
            IntLit i => new IntPat(i.Value),
            TextLit t => new TextPat(t.Value, null),
            BytesLit b => new BytesPat(b.Value),
            HoleLit => new HolePat(),
            ListExpr l => new ListPat(l.Items.Select(ExprToPattern).ToList(), null),
            ConstructorExpr c when c.TypeExpr is Var v && v.Name == "__variant__" =>
                new VariantPat(c.Variant, null),
            ConstructorExpr c => new VariantPat(c.Variant, null),
            BinOpExpr { Op: ">+" } b =>
                new ConsPat(ExprToPattern(b.Left), ExprToPattern(b.Right)),
            BinOpExpr { Op: "++" } b =>
                ExprConcatToPattern(b),
            RecordExpr r => new RecordPat(
                r.Fields.Select(f => (f.Field, ExprToPattern(f.Value))).ToList(),
                r.Spread),
            ApplyExpr a when a.Fn is ConstructorExpr c =>
                new VariantPat(c.Variant, ExprToPattern(a.Arg)),
            _ => throw new ParseError($"Cannot convert expression to pattern: {expr}")
        };
    }

    private Pattern ExprConcatToPattern(BinOpExpr b)
    {
        var left = ExprToPattern(b.Left);
        var right = ExprToPattern(b.Right);
        if (left is TextPat tp && right is VarPat vp)
            return new TextPat(tp.Prefix, vp.Name);
        if (left is ListPat lp && right is VarPat lvp)
            return new ListPat(lp.Items, lvp.Name);
        throw new ParseError($"Invalid ++ pattern");
    }
}
