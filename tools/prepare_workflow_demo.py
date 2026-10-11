"""Install independently calculated synthetic PAYMENT scenarios and reviewed tests."""
import argparse
import hashlib
import json
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare(out):
    root = Path(out).resolve()
    manifest = json.loads((root / "manifest.json").read_text())
    if manifest.get("manifestSchemaVersion") != 2 or manifest.get("backend") != "source" or manifest.get("programs") != ["PAYMENT"]:
        raise ValueError("Expected a schema 2 source PAYMENT project")
    expected_source = Path(__file__).resolve().parents[1] / "examples/WORKFLOW/PAYMENT.cbl"
    if list(manifest["inputHashes"].values()) != [sha(expected_source.read_bytes())]:
        raise ValueError("Only the repository's synthetic PAYMENT source is accepted")
    for path, digest in manifest["artifacts"].items():
        candidate = root / path
        if candidate.is_symlink() or root not in candidate.resolve().parents:
            raise ValueError("Unsafe managed path")
        if sha((root / path).read_bytes()) != digest:
            raise ValueError("Managed source changed: " + path)
    mapping = json.loads((root / "reports/source-map.json").read_text())
    api = next(m for m in mapping if m.get("executorType"))
    name = api["executorType"].removesuffix("Executor")
    fields = {m["originalName"]: m["generatedName"].split(".")[-1] for m in mapping if "symbolId" in m}
    if set(fields) != {"GROSS", "FEE", "NET", "RETURN-CODE"}:
        raise ValueError("Unexpected PAYMENT layout")
    stubs = json.loads((root / "reports/stubs.json").read_text())
    if len(stubs) != 1 or stubs[0]["dependency"]["kind"] != "call":
        raise ValueError("Expected one typed CALL")
    method = stubs[0]["method"]
    case = name + "_HappyPath"
    scenario = f'''using CobolCS.Runtime;
using Executor = CobolCS.Generated.{name}Executor;
using Input = CobolCS.Generated.{name}Input;
using Result = CobolCS.Generated.{name}Result;

namespace CobolCS.Generated;

/// <summary>合成 PAYMENT。100.00 - 3.50 = 96.50 は手計算の期待値です。</summary>
public static class {name}Scenarios
{{
    /// <summary>生成登録の sample も同じ合成入力を使用します。</summary>
    /// <param name="options">今回だけの制限。</param>
    /// <returns>合成シナリオの観測結果。</returns>
    public static Result Sample(ExecutionOptions options) => HappyPath(options);

    /// <summary>手数料応答を毎回新しく用意します。実接続はしません。</summary>
    /// <param name="options">今回だけの実行制限。</param>
    /// <returns>独立期待値と比較する結果。</returns>
    public static Result HappyPath(ExecutionOptions options)
    {{
        var fake = new {name}ScriptedDependencies();
        fake.Expect{method}(
            request => request.Target == "FEE-SERVICE" && request.Argument0 == new Number(100) && request.Argument1 == new Number(0),
            new {name}{method}Response("00", DependencyOutcome.Normal, CobolValue.Numeric("3.50"), new Number(0)));
        return new Executor().Run(new Input {{ AcceptInputs = new[] {{ "100.00" }} }}, fake, options);
    }}

    /// <summary>既定スタブを通す探索例。fake の期待値と同じとは扱いません。</summary>
    /// <param name="options">NoOp は呼出し側が明示してください。</param>
    /// <returns>探索の観測結果。業務 baseline にはできません。</returns>
    public static Result NoOpExplore(ExecutionOptions options)
    {{
        return new Executor().Run(new Input {{ AcceptInputs = new[] {{ "100.00" }} }}, options: options);
    }}
}}
'''
    registration = f'''namespace CobolCS.Generated;

/// <summary>合成デモの明示的なシナリオ一覧です。</summary>
internal static class ScenarioRegistration
{{
    /// <summary>入力と fake はシナリオの呼出しごとに作ります。</summary>
    /// <param name="registry">登録先。</param>
    internal static void Register(ScenarioRegistry registry)
    {{
        registry.Register("PAYMENT", "happy-path", {name}Scenarios.HappyPath);
        registry.Register("PAYMENT", "noop-explore", {name}Scenarios.NoOpExplore);
    }}
}}
'''
    baseline = {
        "baselineKind": "independent-synthetic", "reviewReason": "Hand calculation: 100.00 minus 3.50 equals 96.50; PIC 9(3)V99 displays 09650. No original COBOL capture.",
        "sourceHashes": manifest["inputHashes"], "toolVersion": manifest["toolVersion"],
        "input": "100.00", "fake": {"fee": "3.50", "returning": "0"},
        "expected": {"net": "96.50", "display": "09650", "termination": "Goback", "calls": 1},
    }
    tests = f'''using System.Text.Json.Nodes;
using CobolCS.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.Generated.Tests;

/// <summary>公開 API の金額・表示・終了・外部依頼を独立期待値で固定します。</summary>
[TestClass]
public sealed class {name}Tests
{{
    /// <summary>100.00 - 3.50 = 96.50。実機 COBOL の比較ではありません。</summary>
    [TestMethod]
    public void {case}()
    {{
        var result = {name}Scenarios.HappyPath(new ExecutionOptions());
        Review.Require("PAYMENT", "{case}", result.CallStubMode, result.HasNoOpCalls);
        var baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(Review.Root(), "Tests/Baselines/{name}.json")))!;
        var expected = baseline["expected"]!;
        Assert.AreEqual(CobolValue.Numeric(expected["net"]!.GetValue<string>()), result.Snapshot.{fields['NET']});
        Assert.AreEqual(new Number(100), result.Snapshot.{fields['GROSS']});
        Assert.AreEqual(CobolValue.Numeric("3.50"), result.Snapshot.{fields['FEE']});
        Assert.AreEqual(new Number(0), result.Snapshot.{fields['RETURN-CODE']});
        CollectionAssert.AreEqual(new[] {{ expected["display"]!.GetValue<string>() }}, result.Display.ToArray());
        Assert.AreEqual(expected["termination"]!.GetValue<string>(), result.Termination.ToString());
        Assert.HasCount(expected["calls"]!.GetValue<int>(), result.DependencyTrace);
        var call = ({name}{method}RequestTrace)result.DependencyTrace.Single();
        Assert.AreEqual("FEE-SERVICE", call.Request.Target);
        Assert.AreEqual(new Number(100), call.Request.Argument0);
        Assert.AreEqual(new Number(0), call.Request.Argument1);
        CollectionAssert.AreEqual(new[] {{ "content", "reference" }}, call.Source.PassingModes.ToArray());
        Assert.AreEqual("00", call.StatusCode);
        Assert.IsFalse(result.HasNoOpCalls);
    }}
}}
'''
    files = {f"UserCode/Scenarios/{name}Scenarios.cs": scenario, "UserCode/ScenarioRegistration.cs": registration,
             f"Tests/Cases/{name}Tests.cs": tests, f"Tests/Baselines/{name}.json": json.dumps(baseline, ensure_ascii=False, indent=2) + "\n"}
    review = {"schemaVersion": 1, "cases": [{"programId": "PAYMENT", "caseId": case, "baselineKind": "independent-synthetic", "reviewed": True, "callStubMode": "Throw", "hasNoOpCalls": False,
               "files": {path: sha(content.encode()) for path, content in files.items()}}]}
    files["Tests/review.json"] = json.dumps(review, indent=2) + "\n"
    for path, content in files.items():
        target = root / path
        if target.is_symlink() or root not in target.resolve().parents:
            raise ValueError("Unsafe user path: " + path)
        if not target.exists():
            raise ValueError("Deleted user template must not be recreated: " + path)
        existing = target.read_bytes()
        if existing != content.encode() and sha(existing) != manifest["userTemplateHashes"].get(path):
            raise ValueError("Refusing to overwrite user edits: " + path)
    for path, content in files.items():
        (root / path).write_bytes(content.encode("utf-8"))
    print("Prepared independent-synthetic PAYMENT case; no original COBOL equivalence claim.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True)
    prepare(parser.parse_args().out)
