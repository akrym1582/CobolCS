# 公開コーパスと source backend の検証

固定した公開 COBOL 入力を用いて、解析・生成・ビルドを検証する開発者向けの手順です。普段の変換には[README](../README.md)のサンプルを使用してください。コーパス取得には Git とネットワーク接続が必要です。

## CIの分割評価

carddemo（44件）・bank（43件）の生成／ビルドは各4ジョブで、固定lockの元の順に `index % shardCount` で分割します。course・fun・db2は1ジョブずつです。全分割で同じlockと取得済み入力を検証し、割り当てられた各ケースの既存工程をそのまま実行します。生成器の探索予算やMSTestの検証を減らしません。

```sh
# 0～3の各分割を別ジョブで実行。--outは分割ごとに新規ディレクトリを指定する。
dotnet run --project tools/CobolCS.Tools --no-build -- evaluate --lock tests/public-corpus/carddemo.lock.json --root .cache/public-cobol-corpus/carddemo --out out/carddemo-part-0 --build --shard-index 0 --shard-count 4
# summaries/の直下または直下の分割ディレクトリへ全summary.jsonを集める。
dotnet run --project tools/CobolCS.Tools --no-build -- merge-evaluations --lock tests/public-corpus/carddemo.lock.json --input summaries/carddemo --out out/public-corpus/carddemo/summary.json
```

分割のsummaryは元の全ケース数・ID・生成下限を保持するため、単独の`verify`では成功しません。`merge-evaluations`は全分割の完了・ケースIDと原文hash・元lock・コンパイラーcommit/source hash/SDKを照合し、元の順に統合して全体の`verify`を実行します。分割の欠落・重複、別条件、ケース誤配を拒否し、生成下限・生成物ビルド・独立期待値・スタブ編集保持の条件を維持します。固定lockと期待IRは変更しません。

summaryにはケース／工程のdurationMsを追加し、ケースごとの完了進捗をstderrへ出します。stdoutの件数JSONは維持します。CIは分割summaryと統合summaryをartifactへ保存し、テスト成功や生成結果をキャッシュしません。

追加コーパスの取得済み素材は `tasks/deep-corpus/fetch.sh --cached-only` で通信せず検証できます。
全在庫と実際の取得・解析・生成・ビルド件数は分け、未取得caseは未実施として残します。
出典・固定版・個別ライセンス・通知を確認した小さな外部fixtureに限り
`tests/fixtures/third-party` へ原本・変更記録・独立期待値を取り込みます。
未確認原文や大量の生成物は従来どおり外部キャッシュに保持します。
具体的な採用判断は [ライセンス確認](deep-corpus-license-review.md) を参照してください。
取得済み入力の実測、独立期待値、未実施範囲は [今回の結果](deep-corpus-results.md) に記録しています。

添付ZIPの実体照合・任意GPL24本の追加検証は [uploaded-samples.md](uploaded-samples.md) を参照。
18本の追加対応と同一171本の再測定は [remaining18.md](remaining18.md) を参照。
以下の PR #3 基準の履歴は保持し、新しい測定の before と混同しない。
新しい評価結果は `scaffoldAnalyzed` / `scaffoldAnalysisHasErrors` も記録し、欠落 COPY を許さない通常解析と分ける。

PR #3 の `5234e2b84b4e93983c4d7abb0764a44a5df91953` を基準に、通常 C# の生成・ビルド・編集したスタブの実行を測定する。
実行時に IR を解釈する方式へ戻さない。JSON は診断、fixture、結果比較だけに使用する。
前回の interpreter 向け作業ディレクトリは別 worktree として保持し、この評価器は source backend を明示する。

## 入力、帰属とライセンス

PR #3時点ではLibraryの指示書本文は参照できたが、ZIPの消費側materializationは再試行も失敗した。
そのため公開上流を固定コミットから取得した。当時はZIP自体との同一性が未確認だったが、
追加添付後に既存171本とZIP内原本のパス・ハッシュ一致を確認した（詳細は追加検証報告）。
各リポジトリの全 `.cbl` / `.cob` / `.cobol` / `.sqb` を列挙すると、資料の非 GPL 171 本と件数が一致した。
これを過去の互換性実績としては扱わない。

