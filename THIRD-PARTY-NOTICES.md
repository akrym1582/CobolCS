# Third-party notices

## CobolTS

Upstream: https://github.com/akrym1582/CobolTs
Pinned commit: `3e42de61fa8e0e7747e37cb17a48732ac962e38d`.
MIT, Copyright (c) 2026 akrym1582. License: [LICENSE](LICENSE).

`python/frontend/*.py` and `vendor/pythonbol/` were adopted from that commit.
The C# frontend in `src/CobolCS.Frontend/Parsing` ports the CobolCS frontend
at `0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`, including later CobolCS changes,
under the same MIT license. The retired Python implementation is preserved in Git
history at that baseline commit. Frozen IR and hashes remain in tests/migration;
no Python sources are shipped, built, or invoked.
The AMOUNT, BRANCH, TABLE and FLOW COBOL examples and AMOUNT expected result are
adopted from the same commit. Other example expectations are derived from the
documented portable test rules. No Fujitsu corpus or upstream test suite is copied.

The TypeScript CLI, compiler and runtime are design references only. CobolCS's
C# CLI, generator, exact rational arithmetic, value storage and iterative execution
runtime are newly implemented here. No support list or corpus pass rate is inherited.

## PythonBol-Translator

Upstream: https://github.com/IronAdamant/PythonBol-Translator
Pinned commit: `2b53472b9d884760ff09be2753472442b099d85a`.
MIT, Copyright (c) 2026 IronAdamant.
Original license: [vendor/pythonbol/LICENSE](vendor/pythonbol/LICENSE).
Adoption metadata: [vendor/pythonbol/UPSTREAM.json](vendor/pythonbol/UPSTREAM.json).

The active frontend uses `src/CobolCS.Frontend/PictureParser.cs` and `PictureClause.cs`, which
are C# ports of that pinned PIC parser and its result model, under the same MIT
license. The upstream license is also shipped as `licenses/PythonBol.txt`. Frozen PIC expectations
in `tests/migration/pictures.json` were produced by the pinned upstream parser.
The other vendored parser/preprocessor modules were removed; their original
source and provenance remain available at the pinned commits.
Python code generation, Python runtime adapters and MCP server are not used.
The historical empty package initializer was added by CobolTS.

Generated projects include the new C# runtime and generated C# source (or the
explicit interpreter backend), with CobolCS's MIT license. They do not include or require Python sources.

## StyleCop Analyzers

StyleCop.Analyzers `1.2.0-beta.556` (and its pinned implementation dependency
StyleCop.Analyzers.Unstable `1.2.0.556`) is a development-only NuGet dependency.
It analyzes repository and generated C# projects during builds; it is not a
runtime component of the generated programs. License: MIT.
Upstream: https://github.com/DotNetAnalyzers/StyleCopAnalyzers
