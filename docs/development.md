# 開発

開発の主目的は、C#へのマイグレーションに必要な、COBOLの振る舞いを固定するテスト資産を生成することです。C#生成は実行・観測の基盤として位置付け、入力・外部応答・期待値の根拠とテストの再現性を保ちます。レビュー・adopt後は、テスト資産で振る舞いを確認しながらモダンなアーキテクチャへリファクタリングする想定です。自動観測による生成C#の挙動固定と、元COBOLの結果や独立期待値による検証は区別してください。

このページは CobolCS の実装と検証に参加する人向けです。利用者向けの最短手順は[README](../README.md)、生成物の編集手順は[実行ガイド](generated-csharp-quickstart.md)を参照してください。

外部回帰fixtureは、個別ファイルの出典・固定版・ライセンス・通知を確認した場合に限り
`tests/fixtures/third-party` へ保存します。PROVENANCEに原本と変更後のhash、変更内容、
期待値の独立した根拠を記録します。未確認の原文・生成物と大規模コーパスはコミットしません。

```text
src/CobolCS.Cli/       引数、解析器起動、対応検査、生成、台帳
src/CobolCS.Runtime/   有理数、PIC 値、式、命令列 lowering、反復 dispatcher
src/CobolCS.Frontend/  製品CLIから直接呼ぶC#解析器・設定・PIC
tools/CobolCS.Tools/   コーパス取得・測定・レポート・合成デモ準備
vendor/pythonbol/     採用元SHAとMITライセンスのみ（PythonソースはGit履歴）
examples/             合成 COBOL と期待値
tests/CobolCS.Tests/   C# 数値・文字列の xUnit 回帰検証
tests/CobolCS.Frontend.Tests/ 固定参照の完全IR差分検査、設定・PICのxUnit
tests/CobolCS.IntegrationTests/ 生成物比較と解析器切替時の再生成保護
tests/CobolCS.Frontend.CorpusTests/ 固定公開入力171本の両モードIR比較
tests/migration/      移行前の入力・IR・ソースハッシュとテスト移行対応表
tests/CobolCS.IntegrationTests/Migrated/ 元の回帰・ワークフロー・実行準備の.NETテスト
tests/CobolCS.ProcessProbe/ 引数・両出力・timeout検証専用の.NET実行ファイル
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

解析APIは `FrontendAnalyzer.Analyze(input, configuration, copybookPaths, allowMissingCopy)` で完全な `JsonObject` を返します。
`Parsing/Parser.*` は宣言・式・手続き・ファイル・SQL、`SourceFrontend.*` は位置付き字句処理・条件処理・COPYを担当します。
内部の `V` は共有参照を持つIR構築値です。単一親を要求する `JsonNode` への変換は出力境界だけで行います。
`Values` はUnicodeスカラーの位置、Python由来の数値・文字列・列挙順を保持する内部操作で、外部Pythonプロセスやスクリプト評価は使いません。

IRは**2.2**です。C#解析器の `frontendVersion` は `0.3.0-csharp` です。既存フィールドを保持する追加変更です。
2.2 では `programDeclaration`、`conditionalDirectives`、FD の `descriptionClauses` / `blockClause` / `dataRecords`、
欠落 COPY の `missingCopyOrigins` と unknown 宣言の `layoutAlternatives` を追加しました。
深掘りコーパスでの追加受付では、DISPLAYの改行抑止句を任意の `noAdvancing: true` として保持します。
句がない既存displayノードへfalseフィールドを補いません。これは追加メタデータで、既存Display捕捉APIの値列は変更しません。
IRのJSON読み書きは共通の有限上限 `J.MaxDepth = 512` を使用します。COBOLの入れ子には配列・文・source metadataの階層も加わるため、COBOL文を512段まで保証する指定ではありません。上限超過は拒否し、深い文を切り捨てません。
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
dotnet test tests/CobolCS.Tests --no-build
dotnet test tests/CobolCS.Frontend.Tests --no-build
dotnet test tests/CobolCS.IntegrationTests --no-build
```

StyleCop Analyzers `1.2.0-beta.556` を全 C# プロジェクトと生成プロジェクトのビルドに適用します。
`PrivateAssets="all"` で開発用に限定し、実行時ライブラリとして配布しません。
Runtime・Frontend・Integration・Corpus と生成テストは
Microsoft.NET.Test.Sdk 18.0.1、xunit 2.9.3、xunit.runner.visualstudio 3.1.5 を固定します。
root 実行プロジェクトへテスト依存は追加しません。初回は solution を restore し、以後は `dotnet test --no-restore` も使用できます。

