using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;
using CobolCS.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.IntegrationTests;

/// <summary>評価器のIR読書きも製品と同じ有限な深さ境界を使用します。</summary>
[TestClass]
public sealed class WorkspaceJsonDepthTests
{
    /// <summary>既定64より深いIRを往復し、境界を超えるデータを拒否します。</summary>
    [TestMethod]
    public void ToolReadsAndWritesTheProductDepthBoundary()
    {
        using var folder = new TestDirectory();
        var path = Path.Combine(folder.Path, "deep.json");
        var text = new string('[', 256) + "0" + new string(']', 256);
        var node = J.Parse(text);
        Workspace.Write(path, node);
        Assert.IsTrue(JsonNode.DeepEquals(node, Workspace.Read(path)));
        StringAssert.Contains(Assert.Throws<InvalidOperationException>(() => Workspace.Write(path, new JsonArray(node))).Message, "256");
        File.WriteAllText(path, new string('[', 257) + "0" + new string(']', 257));
        StringAssert.Contains(Assert.Throws<JsonException>(() => Workspace.Read(path)).Message, "256");
    }
}
