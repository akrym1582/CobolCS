# CobolCS

COBOL の定義済み部分集合を、独立してビルド・実行できる **C# / .NET 10** プロジェクトへ変換する CLI です。
[CobolTS](https://github.com/akrym1582/CobolTs) の位置情報付き解析器と、
[PythonBol-Translator](https://github.com/IronAdamant/PythonBol-Translator) の PIC 解析を参考・採用しています。
CLI、生成器、制御フロー、数値・文字列ランタイムは C# で実装しています。
開発時のビルドには StyleCop Analyzers を適用し、XML コメントを含む警告をエラーとして検査します。

解析時だけ Python 3.11 以上を使います。**生成プログラムのビルド・実行には .NET だけで十分です。**
LLM、API キー、Node、Docker、DB、NuGet の実行時依存はありません。
初回ビルドでは開発用の StyleCop Analyzers を NuGet から復元します。ビルド時はNuGetの脆弱性監査の更新にも接続が必要になる場合があります。解析・生成・通常の生成プログラムの実行にネットワークは不要です。
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

既定の `source` backend は、編集して業務処理を読める **通常の C# 本文・型付き状態・C# スタブ**を生成します。
MOVE・算術は型付き代入と精密数値式、IF は `if`、inline PERFORM は `for` / `while` です。
段落落下・PERFORM THRU・GO TO がある場合は、直接生成した本文を enum と `switch` / `while` で接続します。
生成したプログラムは IR を読みません。`reports/ir.json` は解析資料として削除しても単独ビルドできます。
数値は `BigInteger` による有理数で計算し、PIC への代入で切り捨て・ROUNDED を適用します。

```text
Programs/*Program.g.cs       通常の C# 業務本文
Models/*State.g.cs           名前付きプロパティ・グループ型・固定配列
Contracts/*.g.cs             CALL / FILE / SQL / 未対応処理の型付き契約
Stubs/<program>/*.cs         初回生成する、利用者が編集する実装
Runtime/*.cs                IR に依存しない精密数値・固定長文字の共通部品
reports/                    診断・原文対応・スタブ一覧・契約変更
```

```sh
# 生成先を別の端末へ移しても、元リポジトリや Python は不要
dotnet build out/amount/CobolCS.Generated.csproj
dotnet out/amount/bin/Debug/net10.0/CobolCS.Generated.dll AMOUNT examples/AMOUNT/fixture.json

# COPY 展開・位置情報・診断を調べる
dotnet run --project src/CobolCS.Cli --no-build -- analyze examples/COPYJP --copybook-path examples/COPYJP/copy --out out/analysis

# 未対応箇所を報告する、ビルド可能な雛形
dotnet run --project src/CobolCS.Cli --no-build -- translate examples/UNSUPPORTED --mode scaffold --out out/review
```

**0.2 の既定は `--backend source --mode scaffold`** です。従来の動作は `--backend interpreter --mode strict` で選べます。
source は未対応箇所に到達した時点で、原文位置・文ID付きのスタブを呼びます。既定スタブは例外を投げます。
未到達のスタブだけで入口を拒否せず、`Stubs/*.cs` を実装すると変換器を変更せず継続実行できます。
CALL・逐次ファイル・構造化SQLは別インターフェースで、明示的な応答・更新値・状態が必要です。
DB・実ファイル接続は組み込んでいません。fixture のスカラー／表初期化・ACCEPT入力と、編集したC#アダプターを使えます。
具体例は [CLI文書](docs/cli.md#cスタブの実装) を参照してください。

source の `strict` は未解決スタブのある PROGRAM-ID を拒否し、終了コード1を返します。
問題のないPROGRAM-IDは同じ出力先に生成します。明示的な外部アダプター契約はstrictでも生成可能ですが、既定実装への到達は失敗します。
未知のレイアウトは `UnknownCobolValue` で残します。構造を復旧できない入力は診断に理由を残し、正常な空処理にしません。

再生成では管理対象のハッシュを検査し、手編集・削除があれば拒否します。
`Stubs/**/*.cs` は管理対象外の編集領域として**上書き・削除せずバイト単位で保持**します。
契約変更は `reports/contract-changes.json` に通知します。旧実装に自動パッチを当てず、不適合はビルドエラーになります。
それ以外の管理外ファイル、シンボリックリンク、台帳のパス逸脱は引き続き拒否します。
`analyzed`、`generated`、`built`、`executedWithFixtures`、`matchedBaseline`、`needsReview` を区別します。
`compare` は与えた JSON 同士を比較する独立コマンドで、台帳の `matchedBaseline` を変更しません。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
python3 tests/regression.py
```

Windows では必要に応じて `python3` を `python` に読み替えてください。
解析用の実行ファイルは `COBOLCS_PYTHON` 環境変数で指定できます。
GitHub Actions は Ubuntu のみを対象とします。ローカル検証は Linux、.NET SDK 10.0.401 で実施しました。

CLI と fixture の契約は [docs/cli.md](docs/cli.md)、構成・拡張方法は [docs/development.md](docs/development.md)、
採用元 SHA とライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

MIT License。