C#移行の固定基準・検証結果・残作業は [移行記録](csharp-migration.md) を参照してください。
製品・開発・CIは.NETのみで実行します。参照IRは固定ファイルと正準JSONハッシュへ移し、Python実装はGit履歴だけに残しています。
コーパス検査は先にlock指定の入力を取得し、`dotnet test tests/CobolCS.Frontend.CorpusTests --filter FullyQualifiedName~course` 等で実行します。
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
CLIと補助ツールが起動する子プロセスには `MSBUILDDISABLENODEREUSE=1` を設定します。
再利用MSBuildノードが標準出力・標準エラーのパイプを保持すると、`restore`本体の終了後も出力回収が完了しないためです。
CIの統合試験は親の値を `0` にして、利用者側の環境設定なしでも `adopt` が完了することを検証します。
公開時は `dotnet publish src/CobolCS.Cli -o <空の配布先>` でCLI・C#解析器・生成テンプレート・必要なライセンスを出力できます。
Pythonソース・起動設定は撤去済みです。旧配布先への上書きは以前のPythonファイルを残すため、空の配布先を使ってください。

公開 API・fake・レビュー・採用の構成と制約は [実行ワークフロー](execution-workflow.md) を参照してください。
追加結合試験は `tests/CobolCS.IntegrationTests/Migrated/` にあり、元の108関数は [対応表](../tests/migration/test-mapping.md) で追跡できます。

CLIの基本形は `cobolcs <file.cbl|directory> [-o <dir>] [-I <copy-dir>]`。詳細は[CLI仕様](cli.md)を参照してください。CIはPR時のみ実行し、1回のビルド成果物を通常試験と5コーパスの並列ジョブで共有します。NuGetキャッシュとPythonソースなしの検査は共通Actionで管理します。

アーカイブ取得の回帰テストは Bash・curl・unzip・zipinfo・gzip・tar・sha256sum を使います。通常試験ジョブは `unzip` と `zipinfo` の存在を確認し、不足時だけ `unzip` パッケージを導入します。ローカルでも両コマンドを用意してください。テスト用ZIPはローカル生成し、外部入力の取得通信は行いません。

CIは `.github/workflows/ci.yml` の1本で管理します。`build` が書式検査後に `src` / `tests` / `tools` の
`bin`・`obj` をtarで保存し、`test` と `corpus`（5種類）が同じコミットの成果物を展開して `--no-build` で検証します。
全ジョブはUbuntuランナー上で共通Actionが.NET 10 SDKを用意し、コンテナイメージの取得待ちをなくします。tarで実行権限も保持します。NuGetの保存先は全ジョブで
`.cache/nuget` に固定し、依存定義をキーにキャッシュします。生成結果やテスト成功はキャッシュしません。
コーパスごとのsummaryは個別artifactへ保存し、1種類が失敗しても他のコーパス検証は継続します。
統合試験は `branch`・`generation`・`regression-a/b`・`workflows-a/b` の6ジョブへ重複なく分割します。
RegressionTestsの名前がTestA～TestPを含む群と残りを相補的に分け、workflowsはReadiness・所有権・外部ワークフロー等の群と残りを分けます。新規テストも残り側へ入り、除外されません。
各ジョブは45分の上限を持ち、1ジョブの失敗でも他の検証を継続します。個々のテスト結果・時間をコンソールへ出力し、
TRXを `integration-<suite>-results` artifactへ保存します。Runtime・Frontendは `branch` ジョブで1回実行します。
通常試験の時間と5コーパスの時間を足す構成から、ビルド後は最も遅いジョブの完了を待つ構成になります。
carddemoの44ケースとbankの43ケースはそれぞれ4ジョブ（11件ずつ、bankの最後は10件）へ分割します。
コーパス評価は計11ジョブとし、最後の `corpus-verify` で全分割を統合します。固定IR照合はコーパスごとに1回、
ケースの解析・生成・ビルド・独立期待値は全件で実行します。分割の欠落・重複・元lockやコンパイラー条件の相違を拒否し、元の生成数下限を維持します。
コーパスのケース・工程別durationMsと進捗ログを保存します。分割summaryは全体のverifyに使えず、統合したsummaryを検証します。

シナリオ経路の回帰は `ScenarioPathTests` が、sampleの編集済みStubs実行、sample-scriptedの独立応答と厳密な失敗、
既存出力のファイル衝突・削除保持を検証します。生成したテストプロジェクトも実行します。

通常の source `generate` は分岐探索と固定xUnitも出力します。未レビューの自動期待値は C# の挙動固定として実行でき、adopt にはレビューが必要です。未記入sampleは `Tests/Templates/` に置きます。出力先・有限予算・手動候補・未到達理由・再生成時の保持は[分岐探索とxUnit](branch-test-generation.md)を参照してください。

