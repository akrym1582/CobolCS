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

通常 generate は `generated-pristine` の初期スタブを使用し、編集済みユーザーコードを自動実行する入口にはしません。明示的な explore だけが `completed-internal` を使用します。既存の候補・予算・worker・状態検査・二回固定再生・xUnit を共用します。

`reports/stubs.json` の `executionRole` は IR の文・依存・子ノードから分類します。`internal-logic` は内部補完、`external-adapter` は型付き CALL/FILE/SQL、`external-unsupported` は未対応外部効果、`unknown` は分類不能です。後二者は探索時に停止し、ユーザー実装へ落としません。外部アダプターは probe では合成応答、replay では順序付き fake を使い、編集済み外部 Stubs を呼びません。

補助コードは `UserCode/Completion/**/*.cs` に置きます。共通 partial のため Stubs と全 UserCode の必要な C# をコンパイルしますが、scenario・登録処理を探索入口にはしません。コンパイルする利用者ソースのパスと hash を全部記録します。内部補完には外部接続を含めないでください。worker のプロセス分離はセキュリティサンドボックスではありません。

検査→stage 固定コピー→ビルド→有限探索→固定再生→xUnit→原本との編集競合検査→一式公開の順です。ビルド失敗・競合では既存出力を変更しません。不完全な試行は診断として保存し、正常ケースへ昇格させません。xUnit 固定再生に失敗した新規の暫定ケースは公開しません。ユーザー static 状態は worker で隔離し、実際の固定テストで共有状態の影響が出る場合も不完全として拒否します。

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

通常 run と explore は `reports/executions/*.json` に開始・終了・不完全・失敗と内容 hash を保存します。この領域はユーザー編集領域や決定的生成 manifest に混在しません。停止位置は ExecutionException・DependencyTrace・内部 trace から構造化し、識別不能は unknown です。最新の失敗を過去の成功で隠しません。コードや fixture が変わった記録は stale です。prepare はビルド・factory・スタブ・scenario を実行せず、業務ケース・契約・採取データの不備があっても診断と案内を保存すれば 0 です。管理台帳破損、安全パス・リンク違反、I/O、編集競合は 2 です。

## 固定業務境界

既存 characterization は全内部状態の挙動固定として残ります。業務テストは `IBusinessCaseAdapter` の `ContractId` と `Execute(BusinessCaseInput)` を使い、安定した業務名の結果、順序付き外部効果、業務終了状態を比較します。

