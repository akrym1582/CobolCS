# 業務支援・内部スタブ削減の検証記録

2026-10-12、Linux、.NET SDK 10.0.401。実装開始は `feat/xunit-business-test-four-fixes` の `240559168df4e682455afb2cafa87a4a8eb11a9d`（[PR #18](https://github.com/akrym1582/CobolCS/pull/18)）。xUnit 移行 `efdd391d27bc57d5802f3674fa72fefd47d19166` は main の #17 に統合済みです。後続実装は `feat/business-test-assistance-internal-stubs` に分離しました。固定パッケージは xUnit 2.9.3、VSTest adapter 3.1.5、Test SDK 18.0.1 のままです。

## 実装と受入

| ID | 根拠 |
|---|---|
| B1 | payment の ACCEPT/算術/CALL から、既存型・PIC・元位置付きの管理候補を生成。原入力/config/型契約と候補 hash を束縛 |
| B2 | BusinessAssistanceTests.FixedLinkageGroupAndTableKeepOrderAndPic：正式 Arguments の固定グループ/表と、分離した試験注入表を実行。要素順・完全長・PIC を保持 |
| B3 | MappingValidationAndUserBytesArePreserved：未知 ID、他 program、古い hash、型/役割、重複キー/ID、未知 payload/任意式を拒否し原出力一致 |
| B4 | 生成 Adapter を実 solution build。未記入ケースは Templates のままで有効ケース0 |
| B5 | PaymentScaffoldToAdoptionAndServiceReplacement と保存した payment-final：明示 factory、独立計算、個別 xUnit、レビュー、実 adopt |
| B6 | BusinessExternalScaffoldTests：typed FILE EOF、SQL no-data/指示変数、Optional の更新なし。不足/余剰/順序/payload の負ケース。CALL の payload 負試験と編集外部 Stubs の遮断も確認 |
| B7 | 再実行・変更提案・削除・共通 stage の編集競合で利用者バイトを保持。旧補完 fixture と既存再生成試験も維持 |
| B8 | 現在の管理 hash 参照だけを更新し、Tests/review.json と利用者テンプレート・期待値を保持 |
| B9 | adopt 後に Adapter を料金サービスへ置換し、同じケース・契約・期待値の Category=Business を再実行 |
| I1/I2 | SourceCharacterTests：末尾空白、非重複 ALL/LEADING、既存 tally 加算、CHARACTERS/ALL/LEADING/FIRST の固定期待値 |
| I3/I4 | CharacterGenerationTests：国別・複数操作・BEFORE/CONVERTING・異長・参照変更・別名・RENAMES/未知配置は文全体スタブ。配列添字を1回捕捉、PIC 格納と後続 IF |
| S1–S3 | SourceCharacterTests と生成4プログラム：SIZE/値区切り混在、先頭/なし/複数文字、pointer 境界、ちょうど満杯、部分転送/suffix、構造化/dispatcher の分岐1回 |
| S4 | dispatcher の未対応 STRING も元の stringTransfer IR/全句/理由を保持。国別・空区切り・別名・参照変更を部分 native 実行しない |
| M1 | 同一3 CBL/同一設定を4点修正のコンパイラーと追加版で生成し、位置/理由/実停止を比較。下表と固定測定 JSON |
| M2 | InternalStubMetricsTests と corpus の既存検査：未取得/未測定を成功に数えず、COPY 原位置と展開数を分ける |
| M3 | 補完した旧 INSPECT スタブ/ヘルパーを native 再生成後も保持。contract-changes/worklist に unused/比較移植を案内し実 build/run |
| E1 | 元 completion/payment.cbl は内部 INSPECT スタブ不要。料金15.00と固定 fake 結果を維持。未対応補完は別 payment-completion.cbl の BEFORE 句へ分離 |
| E2 | 料金係数・文字カウント・値区切りを意図的に破壊し、固定期待値を変えず不一致を検出。採用後のサービス置換も独立ケースで検証 |

## 同一固定範囲の測定

収録 [characters.cbl](../tests/fixtures/business-assistance/characters.cbl)、[linkage.cbl](../tests/fixtures/business-assistance/linkage.cbl)、[unsupported.cbl](../tests/fixtures/business-assistance/unsupported.cbl) の3本です。`testGeneration.maxAttempts=4` を共通にし、4点修正 SHA の CLI を `/tmp` の独立コピーで build して before、新実装を after としました。元入力 hash・config hash が一致することを確認しています。

| 項目 | before | after |
|---|---:|---:|
| 内部スタブ（展開文） | 5 | 2 |
| INSPECT | 3 | 1 |
| STRING | 2 | 1 |
| 内部スタブを持つ program | 2 | 1 |
| 実測の最初の内部停止 program | 2 | 1 |

減った3文は BUSINESS-CHARACTERS の行8/9の INSPECT と行10の STRING。影響1 programです。残る2文は BUSINESS-UNSUPPORTED の BEFORE 句と receiver/delimiter の別名です。before は最初の INSPECT で CLI 終了2、after は `NORMAL` / `BBAAA07aXZdef04` に独立期待値と一致して終了0。残る未対応 program は after も最初の INSPECT で停止し、後続 STRING は未到達です。LINKAGE の正式入力検証は別の業務 xUnit で行い、この到達測定から全入力実行を主張しません。

完全な原文なしの位置/理由/件数/到達/入力 hash と compiler assembly hash は [fixed-stub-reduction.json](../tests/measurements/business-assistance/fixed-stub-reduction.json)。物理文は生成単位/固定 source case 内で hash/file/start/kind を照合し、COPY 展開の複数 instance と区別します。別 corpus case の物理ファイルは統合しません。以前の171入力や MOVE 2373件などの歴史値を、今回の最新測定として使っていません。

元 payment はこの3本とは別の E1 受入です。独立手計算 `1000.00 * 0.015 = 15.00`、AB の A が1個、固定合成 lookup の OK 応答を使います。[完全 mapping](../tests/fixtures/business-assistance/payment.mapping.json)、[独立ケース](../tests/fixtures/business-assistance/payment.case.json.txt)、[型付き Adapter 参考](../tests/fixtures/business-assistance/payment.adapter.cs.txt)、[手順/対応範囲/一次資料](business-test-assistance.md)。mapping の hash は収録原文・設定・候補に対する値であり、外部 COBOL 採取の真正性を示すものではありません。

## 実行した検証

solution build は警告/エラー0、format verify と git diff --check は成功。Runtime 47件、Frontend 194件は全件成功、skip 0です。Integration の全件コマンドは304件成功（34分26秒）。全件開始後の compiler 集計の出典強化・正式引数の候補制限は関連43件で再確認し、その後の未対応 STRING dispatcher の元IR保持と RENAMES/未知配置の拒否・追加1件は関連17件で最終確認しました。TRX の testId ごとに最新結果を照合し、現行の全305 ID が成功、skip/失敗0です。最後の unused 案内では、まだ呼ばれる CALL NoOp ヘルパーを誤って unused と表示しないよう修正し、関連3件と payment の同一再生成（unused 案内なし）で確認しました。同じ変更で全件を何度も繰り返さず、最終差分の影響範囲を再実行しています。

VSTest の現行 discovery と最新305 IDを、CI の6相補フィルタに照合しました。branch 12、regression-a 34、regression-b 26、generation 53、workflows-a 19、workflows-b 161、重複/漏れ0。追加9件は workflows-b に含まれます。CI の全6ジョブの再実行と GitHub 上の CI は、この後続変更ではローカル未実行です。

実行コマンド（親 MSBUILDDISABLENODEREUSE=0）：

```sh
dotnet build CobolCS.sln --no-restore -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet test tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build --filter '<関連43件/最終17件のクラス条件>'
dotnet test tests/CobolCS.IntegrationTests --no-build --list-tests
git diff --check
```

ローカル証跡は out/verification/assistance-{runtime,frontend,integration,final-affected,final-native}.trx、assistance-verified-summary.json、assistance-ci-coverage.json。固定測定は out/business-assistance の before-final/after-final、原文なしの共有記録は上記コミット対象 JSON です。後続変更は作業木で検証し、実行バイナリの assembly hash と元入力/config hash を保存しています。

保存した生成先 `out/business-assistance/payment-final` は実 build（警告0）、Category=Business（Integrity Fact + 独立ケース1行の計2行）、adopt が成功し、採用先 `payment-adopted` に業務証跡が保存されています。統合試験内でも実 adopt/サービス置換を実行します。期待値・固定IR・既存コーパス lock を観測結果で更新していません。

公開コーパス5群・過去171入力の最新再取得/再測定、Windows、実機 COBOL 比較は今回未実行です。収録・合成範囲を固定して検証しました。一次資料の追加意味をネットから推測せず、添付指示書の限定仕様と独立固定期待値を根拠に実装しています。

スタブ減少、生成 build、固定 fake 実行、独立期待値一致はそれぞれ上の測定/実行で確認しています。実機 COBOL 一致、本番接続、全方言/JEF/物理レコード互換、業務正解の自動確定は確認していません。

次の候補は、固定範囲で実際に停止する BEFORE 等の INSPECT 句と別名領域です。広範な MOVE/編集PIC/byte group MOVE、複合 EVALUATE、UNSTRING、SEARCH、SORT は、より広い固定入力の理由別・実停止の測定と独立仕様をそろえて別作業で選びます。旧スタブ/ヘルパー/利用者 API は削除せず、新しい生成先との比較移植を案内します。
