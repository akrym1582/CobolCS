# 対応範囲

初めて変換する前に、下の表で必要な構文の状態を確認してください。`native` は生成された C# が直接実行し、`adapter-required` は利用者が外部処理の応答を実装し、`unsupported` はスタブの実装または入力の修正が必要です。操作手順は[README](../README.md)と[生成 C# の実行ガイド](generated-csharp-quickstart.md)にあります。

2026-10-11 の追加受付: FD の RECORDING MODE F/V、LABEL RECORDS ARE OMITTED / STANDARD、BLOCK CONTAINS、DATA RECORD、固定長・可変長句。
これは外部ファイル宣言の保持であり、物理ブロッキング・ラベル・ホスト可変長 I/O の実装ではありません。
PROGRAM-ID の引用名、IS INITIAL、RECURSIVE は位置・属性を保持します。INITIAL/RECURSIVE の呼出し寿命は未実装で、
scaffold の入口に編集可能スタブを置き、strict は拒否します。AUTHOR 等の自由記述は通常の literal と区別します。
P64 の明示選択と未確定時の扱いは [CLI](cli.md)、欠落 COPY・全件測定・残る制約は [18本の対応記録](remaining18.md) に記載しています。

解析可能であることと、実行対応・外部契約・未対応を分けます。既定はsource/scaffoldです。
`native` は通常C#として生成、`adapter-required` は型付きC#契約、`unsupported` は実装が必要なC#スタブです。
構造を復旧できずPROGRAM-IDを構築できない範囲は生成不可として報告します。

| 分類 | source の状態 | 範囲 |
|---|---|---|
| 入力・前処理 | native | UTF-8、free/fixed、再帰COPY・REPLACING、循環／欠落診断、原文・COPY履歴 |
| 宣言・状態 | native | 01–49/77、名前付きプロパティ、グループ型、固定OCCURS配列、多次元添字、OF/IN、88条件（スカラーはboolプロパティも生成） |
| PIC | native | 9/S/V、X/A、N、最大1,000桁・scale 0–1,000。精密有理数・固定長文字 |
| 算術 | native | ADD/SUBTRACT/MULTIPLY/DIVIDE/COMPUTE、四則・整数べき、ROUNDED、SIZE ERROR、単一商と余り |
| MOVE・SET | native | 要素MOVE、固定長・JUSTIFIED、文字参照変更、SET 88、SET index TO/UP/DOWN |
| 条件 | native | IF/ELSE、比較、AND/OR/NOT、88、符号・クラス条件、単一subject/TRUEのEVALUATE |
| 制御 | native | inline PERFORM TIMES/UNTIL/VARYING、TEST AFTER UNTIL、入れ子、EXIT PERFORM、段落落下、段落/SECTION PERFORM THRU、GO TO/DEPENDING、終了信号 |
| DISPLAY | native | 表示配列。数値PICの符号・ゼロ埋め・暗黙小数点表記 |
| ACCEPT | native | 対象のみ／FROM CONSOLE。fixture.inputsを順に消費。不足は到達時エラー |
| 関数 | native | ABS、文字LENGTH、UPPER/LOWER-CASE、TRIM（1引数）、REVERSE、単純十進NUMVAL、MIN/MAX、INTEGER、INTEGER-PART、MOD/REM |
| 外部CALL | adapter-required | 静的／動的ターゲット、型付きスカラー・固定グループ引数（内部に固定表可）、BY REFERENCE/VALUE/CONTENT、RETURNING、例外／正常分岐。別名参照の可能性がある複数参照引数はunsupported |
| FILE | adapter-required | sequential/line-sequentialのOPEN/READ/WRITE/REWRITE/CLOSE、既知レコード項目、必須応答、EOF・FILE STATUS。FROM/INTO、KEY、ADVANCING、その他organizationはunsupported |
| SQL | adapter-required | frontendが構造化するSQL原文・入力／出力ホスト・指示変数・SQLCODE/SQLSTATE、型付き応答。WHENEVERのジャンプはunsupported |
| STRING SIZE／値区切り | native（限定） | 固定 ASCII X/A・文字リテラル、SIZEと値区切りの混在、POINTER、ON/NOT ON OVERFLOW。最初の区切りを除外し、部分転送・受信suffix・pointerを保持。別名領域・国別・参照変更・空区切りは文全体スタブ |
| INSPECT | native（限定） | 固定 ASCII X/A の単一 TALLYING（CHARACTERS/ALL/LEADING）と単一 REPLACING（CHARACTERS/ALL/LEADING/FIRST）。非重複、既存tally加算、同長置換、添字の1回評価。複数操作、BEFORE/AFTER、CONVERTING、別名領域・国別・参照変更は文全体スタブ |
| UNSTRING | unsupported | 句・対象・原文を保持する文全体のC#スタブ |
| INITIALIZE | native | 追加句なしの既知スカラー・階層グループ・固定表。数値0／文字空白、FILLERと88を除外。REPLACING等は文全体のスタブ |
| CORRESPONDING | unsupported | 既存IRを保持した型付きスタブ |
| LINKAGE/PROCEDURE USING | native（限定） | 固定レイアウトのBY REFERENCEと数値スカラーBY VALUE。正式Arguments入力と結果snapshot。宣言側BY CONTENT・未知配置・run-unit寿命は未対応 |
| SEARCH/SEARCH ALL | unsupported | 両者をIR・レポートで区別。WHEN/AT ENDを含むブロック全体をスタブ化。線形探索への暗黙代替なし |
| 追加関数 | unsupported | CURRENT-DATE、INTEGER-OF-DATE/DATE-OF-INTEGER、INTEGER-OF-DAY/DAY-OF-INTEGER、NUMVAL-C、SUMは戻り型付きC#スタブ。時計・日付基準・通貨文法のnative対応は継続課題 |
| 複合制御 | unsupported | EVALUATE ALSO/ANY/THRUはsubjects/selections/原文を保持する制御スタブ。PERFORM VARYING AFTER、TEST AFTER VARYING、NEXT SENTENCEも明示スタブ |
| SORT/MERGE/RELEASE/RETURN | unsupported | キー・方向・手続き・レコード等のIRを保持してスタブ化。整列、安定順序、maxRecords/maxSortBytesの実行実装は継続課題 |
| 不明構文・型 | unsupported / 生成不可 | 許容解析は未知文・ブロックを位置付きC#スタブ、未知ヘッダーを入口スタブとして保持。失敗宣言は部分PIC・VALUEを破棄しUnknownCobolValueと未知配置へ変換。strictは拒否。PROGRAM-IDやプログラム境界の重大エラーは生成不可 |

