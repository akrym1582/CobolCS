# CobolCS

CobolCS は、COBOL の対応範囲を解析し、**単独でビルド・実行できる C# / .NET 10 プロジェクト**を生成するコマンドラインツールです。生成物には C# の処理本体、データ型、起動用プログラム、診断レポートが含まれます。

**Python は不要です。** 解析器、CLI、生成器、ランタイム、回帰テストは C# で動きます。古い手順にある `COBOLCS_PYTHON` も現在は使いません。初回の .NET ビルド時は、開発用の StyleCop と生成テスト用の MSTest などを NuGet から復元するため、ネットワーク接続が必要です。

目的別の文書一覧は[ドキュメント案内](docs/README.md)にあります。

まずは同梱の `AMOUNT` を動かし、結果を確認してください。以下のコマンドは**リポジトリのルート（`CobolCS.sln` がある場所）**で実行します。Linux のシェルを例にしています。

## 必要なもの

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)。ランタイムだけではビルドできないため、SDK を導入してください。
- リポジトリの取得に Git。Python、Node.js、Docker、API キー、DB は不要です。
- 初回ビルド時に NuGet へ接続できる環境。復元済みなら通常の解析・生成・実行にネットワークは不要です。

インストール後、端末で `dotnet --version` を実行し、`10.` から始まるバージョンが表示されることを確認します。まだ取得していない場合は次のようにリポジトリを取得します。

```sh
git clone https://github.com/akrym1582/CobolCS.git
cd CobolCS
dotnet --version
```

既に取得済みなら、`git status` で未保存の変更を確認してから `git pull --ff-only origin main` で最新の `main` を取り込んでください。以下の手順は現在の `main` の CLI に対応しています。

## 5分で試す

1. ソリューションをビルドします。`-m:1` は同時ビルド数を抑える指定です。

   ```sh
   dotnet build CobolCS.sln -m:1
   ```

2. サンプルの COBOL を C# に変換します。`-o` は生成先です。省略すると入力名に `-csharp` を付けたディレクトリがカレントディレクトリに作られます。

   ```sh
   dotnet run --project src/CobolCS.Cli --no-build -- examples/AMOUNT/AMOUNT.cbl -o out/amount
   ```

   `--` より後が CobolCS への引数です。生成された `out/amount/` には `CobolCS.Generated.csproj`、`Programs/`、`Models/`、`reports/` などが入ります。スタブが必要な入力では `Stubs/` も作られます。既定は `source` backend の `scaffold` モードです。未対応の文は診断と編集可能なスタブになり、その文に到達すると明示的に停止します。

3. 生成プロジェクトをビルドして実行します。`fixture.json` はサンプルの入力、`runs/amount.json` は実行結果の保存先です。

   ```sh
   dotnet run --project src/CobolCS.Cli --no-build -- build out/amount
   dotnet run --project src/CobolCS.Cli --no-build -- run out/amount --fixture examples/AMOUNT/fixture.json -o runs/amount.json
   ```

   `AMOUNT` は 1 プログラムだけなので `--program` を省略できます。正常終了すると、`display`、最終的な `fields`、終了状態 `signal` を含む JSON が表示されます。`display` には `"overflow"` と `"+00168:9:3:1"` が入ります。

4. 同梱の期待値と比較します。

   ```sh
   dotnet run --project src/CobolCS.Cli --no-build -- compare runs/amount.json examples/AMOUNT/expected.json
   ```

   一致すれば `matchedBaseline: true` と表示され、終了コードは 0 です。この `expected.json` は合成サンプルの期待値であり、元 COBOL の実機結果ではありません。

## 自分の COBOL を変換する

