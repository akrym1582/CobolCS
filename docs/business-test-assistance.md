# 業務テスト支援と限定文字処理

source generate の既存 IR・型付き API・更新候補を使って、利用者が選んだ業務境界の Adapter を作ります。業務名、外部応答、期待値、承認は利用者が明示します。新しい解析エンジンや実行時 IR 解釈を追加する機能ではありません。

## 合成 payment の有効化

リポジトリルートで実行します。.NET 10 と固定 xUnit/VSTest パッケージを使います。

```sh
dotnet build CobolCS.sln -m:1
dotnet src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll generate \
  tests/fixtures/completion/payment.cbl -o out/payment \
  --config tests/fixtures/business-assistance/config.json
```

候補は `out/payment/reports/business-test-candidates.json/.md`、JSON Schema は `out/payment/Testing/BusinessMapping.schema.json` です。PROGRAM-ID ごとの候補は元名・symbolId/statementId・PIC・固定次元・位置/COPY履歴・既存型/メンバー/生成ファイル・選択理由・制約を持ちます。入力は正式 Arguments/ACCEPT、状態注入は試験用、結果は更新候補/ArgumentsSnapshot/DISPLAY、外部操作は実際の Request/Response/Expect と渡し方を分けます。入力・設定・型契約と候補本文を candidateHash に束縛します。

完全な明示選択は [payment.mapping.json](../tests/fixtures/business-assistance/payment.mapping.json)、独立期待値は [payment.case.json.txt](../tests/fixtures/business-assistance/payment.case.json.txt) にあります。mapping の candidateHash はこの版・収録原文・設定に対する値です。別の入力/設定/版では現在の候補をレビューし、mapping を明示的に更新してください。古い値を黙って受け入れません。

```sh
dotnet src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll scaffold-business \
  out/payment --program PAYMENT \
  --mapping tests/fixtures/business-assistance/payment.mapping.json
dotnet build out/payment/CobolCS.Generated.sln -m:1
```

出力は contractId の完全 SHA-256 を含む安定した contract-key を使い、元の ID を管理来歴へ残します。PROGRAM-ID の API 名は既存の可逆 Identifier を使います。名前の大小文字、記号、Unicode、slug の衝突で利用者ファイルを共有しません。

- `UserCode/Business/<contract-key>Adapter.cs`：既存 API へ型付きで配線する利用者所有コード。
- `Tests/BusinessContracts/<contract-key>.json`：選択した境界だけの型契約。
- `Tests/Templates/Business/<contract-key>/case.json.txt`：未記入の入力・応答・期待値。発見対象外。
- 同ディレクトリの mapping、factory 登録追記、初回 hash 参照、README。
- `reports/business-scaffolds/<contract-key>.json`：選択・hash・出力・未記入箇所の来歴。`*-review-files.json` は現在の参考 hash。

合成例を有効化するには、case テンプレートを独立期待値の例で記入して `Tests/BusinessCases` へ置き、registration.cs.txt の1行を `BusinessRegistration.Register` へ追記します。独立の根拠を provenance/reviewReason に保存します。`prepare`、`dotnet test ... --filter Category=Business`、characterization の別レビュー、明示した業務レビュー行、`adopt` の順に確認してください。factory はケースごとに新しい Adapter を返します。adopt 後にサービスへ置換しても、ケース・契約・期待値を変えず同じ業務テストを使えます。

## mapping と変換の範囲

JSON の未知/重複キー、他 PROGRAM-ID、未知/重複 ID、異なる役割/型、古い hash、不正な外部 field を公開前に拒否します。任意 C#、反射、スクリプトを mapping に書けません。入力/結果/操作は明示選択が必須で、全選択を既定にしません。FILLER・添字・計数用の更新を業務結果と自動認定しません。

初期範囲は既知の Number/string、固定 LINKAGE グループ/表、選択した Snapshot/ArgumentsSnapshot/DISPLAY です。グループ内の `field_<symbolId>` は暫定 field 名です。グループ全体の選択は全 field と全表要素の明示選択を意味します。意味ある業務名への別投影は、利用者 Adapter/新契約でレビューしてください。表は宣言順の全要素を保持し、長さ不足を拒否します。部分表の自動配線は行いません。

`initialState` 選択は `input.testState` に分離します。WORKING-STORAGE の注入を正式 Arguments と混同しません。PIC は既存の格納規則で検査し、二進浮動小数点を使わず、固定 scale で完全に表せない値を勝手に丸めません。文字の trim、null→空文字も行いません。未知レイアウト/PIC・未知変換は生成拒否または Preparation.Required/明示例外です。

ACCEPT の静的候補順は実消費順とは限りません。既定は従来の固定キューで、条件・反復・段落移動には明示 acceptOrder が必要です。ケースごとに順序・回数・値を変える場合は acceptMode: "case-sequence" を選びます（acceptOrder との併用は禁止）。同じ symbol への複数 ACCEPT も mapping の statementId ごとに別の業務キーを選びます。

