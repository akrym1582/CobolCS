"""Ordered external adapters, reviewed business boundaries and config-driven adoption."""
import base64
import copy
import hashlib
import json
import os
import pathlib
import shutil
import unittest
import xml.etree.ElementTree as ET

from quality_regression import CLI, ROOT, QualityRegression, case, reviewed


def stamp(value):
    return reviewed(value)


def request(statement, kind, target, operation, arguments=None, query=""):
    return dict(statementId=statement, kind=kind, target=target, operation=operation, arguments=arguments or [], query=query)


def arg(mode, value):
    return dict(mode=mode, value=value)


def reply(req, outcome="success", status="0", **values):
    return dict(request=req, response=dict(outcome=outcome, status=status, **values))


class CompletionRegression(unittest.TestCase):
    setUp = QualityRegression.setUp
    command = QualityRegression.command
    run_tests = QualityRegression.run_tests
    context = QualityRegression.context
    add_case = QualityRegression.add_case
    summary = QualityRegression.summary

    def source(self, body, declarations="01 N PIC 9 VALUE 0.", extra="", name="TEST"):
        path = self.base / (name + ".cbl")
        path.write_text(f"IDENTIFICATION DIVISION.\nPROGRAM-ID. {name}.\n{extra}DATA DIVISION.\nWORKING-STORAGE SECTION.\n{declarations}\nPROCEDURE DIVISION.\n{body}\n")
        return path

    def translate(self, source, settings=None, code=0):
        config = self.base / "config.json"
        config.write_text(json.dumps(settings or {}))
        return self.command(CLI + ["translate", source, "--out", self.output, "--config", config], code)

    def fixed_case(self, source, responses, expected, contract=None, input_values=None):
        self.translate(source, dict(externalAdapter="fixed"))
        context = self.context()
        value = case(context, "TEST", expected)
        value["input"]["externalResponses"] = responses
        value["evidence"]["syntheticDependencies"] = True
        if contract:
            value["contract"] = dict(id=contract["id"], version=contract["version"], hash=contract["review"]["contentHash"])
            value["input"] = dict(values=input_values or {}, arguments=[], externalResponses=responses)
        path = self.add_case(stamp(value))
        self.translate(source, dict(externalAdapter="fixed"))
        return path, value

    def contract(self, observations, inputs=None, arguments=None, include_bytes=False):
        return stamp(dict(schemaVersion="1.0", id="business-test", version="1.0", programId="TEST", inputs=inputs or [], arguments=arguments or [], observations=observations, includeRequests=True, includeSignal=True, includeBytes=include_bytes))

    def save_contract(self, value):
        path = self.output / "Tests/Contracts/business.json"
        path.write_text(json.dumps(value, indent=2))
        return path

    def test_call_group_modes_and_strict_responses(self):
        source = self.source('CALL "EXT" USING BY REFERENCE G BY CONTENT N BY VALUE 7 RETURNING RC IF N = 5 DISPLAY "updated" END-IF GOBACK.', '01 G.\n05 N PIC 9 VALUE 1.\n05 T PIC X(2) VALUE "A".\n01 RC PIC 9 VALUE 0.')
        req = request("s000000", "call", "EXT", "call", [arg("reference", dict(N="1", T="A ")), arg("content", "1"), arg("value", "7")])
        responses = [reply(req, arguments=[dict(N="5", T="B "), "1", "7"], returning="2")]
        expected = dict(display=["updated"], fields={"N OF G": "5", "T OF G": "B ", "RC": "2"}, signal="goback", externalRequests=[req], bytes=[])
        path, value = self.fixed_case(source, responses, expected)
        self.run_tests()
        self.assertEqual(self.summary()["countsByOrigin"]["specification"]["matched"], 1)
        variants = [[], responses + responses, [reply(dict(req, target="OTHER"), arguments=[dict(N="5", T="B "), "1", "7"], returning="2")], [reply(dict(req, arguments=[arg("content", dict(N="1", T="A ")), arg("content", "1"), arg("value", "7")]), arguments=[dict(N="5", T="B "), "1", "7"], returning="2")], [reply(req, arguments=[dict(N="5", T="B "), "9", "7"], returning="2")]]
        for responses in variants:
            variant = copy.deepcopy(value)
            variant["input"]["externalResponses"] = responses
            path.write_text(json.dumps(stamp(variant)))
            self.translate(source, dict(externalAdapter="fixed"))
            self.run_tests(code=1)
            self.assertGreater(self.summary()["infrastructureFailures"], 0)

    def test_file_eof_status_and_request_order(self):
        source = self.base / "TEST.cbl"
        source.write_text('''IDENTIFICATION DIVISION.
PROGRAM-ID. TEST.
ENVIRONMENT DIVISION.
INPUT-OUTPUT SECTION.
FILE-CONTROL.
SELECT F ASSIGN TO "NEVER-OPENED" ORGANIZATION IS SEQUENTIAL FILE STATUS FS.
DATA DIVISION.
FILE SECTION.
FD F.
01 REC.
05 V PIC 9.
WORKING-STORAGE SECTION.
01 FS PIC XX.
01 SUM-N PIC 99 VALUE 0.
PROCEDURE DIVISION.
OPEN INPUT F
PERFORM 2 TIMES
READ F AT END DISPLAY "eof" NOT AT END ADD V TO SUM-N END-READ
END-PERFORM
CLOSE F
GOBACK.
''')
        self.translate(source, dict(externalAdapter="fixed"))
        ir = json.loads((self.output / "reports/ir.json").read_text())
        statements = ir["programs"][0]["paragraphs"][0]["statements"]
        opening = statements[0]["body"][0]["id"]
        read = statements[1]["body"][0]["id"]
        closing = statements[2]["body"][0]["id"]
        requests = [request(opening, "file", "F", "open input"), request(read, "file", "F", "read"), request(read, "file", "F", "read"), request(closing, "file", "F", "close")]
        responses = [reply(requests[0], status="00"), reply(requests[1], status="00", record=dict(V="3")), reply(requests[2], outcome="eof", status="10"), reply(requests[3], status="00")]
        expected = dict(display=["eof"], fields={"V OF REC": "3", "FS": "00", "SUM-N": "3"}, signal="goback", externalRequests=requests, bytes=[])
        path, value = self.fixed_case(source, responses, expected)
        self.run_tests()
        variant = copy.deepcopy(value)
        variant["input"]["externalResponses"][0], variant["input"]["externalResponses"][1] = variant["input"]["externalResponses"][1], variant["input"]["externalResponses"][0]
        path.write_text(json.dumps(stamp(variant)))
        self.translate(source, dict(externalAdapter="fixed"))
        self.run_tests(code=1)
        self.assertGreater(self.summary()["infrastructureFailures"], 0)

    def test_file_write_from_observes_fitted_record(self):
        source = self.base / "TEST.cbl"
        source.write_text('''IDENTIFICATION DIVISION.
PROGRAM-ID. TEST.
ENVIRONMENT DIVISION.
INPUT-OUTPUT SECTION.
FILE-CONTROL.
SELECT F ASSIGN TO "NEVER-WRITTEN" ORGANIZATION IS SEQUENTIAL FILE STATUS FS.
DATA DIVISION.
FILE SECTION.
FD F.
01 REC PIC X(3).
WORKING-STORAGE SECTION.
01 FS PIC XX.
01 T PIC X(5) VALUE "ABCDE".
PROCEDURE DIVISION.
OPEN OUTPUT F
WRITE REC FROM T
CLOSE F
GOBACK.
''')
        self.translate(source, dict(externalAdapter="fixed"))
        statements = json.loads((self.output / "reports/ir.json").read_text())["programs"][0]["paragraphs"][0]["statements"]
        requests = [request(statements[0]["body"][0]["id"], "file", "F", "open output"), request(statements[1]["id"], "file", "F", "write", [arg("record", "ABC")]), request(statements[2]["body"][0]["id"], "file", "F", "close")]
        responses = [reply(r, status="00") for r in requests]
        self.fixed_case(source, responses, dict(display=[], fields=dict(REC="ABC", FS="00", T="ABCDE"), signal="goback", externalRequests=requests, bytes=[]))
        self.run_tests()

    def test_sql_success_no_data_error_and_bytes(self):
        source = self.source('EXEC SQL SELECT 1 INTO :N END-EXEC IF SQLCODE = 0 DISPLAY "ok" ELSE IF SQLCODE = 100 DISPLAY "empty" ELSE DISPLAY "error" END-IF END-IF GOBACK.', '01 N PIC 9 VALUE 0.\n01 SQLCODE PIC S9(9) VALUE 0.\n01 SQLSTATE PIC X(5) VALUE "00000".')
        req = request("s000000", "sql", "SELECT", "select", query="SELECT 1")
        for outcome, status, output, display, sqlstate in [("success", "0", ["4"], "ok", "00000"), ("no-data", "100", [], "empty", "02000"), ("error", "-9", [], "error", "HY000")]:
            responses = [reply(req, outcome, status, outputs=output, sqlstate=sqlstate)]
            expected = dict(display=[display], fields=dict(N="4" if output else "0", SQLCODE=status, SQLSTATE=sqlstate), signal="goback", externalRequests=[req], bytes=[])
            self.fixed_case(source, responses, expected)
            self.run_tests()
        value = json.loads((self.output / "Tests/ReferenceCases/case.json").read_text())
        expected_bytes = dict(base64=base64.b64encode(b"ABC").decode(), length=3, recordLengths=[1, 2], encoding="opaque-test")
        actual_bytes = dict(expected_bytes, base64=base64.b64encode(b"AXC").decode())
        value["input"]["externalResponses"][0]["response"]["bytes"] = [actual_bytes]
        value["expected"]["bytes"] = [expected_bytes]
        self.add_case(stamp(value))
        self.translate(source, dict(externalAdapter="fixed"))
        self.run_tests(code=1)
        report = self.summary()["cases"][0]
        self.assertEqual(report["comparison"]["differences"][0]["byteComparison"]["firstMismatchOffset"], 1)

    def test_sql_null_is_blocked_not_coerced_to_zero(self):
        source = self.source('EXEC SQL SELECT 1 INTO :N INDICATOR :I END-EXEC GOBACK.', '01 N PIC 9 VALUE 7.\n01 I PIC S9 VALUE 0.\n01 SQLCODE PIC S9(9) VALUE 0.')
        req = request("s000000", "sql", "SELECT", "select", query="SELECT 1")
        self.fixed_case(source, [reply(req, outputs=["0", "-1"])], dict(display=[], fields=dict(N="0", I="-1", SQLCODE="0"), signal="goback", externalRequests=[req], bytes=[]))
        self.run_tests(code=1)
        self.assertGreater(self.summary()["infrastructureFailures"], 0)
        self.assertIn("undefined", self.summary()["cases"][0]["error"])

    def test_sql_whenever_controls_flow_without_reevaluation(self):
        source = self.source('EXEC SQL WHENEVER NOT FOUND GO TO EMPTY END-EXEC EXEC SQL SELECT 1 INTO :N END-EXEC MOVE 1 TO FLAG GOBACK. EMPTY. MOVE 7 TO FLAG GOBACK.', '01 N PIC 9 VALUE 0.\n01 SQLCODE PIC S9(9) VALUE 0.\n01 FLAG PIC 9 VALUE 0.')
        req = request("s000001", "sql", "SELECT", "select", query="SELECT 1")
        self.fixed_case(source, [reply(req, "no-data", "100", outputs=[])], dict(display=[], fields=dict(N="0", SQLCODE="100", FLAG="7"), signal="goback", externalRequests=[req], bytes=[]))
        self.run_tests()
        self.assertTrue(any(b["branch"]["op"] == "externalGoto" and b["reachedOutcomes"] == [True] for b in self.summary()["branchCoverage"]["catalog"]))

    def test_business_boundary_preservation_and_mutations(self):
        source = self.source('ADD 2 TO N CALL "EXT" USING N IF N >= 5 DISPLAY "yes" END-IF GOBACK.', '01 N PIC 9 VALUE 0.\n01 INTERNAL PIC 9 VALUE 8.')
        self.translate(source, dict(externalAdapter="fixed"))
        contract = self.contract([dict(key="total", field="N", type="number", statementId="s000000")], inputs=[dict(key="start", field="N", type="number")])
        contract_path = self.save_contract(contract)
        req = request("s000001", "call", "EXT", "call", [arg("reference", "3")])
        responses = [reply(req, arguments=["5"])]
        expected = dict(values=dict(total="5"), signal="goback", externalRequests=[req])
        path, value = self.fixed_case(source, responses, expected, contract, dict(start="1"))
        self.run_tests()
        contract_bytes = contract_path.read_bytes()
        self.translate(source, dict(externalAdapter="fixed"))
        self.assertEqual(contract_path.read_bytes(), contract_bytes)
        program = self.output / "Programs/Program0000.cs"
        original = program.read_text()
        call_line = next(l for l in original.splitlines() if 'new("basic"' in l and r'\u0022kind\u0022:\u0022external\u0022' in l)
        # Repeat the external side effect without altering addresses.
        machine = self.output / "Runtime/Machine.cs"
        machine_original = machine.read_text()
        machine.write_text(machine_original.replace('ExecuteExternal(s);', 'ExecuteExternal(s);\n                ExecuteExternal(s);'))
        self.run_tests(code=1)
        self.assertGreater(self.summary()["infrastructureFailures"], 0)
        machine.write_text(machine_original)
        # Internal state changes are outside the reviewed boundary and preserve the business result.
        api = self.output / "GeneratedApi.cs"
        api.write_text(api.read_text().replace(r'\u00228\u0022', r'\u00227\u0022'))
        self.run_tests()
        variant = copy.deepcopy(contract)
        variant["observations"][0]["field"] = "INTERNAL"
        contract_path.write_text(json.dumps(variant))
        self.run_tests(code=1)

    def test_adoption_trx_review_latest_and_standalone(self):
        source = self.source('ADD 2 TO N GOBACK.')
        self.translate(source)
        value = case(self.context(), "TEST", dict(display=[], fields=dict(N="2"), signal="goback"))
        self.add_case(value)
        self.translate(source)
        self.run_tests()
        trx = max((self.output / "Tests/TestResults").glob("*.trx"), key=lambda p: p.stat().st_mtime_ns)
        review = json.loads((self.output / "Tests/Templates/ReferenceCases/adoption-review.json").read_text())
        review.update(approved=True, reviewer="synthetic-adoption-test")
        review_path = self.base / "review.json"
        review_path.write_text(json.dumps(review))
        destination = self.base / "adopted"
        settings = dict(quality=dict(adopt=dict(destination=str(destination), trx=str(trx), review=str(review_path))))
        # A skip, duplicate or zero behavioral result cannot be adopted.
        tree = ET.parse(trx)
        result = next(e for e in tree.iter() if e.tag.endswith("UnitTestResult") and e.attrib["testName"] == "Reference0000")
        result.set("outcome", "NotExecuted")
        rejected = self.base / "skipped.trx"
        tree.write(rejected)
        invalid = copy.deepcopy(settings)
        invalid["quality"]["adopt"]["trx"] = str(rejected)
        self.translate(source, invalid, code=2)
        self.assertFalse(destination.exists())
        namespace = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
        for mutation in ("zero", "duplicate", "noop", "wrong-execution"):
            tree = ET.parse(trx)
            results = tree.getroot().find(namespace + "Results")
            behavior = next(e for e in results if e.attrib.get("testName") == "Reference0000")
            if mutation == "zero":
                results.clear()
            elif mutation == "duplicate":
                results.append(copy.deepcopy(behavior))
            elif mutation == "noop":
                behavior.set("testName", "NoOp")
            else:
                stdout = behavior.find(".//" + namespace + "StdOut")
                stdout.text = "COBOLCS_QUALITY_EXECUTION=" + "0" * 32
            bad = self.base / (mutation + ".trx")
            tree.write(bad)
            invalid = copy.deepcopy(settings)
            invalid["quality"]["adopt"]["trx"] = str(bad)
            self.translate(source, invalid, code=2)
            self.assertFalse(destination.exists())
        stale_review = copy.deepcopy(review)
        stale_review["contentHash"] = "0" * 64
        review_path.write_text(json.dumps(stale_review))
        self.translate(source, settings, code=2)
        review_path.write_text(json.dumps(review))
        # A newer attempt that never completed cannot reuse earlier green evidence.
        pending = self.output / "TestResults/quality" / ("f" * 32)
        pending.mkdir()
        (pending / "started.json").write_text(json.dumps(dict(startedAt="2099-01-01T00:00:00Z")))
        self.translate(source, settings, code=2)
        shutil.rmtree(pending)
        proof_path = max((self.output / "TestResults/quality").glob("*/summary.json"), key=lambda p: p.stat().st_mtime_ns)
        original_proof = proof_path.read_bytes()
        proof = json.loads(original_proof)
        proof["compiledSourceFingerprint"] = "0" * 64
        proof_path.write_text(json.dumps(proof))
        self.translate(source, settings, code=2)
        proof_path.write_bytes(original_proof)
        self.translate(source, settings)
        adopted = json.loads((destination / "adoption.json").read_text())
        self.assertEqual(adopted["independentVerification"], "tested-registered-cases-only")
        self.assertFalse((destination / "reports").exists())
        shutil.rmtree(self.output)
        source.unlink()
        self.run_tests(output=destination)
        program = destination / "Programs/Program0000.cs"
        program.write_text(program.read_text().replace(r'\u0022value\u0022:\u00222\u0022', r'\u0022value\u0022:\u00223\u0022'))
        self.run_tests(code=1, output=destination)

    def test_business_arguments_and_precise_array_observation(self):
        source = self.source('ADD A TO N MOVE 2 TO TABLE-N(2) GOBACK.', '01 N PIC S9(4)V99 VALUE 0.\n01 A PIC 9 VALUE 0.\n01 TABLE-N PIC 9 OCCURS 2 TIMES VALUE 0.\n01 T PIC X(3) VALUE "A".')
        self.translate(source)
        contract = self.contract([dict(key="amount", field="N", type="number", statementId="s000000"), dict(key="items", field="TABLE-N", type="number-array"), dict(key="label", field="T", type="text")], inputs=[dict(key="amount", field="N", type="number")], arguments=[dict(key="addition", field="A", type="number")])
        path = self.save_contract(contract)
        value = case(self.context(), "TEST", dict(values=dict(amount="1.250", items=["0", "2"], label="A  "), signal="goback", externalRequests=[]))
        value["contract"] = dict(id=contract["id"], version=contract["version"], hash=contract["review"]["contentHash"])
        value["input"] = dict(values=dict(amount="0.25"), arguments=["1"])
        self.add_case(stamp(value))
        self.translate(source)
        self.run_tests()
        probe = self.base / "probe"
        probe.mkdir()
        (probe / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><ProjectReference Include="' + str(self.output / "CobolCS.Generated.csproj") + '" /></ItemGroup></Project>')
        (probe / "Program.cs").write_text('using CobolCS.Generated; using CobolCS.Runtime; using System.Text.Json.Nodes; var result = GeneratedApi.RunBusiness("business-test", new BusinessInput(new JsonObject { ["amount"] = "0.25" }, new JsonArray("1"), new JsonObject(), new JsonArray())); if (result.Observation["values"]!["amount"]!.GetValue<string>() != "1.25" || result.Snapshot["fields"]!["T"]!.GetValue<string>() != "A  ") throw new Exception("Typed business API failed");')
        self.command(["dotnet", "run", "--project", probe / "Probe.csproj", "-p:NuGetAudit=false"])
        variant = copy.deepcopy(value)
        variant["expected"]["values"]["items"] = ["2", "0"]
        variant["expected"]["values"]["label"] = "A"
        self.add_case(stamp(variant))
        self.translate(source)
        self.run_tests(code=1)
        differences = self.summary()["cases"][0]["comparison"]["differences"]
        self.assertTrue(any(d.get("businessKey") == "items" for d in differences))
        self.assertTrue(any(d.get("businessKey") == "label" for d in differences))
        self.translate(source)
        work = json.loads((self.output / "reports/quality/work-items.json").read_text())
        self.assertTrue(any(w["ruleId"] == "reference-comparison-mismatch" for w in work))
        self.assertTrue(path.exists())

    def test_characterization_adoption_is_not_independent(self):
        source = self.source('ADD 2 TO N GOBACK.')
        self.translate(source)
        self.run_tests()
        self.assertEqual(self.summary()["status"], "not-configured")
        self.assertEqual(self.summary()["countsByOrigin"]["csharp-characterization"]["matched"], 1)
        review = json.loads((self.output / "Tests/Templates/ReferenceCases/adoption-review.json").read_text())
        review.update(approved=True, reviewer="synthetic-characterization-test")
        review_path = self.base / "review.json"
        review_path.write_text(json.dumps(review))
        trx = max((self.output / "Tests/TestResults").glob("*.trx"), key=lambda p: p.stat().st_mtime_ns)
        destination = self.base / "adopted"
        self.translate(source, dict(quality=dict(adopt=dict(destination=str(destination), trx=str(trx), review=str(review_path)))))
        self.assertEqual(json.loads((destination / "adoption.json").read_text())["independentVerification"], "not-configured")


del QualityRegression

if __name__ == "__main__":
    unittest.main(verbosity=2)
