"""Populate only fresh ARGCALC user templates with independently reviewed synthetic cases.

Expected numbers are hand multiplication, not observed generated-C# results.
This opt-in demo never changes managed sources or manifest hashes.
"""
import argparse
import hashlib
import json
import pathlib


def test_source(n, body):
    return (f'''using CobolCS.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.Generated.Tests;

/// <summary>独立合成期待値を公開境界で検証します。</summary>
[TestClass]
public sealed class {n}Tests
{{
    /// <summary>正常・境界・異常と値の分離を確認します。</summary>
    [TestMethod]
    public void {n}_Sample()
    {{
{body.strip(chr(10))}
    }}
}}
''')


def prepare(output):
    out = pathlib.Path(output).resolve()
    manifest = json.loads((out / 'manifest.json').read_text())
    if manifest['programs'] != ['ARGCALC']:
        raise ValueError('Generate ARGCALC alone into a fresh directory')
    mapping = json.loads((out / 'reports/source-map.json').read_text())
    n = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
    f = {m['originalName'].split(' OF ')[0]: m['generatedName'].split('.')[-1] for m in mapping if 'symbolId' in m}
    syms = json.loads((out / 'reports/ir.json').read_text())['programs'][0]['symbols']
    group = n + 'Group' + next(s['id'] for s in syms if s['name'] == 'REQUEST-DATA') + 'Snapshot'
    artifacts = {}
    scenario = out / f'UserCode/Scenarios/{n}Scenarios.cs'
    artifacts[scenario] = (f'''using CobolCS.Runtime;

namespace CobolCS.Generated;

/// <summary>合成金額の入力。</summary>
public static class {n}Scenarios
{{
    /// <summary>独立計算用の正式入力。</summary>
    /// <param name="amount">金額。</param>
    /// <param name="rate">料率。</param>
    /// <returns>正式入力。</returns>
    public static {n}Input Input(string amount, string rate) => new()
    {{
        Arguments = new() {{ {f['REQUEST-DATA']} = new {group}({f['AMOUNT']}: Number.Parse(amount), {f['RATE']}: Number.Parse(rate), {f['TOTAL']}: new Number(0), {f['LABEL-TEXT']}: new[] {{ "A", "B" }}) }},
    }};

    /// <summary>登録済みの単体実行。</summary>
    /// <param name="options">実行制限。</param>
    /// <returns>実行結果。</returns>
    public static {n}Result Sample(ExecutionOptions options) => new {n}Executor().Run(Input("100", "0.25"), options: options);
}}
''')
    body = f'''
        var executor = new {n}Executor();
        foreach (var c in new[] {{ ("100", "0.25", "25"), ("0", "0", "0"), ("99999.99", "9.99", "998999.90") }})
        {{
            var input = {n}Scenarios.Input(c.Item1, c.Item2);
            var result = executor.Run(input);
            Assert.IsNotNull(result.Arguments);
            Assert.AreEqual(Number.Parse(c.Item3), result.Arguments.{f['REQUEST-DATA']}.{f['TOTAL']});
            Assert.AreEqual(new Number(0), input.Arguments.{f['REQUEST-DATA']}.Value.{f['TOTAL']});
            Assert.AreEqual(TerminationKind.Goback, result.Termination);
            Assert.HasCount(0, result.Display);
            Assert.HasCount(0, result.DependencyTrace);
            Review.Require("ARGCALC", "{n}_Sample", result.CallStubMode, result.HasNoOpCalls);
        }}

        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new()));
        Assert.ThrowsExactly<ArgumentException>(() => new {group}());
        Assert.ThrowsExactly<ArgumentException>(() => new {group}({f['AMOUNT']}: new Number(0), {f['RATE']}: new Number(0), {f['TOTAL']}: new Number(0), {f['LABEL-TEXT']}: new[] {{ "A" }}));
        var labels = new[] {{ "A", "B" }};
        var group = new {group}({f['AMOUNT']}: new Number(0), {f['RATE']}: new Number(0), {f['TOTAL']}: new Number(0), {f['LABEL-TEXT']}: labels);
        labels[0] = "XX";
        Assert.AreEqual("A  ", group.{f['LABEL-TEXT']}[0]);
        Assert.IsTrue(group.ContentEquals(group.Copy()));
        Assert.AreNotSame(group.{f['LABEL-TEXT']}, group.Copy().{f['LABEL-TEXT']});
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new() {{ Arguments = {n}Scenarios.Input("0", "0").Arguments, InitialState = new() {{ {f['AMOUNT']} = new Number(0) }} }}));
'''
    case = out / f'Tests/Cases/{n}Tests.cs'
    artifacts[case] = test_source(n, body)
    baseline = out / f'Tests/Baselines/{n}.json'
    manifest = json.loads((out / 'manifest.json').read_text())
    artifacts[baseline] = (json.dumps(dict(baselineKind='independent-synthetic', reviewReason='Hand multiplication with truncation to two fractional digits; expected constants reside in hash-bound test code.', sourceHashes=manifest['inputHashes'], toolVersion=manifest['toolVersion'], expected={'codeFile': case.relative_to(out).as_posix(), 'caseId': n+'_Sample'})))
    paths = [case, baseline, scenario, out / f'UserCode/Scenarios/{n}ResponseExamples.cs']
    review = dict(schemaVersion=1, cases=[dict(programId='ARGCALC', caseId=n+'_Sample', baselineKind='independent-synthetic', reviewed=True, callStubMode='Throw', hasNoOpCalls=False, files={p.relative_to(out).as_posix(): hashlib.sha256(artifacts[p].encode() if p in artifacts else p.read_bytes()).hexdigest() for p in paths})])
    artifacts[out / 'Tests/review.json'] = json.dumps(review)
    for path, content in artifacts.items():
        if path.is_symlink() or any(parent.is_symlink() for parent in path.parents) or not path.is_file():
            raise ValueError('Unsafe or deleted user template: ' + str(path))
        previous = path.read_bytes()
        if previous != content.encode() and hashlib.sha256(previous).hexdigest() != manifest['userTemplateHashes'].get(path.relative_to(out).as_posix()):
            raise ValueError('Refusing to overwrite user edits: ' + str(path))
    for path, content in artifacts.items():
        path.write_bytes(content.encode("utf-8"))
    return paths


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True)
    prepare(parser.parse_args().out)
    print('Prepared independent-synthetic ARGCALC; no original COBOL equivalence claim.')