まず 1 本の `.cbl` / `.cob` ファイルを指定します。ディレクトリを渡すと、その中の対象ファイルをまとめて解析します。`PROGRAM-ID` はファイル名と異なる場合があるため、実行時は生成先の `manifest.json` の `programs` か生成先 README を確認してください。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- path/to/sample.cbl -o out/sample
dotnet run --project src/CobolCS.Cli --no-build -- build out/sample
dotnet run --project src/CobolCS.Cli --no-build -- run out/sample --program SAMPLE
```

`SAMPLE` は実際の `PROGRAM-ID` に置き換えます。プログラムが 1 本だけなら `--program` は省略できます。COPY を使う場合は、生成コマンドに `-I path/to/copybooks` を追加してください。複数の検索先は `-I` を繰り返します。固定形式のソースには、例えば `{"sourceFormat":"fixed"}` を保存した設定 JSON を `--config path/to/config.json` で渡します。設定を省略した場合は free format です。

生成先の `reports/diagnostics.md` と `reports/diagnostics.json` に未対応箇所や原文位置が出ます。外部 CALL・ファイル・SQL などの処理を含む場合は、`Stubs/` の該当箇所と入力を補完してから実行してください。起動用の `Program.cs` や `*.g.cs` は生成管理中のファイルです。どこを編集するか、`--scenario sample` でどう動かすかは[生成 C# の実行ガイド](docs/generated-csharp-quickstart.md)に手順があります。

### 主なコマンド

| コマンド | 用途 |
| --- | --- |
| `<input>` または `generate <input>` | C# プロジェクトを生成。`-o` で出力先を指定 |
| `analyze <input>` | C# を生成せず、解析結果と診断を保存 |
| `build <生成先>` | 生成ファイルを検査してからビルド |
| `run <生成先>` | ビルドして実行。`--fixture`、`--scenario`、`--program` を指定可能 |
| `compare <実行結果.json> <期待値.json>` | JSON を比較。キー順は無視し、値と配列順は厳密に比較 |
| `prepare <生成先>` | コードを実行せず、入力・内部処理・外部応答の作業表を更新 |
| `explore <生成先>` | 補完した内部 C# を使い、後続の分岐を再探索して固定テストを生成 |
| `adopt <生成先> -o <採用先>` | レビュー済みの生成物を独立した C# プロジェクトへ採用 |

全オプションは `dotnet run --project src/CobolCS.Cli --no-build -- --help` と [CLI 仕様](docs/cli.md)で確認できます。旧版の `translate` / `check` / `--out` は、それぞれ `generate` / `build` / `-o`（または `--output`）に変更されました。

## うまく進まないとき

| 状況 | 確認すること |
| --- | --- |
| `dotnet` が見つからない、SDK の版が違う | .NET **SDK** 10 のインストールと `dotnet --version` |
| 初回ビルドで NuGet の復元に失敗 | ネットワーク接続と NuGet への到達性。復元が終わってから再実行 |
| 生成はできたが実行時にスタブで停止 | `reports/diagnostics.md`、`reports/stubs.json` と [実行ガイド](docs/generated-csharp-quickstart.md) |
| `Unknown PROGRAM-ID` | ファイル名ではなく COBOL の `PROGRAM-ID` を指定しているか |
| 再生成が管理ファイルの変更を拒否 | `*.g.cs` などを手編集していないか。編集する場所は `Stubs/` と `UserCode/` |
| 期待値と一致しない | 実行結果の JSON と期待値を比較し、入力 fixture と対象プログラムを確認 |

## 対応範囲と開発

このツールは COBOL の全機能や実機の文字コード・ファイル接続を再現するものではありません。[対応範囲](docs/supported-subset.md)に、実行できる構文、外部アダプターが必要な処理、未対応箇所を列挙しています。生成・ビルド・サンプル実行の成功だけで業務上の同値性は確認できません。独立した期待結果で検証してください。

生成した C# の編集、入力シナリオ、MSTest、`adopt` の流れは[生成 C# の実行ガイド](docs/generated-csharp-quickstart.md)と[実行・テスト・採用ワークフロー](docs/execution-workflow.md)を参照してください。通常の source 生成では、分岐探索から C# の挙動を固定するテストも作成します。テストの結果を業務仕様として採用する前にレビューが必要です。詳しくは[分岐探索と MSTest](docs/branch-test-generation.md)にあります。

未対応の内部処理を補完するときは、`prepare` の作業表で編集先を確認し、`explore` で補完後の分岐を再探索します。外部 CALL・FILE・SQL は固定した応答を使います。業務上の期待値は自動生成テストとは別に JSON 契約とケースへ記入します。手順と制約は[内部補完・再探索・業務テスト](docs/completion-workflow.md)を参照してください。

開発と検証のコマンドは[開発ガイド](docs/development.md)、採用元の SHA とライセンスは[第三者通知](THIRD-PARTY-NOTICES.md)にまとめています。MIT License。