| lock 名 | 上流 | 固定コミット | 本数 | 上流 LICENSE |
|---|---|---|---:|---|
| course | openmainframeproject/cobol-programming-course | 1f037d24326124f4f2c5cd2d31e5220ceb549580 | 30 | CC-BY-4.0 |
| fun | IBM/cobol-is-fun | d9244e6292db65fb5f87399246c0c70255f15255 | 2 | Apache-2.0 |
| carddemo | aws-samples/aws-mainframe-modernization-carddemo | 59cc6c2fd7ebd7ef7925cad552a01a4b8b6e4d5e | 44 | Apache-2.0 |
| bank | IBM/Bank-of-Z | 977924a39449178efd69b652765ed5b73da93baa | 43 | Apache-2.0 |
| db2 | IBM/db2-samples | 7be7b1016e13756358c1425ddae70eea4fd15fb0 | 52 | Apache-2.0 |

取得 URL と元パス、ソース・COPY・LICENSE の SHA-256 は `tests/public-corpus/*.lock.json` に固定。
原文、生成 C#、IR はコミットせず、外部キャッシュ／`out/` に保存する。
取得時に LICENSE、NOTICE、各ソースの表示を変更せず保持する。Db2 の各ソースには IBM 独自の sample notice もあるため、
ルート LICENSE だけで原文の全条件を置き換えない。生成物を配布する場合も元ソースの帰属・通知を別途引き継ぐ。

HELLO / PAYROL00 / ADDAMT の表示文と期待値の出典は上記 course。
Copyright Contributors to the COBOL Programming Course、[CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/)。
変更内容は、入力条件の明示・期待値の JSON 化・ADDAMT の単一 INSPECT 文に対応する C# 実装である。
独立計算の期待値であり、元 COBOL を実行して採取した結果ではない。

このPR #3時点の測定では任意の GPL 24 本（neopragma/cobol-unit-test）を取得・評価から除外した。
追加依頼により添付ZIPのGPL24本を外部作業領域で評価したが、通常CIには含めない。
`optional-gpl.lock.json` はパス・ハッシュ等だけを保持する。`--include-gpl` の明示指定が必要であり、171本の選択に自動追加しない。
GPL 原文とその生成物を通常コミットへ混ぜず、取得・利用・再配布の条件を区別する。
ベンダー COPY の欠落は診断に残し、無断で SQLCA 等の合成定義を補わない。

