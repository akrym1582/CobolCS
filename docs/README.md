# ドキュメント案内

[リポジトリの README](../README.md) の導入手順から進むための索引です。初めて使う場合は「使い始める」から順に読んでください。各ページのコマンドは、特記がなければリポジトリのルートで実行します。

## 使い始める

| 文書 | 内容 |
| --- | --- |
| [生成 C# の実行ガイド](generated-csharp-quickstart.md) | 自分の CBL を生成し、入力・スタブを補完して実行・テストする |
| [CLI 仕様](cli.md) | コマンド、オプション、fixture、終了コード、編集保護を調べる |
| [対応範囲](supported-subset.md) | 実行できる構文、アダプターが必要な処理、未対応の機能を確認する |
| [実行準備](execution-readiness.md) | LINKAGE 入力、CALL 応答、準備レポートとシナリオの値契約を確認する |
| [実行・テスト・採用](execution-workflow.md) | 型付き API、テスト結果のレビュー、adopt の条件を理解する |
| [分岐探索と xUnit](branch-test-generation.md) | 自動生成テストの範囲、未レビュー結果、再生成時の保持を確認する |
| [内部補完・再探索・業務テスト](completion-workflow.md) | 内部スタブの補完後に再探索し、独立した業務期待値で採用後の変更を検証する |
| [開発ガイド](development.md) | コードの構成、変更時の検証方法、開発上の制約を確認する |

まず [README の AMOUNT 例](../README.md#5分で試す)を実行してください。未対応の文で止まった場合は[生成 C# の実行ガイド](generated-csharp-quickstart.md#止まったときの確認先)と[対応範囲](supported-subset.md)を確認します。解析・生成・実行に Python は不要です。

## 検証方法と移行の資料

| 文書 | 内容 |
| --- | --- |
| [公開コーパス検証](public-corpus.md) | 固定版の公開入力の取得、生成・ビルド評価、ライセンス上の扱い |
| [公開コーパスのレビュー](public-corpus-review.md) | コーパスで見つかった問題の再現と回帰 |
| [C# 移行記録](csharp-migration.md) | Python 依存を撤去した根拠、固定期待値、現行の検証手順 |
| [C# 移行計画](csharp-only-implementation-plan.md) | 移行開始時の設計と完了条件。現在の操作手順は移行記録を参照 |

## 実測・調査の記録

以下は各作業時点の結果です。記載されたコミット、入力、環境に対する証跡として読み、現在の使い方は上のガイドを参照してください。過去に実行した Python コマンドは現在の CLI の手順ではありません。

| 文書 | 記録内容 |
| --- | --- |
| [分岐生成の実測](branch-test-generation-results.md) | 合成例の探索・MSTest 再生結果 |
| [内部補完・業務テストの検証](completion-workflow-results.md) | 受入項目、実行したテスト、未取得コーパスと残る制約 |
| [MSTest 再試行とグループ CALL](mstest-retry-group-call-results.md) | 再試行と CALL 応答の受入検査 |
| [探索プロセスの性能](probe-performance.md) | worker 再利用前後の実測 |
| [実行準備の検証](execution-readiness-results.md) | LINKAGE・シナリオ拡張時の検証 |
| [実行ワークフローの検証](execution-workflow-results.md) | 型付き実行と採用手順の検証 |
| [公開コーパス実測](public-corpus-results.md) | 固定公開入力の段階別結果 |
| [残る 18 本の対応](remaining18.md) | 当時の未到達入力と修正結果 |
| [添付サンプルの検証](uploaded-samples.md) | 添付 ZIP の検査範囲と結果 |
| [deep corpus 結果](deep-corpus-results.md) | 深掘り入力の基準・対象範囲・未実施範囲 |
| [deep corpus 修正](deep-corpus-fixes.md) | 発見した問題と個別の検証 |
| [deep corpus のライセンス](deep-corpus-license-review.md) | 再配布できる fixture の審査 |
| [END PROGRAM 調査](deep-corpus-end-program.md) | 境界診断と大小文字の調査 |
| [レビュー対応記録](review-2026-10-11.md) | 2026-10-11 時点の不具合修正と受入 |
| [実行時性能](runtime-performance.md) | 数値計算の変更と測定 |
| [CI の性能と検証範囲](ci-performance.md) | 分割ジョブの構成と確認結果 |

[deep corpus の機械可読な結果](deep-corpus-results.json)も保存しています。

業務テストの factory・個別 xUnit 結果・採取元 SHA-256・prepare 診断の変更と受入は [xUnit 業務テスト4修正の検証記録](xunit-business-four-fixes-results.md) を参照してください。
