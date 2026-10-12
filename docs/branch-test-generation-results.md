# 分岐探索からMSTestまでの実測

.NET SDK 10.0.401、Linux、基準HEAD `42010af` 上の実装差分で検証しました。固定IR、既存期待値、コーパスlockは更新していません。

通常generateで合成14プログラムを処理し、192回のプロセス実行（固定再生を含む）から31ケースを保存しました。保存したMSTest31件は全件成功し、TRXを実測しました。安定再生による到達outcomeは55/60です。未知型の2 outcomeは計測可能分母に含めません。

| 合成例 | 試行数 | 到達／計測可能 | 保存ケース |
|---|---:|---:|---:|
| AFFINE | 11 | 2/2 | 2 |
| BLOCKEDCASE | 4 | 0/0 | 0 |
| BOUNDARY | 11 | 2/2 | 2 |
| BRANCHCALL | 24 | 6/6 | 4 |
| CONTROLCASE | 15 | 10/10 | 3 |
| DECIMALBOUND | 13 | 2/2 | 2 |
| EXITLOOPCASE | 27 | 4/5 | 2 |
| BRANCHFILE | 17 | 10/10 | 4 |
| FLOWCASE | 11 | 5/5 | 3 |
| LIMITCASE | 18 | 2/5 | 1 |
| OVERFLOWCASE | 8 | 2/2 | 2 |
| OVERWRITECASE | 6 | 1/2 | 1 |
| SIZECASE | 8 | 2/2 | 2 |
| BRANCHSQL | 19 | 7/7 | 3 |

9999／10000／10001、小数12.33／12.34／12.35、減算逆算1099／1100／1101、AND／OR／88条件、EVALUATE、段落PERFORM／GO TO、0／1／2回、SIZE ERROR、STRING OVERFLOW、CALL正常／異常、FILEレコード→EOF、SQL正常／no-data／異常を保存テストまで検査しています。

未解決は定数上書きのtrue、必ず1回でEXIT PERFORMする例の複数回、更新しないUNTIL例の継続／1回／複数回です。継続の途中traceは制限到達として診断に保存し、正常回帰ケースには昇格しません。未知型はblockedで0ケースです。これらを到達不能の証明や生成完了とは報告していません。

別のAFFINE出力では未レビューadoptの拒否を確認し、整数減算と条件を独立に照合して理由とhashを整えると、2ケースの採用と採用先単独MSTestが成功しました。統合受入試験は元生成先を削除後に計算をメソッドへ抽出しても成功し、減算を加算へ壊すとAssert.AreEqualの不一致になることを確認しています。TRX同名、未レビュー、NoOp、skip／0件、不足・不一致・余剰fake、trace上限、プロセスtimeout、SDK不足、同一条件の決定性、編集・削除保持と異なるseedの重複再利用も回帰で検査します。

ビルド、whitespace、Runtime 12検査、Frontend 194件、統合249件（18分3秒、失敗・skipなし）が成功しました。親のMSBUILDDISABLENODEREUSE=0でも実行し、子プロセスのノード再利用無効化を検査しています。詳細は `tests/branch-generation-results/verification.json`。生成物と実TRXはローカル `out/branch-generation-acceptance/`、採用例は `out/branch-generation-adopted/` にあり、生成C#をコーパス由来の固定期待値として採用していません。
