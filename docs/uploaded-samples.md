# 添付ZIPの195本の追加検証

保存用ドラフトPR [#6](https://github.com/akrym1582/CobolCS/pull/6) を先に作成し、その後に添付された原本を検証した。
サンプルCOBOL・COPY・生成C#・原文を含むIR/診断ログはコミットしない。
`tests/public-corpus/optional-gpl.lock.json` は再現に必要なパス・ハッシュ・出典だけを保存する。
GPL分は明示指定したローカル検証対象で、通常CIの5セットには追加しない。

## 原本との照合

- ZIP SHA-256: `ac73454b21c06b221c6f7bcaaa912376f68057ca51400dd6fc39070c3b5a1f40`
- `import-archive` でZIPのパス・重複・symlinkを検査し、同梱 `SHA256SUMS.txt` の上流722ファイルを照合した。
- `catalog.json` の195本と、実ファイルおよび6セットのlockのパス・SHA-256・固定コミットがすべて一致した。
- 非GPL171本は [remaining18.md](remaining18.md) と同じ原本だった。今回の測定は展開したZIP内の原本を直接使用する。
- 探索対象COPYとLICENSEのハッシュも照合した。欠落したベンダーCOPYは補作していない。

## 追加修正

`FIZZBUZZ.CBL` にある `END-ACCEPT` が独立した未知の文になり、`UNSTRING ... INTO A, B` のカンマが参照名として誤解析されていた。
`ACCEPT` の終端をその文の原文範囲に含め、`UNSTRING` の受取項目間のカンマを区切りとして扱うよう修正した。
これにより受取項目・DELIMITER IN・COUNT IN・後続文をIRに保持し、不必要なunknownブロックを除去する。
原本8件で不要な `UNSUPPORTED_STATEMENT` 診断が減少した。`FIZZBUZZ` は2件から0件、`VIZZBUZZ` は1件から0件となった。

自己作成の回帰入力で、明示終端・終端なしのACCEPT、CONSOLE/COMMAND-LINEの入力種別、原文終端位置、UNSTRINGの複数受取項目と付随項目を検査する。
既存のC#実行テストでも `ACCEPT ... FROM CONSOLE END-ACCEPT` がfixture入力を順に消費することを確認する。
`COMMAND-LINE` のホスト入力や `UNSTRING` 自体の実行機能を追加したものではない。

## 最終結果

| セット | 対象 | 通常解析 | strict生成 | scaffold生成 | ビルド |
|---|---:|---:|---:|---:|---:|
| course | 30 | 30 | 5 | 25 | 30 |
| fun | 2 | 2 | 0 | 2 | 2 |
| carddemo | 44 | 44 | 0 | 44 | 44 |
| bank | 43 | 43 | 0 | 43 | 43 |
| db2 | 52 | 51 | 0 | 52 | 52 |
| optional GPL | 24 | 24 | 2 | 22 | 24 |
| 合計 | 195 | 194 | 7 | 188 | 195 |

Db2の `checkerr` はSQLCA欠落のため通常解析を通らず、scaffoldで未知LINKAGEとして復旧する。
strict生成にも外部依存の明示的な契約が含まれるため、strictの本数を完全な実行対応数と見なさない。
既存171本に生成・ビルドの退行はなく、初期の18失敗もすべて解消した状態を維持する。

既存のHELLO・PAYROL00・ADDAMTの3実行は独立期待値に一致し、ADDAMTの編集スタブ保持と、
reports/manifestを除いた単独ビルド・実行も成功した。追加SAMPLEの3条件と合わせ、4プログラム・6条件を期待値と照合した。
この追加SAMPLE実行は集合測定の `fixtureExecuted=3` とは別の `sample-execution.json` に記録する。

.NET build/format、runtime 12 assertions、regression 60件、公開コーパス12件、パーサー20件が成功。
追加修正コミット `7391ac2` の [GitHub CI全6ジョブ](https://github.com/akrym1582/CobolCS/actions/runs/38113218731) も成功した。
最終コンパイラソースSHA-256: `170123540352717e5eba447e9b9c71e8ac09d29a93f78d8f33b63c50af3456c2`。
測定開始時点のHEADは `911a81f`、途中で同一ソースを `7391ac2` に保存したため、各セットのcompilerCommitは両方を含む。
全セットのcompilerSourceSha256一致を検査しており、異なる実装を混ぜた集計ではない。

結果は `tests/public-corpus/results/uploaded-samples/` に保存する。
`summary.json` は原文を除いたケース別結果、`archive-verification.json` は原本照合、
`parser-improvements.json` は8件の診断改善、`sample-execution.json` は追加実行、
`non-gpl-regressions.json` は既存171本の比較、`tests.json` / `ci.json` はテストの証跡である。

## 再現

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project tools/CobolCS.Tools --no-build -- import-archive /path/to/cobolts-public-samples-20261011.zip --out /tmp/cobolcs-samples
dotnet run --project tools/CobolCS.Tools --no-build -- evaluate --include-gpl \
  --lock tests/public-corpus/optional-gpl.lock.json \
  --root /tmp/cobolcs-samples/cobolts-public-samples/upstream/optional-gpl/cobol-unit-test \
  --out out/uploaded/gpl --build
dotnet run --project tools/CobolCS.Tools --no-build -- verify out/uploaded/gpl/summary.json
```

非GPLは `--include-gpl` を外し、既存lockと以下の展開先を組み合わせて同じ `evaluate` / `verify` を実行する。

| lock | 展開先（`upstream/` 以下） |
|---|---|
| course | omp-course |
| fun | ibm-fun |
| carddemo | aws-carddemo |
| bank | ibm-bank-of-z |
| db2 | ibm-db2 |

`evaluate` のログには原文が含まれるため、配布用結果にはコピーしない。出力先は毎回新規ディレクトリを指定する。
NuGet復元・脆弱性監査には通信が必要。最初の制限環境で発生したNU1900は最終測定に混ぜず、監査を無効にせず再実行した。

## 実行範囲と残る制約

GPL24本のうち、`SAMPLE` は `WS-MESSAGE-TYPE` にGREETING・FAREWELL・OTHERを与えた3条件で、
分岐先のメッセージ・PIC幅40の末尾空白・表示なし・fallthrough終了が独立期待値に一致した。
出力本文は保存せず、正規化JSONの期待値/実測SHA-256と一致判定だけを記録する。
`CALLDEMO` は入口で正常終了し、残る22本は文位置付き `NotImplementedException` で停止した。

生成・ビルド成功はサンプルの業務処理が完走したことを意味しない。元COBOLコンパイラとの実行比較は行っていない。
CICS/Db2/IMS/MQ/VSAM、外部CALL、ファイルアダプター、未対応レイアウト・文は明示スタブのままである。
意図的な不正入力の `convert-bad.cbl` / `convert-bad2.cbl` は正常動作の期待対象にしない。
`CONVER2.CBL` は存在しない段落への参照を含み、制御スタブとして残る。
`CALLDEMO` は入口のGOBACKで正常終了しても、後続段落の外部CALLを実行できたことにはならない。

原本とその生成物はそれぞれの上流ライセンスのままで、今回の修正・測定によってライセンスを変更しない。
任意GPL上流は [neopragma/cobol-unit-test](https://github.com/neopragma/cobol-unit-test)、固定コミット `8e2c04fc8cda62fb54df8042c141b9ee43abdc16`。
GPL原文、コメント、生成コード、実行出力本文は通常リポジトリへ追加しない。

---

[ドキュメント案内](README.md)へ戻る。
