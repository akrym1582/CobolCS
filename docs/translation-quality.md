# 変換品質の検証

通常の生成コマンドは現行 CLI の `translate` です。追加コマンド・フラグはありません。
元文との対応、C# の実行、独立期待値との一致を別々に記録します。
元の HEAD には generate/adopt、characterization MSTest、業務観測契約がなかったため、通常 translate の既存設定経路へ統合しました。新しい CLI コマンドやフラグは追加していません。
本変更は frontend のトークン所属・位置情報、FlowBuilder、Machine、精密 Number、生成ファイルのハッシュ台帳を再利用します。

## 配置と使用例

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/QUALITY --out out/quality
mkdir -p out/quality/Tests/ReferenceCases
cp examples/QUALITY/example.specification.json out/quality/Tests/ReferenceCases/add-two.json
```

`add-two.json` は完全な合成 specification ケースです。元ソースの SHA-256、設定ハッシュ、観測境界ハッシュ、固定入力、独立計算の根拠、期待観測、必須指定を含みます。
原文は「N の初期値 0 に 2 を加算する」ので、期待値は `display: []`, `fields: {"N":"2"}`, `signal: "goback"` です。
C# の出力を採取して期待値にしたものではなく、元 COBOL の実機結果でもありません。

原文と根拠を確認した人が `review.approved` を `true`、`review.reviewer` を自身の識別名に変更してください。
この記入例は未承認で配布します。レビュー欄だけの変更では、付属の `contentHash` は変わりません。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/QUALITY --out out/quality
dotnet test out/quality/Tests/CobolCS.ReferenceTests.csproj --logger trx
```

以後の生成・試験は期待値を更新しません。元ソース・COPY・設定を変更したら既存ケースは stale になります。
実際の入力・期待値・根拠・観測境界を変更した場合は、独立に再確認し、`ReferenceCase.ReviewHash(case)` をレビュー欄へ記録します。
これは生成済みプロジェクトにも含まれる C# API です。

```csharp
var value = ReferenceCase.Parse(File.ReadAllText(casePath));
// 原文と独立根拠を確認した後にだけ、確認者が記入する。
value["review"]!["contentHash"] = ReferenceCase.ReviewHash(value);
value["review"]!["reviewer"] = reviewer;
value["review"]!["approved"] = true;
File.WriteAllText(casePath, value.ToJsonString());
```

`Tests/Templates/ReferenceCases/` には JSON Schema、当該生成のソース・設定ハッシュ、QUALITY.cbl と完全な未承認ケース例が出ます。
テンプレートは実ケースとして収集しません。テンプレートの QUALITY 例を別途変換する場合は同じ設定を使い、元プログラム用の context を流用しないでください。

初回は StyleCop、MSTest 3.6.4、Microsoft.NET.Test.Sdk 17.12.0 の NuGet 復元が必要です。
復元済みキャッシュを使うビルド・比較実行に Python、AI、ネット、CobolCS CLI、COBOL 処理系は不要です。
NuGet の脆弱性情報を取得できないオフライン環境では `dotnet test ... -p:NuGetAudit=false` を使用できます。
生成先をコピーする際は Runtime、Programs、GeneratedApi.cs、Program.cs、両 csproj、Tests のケース・契約・比較コードも持ち出します。
`reports/ir.json`、元 CBL・COPY、manifest、元リポジトリは実行依存にしません。

## 契約と比較の範囲

観測境界は `full-state-v1` またはレビュー済みの業務契約です。full-state は初期値を `input.fields` で注入し、`display`、全 `fields`、`signal` を完全比較します。固定外部モードでは外部依頼列とバイト列も比較します。
full-state の fields は内部初期状態の注入です。業務入力 values と順序付き arguments は業務契約で別に定義し、生成 API の BusinessInput/BusinessResult を使えます。
数値は文字列で受け渡し、double、trim、丸め、許容誤差、順序無視を使用しません。
末尾空白・配列順序・件数、欠落と null、値型を区別します。未設定値や未知型を正常な 0 や空文字へ置き換えません。

ケースの全 JSON のトップレベル・既知のネストは未知フィールドを拒否します。重複 JSON プロパティ、重複 caseId、未知プログラム、空比較対象、版・来歴・レビューの不備も失敗です。
利用者のケースと `ReferenceCases/Data/` の採取ファイルは管理対象から除外して再生成で保存し、反映直前に並行編集を再検査します。
生成 C# や生成テストの編集・削除は従来どおり上書きを拒否します。
必須ケース ID は取込台帳に残し、削除・必須指定解除で黙って通らないようにします。削除したケースを復元する処理はありません。

origin は次の三種です。

