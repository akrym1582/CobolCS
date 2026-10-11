"""Independent ingestion and measurement tests; no downloaded corpus required."""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location("public_corpus", Path(__file__).resolve().parents[1] / "tools/public_corpus.py")
corpus = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(corpus)


class CorpusTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "native.SQB"
        self.source.write_text('IDENTIFICATION DIVISION.\nPROGRAM-ID. NATIVE.\n'
                               'DATA DIVISION.\nWORKING-STORAGE SECTION.\n01 N PIC 9 VALUE 0.\n'
                               'PROCEDURE DIVISION.\nMAIN.\nPERFORM STEP-P\n'
                               'EVALUATE N WHEN 1 DISPLAY "one" WHEN OTHER DISPLAY "wrong" END-EVALUATE\n'
                               'GOBACK.\nSTEP-P.\nADD 1 TO N.\n', encoding="utf-8")
        self.case = {"id": "native-1", "source": self.source.name, "sha256": corpus.digest(self.source),
                     "sourceFormat": "free", "copybookPaths": [], "license": "MIT", "optionalGpl": False}
        self.lock = self.root / "lock.json"
        self.save([self.case])

    def save(self, cases):
        corpus.write_json(self.lock, {"schemaVersion": 1, "scope": "synthetic", "cases": cases})

    def test_tamper_fails_before_output_is_created(self):
        self.source.write_text("changed")
        with self.assertRaisesRegex(ValueError, "hash mismatch"):
            corpus.evaluate(self.lock, self.root, self.root / "out")
        self.assertFalse((self.root / "out").exists())

    def test_duplicate_ids_are_not_merged(self):
        self.save([self.case, dict(self.case)])
        with self.assertRaisesRegex(ValueError, "duplicate case ID"):
            corpus.load_cases(self.lock, self.root)

    def test_gpl_requires_explicit_selection(self):
        gpl = dict(self.case, id="optional", license="GPL-3.0-only", optionalGpl=True)
        self.save([self.case, gpl])
        self.assertEqual(len(corpus.load_cases(self.lock, self.root)[1]), 1)
        self.assertEqual(len(corpus.load_cases(self.lock, self.root, True)[1]), 2)

    def test_paths_and_copy_hashes(self):
        for path in ["../escape", "/absolute", "a/../b", "a\\b", "./a"]:
            with self.subTest(path=path), self.assertRaises(ValueError):
                corpus.confined(self.root, path)
        self.case["copybookHashes"] = {"native.SQB": "0" * 64}
        self.save([self.case])
        with self.assertRaisesRegex(ValueError, "COPY hash mismatch"):
            corpus.load_cases(self.lock, self.root)

    @unittest.skipIf(os.name == "nt", "POSIX symlink fixture")
    def test_symlink_escape(self):
        (self.root / "link").symlink_to(self.root.parent, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "escapes"):
            corpus.confined(self.root, "link/outside")

    def test_timeout_is_not_success(self):
        result = corpus.command([sys.executable, "-c", "import time; time.sleep(10)"], timeout=0.05)
        self.assertTrue(result["timedOut"])
        self.assertNotEqual(result["exitCode"], 0)

    def test_verify_requires_adapter_failure_and_preservation_evidence(self):
        row = {"id": "adapter", "strictGenerated": False, "scaffoldGenerated": True,
               "built": True, "matchedIndependentExpectation": True, "executedWithoutReports": True,
               "unsetAdapterFailed": True, "stubFixtureExecuted": True, "editedStubsPreserved": True}
        path = self.root / "summary.json"
        summary = {"backend": "source", "expectedCases": 1, "minimumGenerated": 1,
                   "expectedExecutionIds": ["adapter"], "counts": {}, "cases": [row]}
        corpus.write_json(path, summary)
        with patch.object(sys, "argv", ["public_corpus.py", "verify", str(path)]):
            corpus.main()
        for field in ("unsetAdapterFailed", "stubFixtureExecuted", "editedStubsPreserved"):
            with self.subTest(field=field):
                row[field] = False
                corpus.write_json(path, summary)
                with patch.object(sys, "argv", ["public_corpus.py", "verify", str(path)]), self.assertRaises(SystemExit) as failure:
                    corpus.main()
                self.assertEqual(failure.exception.code, 1)
                row[field] = True

    def make_zip(self, name="source.cbl", checksum=None):
        archive = self.root / "input.zip"
        content = b"synthetic source\r\n"
        with zipfile.ZipFile(archive, "w") as zf:
            zf.writestr(name, content)
            zf.writestr("SHA256SUMS.txt", (checksum or hashlib.sha256(content).hexdigest()) + "  " + name + "\n")
        return archive, content

    def test_archive_preserves_bytes_and_checks_identity(self):
        archive, content = self.make_zip()
        with self.assertRaisesRegex(ValueError, "Archive SHA-256 mismatch"):
            corpus.import_archive(archive, self.root / "bad")
        with patch.object(corpus, "ARCHIVE_SHA256", corpus.digest(archive)):
            result = corpus.import_archive(archive, self.root / "imported")
        self.assertEqual(result["verifiedEntries"], 1)
        self.assertEqual((self.root / "imported/source.cbl").read_bytes(), content)

    def test_archive_traversal_and_bad_checksum_are_atomic(self):
        for name, checksum in [("../escape", None), ("source.cbl", "0" * 64)]:
            archive, _ = self.make_zip(name, checksum)
            with patch.object(corpus, "ARCHIVE_SHA256", corpus.digest(archive)), self.assertRaises(ValueError):
                corpus.import_archive(archive, self.root / "bad")
            self.assertFalse((self.root / "bad").exists())

    def test_native_perform_evaluate_and_measurement_separation(self):
        expected = {"display": ["one"], "fields": {"N": "1"}, "signal": "goback"}
        corpus.write_json(self.root / "expected.json", expected)
        self.case["execution"] = {"program": "NATIVE", "fixture": {}, "expectedFile": "expected.json",
                                  "expectationKind": "independent-calculation"}
        self.save([self.case])
        result = corpus.evaluate(self.lock, self.root, self.root / "out", build=True)
        self.assertEqual(result["counts"]["strictGenerated"], 1)
        self.assertEqual(result["counts"]["built"], 1)
        self.assertEqual(result["counts"]["fixtureExecuted"], 1)
        self.assertEqual(result["counts"]["matchedIndependentExpectation"], 1)
        self.assertEqual(result["counts"]["stubFixtureExecuted"], 0)
        self.assertEqual(result["counts"]["matchedOriginalCobol"], 0)

    def test_external_scaffold_is_not_counted_as_execution(self):
        self.source.write_text('IDENTIFICATION DIVISION. PROGRAM-ID. EXTERNAL.\n'
                               'PROCEDURE DIVISION.\nCALL "VENDOR".\nGOBACK.\n')
        self.case["sha256"] = corpus.digest(self.source)
        self.save([self.case])
        result = corpus.evaluate(self.lock, self.root, self.root / "out", build=True)
        row = result["cases"][0]
        self.assertTrue(row["strictGenerated"])
        self.assertFalse(row["scaffoldGenerated"])
        self.assertTrue(row["built"])
        self.assertTrue(row["uneditedExecutionFailed"])
        self.assertFalse(row["fixtureExecuted"])
        self.assertFalse(row["matchedOriginalCobol"])
        deps = corpus.read_json(self.root / "out/native-1/strict/reports/dependencies.json")
        self.assertIn("VENDOR", json.dumps(deps))
        self.assertIn("copyStack", json.dumps(deps))


if __name__ == "__main__":
    unittest.main(verbosity=2)
