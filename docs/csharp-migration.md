# C#移行の基準と進捗

2026-10-11。[移行計画](csharp-only-implementation-plan.md)の段階1を実装し、段階2の基盤まで着手。
**計画全体は未完了です。製品CLI、回帰テスト、デモ、コーパス操作にはまだPythonが必要です。**
本記録は従来の検証記録を置き換えません。

## 基準

- 基準コミット: `0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`。PR #8のマージコミット。
- 作業ブランチ: `feat/csharp-only`。基準から作成。
- Debian GNU/Linux 13 (x86_64)、.NET SDK `10.0.401`、Python `3.12.14`。
- IR `schemaVersion: 2.2`、参照実装 `frontendVersion: 0.2.2`。
- [固定マニフェスト](../tests/migration/baseline.json)に基準ソース・入力・lock・期待IRのSHA-256を記録。
- [移行対応表](../tests/migration/test-mapping.md)は元の108テストを関数単位で列挙。C#への移植済み件数ではない。

## 実装した範囲

- `CobolCS.Frontend` ライブラリを追加。設定検証、Python互換の設定シリアライズとハッシュ、上流PIC解析、UTF-8原文と位置計算の基盤。
- `CobolCS.Frontend.Tests` を通常の `dotnet test` に統合。既存のStyleCop・警告エラー化を適用。
- 44入力のPython参照出力、終了コード・標準エラーを固定。全example、解析失敗、異常UTF-8、日本語・非BMP文字、LF/CRLF、27設定を含む。
- PIC 28入力の期待値を固定。上流のenum数値とC#カテゴリ名の対応を明示。ライセンスと由来を追記。
- 比較器は入力ID、RFC 6901 JSON Pointer、期待値、実際値を返す。オブジェクトのキー順のみ無視し、配列順、nullと欠落、空配列、数値の字句、文字列、ID、診断・位置、バージョンを比較する。
- 比較器自体にID・バイト位置・重大度・設定ハッシュ・配列順・数値等の負例を追加。差異の許可リストは空。C#実装がPython実装を名乗るメタデータは追加していない。
- 参照プロセスはテストプロジェクト内だけに配置。引数配列、UTF-8のstdout/stderr、終了コード、timeoutと子プロセス回収を扱う。
- UbuntuのCIに比較テストを追加。既存のPython検査は残す。

`SourceFile` は位置と原文を保持する部品であり、固定形式・継続行・字句分割・COPY・条件処理の解析APIではない。
PIC解析と設定の一部が一致しても、COBOLプログラムをC#だけで解析できる状態ではない。
ライブラリはCLIに接続しておらず、未実装解析の成功扱いやPythonへのフォールバックは追加していない。

