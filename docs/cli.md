# CLI の契約

配布版は `cobolcs`、リポジトリからは `dotnet run --project src/CobolCS.Cli --` に続けて引数を指定します。
ビルド済み DLL は `dotnet src/CobolCS.Cli/bin/Debug/net10.0/cobolcs.dll` でも実行できます。

```sh
cobolcs sample.cbl
cobolcs sample.cbl -o generated/sample
cobolcs generate sources/ -o generated/all -I copybooks/ -I shared-copybooks/
cobolcs sample.cbl --scaffold
cobolcs analyze sample.cbl
cobolcs build sample-csharp
cobolcs run sample-csharp --fixture fixture.json -o actual.json
cobolcs compare actual.json expected.json
```

| コマンド | 役割 |
|---|---|
| `<input> [options]` | C# 生成。`generate` を省略できる |
| `generate <input> [options]` | 対応範囲を検査し、独立した .NET 10 プロジェクトを生成 |
| `analyze <input> [options]` | IR と診断を保存。C# は生成しない |
| `build <dir>` | 生成ファイルのハッシュ検査、dotnet build、built 判定の更新 |
| `run <dir> [--program <id>] [--fixture <json>] [-o <json>]` | ハッシュ検査とビルドの後、生成プログラムを実行。単一プログラムなら ID の指定不要 |
| `compare <actual.json> <baseline.json>` | JSON の全体一致を比較。オブジェクトのキー順は無視、配列順・値は厳密比較 |

input は COBOL ファイルまたはディレクトリです。生成先の既定値はカレントディレクトリの
`<入力名>-csharp/`、解析先は `<入力名>-analysis/` です。
入力名はファイルでは拡張子を除いた名前、ディレクトリでは末尾のディレクトリ名です。
`-o <dir>` / `--output <dir>` で変更できます（run では結果 JSON のファイルパス）。
生成では `Programs/*.cs` にプログラム別の C#、`Runtime/` にランタイム、
`CobolCS.Generated.csproj` にビルド用プロジェクトを保存します。

generate / analyze は `--config <json>` と、繰り返し指定できる
`-I <dir>` / `--copybook-path <dir>` を受け付けます。
生成は既定で strict（未対応箇所があればレポートのみ）。`--scaffold` でレビュー用の雛形を生成できます。
analyze に `--scaffold` は指定できません。
`cobolcs --help` または `cobolcs <command> --help` で使い方、`cobolcs --version` で版を表示します。
`--` 以降はオプションではなく入力パスとして扱います。コマンド名と同じ入力名は `./run` のように指定します。
値が `-` で始まるパスは `./-name` または絶対パスで指定してください。
重複オプション（COPY 検索パスを除く）や未知のオプションは拒否します。

旧 `translate` は `generate`（または省略）、`check` は `build`、`--out` は `-o` / `--output`、
`--mode scaffold` は `--scaffold` に変更しました。compare の入力は位置引数になりました。
旧構文との互換性はありません。

終了コードは 0 が成功、1 が strict 拒否または比較不一致、2 が引数・I/O・解析起動・ビルド・実行エラーです。
analyze は診断を調査するコマンドなので、診断に error があっても出力成功時は 0 を返します。
レポートの `severity` も必ず確認してください。

設定は frontend の契約を採用します。最小設定の例：

```json
{"sourceFormat":"fixed","maxSteps":1000000,"timeoutMs":10000}
```

未指定項目は portable-test の既定値を使用します。未知の設定項目は拒否します。
fujitsu-review とホスト依存格納は C# backend では実行対応していません。

fixture は省略可能です。初期値は COBOL の VALUE を使用します。

```json
{"fields":{"TOTAL":"12.34","CODE":"A"}}
```

値は文字列として指定し、項目名または修飾名でスカラー項目を識別します。
不明・曖昧な項目、表の fixture 初期化、未対応の外部接続設定は拒否します。
互換用に空の `calls: []` / `files: {}` は受け付けます。実データ入りの外部 fixture は未対応です。

実行結果の例：

```json
{"display":["one"],"fields":{"CODE":"A","RESULT":"1"},"signal":"goback"}
```

fields の数値は小数点付きの正規化十進文字列、文字列は末尾の空白を含みます。
表は宣言の添字順を平坦化した配列です。DISPLAY は数値 PIC のゼロ埋めと符号を保持します。
signal は `goback` / `exitProgram` / `stopRun` / `fallthrough` です。

`manifest.json` は入力・設定・採用元・生成ファイルの SHA-256 と工程別判定を保存します。
判定はプロジェクト全体の直近の操作を示し、全 PROGRAM-ID の実行網羅率ではありません。
fixture なしの run は `executedWithFixtures` を true にしません。
元 COBOL baseline の由来を自動で認証できないため、台帳の `matchedBaseline` は false を保持します。
compare の一致は、利用者が渡した JSON との一致だけを意味します。

生成ファイルの手編集、生成済みファイルの削除、管理外ファイル、シンボリックリンク、台帳のパス逸脱がある生成先は再生成を拒否します。
reports とソースを一時ディレクトリに揃えてから入れ替えます。アダプタ編集領域はまだありません。
