# C#移行の基準と進捗

2026-10-11。[移行計画](csharp-only-implementation-plan.md)に沿って解析器本体をC#へ移植し、製品CLIを直接接続した。
**製品の解析・変換にはPython不要。開発用の既存回帰テスト、参照比較、デモ・コーパス操作のC#化は未完了。**
検証対象はユーザー指定によりLinuxのみ。Windows検証要件とCIジョブは削除した。

## 固定基準

- 基準コミット: PR #8のマージ `0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`。
- ブランチ: `feat/csharp-only`。Debian 13 x86_64、.NET SDK `10.0.401`、参照Python `3.12.14`。
- IR `schemaVersion: 2.2`。Python版 `frontendVersion: 0.2.2`、C#版 `0.3.0-csharp`。
- [基準マニフェスト](../tests/migration/baseline.json)のソース・入力・lock・期待IRとハッシュは変更していない。
- [元の108テストの対応表](../tests/migration/test-mapping.md)は関数単位の追跡表。C#への移植済み件数ではない。
- 既存の基準測定は `tests/migration/results/`、今回の結果は `tests/migration/results/native-parser/` に分けて保存する。

## 実装範囲

`FrontendAnalyzer.Analyze(input, configuration, copybookPaths, allowMissingCopy)` が完全なIRを `JsonObject` で返す。
入力列挙から診断・依存情報まで.NET内で実行し、外部PythonプロセスやPythonへのフォールバックを使用しない。

| Python参照モジュール | C#実装 |
| --- | --- |
| main、models | FrontendAnalyzer、FrontendConfiguration、Parser.models |
| source、conditional、copybooks | SourceFrontend.source / conditional / copybooks |
| declarations、PIC | Parser.declarations、PictureParser、PictureClause |
| expressions | Parser.expressions |
| procedure、control、statements | Parser.procedure / control / statements |
| files、sql | Parser.files / sql |

固定・自由形式、継続行、条件処理、COPY探索・REPLACING・循環・欠落診断、宣言・グループ・OCCURS・88・REDEFINES、式・制御・CALL・FILE・SQLの既存表現を保持する。
内部の共有IR構築値 `V` と `Values` は、参照共有、列挙順、Unicodeスカラー位置、数値を保持する。
Pythonと.NETで異なる大文字化・正規表現・UTF-16位置を明示的に扱う。COBOLの対応範囲は追加していない。

CLIの `analyze` / `translate` は上記APIを直接呼ぶ。製品プロジェクトからPython・vendorソースの同梱を削除し、必要なMITライセンスを配布する。
旧 `COBOLCS_PYTHON` は使用しない。参照テスト専用の `COBOLCS_REFERENCE_PYTHON` は引き続き有効。
配布時は空のディレクトリへpublishする。以前の配布先へ上書きした場合、古いPythonファイルまでは自動削除されない。

## 差分の規則

比較器はRFC 6901 JSON Pointer・入力ID・期待値・実際値を返し、オブジェクトのキー順のみ無視する。
配列順、nullと欠落、空配列、数値の字句、ID、診断・位置、設定ハッシュを比較する。パスの無条件置換は行わない。
**許可差異はルート `/frontendVersion` の `0.2.2` → `0.3.0-csharp` のみ。**
比較前に双方がそれぞれの実装バージョンを名乗ることを明示的に検査する。基準IRのファイルは更新しない。

生成物検査では全example × 両backend × strict/scaffoldの52組を比較する。
`reports/ir.json` の上記バージョンと、それを反映する `manifest.json` の `/artifacts/reports~1ir.json` ハッシュだけを許可する。
両台帳の全ハッシュを実ファイルに照合した後に比較し、その他の生成コード・契約・レポート・台帳は一致を要求する。
Python版で生成した出力をC#版で再生成し、編集済みstubのバイト保持、管理ファイルの編集・削除拒否も検査する。

## テスト構成と再現

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
git diff --check
```

- Frontend.Tests: 231件。従来123件、固定IR・失敗入力44件、追加固定境界51件、Unicode/COPY・リンク列挙等のlive参照13件。
- 追加51入力は元の `tests/test_remaining18.py` の解析入力から採取し、固定Python参照実装で期待IRを生成。[manifest](../tests/migration/parser-cases/manifest.json)で個別の入力・COPY・設定・期待IRのSHA-256を固定。
- `PythonReference` は60件。除外した171件はPythonを呼ばない固定期待値検査。元の108回帰の完全移植を意味しない。
- IntegrationTests: 53件。52組の全生成物比較と移行再生成保護。
- Frontend.CorpusTests: 5テスト、固定171入力 × strict/scaffold = 342回の完全IR比較。入力とCOPYのlockハッシュを検証し、未取得の場合は失敗する。

```sh
dotnet test tests/CobolCS.Frontend.Tests --no-build --filter 'TestCategory!=PythonReference'
python3 tools/public_corpus.py fetch --lock tests/public-corpus/course.lock.json --root .cache/public-cobol-corpus/course
dotnet test tests/CobolCS.Frontend.CorpusTests --no-build --filter FullyQualifiedName~course
```

コーパス名は `course` / `fun` / `bank` / `carddemo` / `db2`。全て取得済みならfilterを外して全件検査できる。
CIはLinuxで通常テストを実行し、コーパスjobで取得後に両解析器比較と製品CLI経由の生成・ビルド検査を実行する。
参照実装が必要な間はPythonを残す。最終撤去前にも現在のC#出力で基準を無条件更新しない。

## 検証記録

従来のPython基準は108回帰成功、ランタイム12 assertions成功、5コーパス171本ビルド成功、独立期待値との実行一致3件。
基準の詳細は [corpus.json](../tests/migration/results/corpus.json) と各summaryに保存している。
今回のC#版の実測結果は `tests/migration/results/native-parser/` を参照。
171本の生成・ビルド成功は業務実行成功を意味しない。原COBOLとの実行同値性は検証していない。

## 残作業

- 元の108テストのテストドライバーと補助ツールをC#へ移し、対応表を更新する。
- `tools/public_corpus.py`、report、verify、デモ準備を `CobolCS.Tools` 等へ移植する。
- 参照検査を固定期待値検査へ引き継いだ後、Python参照ソースとCIのsetup-pythonを撤去する。
- Python未導入の開発環境で、全テスト・全コーパス操作・デモも含めて最終検証する。

製品CLIのC#化と、開発環境全体のPython撤去は別の完了条件として追跡する。
