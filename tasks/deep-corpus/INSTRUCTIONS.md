# CobolCS：追加公開サンプルの変換検証・未対応構文／不具合修正指示

作成日：2026-10-11（日本時間）
更新：リポジトリ配置・固定版取得方式へ対応（添付不要）
対象：<https://github.com/akrym1582/CobolCS>
調査時main：`b2141056b7c22d2915fb5fde4aa16cf4110bdb18`

## 1. この依頼で実施すること

このフォルダーの取得台帳に対応するCOBOLサンプルを実際にCobolCSへ入力し、変換・生成C#のビルド・選定ケースの実行結果を検証してください。構文未対応や変換器の不具合で失敗している場合は、根拠と再現テストを用意して共通実装を修正してください。集計や計画の作成だけで終了しないでください。

「正しく変換できた」は、生成ファイルが存在することだけでは判定しません。解析、原文保持、生成、C#ビルド、入力・外部応答を指定した実行、独立期待値との一致を別々に確認します。未対応スタブを含むビルド成功は、その業務処理が正しく移行できたことを意味しません。

ユーザーは、**ライセンス条件を確認して問題なく取り込めるサンプルは、回帰テストのためにリポジトリへ含めることを許可しています。** 原本・必要なCOPY・データ・期待値は、確認できた範囲で実際に取り込んでください。外部サンプルの取得・利用をすべて禁止する運用にはしません。

## 2. リポジトリ内の取得指示と評価入力

添付ZIPは不要です。リポジトリrootで次を実行してください。

```sh
bash tasks/deep-corpus/fetch.sh
```

入力rootは`.cache/public-cobol-corpus/deep-selected/`です。取得完了時に`READY.json`が作成されます。通信に失敗した場合は理由を記録して再試行し、取得していない入力を成功扱いにしません。必要なコマンドはBash、curl、unzip、gzip、tar、sha256sumです。Pythonは不要です。

- 今回の正本台帳：入力root内の`compact-selection/catalog.json`と`summary.json`。catalogのpathは入力root相対です。
- 対象：6,578件（選定集2,962件＋X-COBOL選別版3,616件）、bytes単位distinct 6,514件、重複64件。270個の`.at`は別集計です。
- 取得lock：このフォルダーの`origins.tsv`と`files.tsv.gz`。9,827ファイル（ソース・COPY・データ・ライセンス等）を個別SHA-256で検証して復元します。GitHubは固定commit URL、CMSとZenodoはarchive hashも検証します。
- 補助資料：入力rootの`cobolcs-deep-samples/sources.lock.json`、`SOURCES.md`、`starter-50.json`。starter-50のpathは`cobolcs-deep-samples/`相対です。
- 除外一覧：`compact-selection/excluded-x-cobol.json`。元収集8,157件から1,579件を除外済みです。bytes単位distinct 6,537件中6,514件を保持しています。
- 選定集は付属素材も保持。Xはリポジトリ単位に順番に、構文キーワード・encoding・COPY有無の異なる組合せの小さい例を優先し、その後hash順に選別しています。bytes完全一致・大型入力・容量上限による除外です。実行環境や依存の同等性・意味の網羅性は保証しません。
- 元配布アーカイブは一時的に全量ダウンロードしますが、評価対象として展開するのは台帳の選定分だけです。除外例を評価の分母へ戻しません。
- 元bytes・改行・文字コードは保存します。索引の特徴・encodingは簡易走査であり正解ラベルではありません。
- Xの個別ライセンスは未確認です。今回の取得機構を、原文や派生C#の公開許可と扱わないでください。条件確認済みfixtureの取り込みは第5節に従います。
- 前回の既存171件＋任意GPL24件の回帰は維持し、今回の件数に単純合算しません。

以下の「全件」は今回の6,578件を指します。取得済みキャッシュを再利用する場合も評価開始時にcatalogの入力hashを再検証します。`READY.json`は取得完了の目印であり、変換・ビルド・意味検証の成功を表しません。

## 3. 現行構成と維持すべき方針

調査時には以下を確認しました。開始時に現在のHEADと差分を再確認し、既に実装されている機能は再実装せず受入条件を検証します。

