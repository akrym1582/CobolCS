"""Offline, case-isolated CobolCS measurements; only `fetch` uses the network.

Raw inputs and generated reports belong in ignored local directories. A successful
build, a fixture run, and agreement with an independent expectation are distinct.
This evaluator never claims original-COBOL equivalence.
"""

import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import signal
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
CLI = ["dotnet", str(ROOT / "src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll")]
EXTENSIONS = {".cbl", ".cob", ".cobol", ".sqb"}
ARCHIVE_SHA256 = "ac73454b21c06b221c6f7bcaaa912376f68057ca51400dd6fc39070c3b5a1f40"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def confined(root, name):
    """Require a canonical relative path, without symlink escapes."""
    parts = PurePosixPath(name)
    if not name or "\\" in name or parts.is_absolute() or any(p in {"", ".", ".."} for p in name.split("/")):
        raise ValueError("Unsafe relative path: " + name)
    result = root.joinpath(*parts.parts)
    if not result.resolve().is_relative_to(root.resolve()):
        raise ValueError("Path escapes root: " + name)
    return result


def load_cases(lock_path, source_root, include_gpl=False):
    """Validate every selected byte before invoking the converter."""
    lock = read_json(lock_path)
    if lock.get("schemaVersion") != 1:
        raise ValueError("Unsupported lock schema")
    seen = set()
    cases = []
    for case in lock["cases"]:
        cid = case["id"]
        if not re.fullmatch(r"[a-zA-Z0-9_-]+", cid) or cid in seen:
            raise ValueError("Invalid/duplicate case ID: " + cid)
        seen.add(cid)
        if case.get("optionalGpl") or "GPL" in case["license"].upper():
            if not include_gpl:
                continue
        if case["sourceFormat"] not in {"fixed", "free"}:
            raise ValueError("Explicit sourceFormat required: " + cid)
        source = confined(source_root, case["source"])
        if source.suffix.lower() not in EXTENSIONS or digest(source) != case["sha256"]:
            raise ValueError("Source extension/hash mismatch: " + cid)
        for name in case["copybookPaths"]:
            copy_root = confined(source_root, name)
            if not copy_root.is_dir():
                raise ValueError("Missing COPY directory: " + name)
            for candidate in copy_root.rglob("*"):
                if candidate.is_file() and candidate.relative_to(source_root).as_posix() not in case.get("copybookHashes", {}):
                    raise ValueError("Unpinned COPY candidate: " + str(candidate))
        for name, expected in case.get("copybookHashes", {}).items():
            if digest(confined(source_root, name)) != expected:
                raise ValueError("COPY hash mismatch: " + name)
        execution = case.get("execution")
        if execution:
            if execution["expectationKind"] != "independent-calculation":
                raise ValueError("Only independent-calculation expectations supported")
            read_json(confined(lock_path.parent, execution["expectedFile"]))
        cases.append(case)
    if not cases:
        raise ValueError("No selected cases")
    return lock, cases


def command(args, timeout=150):
    """Capture failures and kill the child process tree on POSIX timeouts."""
    process = subprocess.Popen(list(map(str, args)), cwd=ROOT, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, text=True, encoding="utf-8",
                               errors="replace", start_new_session=os.name != "nt")
    timed_out = False
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        timed_out = True
        if os.name == "nt":
            process.kill()
        else:
            os.killpg(process.pid, signal.SIGKILL)
        stdout, stderr = process.communicate()
    return {"exitCode": process.returncode, "timedOut": timed_out,
            "stdout": stdout, "stderr": stderr}


def classification(code):
    """Triage labels only: retain the complete original diagnostic separately."""
    if "COPY" in code or code == "SOURCE_PATH_COLLISION":
        return "copy"
    if code.startswith("INPUT_"):
        return "input"
    if code in {"CS_STORAGE", "CS_PIC", "CS_FLOAT", "CS_GROUP", "CS_MOVE", "CS_MODIFICATION"}:
        return "declaration-layout"
    if code in {"CS_CONTROL", "CS_LOOP"}:
        return "control"
    if code in {"CS_EXTERNAL", "EXTERNAL_DEPENDENCY"}:
        return "external-dependency"
    if code in {"PARSE_ERROR", "CS_NO_PROGRAMS", "UNKNOWN_HEADER", "UNSUPPORTED_STATEMENT", "CS_STATEMENT"}:
        return "syntax"
    return "unclassified"


