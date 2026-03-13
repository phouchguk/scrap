using Scrapscript.Core;
using Scrapscript.Core.TypeChecker;
using Xunit;

namespace Scrapscript.Tests;

public class TypeCheckerTests
{
    private static string TypeOf(string src) => new ScrapInterpreter().TypeOf(src);
    private static void AssertTypeError(string src) =>
        Assert.Throws<TypeCheckError>(() => new ScrapInterpreter().Eval(src));
    private static void AssertOk(string src) => new ScrapInterpreter().Eval(src); // should not throw

    // ── Literals ──────────────────────────────────────────────────────────────

    [Fact] public void TypeInt()   => Assert.Equal("int",   TypeOf("42"));
    [Fact] public void TypeFloat() => Assert.Equal("float", TypeOf("3.14"));
    [Fact] public void TypeText()  => Assert.Equal("text",  TypeOf("\"hello\""));
    [Fact] public void TypeBytes() => Assert.Equal("bytes", TypeOf("~FF"));
    [Fact] public void TypeHole()  => Assert.Equal("()",    TypeOf("()"));

    // ── Arithmetic ────────────────────────────────────────────────────────────

    [Fact] public void TypeAddInts()    => Assert.Equal("int",   TypeOf("1 + 2"));
    [Fact] public void TypeAddFloats()  => Assert.Equal("float", TypeOf("1.0 + 2.0"));
    [Fact] public void TypeMulInts()    => Assert.Equal("int",   TypeOf("3 * 5"));

    [Fact]
    public void RejectIntPlusFloat() => AssertTypeError("1 + 1.0");

    [Fact]
    public void RejectTextPlusInt() => AssertTypeError("\"hello\" + 1");

    // ── Text ──────────────────────────────────────────────────────────────────

    [Fact] public void TypeConcat() => Assert.Equal("text", TypeOf("\"a\" ++ \"b\""));

    [Fact]
    public void RejectConcatIntText() => AssertTypeError("1 ++ \"hello\"");

    // ── Lists ─────────────────────────────────────────────────────────────────

    [Fact] public void TypeIntList()  => Assert.Equal("list(int)",  TypeOf("[1, 2, 3]"));
    [Fact] public void TypeTextList() => Assert.Equal("list(text)", TypeOf("[\"a\", \"b\"]"));

    [Fact]
    public void RejectHeterogeneousList() => AssertTypeError("[1, \"a\"]");

    [Fact]
    public void TypeListAppend() => Assert.Equal("list(int)", TypeOf("[1, 2, 3] +< 4"));

    [Fact]
    public void RejectListAppendWrongType() => AssertTypeError("[1, 2, 3] +< \"x\"");

    [Fact]
    public void TypeListCons() => Assert.Equal("list(int)", TypeOf("0 >+ [1, 2]"));

    // ── Functions ─────────────────────────────────────────────────────────────

    [Fact] public void TypeIdentity()  => Assert.StartsWith("('t", TypeOf("x -> x"));
    [Fact] public void TypeAddOne()    => Assert.Equal("(int -> int)", TypeOf("x -> x + 1"));
    [Fact] public void TypeCurried()   => Assert.Matches(@"\('.+ -> \('.+ -> '.+\)\)", TypeOf("a -> b -> a + b"));

    [Fact]
    public void TypeFunctionApplication()
    {
        Assert.Equal("int", TypeOf("f 1 ; f = x -> x + 1"));
    }

    [Fact]
    public void RejectWrongArgType()
    {
        AssertTypeError("f \"hello\" ; f = x -> x + 1");
    }

    [Fact]
    public void TypeLetPolymorphism()
    {
        // id applied to different types in the same scope
        AssertOk("id 1 ; id = x -> x");
        AssertOk("[id 1, id 2] ; id = x -> x");
    }

    // ── Records ───────────────────────────────────────────────────────────────

    [Fact]
    public void TypeRecord() => Assert.StartsWith("{", TypeOf("{ a = 1, b = \"hello\" }"));

    [Fact]
    public void TypeRecordAccess()
    {
        Assert.Equal("int", TypeOf("rec.a ; rec = { a = 1, b = \"x\" }"));
    }

    // ── Variants ──────────────────────────────────────────────────────────────

