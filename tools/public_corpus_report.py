"""Save source-only measurement summaries and a compact report, never raw COBOL."""

import argparse
from collections import Counter
import json
from pathlib import Path

from public_corpus import read_json, write_json

NAMES = ("course", "fun", "carddemo", "bank", "db2")
KEYS = ("selected", "analyzed", "strictGenerated", "scaffoldGenerated", "built",
        "fixtureExecuted", "stubFixtureExecuted", "matchedIndependentExpectation", "matchedOriginalCobol")


def report(input_root, before_suffix, after_suffix, output, markdown):
    output.mkdir(parents=True, exist_ok=True)
    tables = {}
    failures = []
    for stage, suffix in (("PR #3", before_suffix), ("Current", after_suffix)):
        rows = []
        for name in NAMES:
            directory = input_root / (name + suffix)
            summary = read_json(directory / "summary.json")
            if "counts" not in summary or len(summary["cases"]) != summary["counts"]["selected"]:
                raise ValueError("Incomplete measurement: " + str(directory))
            counts = summary["counts"]
            rows.append((name, counts))
            if stage == "Current":
                for case in summary["cases"]:
                    diagnostics = directory / case["id"] / "analysis/reports/diagnostics.json"
                    if diagnostics.exists():
                        # Diagnostic messages and positions only; do not commit
                        # original source text, tokens, SQL or generated C#.
                        case["diagnosticEvidence"] = [{
                            "code": d["code"], "severity": d["severity"], "message": d["message"],
                            "file": (d.get("source") or {}).get("file"),
                            "line": (d.get("source") or {}).get("start", {}).get("line")}
                            for d in read_json(diagnostics) if d["code"] in {"PARSE_ERROR", "INPUT_DECODE", "COPY_MISSING", "CS_NO_PROGRAMS"}]
                    if not case["built"]:
                        reasons = ", ".join(case.get("buildDiagnosticCodes", [])) or ", ".join(case["diagnosticCounts"]) or "CLI failure; inspect phase logs"
                        primary = next((d for d in case.get("diagnosticEvidence", []) if d["code"] == "PARSE_ERROR"), None)
                        if primary:
                            reasons = f'{primary["code"]}: {primary["message"]} ({primary["file"]}:{primary["line"]})'
                        reasons = reasons.replace("|", "\\|").replace("\n", " ")
                        failures.append((name, case["source"], reasons))
                write_json(output / (name + ".json"), summary)
            else:
                write_json(output / (name + "-pr3.json"), summary)
        tables[stage] = rows
    lines = ["# 公開コーパス実測", "", "通常 C# source backend の固定入力による測定。元 COBOL の実測比較は未実施。", "",
             "PR #3 基準: `5234e2b84b4e93983c4d7abb0764a44a5df91953`。Current の正確なコンパイラ入力は各 summary の `compilerSourceSha256`。", ""]
    for stage, rows in tables.items():
        lines += ["## " + stage, "", "| set | 対象 | 解析 | strict生成 | scaffold生成 | build | fixture実行 | C#スタブ実行 | 独立期待値一致 | 元COBOL一致 |",
                  "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for name, counts in rows:
            lines.append("| " + name + " | " + " | ".join(str(counts[key]) for key in KEYS) + " |")
        totals = {key: sum(counts[key] for _, counts in rows) for key in KEYS}
        lines.append("| total | " + " | ".join(str(totals[key]) for key in KEYS) + " |")
        lines.append("")
    kinds = Counter()
    for name in NAMES:
        summary = read_json(output / (name + ".json"))
        for case in summary["cases"]:
            kinds.update(case.get("stubKinds", {}))
    lines += ["## 残る生成・ビルド未到達", "", "診断件数は失敗ケース数ではない。解析の error があっても原文付き scaffold を生成できる場合がある。", "",
              "| set | 原文パス | 診断分類 |", "|---|---|---|"]
    for name, path, reason in failures:
        lines.append(f"| {name} | {path} | {reason} |")
    lines += ["", "## 生成スタブの種類", "", "同じケースに複数のスタブがある。これらの数は実装・実行成功数ではない。", "", "| kind | 件数 |", "|---|---:|"]
    lines += [f"| {kind} | {count} |" for kind, count in sorted(kinds.items())]
    lines += ["", "再現コマンド、ライセンス、実行の根拠、制限は [public-corpus.md](public-corpus.md) を参照。", ""]
    markdown.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=Path("out"))
    parser.add_argument("--before", default="-pr3")
    parser.add_argument("--after", default="-final")
    parser.add_argument("--out", type=Path, default=Path("tests/public-corpus/results"))
    parser.add_argument("--markdown", type=Path, default=Path("docs/public-corpus-results.md"))
    args = parser.parse_args()
    report(args.input, args.before, args.after, args.out, args.markdown)
