# Native frontend validation (Linux)

参照基準は `0002ea5bea5d14f49ceb0fd7a1934855249cfb9f`。元の期待IRや基準測定は変更していない。
.NET SDK 10.0.401、Python参照3.12.14、Debian 13 x86_64で実行。

- `frontend.log`: 231件成功、失敗0、skip 0。PythonReferenceは60件。
- `integration.log`: 全example × source/interpreter × strict/scaffoldの52組と、旧Python出力の再生成保護1件。
- `ir-corpus.log`: 5コーパス171入力、strict/scaffold両モードの342回の完全IR比較。
- `build.log`、`runtime.log`: ビルド警告0・エラー0、ランタイム12 assertions。
- `regression.log`、`test_*.log`: 既存8スイート108件。テストドライバーはPythonのまま。`test_remaining18`は固定参照実装を直接検査し、同じ51入力のC#版はFrontend検査に含む。
- `directory-cli.log`: ディレクトリリンクの列挙修正後、既存CLIの入力列挙・配布に関する2件を追加確認。
- `publish.log`、`native-smoke.log`: 空の配布先にpublish、Pythonファイルがないことを確認。PATHにはdotnetへのリンクだけを置き、`COBOLCS_PYTHON=/nonexistent/python`で全製品経路を実行。OSからPythonをアンインストールした検査ではない。
- `corpus-*.json`、`corpus-*.log`: 同じlockでC#製品CLIを使ったevaluate --build / verifyの結果。生成・ビルド171件、独立合成期待値との実行一致3件。原COBOLとの比較は0件。

再現コマンドは `docs/csharp-migration.md` を参照。コーパス出力は `out/migration-native-final/CORPUS`。
リンクを含む入力の修正はコーパス測定開始後に追加した。コーパスは全件ファイルを明示指定しているため、この列挙経路を通らない。
測定時のソースハッシュは各summary内、最終ソース全体は `source-sha256.json` に保存する。
最終のリンク列挙は追加IR比較とCLI回帰、最終publish検査で確認した。

許可差分はIRのルート `/frontendVersion` の `0.2.2` → `0.3.0-csharp` と、生成台帳の対応するIRハッシュのみ。
全アーティファクトの実ファイルハッシュを確認してから台帳を比較する。診断・原文位置・ID・設定ハッシュは正規化しない。

PythonなしPATHでのadopt検査は、AMOUNTの既存独立期待値を生成テストに記入して行った。
(1.25 + 2.10) / 2をROUNDEDで1.68、SMALLの桁あふれでは9を保持、7 / 2の商3・余り1、表示は `overflow` と `+00168:9:3:1`、終了Goback、依存呼出し0を検査する。
レビュー台帳には入力ハッシュ・ツールバージョン・レビュー理由・ケース関連ファイルのハッシュを記入し、adoptが生成テストを実行する。
未レビュー版は終了コード1で拒否されることも検査した。
