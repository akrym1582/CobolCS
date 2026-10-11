# END PROGRAM の境界診断と大小文字の調査

2026-10-11。固定 `tests/deep-corpus/selected.lock.json` と既存の `deep-available` 入力だけを使用した。外部原文・lockを編集していない。

`out/deep-before` の `Invalid END PROGRAM or multiple programs in one source` 診断12件を原文と解析器から分類した。この12件に名前の大小文字差だけの例はなかった。

| 分類 | 件数 | caseId |
|---|---:|---|
| 入れ子プログラムの構造を持つ正常例 | 5 | `deep-0c4193233e345dce`, `deep-aed5a7c802ed9114`, `deep-3aecd0f53d79a776`, `deep-e5dd0eae5f0a54f5`, `deep-ff25ae5e703112db` |
| 入れ子構造に負例・不完全文も含む | 4 | `deep-0666cd39b8f72162`, `deep-6927b705c44d2ba4`, `deep-76515cb3fb228669`, `deep-bd36bf00ad3c1f9c` |
| 独立プログラムの連結と負例を含む | 1 | `deep-a609942dc570d8b0` |
| 二重終止符・欠落空白等の負例を含む | 2 | `deep-a124e8b8b25ee2ee`, `deep-e597744364037e71` |

正常例という分類は入れ子構造についてのもので、他の全構文・ホスト環境への対応や元COBOLとの実行一致を示さない。負例の判断には元資料のエラー注記と不完全文を確認した。

現行の `FrontendAnalyzer` はファイルごとに1つのParserを作る。`Parser.procedure.cs` はEND名不一致と後続トークンを同じ診断で拒否する。後続トークンの検査を除くと別プログラムを黙って失うため、この検査は維持する。入れ子対応にはGLOBAL/COMMON、名前解決、呼出し寿命の設計が必要で、この変更では追加しない。

## 独立して修正する大小文字の問題

既存lock全件の利用可能な入力から、非引用PROGRAM-IDとEND名が大小文字だけ異なる2例を発見した。

| caseId | 入力SHA-256 |
|---|---|
| `deep-f339e5d2c745097a` | `9ad07d16e083568d0984d3d764ef6fde5df671738c4cf326eb94978b06b1c151` |
| `deep-33ac374f1241faa1` | `1f494907b0007d2ccbd6751022282fc57737ab38aca4e41b5f780b6f888d543d` |

非引用名だけをOrdinalIgnoreCaseで照合し、宣言IDの綴りは保持する。引用宣言名の既存の大小文字区別、引用宣言と同じ綴りの非引用END名の互換、名前不一致、後続の別プログラムの拒否を維持する。

修正前には独立した最小合成例がprogram数0と上記PARSE_ERRORになった。回帰は名前照合の正負に加え、対応する単一プログラムをstrict生成し、ビルド・表示結果まで検証する。

## 実行した検証

- `dotnet build tests/CobolCS.IntegrationTests --no-restore -m:1`: 警告・エラー0。
- `MSBUILDDISABLENODEREUSE=0 dotnet test tests/CobolCS.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~EndProgramNameTests`: 10件成功。正常な大小文字差、名前不一致、引用名互換、後続の第2プログラム拒否、strict生成物の表示・終了を確認した。
- 上記の実入力2件を同じlock設定で修正前CLIと修正後CLIから生成した。両者ともprogram数0→1、生成projectなし→ありになった。修正後の2プロジェクトの `dotnet build -m:1` はいずれも警告・エラー0。

`rejectedTokens` は順に241→0、227→0となった。これは全ファイルの解析棄却を回避した結果である。JSON文等の未対応診断と外部実装が必要なスタブは残り、native対応・業務実行成功・元COBOLとの一致を示さない。実コーパス生成物は実行していない。

すべて既存NuGetキャッシュを使用し、packageSourcesを空にした一時設定を子プロセスへ継承した。監査・警告設定は変更していない。VSTestのみローカルプロセス間通信のためsandbox外実行を使用した。外部入力取得・外部接続は行っていない。