`IS NUMERIC` は格納済みNumberなら負数・小数を含めtrueです。PIC Xでは数字列だけを許し、符号・小数点・空白・英字はfalseです。
これは値ベースStorageの契約であり、不正なゾーン／パック表現の検出ではありません。
数値→文字MOVEはFUNCTION ABS/LENGTH/NUMVAL/INTEGERや入れ子関数も含め拒否／スタブ化します。
未知関数を文字戻り値と推定せず診断します。文字戻り値のTRIM等は文字項目へMOVEできます。
source 生成の MOVE スタブには構造化した `moveClassification` を記録します。`reasonCode` は互換性のため維持し、主理由と重複理由を分けて集計します。既知の単純数値 DISPLAY 項目から固定長 PIC X への転送は `numeric-display-to-pic-x` の次期検討候補として記録します。これは native 対応ではなく、符号・暗黙小数点・桁埋め・切詰めの独立仕様が必要です。[分類と検証範囲](shift-jis-move-classification-results.md)を参照してください。

portable-testのASCII照合・leading-separate・exact-rationalのみを通常実行します。
COMP/COMP-3/COMP-5/INDEXは数値値として保持し、実機バイナリ／パック配置は実装していません。
PIC X/AはASCII、PIC NはBMP。富士通EBCDICカナ／JEFをASCIIと同一視しません。

