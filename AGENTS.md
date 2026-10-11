# CobolCSの作業ガイド

最初にREADME、`docs/development.md`、`docs/supported-subset.md`、`docs/cli.md`を確認してください。C#移行の基準と検証根拠は`docs/csharp-migration.md`にあります。

## 構成と実行

- .NET SDK 10を使用します。製品、回帰テスト、補助ツールにPythonは不要です。公開コーパス取得だけGitとネットワークを使用します。
- `src/CobolCS.Frontend`が解析API、`src/CobolCS.Cli`が検査・生成・再生成・adopt、`src/CobolCS.Runtime`が独立ランタイムです。
- 開発ツールは`dotnet run --project tools/CobolCS.Tools --no-build -- --help`で確認できます。
- 検証対象はLinuxです。Windows検証やWindows CIを追加する必要はありません。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
git diff --check
```

公開コーパスは`docs/public-corpus.md`の手順で固定lockから取得し、`tests/CobolCS.Frontend.CorpusTests`とToolsの`evaluate --build`／`verify`で検証します。未取得のコーパス試験は成功扱いにしません。

## 変更の規則

- 警告をエラーとして扱います。StyleCopの既存5除外以外を増やさず、手書きコードを自動生成扱いにして回避しません。XMLコメントは契約と理由を日本語で説明します。
- COBOLの対応を増やすときは検査、生成、ランタイム、正負の回帰、対応表を一緒に更新します。解析成功・生成・ビルド・fixture実行・元COBOLとの一致を区別してください。
- 管理ファイルの編集・削除、パス逸脱、リンクは拒否し、Stubs・UserCode・Testsの編集領域・Fixturesのユーザーバイトを保持します。再生成やadoptの失敗で既存出力を壊さないでください。
- `tests/migration/baseline.json`、固定期待IR、コーパスlock、参照ハッシュを現在の出力で自動更新しません。期待値変更は独立した根拠を説明し、履歴を残します。
- 外部COBOL原文、生成物、実接続情報をコミットしません。コーパスは`.cache/public-cobol-corpus`、生成物は`out`に置きます。測定記録にはハッシュ・件数・原文を含まない診断だけを保存します。
- コマンドや構成を変更したら、README、AGENTS、開発・CLI・ワークフロー文書、CI、テスト対応表も更新します。過去の測定記録は上書きせず、新しい結果と区別してください。