| 責務 | 現行箇所 |
|---|---|
| frontend | `src/CobolCS.Frontend`、`Parsing/SourceFrontend.*`、`Parsing/Parser.*` |
| source backend | `src/CobolCS.Cli/SourceBackend.*.cs` |
| 検証・生成・再生成 | `Validation.cs`、`Generator.cs`、`Adoption.cs` |
| 実行支援 | `src/CobolCS.Runtime`、生成公開Executor、ScriptedDependencies |
| コーパス評価 | `tools/CobolCS.Tools/Corpus.cs`、`Corpus.Evaluation.cs`、`Reports.cs` |
| テスト | `tests/CobolCS.Tests`、Frontend.Tests、Frontend.CorpusTests、IntegrationTests |
| 既存台帳 | `tests/public-corpus/*.lock.json`、既存summary・migration記録 |

- .NET SDK 10を使い、製品・評価器・回帰試験へPython必須依存を再導入しない。
- 通常C#本文を生成する`source` backendを主対象にする。未対応を暗黙にinterpreterへ戻さない。
- 既定はsource/scaffold。既存のstrictの部分拒否・契約許可・診断を維持する。
- 対応済みの通常処理はnative生成、外部依存は型付きC#契約、未対応内部文は原文付き編集可能C#スタブとする。
- 現行の精密Number、Optional、型付きArguments／固定グループCALL、基本INITIALIZE／STRING SIZE、入力例・MSTest生成を再利用する。
- 現行は値ベースの格納で、COMP-3等の実ホストbyte配置・REDEFINES等は未対応。旧CobolTSのbyte storage実装をCobolCSに実装済みと扱わない。
- 解析成功、generated、built、executedWithFixtures、matchedBaseline、needsReviewを混同しない。
- 原文の未消費token、未確定宣言、外部依存を、空処理・0・空文字・常時trueへ置き換えない。
- 利用者編集済みStubs／UserCode／Tests／Fixtures、契約の結び付け、再生成・adoptの保護を維持する。
- Linuxで検証する。今回Windows CIやホスト実接続を追加する必要はない。

調査時のIRはschema 2.2、frontendVersionは`0.3.0-csharp`です。必要な変更は正式なschema／型・互換方針へ接続し、互換性を壊す変更を同じ版のまま隠しません。

## 4. 着手手順

1. README、AGENTS.md、docs/development.md、supported-subset.md、cli.md、public-corpus.md、csharp-migration.md、execution-workflow.mdを読む。
2. 作業ツリーと既存変更を確認し、目的別ブランチを作る。例：`feat/deep-corpus-validation`。既存の未コミット変更を消さない。
3. 現行のビルド・単体・統合試験、既存コーパスの基準結果を保存する。過去の171本成功記録を今回の実測に代用しない。
4. 取得スクリプトで入力を準備し、元hashを検証する。改行変換や一括formatをしない。
5. 全6,578件の在庫・case ID・入力種別・設定・COPY候補を確定し、before評価を取る。
6. 診断の原因を集約し、対応可能なものを修正する。修正ごとに小さい回帰例と元のケースを再評価する。
7. 全体をafter評価し、改善・退行・未完を同一条件で比較する。

ZIPはパス逸脱、symlink、同名entryの重複、破損を拒否します。大規模データの展開量上限を設け、X-COBOL選別版の約103MB（非圧縮、選定集の付属資料は別）を誤って小さな既定上限で拒否しない設定にします。

## 5. ライセンス確認とテスト素材の取り込み

### 5.1 この依頼での許可とルール変更

現行AGENTS.mdには「外部COBOL原文、生成物をコミットしない」という規則があります。今回の明示許可に従って、次の例外へ限定的に更新します。この範囲について再確認を求める必要はありません。

> 出典・固定版・個別ライセンス・必要な通知を確認し、条件を満たす外部テストfixtureは専用directoryへ保存できる。未確認の外部原文、大規模未選別コーパス、未確認の生成物、実接続情報は引き続きコミットしない。

README・開発／コーパス文書にも同じ運用を反映します。CobolCS本体のMITライセンスを一括変更したり、外部素材を本体MITへ付け替えたりしません。

### 5.2 取り込み判定

判定はリポジトリ名ではなく、**取り込む具体的なファイルとその来歴**について行います。ルートLICENSE、サブdirectoryのLICENSE、各ヘッダー、第三者由来の注記を確認します。

