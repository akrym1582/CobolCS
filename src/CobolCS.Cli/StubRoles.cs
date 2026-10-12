using System.Text;
using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>名前ではなく構造化された文と子ノードから、探索で実行できる内部補完を限定します。</summary>
internal static class StubRoles
{
    /// <summary>外部効果や未知ブロックを内部処理として実行しない保守的な分類です。</summary>
    /// <param name="statement">子文を含む元の IR。</param>
    /// <param name="adapter">型付き外部契約が生成できた場合だけ true。</param>
    /// <returns>分類不能なものは unknown、外部効果を含むものは external-unsupported。</returns>
    internal static string Classify(JsonNode statement, bool adapter)
    {
        if (adapter)
        {
            return "external-adapter";
        }

        var nodes = Validation.Walk(statement).ToArray();
        if (nodes.Any(n => n.S("kind") is "external" or "sort" or "sortIo" || n["dependencyId"] != null || n["fileId"] != null))
        {
            return "external-unsupported";
        }

        if (nodes.Any(n => n.S("kind") == "unknown" || n.S("targetKind") == "paragraph" || (n.S("kind") == "function" && !ExpressionTypes.Functions.ContainsKey(n.S("name")))) || statement.S("kind") is "entry" or "programEntry")
        {
            return "unknown";
        }

        return statement.S("kind") is "inspectOperations" or "unstringTransfer" or "stringTransfer" or "initialize" or "move" or "arithmetic" or "compute" or "set" or "search" or "controlStub" or "if" or "evaluate" or "perform" or "display" or "acceptInput" or "initialValue"
            ? "internal-logic" : "unknown";
    }

    /// <summary>追加の役割メタデータだけで旧スタブの原文・型の束縛を変更しません。</summary>
    /// <param name="stub">管理された契約レポート。</param>
    /// <returns>既存形式とも一致する契約 hash。</returns>
    internal static string Binding(JsonNode stub)
    {
        var copy = stub.DeepClone().AsObject();
        copy.Remove("executionRole");
        copy.Remove("reasonCode");
        return Generator.Hash(Encoding.UTF8.GetBytes(Generator.Json(copy)));
    }
}
