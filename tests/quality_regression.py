"""Quality contract integration tests. Expected values are authored in fixtures, never sampled from C#."""
import collections
import hashlib
import json
import os
import pathlib
import shutil
import signal
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
CLI = ["dotnet", str(ROOT / "src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll")]
FIXTURES = ROOT / "tests/quality/fixtures"
CONTRACT_HASH = hashlib.sha256(b"full-state-v1:display:string[];fields:string|string[];signal:string;exact;ordered;logical-only").hexdigest()


def reviewed(value):
    """Stamp a synthetic test review; production reviews must verify the supplied independent evidence."""
    payload = {k: v for k, v in value.items() if k != "review"}
    value["review"] = dict(approved=True, reviewer="synthetic-test-author", contentHash=hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()).hexdigest())
    return value


def case(context, program, expected, case_id=None):
    return reviewed(dict(schemaVersion="1.0", caseId=case_id or program, programId=program, origin="specification", inputHashes=context["inputHashes"], configHash=context["configHash"], contract=dict(id="full-state-v1", version="1.0", hash=CONTRACT_HASH), input=dict(fields={}), expected=expected, required=True, evidence=dict(rationale="Independent hand computed portable subset fixture; not an actual COBOL reference execution.", syntheticDependencies=False, conditions={})))


