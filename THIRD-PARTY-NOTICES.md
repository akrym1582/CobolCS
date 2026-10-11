# Third-party notices

## CobolTS

Upstream: https://github.com/akrym1582/CobolTs
Pinned commit: `3e42de61fa8e0e7747e37cb17a48732ac962e38d`.
MIT, Copyright (c) 2026 akrym1582. License: [LICENSE](LICENSE).

`python/frontend/*.py` and `vendor/pythonbol/` are copied unchanged from that commit.
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

The active frontend imports `pic_parser.parse_pic` and its model types.
The other vendored parser/preprocessor modules are preserved for reference.
Python code generation, Python runtime adapters and MCP server are not used.
CobolTS added the empty package initializer to isolate imports.

Generated projects include only the new C# runtime and generated instruction
arrays, with CobolCS's MIT license. They do not include or require Python sources.
