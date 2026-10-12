# 残る18本の frontend・source backend 対応

> 移行前の測定記録です。Pythonコマンドと件数は当時の証跡として保存しています。現在の実行手順は [C#移行記録](csharp-migration.md) と [テスト対応表](../tests/migration/test-mapping.md) を参照してください。

2026-10-11 UTC。添付指示書の基準は `c899ac8c08ad7ce460eebdbabe6e03c4eeaa0866`。
着手時に GitHub の PR #3 / #4 / #5 と origin を再確認したところ、PR #5 は main にマージ済みだった。
作業基準はその変更を祖先に含む `26de1f36eed45e75a90926196a5766f9183b40af`。
実装は `fix/remaining18-implementation` のコミット `911a81f` に保存し、ドラフトPR [#6](https://github.com/akrym1582/CobolCS/pull/6) を作成した。元の `/workspace/CobolCS` は変更していない。
添付ZIPの195本と追加修正の検証は [uploaded-samples.md](uploaded-samples.md) を参照。
基準測定は別 worktree `/workspace/CobolCS-remaining18` に固定した。

指定された [基準CI run 38110377924](https://github.com/akrym1582/CobolCS/actions/runs/38110377924) も再確認し、6ジョブとも success。今回の変更のリモートCIではない。

Linux、.NET SDK 10.0.401、Python 3.12.14。
最初の通常 sandbox ビルドは NuGet 脆弱性監査への接続で NU1900 / exit 1 となった。
許可されたネットワーク実行で復元・監査を再実行し、警告抑制や監査無効化は使っていない。
以下の成功値は接続問題を除外して書き換えた値ではなく、実際に再実行した結果を用いる。

## 設計と編集箇所

| 分類 | 変更 | 実行上の制約 |
|---|---|---|
| FD | `files.py` で句を順に読み、mode / label / block / data record / record length と各原文範囲を分離。01 と DEPENDING ON を解決 | 物理ブロック・ラベル・ホスト可変長 I/O は未実装。外部契約・スタブへ情報を渡す |
| PROGRAM-ID (`procedure.py`) | 引用名を正規化し、原名・属性・宣言位置を `programDeclaration` に保存 | INITIAL / RECURSIVE は strict 拒否、scaffold は寿命制御の入口スタブ。再帰・反復呼出しの状態分離を実装済みとしない |
| AUTHOR (`source.py`, `copybooks.py`) | identification 部の物理行で documentary entry を literal より先に認識 | 人名の apostrophe を削除しない。通常文字列・継続行と位置情報を維持 |
| 宣言復旧 (`declarations.py`) | 次の SECTION / DIVISION / 宣言 / 条件境界で停止し、未知トークンを所属付きで残す | unknown 宣言の意味を入口スタブが明示的に受け持つ。既知の手続きを消さない |
| P64 (`conditional.py`, `main.py`, `models.py`) | `conditional.py` で入れ子と分岐境界を検査。`p64` は true / false / null、既定 null | 未指定時は両分岐の原文・型候補を保存し unknown 化。手続きは全ブロックの単一スタブで、両 CALL を外へ漏らさない |
| COPY / USING (`declarations.py`, `models.py`, `procedure.py`) | 欠落 COPY の宣言 scope と原文・REPLACING を保存。scaffold の USING root は LINKAGE の欠落 COPY 名と対応する場合だけ許可 | SQLCA の内部項目・サイズ・オフセット・初期値を合成しない。既知 WORKING-STORAGE を USING に使う誤りは拒否 |
| C# スタブ (`SourceBackend.cs`, `.Statements.cs`, `.Dependencies.cs`) | 既知参照と `CobolReference<UnknownCobolValue>` を混在させ、限定できる参照を全状態引数へ広げない | 読み書きが分からない unknown ブロックには状態全体を渡す。未編集は例外型・文 ID・位置付きで失敗 |

IR は追加フィールドを持つ 2.2。既存ノードの実行時 IR 解釈を追加していない。
宣言・条件・COPY の情報は `reports/ir.json`、FD は `reports/dependencies.json` と該当スタブにも残る。
既存 source map、編集済みスタブの契約照合、管理ファイルのハッシュ・パス・symlink 検査は維持する。

未指定条件の elementary 宣言は、両分岐から名前と宣言境界だけを取り出し、実際の PIC / usage / 初期値を選ばない。
条件付き padding の周囲の既知スカラーは型を保持し、グループの物理レイアウトを確定済みとしない。
既存 Db2 の `dbconf.cbl` / `d_dbmcon.cbl` には固定桁内の tab により宣言・ディレクティブが確定できない箇所もある。
入力バイトや sourceFormat を変えず、その条件領域を opaque metadata と入口スタブで保持する。
階層が異なる分岐など名前の結び付けを安全に作れない入力は、明示条件・原文確認を求めて拒否する。

## 最終実測（P64 未指定）

before は今回再実行した最新 main 基準、after は未コミットの実装を含む測定。過去資料の PR #3 とは別の比較である。

| set | 対象 | 通常解析 before→after | strict生成 before→after | scaffold生成 before→after | build before→after | scaffold復旧解析 after |
|---|---:|---:|---:|---:|---:|---:|
| course | 30 | 27→30 | 5→5 | 22→25 | 27→30 | 25 |
| fun | 2 | 2→2 | 0→0 | 2→2 | 2→2 | 2 |
| carddemo | 44 | 41→44 | 0→0 | 41→44 | 41→44 | 44 |
| bank | 43 | 34→43 | 0→0 | 34→43 | 34→43 | 43 |
| db2 | 52 | 49→51 | 0→0 | 49→52 | 49→52 | 52 |
| 合計 | 171 | 153→170 | 5→5 | 148→166 | 153→171 | 166 |

`analysisHasErrors=true` は after 166 件。通常解析の成功を error-free / native 対応とは数えない。
全171件の source生成・build を確認し、基準153件の生成・ビルド退行は0件。18件の入力ID・パス・SHA-256・lock hash は before と同一。

fixture実行3件、独立期待値一致3件、うち編集C#スタブ実行1件（ADDAMT）。HELLO / PAYROL00 / ADDAMT は reports / manifest なしの別ディレクトリでもビルド・実行一致。ADDAMT は未編集スタブ失敗と編集バイト保持も再確認した。元COBOL実行比較・一致は0件。

実測 summary は [保存ディレクトリ](../tests/public-corpus/results/remaining18/)、171件の照合結果は [verification.json](../tests/public-corpus/results/remaining18/verification.json)。
全 phase ログ・診断・生成物は作業環境の `out/parser18/after-final/<set>/<case-id>/`、before は `/workspace/CobolCS-remaining18/out/parser18-baseline/before/` に保持。途中の探索測定 `out/parser18/after/` は停止済み・未完了であり、この集計には使っていない。

両測定の `compilerCommit`: `26de1f36eed45e75a90926196a5766f9183b40af`。未コミット差分の識別は次のソースハッシュによる。

| 測定 | compilerSourceSha256 |
|---|---|
| before | `86564fd9e8604a37376de8a3f11404050d8caef949f2e5203e1efc570255e645` |
| after | `e75d814f2fa4c08dd1d18e9e09ec060736bb8eacfa68cfa928859181a4433721` |

## 18本の対応表

「通常」は通常 analyze の parsed 判定で、下表の全件に要確認診断がある。「復旧」は scaffold 解析。strict/scaffold/build は実際の結果。原文の完全なパス・hash・残る全診断コードは [cases.json](../tests/public-corpus/results/remaining18/cases.json) にも保存した。

| set / case ID | 元パス | 旧障害 → 修正 | 通常 / 復旧 | strict / scaffold / build | 主な残依存 |
|---|---|---|---|---|---|
| course / `course-6647d6c889ee` | `COBOL Programming Course #3 - Advanced Topics/Labs/cbl/CBLDB21.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | COPY: SQLCA; 外部: call/file/sql; 未対応文/型は診断参照 |
| course / `course-a8499f832a16` | `COBOL Programming Course #3 - Advanced Topics/Labs/cbl/CBLDB22.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | COPY: SQLCA; 外部: call/file/sql; 未対応文/型は診断参照 |
| course / `course-36c4a41919e1` | `COBOL Programming Course #3 - Advanced Topics/Labs/cbl/CBLDB23.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | COPY: SQLCA; 外部: call/file/sql; 未対応文/型は診断参照 |
| carddemo / `carddemo-069938f27c5c` | `app/app-vsam-mq/cbl/COACCT01.cbl` | Expected ., got IS → 名前正規化・属性を入口スタブへ | ✓ / ✓ | — / ✓ / ✓ | COPY: CMQGMOV, CMQMDV, CMQODV, CMQPMOV, CMQTML, CMQV; INITIAL/RECURSIVE寿命; 外部: call; 未対応文/型は診断参照 |
| carddemo / `carddemo-405c1833bbe6` | `app/app-vsam-mq/cbl/CODATE01.cbl` | Expected ., got IS → 名前正規化・属性を入口スタブへ | ✓ / ✓ | — / ✓ / ✓ | COPY: CMQGMOV, CMQMDV, CMQODV, CMQPMOV, CMQTML, CMQV; INITIAL/RECURSIVE寿命; 外部: call; 未対応文/型は診断参照 |
| carddemo / `carddemo-c8bd92491fae` | `app/cbl/CBACT01C.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | 外部: call/file; 未対応文/型は診断参照 |
| bank / `bank-54c1ee245e23` | `src/base/cics/cobol/BNK1CCA.cbl` | Expected PROCEDURE, got  → documentary を literal 前に分離 | ✓ / ✓ | — / ✓ / ✓ | COPY: BNK1ACC, DFHAID; 外部: EXEC CICS等の未構造化スタブ; 未対応文/型は診断参照 |
| bank / `bank-ba356e3d8853` | `src/base/cics/cobol/GETCOMPY.cbl` | Expected PROCEDURE, got  → documentary を literal 前に分離 | ✓ / ✓ | — / ✓ / ✓ | 外部: EXEC CICS等の未構造化スタブ; 未対応文/型は診断参照 |
| bank / `bank-63622437fc29` | `src/base/cics/cobol/INQACCCU.cbl` | Expected PROCEDURE, got  → documentary を literal 前に分離 | ✓ / ✓ | — / ✓ / ✓ | COPY: SQLCA; 外部: sql; 未対応文/型は診断参照 |
| bank / `bank-04bac112c920` | `src/base/ims/cobol/IBTRAN.cbl` | Expected ., got RECURSIVE → 名前正規化・属性を入口スタブへ | ✓ / ✓ | — / ✓ / ✓ | INITIAL/RECURSIVE寿命; 外部: call; 未対応文/型は診断参照 |
| bank / `bank-6eed24385d54` | `src/base/ims/cobol/LOADACCT.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | 外部: file; 未対応文/型は診断参照 |
| bank / `bank-245c38699441` | `src/base/ims/cobol/LOADCUSA.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | 外部: file; 未対応文/型は診断参照 |
| bank / `bank-22a61aba51f6` | `src/base/ims/cobol/LOADCUST.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | 外部: file; 未対応文/型は診断参照 |
| bank / `bank-14724425a6bd` | `src/base/ims/cobol/LOADHIST.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | 外部: file; 未対応文/型は診断参照 |
| bank / `bank-7d3a237453fd` | `src/base/ims/cobol/LOADTSTA.cbl` | Unsupported FD record clause → FD の句別解析・01/DEPENDING 接続 | ✓ / ✓ | — / ✓ / ✓ | 外部: file; 未対応文/型は診断参照 |
| db2 / `db2-6ba20b6af2cb` | `cobol_mf/checkerr.cbl` | 通常は sqlca 未解決／scaffold は USING が LINKAGE でない → 欠落 COPY に由来する LINKAGE unknown root | — / ✓ | — / ✓ / ✓ | COPY: sql.cbl, sqlca.cbl; 外部: call; 未対応文/型は診断参照 |
| db2 / `db2-b1d02753e195` | `cobol_mf/tabscont.sqb` | Expected PROCEDURE, got  → 条件境界・未知レイアウト・単一ブロックスタブ | ✓ / ✓ | — / ✓ / ✓ | COPY: sql.cbl, sqlca.cbl, sqlutbcq.cbl, sqlutil.cbl; P64未指定; 外部: call; 未対応文/型は診断参照 |
| db2 / `db2-362287f65bae` | `cobol_mf/tabspace.sqb` | Expected PROCEDURE, got  → 条件境界・未知レイアウト・単一ブロックスタブ | ✓ / ✓ | — / ✓ / ✓ | COPY: sql.cbl, sqlca.cbl, sqlenv.cbl, sqlutbsp.cbl, sqlutil.cbl; P64未指定; 外部: call; 未対応文/型は診断参照 |

Db2 `checkerr.cbl` の通常解析失敗は入力 SQLCA 不足によるもので、scaffold の復旧を通常解析の成功に置き換えていない。

`src/base/cics/cobol/` の3本には CICS の EXEC スタブが残る。依存一覧に構造化されない EXEC を外部処理なしとは扱わない。

P64 true / false の別枠検証では tabscont / tabspace の計4生成・ビルドが成功した。条件・configHash・選択されたPICは [p64-explicit.json](../tests/public-corpus/results/remaining18/p64-explicit.json)。両ケースとも true の数値宣言は18桁、falseは9桁であり、ポインタの実機配置を検証した意味ではない。

## 最終版の検査結果

| コマンド / 検査 | 結果 |
|---|---|
| `dotnet build CobolCS.sln -m:1` | exit 0、警告0・エラー0。StyleCop有効 |
| `dotnet format whitespace ... --verify-no-changes --no-restore` | exit 0 |
| runtime tests | exit 0、12 assertions |
| `python3 tests/regression.py` | exit 0、60件（基準56件を維持） |
| `python3 tests/test_public_corpus.py` | exit 0、12件（基準11件を維持） |
| `python3 tests/test_remaining18.py` | exit 0、18件の新規parser正負試験 |
| `git diff --check` | exit 0 |

コマンド別記録と出力は [tests.json](../tests/public-corpus/results/remaining18/tests.json) と同ディレクトリの logs/ に保存。
公開入力の原文・生成物は通常コミット対象に入れていない。CLIへコピーしたfrontendと作業ソースのバイト一致も確認した。

## 自作最小例と検査

`tests/test_remaining18.py` は元コーパスを転載しない parser の正負試験。
FD の組合せ・両語順・重複・矛盾・不正範囲・境界、引用 PROGRAM-ID と属性・END PROGRAM、
documentary の apostrophe・固定桁・literal / escape / continuation、未終端 literal、
P64 の三状態・入れ子・不正 ELSE / END・procedure 保持・token 所属、COPY 提供・REPLACING・曖昧探索を検査する。

`tests/regression.py` に C# 生成・実行試験を追加した。
AUTHOR の後の `DISPLAY 'KEPT'` は通常本文で `KEPT` を出力する。
P64 真/偽はそれぞれ `64` / `32` と後続の `AFTER` を出力し、三設定の config hash は異なる。
未指定の同ブロックは `NotImplementedException`、文 ID `s000000`、原文行まで確認する。
INITIAL / RECURSIVE の入口、欠落 LINKAGE COPY の入口も例外型・文 ID・位置を確認する。
これらの期待値は自作最小例から独立に決めたもので、元 COBOL 実機の結果ではない。

通常の業務本文は例えば次の形で出る（名前は生成時に可逆符号化される）。

```csharp
display.Add(string.Concat(new string[] { CobolValue.Text("KEPT") }));
signal = "goback";
if (signal.Length != 0)
{
    return;
}
```

`checkerr` の未知 SQLCODE 参照の契約例は次の形。
実際のパス・署名・原文・引数順は各生成先の `reports/stubs.json` を正とする。

```csharp
ControlResult Statement_s000000(
    CobolReference<UnknownCobolValue> value_f000009,
    SourceLocation source);
```

既知 LINKAGE 引数には `CobolReference<string>` などの型付き参照を維持する。
UnknownCobolValue はホストレイアウトの代替実装ではなく、未確定を明示する型である。

## 次工程

### 再現コマンド

基準 worktree は上記 main コミットに固定し、同じコーパスディレクトリを前後で使用した。
`fetch` / `evaluate` は新規ディレクトリを要求するため、再実行時は未使用の出力名を選ぶ。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build --filter "FullyQualifiedName~RegressionTests|FullyQualifiedName~CorpusToolTests|FullyQualifiedName~Remaining18Tests"
git diff --check

# CORPUS_ROOT: before で fetch した5セット。MEASURE_ROOT: 新しい測定保存先。
for set in course fun carddemo bank db2; do
  dotnet run --project tools/CobolCS.Tools --no-build -- evaluate \
    --lock "tests/public-corpus/$set.lock.json" --root "$CORPUS_ROOT/$set" \
    --out "$MEASURE_ROOT/after/$set" --build || exit 1
  dotnet run --project tools/CobolCS.Tools --no-build -- verify "$MEASURE_ROOT/after/$set/summary.json" || exit 1
done
dotnet run --project tools/CobolCS.Tools --no-build -- verify-remaining18 "$MEASURE_ROOT/before" "$MEASURE_ROOT/after"
```

`CobolCS.Tools verify-remaining18` は171件・元153件・残り18件・同一入力hash・各測定内でのcompilerSourceSha256一致・
3件の独立実行・ADDAMTの編集保持を検査する。元の generation floor を変えて成功させる処理はない。

### 利用者による実装

1. lock の sourceFormat / copybookPaths で C# を生成し、`reports/stubs.json` と原文対応を確認する。
2. 本物のベンダー COPY、明示した P64 とホスト契約を提供する。再生成の契約が変わるときは新しい出力先を使う。
3. `Stubs/<program>/*.cs` にファイル・DB・呼出し寿命等の必要処理を実装する。未知値をゼロや成功応答で埋めない。
4. `check`、fixture 実行、独立期待値または別途採取した実機 baseline との比較で動作を固定する。
5. 編集済みスタブを保持する再生成を確認してから、C# のリファクタリングと業務ルール抽出へ進む。

DB2/CICS/IMS/MQ の実接続、元 COBOL 同値性、本番移行は未検証。マージ・デプロイ・公開は行っていない。
上流 LICENSE / NOTICE・Db2 個別 sample notice を取得ディレクトリ内に保持し、生成物配布時の帰属確認を引き継ぐ。

---

[ドキュメント案内](README.md)へ戻る。
