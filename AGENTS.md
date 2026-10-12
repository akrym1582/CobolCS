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
dotnet test tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
git diff --check
```

公開コーパスは`docs/public-corpus.md`の手順で固定lockから取得し、`tests/CobolCS.Frontend.CorpusTests`とToolsの`evaluate --build`／`verify`で検証します。未取得のコーパス試験は成功扱いにしません。

アーカイブ取得の回帰テストには `unzip` と `zipinfo` が必要です。ローカルでは両コマンドを用意してください。CIの通常試験ジョブでは不足時だけ `unzip` パッケージを導入します。

## 変更の規則

- 警告をエラーとして扱います。StyleCopの既存5除外以外を増やさず、手書きコードを自動生成扱いにして回避しません。XMLコメントは契約と理由を日本語で説明します。
- COBOLの対応を増やすときは検査、生成、ランタイム、正負の回帰、対応表を一緒に更新します。解析成功・生成・ビルド・fixture実行・元COBOLとの一致を区別してください。
- 管理ファイルの編集・削除、パス逸脱、リンクは拒否し、Stubs・UserCode・Testsの編集領域・Fixturesのユーザーバイトを保持します。再生成やadoptの失敗で既存出力を壊さないでください。
- `tests/migration/baseline.json`、固定期待IR、コーパスlock、参照ハッシュを現在の出力で自動更新しません。期待値変更は独立した根拠を説明し、履歴を残します。
- 出典・固定版・個別ライセンス・必要な通知を確認した外部テストfixtureは`tests/fixtures/third-party`へ保存できます。未確認の外部原文・生成物、大規模コーパス、実接続情報はコミットしません。コーパスは`.cache/public-cobol-corpus`、生成物は`out`に置きます。測定記録にはハッシュ・件数・原文を含まない診断だけを保存します。
- コマンドや構成を変更したら、README、AGENTS、開発・CLI・ワークフロー文書、CI、テスト対応表も更新します。過去の測定記録は上書きせず、新しい結果と区別してください。
- プロセス回帰は親の `MSBUILDDISABLENODEREUSE=0` でも確認します。CLI・Toolsの子プロセスで再利用を無効化し、残存MSBuildノードによる出力パイプの待機を防ぐ契約を環境設定で隠さないでください。

CLIの基本形は `cobolcs <file.cbl|directory> [-o <dir>] [-I <copy-dir>]`。詳細は[CLI仕様](docs/cli.md)を参照してください。CIはPR時のみUbuntuランナー上の.NET 10 SDKで実行し、1回のビルド成果物を通常試験と5コーパスの並列ジョブで共有します。NuGetキャッシュとPythonソースなしの検査は共通Actionで管理します。

通常 source generate の分岐探索は stage 内の別プロセスで行い、合成応答を固定再生して xUnit を検証します。詳細は `docs/branch-test-generation.md`。`BranchGenerationTests` は保存テスト・未レビュー拒否・TRX衝突・採用後の計算破壊を検証します。従来の手動ケース回帰は `ManualTemplateSupport` で参考テンプレートを明示的に有効化します。NoOp／timeout／不完全traceを正常期待値へ昇格させず、固定IRや独立期待値を観測出力で更新しません。

探索workerは最大32試行で回収し、timeout・異常出力時もプロセスツリーを終了します。生成コードのstatic状態を検査し、共有不可なら毎試行でcollectible AssemblyLoadContextを作り直します。業務状態・fakeは毎試行で新規作成します。`ProbeSessionTests` は静的状態、両出力上限、異常終了後の復旧、孫プロセスの終了を検証します。時間計測はstderr診断へ出し、決定的な管理レポートへ保存しません。

CIの統合試験は `branch`・`generation`・`regression-a/b`・`workflows-a/b` の6ジョブに分割し、各45分を上限とします。クラス名の完全な接頭辞と相補的な名前条件をフィルタに使い、全テストを重複なく実行します。Runtime・Frontendは `branch` で1回実行し、各ジョブのTRXをartifactへ保存します。

公開コーパスはcarddemoとbankを4分割し、残り3種と合わせ11ジョブで評価します。固定lockは変更せず、元の順のindex moduloで分割します。`merge-evaluations` は全分割・元のケースID/hash・コンパイラー条件を照合し、元の生成下限と独立期待値を全体で検証します。部分summaryを全体の成功として扱わず、試行予算も減らしません。

未完了の分岐探索は次の通常generateで同一runIdのまま1回再試行し、前回レポートを同じrun配下の `attempts/` へ管理履歴として保持します。既存ケースのID・編集・削除・レビュー状態・元の来歴hashを保持してください。固定グループCALLの応答候補はBY REFERENCE領域の既知基本項目へ展開し、候補・許可リスト・合成値で同じ集合を使います。

内部補完・再探索・業務テストは `docs/completion-workflow.md`。`CompletionWorkflowTests` と `BusinessComparisonTests` は CI workflows-b で実行します。prepare はコードを実行せず、explore は固定 stage と編集競合検査を使います。UserCode/Completion と UserCode/Business、Tests/BusinessCases と Tests/BusinessContracts は利用者所有です。実行記録は reports/executions に分離し、必須業務 ID は Testing/BusinessHistory.json で保持します。独立手計算と C# characterization の件数を分けて報告してください。
