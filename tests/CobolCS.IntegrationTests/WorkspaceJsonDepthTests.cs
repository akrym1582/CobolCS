using System.Text.Json;
using System.Text.Json.Nodes;
using CobolCS.Runtime;
using CobolCS.Tools;
using Xunit;

namespace CobolCS.IntegrationTests;

/// <summary>評価器のIR読書きも製品と同じ有限な深さ境界を使用します。</summary>
public sealed class WorkspaceJsonDepthTests
{
    /// <summary>既定64より深いIRを往復し、境界を超えるデータを拒否します。</summary>
    [Fact]
    public void ToolReadsAndWritesTheProductDepthBoundary()
    {
        using var folder = new TestDirectory();
        var path = Path.Combine(folder.Path, "deep.json");
        var text = new string('[', 512) + "0" + new string(']', 512);
        var node = J.Parse(text);
        Workspace.Write(path, node);
        Assert.True(JsonNode.DeepEquals(node, Workspace.Read(path)));
        Assert.Contains("512", Assert.ThrowsAny<InvalidOperationException>(() => Workspace.Write(path, new JsonArray(node))).Message);
        File.WriteAllText(path, new string('[', 513) + "0" + new string(']', 513));
        Assert.Contains("512", Assert.ThrowsAny<JsonException>(() => Workspace.Read(path)).Message);
    }
}
