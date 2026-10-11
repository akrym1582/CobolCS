# Deep corpus: fixture license review

Reviewed on 2026-10-11. Only the following specific files are approved for redistribution here. Fetching the remaining corpus does not authorize publishing its source, IR, generated C#, or logs containing source.

## Adopted ProLeap fixtures

Origin: <https://github.com/uwol/proleap-cobol-parser/tree/d1bfe75bdd6d480f70c74c6345bcc02610ac30d3>.

The root `LICENSE` grants MIT permission and identifies Copyright (c) 2017 Ulrich Wolffgang. The selected files are project syntax tests under `src/test/resources/io/proleap/cobol/asg/procedure/`, not the separately sourced `gov/nist` suite. Each complete source was inspected: no additional copyright, external attribution, or conflicting terms appear. The archive has no nested LICENSE or NOTICE applicable to these files. The upstream Java tests select TANDEM format; these particular sources use no Tandem-only directives or comments and are explicitly evaluated as free format.

| File under procedure/ | Fixture | Adopted scope and independent oracle |
| --- | --- | --- |
| add/AddToStatement.cbl | proleap/add | Explicit zero initialization plus DISPLAY; integer sums 1, 5, 5 |
| move/MoveToStatement.cbl | proleap/move | DISPLAY appended; two nine-character padded fields and numeric 01 |
| perform/PerformInlineTimes.cbl | proleap/times | Unmodified original; exactly two Test displays |
| ifstmt/IfStatement.cbl | proleap/condition | Missing condition-name declared; true and false paths independently expected |
| compute/ComputeStatementInTable.cbl | proleap/table | Missing groups declared; 12345 / 100 = 123.45, displayed as 12345 (assumed decimal PIC); qualified group test, not OCCURS |

Each fixture retains original bytes as `original.cbl.txt`, the complete MIT license, input and original SHA-256, fixed revision, source path, adoption scope, and changes in `PROVENANCE.json`. `expected.json` is independently authored, not captured from generated C#. C# is generated only in a temporary directory. This does not relicense upstream material under CobolCS's copyright.

The IF and COMPUTE upstream fixtures intentionally omit declarations for parser testing. Their runnable derivatives must not be reported as execution of an unchanged valid upstream application. The ADD derivative initializes items explicitly rather than relying on an unspecified initial value.

## External-only material

NIST sources in ProLeap and opensourcecobol4j require their own provenance review; ProLeap's root MIT is not applied to them. TypeCobol input/output fixtures and the defects suite need file-specific classification, including generated expectations and runtime defects. X-COBOL's dataset license is not a blanket code license. CMS official programs and mirror modifications must be distinguished. None of these unreviewed materials is redistributed by this change.

## Adopted starter-50 banking menu

`fzn0x/core-banking-system` at `770860d4a11f1d203b0369456d867108097a550f`, file `BANK-MAIN.CBL` (starter #46), is retained byte-for-byte under `tests/fixtures/third-party/core-banking/menu/`. Root MIT copyright is `(c) 2026 fzn0x`; the complete source has no third-party attribution or differing terms and the archive contains no nested LICENSE/NOTICE. The fixture retains LICENSE and PROVENANCE. Independent scenarios select `4`, and `9` then `4`, testing exit and invalid-option behavior without choosing a CALL branch. This establishes those branches only, not database/transaction/report correctness.

`tests/deep-corpus/starter-50-review.json` records all 50 execution feasibility decisions separately from evaluation results. The 45 available ZIP sources were hash-verified. Five official CMS inputs were unavailable; their source format, dependencies and execution conditions remain uninspected. Per the user's updated scope, no further network acquisition is performed.

## Autotest review (separate inventory)

`tests/deep-corpus/autotest-review.json` records four inspected opensourcecobol4j `.at` containers, their hashes, enclosing M4 includes, build configuration, compiler commands, encoding, and AT_CHECK interpretation. No upstream body, generated code or log containing it is committed. Two independently authored portable cases cover signed 32-bit boundaries and Japanese BMP truncation/padding; they are not counted as executions of upstream `.at` tests.

The large-integer upstream expectation is internally inconsistent (a negative integer is compared equal to its positive counterpart yet the expected output includes that branch). The UTF-8 PIC N Greek-letter expectation reflects byte capacity rather than CobolCS's portable BMP character count. The UTF-8 suite also literally invokes `cobc -m` and then `java`, unlike its Shift_JIS `cobj` counterpart; that tool configuration needs confirmation before reference execution. These are explicit oracle/environment limitations, not inferred CobolCS failures.
