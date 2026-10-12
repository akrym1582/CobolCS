using CobolCS.Frontend.Parsing;
using Xunit;
using static CobolCS.Frontend.Parsing.Values;
using static CobolCS.IntegrationTests.RegressionSupport;

namespace CobolCS.IntegrationTests;

/// <summary>既存の test_workflow_safety の入力、期待値、失敗条件を保持する.NET回帰テストです。</summary>
public sealed class OwnershipTests
{
    private readonly V state = new();

    /// <summary>ownership and paths の回帰条件を検証します。</summary>
    [Fact]
    public void TestOwnershipAndPaths()
    {
        V base_670 = Nil;
        V before_671 = Nil;
        V changed_source_672 = Nil;
        V content_673 = Nil;
        V deleted_674 = Nil;
        V edited_675 = Nil;
        V fixture_676 = Nil;
        V helper_677 = Nil;
        V initial_678 = Nil;
        V legacy_679 = Nil;
        V link_680 = Nil;
        V lower_681 = Nil;
        V manifest_682 = Nil;
        V manifest_path_683 = Nil;
        V nested_684 = Nil;
        V old_685 = Nil;
        V old_path_686 = Nil;
        V output_687 = Nil;
        V protected_688 = Nil;
        V source_689 = Nil;
        V stub_690 = Nil;
        V temp_691 = Nil;
        V unknown_692 = Nil;
        V upper_693 = Nil;
        using var folder694 = new TestDirectory();
        temp_691 = PathValue(folder694.Path);
        base_670 = PathValue(Text(temp_691));
        output_687 = Divide(base_670, S("generated"));
        source_689 = Divide(Root, S("examples/WORKFLOW/PAYMENT.cbl"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_687 })), I(0));
        manifest_path_683 = Divide(output_687, S("manifest.json"));
        initial_678 = ReadJson(TestCall(manifest_path_683, "read_text"));
        deleted_674 = Next(TestCall(Divide(output_687, S("UserCode/Scenarios")), "glob", S("*.cs")));
        _ = TestCall(deleted_674, "unlink");
        fixture_676 = Divide(output_687, S("Fixtures/bytes.json"));
        _ = TestCall(TestMember(fixture_676, "parent"), "mkdir");
        _ = TestCall(fixture_676, "write_bytes", new V(Convert.FromHexString("7b22696e70757473223a5b223130302e3030225d7d0d0a")));
        nested_684 = Divide(output_687, S("UserCode/nested/obj/cache"));
        _ = TestCall(TestMember(nested_684, "parent"), "mkdir", Named("parents", B(true)));
        _ = TestCall(nested_684, "write_text", S("discard build artifact"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_687 })), I(0));
        _ = CheckFalse(TestCall(deleted_674, "exists"));
        _ = RunChecked(ListOf(S("dotnet"), S("build"), Divide(output_687, S("CobolCS.Generated.csproj")), S("-m:1")), I(0));
        _ = CheckEqual(TestCall(fixture_676, "read_bytes"), new V(Convert.FromHexString("7b22696e70757473223a5b223130302e3030225d7d0d0a")));
        _ = CheckFalse(TestCall(nested_684, "exists"));
        unknown_692 = Divide(output_687, S("surprise.txt"));
        _ = TestCall(unknown_692, "write_text", S("preserve"));
        before_671 = TestCall(manifest_path_683, "read_bytes");
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_687 })), I(2));
        _ = CheckEqual(TestCall(manifest_path_683, "read_bytes"), before_671);
        _ = CheckEqual(TestCall(unknown_692, "read_text"), S("preserve"));
        _ = TestCall(unknown_692, "unlink");
        link_680 = Divide(output_687, S("UserCode/escape.cs"));
        _ = TestCall(link_680, "symlink_to", source_689);
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_687 })), I(2));
        _ = TestCall(link_680, "unlink");
        upper_693 = Divide(output_687, S("Fixtures/A.json"));
        lower_681 = Divide(output_687, S("Fixtures/a.json"));
        _ = TestCall(upper_693, "write_text", S("{}"));
        _ = TestCall(lower_681, "write_text", S("{}"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_687 })), I(2));
        _ = TestCall(upper_693, "unlink");
        _ = TestCall(lower_681, "unlink");
        manifest_682 = ReadJson(TestCall(manifest_path_683, "read_text"));
        manifest_682[S("manifestSchemaVersion")] = I(99);
        _ = TestCall(manifest_path_683, "write_text", WriteJson(manifest_682));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_687 })), I(2));
        _ = TestCall(manifest_path_683, "write_bytes", before_671);
        legacy_679 = Divide(base_670, S("legacy"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { legacy_679 })), I(0));
        old_path_686 = Divide(legacy_679, S("manifest.json"));
        old_685 = ReadJson(TestCall(old_path_686, "read_text"));
        _ = TestCall(old_685, "pop", S("manifestSchemaVersion"));
        _ = TestCall(old_685, "pop", S("userTemplateHashes"));
        _ = TestCall(old_path_686, "write_text", WriteJson(old_685));
        stub_690 = Next(TestCall(Divide(legacy_679, S("Stubs")), "rglob", S("Dependency_*.cs")));
        _ = TestCall(stub_690, "write_text", TestCall(TestCall(stub_690, "read_text"), "replace", S("Implement call"), S("User note: Implement call")));
        edited_675 = TestCall(stub_690, "read_bytes");
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { legacy_679 })), I(0));
        _ = CheckEqual(TestCall(stub_690, "read_bytes"), edited_675);
        _ = CheckEqual(ReadJson(TestCall(old_path_686, "read_text"))[S("manifestSchemaVersion")], I(2));
        protected_688 = Divide(base_670, S("helper-binding"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_689 }).Concat(new V[] { S("--output") }).Concat(new V[] { protected_688 })), I(0));
        helper_677 = Next(TestCall(Divide(protected_688, S("Stubs")), "rglob", S("NoOp_*.cs")));
        _ = TestCall(helper_677, "write_text", TestAdd(TestCall(helper_677, "read_text"), S("// User-owned exploration response.\n")));
        content_673 = TestCall(helper_677, "read_bytes");
        changed_source_672 = Divide(base_670, S("PAYMENT.cbl"));
        _ = TestCall(changed_source_672, "write_text", TestCall(TestCall(source_689, "read_text"), "replace", S("FEE-SERVICE"), S("OTHER-FEE")));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { changed_source_672 }).Concat(new V[] { S("--output") }).Concat(new V[] { protected_688 })), I(2));
        _ = CheckEqual(TestCall(helper_677, "read_bytes"), content_673);
    }

    /// <summary>new program and strict registration の回帰条件を検証します。</summary>
    [Fact]
    public void TestNewProgramAndStrictRegistration()
    {
        V base_695 = Nil;
        V content_696 = Nil;
        V duplicate_697 = Nil;
        V existing_698 = Nil;
        V manifest_699 = Nil;
        V output_700 = Nil;
        V result_701 = Nil;
        V source_702 = Nil;
        V strict_703 = Nil;
        V temp_704 = Nil;
        using var folder705 = new TestDirectory();
        temp_704 = PathValue(folder705.Path);
        base_695 = PathValue(Text(temp_704));
        source_702 = Divide(base_695, S("sources"));
        _ = TestCall(source_702, "mkdir");
        _ = TestCall(Divide(source_702, S("one.cbl")), "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. ONE. DATA DIVISION. WORKING-STORAGE SECTION. 01 class PIC 9. PROCEDURE DIVISION. DISPLAY \"ONE\" GOBACK."));
        output_700 = Divide(base_695, S("generated"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_702 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_700 })), I(0));
        existing_698 = Next(TestCall(Divide(output_700, S("UserCode/Scenarios")), "glob", S("*.cs")));
        _ = TestCall(existing_698, "write_text", TestAdd(TestCall(existing_698, "read_text"), S("\n// Retain this user edit.\n")));
        content_696 = TestCall(existing_698, "read_bytes");
        _ = TestCall(Divide(source_702, S("two.cbl")), "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. TWO. DATA DIVISION. WORKING-STORAGE SECTION. 01 class PIC 9. PROCEDURE DIVISION. DISPLAY \"TWO\" GOBACK."));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_702 }).Concat(new V[] { S("--output") }).Concat(new V[] { output_700 })), I(0));
        _ = CheckEqual(TestCall(existing_698, "read_bytes"), content_696);
        result_701 = ReadJson(TestMember(RunChecked(ListOf(S("dotnet"), S("run"), S("--project"), output_700, S("--"), S("TWO"), S("--scenario"), S("sample")), I(0)), "stdout"));
        _ = CheckEqual(result_701[S("display")], ListOf(S("TWO")));
        _ = TestCall(Divide(source_702, S("bad.cbl")), "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. BAD. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC X(4). PROCEDURE DIVISION. STRING \"A\" INTO T END-STRING GOBACK."));
        strict_703 = Divide(base_695, S("strict"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_702 }).Concat(new V[] { S("--output") }).Concat(new V[] { strict_703 }).Concat(new V[] { S("--mode") }).Concat(new V[] { S("strict") })), I(1));
        manifest_699 = ReadJson(TestCall(Divide(strict_703, S("manifest.json")), "read_text"));
        _ = CheckEqual(manifest_699[S("rejectedPrograms")], ListOf(S("BAD")));
        _ = CheckEqual(SetCopy(manifest_699[S("programs")]), SetOf(S("ONE"), S("TWO")));
        result_701 = ReadJson(TestMember(RunChecked(ListOf(S("dotnet"), S("run"), S("--project"), strict_703, S("--"), S("ONE"), S("--scenario"), S("sample")), I(0)), "stdout"));
        _ = CheckEqual(result_701[S("display")], ListOf(S("ONE")));
        _ = CheckFalse(Any(Sequence(from item706 in Items(ListOf(S("Api"), S("Testing"), S("Tests/Cases"), S("UserCode"))) let folder_707 = item706 from item708 in Items(TestCall(Divide(strict_703, folder_707), "rglob", S("*"))) let p_709 = item708 select B(Contains(TestStringValue(p_709), S("CBAD_"))))));
        _ = TestCall(Divide(source_702, S("lower.cbl")), "write_text", S("IDENTIFICATION DIVISION. PROGRAM-ID. one. PROCEDURE DIVISION. GOBACK."));
        duplicate_697 = Divide(base_695, S("duplicate"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { source_702 }).Concat(new V[] { S("--output") }).Concat(new V[] { duplicate_697 }).Concat(new V[] { S("--mode") }).Concat(new V[] { S("strict") })), I(1));
        _ = CheckFalse(TestCall(Divide(duplicate_697, S("CobolCS.Generated.csproj")), "exists"));
    }

    /// <summary>adoption rejects changed or skipped case の回帰条件を検証します。</summary>
    [Fact]
    public void TestAdoptionRejectsChangedOrSkippedCase()
    {
        V base_710 = Nil;
        V case_711 = Nil;
        V mutation_712 = Nil;
        V original_case_713 = Nil;
        V output_714 = Nil;
        V review_715 = Nil;
        V review_path_716 = Nil;
        V temp_717 = Nil;
        using var folder718 = new TestDirectory();
        temp_717 = PathValue(folder718.Path);
        base_710 = PathValue(Text(temp_717));
        output_714 = Divide(base_710, S("generated"));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("generate") }).Concat(new V[] { Divide(Root, S("examples/WORKFLOW/PAYMENT.cbl")) }).Concat(new V[] { S("--output") }).Concat(new V[] { output_714 })), I(0));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(ToolCli)).Concat(new V[] { S("prepare-workflow") }).Concat(new V[] { S("--out") }).Concat(new V[] { output_714 })), I(0));
        review_path_716 = Divide(output_714, S("Tests/review.json"));
        review_715 = ReadJson(TestCall(review_path_716, "read_text"));
        review_715[S("cases")][I(0)][S("hasNoOpCalls")] = B(true);
        _ = TestCall(review_path_716, "write_text", WriteJson(review_715));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_714 }).Concat(new V[] { S("--output") }).Concat(new V[] { Divide(base_710, S("no-op")) })), I(1));
        _ = CheckFalse(TestCall(Divide(base_710, S("no-op")), "exists"));
        review_715[S("cases")][I(0)][S("hasNoOpCalls")] = B(false);
        case_711 = Next(TestCall(Divide(output_714, S("Tests/Cases")), "glob", S("*.cs")));
        original_case_713 = TestCall(case_711, "read_text");
        _ = TestCall(case_711, "write_text", TestCall(TestCall(case_711, "read_text"), "replace", S("[Fact]"), S("[Fact(Skip = \"Synthetic skip rejection test\")]")));
        review_715[S("cases")][I(0)][S("files")][TestStringValue(TestCall(case_711, "relative_to", output_714))] = Digest(TestCall(case_711, "read_bytes"));
        _ = TestCall(review_path_716, "write_text", WriteJson(review_715));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_714 }).Concat(new V[] { S("--output") }).Concat(new V[] { Divide(base_710, S("skip")) })), I(1));
        _ = CheckFalse(TestCall(Divide(base_710, S("skip")), "exists"));
        _ = CheckTrue(TestCall(Divide(output_714, S("manifest.json")), "exists"));
        _ = CheckFalse(ListCopy(TestCall(base_710, "glob", S("*.stage-*"))));
        _ = TestCall(case_711, "write_text", TestCall(TestCall(case_711, "read_text"), "replace", S("[Fact(Skip = \"Synthetic skip rejection test\")]\n    "), S(string.Empty)));
        review_715[S("cases")][I(0)][S("files")][TestStringValue(TestCall(case_711, "relative_to", output_714))] = Digest(TestCall(case_711, "read_bytes"));
        _ = TestCall(review_path_716, "write_text", WriteJson(review_715));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_714 }).Concat(new V[] { S("--output") }).Concat(new V[] { Divide(base_710, S("zero-tests")) })), I(1));
        _ = CheckFalse(TestCall(Divide(base_710, S("zero-tests")), "exists"));
        mutation_712 = S("Directory.CreateDirectory(Path.Combine(Review.Root(), \"Fixtures\"));\n        File.WriteAllText(Path.Combine(Review.Root(), \"Fixtures/changed.txt\"), \"changed during test\");\n        ");
        _ = TestCall(case_711, "write_text", TestCall(original_case_713, "replace", S("Assert.False(result.HasNoOpCalls);"), TestAdd(mutation_712, S("Assert.False(result.HasNoOpCalls);"))));
        review_715[S("cases")][I(0)][S("files")][TestStringValue(TestCall(case_711, "relative_to", output_714))] = Digest(TestCall(case_711, "read_bytes"));
        _ = TestCall(review_path_716, "write_text", WriteJson(review_715));
        _ = RunChecked(ListOf(Enumerable.Empty<V>().Concat(Items(CliArguments)).Concat(new V[] { S("adopt") }).Concat(new V[] { output_714 }).Concat(new V[] { S("--output") }).Concat(new V[] { Divide(base_710, S("changed-during-test")) })), I(2));
        _ = CheckFalse(TestCall(Divide(base_710, S("changed-during-test")), "exists"));
        _ = CheckFalse(TestCall(Divide(output_714, S("Fixtures/changed.txt")), "exists"));
    }
}
