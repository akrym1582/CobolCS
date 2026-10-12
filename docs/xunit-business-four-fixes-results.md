# xUnit 業務テスト4修正の検証記録

開始点は `origin/migrate-xunit` の `efdd391d27bc57d5802f3674fa72fefd47d19166`（取得日 2026-10-12）、作業ブランチは `feat/xunit-business-test-four-fixes`。開始時の作業ツリーは未変更でした。確認済み SHA より新しいコミットはありませんでした。

Linux / .NET SDK 10.0.401。xUnit 2.9.3、xunit.runner.visualstudio 3.1.5、Microsoft.NET.Test.Sdk 18.0.1、VSTest/TRX を継承し、版・ランナーは追加変更していません。既存の assembly 直列化と xunit.runner.json を維持しました。

共通 Inspect、実 SHA-256、prepare 診断、新規 factory、ケースごとの破棄、空白を含む正確な JSON Pointer と型付き外部応答エラー、個別 Theory、実行 ID ごとの原子的な記録、今回の stage/TRX/内容 hash の採用照合を実装しました。Data は利用者所有で保存・採用し、ケース発見とコンパイルから除外します。Linux の特殊ファイルは読み取り前に statx で通常ファイル種別を確認します。採用後は実装 hash を来歴として保存し、リファクタリングを許します。

ビルドは警告・エラー0件、書式検査と git diff --check は成功。Runtime **26件**、Frontend **194件**、Integration **296件**の全体実行は全件成功、skip 0です。CI と同じ6分割をローカルで実行し、branch 12、regression-a 34、regression-b 26、generation 53、workflows-a 19、workflows-b 152 の計296件について、全体実行の testId と完全一致し、重複・漏れなしを確認しました。新規4件は workflows-b に含まれます。

全体・CI分割検証の後、Data 内の bin/obj/TestResults の保持と JSON Pointer/型付き失敗記録を最終調整し、影響する業務・補完17件を標準のビルド先で再実行して全件成功、skip 0を確認しました。受入 I1〜X3 は下表の試験で成功しています。生成先の実 TRX は三つの Theory 行と集合 Fact を持ち、意図的な比較失敗を中央に置いた **成功→失敗→成功** を確認しました。ケース記録は complete=true/passed=false/passedCases=2、testhost 中断・部分フィルタは incomplete です。

検証成果物は out/verification/summary.json、ci-coverage.json、integration.trx、final-*.trx、runtime-final.trx、frontend-final.trx、business-current.trx に保存しました。実 runner の三ケースと started/summary/ケース別 JSON は out/verification/generated-business/f9d799577e274a36bcfbfa668ea90956/ に保存しています。

