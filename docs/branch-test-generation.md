# 通常 generate の分岐探索と MSTest

```sh
cobolcs generate payment.cbl -o out/payment
dotnet test out/payment/Tests/CobolCS.Generated.Tests.csproj --logger 'trx;LogFileName=tests.trx'
# 合成仕様または独立した根拠でケースを確認し、review.json と baseline を整える
cobolcs adopt out/payment -o adopted/payment
```

source backend の通常 generate は、stage 内で分岐抽出、生成 C# のビルド、候補実行、固定再生、MSTest の実行まで行います。新しい CLI やテスト生成フラグはありません。通常 run は探索を行いません。SDK 10 と、既存の固定版 MSTest/VSTest/StyleCop パッケージを事前取得してください。通常実行に元 CBL・IR・探索器は不要です。オフラインでは NuGet キャッシュを使います。

自動期待値は **csharp-characterization、reviewed=false** です。生成 C# の現在の挙動を保存したもので、元 COBOL の正解、全経路網羅、MC/DC、業務同値性を示しません。生成した未レビューの挙動テストは実行できます。手動ケースには従来の Review.Require を使い、人のレビューが必要です。新規の未記入 sample は `Tests/Templates/*.cs.txt` に置き、自動発見される TestMethod を追加しません。手動ケースはテンプレートを `Tests/Cases/` へコピーし、入力・fake・独立期待値・レビュー情報を記入してください。既存の利用者テストを無効化することはありません。