| 条件 | 今回の扱い |
|---|---|
| MIT／BSD／Apache-2.0等の条件と適用範囲を確認できる | 帰属・LICENSE・NOTICE・変更表示等を保持して専用fixtureへ取り込む |
| CC0／公的資料 | 原資料の権利表示と適用範囲を確認して取り込む。ミラーのCC0だけで原著作物まで決めない |
| CC-BY | 出典・著者・ライセンス・変更内容の表示等を満たす素材だけ個別に取り込む |
| EPL／CeCILL-C／GPL／LGPL等 | ソース／変更物／生成物の公開条件、利用形態を確認する。別directoryに置くだけで条件を満たしたと判断しない。条件を満たせる場合だけ取り込む |
| 複数ライセンスのcontrib、NIST等の第三者素材 | 個別の適用条件を確認。集約リポジトリのルート表示だけで確定しない |
| LICENSEなし、権利不明、矛盾する表示 | 原文・派生C#をコミットしない。外部評価として保持し、確認事項を台帳へ記載 |
| X-COBOL収録コード | データセットのCC-BYを全コードのライセンスとみなさない。採用候補は元リポジトリまで確認 |

GPL等を使用したテスト入力があるという理由だけで、CobolCS本体が直ちに同じライセンスになると断定しないでください。一方、元COBOLから翻訳したC#や原文を含むIR・snapshot・ログにも元コード由来の条件が関係し得ます。「生成物だから対象外」と一律に扱わないでください。

### 5.3 最初の取り込み候補

- ProLeapのプロジェクト自身が作成したMIT構文テスト。NIST由来部分は別に条件確認。
- OpenCBSのMIT不具合fixture。参照された外部投稿等の第三者表示も確認。
- GC-Examples、core-banking-systemのMIT素材。
- IBM idz-utilities／zopeneditor-sampleのApache-2.0素材。各ソースの追加通知も保存。
- CMS由来の計算例は公式原本の来歴とミラーの修正履歴を区別して採用。

これは個々のファイルの確認を省略する許可ではありません。確認済み素材の採用・修正・試験は先に進め、条件未確定の素材だけを外部評価へ残してください。

### 5.4 保存構成

既存構成に合わせ、例えば以下を追加します。

```text
tests/fixtures/third-party/<origin>/<case>/
  input.cbl
  copy/...
  data/...
  expected.json
  PROVENANCE.json
  LICENSE
  NOTICE                 # 上流に必要な表示がある場合
```

PROVENANCEには上流URL、commit／release、元path、元hash、取得日、著作権表示、license expressionまたは未確定理由、確認した文書path／URL、採用範囲、変更内容、変更後hashを保存します。原文と簡約版を混同せず、最小化した場合も派生元を記録します。

通常はCOBOLと独立期待値を保存し、C#は試験中に生成します。C# goldenを保存する必要がある場合は、その目的とライセンス判断も記録します。大量の生IR・例外の原文snippet・生成物をCI artifactへ自動公開しません。

## 6. コーパス評価器を拡張する

既存`CobolCS.Tools`のfetch／import-archive／evaluate／verifyとCorpusTestsを拡張します。別言語で新しい恒久評価基盤を作り直さないでください。

### 6.1 入力分類

各caseに次を持たせます。

- source-origin、固定版、元path、hash、case ID。
- original-program／copybook-or-fragment／generated-expected／negative-test／autotest-container／unknown。
- sourceFormat、encoding、dialectとその根拠。未知は未知のまま記録。
- PROGRAM-ID一覧、補助プログラム、必要なCOPY候補、SQL INCLUDE、外部依存。
- 期待分類と根拠、実行条件、oracle／fixture、license disposition。

`.cbl/.cob/.cobol/.sqb`に加え、CMS公式原本の拡張子なし入力は台帳で明示指定できるようにします。単にすべての拡張子なしファイルをプログラムとして列挙しないでください。README／ChangeLogのコード例も対象へ混ぜません。

`.tcbl`はTypeCobol拡張として別に扱います。`.at`は原文テストコンテナです。まず日本語・数値・文字境界に関係する代表ケースだけを、元設定とAT_CHECK期待値に対応付けて抽出します。M4マクロを無視した正規表現抽出を正しいCOBOL入力と断定しません。抽出分の件数は6,578件とは別集計です。

### 6.2 正確な形式・encoding

