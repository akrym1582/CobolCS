# C#移行の基準と進捗

2026-10-11。[移行計画](csharp-only-implementation-plan.md)に沿って、解析器本体、CLI接続、108回帰、補助ツールをC#へ移植した。
**製品・開発・CIからPython依存を撤去した。** 検証対象はユーザー指定によりLinuxのみ。

## 固定基準と由来

- 基準コミット: PR #8のマージ `0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`。
- ブランチ: `feat/csharp-only`。基準採取はDebian 13 x86_64、.NET SDK `10.0.401`、Python `3.12.14`。
- IR `schemaVersion: 2.2`。旧版 `frontendVersion: 0.2.2`、C#版 `0.3.0-csharp`。
- [基準マニフェスト](../tests/migration/baseline.json)のソース・入力・lock・期待IR・ハッシュは変更していない。
- [108関数の対応表](../tests/migration/test-mapping.md)でC# MSTestへの移植先を追跡できる。
- 削除したPython実装、テスト、ツール、vendorコードは基準コミットのGit履歴から参照できる。採用元のSHAとMITライセンスは`vendor/pythonbol`と[third-party notices](../THIRD-PARTY-NOTICES.md)に残す。
- 過去の測定は`tests/migration/results/`と`results/native-parser/`。撤去後の測定は`results/csharp-only/`へ分けて保存する。

## 製品と補助ツール

`FrontendAnalyzer.Analyze(input, configuration, copybookPaths, allowMissingCopy)`は.NET内で完全IRの`JsonObject`を返す。
入力列挙、前処理、COPY、宣言、式、手続き、FILE・SQLの解析を行い、外部プロセスやPythonへのフォールバックを使わない。
CLIの`analyze`／`translate`はこのAPIを直接呼ぶ。既存の生成器とランタイムの対応範囲を維持する。

`Parsing/Parser.*`、`SourceFrontend.*`は元の解析規則を機能別に保持する。共有IR構築値`V`／`Values`は参照共有、列挙順、Unicodeスカラー位置、数値を保持し、JSON変換を出力境界で行う。
`PictureParser`／`PictureClause`は固定採用元のPIC解析とモデルのC#移植である。

補助ツールは`tools/CobolCS.Tools`へ統合した。

| コマンド | 用途 |
| --- | --- |
| `fetch --lock FILE --root DIR` | コミット、ライセンス、入力・COPYハッシュを固定して取得 |
| `import-archive ZIP --out DIR` | 提供ZIPの固定識別子、パス、チェックサムを検証して展開 |
| `evaluate --lock FILE --root DIR --out DIR --build` | 解析、生成、ビルド、独立期待値を別々に測定 |
| `verify SUMMARY` | 生成下限、全生成物のビルド、必要な実行・アダプタ証跡を検証 |
| `report --input DIR --before SUFFIX --after SUFFIX --out DIR --markdown FILE` | 原文を含まないJSONと集計文書を作成 |
| `verify-remaining18 BEFORE AFTER` | 歴史的な同一171入力・18件回復の証跡を比較 |
| `prepare-workflow --out DIR` | PAYMENTの独立合成シナリオとレビューを準備 |
| `prepare-readiness --out DIR` | ARGCALCの正式入力・境界ケースとレビューを準備 |

呼出しは`dotnet run --project tools/CobolCS.Tools --no-build -- <command>`。デモ準備は新規テンプレートまたは同一内容だけを更新し、編集・削除・リンクを拒否する。全対象の検査を終えてから書き込む。
`--include-gpl`は明示選択。GPLの原文を製品やリポジトリへ取り込まない。

旧`COBOLCS_PYTHON`と参照用`COBOLCS_REFERENCE_PYTHON`は廃止し、設定しても参照しない。
配布時は`dotnet publish src/CobolCS.Cli -o <空の配布先>`を使う。以前の配布先への上書きでは、古いPythonファイルまで自動削除しない。

## 固定期待値の比較規則

オブジェクトのキー順だけを無視し、配列順、nullと欠落、空配列、数値の字句、ID、診断・位置、設定ハッシュを比較する。
製品IRの許可差異はルート`/frontendVersion`の`0.2.2`→`0.3.0-csharp`のみ。実装バージョンを明示検査した後、比較するメモリ上の値だけを合わせる。保存した期待IRは変更しない。

- 44基準ケースと51追加境界入力は、固定Python版から採取した完全IRを保存。追加入力のCOPY、設定、期待IRは[manifest](../tests/migration/parser-cases/manifest.json)で固定。
- Unicode、COPY異常、リンク列挙の13ケースは[edge-ir](../tests/migration/edge-ir/manifest.json)へ追加固定。一時ディレクトリの絶対パスが診断へ入るケースだけ`@@ROOT@@`を実際の一時ルートへ展開する。それ以外のパス置換はしない。
- 公開171入力×strict/scaffoldの342条件は[corpus-ir-hashes.json](../tests/migration/corpus-ir-hashes.json)へ固定。原文をコミットせず、完全IRと各トップレベル項目の正準JSON SHA-256を保存する。キーをUnicodeスカラー順に並べ、ASCIIエスケープと空白なしの表記を使う。配列・値・数値表記は変えない。入力とCOPYとlockのハッシュを先に検証する。
- 全example×両backend×両modeの52組では、全生成ファイルと実ファイルの台帳ハッシュを検証。IRの版番号と、それを反映する`manifest.json`の`/artifacts/reports~1ir.json`だけを差異として許可する。
- 旧IRから作った出力をC#版で再生成し、編集済みstubのバイト保持、管理ファイルの編集・削除拒否を検証する。

参照実装自体の再現性検査44件は、参照ソース撤去に伴い終了した。同じ44ケースのC#固定期待値検査は維持している。プロセス検査3件はC#実行ファイルへ置き換えた。旧回帰108件を減らして帳尻を合わせてはいない。

## 実行手順

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
git diff --check
```

Frontend.Testsは189ケース。IntegrationTestsは167ケース。生成物比較53件、移植回帰108件、プロセス検査3件、補助CLIの引数拒否3件を含む。
移植した回帰は`Migrated/`に置き、入力・期待値・内部行列を維持する。`RegressionSupport`は.NETのファイル・プロセス操作と値比較だけを共用し、スクリプト評価は行わない。

```sh
for corpus in course fun bank carddemo db2; do
  dotnet run --project tools/CobolCS.Tools --no-build -- fetch \
    --lock "tests/public-corpus/$corpus.lock.json" --root ".cache/public-cobol-corpus/$corpus"
done
dotnet test tests/CobolCS.Frontend.CorpusTests --no-build
```

取得済みならfetchを省略する。未取得の場合、コーパス試験は失敗する。測定コマンドは[公開コーパス](public-corpus.md)を参照。
`compilerSourceSha256`は`src`下の`.cs`／`.csproj`（bin／objを除く）を相対パス順に連結して計算する。旧測定ではさらに`python`ソースも対象だったため、撤去によりハッシュが変わる。測定を実行したGitコミットと未コミット変更も検証記録で区別する。

CIはLinuxの.NET SDKコンテナでPython実行ファイルがないことを検査し、通常試験と5コーパスの取得・342条件比較・生成・ビルド・期待値検証を実行する。

## 検証の意味

生成・ビルド成功は業務実行成功ではない。独立合成期待値との一致と、元COBOLの実行結果との一致を区別する。
原COBOL実機・富士通固有環境・実接続の同値性は検証していない。対応範囲は[supported-subset](supported-subset.md)のまま維持する。
