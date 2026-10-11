# 開発

```text
src/CobolCS.Cli/       引数、解析器起動、対応検査、生成、台帳
src/CobolCS.Runtime/   有理数、PIC 値、式、命令列 lowering、反復 dispatcher
python/frontend/      CobolTS から固定 SHA で採用した位置情報付き解析器
vendor/pythonbol/     固定採用した PythonBol の PIC 解析・モデルと参考 parser
examples/             合成 COBOL と期待値
tests/CobolCS.Tests/   C# 数値・文字列の回帰検証（外部テストライブラリ不要）
tests/regression.py   実 CLI → C# 生成 → ビルド → 実行の結合テスト
```

まず README、supported-subset、cli を読み、AMOUNT を生成してください。
生成された `Programs/Program_0000.cs` は元 COBOL 文のコメントと、C# の型付き命令列を持ちます。
命令の operand は解析済みの JSON 式で、ランタイムが解決済み symbol ID を使用して評価します。
生成時に制御構造を平坦化し、ビジネス処理を反復 dispatcher で実行する方式です。
手書きの業務 C# と同じ形のメソッドへ変換する方式ではありません。

機能を追加する場合は、対応検査・式または命令の実装・behavior テスト・対応表を同時に更新してください。
Python frontend が既に構文を理解できても、Validation の対応表に追加するだけでは不十分です。
生成物の手修正は再生成で拒否されるので、コンパイラ・ランタイムを変更してから生成し直してください。

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project tests/CobolCS.Tests --no-build
python3 tests/regression.py
```

テストは NuGet / pip パッケージの追加なしで動作します。
ソリューションの `-m:1` は、子 MSBuild プロセスを制限する作業環境でも実行できる指定です。
環境の通信設定に依存しないよう、採用した Python ソースはすべてリポジトリに同梱します。
公開時は `dotnet publish src/CobolCS.Cli -o <配布先>` で CLI と解析用ファイルを一緒に出力できます。