現行UTF-8入力へShift-JISのbytesを無断でUTF-8として流した失敗を構文未対応としないでください。encodingは上流説明とbytesから確認します。

まず明示encoding設定の導入または評価側の明示変換を検討します。評価側で変換する場合は元bytesを保存し、派生入力のhashと変換規則を記録します。元fileのbyte offsetと派生UTF-8のoffsetを同じものとして報告しません。誤decodeを置換文字で握り潰さないでください。source encoding対応と、実行時JEF／EBCDICレコード互換性は別です。

fixed／free／Tandem等を上流テスト設定から判断します。不明な形式を固定形式に一律指定して異常入力判定にしません。

### 6.3 重複と実行時間

6,578件の在庫は残したまま、同一bytes・形式・encoding・COPY内容と探索順・compiler config・補助programの組合せだけを重い評価の共有候補にします。元pathが違うケースのsource-mapや診断は元pathへ正しく束縛します。バイトが同じというだけで結果ファイルを別ケースに流用しません。

解析・生成・build・実行を別stageにし、case単位のcheckpoint／resumeを設けます。source hash、compiler source hash、設定・依存hashが変わったstageは再実行します。大量のdotnet buildは上限付き並列化とプロセス木timeoutを使い、無制限に起動しません。生成project間の型衝突を隠すために勝手に一つのprojectへまとめません。

### 6.4 記録する工程

各caseで次を記録します。

`inputVerified`、`analyzed`、`analysisHasErrors`、`generatedPrograms`、`rejectedPrograms`、`built`、`nativeStatementCount`、`adapterRequiredCount`、`unsupportedStatementCount`、`unknownTokenCount`、`executionAttempted`、`executionPassed`、`independentExpectedMatched`、`referenceCompilerMatched`、`needsReview`、missing依存、stage別失敗、適用fixture／oracle。

- 複数PROGRAM-IDの一部成功をファイル全体成功としない。
- scaffoldのexit 0、例外entryのコンパイル成功、NoOp実行を意味検証成功へ算入しない。
- 6578件の在庫、6514件のbytes単位distinct、実際の設定込み実行数をそれぞれ報告する。
- 6,578件全件の解析を試す。断片も試験単位として扱い、正しく断片診断された結果を正常プログラム成功に混ぜない。
- 生成できた全projectについてbuildを試行し、生成器由来のC# compile errorを修正する。外部restore失敗は別分類にする。
- before／afterは同一入力・設定で取り、変更理由を記録する。以前の測定ファイルを上書きしない。

## 7. エラー分類と修正の優先順位

| 優先 | 分類 | 今回行うこと |
|---|---|---|
| P0 | クラッシュ、ハング、未消費token消失、正常風の誤変換 | 最優先で再現・修正。対応成功にしない |
| P0 | 生成C#の型／名前／scope／参照エラー | 生成器を修正。出力C#の手編集で通さない |
| P1 | 合法な構文をfrontendが読めない | 文・句・宣言・ブロック境界を共通解析器へ追加 |
| P1 | 対応済み構文の計算・分岐・格納・反復が誤る | 独立期待値／参照実行で固定してnative実装を修正 |
| P1 | COPY探索、REPLACING、SQL INCLUDE、形式、encoding | 原文来歴・曖昧性・欠落を保ったまま共通処理を修正 |
| P2 | 未対応内部構文で仕様が確定できる | 頻出・影響度順にnative機能を実装。制約・正負境界をテスト |
| P2 | CICS／IMS／MQ／DB等の環境依存 | 原文・operand・入出力・status・制御結果を保持した型付きスタブを改善 |
| 別分類 | 入力不足、missing COPY、未設定応答 | fixtureを明示して検証。未指定を成功値で補わない |
| 別分類 | 意図的な不具合／不正入力 | 上流説明・原文・参照実行等の根拠に沿って診断／失敗を検証 |
| 別分類 | SDK／NuGet／通信／資源不足 | 環境問題として記録し、製品の構文未対応と混同しない |

失敗を減らすために入力の句を削ったり、parserが読めないものをすべて「元ソース不正」にしたりしません。上流の不正例でも、合法にコンパイルされるが結果が誤る例があります。negativeカテゴリ全件に構文エラーを期待しないでください。

合法な未対応の判定には、対象dialect、一次資料、上流テストの設定／期待、必要なら参照コンパイラの診断を用います。GnuCOBOLがそのまま拒否したという理由だけでIBM／富士通等のソースを不正と決めません。

