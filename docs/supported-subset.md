# 対応範囲

この表は CobolCS の C# backend の範囲です。採用した Python 解析器が理解できる範囲とは異なります。
解析できても C# に未実装の機能は `CS_*` 診断で strict 生成を拒否します。

| 分類 | 実行対応 |
|---|---|
| 入力 | UTF-8、free / fixed format、ディレクトリ内の .cbl / .cob |
| 前処理 | 再帰 COPY、検索ディレクトリ、COPY REPLACING、循環・欠落の診断、元位置と COPY 展開履歴 |
| 宣言 | 01–49 / 77、88 条件名、固定 OCCURS と多次元添字、項目名の OF / IN 修飾 |
| PIC | 9 / S / V、X / A、N。数字は最大 1,000 桁、scale 0–1,000 |
| 算術 | ADD / SUBTRACT / MULTIPLY / DIVIDE / COMPUTE、四則演算、整数べき、ROUNDED、ON / NOT ON SIZE ERROR、商と余り |
| データ操作 | 要素 MOVE、文字列の空白埋め・切り詰め、JUSTIFIED、文字列の参照変更、SET 88 TO TRUE、SET index TO / UP / DOWN |
| 条件 | IF / ELSE、EVALUATE 単一 subject / TRUE、比較、AND / OR / NOT、88、符号・文字クラス条件 |
| 制御 | inline PERFORM TIMES / UNTIL / VARYING、入れ子、EXIT PERFORM、段落 / SECTION PERFORM THRU、GO TO、GO TO DEPENDING ON、CONTINUE、GOBACK / STOP RUN / EXIT PROGRAM |
| 出力 | DISPLAY を行の配列として記録。数値項目は符号・ゼロ埋め・暗黙小数点を含む DISPLAY 形式 |
| 関数 | ABS、LENGTH（文字引数）、UPPER-CASE、LOWER-CASE、TRIM（1引数）、REVERSE、NUMVAL（単純な十進表記）、MIN、MAX、INTEGER、INTEGER-PART、MOD、REM |

portable-test の ASCII 照合、leading-separate、exact-rational を対象とします。
COMP / COMP-3 / COMP-5 / INDEX は数値として保持しますが、バイナリ・パック十進のバイト配置を実装したものではありません。
そのため REDEFINES、グループ MOVE、数値の参照変更、外部のバイト列との交換は拒否します。
PIC N は .NET の BMP 文字を扱い、空白埋めと切り詰めを行います。UTF-16BE / LE のバイト列を出力する機能はありません。
PIC X / A に格納する文字は ASCII に限定します。日本語には PIC N を使います。

主な未対応機能：

- REDEFINES、編集 PIC、COMP-1 / COMP-2、OCCURS DEPENDING ON、SYNCHRONIZED、BLANK WHEN ZERO、非対応の SIGN 属性。
- グループ操作、INITIALIZE / CORRESPONDING、MOVE ALL、数値から文字列への MOVE、HIGH / LOW / QUOTE の文脈依存定数。
- STRING / UNSTRING / INSPECT、SEARCH、SORT / MERGE、NEXT SENTENCE、PERFORM VARYING AFTER、TEST AFTER と VARYING の組合せ。
- FILE I/O、SQL、外部 CALL、CICS / DLI / MQ、LINKAGE の実行時引数接続、run-unit のプログラム間呼出し。
- 富士通 / IBM の実機格納・文字コード・照合・業務同値性。

TEST AFTER UNTIL は対応します。残余計算のある DIVIDE は解析器が構成できる単一の商・割り算式を対象とします。
戻り先スタックは 10,000、各項目の表要素数は 1,000,000、べき指数の絶対値は 1,000 を上限とします。
`maxSteps` / `timeoutMs` によって実行を制限します。添字・参照変更の範囲外は実行エラーです。
fixture 初期化はスカラー項目だけに対応します。

scaffold では、バッチ内にエラー診断が1件でもあれば、すべての生成 entry を要レビューの実行拒否にします。
未対応構文を通過させて成功を記録しません。
