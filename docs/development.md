# 開発

```text
src/CobolCS.Cli/       引数、解析器起動、対応検査、生成、台帳
src/CobolCS.Runtime/   有理数、PIC 値、式、命令列 lowering、反復 dispatcher
src/CobolCS.Frontend/  C#移植中の設定・PICライブラリ（製品CLIはまだ未接続）
python/frontend/      CobolTS から固定 SHA で採用した位置情報付き解析器
vendor/pythonbol/     固定採用した PythonBol の PIC 解析・モデルと参考 parser
examples/             合成 COBOL と期待値
tests/CobolCS.Tests/   C# 数値・文字列の回帰検証（外部テストライブラリ不要）
tests/CobolCS.Frontend.Tests/ 固定参照の差分検査、設定・PICのMSTest
tests/migration/      移行前の入力・IR・ソースハッシュとテスト移行対応表
tests/regression.py   実 CLI → C# 生成 → ビルド → 実行の結合テスト
```

まずREADME、supported-subset、cliを読み、AMOUNTを生成してください。
`Programs/*Program.g.cs` は直接生成した代入・if・ループと原文コメントを持ちます。
`SourceBackend` はモデル／式／文／外部契約／プロジェクト生成に分割しています。
単純な本文には命令配列・Machine.Run・JSON式評価を生成しません。
段落を持つ場合にだけ、生成時のFlowBuilderを使ってenumとswitchの移動先を決定します。
各caseの本文も通常C#です。実行時にFlowBuilderやIRは不要です。

`ExpressionTypes` はliteral/reference/figurative/unary/binary/functionの型と、関数の引数数・戻り型・native対応状態を共用します。
未知はunknownとして扱います。Validationは入れ子・VALUE・88条件のlow/highも検査します。
診断とスタブ一覧はPROGRAM-ID・文ID・原文位置・理由・capabilityを保持します。
`CobolValue` と `SourceSupport` はIRに依存しない値の演算・比較・格納規則です。Machineもこれらを共用します。

機能追加は対応検査、source生成、必要な共通処理、実行／スタブ試験、対応表を同時に更新してください。
frontendが解析できるだけでnativeへ昇格させてはいけません。
C〜Fの未対応機能はスタブを生成します。native追加までは文全体の意味をスタブ実装が担い、内側の業務文を外へ落としません。
明示的な外部契約はCALL/FILE/SQL別です。通常のfixture入力と利用者のC#実装、実接続の検証を区別します。

`.g.cs`は生成管理、`Stubs/**/*.cs`は初回生成する編集領域です。
スタブを文IDごとのpartialクラスのメソッドへ分けており、1箇所だけ実装できます。
既知の外部引数・応答は型付きrecord、既知項目の未対応文は型付き引数／参照セルを使います。
読み書きが不明なブロックだけ状態全体を受け取ります。未知型はUnknownCobolValueです。
再生成前に管理ファイルを検証し、スタブのバイト列をそのまま新ステージへコピーします。
契約ハッシュの差分を `reports/contract-changes.json` へ出し、古い実装を自動修正しません。
文の挿入・削除によってfrontendの連番文IDが変わる場合も、古いスタブは残します。差分を確認して必要な実装を移してください。

IRは**2.2**です。既存フィールドを保持する追加変更です。
2.2 では `programDeclaration`、`conditionalDirectives`、FD の `descriptionClauses` / `blockClause` / `dataRecords`、
欠落 COPY の `missingCopyOrigins` と unknown 宣言の `layoutAlternatives` を追加しました。
各 metadata は `reports/ir.json` に残り、FD は依存レポート・対応するスタブの依存契約にも渡されます。
`p64` は未指定を保持し、未選択分岐を native 本文へ混ぜません。詳細は [CLI](cli.md) と [18本の対応記録](remaining18.md) を参照してください。
2.1 では次の変更を追加しました。
frontend診断にprogramId／復旧可能なstatementIdを追加し、acceptInputノードを追加しました。
複合EVALUATEは既存controlStub形式をnative設定でも使って保持します。
2.0の既知ノードは引き続き扱えます。2.1を読む外部ツールは新しいノードを未知として明示処理してください。
CLIの既定はsource/scaffoldへ変更しました。旧命令列の出力には `--backend interpreter --mode strict` を指定します。
`manifest`にはbackend/mode/rejectedPrograms/contractHashes/editableFilesと契約変更状態を追加しています。
contractHashesは契約ソースに加え、型付き引数のレイアウトを表すModelsも対象にします。
制御スタブはControlKindと任意の移動先を持つControlResultを返します。JumpToは段落dispatcherが受け取り、戻り先スタックを維持して生成済み段落・文末境界へ移動します。

