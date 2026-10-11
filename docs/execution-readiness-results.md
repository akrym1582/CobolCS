# 実行準備拡張の検証記録

実測日: 2026-10-11。Debian 13 / Linux x64、.NET SDK 10.0.401、Python 3.12.14。
基準コミットは `894d10f08f7d4de7ebc34c6d04231c23dfa5abb4`。
開始時の作業ツリーは未変更で、古いHEAD `8a516ef` を基準版へfast-forwardして着手しました。

基準版を別ディレクトリへ展開し、既存試験をすべて実行して成功を確認しました。
当初のサンドボックス内実行はプロキシ接続・MSBuildローカル通信制限で失敗したため、
必要な権限で再実行しました。これらの環境エラーを製品の失敗件数には加えていません。

## 最終コマンド

| コマンド | 結果 |
|---|---|
| `dotnet build CobolCS.sln -m:1` | 成功、警告0・エラー0 |
| `dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore` | 成功 |
| `dotnet run --project tests/CobolCS.Tests --no-build` | 12 assertions成功 |
| `python3 tests/regression.py` | 60件成功 |
| `python3 tests/test_public_corpus.py` | 12件成功 |
| `python3 tests/test_remaining18.py` | 20件成功 |
| `python3 tests/test_workflow.py` | 1件成功 |
| `python3 tests/test_workflow_external.py` | 2件成功 |
| `python3 tests/test_workflow_safety.py` | 3件成功 |
| `python3 tests/test_workflow_noop.py` | 2件成功 |
| `python3 tests/test_execution_readiness.py` | 8件成功 |
| `git diff --check` | 成功 |

Python試験は計108件。新規8件は生成プロジェクトと別assemblyのMSTestを実際にビルド・実行します。
既存の「基本INITIALIZE／STRINGはスタブ」という期待だけを今回のnative結果へ更新し、
未対応派生句のスタブ試験を追加しています。試験の削除・skip・警告の一括無効化は行っていません。
利用者編集用テンプレートにも通常のStyleCop/MSTest analyzer検査を適用しています。

## 受入の内容

- 固定LINKAGEグループ、精密金額計算、数値スカラーBY VALUE、固定表引数、必須入力不足、0・空文字、結果snapshot、入力配列との分離。
- 固定グループCALLのmatcher、Captured、typed trace、BY REFERENCE更新、更新省略、BY CONTENTの出力禁止、PIC／表要素数検査、別名・親子・RETURNING重複の拒否。
- INITIALIZEの非ゼロVALUE、88、FILLER保持、入れ子・固定表・選択要素、PIC Nのportable空白。
- STRINGの複数送り、空文字、POINTER省略、正負・範囲外ポインタ、ちょうど収まる転送、途中overflow、受信領域保持、overflow内の段落移動、句省略、未到達の未対応文。
- ACCEPT/CALL/FILE/SQLのレポートと初回ビルド。未記入テストの失敗、正常・EOF/no-data・失敗・NULL指示変数は既存外部契約試験も実行。
- ARGCALCの独立期待値補完、入力変更時のreview hash不一致、編集バイト列と削除保持、reviewed非NoOpケースのadopt、元生成先を移動した後のbuild/test/run。
- 採用後の乗算を手書きサービスへ抽出して同じテストに成功。乗算を加算へ変えた場合は同じテストが失敗し、試験用変更は復元。
- 基準版で実際に生成したスカラーCALLの編集済みスタブ／NoOpヘルパーを新生成器へ移行。同一レイアウトはバイト列保持、PIC変更は拒否の2条件を別途確認。

`out/readiness-argcalc` に補完済みデモ、`out/readiness-adopted` に採用済みデモを作成しました。
前者のsample実行ではAMOUNT=100、RATE=0.25、TOTAL=25、終了Gobackを確認しています。
これらはgit管理外のローカル確認用出力です。[再作成手順と編集先](execution-readiness.md)。

## 固定公開コーパス

既存のcourse/fun/bank/carddemo/db2 lockを変更せず、同じ取得データ、SDK、
`tools/public_corpus.py evaluate --build` と `verify` を使用しました。
基準版と処理系・契約確定後の固定ソースコピーで測定しています。
その後の未対応外部依存のレポート分類修正は、ビルドと準備レポート／未対応領域の2件を再試験しました。
この修正は生成する実行コード・型契約には影響しません。測定時のソースhashはJSONに保持しています。

| 指標 | before | after |
|---|---:|---:|
| 対象 | 171 | 171 |
| strict生成 | 5 | 5 |
| scaffold生成 | 166 | 166 |
| 生成物ビルド成功 | 171 | 171 |
| fixture付き実行完走 | 3 | 3 |
| 独立期待値一致 | 3 | 3 |
| 原COBOL採取値との比較 | 未実施 | 未実施 |
| 未対応内部文 | 5,005 | 4,900 |
| 未対応外部文 | 892 | 868 |
| 入口・初期値等のその他未対応スタブ | 407 | 387 |

ビルド内訳はcourse 30、fun 2、bank 43、carddemo 44、db2 52で、すべて維持しました。
未対応文は採用されたstrictまたはscaffold出力の `capability=unsupported` を、programId/statementId単位で数えます。
`statement.kind=external` は外部文へ分離し、通常文ID以外の入口・初期化補助等は別集計です。
未到達を意味する件数ではありません。未編集スタブ付きの探索実行の完走は0件でした。
**171本ビルド成功を171本業務実行成功とは扱いません。** 新しい合成例の成功もこの3件へ混ぜていません。

[コマンド・集計JSON](../tests/execution-readiness-results/verification.json)、
[beforeのケース別記録](../tests/execution-readiness-results/before.json)、
[afterのケース別記録](../tests/execution-readiness-results/after.json)に測定条件と結果を保存しました。
同じディレクトリのログで試験結果とデモを確認できます。

## 未実施と残る範囲

Windows実行はこの環境では未検証です。Windows用の生成・実行スモークCIジョブを追加しました。
原COBOLコンパイラは未導入で、原処理系の参照実行・業務同値性は未検証です。構文規則は一次資料、
値は独立した合成仕様／手計算で確認しています。外部DB・ホスト接続は行っていません。

REDEFINES、RENAMES、可変OCCURS、ポインタ、未知配置、選択したグループ表要素のCALL、
宣言側BY CONTENT、run-unit寿命、UNSTRING、INSPECT、CORRESPONDING、SORT等は今回の対応外です。
詳細は[対応範囲](supported-subset.md)と[値契約](execution-readiness.md)を参照してください。