def apply_adapters(generated, execution, lock_directory):
    """Apply only reviewed C# bodies to exact, hash-pinned generated contracts."""
    stubs = read_json(generated / "reports/stubs.json")
    edits = {}
    for plan in execution.get("adapters", []):
        matches = [s for s in stubs if s["signature"] == plan["signature"]
                   and hashlib.sha256(s["source"]["raw"].encode()).hexdigest() == plan["statementRawSha256"]]
        if len(matches) != 1:
            raise ValueError("Adapter contract is missing or ambiguous")
        stub = matches[0]
        path = confined(generated, stub["file"])
        if not stub["file"].startswith("Stubs/") or path in edits:
            raise ValueError("Duplicate or unmanaged adapter target")
        body = confined(lock_directory, plan["bodyFile"])
        if digest(body) != plan["bodySha256"]:
            raise ValueError("Adapter body hash mismatch")
        code, count = re.subn(r'throw new NotImplementedException\([^\n]*\);',
                             lambda _: body.read_text(encoding="utf-8").strip(), path.read_text(encoding="utf-8"))
        if count != 1:
            raise ValueError("Expected one unimplemented adapter body")
        edits[path] = code
    for path, code in edits.items():
        path.write_text(code, encoding="utf-8")
    return {str(path.relative_to(generated)): digest(path) for path in edits}