CALL/FILE/SQL は既存 ScriptedDependencies と BusinessExternalScript.BindNext を使います。mapping の request/response は既知 field を一対一で全て選択し、安定キーへ投影します。ケースの固定応答列が登録順を決めます。StatusCode/Outcome は必須、Optional 出力の null は更新なしです。EOF、SQL no-data、null 指示変数、BY REFERENCE/VALUE/CONTENT を保持します。不足・余剰・順序・payload の不一致は失敗し、外部 Stubs/実接続へ fallback しません。未対応外部・未知ブロックを含む Adapter は実行前に明示不足で止めます。

## 所有権と移行

生成する Adapter・契約・参考テンプレートは初回だけです。再実行・通常再生成でも既存バイトと削除を保持します。同じ契約の変更 mapping は管理来歴へ proposal を保存し、自動 patch をしません。別成果物を作る場合は新しい contractId を使います。version だけを変えても新しい Adapter は作られません。既存契約の変更は利用者が明示編集して再レビューしてください。生成中の原出力編集は stage 公開を拒否します。

参考 JSON の cases は、有効ケースごとの未承認レビュー行です。files は対象ケース・全業務契約JSON・ケースの参照Dataだけを固定し、Adapter/ヘルパー/内部スタブ/生成C#は implementationHashes へ分離します。元入力/設定/型契約も採用由来として保持します。現在の管理参照だけを再計算し、Tests/review.json の reviewed・承認 hash・期待値は変えません。初回の利用者所有 review-files.json.txt も保持します。

native 化で不要になった旧スタブ・補助コードを削除しません。reports/contract-changes.json の unused-preserved を確認してください。旧 API を参照するコードの互換を無条件に保証できない場合は、新しい生成先へ比較移植します。adopt 済みソリューションへ scaffold-business/再生成は行わず、通常の C# 編集と dotnet test を使います。

## 固定 ASCII 文字処理

Validation と source 生成は同じ CharacterCapabilities で文全体を判定し、生成 C# は IR を使わない SourceCharacters/SourceSupport を呼びます。

| 文 | 今回の native 条件 | 保持する意味 |
|---|---|---|
| INSPECT TALLYING | 固定 X/A、単一 CHARACTERS/ALL/LEADING | 末尾空白、左から非重複、既存 tally に加算して PIC 格納 |
| INSPECT REPLACING | 同上、CHARACTERS/ALL/LEADING/FIRST、同長 | 更新前の operand/添字を1回評価、長さと未変更部分を保持 |
| STRING | ASCII literal/固定 X/A、SIZE/値区切りの混在 | 最初の区切りを除外、順序、pointer、部分転送、suffix、overflow 分岐1回 |

INSPECT の複数操作・BEFORE/AFTER・CONVERTING、空 pattern、異長置換、国別/未知/REDEFINES/別名領域/参照変更は文全体スタブです。STRING の空 delimiter、受信領域と送信/区切りの別名、国別/未知/参照変更も文全体スタブです。既知配列要素の添字は既存範囲検査を使い更新前に捕捉します。既存の構造化実行と paragraph dispatcher の両方で確認します。

独立期待値の例：AAAA の ALL/LEADING AA は2、AAAAA の ALL AA は2、既存 tally 5は7、FIRST AA→BB の AAAA はBBAA、CHARACTERS は末尾空白も数えます。STRING の A,B は A だけ、先頭区切りは0文字、区切りなしは全送信、満杯ちょうどは overflow なし、追加送信で overflow です。

限定仕様の根拠は富士通公開 COBOL2000 の [INSPECT](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/inspect-statement-cobol-compiler-reference-manual-2022-11-226-22036932.html) と [STRING](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/string-statement-cobol-compiler-reference-manual-2022-11-244-22036953.html)。追加意味を推測せず、添付指示の限定仕様・固定期待値で検証しました。外部資料は実行依存にしません。富士通ホスト/JEF/全方言の互換や実機 COBOL 一致を意味しません。旧 interpreter の追加実装は行っていません。

UNSTRING は [pointer/COUNT/TALLY/overflow 等の独立仕様](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/unstring-statement-cobol-compiler-reference-manual-2022-11-246-22036955.html) が必要で延期します。広範な MOVE、編集 PIC、グループ byte MOVE、複合 EVALUATE、SEARCH、SORT も今回の範囲外です。

## 内部スタブの測定

生成先の reports/internal-stub-summary.json/.md と Tools evaluate の internal-stub-summary.json/.md は、文種・安定 reasonCode・executionRole・配置不明を集計します。内部、外部、未知を分け、完全なエラーメッセージを集計キーにしません。1展開文/プログラム/statementId を1件と数え、同じ COPY 原位置の物理件数を別に表示します。物理位置は各生成単位/固定 source case 内で入力 hash・file・start・kind を照合し、別 corpus case 間の物理ファイルを統合しません。

