# 対応範囲

2026-10-11 の追加受付: FD の RECORDING MODE F/V、LABEL RECORDS ARE OMITTED、BLOCK CONTAINS、DATA RECORD、固定長・可変長句。
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
| 外部CALL | adapter-required | 静的／動的ターゲット、型付きスカラー引数、BY REFERENCE/VALUE/CONTENT、RETURNING、例外／正常分岐。別名参照の可能性がある複数参照引数はunsupported |
| FILE | adapter-required | sequential/line-sequentialのOPEN/READ/WRITE/REWRITE/CLOSE、既知レコード項目、必須応答、EOF・FILE STATUS。FROM/INTO、KEY、ADVANCING、その他organizationはunsupported |
| SQL | adapter-required | frontendが構造化するSQL原文・入力／出力ホスト・指示変数・SQLCODE/SQLSTATE、型付き応答。WHENEVERのジャンプはunsupported |
| STRING/UNSTRING/INSPECT | unsupported | 句・対象・原文を保持する文全体のC#スタブ。OVERFLOWや途中更新をnative実装したとは扱わない |
| INITIALIZE/CORRESPONDING | unsupported | カテゴリ・REPLACING等の既存IRを保持した型付きスタブ。VALUE句復元や全域同名検索による代替実行はしない |
| SEARCH/SEARCH ALL | unsupported | 両者をIR・レポートで区別。WHEN/AT ENDを含むブロック全体をスタブ化。線形探索への暗黙代替なし |
| 追加関数 | unsupported | CURRENT-DATE、INTEGER-OF-DATE/DATE-OF-INTEGER、INTEGER-OF-DAY/DAY-OF-INTEGER、NUMVAL-C、SUMは戻り型付きC#スタブ。時計・日付基準・通貨文法のnative対応は継続課題 |
| 複合制御 | unsupported | EVALUATE ALSO/ANY/THRUはsubjects/selections/原文を保持する制御スタブ。PERFORM VARYING AFTER、TEST AFTER VARYING、NEXT SENTENCEも明示スタブ |
| SORT/MERGE/RELEASE/RETURN | unsupported | キー・方向・手続き・レコード等のIRを保持してスタブ化。整列、安定順序、maxRecords/maxSortBytesの実行実装は継続課題 |
| 不明構文・型 | unsupported / 生成不可 | 復旧可能な文・ブロックは位置付きC#スタブ、欠落COPYや未知PICはUnknownCobolValue。復旧不能な構造は診断して生成しない |

`IS NUMERIC` は格納済みNumberなら負数・小数を含めtrueです。PIC Xでは数字列だけを許し、符号・小数点・空白・英字はfalseです。
これは値ベースStorageの契約であり、不正なゾーン／パック表現の検出ではありません。
数値→文字MOVEはFUNCTION ABS/LENGTH/NUMVAL/INTEGERや入れ子関数も含め拒否／スタブ化します。
未知関数を文字戻り値と推定せず診断します。文字戻り値のTRIM等は文字項目へMOVEできます。

portable-testのASCII照合・leading-separate・exact-rationalのみを通常実行します。
COMP/COMP-3/COMP-5/INDEXは数値値として保持し、実機バイナリ／パック配置は実装していません。
PIC X/AはASCII、PIC NはBMP。富士通EBCDICカナ／JEFをASCIIと同一視しません。

継続課題はREDEFINES、バイト列グループMOVE、編集PIC、COMP-1/2、OCCURS DEPENDING ON、SIGN拡張、SYNCHRONIZED、BLANK WHEN ZERO、MOVE ALL、文脈依存HIGH/LOW/QUOTE、LINKAGE/run-unit、REPORT/SCREEN、CICS/DLI/MQの実接続です。
未知のEXECやサービス構文を汎用成功応答に置き換えません。
元COBOL実機の業務同値性は未検証です。PythonBolとの一致を正解判定には使用していません。

実行上限はmaxSteps/timeoutMs、PERFORM戻り先10,000、表の基本項目あたり1,000,000要素、べき指数絶対値1,000です。
添字・参照変更は範囲検査します。source fixtureはスカラーと添字付きtableFieldsに対応します。
interpreterは移行比較用の旧backendで、追加の外部契約・表fixture・ACCEPTには対応しません。

`ACCEPT` の省略可能な `END-ACCEPT` と、`UNSTRING INTO` の受取項目間のカンマを解析する。
`ACCEPT ... FROM CONSOLE END-ACCEPT` は既存の `inputs` fixture で実行できる。
`FROM COMMAND-LINE` と `UNSTRING` の実行対応はこの構文修正には含まれず、sourceでは明示スタブになる。