### 機能拡張の目安

頻度と原因から順番を決めますが、候補は編集PIC、SEARCH、UNSTRING／INSPECT、INITIALIZE派生句、CORRESPONDING、追加FUNCTION、ODO、file句、SORT、複合条件です。全規格の完全実装は要求しません。今回の実例で遭遇した合法な利用と、その意味を守るための境界条件を実装します。

REDEFINES／byte storageや複雑なrun-unit等、設計変更が必要なものは、部分的に値コピーへ置き換えません。実装するなら宣言・storage・MOVE・snapshot・adapterへの影響を一貫して直します。今回解消できないものは、理由・具体的case・必要仕様・最小再現を残し、未完を明示します。

## 8. EVALUATE／PERFORM・外部依存の扱い

これまでのユーザー方針を維持します。EVALUATEと外部PERFORMは、原文・条件・呼出し先を保持する編集可能なC#スタブでも構いません。ただし既にnativeで対応している単純EVALUATE、段落／SECTION PERFORM等をスタブへ戻しません。

外部PERFORMはout-of-line PERFORM、すなわち同一program内の段落／SECTIONを対象とする制御です。別programへのCALLとは区別します。

- EVALUATE：subjects、ALSO、WHEN条件、THRU／ANY／NOT／OTHER、順序、各body、元位置を保持。
- PERFORM：対象paragraph／SECTION、THRU、TIMES／UNTIL／VARYING、TEST、対象原文・復帰先を保持。
- SQL／CICS等：ブロック全体、operand、host／indicator、status、control outcomeを保持。
- 不明ブロックをスタブ化した場合、その内部bodyを外側の通常処理としても実行しない。
- 外部応答やControlResult.Continueを自動設定しない。未設定は失敗する。
- スタブ補完のテストは既存ScriptedDependencies・C# scenarioを使い、生成された型・文ID・契約hashへ結び付ける。
- 「スタブが正しく生成された」「補完したスタブの契約が正しい」「元COBOLの意味を再現した」を区別する。

COPYなしでも、既知の宣言・文は保持して生成を進めることを目指します。未知項目はunknown／stubで表現し、推測PIC・推測レイアウトを正常な型として採用しません。missing COPYを含む兄弟項目のために、関係ない既知項目まで不必要にunknownへ落とさないでください。

## 9. 正しい変換の確認方法

### 9.1 すべての修正に共通

1. 元caseで修正前の失敗を記録する。
2. 原文から小さな再現ケースを作る。再配布条件が未確定なら、仕様から独立した合成例を作り、元caseとの関連はpath／hashで残す。
3. 正常、境界、異常を、生成器から独立した期待値で固定する。
4. frontend／IRだけでなく、source backendの生成C#を実際にbuild・実行する。
5. 元caseへ戻り改善を確認し、同原因のケースを再評価する。
6. existing native、生成スタブ、再生成・adoptが退行していないことを確認する。

文字列検索で「ADDがC#に出た」だけのテスト、生成結果をコピーした期待値、すべて例外を投げるだけの成功基準は不可です。

### 9.2 期待値の区分

| 種類 | 正しさの根拠 | 表示 |
|---|---|---|
| independent-synthetic | 仕様・手計算・既知byte／値から独立に定める | 独立期待値一致 |
| original-cobol-captured | 元COBOL処理系と条件を保存して採取 | その処理系・条件での一致 |
| reference-compiler | GnuCOBOL等で同じ入力・該当dialectを実行 | 参照処理系との一致。元ホスト同値とは別 |
| csharp-characterization | 現時点のC#挙動を保存 | 回帰固定。COBOL変換の正解証明ではない |
| dependency-contract | 明示fakeの入力・順序・更新・結果を検査 | adapter／stub契約の一致 |

現行CLIはbaselineの出自を認証できず`matchedBaseline=false`を維持します。この依頼のためにtrueへ強制変更せず、評価器にoracle区分と証拠付き一致結果を追加してください。既存のレビューhash・NoOp制限・adopt条件を維持します。

参照実行では、コンパイラ名・version・dialect・flags、ソース／COPY／入力hash、stdout／stderr／終了値／出力fileを保存します。source encoding、末尾空白、ゼロ埋め、丸め、EOF、終了signalを揃えます。理由のないtrim・数値許容差・並べ替えで一致させません。

