# CobolCS

**生成後に何を編集して実行するかは、[生成C#の実行ガイド](docs/generated-csharp-quickstart.md)を参照してください。** 起動用の`Program.cs`と`.csproj`も生成されます。入力とスタブを補完し、`--scenario sample`で実行できます。

通常generateはC#と同時に分岐探索による固定MSTestを `Tests/Cases/BranchGenerated/` へ生成します。未レビューでも挙動テストを実行でき、adoptにはレビューが必要です。[手順と対応範囲](docs/branch-test-generation.md)、[合成試験の実測](docs/branch-test-generation-results.md)を参照してください。

分岐探索は状態分離を保ったworkerでプロセス起動を削減します。[工程別の性能測定と検証範囲](docs/probe-performance.md)を参照してください。

公開サンプルの回帰素材は、個別ライセンスと出典を確認して
`tests/fixtures/third-party` に保存しています。[採用条件と来歴](docs/deep-corpus-license-review.md)を参照してください。
未確認の原文・生成物や大規模コーパスは外部キャッシュに保持します。
今回の取得済み入力による実測と、別作業へ延期した全件評価の範囲は
[検証結果](docs/deep-corpus-results.md)に記録しています。

COBOL の定義済み部分集合を、独立してビルド・実行できる **C# / .NET 10** プロジェクトへ変換する CLI です。
[CobolTS](https://github.com/akrym1582/CobolTs) の位置情報付き解析器と、
[PythonBol-Translator](https://github.com/IronAdamant/PythonBol-Translator) の PIC 解析を参考・採用しています。
CLI、解析器、生成器、ランタイム、回帰テスト、補助ツールを C# で実装しています。
開発時のビルドには StyleCop Analyzers を適用し、XML コメントを含む警告をエラーとして検査します。

**解析・変換・生成プログラムのビルド・実行は .NET 10 だけで動きます。**
回帰テスト、デモ準備、公開コーパスの取得・検証にもPythonは不要です。公開コーパスの取得にはGitを使用します。
LLM、API キー、Node、Docker、DB、NuGet の実行時依存はありません。
初回ビルドでは開発用の StyleCop Analyzers を NuGet から復元します。通常generateの探索・固定テスト検証には.NET SDK 10と固定版MSTest/VSTestの事前取得・キャッシュも使います。ビルド時はNuGetの脆弱性監査の更新にも接続が必要になる場合があります。解析・生成・通常の生成プログラムの実行にネットワークは不要です。
これは初期版であり、参照元の全機能を移植したものではありません。
対応範囲は [docs/supported-subset.md](docs/supported-subset.md) に明記しています。

基本操作は **`cobolcs sample.cbl`** です。カレントディレクトリの `sample-csharp/` に
通常の C# ソースとビルド用プロジェクトを生成します。`-o <dir>` で出力先、`-I <dir>` で COPY 検索先を指定します。
ディレクトリ入力にも対応します。単一プログラムは `cobolcs run sample-csharp` で実行できます。

```sh
dotnet publish src/CobolCS.Cli -o out/cli
./out/cli/cobolcs examples/AMOUNT/AMOUNT.cbl
```

リポジトリから実行する場合：

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project src/CobolCS.Cli --no-build -- examples/AMOUNT/AMOUNT.cbl -o out/amount
dotnet run --project src/CobolCS.Cli --no-build -- build out/amount
dotnet run --project src/CobolCS.Cli --no-build -- run out/amount --fixture examples/AMOUNT/fixture.json -o runs/amount.json
dotnet run --project src/CobolCS.Cli --no-build -- compare runs/amount.json examples/AMOUNT/expected.json
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
dotnet run --project src/CobolCS.Cli --no-build -- analyze examples/COPYJP --copybook-path examples/COPYJP/copy --output out/analysis

# 未対応箇所を報告する、ビルド可能な雛形
dotnet run --project src/CobolCS.Cli --no-build -- examples/UNSUPPORTED --scaffold -o out/review
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
UserCode／Tests/Cases／Tests/Baselines／Tests/Templates／Tests/review.json／Fixtures も利用者所有として保持します。
それ以外の管理外ファイル、シンボリックリンク、台帳のパス逸脱は引き続き拒否します。
`analyzed`、`generated`、`built`、`executedWithFixtures`、`matchedBaseline`、`needsReview` を区別します。
`compare` は与えた JSON 同士を比較する独立コマンドで、台帳の `matchedBaseline` を変更しません。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
```

製品CLIはC#解析器を直接呼びます。旧設定 `COBOLCS_PYTHON` は使用しません。
参照比較用の `COBOLCS_REFERENCE_PYTHON` も廃止しました。移行の根拠と108回帰の対応表は [移行記録](docs/csharp-migration.md) を参照してください。
GitHub Actions は PR 時のみ、Ubuntuで1回ビルドした成果物を共有します。統合試験を6ジョブ、5コーパスの評価を11ジョブ（carddemo・bankは各4分割）で並列検証し、最後に全コーパスの欠落・重複と検証下限を確認します。NuGetパッケージはキャッシュします。push時の実行をなくし、同じPRの古い実行はキャンセルします。ローカル検証はLinux、.NET SDK 10.0.401で実施しました。

アーカイブ取得の回帰テストには `unzip` と `zipinfo` が必要です。CIの通常試験ジョブでは `unzip` パッケージを導入し、ローカルでは事前に両コマンドを用意してください。

CLI と fixture の契約は [docs/cli.md](docs/cli.md)、構成・拡張方法は [docs/development.md](docs/development.md)、
採用元 SHA とライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

MIT License。

固定版の公開 COBOL 171 本の取得・source backend 評価・ライセンス管理は [公開コーパス検証](docs/public-corpus.md) を参照してください。

生成 C# の公開 API、編集可能なシナリオ、MSTest、`adopt` の一連の手順は [実行・テスト・採用ワークフロー](docs/execution-workflow.md) を参照してください。


実行準備拡張では、固定LINKAGEの正式入力／更新結果、固定グループCALL、基本INITIALIZEとSTRING SIZEに対応します。
生成される実行準備レポート・入力／応答例・検査付きテストの使い方は[実行準備](docs/execution-readiness.md)を参照してください。

新規生成の `sample` は編集済みStubs、`sample-scripted` はケース専用fakeを使用します。
既存出力の移行・検査対象ファイルのレビューは[実行準備](docs/execution-readiness.md)を参照してください。

通常の source `generate` は分岐探索と固定MSTestも出力します。未レビューの自動期待値は C# の挙動固定として実行でき、adopt にはレビューが必要です。未記入sampleは `Tests/Templates/` に置きます。出力先・有限予算・手動候補・未到達理由・再生成時の保持は[分岐探索とMSTest](docs/branch-test-generation.md)を参照してください。
