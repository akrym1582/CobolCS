# Completion workflow implementation and verification

## 操作例

```sh
dotnet run --project src/CobolCS.Cli --no-build -- payment.cbl -o out/payment
dotnet run --project src/CobolCS.Cli --no-build -- prepare out/payment
# out/payment/UserCode 内の内部スタブと補助コードを実装する
dotnet run --project src/CobolCS.Cli --no-build -- explore out/payment --program PAYMENT
# Tests/BusinessContracts と Tests/BusinessCases に独立した契約と期待値を追加し、
# UserCode/Business/BusinessRegistration.cs にアダプターを登録する
# Tests/review.json の現在のハッシュと実測を確認し、reviewed を設定する
dotnet run --project src/CobolCS.Cli --no-build -- adopt out/payment -o adopted/payment
dotnet test adopted/payment/Tests/CobolCS.Generated.Tests.csproj
```

`prepare` は静的な作業表を更新し、コードを実行しない。`explore` は現在の編集済み内部実装と補助コードを固定した stage で探索する。外部依存はシナリオの fake を使用する。結果は `reports/test-generation` と `reports/executions` に保存し、`reports/completion-worklist.json` / `.md` に現在の作業案内を出す。元の IR と manifest は採用後のテスト実行には不要。入力・期待値・外部応答・観測結果の契約は `docs/completion-workflow.md` を参照。

## 主な変更ファイル

| 領域 | ファイル |
|---|---|
| 入口、生成、採用 | `src/CobolCS.Cli/Program.cs`, `Generator.cs`, `Adoption.cs`, `SourceBackend.*.cs` |
| 完了探索、証跡、作業表 | `src/CobolCS.Cli/BranchTestGeneration.cs`, `CompletionWorkflow.cs`, `CompletionWorklist.cs`, `StubRoles.cs` |
| 業務契約と比較 | `src/CobolCS.Runtime/Business*.cs`, `IBusinessCaseAdapter.cs`, `src/CobolCS.Cli/BusinessReview.cs` |
| 生成テンプレート | `src/CobolCS.Cli/Templates/Execution*.txt`, `BusinessTests.g.cs.txt`, `Characterization.g.cs.txt` |
| 試験と例 | `tests/CobolCS.IntegrationTests/CompletionWorkflowTests.cs`, `BusinessComparisonTests.cs`, `tests/fixtures/completion/` |
| 利用案内 | `docs/completion-workflow.md` および既存 README/CLI/開発/実行/対応範囲文書 |

## 受入項目の対応

