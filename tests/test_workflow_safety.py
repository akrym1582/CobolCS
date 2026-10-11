"""Manifest ownership and fail-closed adoption checks without external services."""
import json
import pathlib
import tempfile
import unittest
from test_workflow import ROOT, CLI, run


class Ownership(unittest.TestCase):
    def test_ownership_and_paths(self):
        with tempfile.TemporaryDirectory(prefix='cobolcs-ownership-') as temp:
            base = pathlib.Path(temp)
            output = base / 'generated'
            source = ROOT / 'examples/WORKFLOW/PAYMENT.cbl'
            run(*CLI, 'translate', source, '--out', output)
            manifest_path = output / 'manifest.json'
            initial = json.loads(manifest_path.read_text())
            deleted = next((output / 'UserCode/Scenarios').glob('*.cs'))
            deleted.unlink()
            fixture = output / 'Fixtures/bytes.json'
            fixture.parent.mkdir()
            fixture.write_bytes(b'{"inputs":["100.00"]}\r\n')
            nested = output / 'UserCode/nested/obj/cache'
            nested.parent.mkdir(parents=True)
            nested.write_text('discard build artifact')
            run(*CLI, 'translate', source, '--out', output)
            self.assertFalse(deleted.exists())
            run("dotnet", "build", output / "CobolCS.Generated.csproj", "-m:1")
            self.assertEqual(fixture.read_bytes(), b'{"inputs":["100.00"]}\r\n')
            self.assertFalse(nested.exists())
            unknown = output / 'surprise.txt'
            unknown.write_text('preserve')
            before = manifest_path.read_bytes()
            run(*CLI, 'translate', source, '--out', output, code=2)
            self.assertEqual(manifest_path.read_bytes(), before)
            self.assertEqual(unknown.read_text(), 'preserve')
            unknown.unlink()
            link = output / 'UserCode/escape.cs'
            link.symlink_to(source)
            run(*CLI, 'translate', source, '--out', output, code=2)
            link.unlink()
            upper = output / 'Fixtures/A.json'
            lower = output / 'Fixtures/a.json'
            upper.write_text('{}')
            lower.write_text('{}')
            run(*CLI, 'translate', source, '--out', output, code=2)
            upper.unlink()
            lower.unlink()
            manifest = json.loads(manifest_path.read_text())
            manifest['manifestSchemaVersion'] = 99
            manifest_path.write_text(json.dumps(manifest))
            run(*CLI, 'translate', source, '--out', output, code=2)
            manifest_path.write_bytes(before)
            # Schema-less legacy manifests are validated before migration.
            legacy = base / 'legacy'
            run(*CLI, 'translate', source, '--out', legacy)
            old_path = legacy / 'manifest.json'
            old = json.loads(old_path.read_text())
            old.pop('manifestSchemaVersion')
            old.pop('userTemplateHashes')
            old_path.write_text(json.dumps(old))
            stub = next((legacy / 'Stubs').rglob('Dependency_*.cs'))
            stub.write_text(stub.read_text().replace('Implement call', 'User note: Implement call'))
            edited = stub.read_bytes()
            run(*CLI, 'translate', source, '--out', legacy)
            self.assertEqual(stub.read_bytes(), edited)
            self.assertEqual(json.loads(old_path.read_text())['manifestSchemaVersion'], 2)
            protected = base / 'helper-binding'
            run(*CLI, 'translate', source, '--out', protected)
            helper = next((protected / 'Stubs').rglob('NoOp_*.cs'))
            helper.write_text(helper.read_text() + '// User-owned exploration response.\n')
            content = helper.read_bytes()
            changed_source = base / 'PAYMENT.cbl'
            changed_source.write_text(source.read_text().replace('FEE-SERVICE', 'OTHER-FEE'))
            run(*CLI, 'translate', changed_source, '--out', protected, code=2)
            self.assertEqual(helper.read_bytes(), content)


    def test_new_program_and_strict_registration(self):
        with tempfile.TemporaryDirectory(prefix='cobolcs-programs-') as temp:
            base = pathlib.Path(temp)
            source = base / 'sources'
            source.mkdir()
            (source / 'one.cbl').write_text('IDENTIFICATION DIVISION. PROGRAM-ID. ONE. DATA DIVISION. WORKING-STORAGE SECTION. 01 class PIC 9. PROCEDURE DIVISION. DISPLAY "ONE" GOBACK.')
            output = base / 'generated'
            run(*CLI, 'translate', source, '--out', output)
            existing = next((output / 'UserCode/Scenarios').glob('*.cs'))
            existing.write_text(existing.read_text() + '\n// Retain this user edit.\n')
            content = existing.read_bytes()
            (source / 'two.cbl').write_text('IDENTIFICATION DIVISION. PROGRAM-ID. TWO. DATA DIVISION. WORKING-STORAGE SECTION. 01 class PIC 9. PROCEDURE DIVISION. DISPLAY "TWO" GOBACK.')
            run(*CLI, 'translate', source, '--out', output)
            self.assertEqual(existing.read_bytes(), content)
            result = json.loads(run('dotnet', 'run', '--project', output, '--', 'TWO', '--scenario', 'sample').stdout)
            self.assertEqual(result['display'], ['TWO'])
            (source / 'bad.cbl').write_text('IDENTIFICATION DIVISION. PROGRAM-ID. BAD. DATA DIVISION. WORKING-STORAGE SECTION. 01 T PIC X(4). PROCEDURE DIVISION. STRING "A" INTO T END-STRING GOBACK.')
            strict = base / 'strict'
            run(*CLI, 'translate', source, '--out', strict, '--mode', 'strict', code=1)
            manifest = json.loads((strict / 'manifest.json').read_text())
            self.assertEqual(manifest['rejectedPrograms'], ['BAD'])
            self.assertEqual(set(manifest['programs']), {'ONE', 'TWO'})
            result = json.loads(run('dotnet', 'run', '--project', strict, '--', 'ONE', '--scenario', 'sample').stdout)
            self.assertEqual(result['display'], ['ONE'])
            self.assertFalse(any('CBAD_' in str(p) for folder in ['Api', 'Testing', 'Tests/Cases', 'UserCode'] for p in (strict / folder).rglob('*')))
            # COBOL PROGRAM-ID comparison is case-insensitive; duplicates remain rejected.
            (source / 'lower.cbl').write_text('IDENTIFICATION DIVISION. PROGRAM-ID. one. PROCEDURE DIVISION. GOBACK.')
            duplicate = base / 'duplicate'
            run(*CLI, 'translate', source, '--out', duplicate, '--mode', 'strict', code=1)
            self.assertFalse((duplicate / 'CobolCS.Generated.csproj').exists())

    def test_adoption_rejects_changed_or_skipped_case(self):
        with tempfile.TemporaryDirectory(prefix='cobolcs-adoption-') as temp:
            base = pathlib.Path(temp)
            output = base / 'generated'
            run(*CLI, 'translate', ROOT / 'examples/WORKFLOW/PAYMENT.cbl', '--out', output)
            run('python3', ROOT / 'tools/prepare_workflow_demo.py', '--out', output)
            review_path = output / 'Tests/review.json'
            review = json.loads(review_path.read_text())
            review['cases'][0]['hasNoOpCalls'] = True
            review_path.write_text(json.dumps(review))
            run(*CLI, 'adopt', output, '--out', base / 'no-op', code=1)
            self.assertFalse((base / 'no-op').exists())
            review['cases'][0]['hasNoOpCalls'] = False
            case = next((output / 'Tests/Cases').glob('*.cs'))
            original_case = case.read_text()
            case.write_text(case.read_text().replace('[TestMethod]', '[TestMethod]\n    [Ignore("Synthetic skip rejection test")]'))
            import hashlib
            review['cases'][0]['files'][str(case.relative_to(output))] = hashlib.sha256(case.read_bytes()).hexdigest()
            review_path.write_text(json.dumps(review))
            run(*CLI, 'adopt', output, '--out', base / 'skip', code=1)
            self.assertFalse((base / 'skip').exists())
            self.assertTrue((output / 'manifest.json').exists())
            self.assertFalse(list(base.glob('*.stage-*')))
            case.write_text(case.read_text().replace('[TestMethod]\n    [Ignore("Synthetic skip rejection test")]\n    ', ''))
            review['cases'][0]['files'][str(case.relative_to(output))] = hashlib.sha256(case.read_bytes()).hexdigest()
            review_path.write_text(json.dumps(review))
            run(*CLI, 'adopt', output, '--out', base / 'zero-tests', code=1)
            self.assertFalse((base / 'zero-tests').exists())
            mutation = 'Directory.CreateDirectory(Path.Combine(Review.Root(), "Fixtures"));\n        File.WriteAllText(Path.Combine(Review.Root(), "Fixtures/changed.txt"), "changed during test");\n        '
            case.write_text(original_case.replace('Assert.IsFalse(result.HasNoOpCalls);', mutation + 'Assert.IsFalse(result.HasNoOpCalls);'))
            review['cases'][0]['files'][str(case.relative_to(output))] = hashlib.sha256(case.read_bytes()).hexdigest()
            review_path.write_text(json.dumps(review))
            run(*CLI, 'adopt', output, '--out', base / 'changed-during-test', code=2)
            self.assertFalse((base / 'changed-during-test').exists())
            self.assertFalse((output / 'Fixtures/changed.txt').exists())




if __name__ == '__main__':
    unittest.main(verbosity=2)