| origin | 意味 |
|---|---|
| csharp-characterization | 自己観測の回帰。独立 COBOL 検証へ算入しない |
| specification | 原文・仕様から独立して記述した期待値 |
| cobol-reference | 指定した処理系で採取された結果。採取情報とファイルハッシュが必須 |

cobol-reference は `evidence.referenceExecution` に processor、version、compileOptions、capturedAt、method を記入し、`captureFiles` に `Data/` 以下の相対パスと SHA-256 を指定します。
未取得の採取情報は `unknown` と記入して制約として残します。`syntheticCapture` は合成取込試験なら true、実際の採取結果なら false です。
合成取込試験は実機比較件数へ算入しません。`syntheticDependencies` と `conditions` で外部応答・時刻・環境などの適用条件を明記します。
処理系別・条件別の結果を保持し、富士通と他の処理系を同一の確認実績へ合算しません。
ハッシュは改変検出と内容の結び付けであり、実機で採取した事実の保証ではありません。由来の確認は確認者の責任です。

採取時は、元 CBL/COPY・設定、処理系とオプション、採取日時・方法、入力と初期状態、外部応答、時刻・環境、表示・全項目・終了種別を確認してください。
採取した生ファイルを Data に保存し、独立して期待値へ対応付けてからレビューします。不明な対応を推測で補いません。

## レポートとゲート

| 配置 | 内容 |
|---|---|
| reports/quality/translation-ledger.json | 原文範囲、COPY 経路と REPLACING、IR ID、生成メンバーと行範囲、変換状態と診断 |
| reports/quality/summary.json / .md | 生成時だけの件数、未解析・未知レイアウト・COPY 欠落、ケース取込状況と制約 |
| reports/quality/reference-inventory.json | ケース ID・パス・ハッシュ、必須 ID の履歴、取込エラー |
| reports/quality/work-items.json | 原文対応不備、未対応、データ定義・参照データの不備 |
| TestResults/quality/&lt;executionId&gt;/ | 開始、ゲート、ケースごとの差分・文 ID と経路、完了 summary |

解析失敗で IR に残らないトークンも未解析領域に残します。未知文・子の外部依存は親の translated で隠しません。
意味上命令の不要な CONTINUE、終止符、sequence には固定の理由コードを付けます。
原文→IR の所属欠落、生成範囲の欠落、ID 重複、原文位置の範囲外を検出します。
未解析領域があれば文数分母は完全ではなく、変換 100% を表示しません。translated も動作同値の証明ではありません。

例として QUALITY は translated の実行文 2 件（ADD、GOBACK）と非実行の終止符、未解析 0、比較ケース未配置では not-configured です。
独立ケース追加後の生成は ready-not-run です。実行完了時だけ、specification の matched 1 件を実行 summary に記録します。
元 COBOL の実機比較は依然 0 件です。

試験は未設定応答、比較不一致、不正・古いケース、必須欠落、時間・step・観測上限、未処理例外を成功にしません。
差分には JSON Pointer のパス、型、期待値・実値を含め、最大 50 件、各値 512 文字に制限して省略件数を記録します。
条件の再評価をせず、観測した分岐結果を記録します。観測イベント上限は 10,000 です。
生成時レポートは dotnet test で更新しません。started.json だけの実行は未完了で、summary の completed を確認します。
再生成時に完了済み実行の source/config/contract/case/code の内容を照合し、異なる結果を stale として summary と作業項目に表示します。途中ファイルを完了結果へ適用しません。

## 受入検証と残る範囲

`python3 tests/quality_regression.py` は 39 fixture（数値 6、算術 7、条件 4、制御 7、構造 4、外部 3、原文形式 4、組合せ 4）を使います。
対応済み 30 件は手計算した値を比較し、未対応 9 件は明示的な拒否を検査します。
生成 MSTest、stale・必須欠落・編集保持、重複、不正来歴、処理系別の合成取込、同一行 COPY の識別、原ソースと IR を除いた実行を検証します。
比較 >= を > へ、加算量変更、MOVE 省略の破壊を一時生成物で行い、独立期待値が失敗することを検査します。
製品コードに破壊フラグは追加しません。

追加の `python3 tests/quality_completion_regression.py` は、固定外部依存・業務観測・採用後の実行を検証します。CALL の集団更新と passing mode、FILE の正常→EOF と FILE STATUS、SQL の正常/no-data/異常、固定列の不足・余剰・入力/順序違い、物理バイト offset、型付き API と配列順序、業務結果を維持する内部整理、外部呼出し重複、TRX skip/0件/重複/NoOp、古いレビュー、未完了の新しい実行による採用拒否を含みます。

