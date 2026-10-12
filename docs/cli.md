# CLI の契約

初めて使う場合は[README の AMOUNT 例](../README.md#5分で試す)から始めてください。このページは、コマンドの引数や結果の詳細を確認するための仕様です。

CBLの生成から入力・スタブの補完、起動までの手順は[生成C#の実行ガイド](generated-csharp-quickstart.md)を参照してください。CobolCSの`run`と、生成されたProgram.csの引数の違いも説明しています。

`dotnet run --project src/CobolCS.Cli -- <command>` またはビルド後の `dotnet src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll <command>` で呼び出します。

解析は同梱のC#解析ライブラリを直接呼び、Pythonプロセスを起動しません。IRはschema 2.2、`frontendVersion`は`0.3.0-csharp`です。
旧`COBOLCS_PYTHON`と`COBOLCS_REFERENCE_PYTHON`は参照しません。コーパス操作・デモ準備は別の[.NET補助ツール](csharp-migration.md#製品と補助ツール)を使用します。

基本形は `cobolcs <file.cbl|directory> [options]` です。`generate` は省略できます。
出力先は既定でカレントディレクトリの `<入力名>-csharp/`（analyze は `<入力名>-analysis/`）。
入力名はファイルの拡張子を除いた名前、または末尾のディレクトリ名です。
`-o` / `--output` で変更できます。生成先・診断・C#ソースの場所を実行時に表示します。
`-I` / `--copybook-path` は繰り返し指定できます。`--scaffold` は `--mode scaffold` の短縮形で、`--mode` と同時指定できません。
`--mode strict` と `--backend interpreter` は詳細な検証・比較用に維持します。
`run` は単一プログラムなら `--program` を省略できます。複数ある場合は候補を表示します。
`--help` / `-h`、各コマンド直後の `--help` で使い方を確認できます。
`--` 以降は入力パスです。コマンド名と同じ入力は `./run` のように、オプション値の先頭が `-` なら `./-name` と指定します。

旧 `translate` は `generate`（省略可）、`check` は `build`、`--out` は `-o` / `--output` に変更しました。
compare は `compare <actual.json> <baseline.json>` になりました。旧構文との互換性はありません。
補助ツール CobolCS.Tools の `--out` は別の契約です。

| コマンド | 役割 |
|---|---|
| `analyze <input> [-o <dir>]` | frontend と source の対応検査、IR・診断を保存。C# は生成しない |
| `generate <input> [-o <dir>] [--backend source\|interpreter] [--mode strict\|scaffold]` | 独立した .NET 10 プロジェクトを生成 |
| `build <dir>` | 管理対象のハッシュ検査、dotnet build、built 判定の更新 |
| `run <dir> [--program <id>] [--fixture <json> \| --scenario <name>] [--output <json>]` | ハッシュ検査・ビルド後に生成アセンブリを実行。fixture と scenario は同時指定不可 |
| `compare <actual.json> <baseline.json>` | JSON の全体一致。キー順は無視、配列順・値は厳密比較 |
| `adopt <dir> --output <new-dir>` | レビュー済みの生成物を編集可能な別ディレクトリへ採用 |

0.2以降の既定は `source / scaffold`。`interpreter` の既定モードは従来どおり `strict` です。
source は通常のC#を生成し、未対応部分を暗黙にinterpreterへ戻しません。
source strict は未解決のPROGRAM-IDを `manifest.rejectedPrograms` に記録し、他のPROGRAM-IDは生成します。
CALL・逐次ファイル・構造化SQLの明示的な契約はstrictでも許可します。これはアダプターの実装完了を意味しません。
設定・重複PROGRAM-IDなどの致命的問題は全体を拒否します。
interpreter scaffold は従来の入口拒否を維持します。比較・移行用途です。

analyze / generate は `--config <json>` と繰り返しの `--copybook-path <dir>` を受け付けます。
終了コードは0が出力成功、1がstrictの全部／一部拒否または比較不一致、2が引数・I/O・ビルド・実行エラーです。
analyze / scaffold は診断の出力成功でも0になるため、`judgments.generated` と `reports/diagnostics.json` も確認してください。
設定例は `{"sourceFormat":"fixed","maxSteps":1000000,"timeoutMs":10000}`。
未知の設定項目は拒否し、未指定はportable-testを使用します。ホストプロファイルを通常実行として許可しません。

`p64` は `$IF P64 SET` の明示条件です。許容値は JSON の `true` / `false` / `null` だけで、
省略時は `null`（未指定）です。実行マシンのビット数から選びません。
例えば `{"sourceFormat":"fixed","p64":true}` は真側だけを解析し、選ばなかった原文と位置を除外記録へ残します。
条件は `config` と `configHash` に含まれ、IR の `conditionalDirectives` に両分岐・選択値・原文位置が残ります。
未指定の宣言は unknown 型となり strict は拒否します。未指定の手続きブロックは全体を単一の編集可能スタブにします。
分岐の構造が復旧できない宣言は入口スタブで明示失敗し、異なる階層の宣言など安全に境界を作れないものは拒否します。
条件を明示してもベンダー COPY、ポインタ配置、実ホスト契約の不足は解決しません。

## fixture

初期値はCOBOLのVALUE句です。sourceは次の形式を受け付けます。

```json
{
  "fields": {"TOTAL": "12.34", "CODE": "A"},
  "tableFields": [
    {"name": "AMOUNT OF ROW", "subscripts": [1, 2], "value": "9.25"}
  ],
  "inputs": ["FIRST", "SECOND"]
}
```

値は文字列、名前は修飾名または曖昧でない項目名。`fields` はスカラーのみです。
`tableFields` は外側から内側の1始まり添字を指定します。次元数・範囲・同一要素への重複指定を検査します。
`inputs` は `ACCEPT item` / `ACCEPT item FROM CONSOLE` が順に消費します。入力不足はその文の実行時に位置付きで失敗します。
他のFROM形式はスタブ／strict拒否です。暗黙の標準入力読み込みや仮の空値はありません。
interpreterは従来のスカラーfixtureのみです。
外部応答のJSON fixtureは未実装です。互換用の空 `calls: []` / `files: {}` は許容し、実データ入りは拒否します。

## C#スタブの実装

```sh
dotnet run --project src/CobolCS.Cli -- generate payment.cbl --output out/payment
# reports/stubs.json の file と signature に従い Stubs/<program>/<statement>.cs を編集
dotnet run --project src/CobolCS.Cli -- build out/payment
dotnet run --project src/CobolCS.Cli -- run out/payment --program PAYMENT
```

例えば数値項目を1個BY REFERENCEで渡すCALLは、型付きRequest/Responseと次のような実装箇所になります。
`PaymentResponse` は説明用の名前です。実際はPROGRAM-IDと文IDから決定する生成名を使用してください。

```csharp
public PaymentResponse Dependency_s000000(PaymentRequest request)
{
    return new PaymentResponse("00", DependencyOutcome.Normal,
        request.Argument0 + new Number(1));
}
```

応答の引数順と型は `Contracts/*Request.g.cs` を確認してください。
`SourceLocation` はPROGRAM-ID、依存ID、文ID、ファイル・行、原文、操作、CALLの引数順に対応する渡し方を持ちます。
BY CONTENT/VALUEは入力のみ、BY REFERENCE/RETURNINGは応答からPIC変換して反映します。
同一シンボルを複数のBY REFERENCE引数に使う場合は、別名参照の意味を保証できないため未対応スタブにします。
READのレコード／FILE STATUS、SQLの出力ホスト・指示変数／SQLCODE／SQLSTATEは後続分岐前に反映します。
0.3 の応答は Optional です。EOF のレコードは省略して旧値を保持し、FILE STATUS は明示します。
旧必須 constructor では現在と同値の EOF レコードだけ互換として許容します。
null応答、空の状態コード、未定義の結果enumを成功として補いません。

未対応文の既知項目は型付き引数／`CobolReference<T>`／グループ型／固定配列で渡します。
スカラーは `value_f000001.Value = ...` のように編集でき、PIC変換が適用されます。
グループや配列の直接編集には、生成本文と同じ `SourceSupport` の桁合わせを使ってください。
読み書きが不明なブロックや未確定型を含む場合だけプログラム状態全体を渡します。
未対応ブロックは内部の分岐もスタブ実装の責任です。`ControlResult.Continue` はブロック全体の後へ戻り、内部を自動実行しません。
段落dispatcherのある制御スタブは `ControlResult.JumpTo("段落名")` で明示的に移動できます。
NEXT SENTENCEのスタブは `ControlResult.JumpTo(source.NextSentenceTarget!)` を返せます。移動先は生成済み境界に限定し、不明な移動先は失敗します。

## 結果と編集保護

```json
{"display":["one"],"fields":{"CODE":"A","RESULT":"1"},"signal":"goback"}
```

数値は正規化十進文字列、文字は末尾空白込み、表は添字順の平坦な配列です。
DISPLAYは数値PICのゼロ埋め・符号を保持します。signalは `goback` / `exitProgram` / `stopRun` / `fallthrough`。
外部依存を実行した場合は `dependencies` に文・依存ID、操作、状態、結果、`csharp-adapter` の記録が加わります。
これは実接続を検証したという意味ではありません。

manifestは `backend`、`mode`、生成／拒否PROGRAM-ID、管理対象ハッシュ、契約ハッシュ、編集ファイルを保存します。
`analyzed` / `generated` / `built` / `executedWithFixtures` / `matchedBaseline` / `needsReview` は別の判定です。
実行時は `executionKind` が `fixture-input` または `generated-code`、外部実装は `user-csharp-adapter-unverified` として区別します。
fixtureなしでは `executedWithFixtures` はtrueになりません。baselineの出自を認証できないため `matchedBaseline` はfalseを維持します。

`.g.cs`、Runtime、Program.cs、reportsは生成管理対象で、手編集・削除を検知すると再生成／CLI実行を拒否します。
`Stubs/**/*.cs` は利用者の編集領域です。再生成でも上書き・削除しません。不要になった旧スタブも残ります。
`reports/contract-changes.json` で追加・変更・削除された契約を確認し、旧スタブが参照する型がなくなった場合も手動で整理してください。
管理外の他ファイル、シンボリックリンク、パス逸脱は拒否します。成果物は一時領域で揃えて入れ替えます。

編集済みスタブと同じファイル名に、異なる文・呼出し先・引数・原文位置の契約が割り当たる場合は、
再生成を入替え前に拒否します。連番の文IDが同じでも、CALLの並べ替えなどで旧実装を別の処理へ流用しません。
その場合は新しいディレクトリに生成し、`reports/stubs.json` を比較して利用者が実装を移してください。
削除後に残した旧スタブの結び付けも manifest に保持します。旧版の manifest に初期スタブのハッシュがない場合は保守的に編集済みと扱います。
原文位置だけの変更も拒否対象です。意味的に同じかどうかを自動推測しません。

FILLER は状態・初期値を保持しますが、名前付き fixture の対象にはしません。
完全修飾名まで重複する項目は snapshot のキーに `#f000001` のようなシンボルIDを付けて区別します。
曖昧な名前の fixture は拒否し、どちらかの項目を勝手に選びません。

## 0.3 の実行と採用

`run <dir> [--program <id>] --scenario <name> [--call-stubs throw|noop]`、
`adopt <dir> --output <new-dir>` を追加しました。fixture と scenario は同時指定できません。
旧 fixture 引数は維持し、同じ public Executor へ一度だけ typed input に変換して渡します。
既定 Throw と探索 no-op、レビュー schema、生成テスト、所有権・採用条件は [実行ワークフロー](execution-workflow.md) を参照してください。


## 実行準備の追加生成物

source生成時に `reports/execution-readiness.json` / `.md`、
`UserCode/Scenarios/<program>ResponseExamples.cs` を追加します。
`<program>` は既存Identifierによる生成名です。正確な対応は `reports/source-map.json` にあります。
正式LINKAGEはC#シナリオの `Input.Arguments` に設定し、`--scenario sample` で実行します。
従来のJSON fixture `fields` によるLINKAGE設定は移行エラーになります。
入力・応答・期待値が未記入なら、ビルド成功後でも明示的に失敗します。
既存ユーザーファイルと削除は維持し、追加契約はレポートから確認します。
[ARGCALCの補完・実行・採用手順](execution-readiness.md)。

新規生成の `--scenario sample` は編集済みStubsを使用し、`--scenario sample-scripted` は
別の利用者所有ScriptedScenariosからケース専用fakeを注入します。既定テストはsampleです。
旧出力のsampleの意味は保持し、scriptedの追加登録はexecution-readinessレポートの移行手順に従います。
詳細は[実行準備](execution-readiness.md)を参照してください。

通常の source `generate` は分岐探索と固定MSTestも出力します。未レビューの自動期待値は C# の挙動固定として実行でき、adopt にはレビューが必要です。未記入sampleは `Tests/Templates/` に置きます。出力先・有限予算・手動候補・未到達理由・再生成時の保持は[分岐探索とMSTest](branch-test-generation.md)を参照してください。

探索専用workerは最大32試行で回収し、業務状態とfakeを毎試行で作り直します。static状態を共有できない場合はアセンブリを毎回読み直します。timeout・異常終了時はworkerを終了して次試行で再起動します。探索時間はstderrの `Branch performance: {...}` 診断で確認でき、追加フラグは不要です。

---

[ドキュメント案内](README.md)へ戻る。
