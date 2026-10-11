# 実行ワークフロー実装・検証記録

> 移行前の測定記録です。Pythonコマンドと件数は当時の証跡として保存しています。現在の実行手順は [C#移行記録](csharp-migration.md) と [テスト対応表](../tests/migration/test-mapping.md) を参照してください。

基準は `85f20a963507f11b54d1eb2d9e164928c84b02d8`（main、PR #6 マージ後）。
指定された `c899ac8c08ad7ce460eebdbabe6e03c4eeaa0866` を祖先に含み、先行「残る18本」は統合済みです。
開始時に origin を fetch して確認しました。作業ブランチは `feat/typed-execution-workflow`、toolVersion は 0.3.0。
main マージ、公開、デプロイは行っていません。

## 実装

- `SourceBackend.Api.cs` / `Groups.cs`：型付き Input／Optional setup／snapshot／Result／stateless Executor と旧 fixture の runner adapter。
- `SourceBackend.Dependencies.cs` / `Testing.cs`：public 外部契約、internal router、横断 queue の fake、応答の一括検証、編集可能な CALL no-op helper。
- `SourceBackend.Workflow.cs` / `Templates`：C# シナリオ登録、別 MSTest/VSTest プロジェクト、未レビュー初回テスト、review helper、独立 solution と README。
- `Generator.cs`：schema 2、利用者領域の保持・削除保持、旧台帳移行、契約／orphan レポート、直前の変更検出、no-op helper の binding 保護。
- `Adoption.cs` / `Program.cs`：scenario／call-stubs オプションと、stage 内 restore・build・test／TRX・hash 検証後の adopt。
- `SourceSupport.cs`：入力境界の未初期化 Number 拒否。
- `examples/WORKFLOW` / `prepare_workflow_demo.py`：合成 PAYMENT、FILECASE、SQLCASE。管理ソースを変更せずにデモの C# と独立期待値を配置。
- `tests/test_workflow*.py` と CI：公開 API・外部応答・所有権・採用・探索の結合試験。

実 API 名を使ったコードと再現コマンドは [実行ワークフロー](execution-workflow.md) に記載しています。
追加依存は生成 **test project のみ**：Microsoft.NET.Test.Sdk 18.0.1、MSTest.TestFramework 4.0.2、MSTest.TestAdapter 4.0.2。
SDK 10.0.401／Linux で復元・ビルド・VSTest 実行を確認しました。監査無効化・警告の一括抑制・追加 StyleCop 除外はありません。

## 検証結果

| 実行 | 結果 |
|---|---|
| `dotnet build CobolCS.sln -m:1`（初回 restore、以後 `--no-restore`） | 成功、警告0・エラー0 |
| `dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore` | 成功 |
| `dotnet run --project tests/CobolCS.Tests --no-build` | 12 assertions 成功 |
| `python3 tests/regression.py` | 60 件成功、削減・skip なし |
| `python3 tests/test_public_corpus.py` | 12 件成功 |
| `python3 tests/test_remaining18.py` | 20 件成功 |
| `python3 tests/test_workflow.py` | 1 結合試験成功。生成された別 assembly の4ケースも成功 |
| `python3 tests/test_workflow_external.py` | 2 結合試験成功。FILE／SQL／layout の生成3ケース成功 |
| `python3 tests/test_workflow_safety.py` | 3 結合試験成功 |
| `python3 tests/test_workflow_noop.py` | 2 結合試験成功。追加の生成キャンセル試験1ケースも成功 |

追加試験は、同一 facade の成功・失敗・cancel 後の再利用、fake の不足・順序違い・matcher 不一致・余剰、
1000桁・signed/scale・明示0/空文字・PIC N・グループ・多次元表の境界・snapshot 非共有、
READ／WRITE／EOF／FILE STATUS、SQL 0／100／負値／NULL indicator、応答不正を検査します。
既存回帰の EOF 互換・制限メッセージの失敗は修正後に再実行し、60件すべて通過しました。

初回未レビューは `dotnet test` と adopt が失敗します。レビュー済みケースの発見は `--list-tests` でも確認しています。
no-op、skip、0 tests、test 中の staged input 変更では採用を完了せず、source と既存 destination を保護します。
テンプレート削除・編集バイト列、nested obj 除外、未知 schema／管理外ファイル／symlink／大小文字衝突、
旧 manifest 移行、編集済み no-op helper の再束縛拒否、新 program の登録と片方 strict 拒否も検査しています。

探索は既定 Throw と分離し、FILE／SQL・明示 fake・編集済み実装を迂回しません。
no-op 後の計算例外・ループ制限・確定的な応答直後 cancel には途中 trace と原文位置が残ります。
runner の旧 fixture と C# scenario が同じ意味の JSON を返し、CLI wrapper の探索判定も別途確認しています。
数値引数の JSON metadata は浮動小数点にせず、分子／分母の十進文字列で保持します。

## 固定公開コーパス

既存 lock を使い、各 corpus に対して次を実行しました。

```sh
dotnet run --project tools/CobolCS.Tools --no-build -- fetch --lock tests/public-corpus/<corpus>.lock.json --root <temporary-source>
dotnet run --project tools/CobolCS.Tools --no-build -- evaluate --lock tests/public-corpus/<corpus>.lock.json --root <temporary-source> --out <temporary-result> --build
dotnet run --project tools/CobolCS.Tools --no-build -- verify <temporary-result>/summary.json
```

| corpus | 選択 | strict生成 | scaffold生成 | build | 独立期待値一致 | 元COBOL比較 |
|---|---:|---:|---:|---:|---:|---:|
| course | 30 | 5 | 25 | 30 | 3 | 0 |
| fun | 2 | 0 | 2 | 2 | 0 | 0 |
| carddemo | 44 | 0 | 44 | 44 | 0 | 0 |
| bank | 43 | 0 | 43 | 43 | 0 | 0 |
| db2 | 52 | 0 | 52 | 52 | 0 | 0 |
| 合計 | 171 | 5 | 166 | 171 | 3 | 0 |

171 は業務実行・scenario 完走・期待値一致の件数ではありません。fixtureExecuted は3、stubFixtureExecuted は1です。
元 COBOL 実機との比較はありません。測定ごとの compilerSourceSha256 を機械可読記録に保持しています。
コーパス測定後の所有権・metadata の調整は、追加した公開ワークフロー試験で検証しました。

## 独立採用と改修

PAYMENT の期待値は `independent-synthetic`：100.00 − 3.50 = 96.50、DISPLAY は `09650`。
生成結果を正解としてコピーしたものでも、元 COBOL の採取結果でもありません。
CALL 引数・passing mode・返値・呼出し回数・最終状態・終了を検査します。no-op の表示 `10000` は別の探索記録です。

`test_workflow.py` は temp の sibling destination へ adopt し、manifest／reports／bin／obj／TestResults がないこと、
LICENSE／THIRD-PARTY-NOTICES／provenance が残ることを確認します。
元生成先を削除した後で、生成本文の減算を通常の `UserCode/PaymentService.cs` へ抽出し、
同じ public API・tests・baseline のまま `dotnet test` が通ります。
減算を加算へ変えると同じ試験が失敗します。管理 hash による編集拒否はありません。

## 未対応・未検証

実 DB／ホスト／業務ネットワーク、元 COBOL の実機同値性、本番移行、Windows 実行は未検証です。
既存の LINKAGE／PROCEDURE USING、未知 layout、REDEFINES、可変 OCCURS、未対応 alias／制御は対応済みにしていません。
同期の任意アダプターの強制中断と、利用者依存を含む thread safety は保証しません。
レビュー済み期待値の妥当性と元 COBOL の配布権は、人による確認が必要です。

[機械可読検証記録](../tests/workflow-results/verification.json) と同ディレクトリのログに結果を保存しています。
