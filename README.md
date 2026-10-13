# CobolCS

CobolCS は、**C# へのマイグレーションに向けて、COBOL の振る舞いを固定するテスト資産を生成する**コマンドラインツールです。入力・外部応答・期待値・xUnit テストを整え、移行とその後の変更で振る舞いを検証できるようにすることを主目的としています。

COBOL から C# / .NET 10 の処理本体やデータ型、起動用プログラムを生成する機能は、そのための実行・観測基盤です。生成した C# を最終的な設計とせず、テスト資産で振る舞いを確認しながら、移行先をよりモダンなアーキテクチャへリファクタリングしていくことを想定しています。

自動探索で得た期待値は、まず生成 C# の振る舞いを固定する characterization テストです。元 COBOL の振る舞いを固定する根拠には、元 COBOL の実行結果や独立した業務期待値との照合・レビューが必要です。

**Python は不要です。** 解析器、CLI、生成器、ランタイム、回帰テストは C# で動きます。古い手順にある `COBOLCS_PYTHON` も現在は使いません。初回の .NET ビルド時は、開発用の StyleCop と生成テスト用の xUnit などを NuGet から復元するため、ネットワーク接続が必要です。

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

2. サンプルの COBOL から、振る舞いを観測する C# と固定テストを生成します。`-o` は生成先です。省略すると入力名に `-csharp` を付けたディレクトリがカレントディレクトリに作られます。

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

5. 自動生成された固定テストを実行します。通常の source 生成では、分岐探索で得た入力・外部応答・観測期待値を xUnit テストとして保存します。

   ```sh
   dotnet test out/amount/Tests/CobolCS.Generated.Tests.csproj
   ```

   このテストの成功は生成 C# の固定再生を確認するものです。自分の COBOL の移行では、元 COBOL の実行結果や独立した業務期待値で照合・レビューし、`adopt` 後もテストを使ってリファクタリングを検証します。

## 自分の COBOL からテスト資産を作る

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
| `<input>` または `generate <input>` | 振る舞いの観測用 C# プロジェクトを生成。通常の source 生成では固定テストも作成。`-o` で出力先を指定 |
| `analyze <input>` | C# を生成せず、解析結果と診断を保存 |
| `build <生成先>` | 生成ファイルを検査してからビルド |
| `run <生成先>` | ビルドして実行。`--fixture`、`--scenario`、`--program` を指定可能 |
| `compare <実行結果.json> <期待値.json>` | JSON を比較。キー順は無視し、値と配列順は厳密に比較 |
| `prepare <生成先>` | コードを実行せず、入力・内部処理・外部応答の作業表を更新 |
| `explore <生成先>` | 補完した内部 C# を使い、後続の分岐を再探索して固定テストを生成 |
| `adopt <生成先> -o <採用先>` | レビュー済みの生成物とテスト資産を独立した C# プロジェクトへ採用し、リファクタリングへ進む |

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

生成した C# の編集、入力シナリオ、xUnit、`adopt` の流れは[生成 C# の実行ガイド](docs/generated-csharp-quickstart.md)と[実行・テスト・採用ワークフロー](docs/execution-workflow.md)を参照してください。通常の source 生成では、分岐探索から C# の挙動を固定するテストも作成します。テストの結果を業務仕様として採用する前にレビューが必要です。詳しくは[分岐探索と xUnit](docs/branch-test-generation.md)にあります。

未対応の内部処理を補完するときは、`prepare` の作業表で編集先を確認し、`explore` で補完後の分岐を再探索します。外部 CALL・FILE・SQL は固定した応答を使います。業務上の期待値は自動生成テストとは別に JSON 契約とケースへ記入します。業務ケースは個別 xUnit 行と新規アダプター factory で実行し、採取データは実 SHA-256 を照合します。prepare は未完成ケースの診断も作業表へ保存します。手順と制約は[内部補完・再探索・業務テスト](docs/completion-workflow.md)を参照してください。

開発と検証のコマンドは[開発ガイド](docs/development.md)、採用元の SHA とライセンスは[第三者通知](THIRD-PARTY-NOTICES.md)にまとめています。MIT License。

PRのCIはUbuntuランナー上で.NET 10 SDKを用意して実行します。SDKコンテナを各ジョブで取得せず、ビルド成果物を通常試験と公開コーパス試験で共有します。[CIの分割と検証範囲](docs/ci-performance.md)に詳細があります。

業務境界を明示選択する `scaffold-business` と、固定 ASCII の限定 INSPECT／STRING 値区切りに対応しました。[合成例と制約](docs/business-test-assistance.md)、[固定範囲の測定・検証](docs/business-test-assistance-results.md)。

業務ケース記入支援は入力・結果・操作別応答の未完成構造とguideを生成します。ケース別ACCEPTには `acceptMode: "case-sequence"` を指定できます。prepareで現在のケース別レビュー参考行を更新し、filesと採用時のimplementationHashesを分離します。手順は[業務テスト支援](docs/business-test-assistance.md)。

日本語ソースは `sourceEncoding` にUTF-8/CP932/Shift_JISを明示指定できます。ランタイムのPIC格納とは区別します。[日本語コーパスの解析器修正と検証範囲](docs/japanese-corpus-parser-results.md)。