継続課題はREDEFINES、バイト列グループMOVE、編集PIC、COMP-1/2、OCCURS DEPENDING ON、SIGN拡張、SYNCHRONIZED、BLANK WHEN ZERO、MOVE ALL、文脈依存HIGH/LOW/QUOTE、LINKAGEの未対応渡し方/run-unit、REPORT/SCREEN、CICS/DLI/MQの実接続です。
未知のEXECやサービス構文を汎用成功応答に置き換えません。
元COBOL実機の業務同値性は未検証です。PythonBolとの一致を正解判定には使用していません。

実行上限はmaxSteps/timeoutMs、PERFORM戻り先10,000、表の基本項目あたり1,000,000要素、べき指数絶対値1,000です。
添字・参照変更は範囲検査します。source fixtureはスカラーと添字付きtableFieldsに対応します。
interpreterは移行比較用の旧backendで、追加の外部契約・表fixture・ACCEPTには対応しません。

`ACCEPT` の省略可能な `END-ACCEPT` と、`UNSTRING INTO` の受取項目間のカンマを解析する。
`ACCEPT ... FROM CONSOLE END-ACCEPT` は既存の `inputs` fixture で実行できる。
`FROM COMMAND-LINE` と `UNSTRING` の実行対応はこの構文修正には含まれず、sourceでは明示スタブになる。

`DISPLAY ... [WITH] NO ADVANCING` と省略可能な `END-DISPLAY` を解析します。
改行抑止はIRの `noAdvancing: true` に保持します。既存の `Display` は端末画面ではなく
DISPLAY文ごとの文字列captureです。例えば `DISPLAY 'A' NO ADVANCING DISPLAY 'B'` は
`["A", "B"]` です。端末の改行・カーソル移動・ACCEPTのエコーを再現したものではありません。
不完全な句とoperand欠落は診断し、`WITH` をデータ項目に誤変換しません。

0.3 は source の公開 typed Executor、PIC 検査付き setup／snapshot、C# scenario、メモリ内 fake、xUnit と adopt を追加します。
実行準備拡張で固定LINKAGE・グループCALL・基本INITIALIZE・STRING SIZEを追加しました。CALL の探索 no-op は明示指定時だけで、FILE／SQL／未対応構文には適用しません。
[実行ワークフロー](execution-workflow.md) に応答・baseline・採用の制約を記載しています。

従来の内部補完拡張は、補完済み内部 C# の明示再探索、静的／実測 worklist、安定した業務入出力テストを追加したもので、構文の native 対応追加とは別です。未対応外部・未知ブロックを内部補完として探索しません。業務比較は型契約と精密 Number、文字完全一致、厳密な外部操作順を使い、独立期待値を自動推定しません。[契約・制約・旧出力](completion-workflow.md)。

---

[ドキュメント案内](README.md)へ戻る。

上記文字処理の追加は source backend の限定範囲です。旧 interpreter の INSPECT／STRING 値区切りを実装済みとは扱いません。一次資料・独立期待値は [業務支援](business-test-assistance.md)。

ACCEPTの既存CONSOLE・既知scalar範囲は、任意providerとケース別値列にも接続できます。providerとキューは排他で、順序・値・回数・未配線を検査します。日付・環境・未知型・表全体の自動接続はしません。RENAMES宣言の独立01を証明できる場合に限り、無関係なINSPECT／STRINGのスタブ化を避けます。共有／不明範囲は従来どおり文全体スタブです。新しい文種のnative対応は追加していません。

日本語ソースの追加対応: UTF-8/CP932/Shift_JISの厳密な入力、識別子内の `-` / `－` / `−` の区別、固定形式の単語継続、空のPROCEDURE DIVISION、文字列の参照変更長の省略（末尾まで）。source/interpreterの両実行器で参照変更の読取り・書込みを検証します。PIC XのASCII制限・PIC NのBMP制限・既定fixed日本語列の確認診断は維持します。

DISPLAY UPONは値・宛先・NO ADVANCINGをIRに保持し、デバイス接続を `CS_DISPLAY_UPON` とsourceスタブで可視化します。既存のDISPLAYは文ごとのcapture契約を保ち、NO ADVANCINGの解析・capture対応を撤回しません。端末stdoutの行結合や元COBOLとの同値性を証明したことにはしません。検証記録は [日本語コーパス](japanese-corpus-parser-results.md) にあります。