class QualityRegression(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="cobolcs-quality-")
        self.addCleanup(self.temp.cleanup)
        self.base = pathlib.Path(self.temp.name)
        self.output = self.base / "out"

    def command(self, args, code=0, timeout=180):
        process = subprocess.Popen(list(map(str, args)), cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=os.name != "nt")
        try:
            stdout, stderr = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                process.kill()
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.communicate()
            raise
        self.assertEqual(process.returncode, code, stdout + stderr)
        return stdout + stderr

    def translate(self, source, code=0, mode="strict", output=None):
        return self.command(CLI + ["translate", source, "--out", output or self.output, "--mode", mode], code)

    def run_tests(self, code=0, output=None):
        return self.command(["dotnet", "test", (output or self.output) / "Tests/CobolCS.ReferenceTests.csproj", "--logger", "trx", "--nologo", "-p:NuGetAudit=false"], code)

    def context(self):
        return json.loads((self.output / "Tests/Templates/ReferenceCases/context.json").read_text())

    def add_case(self, value, name="case.json"):
        path = self.output / "Tests/ReferenceCases" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n")
        return path

    def summary(self, output=None):
        reports = list(((output or self.output) / "TestResults/quality").glob("*/summary.json"))
        return json.loads(max(reports, key=lambda p: p.stat().st_mtime_ns).read_text())

    def test_semantic_corpus(self):
        sources = self.base / "supported"
        sources.mkdir()
        specs = [json.loads(p.read_text()) for p in sorted(FIXTURES.glob("*.spec.json"))]
        supported = [s for s in specs if not s["blocked"]]
        for s in supported:
            shutil.copyfile(FIXTURES / (s["name"] + ".cbl"), sources / (s["name"] + ".cbl"))
        self.translate(sources)
        context = self.context()
        for s in supported:
            self.add_case(case(context, s["programId"], s["expected"]), s["name"] + ".json")
        self.translate(sources)
        self.run_tests()
        result = self.summary()
        self.assertEqual(result["countsByOrigin"]["specification"]["matched"], len(supported))
        self.assertEqual(result["countsByOrigin"]["cobol-reference"]["matched"], 0)
        for s in supported:
            normal = self.command(["dotnet", self.output / "bin/Debug/net10.0/CobolCS.Generated.dll", s["programId"]])
            self.assertEqual(json.loads(normal), s["expected"], "Tracing must not change outputs")
        for s in [s for s in specs if s["blocked"]]:
            out = self.base / s["name"]
            self.translate(FIXTURES / (s["name"] + ".cbl"), code=1, output=out)
            diagnostics = json.loads((out / "reports/diagnostics.json").read_text())
            self.assertIn(s["blocked"], [d["code"] for d in diagnostics], s["name"])
            summary = json.loads((out / "reports/quality/summary.json").read_text())
            self.assertEqual(summary["execution"], "not-run")
            self.assertTrue(summary["statementCounts"]["untranslated"] or summary["statementCounts"]["stubbed"])
        print("Quality corpus:", len(specs), "fixtures;", len(supported), "value comparisons;", len(specs)-len(supported), "explicitly blocked;", dict(collections.Counter(s["category"] for s in specs)))

    def test_mutations_and_standalone(self):
        source = self.base / "M.cbl"
        source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. M. DATA DIVISION. WORKING-STORAGE SECTION. 01 N PIC 9 VALUE 0. PROCEDURE DIVISION. MOVE 2 TO N IF N >= 2 ADD 1 TO N END-IF GOBACK.')
        self.translate(source)
        expected = dict(display=[], fields=dict(N="3"), signal="goback")
        self.add_case(case(self.context(), "M", expected))
        self.translate(source)
        self.run_tests()
        program_path = self.output / "Programs/Program0000.cs"
        original = program_path.read_text()
        move_line = next(l for l in original.splitlines() if 'new("basic"' in l and r'\u0022kind\u0022:\u0022move\u0022' in l)
        deleted_move = move_line.replace('new("basic"', 'new("jump"').replace('), -1, -1, "s000000")', '), 1, -1, "s000000")')
        mutations = [original.replace(r'\u003E=', r'\u003E'), original.replace(r'\u0022value\u0022:\u00221\u0022', r'\u0022value\u0022:\u00222\u0022'), original.replace(move_line, deleted_move)]
        for mutation in mutations:
            self.assertNotEqual(original, mutation)
            program_path.write_text(mutation)
            self.run_tests(code=1)
            self.assertEqual(self.summary()["countsByOrigin"]["specification"]["mismatched"], 1)
        program_path.write_text(original)
        standalone = self.base / "standalone"
        shutil.copytree(self.output, standalone, ignore=shutil.ignore_patterns("bin", "obj", "reports", "manifest.json", "TestResults"))
        shutil.rmtree(self.output)
        source.unlink()
        self.run_tests(output=standalone)
        self.assertEqual(self.summary(standalone)["status"], "matched")

    def test_invalid_stale_missing_and_preservation(self):
        source = self.base / "add.cbl"
        shutil.copyfile(FIXTURES / "add.cbl", source)
        self.translate(source)
        value = case(self.context(), "Q006", dict(display=[], fields=dict(N="5"), signal="goback"))
        path = self.add_case(value)
        before = path.read_bytes()
        self.translate(source)
        self.assertEqual(path.read_bytes(), before)
        self.run_tests()
        source.write_text(source.read_text() + "\n*> Source change invalidates prior independent evidence.\n")
        self.translate(source)
        changed_summary = json.loads((self.output / "reports/quality/summary.json").read_text())
        self.assertTrue(any(e["status"] == "stale" for e in changed_summary["executionEvidence"]))
        self.assertIn("Stale source", (self.output / "reports/quality/reference-inventory.json").read_text())
        value["expected"]["fields"]["N"] = "6"
        path.write_text(json.dumps(value))
        self.run_tests(code=1)
        self.translate(source)
        self.run_tests(code=1)
        summary = json.loads((self.output / "reports/quality/summary.json").read_text())
        self.assertEqual(summary["independentVerification"], "blocked")
        path.unlink()
        self.translate(source)
        self.run_tests(code=1)
        self.assertFalse(path.exists())
        self.assertIn("Missing required", (self.output / "reports/quality/reference-inventory.json").read_text())

    def test_schema_and_reference_provenance(self):
        source = FIXTURES / "add.cbl"
        self.translate(source)
        base = case(self.context(), "Q006", dict(display=[], fields=dict(N="5"), signal="goback"))
        self.add_case(base, "valid.json")
        duplicate = dict(base)
        self.add_case(duplicate, "duplicate.json")
        malformed = dict(base, caseId="bad", origin="cobol-reference", required=False)
        self.add_case(reviewed(malformed), "malformed.json")
        self.translate(source)
        self.run_tests(code=1)
        errors = (self.output / "reports/quality/reference-inventory.json").read_text()
        self.assertIn("Duplicate caseId", errors)
        self.assertIn("Missing syntheticCapture", errors)
        (self.output / "Tests/ReferenceCases/duplicate.json").unlink()
        (self.output / "Tests/ReferenceCases/malformed.json").unlink()
        for processor in ("synthetic-processor-A", "synthetic-processor-B"):
            reference = json.loads(json.dumps(base))
            reference.update(caseId=processor, origin="cobol-reference")
            capture = self.output / "Tests/ReferenceCases/Data" / (processor + ".txt")
            capture.parent.mkdir(exist_ok=True)
            capture.write_text("synthetic payload; never executed on a COBOL processor\n")
            reference["evidence"].update(syntheticCapture=True, referenceExecution=dict(processor=processor, version="unknown", compileOptions="unknown", capturedAt="2026-10-12T00:00:00Z", method="Synthetic import contract test only"), captureFiles={"Data/" + capture.name: hashlib.sha256(capture.read_bytes()).hexdigest()})
            self.add_case(reviewed(reference), processor + ".json")
        self.translate(source)
        self.run_tests()
        result = self.summary()
        self.assertEqual(result["countsByOrigin"]["cobol-reference"]["matched"], 0)
        self.assertEqual(result["syntheticReferenceContractTests"], 2)
        self.assertEqual(len(result["cobolReferenceByProcessor"]), 2)

    def test_malformed_schema_generates_failing_tests(self):
        self.translate(FIXTURES / "add.cbl")
        self.add_case(dict(schemaVersion="unsupported", unrecognized=True))
        self.translate(FIXTURES / "add.cbl")
        result = self.run_tests(code=1)
        self.assertIn("Failed ReferenceCaseGate", result)
        self.assertEqual(self.summary()["status"], "failed")

    def test_not_configured_and_contract_gate(self):
        self.translate(FIXTURES / "add.cbl")
        self.run_tests()
        self.assertEqual(self.summary()["status"], "not-configured")
        contract = self.output / "Tests/Contracts/full-state-v1.json"
        contract.write_text(contract.read_text() + " ")
        self.run_tests(code=1)
        self.assertEqual(self.summary()["status"], "failed")

    def test_unparsed_and_copy_positions(self):
        self.translate(FIXTURES / "unknown-recovery.cbl", code=1)
        summary = json.loads((self.output / "reports/quality/summary.json").read_text())
        self.assertGreater(summary["unparsedRegions"], 0)
        self.assertFalse(summary["statementDenominatorComplete"])
        source = self.base / "COPY.cbl"
        (self.base / "A.cpy").write_text('DISPLAY "copy".')
        source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. C. PROCEDURE DIVISION. COPY A. COPY A. GOBACK.')
        out = self.base / "copy"
        self.translate(source, output=out)
        ledger = json.loads((out / "reports/quality/translation-ledger.json").read_text())
        copies = [i for i in ledger["items"] if i["regionType"] == "copy-expansion"]
        self.assertEqual(len(copies), 2)
        self.assertNotEqual(copies[0]["regionId"], copies[1]["regionId"])
        displays = [i for i in ledger["items"] if i["regionType"] == "statement" and i["source"]["file"] == "A.cpy" and i["source"]["raw"].startswith("DISPLAY")]
        self.assertNotEqual(displays[0]["copyExpansionPath"], displays[1]["copyExpansionPath"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
