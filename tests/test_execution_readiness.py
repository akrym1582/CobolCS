"""Independent synthetic expectations at the generated public execution boundary."""
import hashlib
import json
import pathlib
import re
import tempfile
import unittest
from test_workflow import ROOT, CLI, run


def generate(base, program):
    output = base / program
    run(*CLI, 'translate', ROOT / f'examples/WORKFLOW/{program}.cbl', '--out', output)
    mapping = json.loads((output / 'reports/source-map.json').read_text())
    name = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
    fields = {m['originalName'].split(' OF ')[0]: m['generatedName'].split('.')[-1] for m in mapping if 'symbolId' in m}
    symbols = json.loads((output / 'reports/ir.json').read_text())['programs'][0]['symbols']
    return output, name, fields, symbols


def tests(output, name, body):
    path = output / f'Tests/Cases/{name}Tests.cs'
    path.write_text(f'''using CobolCS.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.Generated.Tests;

/// <summary>独立合成期待値を公開境界で検証します。</summary>
[TestClass]
public sealed class {name}Tests
{{
    /// <summary>正常・境界・異常と値の分離を確認します。</summary>
    [TestMethod]
    public void {name}_Sample()
    {{
{body.strip(chr(10))}
    }}
}}
''')
    return path


class Readiness(unittest.TestCase):
    def test_arguments_adopt_extract_and_mutate(self):
        with tempfile.TemporaryDirectory(prefix='readiness-arguments-') as tmp:
            base = pathlib.Path(tmp)
            out, n, f, syms = generate(base, 'ARGCALC')
            group = n + 'Group' + next(s['id'] for s in syms if s['name'] == 'REQUEST-DATA') + 'Snapshot'
            project = out / 'Tests/CobolCS.Generated.Tests.csproj'
            run('dotnet', 'build', out / 'CobolCS.Generated.sln', '-m:1')
            failed = run('dotnet', 'test', project, code=1)
            self.assertIn('Unfilled preparation item', failed.stdout)
            run('python3', ROOT/'tools/prepare_readiness_demo.py', '--out', out)
            scenario = out / f'UserCode/Scenarios/{n}Scenarios.cs'
            case = out / f'Tests/Cases/{n}Tests.cs'
            baseline = out / f'Tests/Baselines/{n}.json'
            paths = [case, baseline, scenario, out / f'UserCode/Scenarios/{n}ResponseExamples.cs']
            run('dotnet', 'test', project)
            before = {p.relative_to(out): p.read_bytes() for p in paths}
            # Deletions and edits are preserved, including a new response example file.
            deleted = out / f'UserCode/Scenarios/{n}ResponseExamples.cs'
            deleted.unlink()
            run(*CLI, 'translate', ROOT / 'examples/WORKFLOW/ARGCALC.cbl', '--out', out)
            self.assertFalse(deleted.exists())
            for path, data in before.items():
                if path != deleted.relative_to(out):
                    self.assertEqual(data, (out/path).read_bytes())
            # Restore explicitly as a user, then detect changes to scenario-only inputs.
            deleted.write_bytes(before[deleted.relative_to(out)])
            original = scenario.read_text()
            scenario.write_text(original.replace('"A", "B"', '"C", "D"'))
            self.assertIn('Review hash mismatch', run('dotnet', 'test', project, code=1).stdout)
            scenario.write_text(original)
            adopted = base / 'standalone'
            run(*CLI, 'adopt', out, '--out', adopted)
            # Remove access to original generated output and run from standalone C#.
            out.rename(base / 'hidden-generation')
            run('dotnet', 'build', adopted / 'CobolCS.Generated.sln', '-m:1')
            run('dotnet', 'test', adopted / 'Tests/CobolCS.Generated.Tests.csproj')
            run('dotnet', 'run', '--project', adopted, '--', 'ARGCALC', '--scenario', 'sample')
            program = next((adopted / 'Programs').glob('*Program.g.cs'))
            source = program.read_text()
            # Extract only the multiplication, leaving the same public execution boundary.
            pattern = r'\(CobolValue.Numeric\((state\.[^)]+)\) \* CobolValue.Numeric\((state\.[^)]+)\)\)'
            changed, count = re.subn(pattern, r'Calculation.Multiply(\1, \2)', source)
            self.assertEqual(1, count)
            service = adopted / 'UserCode/Calculation.cs'
            service.write_text('''using CobolCS.Runtime;

namespace CobolCS.Generated;

/// <summary>抽出した合成計算。</summary>
internal static class Calculation
{
    /// <summary>精密乗算。</summary>
    /// <param name="amount">金額。</param>
    /// <param name="rate">料率。</param>
    /// <returns>計算値。</returns>
    internal static Number Multiply(Number amount, Number rate) => amount * rate;
}
''')
            program.write_text(changed)
            run('dotnet', 'test', adopted / 'Tests/CobolCS.Generated.Tests.csproj')
            service.write_text(service.read_text().replace('amount * rate', 'amount + rate'))
            run('dotnet', 'test', adopted / 'Tests/CobolCS.Generated.Tests.csproj', code=1)
            service.write_text(service.read_text().replace('amount + rate', 'amount * rate'))

    def test_group_call(self):
        with tempfile.TemporaryDirectory(prefix='readiness-group-') as tmp:
            out, n, f, syms = generate(pathlib.Path(tmp), 'GROUPCALL')
            group = n + 'Group' + next(s['id'] for s in syms if s['name'] == 'PAYLOAD') + 'Snapshot'
            method = json.loads((out / 'reports/stubs.json').read_text())[0]['method']
            tests(out, n, f'''
        var fake = new {n}ScriptedDependencies();
        var payload = new {group}({f['AMOUNT']}: new Number(25), {f['ITEM']}: new[] {{ "AB", "CD" }});
        fake.Expect{method}(r => r.Target == "SERVICE" && r.Argument0.{f['AMOUNT']} == new Number(12) && r.Argument0.{f['ITEM']}.SequenceEqual(new[] {{ "  ", "  " }}), new {n}{method}Response("00", DependencyOutcome.Normal, Argument0: payload, Returning: new Number(0)));
        var result = new {n}Executor().Run(new(), fake);
        Assert.AreEqual(new Number(25), result.Snapshot.{f['AMOUNT']});
        CollectionAssert.AreEqual(new[] {{ "UPDATED" }}, result.Display.ToArray());
        Assert.AreEqual(new Number(12), fake.Captured{method}.Single().Argument0.{f['AMOUNT']});
        var trace = ({n}{method}RequestTrace)result.DependencyTrace.Single();
        Assert.IsTrue(trace.Request.Argument0.ContentEquals(fake.Captured{method}.Single().Argument0));
        Assert.AreNotSame(trace.Request.Argument0.{f['ITEM']}, fake.Captured{method}.Single().Argument0.{f['ITEM']});
        var noUpdate = new {n}ScriptedDependencies();
        noUpdate.Expect{method}(r => r.Target == "SERVICE", new {n}{method}Response("00", DependencyOutcome.Normal, Returning: new Number(0)));
        CollectionAssert.AreEqual(new[] {{ "UNCHANGED" }}, new {n}Executor().Run(new(), noUpdate).Display.ToArray());
        var bad = new {n}ScriptedDependencies();
        bad.Expect{method}(r => r.Argument0.{f['AMOUNT']} == new Number(12), new {n}{method}Response("00", DependencyOutcome.Normal, Argument0: payload, Returning: new Number(99)));
        Assert.ThrowsExactly<ExecutionException>(() => new {n}Executor().Run(new(), bad));
        Assert.AreEqual(new Number(12), bad.Captured{method}.Single().Argument0.{f['AMOUNT']});
        Assert.ThrowsExactly<ArgumentException>(() => new {group}({f['AMOUNT']}: new Number(1), {f['ITEM']}: new[] {{ "TOOLONG", "OK" }}));
''')
            run('dotnet', 'test', out / 'Tests/CobolCS.Generated.Tests.csproj')
            # An edited stub cannot silently reconnect to a different group layout.
            stub = next(p for p in (out/'Stubs').rglob('Dependency_*.cs'))
            stub.write_text(stub.read_text()+'\n// user implementation under review\n')
            source = pathlib.Path(tmp)/'changed.cbl'
            source.write_text((ROOT/'examples/WORKFLOW/GROUPCALL.cbl').read_text().replace('PIC 9(3)', 'PIC 9(4)'))
            run(*CLI, 'translate', source, '--out', out, code=2)

    def test_initialize_and_string(self):
        with tempfile.TemporaryDirectory(prefix='readiness-text-') as tmp:
            out, n, f, _ = generate(pathlib.Path(tmp), 'TEXTCASE')
            self.assertEqual([], json.loads((out/'reports/stubs.json').read_text()))
            tests(out, n, f'''
        var executor = new {n}Executor();
        foreach (var c in new[] {{ (2, "aXYZe", 5, "OK"), (3, "abXYZ", 6, "OK"), (4, "abcXY", 6, "OVERFLOW"), (0, "abcde", 0, "OVERFLOW"), (6, "abcde", 6, "OVERFLOW"), (-1, "abcde", -1, "OVERFLOW") }})
        {{
            var result = executor.Run(new() {{ InitialState = new() {{ {f['PTR']} = new Number(c.Item1) }} }});
            Assert.AreEqual(c.Item2, result.Snapshot.{f['DEST']});
            Assert.AreEqual(new Number(c.Item3), result.Snapshot.{f['PTR']});
            CollectionAssert.AreEqual(new[] {{ c.Item4, c.Item2 }}, result.Display.ToArray());
            Assert.AreEqual(new Number(0), result.Snapshot.{f['N']});
            CollectionAssert.AreEqual(new[] {{ "  ", "  " }}, result.Snapshot.{f['TXT']}.ToArray());
        }}

        Assert.AreEqual(("abcde", new Number(1), false), SourceSupport.StringSize("abcde", new Number(1), new[] {{ string.Empty }}));
        Assert.AreEqual(("XYZde", new Number(4), false), SourceSupport.StringSize("abcde", new Number(1), new[] {{ "XY", "Z" }}));
''')
            run('dotnet', 'test', out/'Tests/CobolCS.Generated.Tests.csproj')

    def test_scalar_arguments_and_input_only_calls(self):
        with tempfile.TemporaryDirectory(prefix='readiness-scalar-') as tmp:
            base = pathlib.Path(tmp)
            source = base/'SCALAR.cbl'
            source.write_text("""       IDENTIFICATION DIVISION.
       PROGRAM-ID. SCALAR.
       DATA DIVISION.
       LINKAGE SECTION.
       01 ARG PIC 9(3).
       01 ITEMS PIC X(2) OCCURS 2 TIMES.
       PROCEDURE DIVISION USING BY VALUE ARG BY REFERENCE ITEMS.
           ADD 1 TO ARG
           MOVE "OK" TO ITEMS(2)
           GOBACK.
""")
            out = base/'out'
            run(*CLI, 'translate', source, '--out', out, '--mode', 'strict')
            mapping = json.loads((out/'reports/source-map.json').read_text())
            n = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
            f = {m['originalName']: m['generatedName'].split('.')[-1] for m in mapping if 'symbolId' in m}
            tests(out, n, f'''        var values = new[] {{ "A", "B" }};
        var input = new {n}Input {{ Arguments = new() {{ {f['ARG']} = new Number(0), {f['ITEMS']} = new Optional<IReadOnlyList<string>>(values) }} }};
        var result = new {n}Executor().Run(input);
        Assert.IsNotNull(result.Arguments);
        Assert.AreEqual(new Number(1), result.Arguments.{f['ARG']});
        CollectionAssert.AreEqual(new[] {{ "A ", "OK" }}, result.Arguments.{f['ITEMS']}.ToArray());
        Assert.AreEqual("B", values[1]);
        Assert.ThrowsExactly<ExecutionException>(() => new {n}Executor().Run(new() {{ Arguments = new() {{ {f['ARG']} = new Number(0) }} }}));''')
            run('dotnet', 'test', out/'Tests/CobolCS.Generated.Tests.csproj')

    def test_string_flow_and_initialize_selection(self):
        programs = {
            'FLOWTEXT': ("01 T PIC X(3) VALUE 'xyz'. 01 P PIC 9 VALUE 1.",
                "MAIN. STRING 'AB' 'CD' DELIMITED BY SIZE INTO T POINTER P ON OVERFLOW GO TO FAILED NOT ON OVERFLOW GO TO GOOD END-STRING DISPLAY 'LEAK' GOBACK. FAILED. DISPLAY T P GOBACK. GOOD. DISPLAY 'BAD' GOBACK.", ['ABC4']),
            'EMPTYTEXT': ("01 T PIC X(5) VALUE 'abcde'.",
                'STRING "" "Z" DELIMITED BY SIZE INTO T END-STRING DISPLAY T GOBACK.', ['Zbcde']),
            'NOPTR': ("01 T PIC X(3) VALUE 'xyz'.",
                'STRING "ABCDE" DELIMITED BY SIZE INTO T END-STRING DISPLAY T GOBACK.', ['ABC']),
            'INITSELECT': ("01 G. 05 ROWS OCCURS 2 TIMES. 10 N PIC 99 VALUE 17. 88 SEVENTEEN VALUE 17. 10 T PIC X(2) VALUE 'AB'. 10 FILLER PIC X VALUE 'Z'. 01 NAT PIC N(2) VALUE 'あい'.",
                "INITIALIZE ROWS(2) NAT DISPLAY N(1) N(2) T(1) T(2) NAT GOBACK.", ['1700AB    ']),
            'UNREACHED': ("01 N PIC 9 VALUE 1. 01 T PIC X(2).",
                'IF N = 0 STRING "A" DELIMITED BY "Z" INTO T ON OVERFLOW DISPLAY "LEAK" END-STRING END-IF DISPLAY "OK" GOBACK.', ['OK']),
        }
        with tempfile.TemporaryDirectory(prefix='readiness-boundaries-') as tmp:
            base = pathlib.Path(tmp)
            for program, (decl, body, expected) in programs.items():
                with self.subTest(program=program):
                    source = base/(program+'.cbl')
                    source.write_text(f'IDENTIFICATION DIVISION. PROGRAM-ID. {program}. DATA DIVISION. WORKING-STORAGE SECTION. {decl} PROCEDURE DIVISION. {body}')
                    out = base/program
                    run(*CLI, 'translate', source, '--out', out)
                    mapping = json.loads((out/'reports/source-map.json').read_text())
                    n = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
                    values = ', '.join(json.dumps(v) for v in expected)
                    check = f"        var result = new {n}Executor().Run(new());\n        CollectionAssert.AreEqual(new[] {{ {values} }}, result.Display.ToArray());"
                    if program == 'INITSELECT':
                        fields = {m['originalName'].split(' OF ')[0]:m['generatedName'].split('.')[-1] for m in mapping if 'symbolId' in m}
                        check += f"\n        CollectionAssert.AreEqual(new[] {{ \"Z\", \"Z\" }}, result.Snapshot.{fields['G']}.{fields['FILLER']}.ToArray());"
                    tests(out, n, check)
                    run('dotnet', 'test', out/'Tests/CobolCS.Generated.Tests.csproj')

    def test_group_content_is_input_only(self):
        with tempfile.TemporaryDirectory(prefix='readiness-content-') as tmp:
            base = pathlib.Path(tmp)
            source = base/'GROUPCALL.cbl'
            source.write_text((ROOT/'examples/WORKFLOW/GROUPCALL.cbl').read_text().replace('BY REFERENCE PAYLOAD', 'BY CONTENT PAYLOAD'))
            out = base/'out'
            run(*CLI, 'translate', source, '--out', out)
            mapping = json.loads((out/'reports/source-map.json').read_text())
            n = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
            method = json.loads((out/'reports/stubs.json').read_text())[0]['method']
            tests(out, n, f'''        var fake = new {n}ScriptedDependencies();
        fake.Expect{method}(r => r.Target == "SERVICE" && r.Source.PassingModes.Single() == "content", new {n}{method}Response("00", DependencyOutcome.Normal, Returning: new Number(0)));
        var result = new {n}Executor().Run(new(), fake);
        CollectionAssert.AreEqual(new[] {{ "UNCHANGED" }}, result.Display.ToArray());
        Assert.IsNull(typeof({n}{method}Response).GetProperty("Argument0"));''')
            run('dotnet', 'test', out/'Tests/CobolCS.Generated.Tests.csproj')

    def test_unsupported_regions_and_aliases(self):
        with tempfile.TemporaryDirectory(prefix='readiness-unsupported-') as tmp:
            base = pathlib.Path(tmp)
            original = (ROOT/'examples/WORKFLOW/GROUPCALL.cbl').read_text()
            variants = {
                'same': original.replace('RETURNING RESULT-CODE', 'RETURNING AMOUNT'),
                'child': original.replace('BY REFERENCE PAYLOAD', 'BY REFERENCE PAYLOAD AMOUNT'),
                'redefines': original.replace('01 RESULT-CODE', '01 ALIAS-DATA REDEFINES PAYLOAD PIC X(7).\n       01 RESULT-CODE').replace('BY REFERENCE PAYLOAD', 'BY REFERENCE ALIAS-DATA'),
                'delimiter': (ROOT/'examples/WORKFLOW/TEXTCASE.cbl').read_text().replace('DELIMITED BY SIZE', 'DELIMITED BY "X"'),
                'replacing': (ROOT/'examples/WORKFLOW/TEXTCASE.cbl').read_text().replace('INITIALIZE DATA-GROUP', 'INITIALIZE DATA-GROUP REPLACING NUMERIC DATA BY 1'),
                'content-declaration': (ROOT/'examples/WORKFLOW/ARGCALC.cbl').read_text().replace('USING REQUEST-DATA', 'USING BY CONTENT REQUEST-DATA'),
            }
            for key, text in variants.items():
                with self.subTest(key=key):
                    source = base/(key+'.cbl')
                    source.write_text(text)
                    out = base/key
                    run(*CLI, 'translate', source, '--out', out)
                    stubs = json.loads((out/'reports/stubs.json').read_text())
                    self.assertTrue(any(s['capability'] == 'unsupported' for s in stubs))
                    mapping = json.loads((out/'reports/source-map.json').read_text())
                    n = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
                    tests(out, n, f'''        var error = Assert.ThrowsExactly<ExecutionException>(() => new {n}Executor().Run(new()));
        Assert.IsInstanceOfType<NotImplementedException>(error.InnerException);''')
                    run('dotnet', 'test', out/'Tests/CobolCS.Generated.Tests.csproj')

    def test_reports_and_fresh_templates(self):
        with tempfile.TemporaryDirectory(prefix='readiness-reports-') as tmp:
            out = pathlib.Path(tmp)/'all'
            run(*CLI, 'translate', ROOT/'examples/WORKFLOW', '--out', out)
            run('dotnet', 'build', out/'CobolCS.Generated.sln', '-m:1')
            report = json.loads((out/'reports/execution-readiness.json').read_text())
            self.assertEqual(1, report['schemaVersion'])
            categories = {i['classification'] for i in report['items']}
            self.assertTrue({'accept-input','linkage-input','call-response','file-response','sql-response'} <= categories)
            for item in report['items']:
                self.assertTrue((out/item['editFile']).is_file(), item)
                self.assertTrue(item['contractType'])
            run('dotnet', 'test', out/'Tests/CobolCS.Generated.Tests.csproj', code=1)


if __name__ == '__main__':
    unittest.main(verbosity=2)
