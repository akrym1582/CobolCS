"""Self-authored parser regressions for the remaining pinned public inputs."""
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from python.frontend.main import Parser
from python.frontend.models import DEFAULT, ParseError
from python.frontend.source import Frontend


class Remaining18(unittest.TestCase):
    def parse(self, source, *, scaffold=False, fixed=False, copies=None, p64=None):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            path = root / "sample.cbl"
            path.write_text(source)
            for name, value in (copies or {}).items():
                (root / name).write_text(value)
            front = Frontend(dict(DEFAULT, sourceFormat="fixed" if fixed else "free", p64=p64), [], root, scaffold)
            program = Parser(front, front.load(path)).parse()
            return program, front

    def test_documentary_apostrophe(self):
        p, _ = self.parse('IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nAUTHOR. James O\'Grady.\n  DATA and PROCEDURE are words here.\nPROCEDURE DIVISION.\nDISPLAY \'KEPT\' GOBACK.')
        self.assertIn("O'Grady", p["documentation"][0]["source"]["raw"])
        self.assertEqual(p["paragraphs"][0]["statements"][0]["kind"], "display")

    def test_documentary_fixed_positions(self):
        text = "       IDENTIFICATION DIVISION.\n       PROGRAM-ID. TEST.\n       AUTHOR. James O'Grady.\n           says DATA and PROCEDURE are words.\n       PROCEDURE DIVISION.\n           DISPLAY 'KEPT' GOBACK.\n"
        p, _ = self.parse(text, fixed=True)
        statement = p["paragraphs"][0]["statements"][0]
        src = statement["source"]
        self.assertEqual((src["start"]["line"], src["start"]["column"]), (6, 12))
        self.assertEqual(text.encode()[src["start"]["byteOffset"]:src["end"]["byteOffset"]].decode(), src["raw"])
        self.assertIn("says DATA", p["documentation"][0]["source"]["raw"])

    def test_literal_escape_and_continuation(self):
        for fixed, body in [(False, "DISPLAY 'O''Brien' GOBACK."), (False, "DISPLAY 'AB&\n&'CD' GOBACK."), (True, "           DISPLAY 'AB\n      -    'CD' GOBACK.")]:
            header = "IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nPROCEDURE DIVISION.\n"
            if fixed:
                header = "".join("       " + line + "\n" for line in header.splitlines())
            p, f = self.parse(header + body, fixed=fixed)
            self.assertEqual(p["paragraphs"][0]["statements"][0]["kind"], "display")
            self.assertNotIn("UNTERMINATED_LITERAL", [d["code"] for d in f.diagnostics])

    def test_unterminated_literal_does_not_swallow_next_line(self):
        p, f = self.parse("IDENTIFICATION DIVISION.\nPROGRAM-ID. TEST.\nPROCEDURE DIVISION.\nDISPLAY 'BROKEN\nDISPLAY 'KEPT' GOBACK.")
        self.assertIn("UNTERMINATED_LITERAL", [d["code"] for d in f.diagnostics])
        self.assertTrue(any(t["text"] == "'KEPT'" for t in p["tokens"]))

    def test_program_attributes(self):
        for declaration, attr in [('TEST IS INITIAL', 'INITIAL'), ('"TEST" RECURSIVE', 'RECURSIVE')]:
            with self.subTest(declaration=declaration):
                p, _ = self.parse(f'IDENTIFICATION DIVISION. PROGRAM-ID. {declaration}. PROCEDURE DIVISION. DISPLAY "KEPT" GOBACK.')
                self.assertEqual(p["id"], "TEST")
                self.assertEqual(p["programDeclaration"]["attributes"], [attr])
                self.assertIn("PROGRAM_LIFECYCLE", [d["code"] for d in p["diagnostics"]])

    def test_program_attribute_rejections_and_end_name(self):
        for declaration in ["TEST IS MYSTERY", "TEST UNKNOWN", "TEST INITIAL RECURSIVE"]:
            with self.subTest(declaration=declaration), self.assertRaises(ParseError):
                self.parse(f"IDENTIFICATION DIVISION. PROGRAM-ID. {declaration}. PROCEDURE DIVISION. GOBACK.")
        p, _ = self.parse('IDENTIFICATION DIVISION. PROGRAM-ID. "TEST" RECURSIVE\nPROCEDURE DIVISION. GOBACK. END PROGRAM TEST.')
        self.assertIn("PROGRAM_ID_PERIOD", [d["code"] for d in p["diagnostics"]])
        with self.assertRaises(ParseError):
            self.parse('IDENTIFICATION DIVISION. PROGRAM-ID. "TEST". PROCEDURE DIVISION. GOBACK. END PROGRAM OTHER.')

    def test_fd_clauses(self):
        p, _ = self.parse('''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.
SELECT F ASSIGN TO "FILE".
DATA DIVISION. FILE SECTION.
FD F LABEL RECORDS ARE OMITTED BLOCK CONTAINS 0 RECORDS
RECORDING MODE IS V RECORD IS VARYING IN SIZE FROM 10 TO 80
DEPENDING ON N DATA RECORD IS REC.
01 REC PIC X(80).
WORKING-STORAGE SECTION. 01 N PIC 99.
PROCEDURE DIVISION. GOBACK.''')
        dep = p["dependencies"][0]
        self.assertEqual(dep["recordingMode"], "V")
        self.assertEqual(dep["file"]["minLength"], 10)
        self.assertEqual(dep["blockClause"]["max"], 0)
        self.assertEqual(len(dep["descriptionClauses"]), 5)

    def fd(self, clauses, records="01 REC PIC X(80)."):
        return self.parse(f'''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL.
SELECT F ASSIGN TO "FILE".
DATA DIVISION. FILE SECTION. FD F {clauses}.
{records}
PROCEDURE DIVISION. GOBACK.''')

    def test_fixed_fd_orders_and_invalid_clauses(self):
        for clauses in ["RECORDING MODE F RECORD CONTAINS 80 CHARACTERS", "RECORD CONTAINS 80 CHARACTERS RECORDING MODE IS F"]:
            p, _ = self.fd(clauses)
            self.assertEqual(p["dependencies"][0]["file"]["maxLength"], 80)
        for clauses in ["RECORDING MODE F RECORDING MODE V", "RECORDING MODE Z", "RECORD CONTAINS 0 CHARACTERS", "RECORD VARYING SIZE 80 TO 10 DEPENDING ON N", "RECORDING MODE F RECORD VARYING SIZE 1 TO 8 DEPENDING ON N", "MYSTERY SOMETHING", "DATA RECORDS ARE REC OTHER", "DATA RECORD IS OTHER"]:
            with self.subTest(clauses=clauses), self.assertRaises(ParseError):
                self.fd(clauses)

    def test_fd_missing_period_preserves_boundary(self):
        for boundary in ["01 REC PIC X.", "PROCEDURE DIVISION. GOBACK."]:
            with self.assertRaises(ParseError):
                self.parse('IDENTIFICATION DIVISION. PROGRAM-ID. TEST. ENVIRONMENT DIVISION. INPUT-OUTPUT SECTION. FILE-CONTROL. SELECT F ASSIGN TO "FILE". DATA DIVISION. FILE SECTION. FD F RECORDING MODE F\n' + boundary)

    def test_missing_linkage_copy(self):
        p, _ = self.parse('''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
DATA DIVISION. LINKAGE SECTION.
COPY "missing.cpy".
01 KNOWN PIC X.
PROCEDURE DIVISION USING MISSING KNOWN. DISPLAY KNOWN GOBACK.''', scaffold=True)
        missing = next(s for s in p["symbols"] if s["name"] == "MISSING")
        self.assertEqual(missing["scope"], "LINKAGE")
        self.assertTrue(missing["stub"])
        self.assertIsNone(missing["pic"])
        self.assertTrue(missing["missingCopyOrigins"])

    def test_using_known_storage_and_unrelated_unknown_are_rejected(self):
        for data, using in [('WORKING-STORAGE SECTION. 01 X PIC X.', 'X'), ('LINKAGE SECTION. 01 X PIC X.', 'TYPO'), ('WORKING-STORAGE SECTION. COPY "missing.cpy".', 'MISSING'), ('LINKAGE SECTION. COPY "missing.cpy".', 'TYPO')]:
            with self.subTest(data=data, using=using), self.assertRaises(ParseError):
                self.parse(f'IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. {data} PROCEDURE DIVISION USING {using}. GOBACK.', scaffold=True)

    def test_provided_copy_replacing_and_ambiguity(self):
        text = 'IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. LINKAGE SECTION. COPY "custom.cpy" REPLACING ==OLD== BY ==ARG==. PROCEDURE DIVISION USING ARG. DISPLAY ARG GOBACK.'
        p, _ = self.parse(text, copies={"custom.cpy": "01 OLD PIC X(3)."})
        self.assertEqual(p["symbols"][0]["name"], "ARG")
        self.assertFalse(p["symbols"][0]["stub"])
        self.assertTrue(any(t["source"].get("replacementOrigins") for t in p["tokens"]))
        with self.assertRaises(ParseError):
            self.parse(text, scaffold=True, copies={"custom.cpy": "01 OLD PIC X.", "CUSTOM.CPY": "01 OLD PIC X."})

    def test_unset_condition_retains_procedure(self):
        p, _ = self.parse('''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
DATA DIVISION. WORKING-STORAGE SECTION.
$IF P64 SET
77 PTR PIC S9(18) COMP-5.
$ELSE
77 PTR PIC S9(9) COMP-5.
$END
PROCEDURE DIVISION. DISPLAY "KEPT" GOBACK.''', scaffold=True)
        self.assertEqual(len([s for s in p["symbols"] if s["name"] == "PTR"]), 1)
        self.assertTrue(p["symbols"][0]["stub"])
        self.assertEqual(p["paragraphs"][0]["statements"][0]["kind"], "display")

    def test_condition_selection_and_procedure_block(self):
        text = '''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
DATA DIVISION. WORKING-STORAGE SECTION.
$IF P64 SET
77 PTR PIC S9(18) COMP-5.
$ELSE
77 PTR PIC S9(9) COMP-5.
$END
PROCEDURE DIVISION.
$IF P64 SET
DISPLAY "64"
$ELSE
DISPLAY "32"
$END
DISPLAY "AFTER" GOBACK.'''
        for value, digits in [(True, 18), (False, 9), (None, None)]:
            p, _ = self.parse(text, p64=value)
            self.assertEqual(p["symbols"][0]["pic"]["digits"] if digits else p["symbols"][0]["pic"], digits)
            body = p["paragraphs"][0]["statements"]
            self.assertEqual(body[0]["kind"], "unknown" if value is None else "display")
            self.assertEqual(body[1]["kind"], "display")
            self.assertEqual(len(p["conditionalDirectives"]), 2)

    def test_nested_and_malformed_conditions(self):
        header = 'IDENTIFICATION DIVISION. PROGRAM-ID. TEST.\nPROCEDURE DIVISION.\n'
        nested = '$IF P64 SET\n$IF P64 SET\nDISPLAY "X"\n$END\n$ELSE\nDISPLAY "Y"\n$END\nGOBACK.'
        for value in [True, False, None]:
            p, _ = self.parse(header + nested, p64=value)
            self.assertEqual(p["paragraphs"][0]["statements"][0]["kind"], "unknown" if value is None else "display")
        for body in ['$END', '$ELSE', '$IF P64 SET\nGOBACK.', '$IF P64 SET\n$ELSE\n$ELSE\n$END']:
            with self.subTest(body=body), self.assertRaises(ParseError):
                self.parse(header + body)

    def test_unknown_declaration_recovery(self):
        p, _ = self.parse('IDENTIFICATION DIVISION. PROGRAM-ID. TEST. DATA DIVISION. WORKING-STORAGE SECTION.\nMYSTERY\n01 N PIC 9.\nMYSTERY\nPROCEDURE DIVISION. DISPLAY N GOBACK.')
        self.assertEqual(p["symbols"][0]["name"], "N")
        self.assertEqual(p["paragraphs"][0]["statements"][0]["kind"], "display")
        self.assertEqual(len([t for t in p["tokens"] if t["ownerId"] == "$unknownDeclaration"]), 2)

    def test_selected_statement_is_not_marked_excluded(self):
        text = '''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
PROCEDURE DIVISION.
$IF P64 SET
IF 1 = 1 DISPLAY "KEPT" END-IF
$END
GOBACK.'''
        p, front = self.parse(text, p64=True)
        selected = next(t for t in p["tokens"] if t["text"] == "IF")
        self.assertFalse(any(e["source"] == selected["source"] for e in front.excluded))

    def test_conditional_children_keep_known_siblings_typed(self):
        text = '''IDENTIFICATION DIVISION. PROGRAM-ID. TEST.
DATA DIVISION. WORKING-STORAGE SECTION.
01 GROUP-A.
05 KNOWN PIC 9.
$IF P64 SET
05 PAD PIC X(4).
$END
05 AFTER-PAD PIC 9.
PROCEDURE DIVISION. DISPLAY KNOWN AFTER-PAD GOBACK.'''
        p, _ = self.parse(text)
        known = next(s for s in p["symbols"] if s["name"] == "KNOWN")
        pad = next(s for s in p["symbols"] if s["name"] == "PAD")
        after = next(s for s in p["symbols"] if s["name"] == "AFTER-PAD")
        self.assertFalse(known["stub"])
        self.assertFalse(after["stub"])
        self.assertEqual(known["parentId"], after["parentId"])
        self.assertEqual(pad["parentId"], known["parentId"])
        self.assertTrue(pad["stub"])
        self.assertIsNone(pad["pic"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