def evaluate(lock_path, source_root, output, include_gpl=False, build=False):
    lock, cases = load_cases(lock_path, source_root, include_gpl)
    output.mkdir(parents=True, exist_ok=False)
    summary = {"schemaVersion": 1, "scope": lock.get("scope"), "upstream": lock.get("upstream"),
               "lockSha256": digest(lock_path), "includeGpl": include_gpl,
               "expectedCases": len(cases), "minimumGenerated": lock.get("minimumGenerated", 0),
               "expectedExecutionIds": [case["id"] for case in cases if case.get("execution")],
               "compilerCommit": command(["git", "rev-parse", "HEAD"])["stdout"].strip(),
               "compilerSourceSha256": hashlib.sha256(b"".join(
                   p.relative_to(ROOT).as_posix().encode() + p.read_bytes()
                   for directory in (ROOT / "src", ROOT / "python")
                   for p in sorted(directory.rglob("*"))
                   if p.suffix in {".cs", ".py", ".csproj"} and not {"bin", "obj"}.intersection(p.relative_to(ROOT).parts))).hexdigest(),
               "dotnetVersion": command(["dotnet", "--version"])["stdout"].strip(),
               "backend": "source", "originalCobolComparison": "not-performed", "cases": []}
    for case in cases:
        work = output / case["id"]
        work.mkdir()
        config = work / "config.json"
        write_json(config, {"sourceFormat": case["sourceFormat"]})
        source = confined(source_root, case["source"])
        options = ["--config", config]
        for name in case["copybookPaths"]:
            options += ["--copybook-path", confined(source_root, name)]
        row = {"id": case["id"], "source": case["source"], "sha256": case["sha256"],
               "license": case["license"], "intent": case.get("intent", "unreviewed"),
               "analyzed": False, "analysisHasErrors": None, "strictGenerated": False,
               "scaffoldGenerated": False, "built": False, "fixtureExecuted": False,
               "stubFixtureExecuted": False, "matchedIndependentExpectation": False,
               "matchedOriginalCobol": False, "diagnosticCounts": {}, "categories": {}, "phases": {}}

        def phase(name, args):
            result = command(args)
            write_json(work / (name + ".log.json"), result)
            row["phases"][name] = {k: result[k] for k in ("exitCode", "timedOut")}
            return result

        analyzed = work / "analysis"
        phase("analyze", CLI + ["analyze", source, "--out", analyzed] + options)
        manifest_path = analyzed / "manifest.json"
        if manifest_path.exists():
            row["analyzed"] = read_json(manifest_path)["judgments"]["analyzed"]
            diagnostics = read_json(analyzed / "reports/diagnostics.json")
            row["analysisHasErrors"] = any(d["severity"] == "error" for d in diagnostics)
            row["diagnosticCounts"] = dict(Counter(d["code"] for d in diagnostics))
            row["categories"] = dict(Counter(classification(d["code"]) for d in diagnostics))
            # IR, source spans, copyStack, conditions, operands and dependencies
            # remain intact in the generated reports. No lossy stub inference.
        generated = work / "strict"
        result = phase("strict", CLI + ["translate", source, "--out", generated, "--backend", "source", "--mode", "strict"] + options)
        row["strictGenerated"] = result["exitCode"] == 0 and (generated / "CobolCS.Generated.csproj").exists()
        if not row["strictGenerated"]:
            generated = work / "scaffold"
            result = phase("scaffold", CLI + ["translate", source, "--backend", "source", "--mode", "scaffold", "--out", generated] + options)
            row["scaffoldGenerated"] = result["exitCode"] == 0 and (generated / "CobolCS.Generated.csproj").exists()
        stubs_path = generated / "reports/stubs.json"
        if stubs_path.exists():
            stubs = read_json(stubs_path)
            row["stubKinds"] = dict(Counter(s.get("statement", {}).get("kind", s.get("kind", "unknown")) for s in stubs))
            row["stubCount"] = len(stubs)
        if build and (row["strictGenerated"] or row["scaffoldGenerated"]):
            result = phase("build", CLI + ["check", generated])
            row["built"] = result["exitCode"] == 0
            row["buildDiagnosticCodes"] = sorted(set(re.findall(r"error (CS\d+|SA\d+|MSB\d+|NU\d+):", result["stdout"] + result["stderr"])))
            execution = case.get("execution")
            if row["built"] and execution:
                fixture = work / "fixture.json"
                write_json(fixture, execution["fixture"])
                if execution.get("adapters"):
                    failure = phase("before-adapter", CLI + ["run", generated, "--program", execution["program"], "--fixture", fixture])
                    row["unsetAdapterFailed"] = failure["exitCode"] != 0 and execution["expectedUnsetFailure"] in failure["stderr"]
                    edited = apply_adapters(generated, execution, lock_path.parent)
                    mode = "strict" if row["strictGenerated"] else "scaffold"
                    regenerated = phase("regenerate-edited", CLI + ["translate", source, "--backend", "source", "--mode", mode, "--out", generated] + options)
                    row["editedStubsPreserved"] = regenerated["exitCode"] == 0 and all(digest(generated / name) == value for name, value in edited.items())
                    if not row["editedStubsPreserved"]:
                        raise ValueError("Edited adapter preservation failed")
                result = phase("fixture", CLI + ["run", generated, "--program", execution["program"], "--fixture", fixture])
                row["fixtureExecuted"] = result["exitCode"] == 0
                row["stubFixtureExecuted"] = row["fixtureExecuted"] and bool(execution.get("adapters")) and row.get("unsetAdapterFailed", False)
                if row["fixtureExecuted"]:
                    actual = json.loads(result["stdout"])
                    expected = read_json(confined(lock_path.parent, execution["expectedFile"]))
                    row["matchedIndependentExpectation"] = actual == expected
                    portable = work / "portable"
                    shutil.copytree(generated, portable, ignore=shutil.ignore_patterns("bin", "obj", "reports", "manifest.json"))
                    result = phase("standalone-build", ["dotnet", "build", portable / "CobolCS.Generated.csproj", "-m:1", "--nologo"])
                    row["builtWithoutReports"] = result["exitCode"] == 0
                    if row["builtWithoutReports"]:
                        result = phase("standalone-run", ["dotnet", portable / "bin/Debug/net10.0/CobolCS.Generated.dll", execution["program"], fixture])
                        row["executedWithoutReports"] = result["exitCode"] == 0 and json.loads(result["stdout"]) == actual
            elif row["built"] and row.get("stubCount", 0):
                programs = read_json(generated / "manifest.json")["programs"]
                if programs:
                    result = phase("unedited-stub-run", ["dotnet", generated / "bin/Debug/net10.0/CobolCS.Generated.dll", programs[0]])
                    row["uneditedExecutionFailed"] = result["exitCode"] != 0
        summary["cases"].append(row)
        write_json(output / "summary.json", summary)
    keys = ["analyzed", "strictGenerated", "scaffoldGenerated", "built", "fixtureExecuted",
            "stubFixtureExecuted", "matchedIndependentExpectation", "matchedOriginalCobol"]
    summary["counts"] = {key: sum(row[key] for row in summary["cases"]) for key in keys}
    summary["counts"]["selected"] = len(cases)
    write_json(output / "summary.json", summary)
    return summary


def fetch(lock_path, destination, include_gpl=False):
    """Fetch a pinned upstream only; preserve LICENSE and all upstream notices."""
    lock = read_json(lock_path)
    upstream = lock["upstream"]
    if "GPL" in upstream["license"].upper() and not include_gpl:
        raise ValueError("GPL acquisition requires --include-gpl")
    if not re.fullmatch(r"[0-9a-f]{40}", upstream["commit"]):
        raise ValueError("A full pinned commit is required")
    destination.mkdir(parents=True, exist_ok=False)
    for args in (["git", "init", destination],
                 ["git", "-C", destination, "fetch", "--depth=1", upstream["url"], upstream["commit"]],
                 ["git", "-C", destination, "checkout", "--detach", "FETCH_HEAD"]):
        result = command(args)
        if result["exitCode"]:
            raise ValueError(result["stderr"])
    if digest(confined(destination, upstream["licenseFile"])) != upstream["licenseSha256"]:
        raise ValueError("License hash mismatch")
    load_cases(lock_path, destination, include_gpl)