## 比較と再現

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
git diff --check
```

参照テストは固定マニフェストとPython・vendorソースのハッシュを検証してから、各入力を2回解析する。
Python実行ファイルは `COBOLCS_REFERENCE_PYTHON` で指定できる。未指定ならLinuxは`python3`、Windowsは`python`。
Pythonを使わない固定期待値の検査は次のコマンドで実行できる。

```sh
dotnet test tests/CobolCS.Frontend.Tests --no-build --filter 'TestCategory!=PythonReference'
```

これは全テストや製品がPythonなしで動くことの証明ではない。通常のCIは参照検査も実行する。
Python撤去後も残すのは比較器・固定期待値・C#機能検査であり、参照プロセスのテストは最終差分検証後に撤去する。
参照実装を復元する際は上記基準コミットを別checkoutへ展開する。現在の実装出力で期待値を自動更新しない。

IRフィールドのパス正規化は行っていない。標準エラーのプロセス改行だけをCRLFからLFへ変換する。
CRLF入力は `.gitattributes` の `-text` で元のバイト列を固定し、異常UTF-8入力にも同じ指定を適用する。
オブジェクト順を無視するIR構造比較とは別に、設定ハッシュの完全一致を検証する。

## 検証結果

基準のビルドは警告0・エラー0、format検査成功、ランタイム12 assertions成功。
Python回帰は60+12+20+1+2+3+2+8 = **108件成功**。
`tests/migration/results/` に基準ビルド、全8スイートと新設テストの出力を保存する。
新設C#検査は **123件成功、失敗0、skip 0**。このうちPython参照カテゴリは47件。
残る76件は固定期待値によるC#検査であり、元の108テストの移植完了を意味しない。

最初のsandbox内ではformatの名前付きパイプと回帰のパッケージ復元が制限された。
通常権限で再実行して成功した結果を上記に記載している。製品不具合や回帰失敗として数えていない。

公開コーパスは固定されたcourse/fun/bank/carddemo/db2の同一lockを使い、既存Pythonツールで基準を再測定する。
今回の再測定は5種すべてfetch/evaluate/verifyの終了コード0。結果は次のとおり。

| コーパス | 選択／ビルド成功 | 独立期待値との実行一致 |
| --- | ---: | ---: |
| course | 30 / 30 | 3 |
| fun | 2 / 2 | 0 |
| bank | 43 / 43 | 0 |
| carddemo | 44 / 44 | 0 |
| db2 | 52 / 52 | 0 |
| 合計 | 171 / 171 | 3 |

[集計・lock/summaryハッシュ](../tests/migration/results/corpus.json)と、各summary原本を保存した。
未編集stubの失敗検査、fixture実行、独立期待値一致、原COBOL比較は元summaryの別項目を保持する。
171件は業務実行の成功数ではなく、原COBOLとの比較一致は0件。

取得・評価・検証コマンドは次のとおり。`CORPUS` は対象名に置き換える。

```sh
python3 tools/public_corpus.py fetch --lock tests/public-corpus/CORPUS.lock.json --root .cache/public-cobol-corpus/CORPUS
python3 tools/public_corpus.py evaluate --lock tests/public-corpus/CORPUS.lock.json --root .cache/public-cobol-corpus/CORPUS --out out/migration-baseline/CORPUS --build
python3 tools/public_corpus.py verify out/migration-baseline/CORPUS/summary.json
```

ユーザー指示により検証対象はLinuxのみとし、Windows用CIジョブとWindows検証要件を削除した。
Python未導入環境、C#解析器を経由した生成物の互換性は未検証。

## Python依存の移行先

| 現在の依存 | 現状／次の移行先 |
| --- | --- |
| `src/CobolCS.Cli/Program.cs` の解析プロセス | 現行のまま。完全IR一致後、C#解析APIへ切り替える |
| `src/CobolCS.Cli/CobolCS.Cli.csproj` のPython同梱 | 現行のまま。CLI切り替え後に削除 |
| `COBOLCS_PYTHON` | 現行の製品設定として存続。撤去時に移行説明が必要 |
| `python/frontend/main.py`、`models.py` | 設定・位置計算の基盤のみC#化。解析器接続・列挙・診断等は未移植 |
| `source.py`、`conditional.py`、`copybooks.py` | 原文保持以外は未移植 |
| `declarations.py`、vendored PIC・モデル | PICのみC#化。宣言の追加処理は未移植 |
| `expressions.py`、`procedure.py`、`control.py`、`statements.py` | 未移植 |
| `files.py`、`sql.py` | 未移植 |
| `tests/*.py` 108テスト | 未移植。関数単位の対応表で追跡 |
| `tools/public_corpus.py`、`public_corpus_report.py`、`verify_remaining18.py` | 未移植。`CobolCS.Tools`へ |
| `tools/prepare_workflow_demo.py`、`prepare_readiness_demo.py` | 未移植。レビュー・編集保護も移す |
| CIのsetup-python、README・docsのPython手順 | 現行のまま。依存撤去後に更新 |
| 新設`PythonReference`テスト | 移行期間専用。最終差分固定後に撤去 |

## 次の作業

1. 固定・自由形式、位置付きトークン、継続行、条件処理、COPY探索・展開を移植し、比較対象を前処理出力へ広げる。
2. データ宣言を移植し、PICの宣言側補正、OCCURS、88、REDEFINES等の意味を保持する。
3. 式・手続き・依存情報を移植し、合成ケースと公開コーパス全体のIRを比較する。
4. C#版の実装バージョンと許可差異を項目単位で定義し、生成物・編集／削除保護・adoptの移行検証後にCLIを切り替える。
5. 元の108テスト、補助ツール、CIを移植し、Pythonを撤去。LinuxのPythonなし環境で最終検証する。

未完了項目は計画原文の段階2〜5に残る。全COBOL解析の差分一致・完全C#化を本記録から推定しない。