## 再現コマンド

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project tools/CobolCS.Tools --no-build -- fetch --lock tests/public-corpus/course.lock.json --root .cache/public-cobol-corpus/course
dotnet run --project tools/CobolCS.Tools --no-build -- evaluate --lock tests/public-corpus/course.lock.json --root .cache/public-cobol-corpus/course --out out/course --build
dotnet run --project tools/CobolCS.Tools --no-build -- verify out/course/summary.json
```

残りの lock 名についても同じ手順を実行する。`fetch` のみオンライン取得、`evaluate` は取得済みバイトを検証して使う。
NuGet 復元・監査は既存の dotnet ビルド契約に従う。全取得先・出力先は新規ディレクトリとし、以前の記録を上書きしない。
各 source の `fixed` 形式を明示し、拡張子・改行・原文を変更しない。
ケース ID は上流名と元パスの hash。PROGRAM-ID が同じでもケースを統合しない。

CardDemo は `app/cpy` と対象追加アプリの `cpy` / `cpy-bms`、Bank は対象の CICS / IMS の `copy` のみ探索する。
全リポジトリ横断で同名 COPY を探さない。COPY 候補を追加・変更すると lock 検証が失敗する。
`.cpy` と拡張子なしのファイルは自動 entry にしない。複数ファイルの compilation group は未対応。

`evaluate` は診断があっても全件の測定を続ける。正常にレポートを作れれば終了コード 0。
`verify` は件数・ID重複・生成数の下限・生成できた全プロジェクトのビルド・指定した期待値と単独実行を検査し、退行で 1。
未解析ケースを成功扱いしない。ケース詳細は `summary.json`、ログは `<case-id>/*.log.json` に保存する。
CI は非 GPL の5セットを実行し、生のコーパスや IR ではなく summary のみ artifact に残す。

ZIP が正規手順で配置できた場合の `import-archive` は、指定 ZIP SHA-256 と内部 `SHA256SUMS.txt` を検証し、
symlink・パス逸脱・重複 ZIP 名を拒否する。合成ZIPのテストに加え、追加添付された実ZIPでも722ファイルの整合性を確認した。
ZIP 内 `catalog.json` から本 lock 形式への変換は未実装。転載可否不明の資料は追加しない。

## 判定と実行の根拠

`analyzed` と `analysisHasErrors`、strict/scaffold 生成、ビルド、fixture 実行、C# スタブ実行、独立期待値一致を別々に数える。
`matchedOriginalCobol` は常に false。本タスクでは COBOL コンパイラ・ホスト環境との比較をしていない。
宣言・原文位置・条件・呼出し先・operand・COPY stack は各生成先の IR、依存・スタブレポートに保持する。
早期解析失敗では後続依存の抽出も未完了となる。元パスと hash から調査する。

- HELLO: 表示 `HELLO WORLD!` と GOBACK。
- PAYROL00: 19 × 23 = 437。数値表示の `019` / `023` / `00437` と PIC X の末尾空白を比較。
- ADDAMT: 架空入力 Ada、10、20、30、no。native C# で合計 60 を計算し、INSPECT だけを編集した C# スタブが実装。
  `noyes` の各文字を `NOYES` の対応文字へ変換する。業務応答や残高を仮に返さない。
  文の原文 hash・完全な型付き署名・C# 本文 hash を一致確認してから適用する。不一致・重複は拒否。
  未編集では失敗、編集後は実行成功、同一入力で再生成しても編集バイトを保持することを測定。
- CBL0014: `ABCDE` と COMP-3 の重ね合わせによる意図的な異常系。未対応スタブの失敗は実機の S0C7 再現ではない。
- SRCHSER / SRCHBIN、FXSORT、XFRFUN、openftch: 生成・診断の調査対象。SEARCH ALL、編集 PIC のソート、SQL・ベンダー呼出しを実行済みとしない。

明示実行する3ケースは、reports と manifest を除いた別ディレクトリで単独ビルド・実行し、結果一致を再確認する。
それ以外の生成物は未編集スタブへの到達を試験する場合があるが、終了コードだけで「スタブ失敗を確認」と断定しない。
ACCEPT不足、未対応初期値、実行制限もログにそのまま残る。C# スタブを成功実装した件数は ADDAMT のみ。

## 修正と残る制限

生成一時変数の入れ子衝突、FILLER／曖昧な fixture 名の重複ラベル、NOT ON SIZE ERROR 単独時の正常分岐誤実行を修正。
ON SIZE ERROR がない桁あふれ時の受信値は COBOL 規則で未定義なので、その値の同等性は主張しない。
portable の現実装は検出した受信項目を更新せず、NOT ON SIZE ERROR を実行しない。
根拠: [Fujitsu ON SIZE ERROR 規則 6–7](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/on-size-error-phrase-cobol-compiler-reference-manual-2022-11-197-22036903.html)。

編集済みスタブの結び付け変更は再生成を拒否する。CALLを並べ替えて同じ文ID・同じ型になっても、旧実装を別の呼出しへ流用しない。
古いスタブを削除せず保持し、新規ディレクトリへの生成と明示移植を求める。原文位置変更も保守的に拒否する。

追加した入力対応は番号開始の名前、文書用 AUTHOR 等、`.cobol` / `.sqb` 列挙、固定 `RECORDING MODE F`（RECORD CONTAINSの前後）、TOのない ADD GIVING、PROCEDURE USINGのカンマ区切り。
PROGRAM-ID 末尾の句点省略は既知の次ヘッダーがある場合だけ警告付きで受け入れる。文書ヘッダーの原文は IR に保持。
FDの物理ホストI/O互換性を実装したことにはしない。
共通演算をスタブへ後退させず、未知レイアウト・外部依存・未対応文は明示失敗を維持する。
外部サービスの JSON 応答列 fixture、実 DB/CICS/IMS/MQ 接続、業務同値性は未検証。

工程別の件数と失敗一覧は [public-corpus-results.md](public-corpus-results.md) と `tests/public-corpus/results/` の summary を参照。
独立レビュー3候補の再現と回帰は [public-corpus-review.md](public-corpus-review.md) を参照。

---

[ドキュメント案内](README.md)へ戻る。
