# 一時的な大規模COBOL評価タスク

このフォルダーをコーディングエージェントへ指定してください。添付ZIPは不要です。

```text
tasks/deep-corpus/INSTRUCTIONS.mdを読み、fetch.shで入力を準備して、
6,578件の評価・対応可能な不具合修正・回帰テスト追加を実施してください。
サブエージェントの分担、独立期待値、ライセンス確認の規則も守ってください。
```

リポジトリrootで：

```sh
bash tasks/deep-corpus/fetch.sh
```

入力は`.cache/public-cobol-corpus/deep-selected/`、ダウンロードは`.cache/public-cobol-corpus/.downloads/`へ保存します。ネットワークとBash/curl/unzip/gzip/tar/sha256sumが必要です。既に取得済みのアーカイブdirectoryを使う場合は`COBOLCS_CORPUS_ARCHIVES`を設定できます。

本フォルダーには原文を含まない取得台帳・索引・指示だけを配置しています。公開リポジトリに個別ライセンス未確認のコードを一括で追加しません。取得した原文はGit管理外に保ち、コミットする回帰素材は指示書に沿って個別確認してください。

`files.tsv.gz`：origin ID、ZIP内path（GitHubは先頭directory除外）、展開先path、SHA-256。`origins.tsv`：ID、固定URL、先頭directory除外フラグ、archive SHA-256（GitHubはファイル単位検証のため`-`）、キャッシュ名。`metadata.tar.gz`は選定索引と出典資料です。

完了後はこのタスクフォルダーを通常のコミットで削除できます。回帰テストと検証結果は残してください。

検証範囲：取得済みアーカイブによる復元とファイルhashを確認。CobolCSの変換・ビルド・実行テストはこのタスクを受けるエージェントが実施します。
