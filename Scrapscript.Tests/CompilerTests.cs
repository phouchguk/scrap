using System.Diagnostics;
using Scrapscript.Core;
using Scrapscript.Core.Compiler;
using Xunit;

namespace Scrapscript.Tests;

public class CompilerTests
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private static readonly bool _nodeAvailable = CheckNode();

    private static bool CheckNode()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string RunNode(string program)
    {
        var psi = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(psi)!;
        proc.StandardInput.Write(program);
        proc.StandardInput.Close();
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"node exited {proc.ExitCode}: {stderr.Trim()}");
        return stdout.Trim();
    }

    private static string? CompileAndRun(string src)
    {
        if (!_nodeAvailable) return null;
        var interp = new ScrapInterpreter();
        var expr = interp.CompileToJs(src, includeRuntime: false);
        var program = JsCompiler.Runtime + $"\nprocess.stdout.write(_display({expr}) + \"\\n\");";
        return RunNode(program);
    }

    // Convenience: compile + run, compare to interpreter's Display() output
    private static void AssertCompiles(string src)
    {
        var actual = CompileAndRun(src);
        if (actual == null) return; // node not available
        var expected = new ScrapInterpreter().Eval(src, typeCheck: false).Display();
        Assert.Equal(expected, actual);
    }

    private static void AssertCompiles(string src, string expected)
    {
        var actual = CompileAndRun(src);
        if (actual == null) return;
        Assert.Equal(expected, actual);
    }

    // ── Literals ──────────────────────────────────────────────────────────────

    [Fact] public void CompileInt()       => AssertCompiles("42");
    [Fact] public void CompileNegInt()    => AssertCompiles("-1");
    [Fact] public void CompileFloat()     => AssertCompiles("3.14");
    [Fact] public void CompileFloat1()    => AssertCompiles("1.0");
    [Fact] public void CompileText()      => AssertCompiles("\"hello\"");
    [Fact] public void CompileHole()      => AssertCompiles("()");

    // ── Arithmetic ────────────────────────────────────────────────────────────

    [Fact] public void AddInts()          => AssertCompiles("1 + 1");
    [Fact] public void SubInts()          => AssertCompiles("3 - 2");
    [Fact] public void MulInts()          => AssertCompiles("3 * 5");
    [Fact] public void AddFloats()        => AssertCompiles("1.0 + 1.0");
    [Fact] public void MulPrecedence()    => AssertCompiles("1 + 2 * 3");
    [Fact] public void DivInts()          => AssertCompiles("7 / 2");
    [Fact] public void ModInts()          => AssertCompiles("7 % 3");
    [Fact] public void NegateInt()        => AssertCompiles("-42");
    [Fact] public void NegateVar()        => AssertCompiles("-x ; x = 5");

    // ── Where-clauses ─────────────────────────────────────────────────────────

    [Fact] public void WhereSimple()      => AssertCompiles("x ; x = 1");
    [Fact] public void WhereMultiple()    => AssertCompiles("a + b + c ; a = 1 ; b = 2 ; c = 3");
    [Fact] public void WhereNested()      => AssertCompiles("200 + (x ; x = 150)");
    [Fact] public void WhereMutualRef()   => AssertCompiles("f 1 ; f = a -> a + x ; x = 2");

    // ── Functions ─────────────────────────────────────────────────────────────

    [Fact] public void LambdaIdentity()   => AssertCompiles("(x -> x) 5");
    [Fact] public void LambdaCurried()    => AssertCompiles("f 1 2 ; f = a -> b -> a + b");
    [Fact] public void PartialApply()     => AssertCompiles("add1 5 ; add1 = add 1 ; add = a -> b -> a + b");

    // ── Case functions ────────────────────────────────────────────────────────

    [Fact]
    public void CaseOnInt() =>
        AssertCompiles("f 0 ; f = | 0 -> \"zero\" | 1 -> \"one\" | _ -> \"other\"");

    [Fact]
    public void CaseWildcard() =>
        AssertCompiles("f 99 ; f = | 0 -> \"zero\" | _ -> \"other\"");

    [Fact]
    public void CaseTextPrefix() =>
        AssertCompiles("f \"hello world\" ; f = | \"hello \" ++ rest -> rest | _ -> \"\"");

    [Fact]
    public void CaseListCons() =>
        AssertCompiles("f [1, 2, 3] ; f = | h >+ t -> h | [] -> -1");

    [Fact]
    public void CaseListSlice() =>
        AssertCompiles("f [1, 2, 3, 4] ; f = | [x, y] ++ rest -> x + y | _ -> 0");

    [Fact]
    public void CaseRecord() =>
        AssertCompiles("f { a = 1, b = 2 } ; f = | { a = a, b = b } -> a + b");

    // ── Operators ─────────────────────────────────────────────────────────────

    [Fact] public void TextConcat()  => AssertCompiles("\"hello\" ++ \" \" ++ \"world\"");
    [Fact] public void ListConcat()  => AssertCompiles("[1,2] ++ [3,4]");
    [Fact] public void ListAppend()  => AssertCompiles("[1, 2, 3] +< 4");
    [Fact] public void ListCons()    => AssertCompiles("0 >+ [1, 2]");

    [Fact] public void PipeOp()      => AssertCompiles("1 |> (x -> x + 1)");
    [Fact] public void ReversePipe() => AssertCompiles("(x -> x + 1) <| 1");
    [Fact] public void Compose()     => AssertCompiles("(f >> g) 7 ; f = x -> x + 1 ; g = x -> x * 2");

    // ── Comparison operators ──────────────────────────────────────────────────

    [Fact] public void EqTrue()  => AssertCompiles("1 == 1", "#true");
    [Fact] public void EqFalse() => AssertCompiles("1 == 2", "#false");
    [Fact] public void NeTrue()  => AssertCompiles("1 != 2", "#true");
    [Fact] public void Lt()      => AssertCompiles("1 < 2",  "#true");
    [Fact] public void Gt()      => AssertCompiles("2 > 1",  "#true");
    [Fact] public void Le()      => AssertCompiles("2 <= 2", "#true");
    [Fact] public void Ge()      => AssertCompiles("3 >= 2", "#true");

    // ── Records ───────────────────────────────────────────────────────────────

    [Fact]
    public void RecordFieldAccess() =>
        AssertCompiles("rec.a ; rec = { a = 1, b = \"x\" }");

    [Fact]
    public void RecordSpreadAccess() =>
        AssertCompiles("{ ..g, a = 2 }.a ; g = { a = 1, b = \"x\" }");

    [Fact]
    public void RecordSpreadPreservesOtherFields() =>
        AssertCompiles("{ ..g, a = 2 }.b ; g = { a = 1, b = \"x\" }");

    // ── Lists ─────────────────────────────────────────────────────────────────

    [Fact] public void EmptyList()    => AssertCompiles("[]");
    [Fact] public void ListItems()    => AssertCompiles("[1, 2, 3]");
    [Fact] public void NestedList()   => AssertCompiles("[[1, 2], [3, 4]]");

    // ── Variants ──────────────────────────────────────────────────────────────

    [Fact] public void TagVariant()   => AssertCompiles("#true", "#true");
    [Fact] public void TagFalse()     => AssertCompiles("#false", "#false");

    [Fact]
    public void MatchVariant() =>
        AssertCompiles("f #true ; f = | #true -> 1 | #false -> 0");

    [Fact]
    public void VariantWithPayload() =>
        AssertCompiles("c::radius 4 ; c : #radius int", typeCheck: true);

    [Fact]
    public void VariantMultiPayload() =>
        AssertCompiles("point::two_d 3 4 ; point : #two_d int int #three_d int int int", typeCheck: true);

    // ── Bytes ─────────────────────────────────────────────────────────────────

    [Fact] public void BytesLiteral() => AssertCompiles("~FF");
    [Fact] public void BytesConcat()  => AssertCompiles("~00 ++ ~FF");

    // ── Builtins ─────────────────────────────────────────────────────────────

    [Fact] public void BuiltinToFloat()    => AssertCompiles("to-float 42");
    [Fact] public void BuiltinRound()      => AssertCompiles("round 3.7");
    [Fact] public void BuiltinCeil()       => AssertCompiles("ceil 3.1");
    [Fact] public void BuiltinFloor()      => AssertCompiles("floor 3.9");
    [Fact] public void BuiltinAbs()        => AssertCompiles("abs -5");
    [Fact] public void BuiltinMin()        => AssertCompiles("min 3 5");
    [Fact] public void BuiltinMax()        => AssertCompiles("max 3 5");
    [Fact] public void BuiltinListLength() => AssertCompiles("list/length [1, 2, 3]");
    [Fact] public void BuiltinListFirst()  => AssertCompiles("list/first [10, 20, 30]");
    [Fact] public void BuiltinTextLength() => AssertCompiles("text/length \"hello\"");
    [Fact] public void BuiltinTextUpper()  => AssertCompiles("text/to-upper \"hello\"");
    [Fact] public void BuiltinTextLower()  => AssertCompiles("text/to-lower \"HELLO\"");
    [Fact] public void BuiltinTextTrim()   => AssertCompiles("text/trim \"  hi  \"");
    [Fact] public void BuiltinMaybeDefault() => AssertCompiles("maybe/default 0 (list/first [42])");
    [Fact] public void BuiltinMaybeDefaultNothing() => AssertCompiles("maybe/default 0 (list/first [])");
    [Fact] public void BuiltinStringJoin() => AssertCompiles("string/join \", \" [\"a\", \"b\", \"c\"]");
    [Fact] public void BuiltinListMap()    => AssertCompiles("list/map (x -> x + 1) [1, 2, 3]");
    [Fact] public void BuiltinListFilter() => AssertCompiles("list/filter (x -> x > 2) [1, 2, 3, 4]");
    [Fact] public void BuiltinListFold()   => AssertCompiles("list/fold (a -> b -> a + b) 0 [1, 2, 3, 4]");
    [Fact] public void BuiltinListReverse()     => AssertCompiles("list/reverse [1, 2, 3]");
    [Fact] public void BuiltinBytesToUtf8()     => AssertCompiles("bytes/to-utf8-text ~68");
    [Fact] public void BuiltinIntToText()       => AssertCompiles("int/to-text 42");
    [Fact] public void BuiltinIntToTextNeg()    => AssertCompiles("int/to-text -7");
    [Fact] public void BuiltinFloatToText()     => AssertCompiles("float/to-text 3.14");
    [Fact] public void BuiltinFloatToTextWhole()=> AssertCompiles("float/to-text 2.0");
    [Fact] public void BuiltinTextToInt()       => AssertCompiles("text/to-int \"42\"");
    [Fact] public void BuiltinTextToIntNeg()    => AssertCompiles("text/to-int \"-7\"");
    [Fact] public void BuiltinTextSlice()       => AssertCompiles("text/slice 1 4 \"hello\"");
    [Fact] public void BuiltinTextAt()          => AssertCompiles("text/at 0 \"hello\"");
    [Fact] public void BuiltinTextAtOob()       => AssertCompiles("text/at 10 \"hello\"", typeCheck: false);
    [Fact] public void BuiltinTextChars()       => AssertCompiles("text/chars \"hi\"");
    [Fact] public void BuiltinTextContains()    => AssertCompiles("text/contains \"ell\" \"hello\"");
    [Fact] public void BuiltinTextStartsWith()  => AssertCompiles("text/starts-with \"he\" \"hello\"");
    [Fact] public void BuiltinListRange()       => AssertCompiles("list/range 0 4");
    [Fact] public void BuiltinListRangeOffset() => AssertCompiles("list/range 5 8");

    // ── Higher-order / advanced ───────────────────────────────────────────────

    [Fact]
    public void MutualRecursion()
    {
        // Even/odd via mutual recursion
        var src = "is_even 4 ; is_even = | 0 -> #true | n -> is_odd (n - 1) ; is_odd = | 0 -> #false | n -> is_even (n - 1)";
        AssertCompiles(src, "#true");
    }

    [Fact]
    public void RecursiveFunction()
    {
        var src = "fib 10 ; fib = | 0 -> 0 | 1 -> 1 | n -> fib (n - 1) + fib (n - 2)";
        AssertCompiles(src, "55");
    }

    [Fact]
    public void TypeAnnotationPassthrough() =>
        AssertCompiles("(x : int) ; x = 42");

    [Fact]
    public void TypeDefIsHole() =>
        AssertCompiles("() ; color : #red #green #blue");

    // ── Helpers that accept typeCheck flag ───────────────────────────────────

    private static void AssertCompiles(string src, bool typeCheck)
    {
        var actual = CompileAndRun(src);
        if (actual == null) return;
        var expected = new ScrapInterpreter().Eval(src, typeCheck: typeCheck).Display();
        Assert.Equal(expected, actual);
    }
}