GnuCOBOLを利用できる範囲では差分実行を追加します。利用できない場合も、独立期待値で実行検証できるケースは完了させます。PythonBolやinterpreterとの一致だけを正解にしません。別backendでも共通実装の誤りを共有する場合があるためです。

### 9.3 実行対象の選定

- 取得したstarter-50の全件を調べ、形式・依存・正常／不具合・生成期待値等を補正して台帳を確定。
- その中で実行条件を固定できる正常ケースは、入力と独立期待値を用意して実行する。
- さらに各修正機能について、複数の入力・主要分岐・境界・異常を持つ回帰ケースを追加する。
- 単純HelloWorldだけを大量実行して実行検証件数を増やさない。計算、条件、表、文字、file／SQL／CALL契約の各領域を含める。
- 実機依存・COPY欠落・不正ソース等で実行できないcaseは理由と、どこまで検証できたかを記録する。
- `.at`の日本語・数値の代表例も、設定と期待出力を読んで独立ケースとして追加する。元処理系固有の意味をportable標準と偽らない。

## 10. テストとCI

### 通常CI

ライセンス確認済みの小さな原本／簡約版と独立合成fixtureをコミットし、ネットワークなしでも回帰試験を実行できるようにします。サンプルそのものをテストコードへ埋め込み、帰属表示を失う方法は使いません。

生成projectを開発repo外の一時directoryへ出し、build／実行します。正常例ではreportsやIRを取り除いても生成プログラムが動くことを代表ケースで検証し、実行時解析や開発repoへの依存が入り込んでいないことを確認します。

### 大規模評価

6,578件の外部コーパスは、固定lock・取得済みcacheで手動または明示ワークフローから評価します。通常CIで未取得の全件を「成功」とせず、未実施を報告します。再実行にはcheckpointを利用できますが、変更したcompiler/configに旧結果を流用しません。

fixtureのライセンス判定と、生のログ／IR／生成コードをartifact公開する判定は別です。未確認素材の原文を含むartifactは公開対象から除きます。原文を含まない件数・hash・diagnostic code等は報告へ残します。

### 必須回帰

- parserの例外・EOF・継続・scope・COPY境界・原位置。
- 新機能ごとの意味、エラー、native／stub境界。
- generated C#の型・名前・入れ子scope・public API。
- precision、PIC、範囲、配列、外部更新とOptional。
- 不正応答、未設定スタブ、余剰応答、NoOpとreview/adoptの制限。
- 手編集保持、契約変更、再束縛、削除、パス逸脱、atomicな再生成。
- 子プロセスtimeoutと終了。`MSBUILDDISABLENODEREUSE=0`を親に設定した既存プロセス回帰も維持。

## 11. 具体的な実装順と成果物

1. 現行試験と既存171＋24本の基準確認。
2. 選定入力import／lock／在庫台帳、形式・encodingの検査、全件before解析。
3. クラッシュ・ハング・生成C#エラー・誤変換の修正。
4. 頻出する未対応構文・COPY・宣言／式／制御を共通処理へ追加。
5. 独立期待値・参照実行・型付きfakeによる意味検証。
6. ライセンス確認済みの代表素材を専用fixtureへ取り込み、通常CIへ接続。
7. 全件after評価・全生成projectのbuild、残る修正可能な失敗の修正。
8. 既存機能・再生成／adoptを含む最終回帰、対応表・結果文書を更新。

追加成果物例：

- `tests/deep-corpus/*.lock.json`、case台帳、oracle／fixture設定。
- `tests/fixtures/third-party/`とPROVENANCE、LICENSE／NOTICE。
- `docs/deep-corpus-results.md`／JSON：before／after、工程別結果。
- `docs/deep-corpus-fixes.md`：原因ID、影響ケース、修正、独立テスト、残る制約。
- `docs/deep-corpus-license-review.md`：具体的ファイルの採用／外部保持判断と根拠。
- 既存THIRD-PARTY-NOTICES相当、README、AGENTS、CLI、supported-subsetの更新。

JSONの恒久結果は決定的にし、時刻・絶対作業path・所要時間等は別の実行記録へ分けます。

## 11.1 サブエージェントによる並列実行