実サービス接続、全方言、実機レコードの新しい PIC/COMP デコーダ、REDEFINES の領域共有は引き続き範囲外または未対応です。静的な未対応箇所は台帳に残し、到達した場合は成功にしません。固定 fake の一致は当該応答条件下だけの一致であり、実サービス実装の検証ではありません。

今回修正した原文追跡の不備は、同一行の複数 COPY の展開 ID が衝突する点と、異なる COPY 展開間の範囲が一つに潰れる点です。
実機結果は提供されていないため、実案件の元 COBOL との一致確認は **0 件・未実施** です。

## 今回の検証記録

| コマンド | 結果 |
|---|---|
| `dotnet build CobolCS.sln -m:1 --nologo --no-restore` | 成功、警告 0・エラー 0 |
| `dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore` | 成功 |
| `dotnet run --project tests/CobolCS.Tests --no-build` | 数値・文字列・独立比較の 22 項目が成功 |
| `python3 tests/regression.py` | 既存結合テスト 22 件が成功 |
| `python3 tests/quality_regression.py` | 品質結合テスト 7 件が成功。生成 MSTest で 30 fixture の値を比較、9 fixture の未対応を確認 |
| `python3 tests/quality_completion_regression.py` | 外部依存・業務観測・採用の統合テスト 10 件が成功 |
| `git diff --check` / Python の `py_compile` | 成功 |

変更箇所は、frontend の source/copybooks/models/main（読込み失敗と展開・範囲来歴）、Generator/GeneratedCode（通常生成・保存・独立 API）、新規 TranslationQuality/ReferenceTestGeneration（台帳と固定 MSTest）、Runtime の Instruction/FlowBuilder/Machine（文 ID と副作用を変えない記録）、新規 ReferenceCase/ReferenceVerification（schema・レビュー・来歴・差分・完了証跡）、schema テンプレート、QUALITY 使用例、39 fixture、C#・Python のテスト、CI と README/CLI/開発文書です。
生成後のプログラムを一時的に破壊する試験は期待どおり失敗し、通常ケースの期待値は書き換えていません。
これらは合成入力による品質基盤の受入結果です。実案件の全入力の同値性や、元 COBOL の実機一致を表す結果ではありません。

## 固定外部依存と業務観測の完全な例

`examples/QUALITY_EXTERNAL/` は初期業務入力 start=1、集団 CALL の固定応答で N=5、その後の比較で FLAG=1 という独立計算例です。CBL、設定、完全な未承認業務契約、固定入力・依頼・応答・期待値と来歴を含む未承認 specification ケースを同梱しています。実機結果ではありません。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/QUALITY_EXTERNAL/QUALITY_EXTERNAL.cbl --config examples/QUALITY_EXTERNAL/config.json --out out/external
cp examples/QUALITY_EXTERNAL/business.json out/external/Tests/Contracts/business.json
cp examples/QUALITY_EXTERNAL/example.specification.json out/external/Tests/ReferenceCases/group-call.json
```

確認者は両 JSON の原文・根拠・対象項目を確認して `review.approved=true` と自身の `review.reviewer` を記入します。レビュー欄だけの変更なら既存 contentHash は保持できます。入力・期待値・観測選択を変更した場合は `ReferenceCase.ReviewHash` / `BusinessObservation.ContractHash` に結び付けて再レビューします。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/QUALITY_EXTERNAL/QUALITY_EXTERNAL.cbl --config examples/QUALITY_EXTERNAL/config.json --out out/external
dotnet test out/external/Tests/CobolCS.ReferenceTests.csproj --logger "trx;LogFileName=quality.trx"
```

固定外部設定は `externalAdapter: "fixed"` です。省略または none では従来どおり strict が外部依存を拒否します。fixed でも未設定応答への到達は文 ID 付きで失敗します。

`input.externalResponses` の各要素は request と response です。request は statementId/kind/target/operation/arguments/query を持ち、arguments は mode/value の順序列です。CALL の reference/content/value、SQL input/indicator、FILE record/key/advancing を区別します。依頼を計測のために再評価しません。同じ文の複数回実行は別の列要素になります。

response は outcome/status と、処理に必要な arguments/returning/record/outputs/sqlstate を持ちます。CALL の status は成功 0、例外 1。BY CONTENT/VALUE の更新は拒否します。FILE は成功 00、EOF 10 と明示したその他の状態。SQL は成功 0、no-data 100、異常の負の整数です。WHENEVER の SQLERROR/NOT FOUND は固定状態から段落へ分岐します。FILE/SQL の実 DB/ファイルへは接続しません。

集団は子名と精密文字列の論理オブジェクト、表は順序付き論理配列で交換します。REDEFINES のバイト共有を実装したものではありません。外部応答の bytes は base64/length/recordLengths/encoding を明示した opaque バイト列です。観測契約 includeBytes=true の場合に完全比較し、差分は最初の offset と最大16バイト、境界と文字コード情報を限定表示します。数値の物理格納互換へ読み替えません。

