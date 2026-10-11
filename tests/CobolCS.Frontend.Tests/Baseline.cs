using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Tests;

/// <summary>固定入力と参照実装のハッシュを確認するテストデータ入口です。</summary>
internal static class Baseline
{
    /// <summary>Gets テスト実行ディレクトリによらないリポジトリルート。</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>Gets レビュー対象の固定マニフェスト。</summary>
    internal static JsonObject Manifest { get; } = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "tests/migration/baseline.json")))!.AsObject();

    /// <summary>Gets 固定された解析入力。</summary>
    internal static IEnumerable<JsonObject> Cases => Manifest["cases"]!.AsArray().Select(x => x!.AsObject());

    /// <summary>テストの独立した実行名として入力 ID を返します。</summary>
    /// <returns>入力 ID の行。</returns>
    internal static IEnumerable<object[]> CaseIds() => Cases.Select(x => new object[] { x["id"]!.GetValue<string>() });

    /// <summary>入力集合と保存した期待値の書換えを検出します。</summary>
    internal static void VerifyInputs()
    {
        VerifyHash("tests/migration/pictures.json", Manifest["pictureSha256"]!.GetValue<string>());
        foreach (var (path, hash) in Manifest["files"]!.AsObject().Where(p => p.Key.StartsWith("examples/", StringComparison.Ordinal) || p.Key.StartsWith("tests/migration/inputs/", StringComparison.Ordinal) || p.Key.StartsWith("tests/public-corpus/", StringComparison.Ordinal)))
        {
            VerifyHash(path, hash!.GetValue<string>());
        }

        foreach (var item in Cases.Where(c => c["expected"] is not null))
        {
            VerifyHash(item["expected"]!.GetValue<string>(), item["expectedSha256"]!.GetValue<string>());
        }
    }

    /// <summary>固定期待 IR を読み込みます。</summary>
    /// <param name="item">入力のマニフェスト。</param>
    /// <returns>期待 IR。標準出力なしなら空文字。</returns>
    internal static string Expected(JsonObject item) => item["expected"] is null ? string.Empty : File.ReadAllText(Path.Combine(Root, item["expected"]!.GetValue<string>()));

    private static void VerifyHash(string path, string expected)
    {
        Assert.AreEqual(expected, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, path)))), "Baseline changed: " + path);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CobolCS.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("CobolCS.sln not found above test assembly");
    }
}
