"""Offline end-to-end tests: actual CLI, generated C# build, and runtime behavior."""
import json
import os
import pathlib
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
CLI = ["dotnet", str(ROOT / "src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll")]


class Regression(unittest.TestCase):
    def test_remaining18_documentary_native_execution(self):
        source = self.source("DISPLAY 'KEPT' GOBACK.")
        source.write_text(source.read_text().replace("DATA DIVISION.", "AUTHOR. James O'Grady.\n  PROCEDURE and DATA are documentary words.\nDATA DIVISION."))
        self.cli("translate", source, "--out", self.output, "--mode", "strict")
        result = self.cli("run", self.output, "--program", "TEST")
        self.assertEqual(json.loads(result.stdout)["display"], ["KEPT"])
        ir = json.loads((self.output / "reports/ir.json").read_text())
        self.assertIn("O'Grady", ir["programs"][0]["documentation"][0]["source"]["raw"])

    def test_remaining18_lifecycle_fails_at_named_stub(self):
        for attribute in ["IS INITIAL", "RECURSIVE"]:
            self.output = self.base / attribute.replace(" ", "-")
            source = self.source('DISPLAY "KEPT" GOBACK.', program='"TEST" ' + attribute)
            self.cli("translate", source, "--out", self.output, "--mode", "strict", code=1)
            self.cli("translate", source, "--out", self.output)
            stubs = json.loads((self.output / "reports/stubs.json").read_text())
            stub = next(s for s in stubs if "lifecycle" in s["statement"].get("reason", ""))
            result = self.cli("run", self.output, "--program", "TEST", code=2)
            self.assertIn("NotImplementedException", result.stderr)
            self.assertIn(stub["statementId"], result.stderr)
            self.assertIn("source.cbl:2", result.stderr)
            self.assertIn("KEPT", "".join(p.read_text() for p in (self.output / "Programs").glob("*.cs")))

    def test_remaining18_condition_selection_and_unset_stub(self):
        source = self.source('''$IF P64 SET
DISPLAY "64"
$ELSE
DISPLAY "32"
$END
DISPLAY "AFTER" GOBACK.''', '''$IF P64 SET
77 PTR PIC S9(18) COMP-5.
$ELSE
77 PTR PIC S9(9) COMP-5.
$END''')
        hashes = set()
        for value in [True, False, None]:
            output = self.base / str(value)
            config = self.base / "config.json"
            config.write_text(json.dumps({"p64": value}))
            self.cli("translate", source, "--config", config, "--out", output)
            ir = json.loads((output / "reports/ir.json").read_text())
            hashes.add(ir["configHash"])
            self.assertEqual(len(ir["programs"][0]["conditionalDirectives"]), 2)
            if value is None:
                result = self.cli("run", output, "--program", "TEST", code=2)
                stub = next(s for s in json.loads((output / "reports/stubs.json").read_text()) if s["statement"].get("conditional"))
                self.assertIn("NotImplementedException", result.stderr)
                self.assertIn(stub["statementId"], result.stderr)
                self.assertIn("source.cbl:11", result.stderr)
                self.cli("translate", source, "--config", config, "--out", self.base / "strict-unset", "--mode", "strict", code=1)
            else:
                result = self.cli("run", output, "--program", "TEST")
                self.assertEqual(json.loads(result.stdout)["display"], ["64" if value else "32", "AFTER"])
        self.assertEqual(len(hashes), 3)
        config.write_text('{"p64":"true"}')
        self.assertIn("p64 must be", self.cli("analyze", source, "--config", config, "--out", self.base / "invalid", code=2).stderr)

    def test_remaining18_missing_linkage_unknown_build(self):
        source = self.source("DISPLAY KNOWN GOBACK.")
        source.write_text('''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
DATA DIVISION. LINKAGE SECTION.
COPY "missing.cpy" REPLACING ==OLD== BY ==NEW==.
01 KNOWN PIC X(3).
PROCEDURE DIVISION USING MISSING KNOWN. DISPLAY KNOWN GOBACK.''')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        ir = json.loads((self.output / "reports/ir.json").read_text())
        symbol = next(s for s in ir["programs"][0]["symbols"] if s["name"] == "MISSING")
        self.assertEqual(symbol["scope"], "LINKAGE")
        self.assertIn("REPLACING", symbol["missingCopyOrigins"][0]["source"]["raw"])
        result = self.cli("run", self.output, "--program", "TEST", code=2)
        self.assertIn("NotImplementedException", result.stderr)
        self.assertIn("TEST linkage source.cbl:5", result.stderr)
        self.assertIn("UnknownCobolValue", "".join(p.read_text() for p in (self.output / "Models").glob("*.cs")))
        linkage = next(s for s in json.loads((self.output / "reports/stubs.json").read_text()) if s["statementId"] == "linkage")
        self.assertIn("CobolReference<UnknownCobolValue>", linkage["signature"])
        self.assertIn("CobolReference<string>", linkage["signature"])
        self.assertNotIn("State state", linkage["signature"])

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="cobolcs-test-")
        self.addCleanup(self.temp.cleanup)
        self.base = pathlib.Path(self.temp.name)
        self.output = self.base / "out"

    def cli(self, *args, code=0):
        result = subprocess.run(CLI + list(map(str, args)), cwd=ROOT, capture_output=True, text=True, timeout=150)
        self.assertEqual(result.returncode, code, result.stdout + result.stderr)
        return result

    def source(self, procedure, declarations="01 N PIC 9(6) VALUE 0.", program="TEST"):
        source = self.base / "source.cbl"
        source.write_text(f"IDENTIFICATION DIVISION.\nPROGRAM-ID. {program}.\nDATA DIVISION.\nWORKING-STORAGE SECTION.\n{declarations}\nPROCEDURE DIVISION.\n{procedure}\n", encoding="utf-8")
        return source

    def run_source(self, procedure, declarations="01 N PIC 9(6) VALUE 0.", config=None, fixture=None, run_code=0):
        source = self.source(procedure, declarations)
        options = []
        if config:
            path = self.base / "config.json"
            path.write_text(json.dumps(config))
            options = ["--config", path]
        self.cli("translate", source, "--out", self.output, *options)
        fixture_path = self.base / "fixture.json"
        fixture_path.write_text(json.dumps(fixture or {}))
        result = self.cli("run", self.output, "--program", "TEST", "--fixture", fixture_path, code=run_code)
        return json.loads(result.stdout) if run_code == 0 else result

    def test_numeric_class_review(self):
        result = self.run_source('IF N IS NUMERIC DISPLAY "OK" ELSE DISPLAY "NG" END-IF GOBACK.', '01 N PIC S9V99 VALUE -1.25.')
        self.assertEqual(result["display"], ["OK"])

    def test_function_move_review(self):
        source = self.source('MOVE FUNCTION ABS(-1) TO T GOBACK.', '01 T PIC X(3).')
        self.cli("translate", source, "--out", self.output, "--mode", "strict", code=1)
        self.assertIn("CS_MOVE", (self.output / "reports/diagnostics.json").read_text())

    def test_numeric_class_matrix_both_backends(self):
        declarations = []
        body = []
        expected = []
        for i, value in enumerate(["-1", "1.25", "-1.25", "0", "25"]):
            declarations.append(f"01 N{i} PIC S9(3)V99 VALUE {value}.")
            body.append(f'IF N{i} IS NUMERIC DISPLAY "Y" ELSE DISPLAY "N" END-IF IF N{i} IS NOT NUMERIC DISPLAY "N" ELSE DISPLAY "Y" END-IF')
            expected.extend(["Y", "Y"])
        for i, value in enumerate(["123", "-12", "1.2", " 12", "1A2"]):
            declarations.append(f'01 T{i} PIC X(3) VALUE "{value}".')
            body.append(f'IF T{i} IS NUMERIC DISPLAY "Y" ELSE DISPLAY "N" END-IF IF T{i} IS NOT NUMERIC DISPLAY "Y" ELSE DISPLAY "N" END-IF')
            expected.extend(["Y", "N"] if i == 0 else ["N", "Y"])
        source = self.source(" ".join(body) + " GOBACK.", "\n".join(declarations))
        for backend in ["source", "interpreter"]:
            output = self.base / backend
            self.cli("translate", source, "--out", output, "--backend", backend)
            result = self.cli("run", output, "--program", "TEST")
            self.assertEqual(json.loads(result.stdout)["display"], expected)

    def test_function_type_validation_matrix(self):
        for backend in ["source", "interpreter"]:
            for expression, diagnostic in [('FUNCTION ABS(-1)', 'CS_MOVE'), ('FUNCTION LENGTH("abc")', 'CS_MOVE'), ('FUNCTION NUMVAL("12")', 'CS_MOVE'), ('FUNCTION INTEGER(-1.2)', 'CS_MOVE'), ('FUNCTION ABS(FUNCTION INTEGER(-1.2))', 'CS_MOVE'), ('1', 'CS_MOVE'), ('FUNCTION MYSTERY(1)', 'CS_FUNCTION'), ('FUNCTION ABS(1, 2)', 'CS_FUNCTION')]:
                with self.subTest(backend=backend, expression=expression):
                    source = self.source(f'MOVE {expression} TO T GOBACK.', '01 T PIC X(3).')
                    self.cli("translate", source, "--out", self.output, "--backend", backend, "--mode", "strict", code=1)
                    self.assertIn(diagnostic, (self.output / "reports/diagnostics.json").read_text())
        result = self.run_source('MOVE FUNCTION ABS(-1) TO N MOVE FUNCTION LENGTH("abc") TO N MOVE FUNCTION NUMVAL("12") TO N MOVE FUNCTION INTEGER(-1.2) TO N MOVE FUNCTION TRIM(" A ") TO T GOBACK.', '01 N PIC S9(3).\n01 T PIC X(3).')
        self.assertEqual(result["fields"], {"N": "-2", "T": "A  "})

    def test_source_has_no_runtime_ir_and_is_portable(self):
        source = self.source('MOVE 2 TO N IF N > 0 ADD 1 TO N END-IF PERFORM 2 TIMES SUBTRACT 1 FROM N END-PERFORM DISPLAY N GOBACK.')
        self.cli("translate", source, "--out", self.output)
        code = "\n".join(p.read_text() for p in (self.output / "Programs").glob("*.cs"))
        for forbidden in ["Instruction[]", "Machine.Run", "new Machine", "J.Parse", "JsonNode e", "Eval("]:
            self.assertNotIn(forbidden, code)
        for native in ["if (", "for (", " + ", " = "]:
            self.assertIn(native, code)
        self.assertTrue(list((self.output / "Models").glob("*State.g.cs")))
        portable = self.base / "portable"
        shutil.copytree(self.output, portable)
        shutil.rmtree(portable / "reports")
        (portable / "manifest.json").unlink()
        build = subprocess.run(["dotnet", "build", str(portable / "CobolCS.Generated.csproj"), "--nologo"], capture_output=True, text=True, timeout=150)
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
        result = subprocess.run(["dotnet", str(portable / "bin/Debug/net10.0/CobolCS.Generated.dll"), "TEST"], capture_output=True, text=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["fields"]["N"], "1")

    def edit_stub(self, stub, body):
        path = self.output / stub["file"]
        source = path.read_text()
        source, count = re.subn(r'throw new NotImplementedException\([^\n]*\);', lambda _: body, source)
        self.assertEqual(count, 1)
        path.write_text(source)
        return path

    def test_call_stub_replacement_and_regeneration(self):
        source = self.source('CALL "SERVICE" USING BY REFERENCE N BY CONTENT T RETURNING R END-CALL ADD 1 TO N DISPLAY N R T GOBACK.', '01 N PIC 9 VALUE 1.\n01 R PIC 9.\n01 T PIC X(3) VALUE "ABC".')
        self.cli("translate", source, "--out", self.output, "--mode", "strict")
        self.cli("check", self.output)
        failure = self.cli("run", self.output, "--program", "TEST", code=2)
        self.assertIn("s000000", failure.stderr)
        self.assertIn("source.cbl:", failure.stderr)
        stub = json.loads((self.output / "reports/stubs.json").read_text())[0]
        response_type = stub["signature"].split()[0]
        path = self.edit_stub(stub, f'return new {response_type}("00", DependencyOutcome.Normal, request.Argument0.Abs() + new Number(1), new Number(4));')
        path.write_bytes(b"\xef\xbb\xbf" + path.read_bytes())
        edited = path.read_bytes()
        self.cli("translate", source, "--out", self.output, "--mode", "strict")
        self.assertEqual(path.read_bytes(), edited)
        result = self.cli("run", self.output, "--program", "TEST")
        self.assertEqual(json.loads(result.stdout)["display"], ["34ABC"])
        source.write_text(source.read_text().replace('BY REFERENCE N', 'BY REFERENCE T'))
        refusal = self.cli("translate", source, "--out", self.output, code=2)
        self.assertIn("Edited stub binding changed", refusal.stderr)
        self.assertEqual(path.read_bytes(), edited)
        self.cli("check", self.output)

    def test_nested_source_temporaries(self):
        result = self.run_source('EVALUATE N WHEN 0 EVALUATE N WHEN 0 DISPLAY "nested" END-EVALUATE END-EVALUATE ADD 10 TO N ON SIZE ERROR ADD 1 TO N NOT ON SIZE ERROR DISPLAY "recovered" END-ADD END-ADD GOBACK.', '01 N PIC 9.')
        self.assertEqual(result["display"], ["nested", "recovered"])
        self.assertEqual(result["fields"]["N"], "1")

    def test_nested_call_response_and_reference_temporaries(self):
        source = self.source('CALL "A" USING N(1) ON EXCEPTION CALL "B" USING N(1) END-CALL END-CALL GOBACK.', '01 N PIC 9 OCCURS 2 TIMES.')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        for stub in json.loads((self.output / "reports/stubs.json").read_text()):
            response = stub["signature"].split()[0]
            target = stub["dependency"]["name"]
            outcome = "Exception" if target == "A" else "Normal"
            self.edit_stub(stub, f'return new {response}("00", DependencyOutcome.{outcome}, request.Argument0 + new Number(1));')
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["fields"]["N"], ["2", "0"])
        self.assertEqual(len(result["dependencies"]), 2)

    def test_not_on_size_error_only(self):
        for prefix in ["", "MAIN. "]:
            for calculation, expected in [("10", []), ("1 / 0", []), ("9", ["fits"])]:
                result = self.run_source(prefix + f'COMPUTE N = {calculation} NOT ON SIZE ERROR DISPLAY "fits" END-COMPUTE GOBACK.', '01 N PIC 9.')
                # Without ON SIZE ERROR the overflowing value is unspecified;
                # the positive branch, however, must never execute.
                self.assertEqual(result["display"], expected)

    def test_edited_stub_reordering_is_refused_atomically(self):
        source = self.source('CALL "A" USING N END-CALL CALL "B" USING N END-CALL GOBACK.')
        self.cli("translate", source, "--out", self.output)
        for stub in json.loads((self.output / "reports/stubs.json").read_text()):
            response = stub["signature"].split()[0]
            self.edit_stub(stub, f'return new {response}("00", DependencyOutcome.Normal, request.Argument0 + new Number(1));')
        before = {p.relative_to(self.output): p.read_bytes() for p in self.output.rglob("*") if p.is_file()}
        source.write_text(source.read_text().replace('"A"', '"TMP"').replace('"B"', '"A"').replace('"TMP"', '"B"'))
        result = self.cli("translate", source, "--out", self.output, code=2)
        self.assertIn("Edited stub binding changed", result.stderr)
        self.assertEqual(before, {p.relative_to(self.output): p.read_bytes() for p in self.output.rglob("*") if p.is_file()})

    def test_public_headers_numbered_paragraphs_and_filler(self):
        source = self.source('100-MAIN. PERFORM 200-WORK GOBACK. 200-WORK. DISPLAY N.', '01 G.\n 05 FILLER PIC X VALUE "A".\n 05 FILLER PIC X VALUE "B".\n 05 N PIC 9 VALUE 3.')
        source.write_text(source.read_text().replace('PROGRAM-ID. TEST.', 'PROGRAM-ID. TEST\nAUTHOR. Synthetic author.\nDATE-WRITTEN. Test only.'))
        self.cli("translate", source, "--out", self.output, "--mode", "strict")
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["display"], ["3"])
        ir = json.loads((self.output / "reports/ir.json").read_text())
        self.assertIn("Synthetic author", str(ir["programs"][0]["documentation"]))
        self.assertNotIn("FILLER", str(result["fields"]))

    def test_public_extensions_are_enumerated(self):
        sources = self.base / "sources"
        sources.mkdir()
        for index, suffix in enumerate([".COBOL", ".SQB", ".CBL", ".COB"]):
            (sources / (str(index) + suffix)).write_text(f'IDENTIFICATION DIVISION. PROGRAM-ID. P{index}. PROCEDURE DIVISION. GOBACK.')
        (sources / "not-an-entry.cpy").write_text("01 X PIC 9.")
        self.cli("translate", sources, "--out", self.output)
        self.assertEqual(len(json.loads((self.output / "manifest.json").read_text())["programs"]), 4)

    def test_no_parsed_program_still_produces_diagnostics(self):
        source = self.base / "invalid.cbl"
        source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. BROKEN. DATA DIVISION. WORKING-STORAGE SECTION.')
        for command, options in [("analyze", []), ("translate", ["--mode", "scaffold"]), ("analyze", [])]:
            self.cli(command, source, "--out", self.output, *options)
            diagnostics = json.loads((self.output / "reports/diagnostics.json").read_text())
            self.assertTrue(any(d["code"] == "PARSE_ERROR" for d in diagnostics))
            self.assertFalse(json.loads((self.output / "manifest.json").read_text())["judgments"]["generated"])

    def test_add_giving_without_to(self):
        result = self.run_source('ADD A B 3 GIVING A N DISPLAY A N GOBACK.', '01 A PIC 9 VALUE 1.\n01 B PIC 9 VALUE 2.\n01 N PIC 9 VALUE 9.')
        self.assertEqual(result["display"], ["66"])

    def test_ambiguous_qualified_fixture_names_do_not_break_build(self):
        result = self.run_source('DISPLAY "done" GOBACK.', '01 N PIC 9 VALUE 1.\n01 N PIC 9 VALUE 2.')
        self.assertEqual(list(result["fields"].values()), ["1", "2"])
        self.assertTrue(all(name.startswith("N#f") for name in result["fields"]))
        fixture = self.base / "ambiguous.json"
        fixture.write_text('{"fields":{"N":"3"}}')
        self.cli("run", self.output, "--program", "TEST", "--fixture", fixture, code=2)

    def test_unresolved_flow_and_header_stubs_have_unique_control_names(self):
        source = self.source('GO TO MISSING GOBACK.')
        source.write_text(source.read_text().replace('DATA DIVISION.', 'UNRECOGNIZED. CONTENT.\nDATA DIVISION.'))
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        failure = self.cli("run", self.output, "--program", "TEST", code=2)
        self.assertIn("source.cbl:", failure.stderr)

    def test_fixed_record_metadata_is_retained(self):
        source = self.base / "file.cbl"
        source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. TEST. ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT INPUT-FILE ASSIGN TO HOSTDD. DATA DIVISION. FILE SECTION. FD INPUT-FILE RECORDING MODE F. 01 REC PIC X(8). PROCEDURE DIVISION. GOBACK.')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        dependency = json.loads((self.output / "reports/ir.json").read_text())["programs"][0]["dependencies"][0]
        self.assertEqual(dependency["recordingMode"], "F")
        self.assertIn("RECORDING MODE F", dependency["descriptionSource"]["raw"])
        source.write_text(source.read_text().replace('RECORDING MODE F.', 'RECORD CONTAINS 8 CHARACTERS RECORDING MODE F.'))
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        dependency = json.loads((self.output / "reports/ir.json").read_text())["programs"][0]["dependencies"][0]
        self.assertEqual(dependency["file"]["minLength"], 8)
        self.assertEqual(dependency["recordingMode"], "F")
        source.write_text(source.read_text().replace('RECORDING MODE F.', 'RECORDING MODE F LABEL RECORDS ARE OMITTED BLOCK CONTAINS 0 RECORDS DATA RECORD IS REC.'))
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        dependency = json.loads((self.output / "reports/ir.json").read_text())["programs"][0]["dependencies"][0]
        self.assertEqual(dependency["blockClause"]["max"], 0)
        self.assertEqual(dependency["file"]["minLength"], 8)
        self.assertEqual(dependency["dataRecords"], ["REC"])
        self.assertEqual(len(dependency["descriptionClauses"]), 5)
        self.assertIn("LABEL RECORDS ARE OMITTED", (self.output / "reports/dependencies.json").read_text())

    def test_procedure_using_comma_keeps_argument_order(self):
        source = self.base / "linkage.cbl"
        source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. LINKAGE SECTION. 01 A PIC 9. 01 B PIC X. PROCEDURE DIVISION USING A, BY CONTENT B. GOBACK.')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        ir = json.loads((self.output / "reports/ir.json").read_text())
        self.assertEqual([a["mode"] for a in ir["programs"][0]["using"]], ["reference", "content"])
        self.cli("run", self.output, "--program", "TEST", code=2)

    def test_unreachable_stubs_and_program_isolation(self):
        sources = self.base / "sources"
        sources.mkdir()
        good = self.source('DISPLAY "GOOD" GOBACK.', program="GOOD")
        (sources / "good.cbl").write_text(good.read_text())
        bad = self.source('MOVE FUNCTION ABS(-1) TO T GOBACK.', '01 T PIC X(3).', program="BAD")
        (sources / "bad.cbl").write_text(bad.read_text())
        self.cli("translate", sources, "--out", self.output, "--mode", "strict", code=1)
        self.cli("check", self.output)
        result = self.cli("run", self.output, "--program", "GOOD")
        self.assertEqual(json.loads(result.stdout)["display"], ["GOOD"])
        diagnostics = json.loads((self.output / "reports/diagnostics.json").read_text())
        self.assertTrue(any(d.get("programId") == "BAD" and d["code"] == "CS_MOVE" for d in diagnostics))
        source = self.source('IF N = 1 CALL "UNREACHED" END-CALL END-IF DISPLAY "OK" GOBACK.')
        self.cli("translate", source, "--out", self.base / "unreachable")
        result = self.cli("run", self.base / "unreachable", "--program", "TEST")
        self.assertEqual(json.loads(result.stdout)["display"], ["OK"])

    def test_sql_typed_stub_updates_and_undefined_response(self):
        source = self.source('EXEC SQL SELECT VALUE INTO :N :IND FROM T WHERE K = :K END-EXEC IF SQLCODE = 0 DISPLAY N ELSE DISPLAY "ERROR" END-IF GOBACK.', '01 N PIC 9(2).\n01 K PIC 9 VALUE 1.\n01 IND PIC S9(4).\n01 SQLCODE PIC S9(9).')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        stub = json.loads((self.output / "reports/stubs.json").read_text())[0]
        response_type = stub["signature"].split()[0]
        self.edit_stub(stub, f'return new {response_type}("00000", DependencyOutcome.Normal, new Number(42), new Number(0), new Number(0));')
        result = self.cli("run", self.output, "--program", "TEST")
        self.assertEqual(json.loads(result.stdout)["display"], ["42"])
        path = self.output / stub["file"]
        path.write_text(re.sub(r'return new [^\n]*;', 'return null!;', path.read_text()))
        result = self.cli("run", self.output, "--program", "TEST", code=2)
        self.assertIn("Undefined dependency response", result.stderr)
        self.assertIn("s000000", result.stderr)

    def test_unsupported_c_to_f_are_editable_block_stubs(self):
        cases = [
            ('STRING "A" DELIMITED BY SIZE INTO T END-STRING', 'stringTransfer'),
            ('UNSTRING T DELIMITED BY "," INTO U END-UNSTRING', 'unstringTransfer'),
            ('INSPECT T TALLYING N FOR ALL "A"', 'inspectOperations'),
            ('INITIALIZE T', 'initialize'),
            ('MOVE CORRESPONDING G TO H', 'corresponding'),
            ('SEARCH TAB WHEN ITEM(IX) = 1 DISPLAY "INNER" END-SEARCH', 'search'),
            ('SEARCH ALL TAB WHEN ITEM(IX) = 1 DISPLAY "INNER" END-SEARCH', 'search'),
            ('EVALUATE N ALSO N WHEN 1 ALSO ANY DISPLAY "INNER" WHEN OTHER DISPLAY "OTHER" END-EVALUATE', 'controlStub'),
            ('EVALUATE N WHEN 1 THRU 3 DISPLAY "INNER" END-EVALUATE', 'controlStub'),
            ('PERFORM VARYING N FROM 1 BY 1 UNTIL N > 2 AFTER IX FROM 1 BY 1 UNTIL IX > 2 DISPLAY "INNER" END-PERFORM', 'loop'),
        ]
        declarations = '01 T PIC X(4).\n01 U PIC X(4).\n01 N PIC 9.\n01 G.\n 05 A PIC 9.\n01 H.\n 05 A PIC 9.\n01 TAB OCCURS 2 TIMES INDEXED BY IX.\n 05 ITEM PIC 9.'
        for index, (statement, kind) in enumerate(cases):
            with self.subTest(statement=statement):
                self.output = self.base / ("out" + str(index))
                source = self.source(statement + ' DISPLAY "AFTER" GOBACK.', declarations)
                self.cli("translate", source, "--out", self.output)
                self.cli("check", self.output)
                stubs = json.loads((self.output / "reports/stubs.json").read_text())
                stub = next(s for s in stubs if s["statement"]["kind"] == kind)
                result = self.cli("run", self.output, "--program", "TEST", code=2)
                self.assertIn(stub["statementId"], result.stderr)
                self.edit_stub(stub, 'return ControlResult.Continue;')
                result = self.cli("run", self.output, "--program", "TEST")
                self.assertEqual(json.loads(result.stdout)["display"], ["AFTER"])

    def test_typed_expression_stub_and_table_fixture(self):
        source = self.source('MOVE FUNCTION SUM(1, 2) TO N(2) DISPLAY N(1) N(2) GOBACK.', '01 N PIC 9 OCCURS 2 TIMES.')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        stub = json.loads((self.output / "reports/stubs.json").read_text())[0]
        self.assertTrue(stub["signature"].startswith("Number "))
        self.edit_stub(stub, 'return argument0 + argument1;')
        fixture = self.base / "fixture.json"
        fixture.write_text(json.dumps({"tableFields": [{"name": "N", "subscripts": [1], "value": "7"}]}))
        result = self.cli("run", self.output, "--program", "TEST", "--fixture", fixture)
        self.assertEqual(json.loads(result.stdout)["display"], ["73"])
        for entries in [[{"name": "N", "subscripts": [3], "value": "7"}], [{"name": "N", "subscripts": [1], "value": "7"}] * 2]:
            fixture.write_text(json.dumps({"tableFields": entries}))
            result = self.cli("run", self.output, "--program", "TEST", "--fixture", fixture, code=2)
            self.assertIn("subscript", result.stderr)

    def test_file_contract_eof_status_and_trace(self):
        source = self.base / "file.cbl"
        source.write_text('''IDENTIFICATION DIVISION.
PROGRAM-ID. TEST.
ENVIRONMENT DIVISION.
INPUT-OUTPUT SECTION.
FILE-CONTROL.
SELECT F ASSIGN TO "fixture" ORGANIZATION LINE SEQUENTIAL FILE STATUS ST.
DATA DIVISION.
FILE SECTION.
FD F.
01 REC.
 05 LINE-TEXT PIC X(3).
WORKING-STORAGE SECTION.
01 ST PIC XX.
PROCEDURE DIVISION.
OPEN INPUT F
READ F AT END DISPLAY "UNEXPECTED" NOT AT END DISPLAY LINE-TEXT END-READ
READ F AT END DISPLAY "EOF" NOT AT END DISPLAY "UNEXPECTED" END-READ
DISPLAY ST
CLOSE F
GOBACK.
''')
        self.cli("translate", source, "--out", self.output, "--mode", "strict")
        self.cli("check", self.output)
        stubs = json.loads((self.output / "reports/stubs.json").read_text())
        reads = 0
        for stub in stubs:
            response = stub["signature"].split()[0]
            if stub["statement"]["operation"].startswith("read"):
                reads += 1
                body = f'return new {response}("00", DependencyOutcome.Normal, "ABC", "00");' if reads == 1 else f'return new {response}("10", DependencyOutcome.End, "ABC", "10");'
            else:
                body = f'return new {response}("00", DependencyOutcome.Normal, "00");'
            self.edit_stub(stub, body)
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["display"], ["ABC", "EOF", "10"])
        self.assertEqual([d["operation"] for d in result["dependencies"]], ["open input", "read", "read", "close"])
        self.assertEqual(result["dependencies"][2]["outcome"], "End")

    def test_sort_merge_are_isolated_stubs(self):
        for operation in ["SORT", "MERGE"]:
            source = self.base / (operation + ".cbl")
            source.write_text('''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.
SELECT INPUT-F ASSIGN TO "in".
SELECT OUTPUT-F ASSIGN TO "out".
DATA DIVISION. FILE SECTION.
FD INPUT-F.
01 INPUT-R PIC X(4).
FD OUTPUT-F.
01 OUTPUT-R PIC X(4).
SD SORT-F.
01 SORT-R.
 05 SORT-K PIC 9(2).
 05 SORT-V PIC X(2).
PROCEDURE DIVISION.
''' + operation + ' SORT-F ON ASCENDING KEY SORT-K USING INPUT-F GIVING OUTPUT-F DISPLAY "AFTER" GOBACK.')
            self.output = self.base / (operation + "-out")
            self.cli("translate", source, "--out", self.output)
            self.cli("check", self.output)
            stub = next(s for s in json.loads((self.output / "reports/stubs.json").read_text()) if s["statement"]["kind"] == "sort")
            self.assertEqual(stub["statement"]["merge"], operation == "MERGE")
            self.cli("run", self.output, "--program", "TEST", code=2)
            self.edit_stub(stub, 'return ControlResult.Continue;')
            result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
            self.assertEqual(result["display"], ["AFTER"])

    def test_accept_fixture_sequence_and_exhaustion(self):
        result = self.run_source('ACCEPT T END-ACCEPT ACCEPT T FROM CONSOLE END-ACCEPT DISPLAY T GOBACK.', '01 T PIC X(3).', fixture={"inputs": ["A", "B"]})
        self.assertEqual(result["display"], ["B  "])
        failure = self.cli("run", self.output, "--program", "TEST", code=2)
        self.assertIn("ACCEPT fixture input exhausted", failure.stderr)
        source = self.source('ACCEPT T FROM DATE DISPLAY T GOBACK.', '01 T PIC X(8).')
        self.cli("translate", source, "--out", self.output, "--mode", "strict", code=1)
        self.assertIn("Unsupported ACCEPT FROM DATE", (self.output / "reports/diagnostics.json").read_text())

    def test_paragraph_fallthrough_thru_goto_and_size_branch(self):
        result = self.run_source('MAIN. PERFORM A THRU B DISPLAY "MAIN" GO TO LAST. A. DISPLAY "A". B. PERFORM 2 TIMES COMPUTE N = 10 ON SIZE ERROR EXIT PERFORM END-COMPUTE END-PERFORM DISPLAY "B". C. DISPLAY "BAD". LAST. DISPLAY "LAST" GOBACK.', '01 N PIC 9.')
        self.assertEqual(result["display"], ["A", "B", "MAIN", "LAST"])
        result = self.run_source('FIRST. DISPLAY "FIRST". SECOND. DISPLAY "SECOND" GOBACK.')
        self.assertEqual(result["display"], ["FIRST", "SECOND"])

    def test_names_groups_and_condition_properties(self):
        result = self.run_source('SET READY TO TRUE IF READY DISPLAY "YES" END-IF MOVE 3 TO class OF G MOVE 4 TO class OF H MOVE 5 TO 金額 DISPLAY class OF G class OF H 金額 GOBACK.', '01 FLAG PIC 9.\n88 READY VALUE 1 THRU 2.\n01 G.\n 05 class PIC 9.\n01 H.\n 05 class PIC 9.\n01 金額 PIC 9.')
        self.assertEqual(result["display"], ["YES", "345"])
        models = "\n".join(p.read_text() for p in (self.output / "Models").glob("*.cs"))
        self.assertIn("internal bool", models)
        self.assertIn("Group", models)
        mapping = json.loads((self.output / "reports/source-map.json").read_text())
        names = [entry["generatedName"] for entry in mapping if "symbolId" in entry]
        self.assertEqual(len(names), len(set(names)))

    def test_unknown_control_block_does_not_leak(self):
        source = self.source('IF FUNCTION MYSTERY(N) DISPLAY "INSIDE" END-IF DISPLAY "AFTER" GOBACK.')
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        stub = json.loads((self.output / "reports/stubs.json").read_text())[0]
        self.assertEqual(stub["statement"]["kind"], "if")
        self.edit_stub(stub, 'return ControlResult.Continue;')
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["display"], ["AFTER"])

    def test_value_and_condition_bounds_are_validated(self):
        for index, declaration in enumerate([
            "01 N PIC 9 VALUE FUNCTION MYSTERY(1).",
            "01 N PIC 9.\n88 FLAG VALUE FUNCTION MYSTERY(1) THRU 2.",
            "01 N PIC 9.\n88 FLAG VALUE 1 THRU FUNCTION MYSTERY(2).",
        ]):
            source = self.source("GOBACK.", declaration)
            self.output = self.base / ("declaration" + str(index))
            self.cli("translate", source, "--out", self.output, "--mode", "strict", code=1)
            diagnostics = json.loads((self.output / "reports/diagnostics.json").read_text())
            self.assertTrue(any(d["code"] == "CS_FUNCTION" and d["programId"] == "TEST" and d["source"] for d in diagnostics))
            self.cli("translate", source, "--out", self.output)
            self.cli("check", self.output)

    def test_evaluate_body_thru_remains_native(self):
        result = self.run_source('MAIN. EVALUATE N WHEN 0 PERFORM A THRU B WHEN OTHER DISPLAY "BAD" END-EVALUATE GOBACK. A. DISPLAY "A". B. DISPLAY "B".')
        self.assertEqual(result["display"], ["A", "B"])
        self.assertEqual(json.loads((self.output / "reports/stubs.json").read_text()), [])

    def test_call_reference_indices_are_captured_before_updates(self):
        source = self.source('CALL "S" USING I A(I) END-CALL GOBACK.', '01 I PIC 9 VALUE 1.\n01 A PIC 9(2) OCCURS 2 TIMES.')
        self.cli("translate", source, "--out", self.output)
        stub = json.loads((self.output / "reports/stubs.json").read_text())[0]
        response = stub["signature"].split()[0]
        self.edit_stub(stub, f'return new {response}("00", DependencyOutcome.Normal, new Number(2), new Number(99));')
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["fields"], {"I": "2", "A": ["99", "0"]})

    def test_remainder_destination_index_is_captured(self):
        result = self.run_source('DIVIDE N BY 2 GIVING I REMAINDER A(I) GOBACK.', '01 N PIC 9 VALUE 7.\n01 I PIC 9 VALUE 2.\n01 A PIC 9 OCCURS 4 TIMES.')
        self.assertEqual(result["fields"], {"N": "7", "I": "3", "A": ["0", "1", "0", "0"]})

    def test_repeated_call_and_exception_branch(self):
        source = self.source('PERFORM 2 TIMES CALL "S" USING N ON EXCEPTION DISPLAY "ERROR" NOT ON EXCEPTION DISPLAY "OK" END-CALL END-PERFORM GOBACK.', '01 N PIC 9.')
        self.cli("translate", source, "--out", self.output)
        stub = json.loads((self.output / "reports/stubs.json").read_text())[0]
        response = stub["signature"].split()[0]
        self.edit_stub(stub, f'return new {response}("test", request.Argument0.Numerator.IsZero ? DependencyOutcome.Normal : DependencyOutcome.Exception, request.Argument0 + new Number(1));')
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["display"], ["OK", "ERROR"])
        self.assertEqual(result["fields"]["N"], "2")
        self.assertEqual(len(result["dependencies"]), 2)

    def test_control_stub_can_jump_to_sentence_or_paragraph(self):
        source = self.source('IF N = 0 NEXT SENTENCE END-IF DISPLAY "SKIP". DISPLAY "AFTER" GOBACK.')
        self.cli("translate", source, "--out", self.output)
        stub = next(s for s in json.loads((self.output / "reports/stubs.json").read_text()) if s["statement"]["kind"] == "nextSentence")
        self.cli("check", self.output)
        fixture = self.base / "fixture.json"
        fixture.write_text('{"fields":{"N":"1"}}')
        result = json.loads(self.cli("run", self.output, "--program", "TEST", "--fixture", fixture).stdout)
        self.assertEqual(result["display"], ["SKIP", "AFTER"])
        self.edit_stub(stub, 'return ControlResult.JumpTo(source.NextSentenceTarget!);')
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["display"], ["AFTER"])
        self.output = self.base / "paragraph-jump"
        source = self.source('MAIN. EVALUATE N ALSO N WHEN 0 ALSO 0 GO TO LAST END-EVALUATE DISPLAY "SKIP". LAST. DISPLAY "AFTER" GOBACK.')
        self.cli("translate", source, "--out", self.output)
        stub = next(s for s in json.loads((self.output / "reports/stubs.json").read_text()) if s["statement"]["kind"] == "controlStub")
        self.edit_stub(stub, 'return ControlResult.JumpTo("LAST");')
        result = json.loads(self.cli("run", self.output, "--program", "TEST").stdout)
        self.assertEqual(result["display"], ["AFTER"])

    def test_reference_examples(self):
        for name in ["AMOUNT", "BRANCH", "TABLE", "FLOW", "NATIONAL", "COPYJP"]:
            with self.subTest(name=name):
                example = ROOT / "examples" / name
                out = self.base / name
                copy = ["--copybook-path", example / "copy"] if name == "COPYJP" else []
                self.cli("translate", example, "--out", out, *copy)
                result = self.cli("run", out, "--program", name, "--fixture", example / "fixture.json")
                self.assertEqual(json.loads(result.stdout), json.loads((example / "expected.json").read_text(encoding="utf-8")))
                manifest = json.loads((out / "manifest.json").read_text())
                self.assertTrue(manifest["judgments"]["built"])
                self.assertTrue(manifest["judgments"]["executedWithFixtures"])
                self.assertFalse(manifest["judgments"]["matchedBaseline"])

    def test_strict_and_scaffold(self):
        self.cli("translate", ROOT / "examples/UNSUPPORTED", "--out", self.output, "--backend", "interpreter", "--mode", "strict", code=1)
        self.assertFalse((self.output / "CobolCS.Generated.csproj").exists())
        diagnostics = json.loads((self.output / "reports/diagnostics.json").read_text())
        self.assertTrue(any(d["code"] == "CS_EXTERNAL" for d in diagnostics))
        self.cli("translate", ROOT / "examples/UNSUPPORTED", "--out", self.output, "--mode", "scaffold")
        self.cli("check", self.output)
        self.cli("run", self.output, "--program", "UNSUPPORTED", code=2)

    def test_deterministic_and_edit_protection(self):
        source = self.source("ADD 1 TO N GOBACK.")
        self.cli("translate", source, "--out", self.output)
        manifest = (self.output / "manifest.json").read_bytes()
        self.cli("translate", source, "--out", self.output)
        self.assertEqual(manifest, (self.output / "manifest.json").read_bytes())
        generated = next((self.output / "Programs").glob("*.g.cs"))
        generated.write_text(generated.read_text() + "// user edit\n")
        self.cli("translate", source, "--out", self.output, code=2)
        self.assertTrue(generated.read_text().endswith("// user edit\n"))
        self.cli("check", self.output, code=2)

    def test_unmanaged_files_preserved(self):
        source = self.source("GOBACK.")
        self.cli("translate", source, "--out", self.output)
        user = self.output / "business.txt"
        user.write_text("keep")
        self.cli("translate", source, "--out", self.output, code=2)
        self.assertEqual(user.read_text(), "keep")

    def test_compare_and_invalid_options(self):
        first = self.base / "first.json"
        second = self.base / "second.json"
        first.write_text('{"a":"1","b":"2"}')
        second.write_text('{"b":"2","a":"1"}')
        self.cli("compare", "--actual", first, "--baseline", second)
        second.write_text('{"a":"2"}')
        self.cli("compare", "--actual", first, "--baseline", second, code=1)
        self.cli("translate", "x", "--unknown", "y", code=2)

    def test_exact_decimal_and_negative_rounding(self):
        result = self.run_source("COMPUTE N ROUNDED = -1.235 DISPLAY N GOBACK.", "01 N PIC S9V99 VALUE 0.")
        self.assertEqual(result["fields"]["N"], "-1.24")
        self.assertEqual(result["display"], ["-124"])

    def test_justified_right_truncation(self):
        result = self.run_source('MOVE "ABCDE" TO T DISPLAY T GOBACK.', "01 T PIC X(3) JUSTIFIED RIGHT.")
        self.assertEqual(result["fields"]["T"], "CDE")

    def test_division_alias_remainder(self):
        result = self.run_source("DIVIDE N BY 2 GIVING N REMAINDER R DISPLAY N R GOBACK.", "01 N PIC 9 VALUE 7.\n01 R PIC 9 VALUE 0.")
        self.assertEqual(result["fields"], {"N": "3", "R": "1"})

    def test_divide_by_zero_size_error(self):
        result = self.run_source('DIVIDE 0 INTO N ON SIZE ERROR DISPLAY "zero" END-DIVIDE GOBACK.', "01 N PIC 9 VALUE 7.")
        self.assertEqual(result["display"], ["zero"])
        self.assertEqual(result["fields"]["N"], "7")

    def test_loop_and_exit(self):
        result = self.run_source("PERFORM 10 TIMES ADD 1 TO N IF N = 3 EXIT PERFORM END-IF END-PERFORM GOBACK.")
        self.assertEqual(result["fields"]["N"], "3")

    def test_test_after(self):
        result = self.run_source("PERFORM WITH TEST AFTER UNTIL N > 0 ADD 1 TO N END-PERFORM GOBACK.", "01 N PIC 9 VALUE 2.")
        self.assertEqual(result["fields"]["N"], "3")

    def test_step_limit(self):
        result = self.run_source("AGAIN. GO TO AGAIN.", config={"maxSteps": 20}, run_code=2)
        self.assertIn("Execution limit", result.stderr)

    def test_goto_depending_and_section_perform(self):
        result = self.run_source('MAIN. PERFORM WORK-SECTION GO TO A B DEPENDING ON N A. DISPLAY "A" GOBACK. B. DISPLAY "B" GOBACK. WORK-SECTION SECTION. FIRST-P. MOVE 2 TO N. LAST-P. CONTINUE.')
        self.assertEqual(result["display"], ["B"])

    def test_unsupported_profile(self):
        source = self.source("GOBACK.")
        config = self.base / "config.json"
        config.write_text('{"dataEncoding":"fujitsu-ebcdic-kana"}')
        self.cli("translate", source, "--config", config, "--out", self.output, "--mode", "strict", code=1)
        self.assertIn("CS_PROFILE", (self.output / "reports/diagnostics.json").read_text())

    def test_subscript_bounds(self):
        result = self.run_source("MOVE 1 TO N(3) GOBACK.", "01 N PIC 9 OCCURS 2 TIMES VALUE 0.", run_code=2)
        self.assertIn("subscript", result.stderr)

    def test_fixture_and_reference_modification(self):
        result = self.run_source('MOVE "Z" TO T(2:1) DISPLAY T GOBACK.', "01 T PIC X(4) VALUE SPACES.", fixture={"fields": {"T": "AB"}})
        self.assertEqual(result["fields"]["T"], "AZ  ")

    def test_unknown_external_fixture_fails(self):
        result = self.run_source("GOBACK.", fixture={"calls": [{"name": "UNKNOWN"}]}, run_code=2)
        self.assertIn("fixture", result.stderr)

    def test_missing_copy_scaffold(self):
        source = self.source("MOVE 1 TO UNKNOWN-FIELD GOBACK.", "COPY MISSING.")
        self.cli("translate", source, "--out", self.output, "--mode", "strict", code=1)
        self.cli("translate", source, "--out", self.output, "--mode", "scaffold")
        self.cli("check", self.output)
        self.cli("run", self.output, "--program", "TEST", code=2)

    def test_redefines_and_group_moves_refused(self):
        for declarations, body in [("01 N PIC X.\n01 R REDEFINES N PIC X.", "GOBACK."), ("01 G.\n05 N PIC 9.", 'MOVE "1" TO G GOBACK.')]:
            source = self.source(body, declarations)
            self.cli("translate", source, "--out", self.output, "--mode", "strict", code=1)

    @unittest.skipIf(os.name == "nt", "Symlink creation requires Windows privileges")
    def test_symlink_and_manifest_traversal_refused(self):
        source = self.source("GOBACK.")
        self.cli("translate", source, "--out", self.output)
        link = self.base / "link"
        link.symlink_to(self.output, target_is_directory=True)
        self.cli("translate", source, "--out", link, code=2)
        manifest_path = self.output / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        manifest["artifacts"]["../outside.txt"] = "0" * 64
        manifest_path.write_text(json.dumps(manifest))
        self.cli("check", self.output, code=2)

    def test_standalone_multi_program(self):
        sources = self.base / "sources"
        sources.mkdir()
        for name in ["ONE", "TWO"]:
            (sources / (name + ".cbl")).write_text(f'IDENTIFICATION DIVISION. PROGRAM-ID. {name}. PROCEDURE DIVISION. DISPLAY "{name}" GOBACK.')
        self.cli("translate", sources, "--out", self.output)
        self.cli("check", self.output)
        result = subprocess.run(["dotnet", str(self.output / "bin/Debug/net10.0/CobolCS.Generated.dll"), "TWO"], capture_output=True, text=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["display"], ["TWO"])

    def test_generated_stylecop_is_active(self):
        source = self.source("GOBACK.")
        self.cli("translate", source, "--out", self.output)
        self.cli("check", self.output)
        project = (self.output / "CobolCS.Generated.csproj").read_text(encoding="utf-8")
        self.assertIn('Include="StyleCop.Analyzers"', project)
        self.assertIn("SA1101;SA1200;SA1309;SA1629;SA1633", project)
        generated = (next((self.output / "Programs").glob("*.g.cs"))).read_text(encoding="utf-8")
        self.assertIn("/// <summary>", generated)
        self.assertIn("期待値との比較", generated)
        # 実際に文書化されていない型を加え、Analyzer が有効なことを負の試験で確かめる。
        (self.output / "Undocumented.cs").write_text("namespace CobolCS.Generated;\n\npublic class Undocumented\n{\n}\n", encoding="utf-8")
        result = subprocess.run(["dotnet", "build", str(self.output / "CobolCS.Generated.csproj"), "--no-restore", "--nologo"], capture_output=True, text=True, timeout=150)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SA1600", result.stdout)
        self.assertIn("CS1591", result.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