```sh
dotnet build CobolCS.sln -m:1
dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore
dotnet run --project tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
python3 tests/regression.py
```

StyleCop Analyzers `1.2.0-beta.556` を全 C# プロジェクトと生成プロジェクトのビルドに適用します。
`PrivateAssets="all"` で開発用に限定し、実行時ライブラリとして配布しません。
既存 runtime 回帰は追加テストライブラリ不要です。0.3 の生成テストと Frontend.Tests は
Microsoft.NET.Test.Sdk 18.0.1、MSTest.TestFramework 4.0.2、MSTest.TestAdapter 4.0.2 を固定します。
root 実行プロジェクトへテスト依存は追加しません。初回は solution を restore し、以後は `dotnet test --no-restore` も使用できます。

C#移行の固定基準・検証結果・残作業は [移行記録](csharp-migration.md) を参照してください。
`PythonReference` カテゴリは移行中だけの参照実装検査です。製品の `COBOLCS_PYTHON` と独立した
`COBOLCS_REFERENCE_PYTHON` で参照用実行ファイルを指定できます。指定しない場合は Linux の `python3`、Windows の `python` を使用します。
Pythonを使わない固定期待値の検査は `dotnet test tests/CobolCS.Frontend.Tests --filter 'TestCategory!=PythonReference'` で実行できます。
これは全CIや製品のPython不要化の証明ではありません。通常のCIでは参照検査も実行します。
ただしNuGetの脆弱性監査キャッシュ更新は接続を要求する場合があります。監査エラーも警告のエラー化の対象です。
警告はエラーとして扱い、除外は指定された SA1101 / SA1200 / SA1309 / SA1629 / SA1633 の五つだけです。
追加のルール無効化や、手書きコードを自動生成扱いにする回避は行いません。

XML コメントでは、役割と必要な理由、入力の契約、返される値、代表的な失敗条件を日本語で説明します。
たとえば「切り捨てる」だけではなく、PIC への格納が丸めの境界である理由や、負数の切り捨て方向まで記載します。
StyleCop が要求するコンストラクターの `Initializes ...` とプロパティの `Gets ...` の定型句に続けて、
日本語の具体的な説明を付けています。メソッド名を言い換えただけのコメントで済ませないでください。

生成プロジェクトは自分自身の `Directory.Build.props` を持ち、保存先の親プロジェクトの設定に依存しません。
生成する C# にも日本語 XML コメントを含めます。`.g.cs` と生成runnerには生成コードの印を付け、Runtimeと編集可能なStubsには通常のStyleCop検査を適用します。
編集領域を生成コード扱いにして検査を回避しません。生成プロジェクトのAnalyzerが有効なことは、未文書化クラスの負のビルド試験で確認します。
ソリューションの `-m:1` は、子 MSBuild プロセスを制限する作業環境でも実行できる指定です。
環境の通信設定に依存しないよう、採用した Python ソースはすべてリポジトリに同梱します。
公開時は `dotnet publish src/CobolCS.Cli -o <配布先>` で CLI と解析用ファイルを一緒に出力できます。

公開 API・fake・レビュー・採用の構成と制約は [実行ワークフロー](execution-workflow.md) を参照してください。
追加結合試験は `tests/test_workflow.py`、`tests/test_workflow_external.py`、`tests/test_workflow_safety.py`、`tests/test_workflow_noop.py` です。
