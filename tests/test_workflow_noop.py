"""Exploration diagnostics survive errors and limits; no fabricated completion."""
import json
import pathlib
import tempfile
import unittest
from test_workflow import CLI, run


class Exploration(unittest.TestCase):
    def test_partial_trace_and_default_throw(self):
        with tempfile.TemporaryDirectory(prefix='cobolcs-noop-') as temp:
            base = pathlib.Path(temp)
            for program, body in [
                ('LOOP', 'PERFORM UNTIL N > 0 CALL "WAIT" USING N END-CALL END-PERFORM GOBACK.'),
                ('ERROR', 'CALL "WAIT" USING N END-CALL COMPUTE N = 1 / 0 GOBACK.'),
            ]:
                source = base / (program + '.cbl')
                source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. ' + program + '.\nDATA DIVISION. WORKING-STORAGE SECTION.\n01 N PIC 99 VALUE ZERO.\nPROCEDURE DIVISION.\n' + body)
                output = base / program
                config = base / 'config.json'
                config.write_text('{"maxSteps":20}')
                run(*CLI, 'translate', source, '--config', config, '--out', output)
                failed = run(*CLI, 'run', output, '--program', program, '--call-stubs', 'noop', code=2)
                self.assertIn('generated-call-no-op', failed.stderr)
                self.assertIn('WAIT', failed.stderr)
                self.assertIn('Execution limit' if program == 'LOOP' else 'DivideByZeroException', failed.stderr)
                ordinary = run(*CLI, 'run', output, '--program', program, code=2)
                self.assertIn('NotImplementedException', ordinary.stderr)
                self.assertNotIn('generated-call-no-op', ordinary.stderr)
                # An edited implementation must never be bypassed by NoOp.
                stub = next((output / 'Stubs').rglob('Dependency_*.cs'))
                text = stub.read_text()
                start = text.index('        if (Options.CallStubMode')
                end = text.index('        throw new NotImplementedException', start)
                text = text[:start] + text[end:]
                text = text.replace('throw new NotImplementedException', 'throw new InvalidOperationException')
                stub.write_text(text)
                implemented_failure = run(*CLI, 'run', output, '--program', program, '--call-stubs', 'noop', code=2)
                self.assertIn('InvalidOperationException', implemented_failure.stderr)
                self.assertNotIn('generated-call-no-op', implemented_failure.stderr)

    def test_cancel_after_noop_retains_trace(self):
        with tempfile.TemporaryDirectory(prefix='cobolcs-noop-cancel-') as temp:
            base = pathlib.Path(temp)
            source = base / 'cancel.cbl'
            source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. CANCELCASE. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 99. PROCEDURE DIVISION. CALL "WAIT" USING N END-CALL GOBACK.')
            output = base / 'generated'
            run(*CLI, 'translate', source, '--out', output)
            mapping = json.loads((output / 'reports/source-map.json').read_text())
            name = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
            helper = next((output / 'Stubs').rglob('NoOp_*.cs'))
            helper.write_text(helper.read_text().replace('        return new(', '        CancelProbe.Callback?.Invoke();\n        return new('))
            (output / 'UserCode/CancelProbe.cs').write_text('''namespace CobolCS.Generated;

/// <summary>テストで no-op helper の戻り直前を確定的に観測します。</summary>
internal static class CancelProbe
{
    /// <summary>Gets or sets 戻り直前にキャンセルを通知するテスト操作。</summary>
    internal static Action? Callback { get; set; }
}
''')
            # Test assembly uses the editable scenario as the public entry point.
            scenario = next((output / 'UserCode/Scenarios').glob('*.cs'))
            text = scenario.read_text()
            # Existing sample uses an alias, so append a separate public method before the final class brace.
            insertion = f'''
    /// <summary>no-op 完了直後の協調キャンセルを発生させます。</summary>
    /// <returns>呼出し履歴を保持したキャンセル例外。</returns>
    public static OperationCanceledException CancelAfterCall()
    {{
        using var source = new CancellationTokenSource();
        CancelProbe.Callback = source.Cancel;
        try
        {{
            new {name}Executor().Run(new(), options: new ExecutionOptions {{ CallStubMode = CallStubMode.NoOp }}, cancellationToken: source.Token);
            throw new InvalidOperationException("Expected cancellation");
        }}
        catch (OperationCanceledException error)
        {{
            return error;
        }}
        finally
        {{
            CancelProbe.Callback = null;
        }}
    }}
'''
            text = text[:text.rfind('}')] + insertion + '}\n'
            scenario.write_text(text)
            tests = f'''using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.Generated.Tests;

/// <summary>探索の途中記録をキャンセル診断に保持します。</summary>
[TestClass]
public sealed class {name}Tests
{{
    /// <summary>待機や短い sleep に頼らず応答直後をキャンセルします。</summary>
    [TestMethod]
    public void CancelAfterCall()
    {{
        var error = {name}Scenarios.CancelAfterCall();
        var trace = (IReadOnlyList<DependencyTrace>)error.Data["DependencyTrace"]!;
        Assert.AreEqual("generated-call-no-op", trace.Single().Implementation);
        Assert.AreEqual("WAIT", trace.Single().Target);
        Assert.IsNotNull(error.Data["SourceLocation"]);
    }}
}}
'''
            (output / f'Tests/Cases/{name}Tests.cs').write_text(tests)
            run('dotnet', 'test', output / 'Tests/CobolCS.Generated.Tests.csproj')


if __name__ == '__main__':
    unittest.main(verbosity=2)
