"""Compare the same 171 locked inputs, beyond the historical generation floors."""
import argparse
import json
from pathlib import Path


def verify(before_root, after_root):
    sizes = {"course": 30, "fun": 2, "carddemo": 44, "bank": 43, "db2": 52}
    incomplete = []
    regressions = []
    recovered = []
    hashes = {"before": set(), "after": set()}
    counts = {}
    for name, size in sizes.items():
        before = json.loads((before_root / name / "summary.json").read_text())
        after = json.loads((after_root / name / "summary.json").read_text())
        for phase, summary in [("before", before), ("after", after)]:
            assert len(summary["cases"]) == size, (phase, name, "case count")
            assert len({r["id"] for r in summary["cases"]}) == size, (phase, name, "duplicate ID")
            assert summary["backend"] == "source" and not summary["includeGpl"]
            assert summary["originalCobolComparison"] == "not-performed"
            hashes[phase].add(summary["compilerSourceSha256"])
        assert before["lockSha256"] == after["lockSha256"], (name, "lock changed")
        old = {r["id"]: r for r in before["cases"]}
        assert set(old) == {r["id"] for r in after["cases"]}
        for row in after["cases"]:
            previous = old[row["id"]]
            assert row["source"] == previous["source"] and row["sha256"] == previous["sha256"]
            assert not row["matchedOriginalCobol"]
            success = (row["strictGenerated"] or row["scaffoldGenerated"]) and row["built"]
            identity = dict(set=name, id=row["id"], source=row["source"], sha256=row["sha256"])
            if not success:
                incomplete.append(identity)
                if previous["built"]:
                    regressions.append(identity)
            elif not previous["built"]:
                recovered.append(identity)
            if row["id"] in after["expectedExecutionIds"]:
                assert row["fixtureExecuted"] and row["matchedIndependentExpectation"]
                assert row["builtWithoutReports"] and row["executedWithoutReports"]
                if "unsetAdapterFailed" in row:
                    assert row["unsetAdapterFailed"] and row["stubFixtureExecuted"] and row["editedStubsPreserved"]
        counts[name] = dict(before=before["counts"], after=after["counts"])
    assert all(len(values) == 1 for values in hashes.values()), "Mixed compiler versions within measurement"
    assert not regressions, regressions
    assert not incomplete, incomplete
    assert len(recovered) == 18, "Expected the original eighteen failures to recover"
    assert sum(v["after"]["fixtureExecuted"] for v in counts.values()) == 3
    assert sum(v["after"]["stubFixtureExecuted"] for v in counts.values()) == 1
    return dict(selected=171, generatedAndBuilt=171, recovered=recovered, regressions=regressions,
                compilerSourceSha256={k: next(iter(v)) for k, v in hashes.items()}, counts=counts,
                originalCobolComparison="not-performed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.before, args.after), indent=2))
