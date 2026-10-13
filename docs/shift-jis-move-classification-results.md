# Shift_JIS 診断と MOVE 分類の検証記録

開始基準は `359c0a4205bde3f821fd53c553327e0d2cf3ad22` (`origin/main`)。この変更は source backend の MOVE native 対応を増やさず、診断と測定メタデータを改善する。

## 入力診断

Shift_JIS では、CP932 デコーダーが後方の不完全符号を先に検出した場合、そこまでの有効な CP932 接頭部を Shift_JIS として再検査する。前方の Windows 拡張が不正なら、その最初の元バイト位置を返す。診断が使う接頭部は選択文字コードで成功するため、`INPUT_DECODE` の作成中に例外を漏らさない。元入力の `sources.bytesHex` と `sha256` は変えない。

独自の合成バイト `874081`、`ed4081`、`8081` を CBL 本体と COPY の両方で検査する。Shift_JIS は先頭の 87/ED/80、CP932 は末尾の 81 を報告する。正常 CBL と不正 CBL を同じディレクトリに置く回帰では、正常プログラムと不正側の `INPUT_DECODE` を両方確認する。既存の UTF-8 厳密検査、CP932 重複符号と6文字の Shift_JIS 対応差も通常試験で確認する。

## MOVE 測定

`reports/stubs.json` の MOVE 文には `moveClassification` を追加する。`primaryReason` は `unknown-layout`、`alias-layout`、`group-storage`、`edited-pic`、`unsupported-expression`、`numeric-to-text`、`other` の優先順で一つ選ぶ。`reasons` は同じ順の重複なし配列で、複数受信先から得た理由を保持する。既存の `reasonCode` は維持する。生成先と Tools evaluate の `internal-stub-summary.json` / `.md` には、主理由別、重複理由別、理由の組合せ別の件数を出す。過去の分類なしレポートは `classification-unavailable` と表示する。集計は case/program/statement の組で重複を除く。

`numeric-display-to-pic-x` 候補は、既知の単純数値 PIC を持つ DISPLAY 基本項目から、全受信先が既知の固定長 PIC X 基本項目への文だけとする。数値 literal、式、関数、COMP 系、P、編集 PIC、別途 SIGN 句、JUSTIFIED、グループ、表、部分参照、未知配置、別名を除く。候補には PIC、digits、scale、signed、usage、受信長、原位置、programId、statementId を記録し、原文と実データ値を含めない。signed と scale、受信長は集計でも区別する。候補状態は `requires-semantic-review` とする。

生成された C# の native MOVE 判断と到達時のスタブ停止は変えていない。`moveClassification` の追加で管理ファイルのハッシュは変わり得るが、それは実行意味の変更ではない。次期 native 対応前には、送信表現、暗黙小数点、符号、桁埋め、受信長超過時の切詰め、空白埋めについて、元 COBOL 実行結果または独立した仕様・期待値で確定する必要がある。生成 C# の `ToString` や観測値を独立期待値として採用しない。

## 検証範囲

合成回帰は `JapaneseParserTests` と `MoveClassificationTests` にある。固定公開5群171入力のキャッシュはこの作業環境に存在しないため、実 CBL の分類件数、候補件数、影響プログラムは未測定である。既存の集約 JSON から詳細 PIC や候補件数を推測しない。固定 lock、入力 hash、設定、COPY、探索予算と過去の before/after は変更しない。実測する場合は取得済み原文を `.cache/public-cobol-corpus` に置き、Tools `evaluate --build` の新しい出力先 `out` に保存する。共有結果には compiler SHA、assembly hash、入力・設定・lock hash、取得・生成・分類件数を含める。

## この変更の実施結果

.NET SDK 10.0.401 / Linux で次を実行した。

| 検証 | 結果 |
| --- | --- |
| `dotnet build CobolCS.sln -m:1` | 成功、警告0、エラー0 |
| `dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore` | 成功 |
| `dotnet test tests/CobolCS.Tests --no-build` | 47成功、失敗0、skip 0 |
| `dotnet test tests/CobolCS.Frontend.Tests --no-build` | 194成功、失敗0、skip 0 |
| Integration の `JapaneseParserTests`、`MoveClassificationTests`、`InternalStubMetricsTests`、`CorpusShardTests` | 69成功、失敗0、skip 0 |
| MOVE 候補の未補完スタブ到達回帰（最終変更後の再実行） | 1成功、失敗0、skip 0 |
| 分岐生成で全体実行中に失敗した2件の単独再実行 | 2成功、失敗0、skip 0 |
| `git diff --check` | 成功 |

`dotnet test tests/CobolCS.IntegrationTests --no-build` の全体実行は45分を超えて完了しなかったため、CIの1ジョブ上限に合わせて中断した。実行中に `BranchGenerationTests.OrdinaryGenerateExploresAndReplaysSyntheticPrograms` と `BranchGenerationTests.DeterministicRunsPreserveUserEvidenceAndDeletions` の2件が失敗した。前者は生成テスト再生の `dotnet` 制限時間超過、後者は生成ケース比較の不一致だった。同じ2件は単独再実行で両方成功した。全体実行の完了件数は確定していないため、全件成功とは扱わない。CIの6分割試験と固定171入力の再測定は未実施である。
