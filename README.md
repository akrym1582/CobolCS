# CobolCS

COBOL の定義済み部分集合を、独立してビルド・実行できる **C# / .NET 10** プロジェクトへ変換する CLI です。
[CobolTS](https://github.com/akrym1582/CobolTs) の位置情報付き解析器と、
[PythonBol-Translator](https://github.com/IronAdamant/PythonBol-Translator) の PIC 解析を参考・採用しています。
CLI、生成器、制御フロー、数値・文字列ランタイムは C# で実装しています。
開発時のビルドには StyleCop Analyzers を適用し、XML コメントを含む警告をエラーとして検査します。

解析時だけ Python 3.11 以上を使います。**生成プログラムのビルド・実行には .NET だけで十分です。**
LLM、API キー、Node、Docker、DB、NuGet の実行時依存はありません。
初回ビルドでは開発用の StyleCop Analyzers を NuGet から復元します。生成先の比較テストでは MSTest と Test SDK も初回復元します。復元後の解析・生成・実行にネットワークは不要です。
これは初期版であり、参照元の全機能を移植したものではありません。
対応範囲は [docs/supported-subset.md](docs/supported-subset.md) に明記しています。

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/AMOUNT --out out/amount
dotnet run --project src/CobolCS.Cli --no-build -- check out/amount
dotnet run --project src/CobolCS.Cli --no-build -- run out/amount --program AMOUNT --fixture examples/AMOUNT/fixture.json --out runs/amount.json
dotnet run --project src/CobolCS.Cli --no-build -- compare --actual runs/amount.json --baseline examples/AMOUNT/expected.json
```

AMOUNT は丸め・桁あふれ・商と余りを確認し、`overflow` と `+00168:9:3:1` を出力します。
`expected.json` は合成試験の期待値です。元 COBOL の実機結果ではありません。
ビルドや fixture 実行の成功は業務同値性・富士通 COBOL 互換性の証明にはなりません。

生成先には C# のプログラム別命令列、ランタイム、C# 内に固定した宣言・設定、解析レポート、ハッシュ台帳を出力します。
PERFORM / GO TO はプログラムカウンタと戻り先スタックで実行し、直接 GO TO を C# の再帰呼出しにしません。
数値は `BigInteger` による有理数で計算し、PIC への代入で切り捨て・ROUNDED を適用します。

```sh
# 生成先を別の端末へ移しても、元リポジトリや Python は不要
dotnet build out/amount/CobolCS.Generated.csproj
dotnet out/amount/bin/Debug/net10.0/CobolCS.Generated.dll AMOUNT examples/AMOUNT/fixture.json

# COPY 展開・位置情報・診断を調べる
dotnet run --project src/CobolCS.Cli --no-build -- analyze examples/COPYJP --copybook-path examples/COPYJP/copy --out out/analysis

# 未対応箇所を報告する、ビルド可能な雛形
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/UNSUPPORTED --mode scaffold --out out/review
```

`strict` は未対応箇所があるとレポートだけを出力し、終了コード 1 を返します。
`scaffold` は C# プロジェクトを出力しますが、エラー診断があれば実行時に明示的に失敗します。
SQL・CALL・ファイル I/O を成功したように扱う空実装はありません。既存設定の `externalAdapter: "fixed"` では、明示した固定依頼・応答を順番と値まで照合して実行できます。
欠落 COPY は scaffold の解析で未確定シンボルとして残し、実行できたことにはしません。

再生成では前回の生成ファイルのハッシュを確認します。手編集・削除・管理外ファイルがあれば拒否し、内容を保護します。
`analyzed`、`generated`、`built`、`executedWithFixtures`、`matchedBaseline`、`needsReview` を区別します。
`compare` は与えた JSON 同士を比較する独立コマンドで、台帳の `matchedBaseline` を変更しません。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
python3 tests/regression.py
python3 tests/quality_regression.py
python3 tests/quality_completion_regression.py
```

Windows では必要に応じて `python3` を `python` に読み替えてください。
解析用の実行ファイルは `COBOLCS_PYTHON` 環境変数で指定できます。
GitHub Actions は Ubuntu のみを対象とします。ローカル検証は Linux、.NET SDK 10.0.401 で実施しました。

CLI と fixture の契約は [docs/cli.md](docs/cli.md)、構成・拡張方法は [docs/development.md](docs/development.md)、
採用元 SHA とライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

MIT License。

独立期待値の取り込み・変換台帳・生成 MSTest の手順と制約は [docs/translation-quality.md](docs/translation-quality.md) を参照してください。通常の `translate` が `Tests/ReferenceCases/` を読み込み、生成時レポートと実行時の差分を分離します。元 COBOL の実機比較は未実施です。

業務観測はレビュー済み `Tests/Contracts/` の契約と型付き `GeneratedApi.RunBusiness` を使います。採用は既存 `--config` の `quality.adopt` に採用先・TRX・レビューを指定します。外部サービスの実接続と実機互換を確認済みとするものではありません。