探索workerの回帰は `ProbeSessionTests` で確認します。最大32試行での回収、static状態の共有可否、異常JSON、UTF-8バイト上限と両パイプ上限、timeout後の孫プロセス終了と復旧を検証します。時間は管理ファイルの決定性を保つためstderr診断だけへ出します。[工程別の性能測定](probe-performance.md)を参照してください。

内部補完の `prepare/explore` と業務テストは [内部補完ワークフロー](completion-workflow.md)。探索器は実装選択だけを拡張して共用し、IR 依存のない BusinessComparison/BusinessCases を Runtime に置きます。CompletionWorkflow が stage・競合・所有権を扱い、CompletionWorklist が手動状態と実測を分離し、BusinessReview が必須 ID 履歴と採用時 hash を検査します。新規回帰は CompletionWorkflowTests / BusinessComparisonTests / BusinessFourFixesTests で、CI workflows-b の相補フィルタに含まれます。


## xUnit の実行契約

リポジトリ内と新規生成先のテストは xUnit 2.9.3 と VSTest adapter 3.1.5 を使用します。
ランタイムの既存16263アサーションは26件のテストにまとめ、`dotnet test tests/CobolCS.Tests --no-build` で実行します。
テストの共有状態を保護するため、assembly のテスト並列化は無効です。
初期化と後片付けはテストごとのコンストラクターと `IDisposable.Dispose` に移しました。
CI の分割と生成ケースの選択は xUnit adapter が扱う `FullyQualifiedName` で行います。
生成先の `Tests/xunit.runner.json` は methodDisplay=method と直列実行を継承します。
characterization の Fact はメソッド名で、業務 Theory は完全 SHA-256 の32桁ずつ2引数で TRX と対応させます。同名・skip・0件の拒否は維持します。
探索レポートの `replayedMSTest`、性能値の `mstestReplayMs`、工程の `mstest-*` は既存 JSON の互換性を保つため名前を維持し、
現在は xUnit の固定再生成功を示します。既存生成先の利用者所有テストは再生成でも保持されるため、
MSTest のテストを持つ既存出力は移行前のバックアップから新しい生成先へ手動で移植してください。
過去の受入記録は当時の MSTest の結果として残しています。

`5d928fb` を基準に Linux / .NET SDK 10.0.401 で検証しました。Runtime 26件、Frontend 194件、Integration 292件、固定公開コーパス5群の計517件の最新結果は全件成功、skip 0です。統合試験は全292件を実行した後、修正の影響を受けた36件と最後の計算破壊検出1件を再実行し、同じ testId の最新TRX結果を照合しました。ビルドは警告・エラー0件、整形検査と `git diff --check` も成功しました。生成・採用・skip拒否・採用後の計算破壊検出を維持し、固定参照IRやコーパスlockは変更していません。

---

[ドキュメント案内](README.md)へ戻る。

業務支援は SourceBackend.BusinessCandidates（既存 metadata）、BusinessScaffolding（検査と stage）、BusinessProjection（型付き式）、Runtime BusinessValues（固定表・精密十進）に分離します。文字処理は CharacterCapabilities を Validation と source 生成が共用し、SourceCharacters を生成先へ同梱します。Runtime の IR interpreter はこの追加範囲に未対応です。CI workflows-b が追加4クラスを含み、Runtime は SourceCharacterTests を含みます。[仕様と合成例](business-test-assistance.md)、[検証](business-test-assistance-results.md)。

レビュー指示のF1/F2/B1/A1/M1回帰はBusinessAssistanceTests、BusinessTemplateTests、BusinessAcceptTests、CharacterGenerationTestsで検証します。新2クラスはCI workflows-bの相補フィルタに含まれます。Runtime BusinessAcceptScriptは生成物コピー許可リストに含め、独立build／adopt後サービス置換でも使用します。InspectとStrictの共通検査を拡張し、factoryを探索・discoveryで実行しません。固定公開171入力のbeforeはef554f8で別worktreeから測定します。結果は新しいreview-workflow-results記録へ保存し、過去の測定・期待IR・lockを変更しません。

日本語コーパスの独自最小再現は `JapaneseParserTests` にあります。source/interpreterの独立ビルド・実行、文字コード、COPY原位置、継続行、出力句・復旧・境界を確認します。既存CIの相補的なworkflows-bフィルタに含まれます。第三者入力は同梱せず、847行の原文なし測定記録は `tests/japanese-corpus-results` に分離しています。[測定範囲と制約](japanese-corpus-parser-results.md)。
