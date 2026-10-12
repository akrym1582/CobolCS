# 型付き実行・シナリオ・回帰テスト・採用

0.3.0 の source backend は public API と編集可能な C# シナリオ、別の MSTest/VSTest プロジェクトを生成します。
この機能と WORKFLOW デモは今回追加したものです。旧 interpreter backend と analyze は対象外です。
元 COBOL の実機同値性、本番移行完了、実 DB／ホスト接続の検証を意味しません。

## PAYMENT を再現する

リポジトリのルートで .NET SDK 10 を使用します。
初回は NuGet restore と脆弱性監査の通信が必要です。監査は無効化しません。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build

CLI=src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll
dotnet "$CLI" generate examples/WORKFLOW/PAYMENT.cbl --output out/payment --backend source --mode scaffold
# 自動生成のcharacterizationテストは未レビューでも実行できます。
dotnet test out/payment/Tests/CobolCS.Generated.Tests.csproj
# 手動の独立期待値も準備します。別ファイルの自動ケースのレビュー状態は保持します。
dotnet run --project tools/CobolCS.Tools --no-build -- prepare-workflow --out out/payment

dotnet run --project out/payment -- PAYMENT --scenario happy-path
dotnet test out/payment/Tests/CobolCS.Generated.Tests.csproj --logger trx
# 復元後の実行例
dotnet test out/payment/Tests/CobolCS.Generated.Tests.csproj --no-restore

# 自動ケースを含む全baselineの独立根拠・reviewReason・hash・reviewedを整えてから採用します。
dotnet "$CLI" adopt out/payment --output adopted/payment
dotnet build adopted/payment/CobolCS.Generated.sln -m:1
dotnet test adopted/payment/Tests/CobolCS.Generated.Tests.csproj --no-restore
dotnet run --project adopted/payment -- PAYMENT --scenario happy-path
```

Toolsの `prepare-workflow` はリポジトリの合成 PAYMENT の入力 hash、PROGRAM-ID、管理ファイル、公開名 mapping を確認します。
生成時テンプレートか、同じデモ内容だけを置換でき、異なる利用者編集・削除・symlink は拒否します。
管理ソースや manifest は変更しません。配置された C# は通常の StyleCop 検査対象です。

期待値は `independent-synthetic`。100.00 − 3.50 = 96.50 を手計算し、PIC `9(3)V99` の表示を `09650` としています。
DISPLAY、最終 GROSS/FEE/NET/RETURN-CODE、Goback、呼出し1回、引数と passing mode を別 assembly のテストで検査します。
実行結果を expected に自動コピーしたものではありません。実機 COBOL の採取結果ではありません。
FILECASE/SQLCASE は `tests/CobolCS.IntegrationTests/Migrated/ExternalWorkflowTests.cs` が公開 fake を使う別の合成例です。実ファイル／DB エミュレーターではありません。

## 公開 API

実際の型名は Identifier と同じ決定的な mapping で生成し、`reports/source-map.json`、生成 README、サンプルの using alias に残します。
PAYMENT の例です。

```csharp
using CobolCS.Generated;
using CobolCS.Runtime;
using Executor = CobolCS.Generated.CPAYMENT_22bee6e6Executor;
using Fake = CobolCS.Generated.CPAYMENT_22bee6e6ScriptedDependencies;
using Input = CobolCS.Generated.CPAYMENT_22bee6e6Input;
using Response = CobolCS.Generated.CPAYMENT_22bee6e6Dependency_s000001Response;

var fake = new Fake();
fake.ExpectDependency_s000001(
    request => request.Target == "FEE-SERVICE" && request.Argument0 == new Number(100),
    new Response("00", DependencyOutcome.Normal, CobolValue.Numeric("3.50"), new Number(0)));