| ID | 実装・確認内容 |
|---|---|
| A1–A3 | 未補完内部文は通常実行で停止する。補完後の `explore` は後続分岐を固定再生し、外部 CALL には fake を使用する。`CompletionToBusinessAdoptionAndRefactoring`。 |
| A4 | 未知関数・間接制御と外部 FILE/SQL は内部扱いしない。`UnknownAndUnsupportedExternalStayBlocked`, `FileAndSqlReplayNeverCallEditedExternalStubs`, `UnknownFunctionsAndIndirectBlocksRemainConservative`。 |
| A5 | 補助コード変更は新 run と新来歴を作り、旧ケース・レビューを保持する。`CompletionToBusinessAdoptionAndRefactoring`。 |
| A6 | static 状態、無限ループ、非決定出力を有限予算と再生比較で選別する。`StaticStateTimeoutAndUnstableReplayAreNotBusinessSuccess`。 |
| A7 | stage 中に元ユーザーファイルを変更すると公開を拒否する。`ConcurrentUserEditRefusesPublication`。 |
| A8 | 同一 run のケース削除を尊重し、ビルド失敗時は新候補のみ破棄する。独立した失敗記録は残す。`CompletionToBusinessAdoptionAndRefactoring`。 |
| B1 | COPY 欠落・未解決レイアウト・既知の必要入力・外部応答を位置と編集先付きで列挙する。2 件の欠落 COPY に別の項目 ID と再生成コマンドを付ける。`MissingCopyAndUnknownLayoutHaveStructuredWorkItems`。未解析プログラムの入力は推測しない。 |
| B2–B3 | 手動 `done` と実測を分離し、古い記録を stale にする。`prepare` はシナリオとスタブを実行しない。`RunFailureEvidenceAndPrepareDoNotExecuteScenarios`。 |
| C1–C2 | 手計算の料金 101 × 0.015 = 1.52 を `specification` ケースに記録し、計算係数の意図的変更が同じ期待値で失敗する。`CompletionToBusinessAdoptionAndRefactoring`。 |
| C3 | 採用後に生成メンバー名を変更し、別サービス実装へ移し、同一ケース・契約バイト列で再実行する。`CompletionToBusinessAdoptionAndRefactoring`。 |
| C4 | 外部呼出し順・要求 payload・未消費呼出しは固定スクリプトで失敗する。`StableExternalOperationsRejectOrderPayloadAndMissingCalls` と採用後テスト。 |
| C5 | 小数の精度、空白、配列順、null/欠落、未知キーを型付き契約で比較する。`ExactTypesPreserveDecimalWhitespaceOrderAndPresence`。 |
| C6 | stale レビュー、必須ケース削除、0 件・skip・未完了の実測を拒否する。`CompletionToBusinessAdoptionAndRefactoring`, `BusinessExecutionGateRejectsZeroSkippedAndIncompleteRecords`。 |
| C7 | 採用先だけで業務テストを再実行し、原ソースと IR に依存しない。`CompletionToBusinessAdoptionAndRefactoring`。 |
| C8 | 再生成でユーザーファイルと削除したテンプレートを保持する。古い出力に探索 API がない場合は理由付きで再生成を要求する。`RegenerationPreservesBusinessFilesHelpersAndDeletedTemplates`。 |

業務期待値は独立した `specification` 1 ケース。受入シナリオで探索が作った C# 挙動固定のケースは初回 2 件、補助コード変更後の新 run で 2 件（計 4 件）。後者を業務上の正解とは扱わない。手計算例には CALL の正常応答と料金の丸めを含む。FILE 正常/EOF/書出しと SQL 正常/no-data は外部 fake の遮断試験で確認する。

## 検証と制約

`dotnet build CobolCS.sln -m:1` は警告・エラー 0。`dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore` と `git diff --check` は成功。Runtime は 16,263 assertions、Frontend は 194 テストが成功した。

Integration は CI と同じ 6 フィルタで実行し、新規の完了探索・比較試験は workflows-b の対象として別途実行した。regression-a は 35/35、regression-b は 25/25、workflows-a は 19/19 が初回から成功。branch の初回 11/12 で落ちた 1 件、workflows-b の初回 131/135 で落ちた 4 件、generation の初回 52/53 で落ちた 1 件は、修正後または個別再実行で全件成功した。新規 13 件は初回 9 件成功、timeout と作業表の分類で落ちた 4 件を修正・単独再実行して全件成功した。したがって 292 件の異なる Integration テストが少なくとも 1 回成功した。初回の全分割実行は成功ではない。

重い SDK 起動が重なると探索予算内のビルドが timeout した。コーパス分割 1 件は試験実行中にコンパイラの `src` を編集したため分割間の compiler source hash が変わり、編集を止めた再試験で成功した。FILECASE の manifest 比較 1 件は再試験 2 回では再現せず、原因未確定の一時的な不一致として残る。比較が再び失敗した場合に項目ごとの差分を出すようテスト診断を改善した。

公開 COBOL コーパスはこの環境に取得されておらず、全件再評価は未実施。合成例は全方言や実サービスとの同値性を証明しない。旧出力に `IsExploration` を持つ実行 API がない場合は、元 COBOL から `generate` し直す。採用後の内部リファクタリングは業務ケースで確かめられる一方、過去の C# characterization ケースには当時の内部スナップショットが残る。

---

[ドキュメント案内](README.md)へ戻る。