    [Fact]
    public void TypeValidVariant()
    {
        AssertOk("scoop::chocolate ; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void RejectInvalidVariant()
    {
        AssertTypeError("scoop::banana ; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void TypeVariantWithPayload()
    {
        AssertOk("c::radius 4 ; c : #radius int");
    }

    [Fact]
    public void RejectVariantPayloadWrongType()
    {
        AssertTypeError("c::radius \"hello\" ; c : #radius int");
    }

    [Fact]
    public void RejectInvalidVariantInPattern()
    {
        AssertTypeError(
            "f (scoop::chocolate) " +
            "; f = | #vanilla -> 1 | #banana -> 2 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    // ── Pipe operators ────────────────────────────────────────────────────────

    [Fact]
    public void TypePipe() => Assert.Equal("int", TypeOf("1 |> (x -> x + 1)"));

    [Fact]
    public void TypeReversePipe() => Assert.Equal("int", TypeOf("(x -> x + 1) <| 1"));

    // ── Function composition ──────────────────────────────────────────────────

    [Fact]
    public void TypeCompose()
    {
        Assert.Equal("int", TypeOf("(f >> g) 7 ; f = x -> x + 1 ; g = x -> x * 2"));
    }

    // ── Built-ins ─────────────────────────────────────────────────────────────

    [Fact] public void TypeToFloat()  => Assert.Equal("float", TypeOf("to-float 3"));
    [Fact] public void TypeRound()    => Assert.Equal("int",   TypeOf("round 3.5"));
    [Fact] public void TypeListFirst() => Assert.Equal("maybe(int)", TypeOf("list/first [1, 2, 3]"));
    [Fact] public void TypeListLength() => Assert.Equal("int", TypeOf("list/length [1, 2, 3]"));
    [Fact] public void TypeTextLength() => Assert.Equal("int", TypeOf("text/length \"hello\""));
    [Fact] public void TypeStringJoin() => Assert.Equal("text", TypeOf("string/join \", \" [\"a\", \"b\"]"));
    [Fact] public void TypeBytesToUtf8() => Assert.Equal("text", TypeOf("bytes/to-utf8-text ~FF"));

    [Fact]
    public void RejectToFloatOnText() => AssertTypeError("to-float \"hello\"");

    // ── Case functions ────────────────────────────────────────────────────────

    [Fact]
    public void TypeCaseFunction()
    {
        Assert.Equal("int", TypeOf("f 0 ; f = | 0 -> 1 | _ -> 2"));
    }

    [Fact]
    public void RejectMismatchedCaseBodies()
    {
        AssertTypeError("f 0 ; f = | 0 -> 1 | _ -> \"other\"");
    }

    // ── Spec examples ─────────────────────────────────────────────────────────

    [Fact]
    public void TypeWhereClause() => Assert.Equal("int", TypeOf("a + b + c ; a = 1 ; b = 2 ; c = 3"));

    [Fact]
    public void TypeFunctionSpec() => Assert.Equal("int", TypeOf("f 1 2 ; f = a -> b -> a + b"));

    [Fact]
    public void TypePatternMatchSpec()
    {
        Assert.Equal("int", TypeOf("f \"b\" ; f = | \"a\" -> 1 | \"b\" -> 2 | _ -> 0"));
    }

    // ── Exhaustive match ──────────────────────────────────────────────────────

    [Fact]
    public void ExhaustiveMatchAllVariants()
    {
        AssertOk(
            "f (scoop::vanilla) " +
            "; f = | #vanilla -> 1 | #chocolate -> 2 | #strawberry -> 3 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void ExhaustiveMatchWildcard()
    {
        AssertOk(
            "f (scoop::chocolate) " +
            "; f = | #vanilla -> 1 | _ -> 0 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void ExhaustiveMatchVarCatchAll()
    {
        AssertOk(
            "f (scoop::chocolate) " +
            "; f = | #vanilla -> 1 | x -> 0 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void ExhaustiveMatchIntPatternsSkipped()
    {
        // No exhaustiveness check for non-variant types — must not throw
        AssertOk("f 0 ; f = | 0 -> 1 | 1 -> 2");
    }

    [Fact]
    public void RejectNonExhaustiveOneMissing()
    {
        AssertTypeError(
            "f (scoop::vanilla) " +
            "; f = | #vanilla -> 1 | #chocolate -> 2 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void RejectNonExhaustiveManyMissing()
    {
        AssertTypeError(
            "f (scoop::vanilla) " +
            "; f = | #vanilla -> 1 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    // ── Comparison operators ──────────────────────────────────────────────────

    // ── Division ──────────────────────────────────────────────────────────────

    [Fact] public void TypeDivInts()   => Assert.Equal("int",   TypeOf("6 / 2"));
    [Fact] public void TypeDivFloats() => Assert.Equal("float", TypeOf("6.0 / 2.0"));
    [Fact] public void RejectDivIntFloat() => AssertTypeError("1 / 1.0");

    // ── Redundant literal arms ─────────────────────────────────────────────────

    [Fact]
    public void RejectRedundantIntArm()
    {
        AssertTypeError("f 1 ; f = | 0 -> \"a\" | 1 -> \"b\" | 0 -> \"c\"");
    }

    [Fact]
    public void RejectRedundantTextArm()
    {
        AssertTypeError("f \"x\" ; f = | \"a\" -> 1 | \"b\" -> 2 | \"a\" -> 3");
    }

    // ── Comparison operators ──────────────────────────────────────────────────

    [Fact] public void TypeEqInts()    => Assert.Equal("bool", TypeOf("1 == 2"));
    [Fact] public void TypeNeqInts()   => Assert.Equal("bool", TypeOf("1 != 2"));
    [Fact] public void TypeLtInts()    => Assert.Equal("bool", TypeOf("1 < 2"));
    [Fact] public void TypeGtFloats()  => Assert.Equal("bool", TypeOf("1.0 > 2.0"));
    [Fact] public void TypeLtEqText()  => Assert.Equal("bool", TypeOf("\"a\" <= \"b\""));

    [Fact]
    public void RejectEqIntText() => AssertTypeError("1 == \"hello\"");

    [Fact]
    public void RejectLtOnList() => AssertTypeError("[1] < [2]");

    [Fact]
    public void BoolExhaustivenessOk()
    {
        AssertOk("(| #true -> 1 | #false -> 0) (1 == 1)");
    }

    [Fact]
    public void RejectNonExhaustiveBool()
    {
        AssertTypeError("(| #true -> 1) (1 == 1)");
    }

    // ── Negation ──────────────────────────────────────────────────────────────

    [Fact] public void TypeNegateInt()   => Assert.Equal("int",   TypeOf("-x ; x = 5"));
    [Fact] public void TypeNegateFloat() => Assert.Equal("float", TypeOf("-x ; x = 1.5"));
    [Fact] public void RejectNegateText() => AssertTypeError("-x ; x = \"hi\"");

    // ── list/map, list/filter, list/fold ──────────────────────────────────────

    [Fact] public void TypeListMap() =>
        Assert.Equal("list(int)", TypeOf("list/map (n -> n * 2) [1, 2, 3]"));

    [Fact] public void TypeListMapPolymorphic() =>
        Assert.Equal("list(text)", TypeOf("list/map (n -> \"x\") [1, 2, 3]"));

    [Fact] public void TypeListFilter() =>
        Assert.Equal("list(int)", TypeOf("list/filter (n -> n == 0) [1, 2, 3]"));

    [Fact] public void TypeListFold() =>
        Assert.Equal("int", TypeOf("list/fold (acc -> n -> acc + n) 0 [1, 2, 3]"));

    [Fact] public void RejectListMapWrongFn() =>
        AssertTypeError("list/map (n -> n + 1) [\"a\", \"b\"]");

    // ── Modulo ────────────────────────────────────────────────────────────────

    [Fact] public void TypeModInts()   => Assert.Equal("int",   TypeOf("7 % 3"));
    [Fact] public void TypeModFloats() => Assert.Equal("float", TypeOf("7.0 % 3.0"));
    [Fact] public void RejectModIntFloat() => AssertTypeError("7 % 3.0");

    // ── Row polymorphism ──────────────────────────────────────────────────────

    [Fact]
    public void RowPolyFieldAccess()
    {
        // f infers as { a : int | ... } -> int
        Assert.Equal("int", TypeOf("f { a = 1, b = \"x\" } ; f = r -> r.a + 1"));
    }

    [Fact]
    public void RowPolyTwoFields()
    {
        // f uses both r.a and r.b — both must be int
        Assert.Equal("int", TypeOf("f { a = 1, b = 2 } ; f = r -> r.a + r.b"));
    }

    [Fact]
    public void RowPolyPassedToTwoFunctions()
    {
        // g uses r for two open-record functions simultaneously
        AssertOk("g { a = 1, b = 2 } ; g = r -> f r + r.b ; f = r -> r.a");
    }

    [Fact]
    public void RejectMissingField()
    {
        // { b = 1 } doesn't have field a
        AssertTypeError("f { b = 1 } ; f = r -> r.a + 1");
    }

    [Fact]
    public void RejectWrongFieldType()
    {
        // r.a is text, can't add 1
        AssertTypeError("f { a = \"hi\" } ; f = r -> r.a + 1");
    }

    // ── Record spread ─────────────────────────────────────────────────────────

    [Fact]
    public void SpreadKnownRecordOk()
    {
        AssertOk("{ ..r, b = 2 } ; r = { a = 1 }");
    }

    [Fact]
    public void RejectSpreadNonRecord()
    {
        AssertTypeError("{ ..x, a = 1 } ; x = 5");
    }

    // ── Redundant arms ────────────────────────────────────────────────────────

    [Fact]
    public void RejectRedundantVariantArm()
    {
        AssertTypeError(
            "f (scoop::vanilla) " +
            "; f = | #vanilla -> 1 | #chocolate -> 2 | #vanilla -> 3 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    [Fact]
    public void NoRedundantArmsOk()
    {
        AssertOk(
            "f (scoop::vanilla) " +
            "; f = | #vanilla -> 1 | #chocolate -> 2 | #strawberry -> 3 " +
            "; scoop : #vanilla #chocolate #strawberry");
    }

    // ── Type annotations ──────────────────────────────────────────────────────

    [Fact]
    public void AnnotationMatchesInferredType()
    {
        AssertOk("f 1 ; f : int -> int = x -> x + 1");
    }

    [Fact]
    public void RejectAnnotationConflict()
    {
        AssertTypeError("f ; f : int = \"hello\"");
    }

    [Fact]
    public void AnnotationWithNamedType()
    {
        AssertOk("greet (person::cowboy) ; greet : person -> text = | #cowboy -> \"howdy\" | _ -> \"hi\" ; person : #cowboy #other");
    }

    [Fact]
    public void PolymorphicAnnotationOk()
    {
        AssertOk("id 1 ; id : a -> a = x -> x");
    }
}