この依頼では、大量サンプルの評価・原因調査・独立した修正・ライセンス確認にサブエージェントを活用してください。利用できない環境では同じ台帳とcheckpointを使って逐次実行し、利用不可を理由に必要な作業を省略しないでください。

### 分担と開始条件

まず統括エージェントが入力lock、case ID、分類、共通config、評価器、結果schemaを確定します。代表数件で評価器自身の動作を確認してから、**同じ評価対象コミット／compiler source hash**を各担当へ渡します。評価器が未確定な状態で全件を別々の方法で測定しないでください。

初期構成は統括1＋サブエージェント3程度とし、環境のCPU・メモリ・利用枠に余裕があれば4担当まで増やします。数千ファイルに対して1ファイル1エージェントは作りません。

| 担当 | 主な責務 | 出力 |
|---|---|---|
| 統括 | 評価基盤、割当、重複原因の集約、共通修正の所有者、統合、最終判定 | 全体台帳、統合コミット、before／after |
| 評価・調査A | NIST／ProLeap／TypeCobol等の構文・宣言・制御例 | ケース結果、再現例、原因候補 |
| 評価・調査B | GenApp／CMS／IBM／GnuCOBOL実用例等 | 業務系・外部依存の結果、契約の問題 |
| 評価・調査C | 日本語／ESQL／OpenCBS、余力でX-COBOL分割分 | encoding・異常系・意味の問題 |
| 任意の追加担当 | ファイル単位のライセンス確認、oracle／fixture検証 | 出典根拠、採用候補、独立期待値 |

担当が早く終わったら、統括が未評価batchを追加割当します。X-COBOLを一人へ固定して待ち時間を作らず、100〜300件程度を目安に行数・サイズ・依存数を考慮して分割します。長大なCMS／NIST等は小さいbatchにします。意味検証・最小再現・ライセンス確認は数件〜数十件のまとまりで割り当てます。

### 作業場所と同時編集

- 原本cacheは検証済みの共有read-only入力にする。各担当の出力先・一時directory・ログ・生成projectは別々にする。
- 修正担当には分離したworktree／checkoutと担当ファイルを割り当てる。共通作業ツリーの同じファイルを並行編集しない。
- Parser、schema、SourceBackend、runtimeなど複数担当が触りたい箇所は統括が所有者を決める。他担当は最小再現・提案・影響caseを渡す。
- 共通原因は一つのcause IDに集約し、各コーパス担当が独自の同種fallbackを実装しない。
- ロック・期待値・PROVENANCE・集計ファイルの変更も所有者を決める。全件summaryは統括のみが生成する。

### .NETの資源とプロセス管理

エージェント数とdotnet buildの並列数は別に制御します。初期値は**全担当合計で同時build最大2、各buildは`-m:1`**を目安とし、環境に合わせて下げます。各担当が独立して最大2を起動し、全体で8本になる構成にしないでください。共通ジョブqueue／semaphoreまたは統括による実行枠割当を用います。

解析・索引処理は別枠で並列化できますが、CPU・メモリ上限を設けます。NuGet復元cacheを準備し、同じ依存復元を全担当が同時に始めないようにします。子プロセス終了・MSBuild node reuse・timeoutの既存契約を各担当も維持します。

### 担当へ渡す依頼テンプレート

```text
対象compiler commit/hash:
対象case ID/batchと入力lock:
入力cache（read-only）:
使用config／COPY path／oracle:
作業worktreeと変更を許すファイル:
専用出力directory:
許可された解析・build・実行の並列枠:
期待する成果物:
  - case別の全工程結果、実際のコマンドとログpath
  - 入力不正／未対応／製品不具合／環境問題の根拠付き分類
  - 修正前失敗と修正後結果、最小再現、独立期待値
  - 元caseと修正機能の対応、未完と必要情報
禁止事項:
  - 他担当の原本・出力・期待値を書き換えない
  - unknownや例外を成功値へ置換しない
  - 生成C#の手修正だけで製品の不具合修正済みとしない
  - 実行していない結果を成功として報告しない
```

### 統合と最終検証

サブエージェントの文章による「成功」報告だけを採用しません。統括は結果schema、case ID、入力／compiler／config hash、実ログ、修正差分、期待値の出所を確認します。欠落・重複・異なる版の結果を検出してから集約します。

