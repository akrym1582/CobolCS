# MSTest再試行・グループCALL修正の受入記録

2026-10-12、SDK 10.0.401、Linux。基準HEADは `f1fc6e87019e5e6971456be91b4b5da91b98d5da`。これは新しい記録であり、以前の14プログラム・249統合試験の[実測結果](branch-test-generation-results.md)と `verification.json` は変更していません。機械可読記録は [retry-group-verification.json](../tests/branch-generation-results/retry-group-verification.json) です。

修正前の製品バイナリに対して再現試験を実行し、SDK復旧後も同一runの未完了結果を再利用する失敗、グループ配下の基本項目がresponseItemsにない失敗の2件を確認しました。

| 検証対象 | 実測結果 |
|---|---|
| SDK不足 → 復旧 | 同じrunId `3f887f0a6b6b114c`、0件／complete=false → 2件／complete=true。前回レポートを `attempts/0001/` に保存 |
| 部分保存後の失敗 → 再試行 | 子dotnetをテスト内のプロセス境界で制御して最初の1件保存後に失敗。再試行で保持1件・確認済み再利用1件・新規1件、合計2件。ID衝突・重複なし、固定MSTest成功 |
| 部分ケース編集後の新規追加 | 編集済みケースとreviewのエントリを保持して新規1件を追加。元の来歴hashを保持し、新規ケースは成功。編集後のhashへreviewを更新した未レビューケースはprovenance mismatchで拒否 |
| 未完了の編集・削除 | ケース／baselineのバイト、reviewed・reviewReason、レビューエントリ、削除状態、元の来歴を保持。同一ケースの別ID再作成なし。最新summaryとmanifestが未完了で一致 |
| 原子的反映 | プロセス境界で反映前の元出力編集とMSTest中のstage編集を発生させ、元出力を保護。管理レポートの改変・欠落も拒否 |
| 完了runの再利用 | ケース数・証跡を変えず再利用。別seedのrunが既存ケースだけを再利用した場合も、元runの来歴を検査して再生成可能 |
| グループCALL | 下記の99.99・100.00・100.01を通常generateで試行。入れ子の子項目を合成応答へ設定し、IFのtrue/falseをtraceで確認 |
| 手動応答 | 子項目・固定表・RETURNINGを受理。未知ID・グループルート・88条件・BY CONTENT／BY VALUE専用項目・符号／桁／scale違反・文字長違反の9候補を拒否 |
| 採用 | GROUPBRANCHの応答子項目・依頼・snapshot・HIGH/LOWの独立合成仕様を確認し2件をレビュー。adopt成功、元生成先を移動した状態で2件のMSTest成功 |

合成fixtureは `tests/fixtures/branch-generation/group-call.cbl`。`RESULT-AMOUNT` は `f000003`、PIC 9(5)V99で、BY REFERENCEの入れ子グループに属します。runId `3e9ec4086e07ab54`、36プロセス試行（固定再生を含む）、保存ケース2件、安定した完了ケースで3/4の計測可能outcomesに到達しました。残りのCALL Exceptionは例外ハンドラーがなく不完全実行となるため、partial-onlyの診断として残し、成功baselineには採用していません。生成・固定再生のcomplete=trueは全枝網羅を意味しません。

```json
{ "responseValues": { "f000003": "99.99" } }
{ "responseValues": { "f000003": "100.00" } }
{ "responseValues": { "f000003": "100.01" } }
```

全て合成入力と生成C#のcharacterizationです。元COBOLとの等価性、到達不能証明、全経路網羅は主張しません。スカラーCALL・RETURNING・FILEレコード→EOF・SQL正常／no-data／異常は既存の `OrdinaryGenerateExploresAndReplaysSyntheticPrograms` でも検証します。製品・試験に新しいネットワーク依存は追加していません。

## 回帰検証

ビルドは警告・エラー0、整形検査成功、Runtime 12 assertions、Frontend 194 tests、全Integration 254 tests成功（skip 0、20分21秒）。親の `MSBUILDDISABLENODEREUSE=0` で実行しました。探索の再試行により変わるreportと管理履歴のhashを従来の手動テンプレートの決定性比較から除き、他の管理ソース・利用者ファイル・レビューの保持検査は維持しています。

```sh
dotnet build CobolCS.sln -m:1 --no-restore
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build --no-restore
MSBUILDDISABLENODEREUSE=0 dotnet test tests/CobolCS.IntegrationTests --no-build --no-restore --logger 'trx;LogFileName=integration.trx'
git diff --check
```

全統合試験の後に別runの借用ケースを保持件数へ重複加算しない集計を調整し、関連する分岐生成試験12件を再実行して全件成功（skip 0、1分59秒）を確認しました。独立した生成MSTest・グループCALL採用後のMSTestは上記の合成受入試験で実行しています。

---

[ドキュメント案内](README.md)へ戻る。