| 受入 | 検証する試験・観測 |
|---|---|
| I1 ✓ | FactoriesIsolateStateAndDisposeOnEveryOutcome：同契約三ケースが各々の初期カウンターで成功 |
| I2 ✓ | 同試験：ID の逆順でも結果不変、大文字小文字の異なる ID を区別 |
| I3 ✓ | 同試験：factory 例外・null・重複・契約不一致・実行例外・timeout・取消・NoOp・不完全、非同期破棄を優先し、後続二ケースを実行 |
| T1 ✓ | GeneratedTheoryRowsAndAdoptionUseCurrentExecution：実 runner の順を確認して中央だけ意図的に失敗させ、成功→失敗→成功の三終端、TRX 三行と集合 Fact を検証 |
| T2 ✓ | 同試験：空白・日本語・引用符・同接頭辞 ID、SHA-256 前後半の個別フィルタ、ファイル名変更、完全一致の TRX 対応 |
| T3 ✓ | 同試験の testhost 中断、欠けた終端ファイル、診断 Fact と有効三ケースの継続、状態試験の NoOp、不完全。必須削除は prepare 履歴試験と CompletionToBusinessAdoptionAndRefactoring。既存 BranchGenerationTests は skip/0件等の採用拒否を維持 |
| T4 ✓ | 同試験：別 ID・古い実行・入力編集・終端記録の executionId 改変を拒否 |
| T5 ✓ | 同試験：連続実行と二つの同時プロセスで異なる実行 ID と TRX 保存先を使用し、混在なし |
| R1 ✓ | InspectionCollectsErrorsAndValidatesReferenceBytes：固定した合成 JSON バイトと大文字表現の正しい SHA-256 を照合 |
| R2 ✓ | 同試験：1バイト追加・欠落・不正 hash を診断、strict Load 拒否。生成先の Data 欠落でも prepare は診断を保存 |
| R3 ✓ | 同試験：絶対・ドライブ・UNC・バックスラッシュ・空要素・`.`・`..`・NUL、ファイルリンク・親リンク・FIFO を拒否 |
| R4 ✓ | 同試験と実 runner 試験：Data の JSON はケースとして列挙しない。Data の不正 C# 資料をコンパイルしない |
| R5 ✓ | 実 runner 試験：再生成で Data のバイトと削除を保持、採用先へ持ち出し、元出力を削除した後に検査と業務テストを実行。Data の bin/obj/TestResults 下も資料として保持 |
| P1 ✓ | PreparePreservesRequiredHistoryAndRunResult：正常＋壊れた JSON＋未記入期待値の全診断、有効数1と案内を保存 |
| P2 ✓ | 静的検査試験：未知契約・不正契約依存・重複 ID のファイルと因果関係を表示し、有効件数から除外 |
| P3 ✓ | prepare 履歴試験と CompletionToBusinessAdoptionAndRefactoring：必須削除の診断、履歴保持、strict Load/adopt 拒否 |
| P4 ✓ | prepare 履歴試験：未完成ケースを残した run/explore の正常結果を維持し、案内へ不備を保存 |
| P5 ✓ | RunFailureEvidenceAndPrepareDoNotExecuteScenarios、静的検査と生成先 prepare：実行せず案内を保存。旧登録に具体的な factory 移行診断 |
| P6 ✓ | 管理台帳破損は prepare 履歴試験、編集競合は ConcurrentUserEditRefusesPublication、管理対象とパスは既存 OwnershipTests 等で拒否 |
| X1 ✓ | CompletionToBusinessAdoptionAndRefactoring：生成→補完→explore→review→adopt |
| X2 ✓ | 同試験と実 runner 試験：採用後の生成メンバー・サービス・アダプター内部改名でも固定業務期待値で成功 |
| X3 ✓ | 同試験と実 runner 試験：採用後の業務計算破壊は固定期待値で失敗。期待値の自動更新なし |

R1/R5 の採取形式 fixture は合成の hash 検査です。実 COBOL 処理系で採取した件数は **0**。C# characterization と独立の合成手計算の件数を実機比較の件数へ足していません。ファイル一致は実機実行や期待値との対応を認証しません。

別出力先を使った予備検証では、既存の process-boundary 試験が標準の bin/Debug/net10.0 を前提とするため4件の環境パス不一致が出ました。標準出力先で CI の6分割を実行し直して全件成功を確認しています。最終の17件も標準出力先です。

実行コマンド：

```sh
git fetch origin migrate-xunit
git switch -c feat/xunit-business-test-four-fixes FETCH_HEAD
dotnet restore CobolCS.sln
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet test tests/CobolCS.Tests --no-build --logger 'trx;LogFileName=runtime.trx' --results-directory out/verification
dotnet test tests/CobolCS.Frontend.Tests --no-build --logger 'trx;LogFileName=frontend.trx' --results-directory out/verification
MSBUILDDISABLENODEREUSE=0 dotnet test tests/CobolCS.IntegrationTests --no-build --logger 'trx;LogFileName=integration.trx' --results-directory out/verification
COBOLCS_BUSINESS_VERIFICATION_ROOT="$PWD/out/verification/generated-business" dotnet test tests/CobolCS.IntegrationTests --no-build --filter 'FullyQualifiedName~BusinessFourFixesTests|FullyQualifiedName~CompletionWorkflowTests|FullyQualifiedName~BusinessComparisonTests' --logger 'trx;LogFileName=business-current.trx' --results-directory out/verification
git diff --check
```

生成先・採用先の `dotnet test`、TRX の検証、部分選択、二プロセス、異常終了は上記の Integration 内で実行します。実 runner の三ケースの失敗検出を、成功する親テストの否定試験として照合しています。

公開コーパスの再取得・全評価、実 COBOL 処理系比較、Windows、GitHub 上の CI 実行は未実行です。COBOL 構文、固定 IR、コーパス lock は変更していません。

旧出力はバックアップを保ち、新ディレクトリへ生成して UserCode/Stubs/契約/ケース/Data/レビューの利用者編集を明示移植してください。登録を `IDictionary<string, Func<IBusinessCaseAdapter>>` と `adapters.Add("payment-v1", () => new PaymentAdapter());` に変更します。旧インスタンス登録を黙って共有せず、旧 business-execution.json を個別実行の成功として採用しません。管理台帳の規則を迂回して上書きする移行は追加していません。
