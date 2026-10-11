# Deep corpus validation

This evaluation uses Linux and .NET SDK 10.0.401. The baseline compiler is `e0cb67611506ca788f359347690baa99d3a8bd1d`. The user restricted further work to existing files: no additional downloads or package feeds are used.

## Input scope

The pinned inventory contains 6,578 paths and 6,514 distinct input hashes. Cached archives restore 2,525 selected source files; 4,053 entries remain unavailable and are not counted as compiler failures or successful tests. `fetch.sh --cached-only` verifies 5,542 files including supporting material, reports three missing archives and one archive rejected for symlink entries, and writes `INCOMPLETE.json`, not `READY.json`.

The case ledger is [selected.lock.json](../tests/deep-corpus/selected.lock.json). Its SHA-256 is `3eb097c463543616c9f4db8c9f3dfba928310ee403e19e4f2e8901dde2eda086`. Import-time format and PROGRAM-ID classifications are provisional lexical observations, not dialect or validity judgments. Unavailable inputs have no inspected format, encoding or input-kind evidence; their provisional ledger defaults must not be interpreted as observations. Non-UTF-8 bytes are kept unchanged and separately classified. COPY search order remains unresolved unless independently reviewed; missing COPY diagnostics are not proof that an original application is invalid.

## Baseline measurement

All 6,578 analysis checkpoints were collected with a 120-second process-tree timeout per case. Of the 2,525 available inputs, 2,468 commands completed, 38 were classified as encoding-blocked, 16 failed, and three timed out. Only 955 checkpoints report `analyzed=true`; command completion by itself does not establish a successfully analyzed program.

The 16 failures comprise four empty inputs and 12 inputs exceeding the original JSON depth limit. The three timeout cases are `deep-dbae408fe76d48e9`, `deep-0cab6d6df44448ae`, and `deep-0c96e8d26d231082`. The baseline generation pilot covered three rejected NIST cases and produced zero programs. Full baseline generation/build was not performed; generation/build changes must therefore use targeted reproductions rather than an invented whole-corpus before count.

Compiler and evaluator identities, per-case input/config hashes, stage statuses and raw local evidence are retained under ignored `out/deep-before`. The evaluator is frozen separately from the compiler. Its JSON read/write boundary changes with the depth fix; this evaluator version difference is recorded rather than silently reusing incompatible checkpoints.

## Execution evidence and limits

Six individually reviewed MIT fixtures are retained with their full license and provenance. Five ProLeap syntax fixtures use independently specified arithmetic, padding, repetition and branch expectations; some require explicitly documented declarations or initialization. The unchanged banking menu covers exit and invalid-option branches. Two additional independent synthetic cases cover signed 32-bit boundaries and Japanese BMP truncation/padding. These are separate from execution of upstream `.at` containers.

The [starter ledger](../tests/deep-corpus/starter-50-review.json) covers all 50 entries: 45 inspected/hash-verified sources and five unavailable official CMS files. The [autotest ledger](../tests/deep-corpus/autotest-review.json) records four inspected containers and their configuration/oracle limitations. No reference COBOL compiler is installed; no reference-compiler or original-host equivalence is claimed. Stub-containing C# builds do not prove the behavior of the missing implementation.

See [fixes and regressions](deep-corpus-fixes.md), [END PROGRAM case evidence](deep-corpus-end-program.md), and [license review](deep-corpus-license-review.md). Existing 171-source measurements remain a separate population; unavailable optional GPL inputs are not silently added or reported as passing.

## Reproduction

Use a local NuGet configuration containing `<configuration><packageSources><clear /></packageSources></configuration>` and set `RestoreConfigFile` to its absolute path. Required packages must already be cached. Keep warning and audit settings enabled. Set `MSBUILDDISABLENODEREUSE=0` in the parent process; the product and evaluator manage child reuse themselves.

```sh
bash tasks/deep-corpus/fetch.sh --cached-only
dotnet build CobolCS.sln -m:1
dotnet tools/CobolCS.Tools/bin/Debug/net10.0/CobolCS.Tools.dll evaluate-deep --lock tests/deep-corpus/selected.lock.json --root .cache/public-cobol-corpus/deep-available --out out/deep-after --stage analyze --cli src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll --compiler-root . --timeout 120
dotnet tools/CobolCS.Tools/bin/Debug/net10.0/CobolCS.Tools.dll evaluate-deep --lock tests/deep-corpus/selected.lock.json --root .cache/public-cobol-corpus/deep-available --out out/deep-after --stage generate --cli src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll --compiler-root . --timeout 120
dotnet tools/CobolCS.Tools/bin/Debug/net10.0/CobolCS.Tools.dll evaluate-deep --lock tests/deep-corpus/selected.lock.json --root .cache/public-cobol-corpus/deep-available --out out/deep-after --stage build --cli src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll --compiler-root . --timeout 120
dotnet tools/CobolCS.Tools/bin/Debug/net10.0/CobolCS.Tools.dll summarize-deep --lock tests/deep-corpus/selected.lock.json --out out/deep-after
```

Do not rerun extraction over an existing input directory. Keep compiler source/binaries and evaluator source fixed throughout a measurement. A changed identity requires a new output directory. Ranges can be assigned using `--offset` and `--count`; each case retains its own path and checkpoint. Limit aggregate concurrent builds to two, with `-m:1` per build. Raw external source, IR, logs and generated C# remain local and are not publication artifacts.