| 領域 | 所有と役割 |
|---|---|
| UserCode/Business/ | 利用者の DTO・アダプター・観測マッピング |
| UserCode/Business/BusinessRegistration.cs | 契約 ID と新規アダプター factory の明示登録 |
| Tests/BusinessContracts/*.json | 利用者が選んだ境界の版・型・選択理由 |
| Tests/BusinessCases/*.json | 固定入力・固定外部応答・独立期待値・由来 |
| Tests/Templates/Business/ | 未記入の参考ファイル。テスト発見対象外 |
| Runtime/Business*.cs | 共通の精密比較と登録検査 |
| Tests/Support/BusinessTests.g.cs | 既存 xUnit 内の業務実行入口 |
| Testing/BusinessHistory.json | 必須ケース ID の管理履歴。削除でゲートを回避できない |

契約 `input/result/termination` は `type` を持つ型記述です。`number` は正確な十進文字列を Number で比較し、double を使いません。`string`、`boolean`、`object` の `fields`、`array` の `items` を扱い、`nullable:true` だけが null を許します。単純 PIC は number/string に指定できます。文字の末尾空白、配列順・要素数、キーの欠落と null、型を区別します。空の result、未知／重複キー、未知型、不正 PIC、ケース ID 重複、未設定期待値を拒否します。差分は caseId と JSON Pointer、期待・実際、有限長の sourceReferences を示します。

`BusinessCaseInput.Values` は正式業務入力です。アダプターで正式 Arguments／ACCEPT 入力へ変換し、Executor.Run の結果から必要な項目だけを投影します。InitialState は明示的な試験補助です。生成メンバーや statementId を公開ケース JSON の業務キーには使いません。内部 snapshot 全体を BusinessObservation にコピーしません。

固定外部応答は `operation/request/response` の順序列です。`BusinessExternalScript.BindNext` を既存の型付き `ScriptedDependencies.Expect...` に接続し、生成要求から業務要求、固定業務応答から生成応答を変換します。業務名は `customer.lookup` 等です。順序・不足・余剰・要求不一致が失敗し、`External.Effects` は業務操作と payload の順序を保持します。採用後のサービスには `Invoke` で接続し直せます。時刻等は明示入力で注入し、比較から暗黙に除外しません。

合成の完全な契約・独立手計算ケース・型付き fake アダプターは `tests/fixtures/completion/`、生成 API への接続と実行例は `CompletionWorkflowTests` にあります。手計算の期待値は `101 × 0.015 = 1.515 → ROUNDED 1.52` です。テンプレートから業務名や正しい期待値を自動確定しません。

## ケースごとの状態と実行証跡

登録先は `IDictionary<string, Func<IBusinessCaseAdapter>>` です。

```csharp
adapters.Add("payment-v1", () => new PaymentAdapter());
```

各ケースの実行時にローカルな登録辞書を作り、factory を一回だけ呼びます。factory の例外、null、重複登録、ContractId 不一致はそのケースの失敗です。生成後の境界は成功・例外の両経路で破棄し、両インターフェースを持つ場合は IAsyncDisposable を優先します。利用者の singleton、static、時計、環境変数まではインスタンス化で隔離できません。サービス・fake・時計等を明示注入し、factory は新規の状態を返してください。移行済みの直列実行設定を継承します。

発見時には静的 Inspect と SHA-256 の行識別だけを行います。factory・スタブ・アダプターは実行しません。元 caseId は大文字小文字を区別し、ファイル名や順序に依存しません。xUnit 2.9.3 は長い引数文字列を表示名で省略するため、完全な SHA-256 を前半32桁・後半32桁の2引数で渡します。adopt は表示名の部分一致を使わず、この2引数、共通メソッド、クラス、TRX の testId/definition/execution を照合します。

```text
TestResults/business/<executionId>/
  started.json
  cases/<元caseIdの完全SHA-256>.json
  summary.json
```

started は対象 ID 集合、ケース・契約・採取データ・レビュー・履歴・実行ソースの hash と診断を持ちます。ケース記録は元 ID、programId、contractId、executionId、入力 hash、終端状態、型付き理由・JSON Pointer・有限長の差分を持ちます。一時ファイルから原子的に置換します。対象全件と集合検査が揃った場合のみ complete=true です。比較失敗が揃えば complete=true/passed=false。開始のみ、必須欠落、診断、部分フィルタは未完了です。未設定の案内行は独立業務成功数に算入せず、passedCases=0/passed=false とします。

通常実行では fixture が毎回新しい executionId を作ります。adopt は新規 ID を子プロセスの `COBOLCS_BUSINESS_EXECUTION_ID` にだけ渡し、同一 stage の今回の TRX と記録を照合します。同じ ID の保存先を再利用せず、複数プロセスの証跡は別ディレクトリになります。手書きの古い JSON を今回の成功と見なしません。

業務回帰の選択は `--filter Category=Business`、生成 characterization の選択は `--filter Category=Characterization` です。ケースを単独選択する場合は、started/ケース記録の caseKey を前半・後半に分け、`--filter 'DisplayName~<前半32桁>&DisplayName~<後半32桁>'` を指定できます。部分実行は adopt の全件ゲートを満たしません。

## 採取データと診断

`cobol-reference` の provenance.fileHashes は `Tests/BusinessCases/Data/` からの相対パスと実バイトの SHA-256 です。Data 以下の JSON は業務ケースとして発見しません。Data は利用者所有で再生成・採用時にバイトと削除を保持し、Data の C# ファイルもコンパイルしません。

空の hash 集合、重複キー、不正 hash、絶対・ドライブ・UNC・バックスラッシュ・空要素・`.`・`..`・NUL を拒否します。親を含むリンクを拒否し、実在する採取ファイルの hash を小文字に正規化して照合します。レビューの files に参照した採取ファイルも追加してください。削除されたファイルを証跡から再作成しません。採用後も採取元を持ち出して単独検証します。ファイル一致は改変・取り違えの検出であり、実機実行や期待値との対応の正しさは由来・条件とともに人がレビューします。未参照資料は実機比較件数へ算入しません。

`BusinessCases.Inspect(root)` は有効な契約・ケースと診断を返し、`Load(root)` は同じ検査結果に診断があれば拒否します。prepare/worklist は一回の Inspect を全プログラムで共有します。`business-case-error`、`business-contract-error`、`reference-data-error` にファイル、判明した caseId、理由コード、JSON Pointer、修正先、確認コマンドを残します。契約不正に依存するケースは blocked-contract です。必須履歴は有効なケースだけを追加し、不正・削除によって過去の ID を消しません。手動 done は診断解消の証明になりません。run/explore 後の案内更新にも診断収集を使い、未完成業務ケースだけで正常な内部実行を失敗へ変更しません。

## レビュー・adopt・リファクタリング

`Tests/review.json` の既存 `schemaVersion:1` を拡張し、業務行は `caseKind:"business"` とします。`files` には業務ケース JSON と全業務契約 JSON の SHA-256、`implementationHashes` にはテストを除く全 C# ソース（Stubs、補助コード、アダプター、マッピングを含む）、`sourceHashes/configHash/contractHashes` には manifest の現在値を束縛します。入力・固定応答・期待値はケース JSON の hash が束縛します。実装や期待値をレビューした後にだけ `reviewed:true` としてください。内部補完由来の characterization 行にも現在の全利用者 C# を `implementationHashes` で束縛します。

由来は `csharp-characterization`（現在の C# 挙動）、`specification`（元 CBL・仕様・手計算等から独立した根拠）、`cobol-reference`（COBOL採取）の三種です。COBOL 採取は provenance の processor/version/conditions/method/fileHashes を要求します。自動探索をレビューしただけで独立期待値へ昇格させません。従来の independent-synthetic/original-cobol-captured は従来ケースで維持します。

業務ケースなしだけでは従来 adopt を禁止しません。登録があれば必須 ID 履歴、現在の hash レビュー、同じ採用 stage の xUnit TRX と各業務ケースの実測成功記録を追加ゲートにします。0 件・skip・NoOp・削除・不完全・古いコードの成功を流用しません。業務スイートは個別 Theory 行として有効な全ケースを実行し、ケースごとの開始・成功・失敗を `TestResults/business/<executionId>/cases/<SHA-256>.json` に保存します。集合の診断・必須欠落は `BusinessCases_Integrity` Fact が拒否します。不正な JSON がある場合も有効な別ケースを実行します。期待値更新コマンドは追加していません。

採用後は元 CBL、IR、CLI、元リポジトリを実行時依存にしません。実装 hash は採用時の来歴として保持し、アダプターやサービスの内部変更を hash だけで拒否しません。業務ケース／契約変更はレビュー hash の更新が必要です。

```sh
# 業務境界だけを確認する場合。内部 snapshot の characterization は別の目的のまま残る
dotnet test adopted/payment/Tests/CobolCS.Generated.Tests.csproj --filter Category=Business
# ソリューション全体の回帰
dotnet test adopted/payment/Tests/CobolCS.Generated.Tests.csproj
```

内部 snapshot を変更すると characterization は意図どおり差分を示すことがあります。業務テストのケース・契約・期待値は同じまま、アダプターを新サービスに接続し直せます。外部呼出しの集約を自動で同値扱いしません。今回の外部効果は厳密な順序比較です。

## 旧出力

旧出力の prepare は検証済みスタブ IR から保守的に役割を読めます。保護 router／探索 API がない旧出力の explore は理由付きで拒否し、同じ原文と設定による再 generate を案内します。再生成は Stubs、UserCode、業務ケース／契約／Data、テンプレート、レビューのバイトと削除を保持します。旧インスタンス登録は自動上書きせず、prepare で legacy-registration と具体的な factory 移行例を表示します。旧 business-execution.json は採用証跡として拒否します。管理ファイルを互換更新できない出力では、新ディレクトリへ生成し、登録の型と Add の右辺を上の例へ直して利用者編集を移植してください。役割だけの追加は既存のスタブ束縛 hash を変更しません。原文や型の対応が変わって編集スタブの束縛が衝突する場合は従来どおり新ディレクトリへ生成して明示移植します。採用済み先への explore／再 generate は非対応で、dotnet test を使います。

xUnit の4修正の受入項目と実 runner 検証は[最新の検証記録](xunit-business-four-fixes-results.md)を参照してください。[従来の検証記録](completion-workflow-results.md)は当時の結果として保持します。

---

[ドキュメント案内](README.md)へ戻る。

## 業務境界の候補と雛形

`cobolcs scaffold-business DIRECTORY --program PROGRAM-ID --mapping mapping.json` は、通常 source generate の型付き metadata から明示選択だけを配線します。候補は `reports/business-test-candidates.json/.md`、選択用の未記入 mapping は `Tests/Templates/Business/<program-key>/mapping.json.txt`、JSON Schema は `Testing/BusinessMapping.schema.json` です。

正式入力は固定 LINKAGE Arguments と ACCEPT、試験注入は `initialState` 選択とケースの `input.testState`、結果は既存更新候補・ArgumentsSnapshot・DISPLAY を別区分で扱います。mapping は PROGRAM-ID、candidateHash、役割、型、重複 ID/キー、外部 payload を照合します。名前から業務意味を決めず、全変数を結果へ選びません。固定表は宣言順の全要素を保持し、不明な配置・変換・丸めは拒否または明示例外です。

`UserCode/Business/<contract-key>Adapter.cs`、`Tests/BusinessContracts/<contract-key>.json` と契約別参考テンプレートは初回だけ作成します。未記入ケースは Tests/Templates に残り、発見されません。登録は factory 追記例、レビューは hash の参考一覧です。既存の登録・期待値・reviewed を変更しません。再実行・再生成は利用者バイトと削除を保持し、変更 mapping は `reports/business-scaffolds` の提案だけに保存します。現在のレビュー参照 hash は同領域の `*-review-files.json` に再計算します。stage の途中で原出力が編集された場合は公開を拒否します。

外部操作は選択した typed Request/Response と既存 ScriptedDependencies に `BusinessExternalScript.BindNext` で接続します。応答順はケース自身が指定します。StatusCode/Outcome は必須、null の Optional 出力は更新なしであり、EOF・SQL no-data・指示変数を正常値で補いません。外部 Stubs へ fallback しません。条件・反復 ACCEPT は推測しません。既定の固定キューでは明示 acceptOrder、新しいケース別値列では acceptMode=case-sequence と各ケースの acceptSequence を選びます。adopt 済み出力ではこのコマンドを拒否し、通常の dotnet test を使います。

完全な合成 mapping・独立期待値・実行手順と制約は [業務支援と文字処理](business-test-assistance.md)、受入・固定範囲の測定は [実装検証記録](business-test-assistance-results.md) にあります。

prepareはreports/business-scaffolds/*-review-files.jsonのcasesを現在の有効業務ケースで更新します。filesにケース・全業務契約JSON・参照Dataを束縛し、全実装C#はimplementationHashesへ分離します。review.jsonと初回利用者テンプレートは保持します。mapping提案を元へ戻すとproposalを削除します。ケース別ACCEPTと構造付き未記入雛形は[業務支援](business-test-assistance.md)。必須正式引数未選択はmissing-formal-argumentとして作業表へ残り、正常既定値へ補いません。
