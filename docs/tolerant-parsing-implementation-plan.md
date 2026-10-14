# 未知構文を含むCOBOLの解析継続とC#生成の実装計画

基準コミットは `d3cb09e`（2026-10-14にorigin/mainから取得）。実装と検証結果は [許容解析の検証記録](tolerant-parsing-results.md) を参照。
未知構文や一部の構文エラーがある入力でも、復旧可能な範囲を解析してC#と補完用スタブを生成する。
既知部分を維持し、未知部分の原文・位置・理由を残す。「無視」は解析・生成を中断しない意味とし、実行時の無条件スキップにはしない。

## 現状と変更の対象

| 対象 | 現状 | 変更 |
| --- | --- | --- |
| FrontendAnalyzer.Analyze | ParseExceptionが外へ出ると、当該ファイルのprogramを追加せず、全tokenをrejectedTokensへ登録 | 部・宣言・文の境界で復旧し、構築できたprogramを返す。最外周の例外処理は最後の拒否として維持 |
| Parser.procedure.cs M_block | 未知文をunknownへ変換。既知verbやピリオドまで進む。特定のブロックは同じverbの深さで走査 | 外側の終端・段落境界を保護する共通復旧処理へ置換 |
| Parser.declarations.cs | 一部の未知宣言は復旧するが、既知宣言内の例外は全体拒否へ波及し得る | 宣言単位で復旧し、特定できた項目を未知レイアウトとして保持 |
| SourceBackend.Statements.cs | 未対応文はStatementStubへ変換 | 復旧したunknownを既存スタブ経路へ接続。制御・宣言の未知も生成前に整理 |
| Generator.cs / Validation.cs | source/scaffoldは生成、strictは診断に基づいて拒否。重大エラーは生成不可 | 復旧済みIRを受け付け、未復旧の構造だけ生成拒否。復旧診断を消さない |

## 公開契約

解析APIへ省略可能な最終引数 `SyntaxRecoveryPolicy` を追加する。値は `Standard` と `Tolerant`。
既存API呼び出しの既定はStandardで、固定期待IRと既存の復旧動作を維持する。
CLIには `--syntax-recovery standard|tolerant` を追加する。
source/scaffoldの既定はtolerant、strictとinterpreterの既定はstandard、analyzeの既定はtolerantとする。
interpreterでtolerantを指定した場合は引数エラーとし、今回の拡張はsource生成を対象にする。
strictでtolerantを明示した場合は解析を続けるが、復旧診断があるprogramの生成は拒否する。

```sh
cobolcs sample.cbl -o out/sample
cobolcs analyze sample.cbl --syntax-recovery tolerant -o out/analysis
cobolcs sample.cbl --mode strict --syntax-recovery tolerant -o out/strict
cobolcs sample.cbl --syntax-recovery standard -o out/legacy
```

CLIヘルプ・analyzeの引数許可・API XMLコメントを同時に更新する。
解析方針はIRとmanifestの追加メタデータへ保存し、IRのartifact hashへ反映し、探索レポートにも方針を記録する。探索run IDは生成C#と実行設定で決め、方針だけの違いでは既存の固定ケースを分断しない。
StandardのIRには新しい既定フィールドを補わず、既存の設定JSON・configHashを変更しない。
IRは追加フィールドで拡張し、互換性を確認してschemaVersion/frontendVersionの更新要否を決める。

## 実装順序

### 1 復旧用の状態管理と境界判定

`Parsing/Parser.Recovery.cs` を追加し、Parser部分クラスに次の責務を分離する。

- 文・宣言の開始時にチェックポイントを作る。token位置、owners、文ID、symbols、段落、dependencies、診断、および既存要素への変更を対象にする。
- 解析失敗時は途中で追加・変更した状態を戻し、開始位置から復旧範囲を決める。共有参照を持つVを考慮し、配列の件数を戻すだけで済ませない。
- 復旧範囲を確定後、unknownノードと診断を一度だけ登録し、tokenの所属を確定する。
- EOFか上位終端へ戻る場合を除き、必ずtokenを消費する。無限ループを防ぐ。

復旧判定は引用済みtokenと予約語を区別し、行末だけを境界にしない。
単純な未知文は次の確実な既知文、文末、上位終端、段落・SECTION、END PROGRAMまでを候補とする。
既知のブロック開始で失敗した場合は異なる種類の入れ子も追跡する。
対応する終端を特定できればブロック全体をunknownにし、内側の既知文を外へ移さない。
外側のELSE/WHEN/END-*や次段落を復旧処理が消費しない。同種の親子で所属が曖昧な解析失敗は親全体の復旧へ伝え、内側の文を外側のnative本文へ移さない。
境界が曖昧なら次の確実な文末・構造境界までをまとめて未知扱いにする。

### 2 手続き部の解析継続

`Parser.procedure.cs` のM_blockでTolerantの場合だけ共通復旧を使う。
unknownには既存のkind/id/source/reasonに加え、token列と復旧理由・再開位置を保持する。
`UNSUPPORTED_STATEMENT`等の既存診断コードを活かし、復旧方法は追加メタデータにする。
programId、statementId、COPY展開元の位置を診断・token所有情報へ一貫して結び付ける。
`UNKNOWN-WORD.` が段落名と区別できないケースを受入テストに含め、曖昧なものを勝手に未知文へ変更しない。

### 3 ヘッダーと宣言の解析継続

