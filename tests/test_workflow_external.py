"""Public fake contracts: FILE status/EOF, SQL NULL/no-data/error, typed setup."""
import json
import pathlib
import tempfile
import unittest
from test_workflow import ROOT, CLI, run


class ExternalWorkflow(unittest.TestCase):
    def test_file_and_sql(self):
        with tempfile.TemporaryDirectory(prefix="cobolcs-contracts-") as temp:
            base = pathlib.Path(temp)
            for program in ['FILECASE', 'SQLCASE']:
                output = base / program
                run(*CLI, 'translate', ROOT / f'examples/WORKFLOW/{program}.cbl', '--out', output)
                mapping = json.loads((output / 'reports/source-map.json').read_text())
                name = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
                fields = {m['originalName'].split(' OF ')[0]: m['generatedName'].split('.')[-1] for m in mapping if 'symbolId' in m}
                stubs = json.loads((output / 'reports/stubs.json').read_text())
                if program == 'FILECASE':
                    read = next(s['method'] for s in stubs if s['statement']['operation'] == 'read')
                    write = next(s['method'] for s in stubs if s['statement']['operation'] == 'write')
                    import re
                    contract = (output / f'Contracts/{name}{read}Request.g.cs').read_text()
                    record = re.search(r'Optional<string> (Record\w+)', contract).group(1)
                    body = f'''
        var executor = new {name}Executor();
        var fake = new {name}ScriptedDependencies();
        fake.Expect{read}(_ => true, new {name}{read}Response("00", DependencyOutcome.Normal, "ABC", "00"));
        fake.Expect{write}(r => r.{record} == "ABC", new {name}{write}Response("00", DependencyOutcome.Normal, "00"));
        var result = executor.Run(new(), fake);
        Assert.AreEqual("ABC", result.Snapshot.{fields['TEXT-VALUE']});
        Assert.AreEqual("00", result.Snapshot.{fields['ST']});
        Assert.AreEqual("ABC", fake.Captured{write}.Single().{record});
        var eof = new {name}ScriptedDependencies();
        eof.Expect{read}(_ => true, new {name}{read}Response("10", DependencyOutcome.End, FileStatus: "10"));
        var ended = executor.Run(new() {{ InitialState = new() {{ {fields['TEXT-VALUE']} = "OLD" }} }}, eof);
        Assert.AreEqual("OLD", ended.Snapshot.{fields['TEXT-VALUE']});
        CollectionAssert.AreEqual(new[] {{ "EOF", "10" }}, ended.Display.ToArray());
        var missing = new {name}ScriptedDependencies();
        missing.Expect{read}(_ => true, new {name}{read}Response("00", DependencyOutcome.Normal, FileStatus: "00"));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new(), missing));
        var wrong = new {name}ScriptedDependencies();
        wrong.Expect{read}(_ => true, new {name}{read}Response("00", DependencyOutcome.End, FileStatus: "00"));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new(), wrong));
        var order = new {name}ScriptedDependencies();
        order.Expect{write}(_ => true, new {name}{write}Response("00", DependencyOutcome.Normal, "00"));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new(), order));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new(), options: new ExecutionOptions {{ CallStubMode = CallStubMode.NoOp }}));
'''
                else:
                    method = stubs[0]['method']
                    body = f'''
        var executor = new {name}Executor();
        var fake = new {name}ScriptedDependencies();
        fake.Expect{method}(r => r.Host1 == new Number(1), new {name}{method}Response("0", DependencyOutcome.Normal, new Number(42), new Number(0), new Number(0), "00000"));
        var result = executor.Run(new(), fake);
        Assert.AreEqual(new Number(42), result.Snapshot.{fields['N']});
        Assert.AreEqual(new Number(0), result.Snapshot.{fields['SQLCODE']});
        var noData = new {name}ScriptedDependencies();
        noData.Expect{method}(_ => true, new {name}{method}Response("100", DependencyOutcome.End, SqlCode: new Number(100), SqlState: "02000"));
        var ended = executor.Run(new(), noData);
        Assert.AreEqual(new Number(17), ended.Snapshot.{fields['N']});
        Assert.AreEqual(new Number(100), ended.Snapshot.{fields['SQLCODE']});
        var failed = new {name}ScriptedDependencies();
        failed.Expect{method}(_ => true, new {name}{method}Response("-1", DependencyOutcome.Exception, SqlCode: new Number(-1), SqlState: "HY000"));
        var errorResult = executor.Run(new(), failed);
        Assert.AreEqual(new Number(17), errorResult.Snapshot.{fields['N']});
        Assert.AreEqual(new Number(-1), errorResult.Snapshot.{fields['SQLCODE']});
        var nullValue = new {name}ScriptedDependencies();
        nullValue.Expect{method}(_ => true, new {name}{method}Response("0", DependencyOutcome.Normal, Indicator0: new Number(-1), SqlCode: new Number(0), SqlState: "00000"));
        Assert.AreEqual(new Number(17), executor.Run(new(), nullValue).Snapshot.{fields['N']});
        var wrong = new {name}ScriptedDependencies();
        wrong.Expect{method}(_ => true, new {name}{method}Response("0", DependencyOutcome.End, SqlCode: new Number(100), SqlState: "02000"));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new(), wrong));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new(), options: new ExecutionOptions {{ CallStubMode = CallStubMode.NoOp }}));
'''
                tests = f'''using CobolCS.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.Generated.Tests;

/// <summary>合成応答の状態・値・順序を独立期待値で固定します。</summary>
[TestClass]
public sealed class {name}Tests
{{
    /// <summary>実サーバーを使わず公開契約の成否を確認します。</summary>
    [TestMethod]
    public void StatusAndValues()
    {{{body}    }}
}}
'''
                (output / f'Tests/Cases/{name}Tests.cs').write_text(tests)
                run('dotnet', 'test', output / 'Tests/CobolCS.Generated.Tests.csproj')

    def test_typed_setup_and_snapshot(self):
        with tempfile.TemporaryDirectory(prefix="cobolcs-setup-") as temp:
            base = pathlib.Path(temp)
            source = base / 'layout.cbl'
            source.write_text("""IDENTIFICATION DIVISION.
PROGRAM-ID. LAYOUT.
DATA DIVISION.
WORKING-STORAGE SECTION.
01 ROOT-GROUP.
 05 ROW OCCURS 2.
  10 CELL PIC S9(5)V99 OCCURS 3.
01 ZERO-TEST PIC 9 VALUE 7.
01 TEXT-TEST PIC X(4) JUSTIFIED RIGHT VALUE 'AB'.
01 NATIONAL-TEST PIC N(3).
01 BIG-TEST PIC 9(1000).
PROCEDURE DIVISION.
GOBACK.
""")
            output = base / 'generated'
            run(*CLI, 'translate', source, '--out', output)
            mapping = json.loads((output / 'reports/source-map.json').read_text())
            name = next(m['executorType'] for m in mapping if 'executorType' in m).removesuffix('Executor')
            fields = {m['originalName'].split(' OF ')[0]: m['generatedName'].split('.')[-1] for m in mapping if 'symbolId' in m}
            body = f'''using CobolCS.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CobolCS.Generated.Tests;

/// <summary>精密値・グループ・多次元表と結果の分離を確認します。</summary>
[TestClass]
public sealed class {name}Tests
{{
    /// <summary>全入力は fixture と同じ PIC 境界を通ります。</summary>
    [TestMethod]
    public void TypedStorage()
    {{
        var executor = new {name}Executor();
        var defaults = executor.Run(new());
        Assert.AreEqual(new Number(7), defaults.Snapshot.{fields['ZERO-TEST']});
        var huge = CobolValue.Numeric(new string('9', 1000));
        var entries = new[] {{ new TableEntry<Number>(new[] {{ 2, 3 }}, CobolValue.Numeric("-12.34")) }};
        var result = executor.Run(new()
        {{
            InitialState = new()
            {{
            {fields['ROOT-GROUP']} = new() {{ {fields['CELL']} = entries }},
            {fields['ZERO-TEST']} = new Number(0),
            {fields['TEXT-TEST']} = string.Empty,
            {fields['NATIONAL-TEST']} = "日本",
            {fields['BIG-TEST']} = huge,
            }},
        }});
        Assert.AreEqual(new Number(0), result.Snapshot.{fields['ZERO-TEST']});
        Assert.AreEqual("    ", result.Snapshot.{fields['TEXT-TEST']});
        Assert.AreEqual("日本 ", result.Snapshot.{fields['NATIONAL-TEST']});
        Assert.AreEqual(huge, result.Snapshot.{fields['BIG-TEST']});
        Assert.AreEqual(CobolValue.Numeric("-12.34"), result.Snapshot.{fields['ROOT-GROUP']}.{fields['CELL']}[5]);
        entries[0] = new(new[] {{ 1, 1 }}, new Number(99));
        Assert.AreEqual(CobolValue.Numeric("-12.34"), result.Snapshot.{fields['CELL']}[5]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<Number>)result.Snapshot.{fields['CELL']})[5] = new Number(99));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new() {{ InitialState = new() {{ {fields['ZERO-TEST']} = default(Number) }} }}));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new() {{ InitialState = new() {{ {fields['ZERO-TEST']} = new Number(10) }} }}));
        foreach (var indices in new[] {{ new[] {{ 0, 1 }}, new[] {{ 3, 1 }}, new[] {{ 1 }} }})
        {{
            Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new() {{ InitialState = new() {{ {fields['CELL']} = new[] {{ new TableEntry<Number>(indices, new Number(1)) }} }} }}));
        }}

        var duplicate = new TableEntry<Number>(new[] {{ 1, 1 }}, new Number(1));
        Assert.ThrowsExactly<ExecutionException>(() => executor.Run(new() {{ InitialState = new() {{ {fields['CELL']} = new[] {{ duplicate, duplicate }} }} }}));
    }}
}}
'''
            (output / f'Tests/Cases/{name}Tests.cs').write_text(body)
            run('dotnet', 'test', output / 'Tests/CobolCS.Generated.Tests.csproj')


if __name__ == '__main__':
    unittest.main(verbosity=2)
