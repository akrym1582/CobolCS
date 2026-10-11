# Python撤去後の検証記録

2026-10-11 UTC。実装コミットは`a390970`。基準Python版は`0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`、解析器切替までの既存コミットは`a2692f6`。

## 環境

Debian 13 x64、.NET SDK 10.0.401、Git 2.47.3。Dockerの空のイメージへLinux共有ライブラリ、シェル、Git、CA証明書だけを配置し、SDKとNuGetキャッシュをマウントした。ユーザーID 1000で実行。
公式SDKイメージの取得がこの実行環境のネットワークポリシーで拒否されたため、ローカルSDKから構成した隔離環境を使用した。CIは公式`mcr.microsoft.com/dotnet/sdk:10.0`を使用する。

`environment.txt`は`python`／`python3`がPATHになく、`/usr`、`/bin`、`/lib`、SDKにもPython実行ファイルとライブラリがないことを検査した後の記録。
リポジトリと配布物の`.py`／`.pyc`も0件。NuGetの復元・監査、TLS検証は無効化していない。初回は最小環境の`/etc/passwd`の権限不足で復元が待機したため、通常の読み取り権限へ修正した。

## 記録の構成

- `foundation.txt`: ランタイム、189ケース、公開コーパス5テスト・342条件の固定IRハッシュ比較。
- `integration.txt`: 最終167ケースの一括実行。元の108回帰、52組の生成物比較、移行再生成保護、プロセス3件、補助CLI引数3件。
- `final-fixes.txt`: 最終ビルドと修正箇所の4ケース。移植時にテスト値の内容比較・配列反復を補い、シナリオ選択の列挙順依存を除いた。COBOLの期待値は変更していない。
- `corpus-*.json`: 各固定入力の解析・生成・ビルド・実行を分離した測定。原文・IR・生成C#は含めない。
- `corpus-verification.txt`: 全生成物とレビュー済み実行の証跡検証。
- `reports-publish.txt`: 集計ツールと公開CLIのpublish、AMOUNTの生成・ビルド・実行・期待値一致。
- `publish-files.txt`: 空の保存先へpublishした配布物一覧。必要なMITライセンスを含む。
- `historical-recovery.json`: 保存済みの前後測定をC#の`verify-remaining18`で再計算した結果。
- `format-and-historical-comparison.txt`: 上記再計算結果が保存済み`verification.json`と一致し、書式検査も成功した記録。
- `source-files.json`: 測定時の製品ソース57ファイルのSHA-256。
- `validation.json`: 件数、ソース識別子、記録ファイルのハッシュ。

コーパスsummaryの`compilerCommit`は測定開始時のHEAD `a2692f6`で、C#移植・Python撤去の未コミット変更を含んでいた。
実際に測定した製品ソースは全コーパス共通の`compilerSourceSha256 = 3bce76bdbaf8577a4bd5640eb630cf2f71f9743076e2daf5a99de2953d8e5749`と`source-files.json`で固定し、実装コミットの内容との一致を確認した。
最終記録のコミットでは測定データと文書だけを追加する。

## 再現

通常検査は[移行記録](../../../../docs/csharp-migration.md#実行手順)、コーパス取得・測定は[公開コーパス](../../../../docs/public-corpus.md)を参照。
GitHub ActionsではPythonがないSDKコンテナで同じビルド・試験・5コーパスの検証を実行する。
取得と評価の保存先は新規ディレクトリを指定する。元COBOLの実機比較は実施していない。