ヘッダーは句・部の境界、宣言はレベル番号・SECTION・PROCEDURE DIVISIONを復旧候補にする。
未知のヘッダー句は原文・位置付きで保持する。DECIMAL-POINT/CURRENCY等、意味設定が未確定になる場合は関連処理をnativeへ昇格させない。
項目名と所属を確定できる宣言はstub/未知レイアウトにし、既存のUnknownCobolValue生成へ渡す。
親グループの配置が不明になる場合は親と配置に依存する処理にも未確定状態を伝える。
項目名を確定できない宣言は架空の名前・サイズを付けず、未知宣言レコードとして残す。
未解決参照を持つ文は文全体スタブにする。

Missing PROGRAM-ID、重複ID、判別不能なプログラム境界、入力デコード失敗、生成不能な設定は引き続き明示的に拒否する。
不正な条件前処理やCOPYの循環等を一般の未知文として飲み込まない。
複数program対応の新規拡張は今回の対象に含めず、現行の境界契約を維持する。

### 4 C#生成と診断の統合

`Validation.cs` の参照検査と型判定が未知symbolや未解決参照で例外終了しないようにする。
`SourceBackend.Statements.cs` / `SourceBackend.Dependencies.cs` の既存StatementStubを使い、未知範囲全体を一つの補完単位にする。
宣言の未知状態は `SourceBackend.Models.cs` 等の型・配置判定へ渡し、誤ったnativeの代入や配置計算を生成しない。
段落dispatcherとFlowBuilderが未知文を制御スタブとして保持できることを確認する。
header/declarationだけの診断は無関係な文IDへ結び付けず、program単位の補完課題として残す。

復旧できたprogramがあればsource/scaffoldで生成し、needsReviewを維持する。
未知スタブは初期状態で到達時に明示的な未実装例外を投げる。CALL/FILE/SQLの既存契約は変更しない。
不明な内部構文は分岐探索で未完了・blockedとして残し、正常な期待値や採用可能な証拠へ昇格させない。
再生成では既存のStubs/UserCode/Tests/Fixturesを保護し、スタブ契約の変更は既存の差分レポートへ出す。

### 5 テストと文書を更新

Frontendに `TolerantParsingTests`、Integrationに `TolerantGenerationTests` を追加する。
固定baselineを現在の出力で更新せず、新規の自作最小COBOLと独立した期待値で検証する。
既存CIの相補フィルタで新クラスが実行されることを確認する。
README、AGENTS、docs/cli.md、development.md、supported-subset.md、generated-csharp-quickstart.md、テスト対応表へ契約を反映する。

## 受入条件

| ケース | 確認する結果 |
| --- | --- |
| 既知MOVE → 未知文 → DISPLAY | 前後の既知文を保持し、未知部分だけ原文付きスタブになる |
| 改行だけで区切られた未知句 | 行末で誤って解析を再開しない |
| IF内の未知文とELSE | then/elseの所属と終端を保つ |
| 不正なIF/EVALUATE/PERFORM、異種の入れ子 | 未知ブロック内の文を外側へ漏らさず、後続段落を保持する |
| 未知宣言の後に正常宣言 | 正常項目を解析し、未知配置を既知として扱わない |
| 未知ヘッダー句 | programを保持できる場合は生成し、設定依存部分を補完対象にする |
| 未知語が引用文字列内にある | 予約語や復旧境界として扱わない |
| fixed/free、日本語、CP932/Shift_JIS、COPY | 診断位置と展開元が正しい |
| EOF・終端欠落・連続する未知文 | 有限時間で終了し、tokenの脱落・重複所有がない |
| source/scaffold | 未知部分を含めてC#プロジェクトを生成し、独立buildが通る |
| 未知部分へ到達しない独立シナリオ | 既知処理の期待値と一致する |
| 未知スタブへ到達 | 明示的に失敗し、成功したテスト資産に数えない |
| strict + tolerant | 解析レポートを保存し、復旧診断のあるprogramを拒否する |
| Standard | 固定期待IRと既存の拒否・生成動作を維持する |
| 再生成・探索・adopt | 利用者の編集を保持し、未知部分を未補完のまま承認しない |

検証は次を実行し、新しい結果を別記録へ残す。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet test tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
git diff --check
```

プロセス回帰は親の `MSBUILDDISABLENODEREUSE=0` でも確認する。
公開コーパスは固定lockから取得した入力で解析・source生成・独立buildを比較し、復旧数、生成数、重大拒否数を分けて記録する。
未取得のコーパスは未実施とし、取得済み固定期待IRや生成下限を変更しない。

## 変更を分ける単位

1. APIの方針指定、共通復旧、手続き部の最小回帰。
2. ヘッダー・宣言の復旧と未知レイアウトの伝播。
3. C#生成・CLI既定の接続、統合試験、文書と検証記録。

CLIの既定を切り替えるのは、共通復旧とC#生成の受入条件を満たしてからにする。
未知スタブの自動NoOp実行、新しいCOBOL構文の実行実装、未知の型・配置の推測はこの変更に含めない。

再生成のsource/scaffoldでは、`--syntax-recovery` を省略すると生成済み出力の解析方針を引き継ぎます。メタデータがない旧出力はStandardです。方針を変える場合は明示指定し、IRとmanifestの来歴に反映します。生成C#と実行設定が同じ場合は探索runを再利用し、元の証跡を保持します。新規出力の既定はTolerantです。
