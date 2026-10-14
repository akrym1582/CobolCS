# 許容解析の検証記録

2026-10-14、基準コミット `d3cb09e` に対する作業差分を Linux / .NET SDK 10.0.401 で検証。
機械可読の件数と証跡hashは [検証結果JSON](tolerant-parsing-results.json) に保存する。

## 実装した契約

source/scaffoldとanalyzeは許容解析を既定にする。`--syntax-recovery standard` で従来規則へ戻せる。
解析APIとstrict/interpreterの既定はStandard。strictにTolerantを明示しても復旧エラーのあるprogramは拒否する。
未知文・失敗ブロック・未知ヘッダーを原文付きスタブにし、失敗宣言の部分PIC・VALUEを破棄して未知配置を伝える。
共有IRの変更を取り消して再開し、未知処理を自動で正常なNoOpへ置き換えない。
再生成は解析方針を引き継ぎ、生成C#と実行設定が同じ場合は探索runを再利用して元の証跡を保持する。
同種の入れ子でELSEや終端が曖昧な失敗は親全体をスタブ化し、内側の文を外側のnative本文へ漏らさない。
PROGRAM-IDや入力デコード、プログラム境界の重大エラーは拒否する。

## 回帰結果

| 対象 | 成功 / 全件 | 状態 |
| --- | --- | --- |
| runtime | 47 / 47 | passed |
| frontend | 222 / 222 | passed |
| integration | 378 / 378 | passed |
| tolerantGeneration | 10 / 10 | passed |
| courseFrozenIr | 1 / 1 | passed |

TolerantParsingTestsは自作最小入力による28件。TolerantGenerationTestsは10件。
未知範囲、入れ子、ELSE、段落、EOF、状態取消、日本語COPY、文字コード、宣言とヘッダーを検証する。
独立C#ビルド、既知経路の独立期待値、未知到達時の失敗、strict拒否、CLI引数、再生成の編集保持を確認する。
統合試験はCIと同じ6分割を実行し、最終調整後に関連クラスを再実行して、各ケースの最新結果と全発見一覧の多重集合を照合する。親の `MSBUILDDISABLENODEREUSE=0` で実行する。
ビルドは警告・エラー0件。書式検査と `git diff --check` も確認する。
固定期待IR、baseline、コーパスlockは更新していない。

## 設定リファレンスの確認

[config.json 全設定](configuration.md)にトップレベル23項目、探索設定5項目、手動候補5項目の候補値・既定値・制約を記載した。
実装から抽出した既定値と全項目例を照合し、全キーの表への掲載、4つのJSON例の構文とCLI analyzeでの設定受理、案内ページのリンクを確認した。
既存のConfigurationTestsとBranchGenerationConfigurationTestsを再実行し、30件すべて成功した。
候補の設定受理と対象プログラムでの探索成功を区別し、ホスト依存設定の生成制約・予約項目・32ビットの実行上限も記載した。

## 公開教材の測定

courseの固定lockから取得した30本すべてを生成・独立ビルドできた。
strict生成5本、許容scaffold生成25本。登録済みの独立期待値3件が一致し、補完付きfixtureの実行1件も成功した。
生成数下限26本、全生成物のビルド、登録期待値をTools verifyで検証した。
courseの固定IRハッシュ照合も成功した。原文と生成物はコミットせず、cacheとoutへ保存する。
他の4コーパスは今回未実施。元COBOLの実行結果との照合は行っていない。

## 再現コマンド

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet test tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
MSBUILDDISABLENODEREUSE=0 dotnet test tests/CobolCS.IntegrationTests --no-build
dotnet run --project tools/CobolCS.Tools --no-build -- fetch --lock tests/public-corpus/course.lock.json --root .cache/public-cobol-corpus/course
dotnet test tests/CobolCS.Frontend.CorpusTests --no-build --filter FullyQualifiedName~Course
dotnet run --project tools/CobolCS.Tools --no-build -- evaluate --lock tests/public-corpus/course.lock.json --root .cache/public-cobol-corpus/course --out out/tolerant-course-final-checked --build
dotnet run --project tools/CobolCS.Tools --no-build -- verify out/tolerant-course-final-checked/summary.json
git diff --check
```
