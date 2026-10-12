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
CLIの`analyze`／`generate`はこのAPIを直接呼ぶ。旧`translate`は`generate`へ変更した。既存の生成器とランタイムの対応範囲を維持する。

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
C#への移植自体の許可差異はルート`/frontendVersion`の`0.2.2`→`0.3.0-csharp`のみ。実装バージョンを明示検査した後、比較するメモリ上の値だけを合わせる。保存した期待IRは変更しない。

- 44基準ケースと51追加境界入力は、固定Python版から採取した完全IRを保存。追加入力のCOPY、設定、期待IRは[manifest](../tests/migration/parser-cases/manifest.json)で固定。
- Unicode、COPY異常、リンク列挙の13ケースは[edge-ir](../tests/migration/edge-ir/manifest.json)へ追加固定。一時ディレクトリの絶対パスが診断へ入るケースだけ`@@ROOT@@`を実際の一時ルートへ展開する。それ以外のパス置換はしない。
- 公開171入力×strict/scaffoldの342条件は[corpus-ir-hashes.json](../tests/migration/corpus-ir-hashes.json)へ固定。原文をコミットせず、完全IRと各トップレベル項目の正準JSON SHA-256を保存する。キーをUnicodeスカラー順に並べ、ASCIIエスケープと空白なしの表記を使う。配列・値・数値表記は変えない。入力とCOPYとlockのハッシュを先に検証する。
- 移植後のDISPLAY句対応は、旧参照を残した[審査済み差分台帳](../tests/migration/reviewed-corpus-changes.json)で別管理する。DB2の33入力×2モードだけが対象で、旧版52入力×2モードの全ハッシュが元参照と一致することを先に実測した。差は`/programs`、`/diagnostics`とそれらを含むルートだけ。NO ADVANCING文、誤ったwith/no/advancing参照・診断の除去、連動するsymbol IDとtoken所有者の修正以外に構造差がないことを確認した。strict側77文は旧tokenの引用literalをCOBOLの重複引用規則で独立にデコードし、新valuesと照合した。scaffold側は旧valuesから句の誤参照だけを除いて照合した。試験は入力hash・旧hash・新hash・全対象pointerを検査し、対象外は元参照との完全一致を維持する。独立実行の根拠は`DisplayClauseTests`と銀行メニューfixtureの2経路であり、DB2実機動作の証明ではない。
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

CIはPR時のみ、1回のビルド成果物を通常試験と5コーパスの並列ジョブで共有し、Linuxの.NET SDKコンテナでPython実行ファイルがないことを検査し、通常試験と5コーパスの取得・342条件比較・生成・ビルド・期待値検証を実行する。

## 検証の意味

生成・ビルド成功は業務実行成功ではない。独立合成期待値との一致と、元COBOLの実行結果との一致を区別する。
原COBOL実機・富士通固有環境・実接続の同値性は検証していない。対応範囲は[supported-subset](supported-subset.md)のまま維持する。

## 最終結果

Python実行ファイル・ライブラリを含まないDebian 13環境、.NET SDK 10.0.401で全体ビルド・書式検査に成功（警告0、エラー0）。
Frontend 189件、Integration 167件、元の108関数すべて、ランタイム12 assertions、公開コーパス342条件の完全IRハッシュ比較が成功した。失敗・スキップなし。
5コーパス171本が生成・ビルド成功、独立期待値一致3件、編集済みC#スタブ実行1件。
固定Git取得、レポートと過去18件比較、publish後のAMOUNT実行、レビュー済みデモのadoptを.NETだけで検証した。
詳細・環境構成・ソース識別子は[最終検証記録](../tests/migration/results/csharp-only/README.md)と[validation.json](../tests/migration/results/csharp-only/validation.json)を参照。

## CIで判明したプロセス待機の修正

[初回CI](https://github.com/akrym1582/CobolCS/actions/runs/38132894323/job/114450715464)では、統合試験の`TestAdoptionRejectsChangedOrSkippedCase`が300秒でタイムアウトした。
ローカル検証は`MSBUILDDISABLENODEREUSE=1`を親環境に設定していたため、この条件を見逃していた。
設定を外すと、`adopt`内の`dotnet restore`本体が終了しても再利用MSBuildノードが出力パイプを保持して待機することを再現できた。残ったノードだけを終了すると次の処理へ進むことも確認した。
CLIの`ProcessRunner`とToolsの`Workspace.Command`で子プロセスの再利用を無効化し、CIの統合試験は親環境を`MSBUILDDISABLENODEREUSE=0`に固定して再発を検出する。
両方のプロセス境界を直接呼ぶ並列restoreの回帰2件も追加し、片方の設定がもう片方の対策漏れを隠さないようにした。
テストの期待値・タイムアウト上限は変更していない。以前の測定記録は当時の環境とソースに対する結果として保持する。

## CLI整理後の追加回帰

`tests/CobolCS.IntegrationTests/CliTests.cs` で直接cbl入力、既定出力先、短縮オプション、単一／複数プログラム選択、引数エラーを検証します。移植済み回帰108関数のCLI呼出しは新構文に更新し、固定IR・過去の測定値は変更しません。


現在のテスト実装と生成テストは xUnit へ移行しています。過去の MSTest 検証記録は当時の結果です。現在の実行契約は[開発ガイド](development.md#xunit-の実行契約)を参照してください。

---

[ドキュメント案内](README.md)へ戻る。
