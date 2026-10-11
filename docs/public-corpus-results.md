# 公開コーパス実測

残る18本への追加実装・同一171本の before/after は [remaining18.md](remaining18.md) を参照。
以下は PR #3 から公開コーパス対応までの履歴であり、新しい測定の before ではない。

通常 C# source backend の固定入力による測定。元 COBOL の実測比較は未実施。

PR #3 基準: `5234e2b84b4e93983c4d7abb0764a44a5df91953`。Current の正確なコンパイラ入力は各 summary の `compilerSourceSha256`。

## PR #3

| set | 対象 | 解析 | strict生成 | scaffold生成 | build | fixture実行 | C#スタブ実行 | 独立期待値一致 | 元COBOL一致 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| course | 30 | 8 | 4 | 4 | 8 | 2 | 0 | 2 | 0 |
| fun | 2 | 1 | 0 | 1 | 1 | 0 | 0 | 0 | 0 |
| carddemo | 44 | 37 | 0 | 37 | 9 | 0 | 0 | 0 | 0 |
| bank | 43 | 33 | 0 | 33 | 1 | 0 | 0 | 0 | 0 |
| db2 | 52 | 49 | 0 | 49 | 47 | 0 | 0 | 0 | 0 |
| total | 171 | 128 | 4 | 124 | 66 | 2 | 0 | 2 | 0 |

## Current

| set | 対象 | 解析 | strict生成 | scaffold生成 | build | fixture実行 | C#スタブ実行 | 独立期待値一致 | 元COBOL一致 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| course | 30 | 27 | 5 | 22 | 27 | 3 | 1 | 3 | 0 |
| fun | 2 | 2 | 0 | 2 | 2 | 0 | 0 | 0 | 0 |
| carddemo | 44 | 41 | 0 | 41 | 41 | 0 | 0 | 0 | 0 |
| bank | 43 | 34 | 0 | 34 | 34 | 0 | 0 | 0 | 0 |
| db2 | 52 | 49 | 0 | 49 | 49 | 0 | 0 | 0 | 0 |
| total | 171 | 153 | 5 | 148 | 153 | 3 | 1 | 3 | 0 |

## 残る生成・ビルド未到達

診断件数は失敗ケース数ではない。解析の error があっても原文付き scaffold を生成できる場合がある。

| set | 原文パス | 診断分類 |
|---|---|---|
| course | COBOL Programming Course #3 - Advanced Topics/Labs/cbl/CBLDB21.cbl | PARSE_ERROR: Unsupported FD record clause (CBLDB21.cbl:26) |
| course | COBOL Programming Course #3 - Advanced Topics/Labs/cbl/CBLDB22.cbl | PARSE_ERROR: Unsupported FD record clause (CBLDB22.cbl:27) |
| course | COBOL Programming Course #3 - Advanced Topics/Labs/cbl/CBLDB23.cbl | PARSE_ERROR: Unsupported FD record clause (CBLDB23.cbl:28) |
| carddemo | app/app-vsam-mq/cbl/COACCT01.cbl | PARSE_ERROR: Expected ., got IS (COACCT01.cbl:2) |
| carddemo | app/app-vsam-mq/cbl/CODATE01.cbl | PARSE_ERROR: Expected ., got IS (CODATE01.cbl:2) |
| carddemo | app/cbl/CBACT01C.cbl | PARSE_ERROR: Unsupported FD record clause (CBACT01C.cbl:85) |
| bank | src/base/cics/cobol/BNK1CCA.cbl | PARSE_ERROR: Expected PROCEDURE, got  (BNK1CCA.cbl:979) |
| bank | src/base/cics/cobol/GETCOMPY.cbl | PARSE_ERROR: Expected PROCEDURE, got  (GETCOMPY.cbl:43) |
| bank | src/base/cics/cobol/INQACCCU.cbl | PARSE_ERROR: Expected PROCEDURE, got  (INQACCCU.cbl:882) |
| bank | src/base/ims/cobol/IBTRAN.cbl | PARSE_ERROR: Expected ., got RECURSIVE (IBTRAN.cbl:3) |
| bank | src/base/ims/cobol/LOADACCT.cbl | PARSE_ERROR: Unsupported FD record clause (LOADACCT.cbl:27) |
| bank | src/base/ims/cobol/LOADCUSA.cbl | PARSE_ERROR: Unsupported FD record clause (LOADCUSA.cbl:27) |
| bank | src/base/ims/cobol/LOADCUST.cbl | PARSE_ERROR: Unsupported FD record clause (LOADCUST.cbl:27) |
| bank | src/base/ims/cobol/LOADHIST.cbl | PARSE_ERROR: Unsupported FD record clause (LOADHIST.cbl:27) |
| bank | src/base/ims/cobol/LOADTSTA.cbl | PARSE_ERROR: Unsupported FD record clause (LOADTSTA.cbl:27) |
| db2 | cobol_mf/checkerr.cbl | PARSE_ERROR: Unresolved or ambiguous reference: sqlca (checkerr.cbl:75) |
| db2 | cobol_mf/tabscont.sqb | PARSE_ERROR: Expected PROCEDURE, got  (tabscont.sqb:260) |
| db2 | cobol_mf/tabspace.sqb | PARSE_ERROR: Expected PROCEDURE, got  (tabspace.sqb:414) |

## 生成スタブの種類

同じケースに複数のスタブがある。これらの数は実装・実行成功数ではない。

| kind | 件数 |
|---|---:|
| acceptInput | 13 |
| arithmetic | 22 |
| display | 196 |
| external | 879 |
| initialValue | 26 |
| initialize | 114 |
| inspectOperations | 81 |
| linkageArguments | 25 |
| move | 2281 |
| nextSentence | 1 |
| search | 3 |
| set88 | 29 |
| setIndex | 130 |
| sort | 1 |
| stringTransfer | 127 |
| unknown | 1083 |
| unresolvedControl | 1 |
| unstringTransfer | 2 |
| unsupportedArithmetic | 38 |
| unsupportedControl | 680 |

再現コマンド、ライセンス、実行の根拠、制限は [public-corpus.md](public-corpus.md) を参照。