var result = new Executor().Run(new Input { AcceptInputs = new[] { "100.00" } }, fake);
```

Executor 自体は状態を持ちません。Run ごとに内部 Program、State、表示・入力・trace・制限計数を新規作成します。
入力は `AcceptInputs` と試験用 `InitialState`。WORKING-STORAGE を正式な業務引数へ昇格させるものではありません。
Optional は未指定と明示した 0／空文字を区別し、VALUE 初期化の後で PIC 格納規則を適用します。
数値は Number の精密有理数を維持し、`default(Number)` は拒否します。
グループ setup はグループ内の基本項目を型付きでまとめます。表 entry は外側からの全次元の1始まり添字です。
平坦な基本項目 setup とグループ setup の重複も拒否します。snapshot の配列・グループ・trace は実行状態と共有しません。
未知レイアウトは UnknownCobolValue として残り、FILLER は内部状態には保持して名前付き入力から除きます。
固定LINKAGE／PROCEDURE USINGは専用Argumentsへ入力します。REDEFINES、可変 OCCURS、未対応alias、run-unit寿命は対象外です。詳細と新しい合成デモは[実行準備](execution-readiness.md)を参照してください。

外部 interface は CALL／FILE／SQL だけを公開します。内部 mutable State、Unsupported スタブ、編集済み partial Dependencies の公開化は不要です。
external を省略すると従来の partial スタブを使います。fake は全分類を横断する1つの順序 queue を使い、
文・依頼 matcher・未使用応答を検証します。CapturedDependency_* と型付き RequestTrace から外部への送信値を確認できます。

出力は Optional です。未指定出力は旧値を保持します。READ 成功には record、EOF には明示 status が必要で、EOF の record 更新は禁止します。
旧必須 constructor は互換 overload で保持し、EOF の既存レコードと同値の指定だけ受け付けます。異なる値なら移行診断で停止します。
SQL 正常出力は host／indicator を要求し、負の NULL indicator は host を未指定にして旧値を保持します。
SQLCODE 0／100／負値と outcome、SQLSTATE の 00／02／失敗クラスを照合します。WHENEVER は引き続き未対応スタブです。
複数出力は全部の検証後に反映します。

実行エラーは ExecutionException に program、statement、SourceLocation、inner exception を保持します。
OperationCanceledException、StepLimitException、ExecutionTimeoutException は別々に伝播し、途中の trace を例外の Data に保持します。
同期アダプターを強制中断する API ではありません。開始前、Tick、外部呼出し前後で協調確認します。
CLI wrapper は別プロセスに既存の時間制限を適用します。利用者依存の thread safety は保証しません。

## 探索 CALL

```sh
dotnet run --project out/payment -- PAYMENT --scenario noop-explore --call-stubs noop
dotnet "$CLI" run out/payment --program PAYMENT --scenario noop-explore --call-stubs noop
```

既定は Throw。NoOp はその実行だけで、未実装の生成 CALL テンプレートが編集可能な NoOp_* helper を呼びます。
注入 fake／利用者実装の失敗を catch して迂回しません。FILE／SQL／未知型／未対応ブロックは通しません。
参照引数を変更せず、既知の RETURNING に数値0または PIC に従う空白を返し、status は `NOOP` です。
RETURNING と参照引数が重なる場合は推測せず拒否します。
正常 return により NOT ON EXCEPTION や仮値による誤った業務分岐へ進む可能性があります。
PAYMENT の探索表示は `10000` で、手数料 fake を使う `09650` とは異なります。

HasNoOpCalls と NoOpCalls は実際の使用を示し、選択した CallStubMode とは区別します。
CALL ターゲット、引数順・型付き値・passing mode、原文位置を trace と JSON metadata に残し、警告は stderr へ出します。
終了コード0は探索実行の終了だけを意味し、レビュー済み業務 baseline や adopt の必要ケースには使えません。

## 所有権と採用

manifestSchemaVersion 2 は管理 hash、利用者 template hash、program binding を区別します。
Stubs／UserCode／Tests/Cases／Tests/Baselines／Tests/Templates／Tests/review.json／Fixtures は既存内容をバイト単位で保持します。
削除したテンプレートは復活させず、新規 program だけにサンプルを追加します。削除 program の利用者ファイルは orphan として報告します。
契約変更は報告し、古い利用者コードを自動修正しません。旧 schema 無し manifest は旧 hash の検証後に移行します。
将来 schema、管理 hash 不一致、管理外ファイル、symlink、パス逸脱、大文字小文字衝突は拒否します。
全階層の bin／obj／TestResults は管理・採用対象から除きます。入替え直前の hash 再確認で競合編集を検出します。

review.json は programId、caseId、baselineKind、reviewed、callStubMode、hasNoOpCalls、関連ファイルの hash を持ちます。
baseline は sourceHashes、toolVersion、input、fake、expected、reviewReason を保持してください。
分類は independent-synthetic／csharp-characterization／original-cobol-captured。最後の分類には captureEvidence も必要です。
期待値やレビューをテスト実行で書き換えません。characterization は現在の C# 挙動の固定で、正しさの証明ではありません。

adopt は source を変更せず、別 filesystem への直接移動ではなく destination の sibling stage で restore／build／test を実行します。
各 program のレビュー済み非 no-op ケース、case／baseline hash、TRX の実テスト数・Passed・ケース対応を確認します。
0 tests、skip、失敗、未レビュー、途中変更、非空 destination、相互包含 path は拒否します。`--force` はありません。
テスト不一致／未レビューは終了コード1、入力・I/O・build 等のエラーは2です。
生成管理 manifest を採用先に置かず、provenance/cobolcs-origin.json に履歴を残します。

採用後は `.g.cs` の編集・rename が可能で、CobolCS CLI／Python／元 repository／reports／元 COBOL を実行に必要としません。
`tests/CobolCS.IntegrationTests/Migrated/WorkflowTests.cs` は元生成先を削除し、採用先の計算を手書き PaymentService に抽出して同じテストが通ること、
誤った加算へ変えると同じテストが失敗することを確認します。再生成は別ディレクトリへ出して差分を移してください。
採用済み provenance のある場所への generate は拒否します。

LICENSE／THIRD-PARTY-NOTICES と元 COBOL の配布条件を維持してください。全派生コードを自動的に MIT とみなしません。
許可された利用者コード／fixture／baseline にも秘密が入る可能性があるため、配布前に確認してください。
.git、.env、鍵、NuGet.Config は採用対象として拒否します。デモは合成データだけです。

実装基準 SHA、検証件数、固定コーパスと未検証事項は [実装・検証記録](execution-workflow-results.md) を参照してください。


## 実行準備拡張

`reports/execution-readiness.json`（schemaVersion 1）と `.md` は静的な入力・依存・未対応候補です。
到達確認や消費順の保証ではありません。`UserCode/Scenarios/*Scenarios.cs` の入力と、
`*ResponseExamples.cs` の matcher・応答をケースごとに補完します。内部未対応文は `Stubs` で別途実装します。
手動の期待値雛形は初回未設定の `Tests/Templates/*.cs.txt` です。Tests/Casesへ有効化して表示・終了・状態・引数結果・依頼数を検査し、
シナリオ・応答設定もreviewの明示ファイル一覧へ含めます。
期待値をコードで固定する場合、baselineの `expected.codeFile` にhash束縛したテストファイルを示し、値を二重管理しません。
[正式引数、境界規則、最短の実行手順](execution-readiness.md)。

新規sampleはexternalを指定せず編集済みStubsで実行します。明示fakeには別ファイルのsample-scriptedを使用します。
入力、応答設定、使用するStubsもreview.jsonのfilesに含め、変更後は独立した根拠で再レビューしてください。
旧sampleは再生成でも保持し、scriptedの手動追加は[実行準備の移行手順](execution-readiness.md)に従います。

通常の source `generate` は分岐探索と固定MSTestも出力します。未レビューの自動期待値は C# の挙動固定として実行でき、adopt にはレビューが必要です。未記入sampleは `Tests/Templates/` に置きます。出力先・有限予算・手動候補・未到達理由・再生成時の保持は[分岐探索とMSTest](branch-test-generation.md)を参照してください。

`prepare-workflow` / `prepare-readiness` は、未編集の参考テンプレートから手動ケースを有効化できます。ケースを置き換える場合も別ファイルの自動生成ケースとレビュー状態は保持します。新しい自動ケースは未レビューのままなので、デモの独立期待値を準備しただけでadoptが通るとは限りません。各自動baselineも仕様と照合し、理由・hash・reviewedを整えてください。

---

[ドキュメント案内](README.md)へ戻る。
