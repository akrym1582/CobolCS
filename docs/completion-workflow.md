# 内部補完・再探索・業務テスト

通常 source generate の初期探索に加えて、編集済み内部 C# を使う `explore` と、コードを実行しない `prepare` を追加しました。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- payment.cbl -I copybooks -o out/payment
dotnet run --project src/CobolCS.Cli --no-build -- prepare out/payment
# reports/completion-worklist.md に従い入力・Stubs・補助コードを編集
dotnet run --project src/CobolCS.Cli --no-build -- run out/payment --scenario sample
dotnet run --project src/CobolCS.Cli --no-build -- explore out/payment --program PAYMENT
dotnet test out/payment/Tests/CobolCS.Generated.Tests.csproj
# 独立した期待値と実装・マッピングのレビュー後
dotnet run --project src/CobolCS.Cli --no-build -- adopt out/payment -o adopted/payment
```

## 再探索の契約

通常 generate は `generated-pristine` の初期スタブを使用し、編集済みユーザーコードを自動実行する入口にはしません。明示的な explore だけが `completed-internal` を使用します。既存の候補・予算・worker・状態検査・二回固定再生・MSTest を共用します。

`reports/stubs.json` の `executionRole` は IR の文・依存・子ノードから分類します。`internal-logic` は内部補完、`external-adapter` は型付き CALL/FILE/SQL、`external-unsupported` は未対応外部効果、`unknown` は分類不能です。後二者は探索時に停止し、ユーザー実装へ落としません。外部アダプターは probe では合成応答、replay では順序付き fake を使い、編集済み外部 Stubs を呼びません。

補助コードは `UserCode/Completion/**/*.cs` に置きます。共通 partial のため Stubs と全 UserCode の必要な C# をコンパイルしますが、scenario・登録処理を探索入口にはしません。コンパイルする利用者ソースのパスと hash を全部記録します。内部補完には外部接続を含めないでください。worker のプロセス分離はセキュリティサンドボックスではありません。

検査→stage 固定コピー→ビルド→有限探索→固定再生→MSTest→原本との編集競合検査→一式公開の順です。ビルド失敗・競合では既存出力を変更しません。不完全な試行は診断として保存し、正常ケースへ昇格させません。MSTest 固定再生に失敗した新規の暫定ケースは公開しません。ユーザー static 状態は worker で隔離し、実際の固定テストで共有状態の影響が出る場合も不完全として拒否します。

内部到達と正常復帰は `InternalStubEvent` に別イベントで残します。COBOL 由来の分岐計測とは別です。手書き C# の分岐網羅率は算出しません。trace 上限を超えると不完全です。合成外部の未処理異常応答は除外として表示し、期待値にはしません。

来歴には実装モード、全利用者ソース hash、元 CBL/COPY、意味設定、契約、探索設定、generator 版を含めます。実装変更時は新 runId になります。旧ケース・期待値・レビューは保持します。同一の完了 run はケースの編集・削除を保持し、削除したものを勝手に再作成しません。未完了 run の再試行履歴も保持します。保持 run の上限は従来どおり 4 です。

```json
{"testGeneration":{"seed":1,"maxAttempts":200,"timeoutMs":120000,"maxTraceEvents":10000,"candidates":[]}}
```

explore の `--config` はこの `testGeneration` だけを受け付けます。既存の設定検査を共用し、意味設定・PIC・方言・モードの変更は再 generate へ案内します。program 省略時は全生成プログラムです。終了 0 は対象の探索・固定再生が完了したこと、1 は不完全・未対応到達・再生不一致、2 は入力／設定／管理対象／ビルドの不正です。全経路網羅や業務正当性を意味しません。

## 次の作業と証跡

`reports/completion-worklist.json` / `.md` は静的な不足と現在のコード・入力に結び付く最新証跡を統合します。構造/COPY不足、入力、外部応答、内部補完、期待値、レビューを分け、原文・宣言・編集先・型・メソッド・確認コマンドと優先最大 5 件を表示します。静的な経路外候補をすべてのケースの必須入力とは扱いません。

`UserCode/Completion/work-items.json` は利用者所有です。

```json
{"schemaVersion":1,"items":[{"itemId":"worklist の ID","status":"in-progress","notes":"確認した条件","assignee":"担当者"}]}
```

手動状態 `open/in-progress/done` と実測 `not-observed/reached-failed/reached-completed/stale` は別です。ファイル変更・コンパイル成功・手動 done だけで実装済みと断定しません。元ソースの対応が変わった手動メモは orphaned/stale として残し、別の文へ移植しません。入力例は型/PIC の参考で、業務的に正しい値の推定ではありません。`Preparation.Required` を 0 や空文字へ置換しません。

通常 run と explore は `reports/executions/*.json` に開始・終了・不完全・失敗と内容 hash を保存します。この領域はユーザー編集領域や決定的生成 manifest に混在しません。停止位置は ExecutionException・DependencyTrace・内部 trace から構造化し、識別不能は unknown です。最新の失敗を過去の成功で隠しません。コードや fixture が変わった記録は stale です。prepare はビルド・スタブ・scenario を実行せず、作業が残る場合も 0、台帳や入力不正は 2 です。

## 固定業務境界

既存 characterization は全内部状態の挙動固定として残ります。業務テストは `IBusinessCaseAdapter` の `ContractId` と `Execute(BusinessCaseInput)` を使い、安定した業務名の結果、順序付き外部効果、業務終了状態を比較します。

| 領域 | 所有と役割 |
|---|---|
| UserCode/Business/ | 利用者の DTO・アダプター・観測マッピング |
| UserCode/Business/BusinessRegistration.cs | 契約 ID とアダプターの明示登録 |
| Tests/BusinessContracts/*.json | 利用者が選んだ境界の版・型・選択理由 |
| Tests/BusinessCases/*.json | 固定入力・固定外部応答・独立期待値・由来 |
| Tests/Templates/Business/ | 未記入の参考ファイル。テスト発見対象外 |
| Runtime/Business*.cs | 共通の精密比較と登録検査 |
| Tests/Support/BusinessTests.g.cs | 既存 MSTest 内の業務実行入口 |
| Testing/BusinessHistory.json | 必須ケース ID の管理履歴。削除でゲートを回避できない |

契約 `input/result/termination` は `type` を持つ型記述です。`number` は正確な十進文字列を Number で比較し、double を使いません。`string`、`boolean`、`object` の `fields`、`array` の `items` を扱い、`nullable:true` だけが null を許します。単純 PIC は number/string に指定できます。文字の末尾空白、配列順・要素数、キーの欠落と null、型を区別します。空の result、未知／重複キー、未知型、不正 PIC、ケース ID 重複、未設定期待値を拒否します。差分は caseId と JSON Pointer、期待・実際、有限長の sourceReferences を示します。

`BusinessCaseInput.Values` は正式業務入力です。アダプターで正式 Arguments／ACCEPT 入力へ変換し、Executor.Run の結果から必要な項目だけを投影します。InitialState は明示的な試験補助です。生成メンバーや statementId を公開ケース JSON の業務キーには使いません。内部 snapshot 全体を BusinessObservation にコピーしません。

固定外部応答は `operation/request/response` の順序列です。`BusinessExternalScript.BindNext` を既存の型付き `ScriptedDependencies.Expect...` に接続し、生成要求から業務要求、固定業務応答から生成応答を変換します。業務名は `customer.lookup` 等です。順序・不足・余剰・要求不一致が失敗し、`External.Effects` は業務操作と payload の順序を保持します。採用後のサービスには `Invoke` で接続し直せます。時刻等は明示入力で注入し、比較から暗黙に除外しません。

合成の完全な契約・独立手計算ケース・型付き fake アダプターは `tests/fixtures/completion/`、生成 API への接続と実行例は `CompletionWorkflowTests` にあります。手計算の期待値は `101 × 0.015 = 1.515 → ROUNDED 1.52` です。テンプレートから業務名や正しい期待値を自動確定しません。

## レビュー・adopt・リファクタリング

`Tests/review.json` の既存 `schemaVersion:1` を拡張し、業務行は `caseKind:"business"` とします。`files` には業務ケース JSON と全業務契約 JSON の SHA-256、`implementationHashes` にはテストを除く全 C# ソース（Stubs、補助コード、アダプター、マッピングを含む）、`sourceHashes/configHash/contractHashes` には manifest の現在値を束縛します。入力・固定応答・期待値はケース JSON の hash が束縛します。実装や期待値をレビューした後にだけ `reviewed:true` としてください。内部補完由来の characterization 行にも現在の全利用者 C# を `implementationHashes` で束縛します。

由来は `csharp-characterization`（現在の C# 挙動）、`specification`（元 CBL・仕様・手計算等から独立した根拠）、`cobol-reference`（COBOL採取）の三種です。COBOL 採取は provenance の processor/version/conditions/method/fileHashes を要求します。自動探索をレビューしただけで独立期待値へ昇格させません。従来の independent-synthetic/original-cobol-captured は従来ケースで維持します。

業務ケースなしだけでは従来 adopt を禁止しません。登録があれば必須 ID 履歴、現在の hash レビュー、同じ採用 stage の MSTest TRX と各業務ケースの実測成功記録を追加ゲートにします。0 件・skip・NoOp・削除・不完全・古いコードの成功を流用しません。業務スイートは全ケースを実行し、ケースごとの開始・成功を `TestResults/business-execution.json` に保存します。期待値更新コマンドは追加していません。

採用後は元 CBL、IR、CLI、元リポジトリを実行時依存にしません。実装 hash は採用時の来歴として保持し、アダプターやサービスの内部変更を hash だけで拒否しません。業務ケース／契約変更はレビュー hash の更新が必要です。

```sh
# 業務境界だけを確認する場合。内部 snapshot の characterization は別の目的のまま残る
dotnet test adopted/payment/Tests/CobolCS.Generated.Tests.csproj --filter Name=BusinessCases_Run
# ソリューション全体の回帰
dotnet test adopted/payment/Tests/CobolCS.Generated.Tests.csproj
```

内部 snapshot を変更すると characterization は意図どおり差分を示すことがあります。業務テストのケース・契約・期待値は同じまま、アダプターを新サービスに接続し直せます。外部呼出しの集約を自動で同値扱いしません。今回の外部効果は厳密な順序比較です。

## 旧出力

旧出力の prepare は検証済みスタブ IR から保守的に役割を読めます。保護 router／探索 API がない旧出力の explore は理由付きで拒否し、同じ原文と設定による再 generate を案内します。再生成は Stubs、UserCode、業務ケース／契約、テンプレート、レビューのバイトと削除を保持します。役割だけの追加は既存のスタブ束縛 hash を変更しません。原文や型の対応が変わって編集スタブの束縛が衝突する場合は従来どおり新ディレクトリへ生成して明示移植します。採用済み先への explore／再 generate は非対応で、dotnet test を使います。

受入項目と実行した試験は[検証記録](completion-workflow-results.md)を参照してください。

---

[ドキュメント案内](README.md)へ戻る。
