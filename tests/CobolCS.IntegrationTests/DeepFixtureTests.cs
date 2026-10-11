using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>出典確認済みの外部fixtureを独立期待値に対して生成・ビルド・実行します。</summary>
[TestClass]
public sealed class DeepFixtureTests
{
    /// <summary>手計算・文字幅・分岐から求めた表示を、通常source backendで検証します。</summary>
    /// <param name="name">専用ディレクトリに保存したfixture名。</param>
    /// <returns>生成されたC#のビルドと実行が完了するタスク。</returns>
    [TestMethod]
    [DataRow("add")]
    [DataRow("move")]
    [DataRow("times")]
    [DataRow("condition")]
    [DataRow("table")]
    public async Task LicensedFixtureMatchesIndependentExpected(string name)
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var fixture = Path.Combine(Workspace.Root, "tests", "fixtures", "third-party", "proleap", name);
        var source = Path.Combine(fixture, "input.cbl");
        var provenance = Workspace.Read(Path.Combine(fixture, "PROVENANCE.json"));
        Assert.AreEqual(provenance.S("modifiedSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        Assert.AreEqual(provenance.S("originalSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture, "original.cbl.txt")))));
        var config = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(config, "{\"sourceFormat\":\"free\"}");
        var output = Path.Combine(directory.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, source, "--config", config, "-o", output], directory.Path);
        var actual = Path.Combine(directory.Path, "actual.json");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "-o", actual], directory.Path);
        var expected = Workspace.Read(Path.Combine(fixture, "expected.json"));
        Assert.IsTrue(JsonNode.DeepEquals(expected["display"], Workspace.Read(actual)["display"]), File.ReadAllText(actual));
        if (name == "add")
        {
            Directory.Delete(Path.Combine(output, "reports"), true);
            var standalone = await ProcessRunner.Run("dotnet", [Path.Combine(output, "bin", "Debug", "net10.0", "CobolCS.Generated.dll"), "ADDSTMT"], directory.Path);
            Assert.IsTrue(JsonNode.DeepEquals(expected["display"], JsonNode.Parse(standalone)!["display"]), standalone);
        }
    }

    /// <summary>starter-50の未変更メニューを、終了と不正入力の二経路で検証します。</summary>
    /// <returns>生成されたC#の実行が完了するタスク。</returns>
    [TestMethod]
    public async Task StarterBankMenuMatchesIndependentBranches()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var fixture = Path.Combine(Workspace.Root, "tests", "fixtures", "third-party", "core-banking", "menu");
        var source = Path.Combine(fixture, "input.cbl");
        var provenance = Workspace.Read(Path.Combine(fixture, "PROVENANCE.json"));
        Assert.AreEqual(provenance.S("originalSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        var output = Path.Combine(directory.Path, "generated");
        var config = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(config, "{\"sourceFormat\":\"fixed\"}");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, source, "--config", config, "-o", output], directory.Path);
        foreach (var scenario in Workspace.Read(Path.Combine(fixture, "expected.json"))["scenarios"]!.AsArray())
        {
            var input = Path.Combine(directory.Path, "input.json");
            File.WriteAllText(input, new JsonObject { ["fields"] = new JsonObject { ["WS-CHOICE"] = " " }, ["inputs"] = scenario!["inputs"]!.DeepClone() }.ToJsonString());
            var actual = Path.Combine(directory.Path, "actual.json");
            await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "--fixture", input, "-o", actual], directory.Path);
            Assert.IsTrue(JsonNode.DeepEquals(scenario["display"], Workspace.Read(actual)["display"]), File.ReadAllText(actual));
        }
    }

    /// <summary>外部AT_CHECKと区別した独立の数値境界・日本語BMP期待値を検証します。</summary>
    /// <param name="id">原文を含まないレビュー台帳の合成ケースID。</param>
    /// <returns>生成されたC#のビルドと実行が完了するタスク。</returns>
    [TestMethod]
    [DataRow("independent-signed-32-boundary")]
    [DataRow("independent-japanese-bmp")]
    public async Task AutotestInspiredPortableCaseMatchesIndependentExpected(string id)
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var review = Workspace.Read(Path.Combine(Workspace.Root, "tests", "deep-corpus", "autotest-review.json"));
        var item = review["syntheticCases"]!.AsArray().Single(c => c!.S("id") == id)!;
        var source = Path.Combine(directory.Path, "input.cbl");
        File.WriteAllText(source, item.S("source"));
        var config = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(config, new JsonObject { ["sourceFormat"] = item.S("sourceFormat") }.ToJsonString());
        var output = Path.Combine(directory.Path, "generated");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, source, "--config", config, "-o", output], directory.Path);
        var actual = Path.Combine(directory.Path, "actual.json");
        await ProcessRunner.Run("dotnet", [.. Workspace.Cli, "run", output, "-o", actual], directory.Path);
        Assert.IsTrue(JsonNode.DeepEquals(item["expectedDisplay"], Workspace.Read(actual)["display"]), File.ReadAllText(actual));
    }
}