業務契約は `Tests/Contracts/` の利用者所有 JSON です。programId と宣言済み field に、inputs/arguments/observations の key/type を対応させます。型は number/text/number-array/text-array。number は精密数値として比較し、文字の空白と配列順序は完全一致です。外部依頼列と終了種別の観測は必須です。契約の未知項目・型違い・重複・未承認・古いハッシュはゲート失敗です。

```csharp
var input = new BusinessInput(
    new JsonObject { ["start"] = "1" },
    new JsonArray(),
    new JsonObject(), // 独立した内部状態注入
    configuredExternalResponses);
var result = GeneratedApi.RunBusiness("quality-external-business", input);
// result.Observation は契約内の業務結果。result.Snapshot は別の full-state。
```

内部状態の変更は業務観測境界の外なら比較に影響しません。観測の選択変更には再レビューが必要です。期待値を現在の C# の出力で自動更新する操作はありません。

## 既存設定経路からの採用

テスト成功後、生成された `Tests/Templates/ReferenceCases/adoption-review.json` を `Tests/Reviews/adoption.json` へコピーします。ソース/設定/コード/観測契約/全ケースの内容を確認した人が approved と reviewer を記入します。このテンプレートの contentHash はそれらの内容に結び付いています。

リポジトリルートに、例えば次の `adopt-config.json` を作ります。

```json
{
  "externalAdapter": "fixed",
  "quality": {
    "adopt": {
      "destination": "adopted/external",
      "trx": "out/external/Tests/TestResults/quality.trx",
      "review": "out/external/Tests/Reviews/adoption.json"
    }
  }
}
```

```sh
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/QUALITY_EXTERNAL/QUALITY_EXTERNAL.cbl --config adopt-config.json --out out/external
dotnet test adopted/external/Tests/CobolCS.ReferenceTests.csproj --logger trx
```

パスは設定ファイルのディレクトリを基準に解決します。quality.adopt は運用設定なので COBOL の意味設定ハッシュを変えません。採用は strict で生成できたものに限り、既存の非空採用先を上書きしません。

採用ゲートは、TRX の全結果 Passed、ゲートと全ケースの一意な method/caseId 対応、各結果の同じ実行 ID、最新の開始と完了証跡、source/config/contract/case/code ハッシュ、全必須ケース成功、レビューを照合します。0件、skip、NoOp、重複、古い成功・レビュー、新しい未完了実行は拒否します。入力・期待値・必要な採取ファイルの変更も拒否します。

参照ケースが未配置の場合は生成 C# の default characterization を別 origin で回帰確認します。独立検証状態は not-configured です。既知の初期状態で回帰を完了できない場合は固定ケースを配置する必要があります。試験できない default を成功とみなしたり skip したりはしません。自己期待値の成功が独立比較の失敗を隠すことはありません。

採用先にはプログラム・Runtime・型付き API・固定ケース・来歴・観測契約・比較コード・レビュー・adoption.json をコピーします。reports/IR、元 CBL/COPY、CobolCS CLI、元 COBOL 処理系は実行依存にしません。内部リファクタリング後も同じ期待値と契約を保持してテストできます。adoption.json は登録ケースに限る一致と、独立検証未配置の場合の未実施を記録します。

失敗は実行別 work-items.json へ caseId/ruleId/businessKey/statementId/path で出し、次の通常 translate で現在の証跡だけを生成 work-items へ取り込みます。静的診断と実行差分を同じ状態へ混ぜず、人が done を記入しても比較結果を変更しません。利用者ケース・契約・Reviews・削除を再生成で保持し、並行編集時には反映を拒否します。

追加統合の検証では `quality_completion_regression.py` の10件が成功しました。採用の0件・skip・重複・NoOp・実行ID違い・レビューhash違い・未完了実行・古いバイナリ識別情報を拒否し、採用後に元ソースを削除した実行と業務破壊の検出を確認しています。型付き API の直接呼出し、精密数値の異なる表記、空白・配列順序、固定応答の過不足・passing mode・順序、バイト offset も検証済みです。
採用は TRX の全体集計も確認し、生成ソースの識別情報を実行アセンブリに埋め込んで完了証跡と照合します。これは生成C#と実行コードの取り違えを検出するためであり、元 COBOL の全入力同値性を証明するものではありません。

SQL NULL の負の出力 indicator は host 値が未定義になるため、0/空文字への置換をせず明示的に拒否します。集団・型をまたぐ FILE 転記も物理レイアウトや文脈の推測を避けて診断します。これらの未対応をケース成功へ含めません。