| 出力 | 内容 |
|---|---|
| reports/branches.json | PROGRAM-ID・statementId・枝の役割から決めた ID、outcome、原文位置、条件 IR、計測能力 |
| reports/branch-plans.json | 生成時の入力型・項目 ID・メンバーと候補生成に使った IR |
| reports/test-generation/&lt;runId&gt;/summary.json / summary.md | 試行数、実測到達数／計測可能な outcome 数、未解決、制限、生成／再生の成否 |
| 同じ run の trials.json / cases.json | 無効・失敗を含む試行と、固定入力・応答列・到達証跡・観測 hash |
| Tests/Cases/BranchGenerated/&lt;runId&gt;/*.g.cs | caseId と同名の独立 TestMethod |
| Tests/Baselines/BranchGenerated/&lt;runId&gt;/*.json | 固定入力、固定応答、表示・最終状態・Arguments 更新・外部依頼の期待値 |
| Testing/Characterization_*.g.cs | 未レビューケースの由来と初期ファイル hash を束縛する管理ソース |
| Tests/review.json | 既存レビューを保持し、新規ケースを未レビューで追加 |

IF の文全体の true/false、対応 EVALUATE の選択された WHEN／OTHER／該当なし、inline PERFORM の継続／終了と0・1・複数回、SIZE ERROR、STRING OVERFLOW、CALL／FILE／SQL の結果を記録します。段落 dispatcher の内部ジャンプは分母に含めません。SQL の結果と後続 IF は別の枝です。未対応ブロック内の条件は計測不能として残します。公開 ExecutionOptions の EnableBranchTrace は既定 false、MaxBranchEvents は既定10000です。Result.BranchTrace は不変コピーです。失敗時の途中 trace は例外の Data["BranchTrace"] に残します。上限を超えた trace は Truncated=true とし、成功ケースに使いません。条件を再評価せず、AND／OR の短絡評価を維持します。

数値比較は定数の直前・一致・直後、0・符号付き負値・PIC の端値を作り、小数は scale の最小単位を使います。Number の精密有理数で計算します。文字比較、88値と範囲、複合条件の葉の組合せ、WHEN 定数、入力で制御する0・1・2回ループを候補にします。MOVE と単一入力への加減算を限定的に追跡します。例えば INPUT-AMOUNT - 100 の後の N >= 1000 には1099／1100／1101を与えます。条件付き代入・複雑な式の静的追跡はヒューリスティックで、到達の判定は実行 trace のみです。

ACCEPT は文字列キュー、固定 LINKAGE は Arguments、状態注入は InitialState を使います。状態注入ケースには stateInjection=true を記録します。内部の計算結果を直接変更して到達を主張しません。CALL の参照出力と RETURNING、FILE の有限レコード列と EOF、SQL の正常／no-data／異常は契約に従う合成応答です。実サービスや編集済み外部 Stubs は探索で呼びません。未対応内部 Stubs に到達すれば blocked です。保存する ScriptedDependencies は採取した依頼順と値・ターゲット・passing modesを照合し、不足、不一致、順序違反、余剰応答を拒否します。

試行は ProcessRunner の別プロセスで行い、既存 maxSteps／timeoutMs と全体時間制限を適用します。完走候補は同じ入力と採取した固定応答で2回再実行し、観測値と trace が一致した場合に昇格します。通常の異常応答で完走したケースも対象です。timeout、未実装例外、入力不足、PIC に格納できない値、trace 上限、不安定な再生は正常期待値にしません。試行ごとの出力は1 Mi文字、固定引数表の自動候補は1024要素までに制限します。有限探索で見つからない枝は到達不能の証明ではありません。summary の未解決と trials の理由を確認してください。

## 設定と手動候補

既存 `--config config.json` の任意の testGeneration 項目で設定します。指定しない従来の frontend 設定 hash は変わりません。

```json
{
  "maxSteps": 1000000,
  "timeoutMs": 10000,
  "testGeneration": {
    "maxAttempts": 200,
    "seed": 1,
    "timeoutMs": 120000,
    "maxTraceEvents": 10000,
    "candidates": []
  }
}
```

maxAttempts はプログラムごとに1～200で、確認用の2回の再生も予算に含めます。seed は0以上の整数です。探索全体の timeoutMs は1000～600000、maxTraceEvents は1～100000です。ビルドと MSTest は全体の残り時間内で、それぞれ最大60秒です。設定・seed・実行コード hash・生成器 version・原文 hash を記録します。同条件で正常に終わるケース選択と命名は決定的です。時間切れの場合は機械の速度で到達結果が変わります。

candidates は同じ設定ファイルに最大200件を追加できます。各エントリは programId と input を必須とし、responseMode、responseModes、responseValues を任意とします。input は生成公開型の JSON です。AcceptInputs は文字列配列、Arguments と InitialState は `reports/branch-plans.json` の **member** または公開 API のメンバー名をキーにします。Number は数値文字列または numerator／denominator の文字列オブジェクトです。基本項目、完全な固定グループ、固定表のみを指定できます。InitialState の表は Subscripts（外側から1始まりの整数配列）と Value の列、Arguments の表は宣言順の完全な値列です。重複添字、未知メンバー、LINKAGE への InitialState、欠けた固定グループを拒否します。未知レイアウトは推測しません。

```json
{
  "programId": "PAYMENT",
  "input": { "AcceptInputs": ["100.00"], "Arguments": {}, "InitialState": {} },
  "responseMode": 0,
  "responseValues": { "f000002": "3.50", "f000004": "0" }
}
```

responseValues は対応する外部応答の項目 ID と文字列値です。未知項目や外部の更新対象でない項目は拒否します。responseMode は0=正常、1=CALL異常／FILE即EOF／SQL no-data、2=FILE／SQL異常、3=FILEで2レコード後EOFです。CALLの2・3、読み込み以外のFILEは正常応答になります。responseModes で文別の `Dependency_<statementId>` をキーに同じモードを指定できます。状態出力（FILE STATUS、SQLCODE、SQLSTATE）は契約に合わせて合成します。候補の未知キーや型不整合は診断し、無効候補を成功にしません。同じ入力・応答の試行は重複排除します。手動候補なしでも単純条件と対応外部応答を探索します。

## レビュー、採用、保持

未レビューの自動テストは管理ソースの来歴とケース／baseline の hash を必須にします。baseline 内の入力・応答・期待値を変更して review.json の hash だけ更新しても、未レビューのまま成功することはありません。テスト実行は値・hash・reviewed を更新しません。レビューする場合は独立根拠を確認し、baseline の reviewReason を UNREVIEWED 以外の具体的な理由にし、変更したファイルの hash と review.json の reviewed=true を明示します。レビュー済みケースには通常のファイル hash 検査を適用します。

adopt は全レビューエントリ、非NoOp、実際に実行された Passed、TRX の testName／caseId／testId／executionId／TestDefinitions の一致を要求します。同名テスト、skip、0 tests、失敗、未レビューを拒否します。曖昧な部分一致は使いません。adopt後は元生成先と探索用reportsを削除してもテストが単独で動きます。通常のリファクタリングでは branchId を検査せず、計算や依頼の変更は挙動の不一致として検出します。

Tests/Cases、Tests/Baselines、Tests/Templates、review.json は利用者所有です。同一runの再生成では編集・レビュー状態・削除を保持し、既存証跡を再利用します。再生成は既存の管理ファイル hash と最後の実ファイル一覧を検査し、競合時は元出力を保持します。異なるrunの追加でも旧証跡を管理台帳へ残します。同じ入力・応答・期待値を持つ既存ケースは固定再生を確認して再利用し、重複を追加しません。自動ケースを保持するrunは最大4種類とし、上限時は未完了の理由を報告します。利用者がケースを整理するか、新規生成先を使ってください。自動期待値は業務レビューを代行しません。削除や旧入力 hash に基づくケースの扱いは人が確認してください。

SDK不足、ビルド失敗、未対応、制限到達でも、可能な通常C#・Stubs・実行ソース・テストプロジェクトと理由付きレポートを残します。manifest の judgments.generated と judgments.testGeneration.complete は別です。0件の生成を完了としません。従来どおり、source scaffold の未対応診断だけで終了コードを変えず、strictの拒否は1、入力・所有権・実行エラーは2です。`summary.json` と CLI の「incomplete」を確認してください。既存の未記入手動テストが dotnet test を妨げる場合、generateは理由を報告し、上書き・skip・成功扱いにしません。
