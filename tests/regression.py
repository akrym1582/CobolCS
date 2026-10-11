"""Offline end-to-end tests: actual CLI, generated C# build, and runtime behavior."""
import json
import os
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
CLI = ["dotnet", str(ROOT / "src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll")]


class Regression(unittest.TestCase):
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
        self.cli("translate", ROOT / "examples/UNSUPPORTED", "--out", self.output, code=1)
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
        generated = self.output / "Programs/Program0000.cs"
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
        self.cli("translate", source, "--config", config, "--out", self.output, code=1)
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
        self.cli("translate", source, "--out", self.output, code=1)
        self.cli("translate", source, "--out", self.output, "--mode", "scaffold")
        self.cli("check", self.output)
        self.cli("run", self.output, "--program", "TEST", code=2)

    def test_redefines_and_group_moves_refused(self):
        for declarations, body in [("01 N PIC X.\n01 R REDEFINES N PIC X.", "GOBACK."), ("01 G.\n05 N PIC 9.", 'MOVE "1" TO G GOBACK.')]:
            source = self.source(body, declarations)
            self.cli("translate", source, "--out", self.output, code=1)

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
        generated = (self.output / "Programs/Program0000.cs").read_text(encoding="utf-8")
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
