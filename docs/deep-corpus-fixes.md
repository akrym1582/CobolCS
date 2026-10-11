# Deep corpus: fixes and evidence

The baseline is `e0cb676`. Evaluation uses .NET 10 on Linux, the pinned input bytes and source/configuration hashes. After the user limited this task to existing files, all work uses local archives and an offline NuGet configuration. Raw external source, IR and generated C# remain in ignored local output directories.

| Cause | Before | Shared implementation and regression | Remaining boundary |
| --- | --- | --- | --- |
| DISPLAY clause boundary | Starter #46 `BANK-MAIN.CBL` treated `WITH` as an unresolved operand | Parse `[WITH] NO ADVANCING` and `END-DISPLAY`; positive/negative synthetic tests and both independently expected menu branches | Capture remains one string per DISPLAY; it does not emulate terminal cursor, newline or echo behavior |
| FD label clause | Legal `LABEL RECORD[S] [IS/ARE] STANDARD` rejected | Preserve STANDARD metadata alongside OMITTED; singular/plural, malformed clause and typed FILE generated-code tests | Does not implement host label bytes. NIST `XXXXX084` remains an unresolved placeholder; this fix does not make those files valid |
| Source-position memory | A 1.5 MB TypeCobol input exhausted the 120-second budget and reached about 6.7 GB RSS | Compact internal positions, shared file/COPY metadata and cached UTF-8 offsets; existing complete IR comparisons plus allocation/Unicode/COPY regressions | Public IR/source positions are unchanged; storage optimization alone did not remove the CPU timeout |
| Updated-field assertion coverage | NET could be absent from generated expectations; unsupported operations lost explicit update destinations | Walk structured IR destinations, fixed groups and arrays; record all update reasons, unknown regions and test ownership. Add UNSTRING, INSPECT, FILE FROM, SORT and SEARCH destinations | Static potential updates are candidates, not proof of execution. Unsupported operations remain explicit stubs |
| Default scenario path | Sample could exercise scripted responses instead of edited Stubs | Default sample constructs the public executor with options; separate scripted scenario explicitly selects the strict fake | Existing edited/deleted files are preserved; old registrations require the documented migration |
| Offline partial extraction | Rejection of every archive could leave no staging data directory | Create the staging directory before extraction and test missing/rejected archives | Incomplete acquisition is explicitly marked INCOMPLETE, never READY |

The arithmetic regression independently sets NET to 9700. After adoption, changing the calculation from subtraction to addition produces 10300 and fails the generated assertion. This checks arithmetic behavior in addition to review hashes. Scenario regressions distinguish edited Stubs (111) from scripted responses (222), check missing/mismatched/excess responses, preserve manual test files and deletions, and reject registration collisions.

The assertion-candidate report links program/symbol IDs, PIC/type/dimensions, statement/source/reason, assertion member and test reference. Retained manual files are marked `manual-existing-tests`, and unresolved values remain `manual-unknown`. Generated templates do not certify independent expected values.

## External-case diagnosis

Nine available sources containing FD STANDARD were checked directly. Four progressed to a subsequent error: `deep-7c115f60d9a18970` and `deep-3644f50f59c23cfc` have ambiguous level-01 records, `deep-8d9a2cebdd20204d` reaches an unsupported varying record clause, and `deep-0fc97fd50e28fa1e` reaches `VALUE`. The other five fail earlier for unrelated reasons. None of these nine is claimed as a newly generated program.

The large-source performance reproduction is `deep-dbae408fe76d48e9`, input SHA-256 `ea35973a2724f06f873cadc15046d61534bbf43f2013bf53994f56b0b779cf76`. It contains 18,576 declarations, including 6,067 level-88 declarations. Its original bytes and the baseline timeout checkpoint are retained locally.

See [license review](deep-corpus-license-review.md), the [starter feasibility ledger](../tests/deep-corpus/starter-50-review.json), and the [autotest review](../tests/deep-corpus/autotest-review.json) for independent-oracle and redistribution boundaries. No reference COBOL compiler is available in this environment; reference-compiler equivalence is not claimed.
