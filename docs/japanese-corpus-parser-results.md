# 日本語コーパスの解析器修正

この変更は `796997457905fe42b23fa8521714d8168aeb8eaf` を基準に、C#解析器と既存source/interpreterの実行・観測基盤へ修正を移植したものです。旧Python構成での実装・検証は保存ブランチ `fix/japanese-corpus-parser-legacy` の `6e8f068` に保持し、その成功件数を現行C#版の成功件数へ加算しません。

## 変更

- ソースとCOPYにUTF-8/CP932/Shift_JISを明示指定。不正バイトは `INPUT_DECODE` で実ファイル・行・バイト位置・COPY履歴を残し、原バイトを保持します。CP932の合法な重複符号を.NETのbest-fit/置換設定に依存せず復元し、Shift_JISではWindows拡張を拒否して6文字の対応差を保持します。これはランタイムの格納文字コード変更ではありません。
- 日本語名の `-` / `－` / `−` を別名として保持。COPYの部分語置換、SECTIONと項目参照に同じ字句規則を適用し、NFKC正規化で同名化しません。
- 固定形式の単語継続へ区切りを挿入せず、原文字位置・原バイト位置を保持します。既定の固定形式日本語列のホスト確認診断は維持します。
- `項目(開始位置:)` の省略長を末尾までとして解釈。source/interpreterの読取り・書込みとCALL BY REFERENCEの生成へ反映し、範囲外と加算オーバーフローを拒否します。
- 空のPROCEDURE DIVISIONを正常な空プログラムとして保持。空入力は `EMPTY_SOURCE` として記録します。
- DISPLAY UPONを値・宛先・NO ADVANCINGとともにIRへ保持し、デバイス接続は `CS_DISPLAY_UPON` とsourceスタブで可視化。不正・重複句で次の文を脱落させません。mainで既に対応済みのNO ADVANCING文単位captureと、限定LINKAGE接続の契約は維持します。

## 固定コーパスの解析測定

ZIP SHA256: `0fb48d8d2a98eb235fc14b346fc7e2b7e985adf512c7303c4d805972bffab024`。

INDEXには847ケースと入力934ファイルの宣言がありますが、case.json実体は500件、入力実体は550件でした。SHA台帳3,667項目は2,075一致、1,591欠落、1不一致。不一致は `opensourcecobol4j/i18n_sjis/limits/013/input/prog.cob`。347件のcase.json欠落と1件の不一致は入力阻害として残しています。manifest・上流ソースアーカイブ・ライセンス原本等も欠落しています。元の文字コード・期待値・UTF-8閲覧版は編集していません。

| 工程（分母847ケース） | main基準 | 修正後 |
|---|---:|---:|
| 原入力不備で阻害 | 348 | 348 |
| 解析APIのIR・診断出力 | 406 | 499 |
| 設定拒否等のAPI例外 | 93 | 0 |
| Frontendエラー診断なし | 80 | 86 |

修正後499ケースのIR内原バイトは入力と一致しました。エラー診断付き出力を解析成功・実行成功としていません。今回の現行main測定は解析工程だけです。上流実行比較、全コーパスの生成・ビルド・実行、富士通互換性を検証したとは扱いません。

[before](../tests/japanese-corpus-results/native-before.json)、[after](../tests/japanese-corpus-results/native-after.json)、[CSV](../tests/japanese-corpus-results/native-after.csv) は全847行の原文なし測定記録です。診断はcode/file/line/byteOffsetのみ。入力や生成C#を転載・再ライセンスしていません。使用したFrontend DLLのSHA256は各summaryにあります。測定は同一ケース・原バイト・sourceFormat・sourceEncoding・COPY探索条件で `FrontendAnalyzer.Analyze` を呼び出し、API例外と診断を分けました。通常の固定期待IR・コーパスlockは変更していません。

## 検証

.NET SDK 10.0.401 / Runtime 10.0.12、Linux。通常ビルドは警告・エラー0、Frontend全194件・Runtime全47件成功、skip 0。日本語の追加結合27件成功、skip 0。UTF-8/CP932/Shift_JIS、原バイト・COPY位置、異なるハイフン名、継続行、省略長と境界を独立C#アセンブリで検証しました。source/interpreter双方の実行と、外部CALLのコンパイルのみの検査を区別しています。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet test tests/CobolCS.Frontend.Tests --no-build --no-restore
dotnet test tests/CobolCS.Tests --no-build --no-restore
MSBUILDDISABLENODEREUSE=0 dotnet test tests/CobolCS.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~JapaneseParserTests
git diff --check
```

上記はすべて成功。通常のIntegration全件も `MSBUILDDISABLENODEREUSE=0` で実行中で、最終結果をPRへ追記します。新規クラスは既存CIのworkflows-b相補フィルタに含まれます。初回のVSTest/formatはサンドボックスのローカルソケット禁止で阻害されましたが、必要なソケット権限を付与した再実行は成功しています。制約回避のためにテスト、StyleCop、警告を無効にしていません。

## 残課題と再現

固定形式のホストのバイト列・タブ規則、PIC X/Nのホスト格納・照合、DISPLAY端末・デバイス出力、上流マクロ条件と独立期待値の確認は未完了です。入力が欠落した348件は追加資料なしでは検証できません。生成characterizationはC#の挙動固定であり、元COBOLとの同値性や移行完了とは区別します。

添付ZIPをリポジトリ外へ展開し、二重トップフォルダー内の `INDEX.csv` / `SHA256SUMS.json` を確認します。ハッシュが一致するケースのinputだけを使用し、ケースごとに設定JSONと出力を分離してください。`ascii`入力はUTF-8、明示freeオプションのない上流ケースはfixedで測定しました。COPY探索先はそのケースのinputです。例:

```sh
dotnet src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll analyze /external/corpus/cases/<case-id>/input -I /external/corpus/cases/<case-id>/input --config /tmp/case-config.json -o out/japanese/<case-id>
```

設定例は `{"sourceEncoding":"cp932","sourceFormat":"fixed"}`。診断、IR、原バイトを確認し、解析成功・生成・ビルド・実行・独立期待値一致を別に記録してください。通常テストにはコーパスを大量登録せず、追加した独自最小再現だけを使っています。取得元と参考ケースIDはテストコメントに記録しています。