修正は統括が順に統合し、共通の統合コミットで影響ケースを再評価します。最終afterは**同一の統合版**による結果に揃えます。異なる修正枝の結果を足し合わせて全件成功としません。各修正のたびに全6,578件を無条件に再実行せず、途中は影響範囲、最終は必要な全件stageを実行します。

各担当はcase単位の完了結果をcheckpointへ保存します。中断・timeout・コンテキスト切替時も未完caseを成功扱いせず、引継ぎ台帳から再開します。最後に統括が既存回帰・新規回帰・生成物のbuild／実行・ライセンス表示を確認し、未完を残したまま「全対応完了」と宣言しないでください。

## 12. 検証コマンドと作業上の制約

調査時の現行必須コマンド：

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
git diff --check
```

既存評価の例：

```sh
dotnet run --project tools/CobolCS.Tools --no-build -- --help
dotnet run --project tools/CobolCS.Tools --no-build -- evaluate --lock tests/public-corpus/course.lock.json --root .cache/public-cobol-corpus/course --out out/course --build
dotnet run --project tools/CobolCS.Tools --no-build -- verify out/course/summary.json
```

選定入力・外部datasetのimportと全件実行コマンドは、既存Toolsの構文を確認して追加し、実際に使用した完全なコマンドを文書化してください。新コマンドを現行に存在するものとして記載しないでください。

生成側のbuildとテストも必須です。SDK／NuGet不足を製品不具合へ混ぜず、実行できない場合は理由と未実施を残します。Docker、WSL、DB資格情報、ホストアクセス、AI APIは必須にしません。原COBOL・生成C#の実行は一時領域・明示入力・timeoutを使い、上流のインストールscriptを自動実行しません。

既存警告設定を弱める、テストを削除する、期待値を一括再生成する、未対応を常時正常へ置換する方法で通さないでください。

## 13. 完了条件

- [ ] 6,578件の在庫と全件解析結果が照合され、重複・断片・不正例・未実施を隠していない。
- [ ] 生成可能ケースのC#を実際にbuildし、生成器由来のcompile errorを修正した。
- [ ] 修正可能な構文・意味・COPY・生成・実行不具合に共通修正と独立した回帰試験を追加した。
- [ ] 最初の50件と機能別代表例の実行可能性を調査し、条件を固定できるケースを実行・独立期待値で検証した。
- [ ] native生成、外部契約、未対応スタブ、実行可能、意味検証済みを区別して報告できる。
- [ ] ライセンス確認済みの代表サンプルを実際にリポジトリへ取り込み、必要な通知・来歴と通常CIを整備した。
- [ ] 条件未確定の素材は外部に残し、原文を含む生成物・ログの公開範囲も確認した。
- [ ] 現行C#構成、公開API、精密数値、ユーザー編集保持、再生成／adoptと既存テストが退行していない。
- [ ] before／afterを同一条件で比較し、残る制約・未実施項目・根拠を記録した。

全規格・全方言の完全実装や、全6,578件の正常実行を完了条件にはしません。一方、合法な構文で修正可能なエラーを「未対応」と記録しただけで完了としないでください。大きな設計変更・実機仕様不足などで残る場合は、未完項目として具体的case、原因、必要な変更・情報を報告してください。

最終報告は、修正した機能、取り込んだテスト素材とライセンス、工程別件数、意味を検証したケース、未完・未実施を中心に簡潔にまとめます。push・PR公開・mainへのmergeは別途指示がある場合に行います。

## 14. 参照した一次資料

- CobolCS：<https://github.com/akrym1582/CobolCS/tree/b2141056b7c22d2915fb5fde4aa16cf4110bdb18>
- Apache-2.0、再配布条件（第4項）：<https://www.apache.org/licenses/LICENSE-2.0>
- MIT本文：<https://opensource.org/license/mit>
- EPL-2.0本文：<https://www.eclipse.org/legal/epl-2.0/>
- GNU GPL v3本文、生成物・再配布等：<https://www.gnu.org/licenses/gpl-3.0.en.html>
- GNUライセンスFAQ、出力へのライセンス適用：<https://www.gnu.org/licenses/gpl-faq.en.html>
- X-COBOL配布レコード：<https://zenodo.org/records/14269462>

ここに示した採用手順は、個別ファイルについて条件を確認するための実装方針です。収集済み全ソースの再配布を一括承認したものではありません。