def import_archive(archive, destination):
    """Verify the supplied ZIP identity and safe members before extracting bytes.

    The optional GPL members remain external inputs; this does not select them for
    evaluation or relicense the contents. Existing destinations are never replaced.
    """
    if digest(archive) != ARCHIVE_SHA256:
        raise ValueError("Archive SHA-256 mismatch")
    if destination.exists():
        raise ValueError("Archive destination already exists")
    with zipfile.ZipFile(archive) as zf:
        seen = set()
        for info in zf.infolist():
            name = info.filename.rstrip("/")
            confined(destination, name)
            if name in seen or ((info.external_attr >> 16) & 0o170000) == 0o120000:
                raise ValueError("Duplicate or symlink ZIP entry")
            seen.add(name)
        # Extract to a sibling staging directory so failed verification is not
        # mistaken for a usable imported corpus.
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=destination.parent) as temp:
            stage = Path(temp) / "corpus"
            zf.extractall(stage)
            sums = list(stage.rglob("SHA256SUMS.txt"))
            if len(sums) != 1:
                raise ValueError("Expected one SHA256SUMS.txt")
            count = 0
            for line in sums[0].read_text(encoding="utf-8").splitlines():
                if not line.strip():
                    continue
                match = re.fullmatch(r"([0-9a-f]{64}) [ *](.+)", line)
                if not match or digest(confined(sums[0].parent, match[2])) != match[1]:
                    raise ValueError("SHA256SUMS mismatch: " + line)
                count += 1
            stage.rename(destination)
    return {"archiveSha256": digest(archive), "verifiedEntries": count}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    for name in ("fetch", "evaluate"):
        p = sub.add_parser(name)
        p.add_argument("--lock", type=Path, default=ROOT / "tests/public-corpus/course.lock.json")
        p.add_argument("--root", type=Path, required=True)
        p.add_argument("--include-gpl", action="store_true")
        if name == "evaluate":
            p.add_argument("--out", type=Path, required=True)
            p.add_argument("--build", action="store_true")
            p.add_argument("--verify-expectations", action="store_true")
    p = sub.add_parser("import-archive")
    p.add_argument("archive", type=Path)
    p.add_argument("--out", type=Path, required=True)
    p = sub.add_parser("verify")
    p.add_argument("summary", type=Path)
    args = parser.parse_args()
    try:
        if args.action == "fetch":
            fetch(args.lock.resolve(), args.root.resolve(), args.include_gpl)
        elif args.action == "import-archive":
            print(json.dumps(import_archive(args.archive.resolve(), args.out.resolve())))
        elif args.action == "verify":
            result = read_json(args.summary)
            rows = result["cases"]
            generated = [r for r in rows if r["strictGenerated"] or r["scaffoldGenerated"]]
            expected = set(result["expectedExecutionIds"])
            failed = (result.get("backend") != "source" or len(rows) != result["expectedCases"] or "counts" not in result
                      or len({r["id"] for r in rows}) != len(rows)
                      or len(generated) < result["minimumGenerated"]
                      or any(not r["built"] for r in generated)
                      or any(not r["matchedIndependentExpectation"] or not r.get("executedWithoutReports", False)
                             for r in rows if r["id"] in expected)
                      or any(not r["unsetAdapterFailed"] or not r.get("stubFixtureExecuted", False)
                             or not r.get("editedStubsPreserved", False)
                             for r in rows if "unsetAdapterFailed" in r)
                      or not expected.issubset({r["id"] for r in rows}))
            if failed:
                parser.exit(1, "Corpus build/expectation regression; inspect summary and phase logs\n")
            print("Corpus generation floor, builds and reviewed expectations passed")
        else:
            result = evaluate(args.lock.resolve(), args.root.resolve(), args.out.resolve(), args.include_gpl, args.build)
            print(json.dumps(result["counts"], indent=2))
            if args.verify_expectations:
                _, cases = load_cases(args.lock.resolve(), args.root.resolve(), args.include_gpl)
                expected_ids = {case["id"] for case in cases if case.get("execution")}
                if not expected_ids or any(not row["matchedIndependentExpectation"] for row in result["cases"] if row["id"] in expected_ids):
                    parser.exit(1, "Independent expectations were not all matched\n")
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        parser.exit(2, str(error) + "\n")


if __name__ == "__main__":
    main()