現在のコード/元入力/fixture hash に一致する実行記録だけを reached/firstStop とし、未実行・未到達・古い証跡を分けます。指紋・設定・入力・lock・コンパイラー hash・対象数を記録し、原文・値は集計へ含めません。Tools の分割は元 lock で統合します。未取得/未測定や missing COPY を成功に数えず、過去171件などの測定を最新値と呼びません。優先順位は実停止、影響プログラム、確定できる意味を見て決め、推測スコアで自動確定しません。

## ケースの記入とレビュー

`Tests/Templates/Business/<contract-key>/case.json.txt` は選択した input と expected.result の階層を展開します。scalar と expected.termination は null、応答列と effects は空の記入欄です。`response-examples.json.txt` は操作ごとの候補構造で、列挙順は実呼出し順ではありません。`case-guide.md` に JSON Pointer、型・PIC、固定要素数、根拠と元位置を示します。

固定表は100要素まで展開し、100を超える表は null と全要素数・要素例を案内します。深さ・サイズの上限に達した部分も null と不足案内を残します。全要素を記入してください。Optional外部出力の null は「更新なし」を意図して選ぶ有効値で、未記入と区別します。StatusCode／Outcomeは必ず明示します。必須 PROCEDURE USING 引数を選ばないと missing-formal-argument を案内し、Adapterの冒頭が Preparation.Required で停止します。

初回の利用者資料は編集・削除を保持します。最新提案は reports/business-scaffolds にあります。A→B→A は proposal プロパティを削除し、A→B→C は現在の一件をCに置換します。提案は参考資料のみで、実装・契約を自動変更しません。

ケース記入後に `prepare` を実行すると、管理側 `*-review-files.json` の cases に現在のhashと未承認レビュー行が出ます。業務テストを実行し、対象行の入力・応答・独立期待値・実装・来歴を明示確認してから reviewed=true と具体的な reviewReason を設定し、Tests/review.json の cases へ追加します。hasNoOpCalls=false の雛形値は実行証跡ではありません。adoptは今回の実行とTRXを引き続き要求します。古い参考一覧から実装パスを files へ入れた承認は移動先を診断し、明示再レビューを求めます。承認の自動書換えはありません。

## ケース別 ACCEPT

mapping に `"acceptMode": "case-sequence"` を指定すると、選択したACCEPTキーの値は配列、予約入力acceptSequenceは業務キーの消費順になります。

```json
{
  "input": {
    "customerCode": ["A001", "A002"],
    "amount": ["100.00", "200.00"],
    "acceptSequence": ["customerCode", "amount", "customerCode", "amount"]
  }
}
```

契約の任意metadataは `"acceptInputs": {"sequenceField":"acceptSequence","valueFields":["customerCode","amount"]}` です。共通 Strict／Inspect は参照先・型・重複・未知キー・回数を検査します。分岐で消費しないキーは空配列にします。数値は丸めない精密十進文字列で、PIC外・null・末尾空白を拒否し、文字の末尾空白を保持します。testStateとacceptSequenceは予約入力名です。

生成 Input.AcceptInput に IAcceptInput provider を指定すると、文の実行時に SourceLocation を渡します。AcceptInputs キューとの同時指定は禁止です。未指定なら従来キューです。Adapterだけが statementId→業務キーの配線を持ち、ケースの BusinessAcceptScript.Read(key) でそのキーの次の値を消費します。正常終了後だけ VerifyComplete を呼びます。未配線・順序違い・不足・余剰は accept-input-unmapped／accept-input-order-mismatch／accept-input-missing／accept-input-unused とJSON Pointerを失敗証跡に残します。Console・キュー・外部スタブへfallbackせず、ACCEPTを外部effectsへ加えません。

採用後のサービスも同じ契約とケース、BusinessAcceptScriptを使用し、生成文IDを使わず業務キーで消費できます。利用者所有の既存Adapterは自動patchされません。新しい境界は新contractIdで生成するか、明示編集と再レビューで移行してください。

RENAMES は既存IRの保持された宣言と親子関係から参照先の01領域を証明できる場合だけ影響範囲を限定します。独立した01のINSPECT／STRINGは他の既存条件を満たせばnativeです。共有領域・不明範囲・曖昧な参照先・不明配置は拒否します。これはレベル66自体の実行対応追加ではありません。

反復・分岐の固定合成例は `tests/fixtures/business-assistance/accept-sequence.cbl` と `accept-sequence.mapping.json.txt` です。mappingのcandidateHashは生成後の現在値を明示転記してください。これは未記入参考mappingで、そのまま実行するものではありません。E2E回帰はこのCBLから独立期待値を固定し、dispatcher版も検証します。
