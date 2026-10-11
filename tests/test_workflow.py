"""Execute the generated public API, review gate, regeneration and adoption."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
CLI = ["dotnet", str(ROOT / "src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll")]


def run(*args, code=0):
    result = subprocess.run([str(a) for a in args], text=True, capture_output=True, timeout=300)
    if result.returncode != code:
        raise AssertionError(f"{args}: expected {code}, got {result.returncode}\n{result.stdout}\n{result.stderr}")
    return result


class Workflow(unittest.TestCase):
    def test_public_api_and_adoption(self):
        with tempfile.TemporaryDirectory(prefix="cobolcs-workflow-") as temp:
            base = pathlib.Path(temp)
            output = base / "payment"
            source = ROOT / "examples/WORKFLOW/PAYMENT.cbl"
            run(*CLI, "translate", source, "--out", output)
            tests = output / "Tests/CobolCS.Generated.Tests.csproj"
            run("dotnet", "test", tests, code=1)  # Initial case is unreviewed.
            run(*CLI, "adopt", output, "--out", base / "unreviewed", code=1)
            run("python3", ROOT / "tools/prepare_workflow_demo.py", "--out", output)
            mapping = json.loads((output / "reports/source-map.json").read_text())
            name = next(m["executorType"] for m in mapping if "executorType" in m).removesuffix("Executor")
            net = next(m["generatedName"].split(".")[-1] for m in mapping if m.get("originalName") == "NET")
            method = json.loads((output / "reports/stubs.json").read_text())[0]["method"]
            harness = '''using CobolCS.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Executor = CobolCS.Generated.NAMEExecutor;
using Fake = CobolCS.Generated.NAMEScriptedDependencies;
using Input = CobolCS.Generated.NAMEInput;
using Response = CobolCS.Generated.NAMEMETHODResponse;

namespace CobolCS.Generated.Tests;

/// <summary>実行の分離と明示的な失敗を公開境界から検証します。</summary>
[TestClass]
public sealed class ExecutionBoundaryTests
{
    /// <summary>成功・失敗・キャンセル後も同じ facade を再利用できます。</summary>
    [TestMethod]
    public void RepeatedRuns()
    {
        var executor = new Executor();
        var input = new Input { AcceptInputs = new[] { "100.00" } };
        var fake = new Fake();
        fake.ExpectMETHOD(r => r.Argument0 == new Number(100), new Response("00", DependencyOutcome.Normal, CobolValue.Numeric("3.50"), new Number(0)));
        var first = executor.Run(input, fake);
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => executor.Run(input, cancellationToken: canceled.Token));
        var nextFake = new Fake();
        nextFake.ExpectMETHOD(r => r.Argument1 == new Number(0), new Response("00", DependencyOutcome.Normal, new Number(4), new Number(0)));
        var second = executor.Run(input, nextFake);
        Assert.AreEqual(CobolValue.Numeric("96.50"), first.Snapshot.NET);
        Assert.AreEqual(new Number(96), second.Snapshot.NET);
        CollectionAssert.AreEqual(new[] { "09650" }, first.Display.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)first.Display)[0] = "MUTATED");
        Assert.ThrowsExactly<StepLimitException>(() => executor.Run(input, options: new ExecutionOptions { MaxSteps = 1 }));
        Assert.ThrowsExactly<ArgumentException>(() => executor.Run(input, options: new ExecutionOptions { MaxSteps = 0 }));
    }

    /// <summary>探索が fake の失敗を迂回せず、既定値へ漏れないことを確認します。</summary>
    [TestMethod]
    public void ExplicitExploration()
    {
        var executor = new Executor();
        var input = new Input { AcceptInputs = new[] { "100.00" } };
        var options = new ExecutionOptions { CallStubMode = CallStubMode.NoOp };
        var result = executor.Run(input, options: options);
        Assert.IsTrue(result.HasNoOpCalls);
        Assert.AreEqual("NOOP", result.NoOpCalls.Single().StatusCode);
        Assert.AreEqual(new Number(100), result.Snapshot.NET);
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, new Fake(), options));
        var wrong = new Fake();
        wrong.ExpectMETHOD(_ => false, new Response("00", DependencyOutcome.Normal));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, wrong, options));
        var extra = new Fake();
        extra.ExpectMETHOD(_ => true, new Response("00", DependencyOutcome.Normal, Returning: new Number(0)));
        extra.ExpectMETHOD(_ => true, new Response("00", DependencyOutcome.Normal));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, extra));
    }

    /// <summary>外部応答直後のキャンセルと不正な数値を拒否します。</summary>
    [TestMethod]
    public void ResponseValidation()
    {
        var executor = new Executor();
        var input = new Input { AcceptInputs = new[] { "100.00" } };
        using var canceled = new CancellationTokenSource();
        var fake = new Fake();
        fake.ExpectMETHOD(
            _ =>
            {
                canceled.Cancel();
                return true;
            },
            new Response("00", DependencyOutcome.Normal));
        Assert.ThrowsExactly<OperationCanceledException>(() => executor.Run(input, fake, cancellationToken: canceled.Token));
        var invalid = new Fake();
        invalid.ExpectMETHOD(_ => true, new Response("00", DependencyOutcome.Normal, default(Number), new Number(0)));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(input, invalid));
    }
}
'''.replace('NAME', name).replace('METHOD', method).replace('Snapshot.NET', 'Snapshot.' + net)
            (output / "Tests/Cases/ExecutionBoundaryTests.cs").write_text(harness)
            run("dotnet", "test", tests, "--logger", "trx")
            discovered = run("dotnet", "test", tests, "--no-restore", "--list-tests").stdout
            self.assertIn("RepeatedRuns", discovered)
            fixture = base / "input.json"
            fixture.write_text('{"inputs":["100.00"]}')
            legacy = json.loads(run("dotnet", "run", "--project", output, "--no-build", "--", "PAYMENT", fixture, "--call-stubs", "noop").stdout)
            scenario = json.loads(run("dotnet", "run", "--project", output, "--no-build", "--", "PAYMENT", "--scenario", "noop-explore", "--call-stubs", "noop").stdout)
            self.assertEqual(legacy, scenario)
            wrapped = json.loads(run(*CLI, "run", output, "--program", "PAYMENT", "--scenario", "noop-explore", "--call-stubs", "noop").stdout)
            self.assertEqual(wrapped, legacy)
            judgments = json.loads((output / "manifest.json").read_text())["judgments"]
            self.assertEqual(judgments["executionKind"], "call-no-op-exploration")
            self.assertTrue(judgments["hasNoOpCalls"])

            self.assertEqual(legacy["display"], ["10000"])
            self.assertEqual(legacy["metadata"]["noOpCalls"][0]["Target"], "FEE-SERVICE")
            self.assertEqual(len(legacy["metadata"]["noOpCalls"][0]["Arguments"]), 2)
            self.assertEqual(legacy["metadata"]["noOpCalls"][0]["Arguments"][0]["Numeric"], {"numerator": "100", "denominator": "1"})
            original = {str(p.relative_to(output)): p.read_bytes() for p in output.rglob("*") if p.is_file() and ("UserCode" in p.parts or "Cases" in p.parts or "Baselines" in p.parts or p.name == "review.json")}
            run(*CLI, "translate", source, "--out", output)
            for path, content in original.items():
                self.assertEqual(content, (output / path).read_bytes())
            adopted = base / "adopted"
            run(*CLI, "adopt", output, "--out", adopted)
            self.assertFalse((adopted / "manifest.json").exists())
            self.assertFalse((adopted / "reports").exists())
            self.assertTrue((adopted / "provenance/cobolcs-origin.json").exists())
            self.assertFalse(any(p.name in {"bin", "obj", "TestResults"} for p in adopted.rglob("*")))
            run(*CLI, "translate", source, "--out", adopted, code=2)
            run(*CLI, "adopt", output, "--out", output / "nested", code=2)
            run(*CLI, "adopt", output, "--out", adopted, code=2)
            program = next((adopted / "Programs").glob("*Program.g.cs"))
            text = program.read_text()
            # Extract the generated calculation into a handwritten service, preserving API/tests.
            import re
            match = re.search(r'CobolValue.Numeric\(\(CobolValue.Numeric\((state\.\w+)\) - CobolValue.Numeric\((state\.\w+)\)\)\)', text)
            self.assertIsNotNone(match)
            modified = text[:match.start()] + 'PaymentService.Net(' + match.group(1) + ', ' + match.group(2) + ')' + text[match.end():]
            program.write_text(modified)
            service = adopted / "UserCode/PaymentService.cs"
            service.write_text('''using CobolCS.Runtime;

namespace CobolCS.Generated;

/// <summary>採用後に抽出した通常の手書き計算です。</summary>
internal static class PaymentService
{
    /// <summary>手数料を差し引きます。</summary>
    /// <param name="gross">受入金額。</param>
    /// <param name="fee">明示された手数料。</param>
    /// <returns>差引額。格納側で PIC 検査します。</returns>
    internal static Number Net(Number gross, Number fee) => gross - fee;
}
''')
            shutil.rmtree(output)  # No generated source directory or reports remain.
            run("dotnet", "test", adopted / "Tests/CobolCS.Generated.Tests.csproj")
            service.write_text(service.read_text().replace('gross - fee', 'gross + fee'))
            run("dotnet", "test", adopted / "Tests/CobolCS.Generated.Tests.csproj", "--no-restore", code=1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
