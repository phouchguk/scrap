using Scrapscript.Core;
using Scrapscript.Core.Scrapyard;
using Scrapscript.Core.Serialization;
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
    public void DisambiguateSharedTagNameCorrectPayload()
    {
        // hand::left checks int payload; leg::left checks text payload — both ok individually
        AssertOk("hand::left 5 ; hand : #left int #right int ; leg : #left text #right text");
        AssertOk("leg::left \"knee\" ; hand : #left int #right int ; leg : #left text #right text");
    }

    [Fact]
    public void DisambiguateSharedTagNameWrongPayloadRejected()
    {
        // hand::left with a text payload is a type error (hand declares #left int)
        AssertTypeError("hand::left \"oops\" ; hand : #left int #right int ; leg : #left text #right text");
    }

    [Fact]
    public void DisambiguateSharedTagNameMixedListRejected()
    {
        // hand ≠ leg — a list cannot hold both (nominal typing, same as Elm)
        AssertTypeError("[hand::left 5, leg::left \"knee\"] ; hand : #left int #right int ; leg : #left text #right text");
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

    [Fact]
    public void InlineTypeExhaustivenessOk()
    {
        Assert.Equal("int", TypeOf("(#a #b #c)::a |> | #a -> 1 | #b -> 2 | #c -> 3"));
    }

    [Fact]
    public void RejectInlineTypeNonExhaustive()
    {
        AssertTypeError("(#a #b #c)::a |> | #a -> 1 | #b -> 2");
    }

    [Fact]
    public void RejectInlineTypeInvalidVariant()
    {
        AssertTypeError("(#a #b #c)::z");
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

    [Fact]
    public void AnonVariantAnnotationRejectsMissingArm()
    {
        // (#foo #bar) in annotation registers synthetic type; #bar is missing
        AssertTypeError("0 ; f : ((#foo #bar) -> int) = | #foo -> 1");
    }

    [Fact]
    public void AnonVariantAnnotationAcceptsAllArms()
    {
        AssertOk("0 ; f : ((#foo #bar) -> int) = | #foo -> 1 | #bar -> 2");
    }

    [Fact]
    public void AnonVariantAnnotationAcceptsWildcard()
    {
        AssertOk("0 ; f : ((#foo #bar) -> int) = | #foo -> 1 | _ -> 0");
    }

    // ── Typed bare constructors ───────────────────────────────────────────────

    [Fact]
    public void TypeBareConstructorRegistersType()
    {
        Assert.Equal("$anon_foo", TypeOf("#foo"));
    }

    [Fact]
    public void KnownTagUnaffectedByPart1()
    {
        Assert.Equal("bool", TypeOf("#true"));
    }

    [Fact]
    public void CaseInfersArgTypeFromVariantPatterns()
    {
        Assert.StartsWith("($anon_foo_bar ->", TypeOf("| #foo -> 1 | #bar -> 2"));
    }

    [Fact]
    public void BareConstructorWidensToMultiVariantCaseType()
    {
        // Bare #foo finds the $anon_foo_bar type registered by the case, so f #foo is valid.
        AssertOk("f #foo ; f = | #foo -> 1 | #bar -> 2");
    }

    [Fact]
    public void RejectWildcardCaseWithUnknownVariant()
    {
        AssertTypeError("| #foo -> 1 | _ -> 0");
    }

    [Fact]
    public void WildcardCaseWithKnownVariantOk()
    {
        AssertOk("(| #true -> 1 | _ -> 0) (1 == 1)");
    }

    [Fact]
    public void CaseSyntheticTypeIsExhaustive()
    {
        AssertOk("(#foo #bar)::foo |> | #foo -> 1 | #bar -> 2");
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

    // ── Record type annotations ───────────────────────────────────────────────

    [Fact]
    public void RecordAnnotationAccepted()
    {
        AssertOk("42 ; rec : { a : int, b : text } = { a = 1, b = \"hello\" }");
    }

    [Fact]
    public void RecordAnnotationRejectsWrongFieldType()
    {
        AssertTypeError("42 ; rec : { a : int } = { a = \"oops\" }");
    }

    [Fact]
    public void RecordAnnotationRejectsMissingField()
    {
        AssertTypeError("42 ; rec : { a : int, b : text } = { a = 1 }");
    }

    [Fact]
    public void RecordAnnotationRejectsExtraField()
    {
        // closed record — extra field not in annotation is a mismatch
        AssertTypeError("42 ; rec : { a : int } = { a = 1, b = 2 }");
    }

    [Fact]
    public void RecordAnnotationNamedTypeField()
    {
        AssertOk("42 ; rec : { flag : bool } = { flag = #true }");
    }

    [Fact]
    public void RecordAnnotationNamedTypeFieldRejectsWrongType()
    {
        AssertTypeError("42 ; rec : { flag : bool } = { flag = 1 }");
    }

    [Fact]
    public void RecordAnnotationGenericField()
    {
        AssertOk("42 ; rec : { n : maybe int } = { n = #just 1 }");
    }

    [Fact]
    public void RecordAnnotationGenericFieldNothingVariant()
    {
        AssertOk("42 ; rec : { n : maybe int } = { n = #nothing }");
    }

    [Fact]
    public void RecordAnnotationGenericFieldRejectsWrongPayload()
    {
        AssertTypeError("42 ; rec : { n : maybe int } = { n = #just \"oops\" }");
    }

    [Fact]
    public void RecordAnnotationNamedVariantField()
    {
        // Named type declared first — bare #foo resolves to that named type
        AssertOk("42 ; rec : { a : foobar } = { a = #foo } ; foobar : #foo #bar");
    }

    [Fact]
    public void RecordAnnotationInlineVariantField()
    {
        // Inline variant type in annotation — requires explicit (#foo #bar)::foo
        // because bare #foo gets one-variant type $anon_foo, not the two-variant $anon_foo_bar
        AssertOk("42 ; rec : { a : (#foo #bar) } = { a = (#foo #bar)::foo }");
    }

    [Fact]
    public void RecordAnnotationFunctionField()
    {
        AssertOk("42 ; rec : { f : (int -> int) } = { f = n -> n + 1 }");
    }

    [Fact]
    public void RecordAnnotationFunctionFieldRejectsWrongReturnType()
    {
        AssertTypeError("42 ; rec : { f : (int -> int) } = { f = n -> \"oops\" }");
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

    // ── Annotation-guided inference for recursive bindings ────────────────────

    [Fact]
    public void AnnotatedRecursiveFunctionOk()
    {
        // The annotation type is used as the placeholder during body inference,
        // so the recursive self-reference sees int -> int immediately.
        Assert.Equal("int",
            TypeOf("factorial 5 ; factorial : int -> int = | 0 -> 1 | n -> n * factorial (n - 1)"));
    }

    [Fact]
    public void RejectAnnotatedRecursiveWrongReturnType()
    {
        // Annotation says int -> text but every arm returns int.
        AssertTypeError("f 1 ; f : int -> text = | 0 -> 1 | n -> f (n - 1) + 1");
    }

    [Fact]
    public void RejectAnnotatedRecursiveWrongArgType()
    {
        // Annotation says text -> int but recursive call passes int.
        AssertTypeError("f \"hi\" ; f : text -> int = | _ -> f 0");
    }

    [Fact]
    public void AnnotatedMutualRecursionOk()
    {
        // Both bindings annotated; each self-reference sees the precise declared type.
        AssertOk(
            "even 4" +
            " ; even : int -> bool = | 0 -> #true  | n -> odd  (n - 1)" +
            " ; odd  : int -> bool = | 0 -> #false | n -> even (n - 1)");
    }

    [Fact]
    public void AnnotatedRecursiveWithNamedTypeOk()
    {
        // Annotation introduces the named type; exhaustiveness checked correctly.
        AssertOk(
            "describe (flavour::vanilla)" +
            " ; describe : flavour -> text = | #vanilla -> \"plain\" | #chocolate -> \"rich\"" +
            " ; flavour : #vanilla #chocolate");
    }

    [Fact]
    public void RejectAnnotatedRecursiveNonExhaustive()
    {
        // Annotation with named type; missing arm should be caught.
        AssertTypeError(
            "describe (flavour::vanilla)" +
            " ; describe : flavour -> text = | #vanilla -> \"plain\"" +
            " ; flavour : #vanilla #chocolate");
    }

    [Fact]
    public void AnnotatedWithPayloadVariantOk()
    {
        // Multi-arg payload: #rect stores [w, h] as a ScrapList; _ matches the whole payload.
        AssertOk(
            "describe (shape::circle 3.0)" +
            " ; describe : shape -> text = | #circle _ -> \"round\" | #rect _ -> \"square\"" +
            " ; shape : #circle float #rect float float");
    }

    [Fact]
    public void RejectAnnotatedPayloadVariantNonExhaustive()
    {
        AssertTypeError(
            "describe (shape::circle 3.0)" +
            " ; describe : shape -> text = | #circle _ -> \"round\"" +
            " ; shape : #circle float #rect float float");
    }

    // ── Hash-ref type inference ───────────────────────────────────────────────

    private static (LocalYard yard, ScrapInterpreter interp) MakeTempYard()
    {
        var yard = new LocalYard(Path.Combine(Path.GetTempPath(), "scrap-tc-" + Guid.NewGuid()));
        yard.Init();
        return (yard, new ScrapInterpreter(yard));
    }

    private static string PushAndHashRef(LocalYard yard, string src)
    {
        var interp = new ScrapInterpreter(yard);
        var value = interp.Eval(src, typeCheck: false);
        return "$" + yard.Push(FlatEncoder.Encode(value));
    }

    [Fact]
    public void HashRefIntTypeInferred()
    {
        var (yard, interp) = MakeTempYard();
        var hashRef = PushAndHashRef(yard, "42");
        Assert.Equal("int", interp.TypeOf(hashRef));
        interp.Eval($"{hashRef} + 1"); // should not throw
    }

    [Fact]
    public void HashRefTextTypeInferred()
    {
        var (yard, interp) = MakeTempYard();
        var hashRef = PushAndHashRef(yard, "\"hello\"");
        interp.Eval($"{hashRef} ++ \" world\""); // should not throw
    }

    [Fact]
    public void HashRefListTypeInferred()
    {
        var (yard, interp) = MakeTempYard();
        var hashRef = PushAndHashRef(yard, "[1, 2, 3]");
        interp.Eval($"list/length {hashRef}"); // should not throw
    }

    [Fact]
    public void HashRefRecordTypeInferred()
    {
        var (yard, interp) = MakeTempYard();
        var hashRef = PushAndHashRef(yard, "{ x = 1, y = 2 }");
        // Use where-binding so the record access parses correctly
        interp.Eval($"r.x + 1 ; r = {hashRef}"); // should not throw
    }

    [Fact]
    public void HashRefWrongTypeRejected()
    {
        var (yard, interp) = MakeTempYard();
        var hashRef = PushAndHashRef(yard, "\"text\"");
        Assert.Throws<TypeCheckError>(() => interp.Eval($"{hashRef} + 1"));
    }

    [Fact]
    public void HashRefNotFoundRemainsOpaque()
    {
        var (_, interp) = MakeTempYard();
        // Fake hash — not in yard; type checker should treat as opaque (no TypeCheckError)
        // We only run type checking, not eval, to avoid a runtime "not found" error.
        interp.TypeOf("$sha1~~0000000000000000000000000000000000000000");
    }

    // ── List type annotations ─────────────────────────────────────────────────

    [Fact]
    public void ListTypeAnnotationOk()
    {
        AssertOk("f [1, 2, 3] ; f : [int] -> int = xs -> 0");
    }

    [Fact]
    public void RejectListTypeAnnotationWrongElement()
    {
        AssertTypeError("f [1, 2, 3] ; f : [text] -> int = xs -> 0");
    }

    [Fact]
    public void PolymorphicListAnnotationOk()
    {
        AssertOk("f [1, 2, 3] ; f : [a] -> a = xs -> 1");
    }

    // ── Recursive / multi-payload variants ────────────────────────────────────

    [Fact]
    public void MultiPayloadVariantInference()
    {
        // f infers argument type as a two-payload variant; l and r get independent types
        AssertOk("(f (#node 1 2)) ; f = | #node l r -> l + r");
    }

    [Fact]
    public void RecursiveAnonymousVariantType()
    {
        // depth infers a recursive anonymous tree type without an explicit type declaration
        AssertOk(
            "(depth (#node (#node #leaf #leaf) #leaf))" +
            "; depth = | #leaf -> 0 | #node l r -> 1 + (depth l)");
    }

    [Fact]
    public void ExplicitRecursiveTypeDeclOk()
    {
        // tree : #leaf #node tree tree — explicit recursive type declaration
        AssertOk(
            "(depth (#node (#node #leaf #leaf) #leaf))" +
            "; depth : tree -> int = | #leaf -> 0 | #node l r -> 1 + (depth l)" +
            "; tree : #leaf #node tree tree");
    }

}
