# 実行準備、正式引数、独立期待値

既存source backendと公開Executorを拡張します。別の実行エンジンやJSON命令列は追加しません。
生成C#の実行には元CBL・IR・Pythonは不要です。原COBOLとの業務同値性を保証するものではありません。

## 出力と編集先

| 出力 | 所有者・用途 |
|---|---|
| `reports/execution-readiness.json` / `.md` | 生成器。schemaVersion 1、入力・依存・未知宣言・内部未対応の静的候補、位置、型、PIC、引数順・渡し方、編集先 |
| `Api/*Api.g.cs` | 生成器。Arguments、ArgumentsSnapshot、既存StateSetupとExecutor |
| `UserCode/Scenarios/*Scenarios.cs` | 利用者。sample用ACCEPTキュー、正式Arguments、編集済みStubsで実行 |
| `UserCode/Scenarios/*ScriptedScenarios.cs` | 利用者。sample-scripted専用入力とfake登録。ケース間で入力・fakeを共有しない |
| `UserCode/Scenarios/*ResponseExamples.cs` | 利用者。文別matcherと型付き応答の記入例。自動登録しない |
| `Tests/Cases` / `Tests/Baselines` / `Tests/review.json` | 利用者。期待値と由来、明示ファイルhash、レビュー状態 |
| `Stubs` | 利用者。外部実装または到達時に必要な内部未対応処理 |

`Preparation.Required<T>` は位置または項目名付きの未設定エラーです。0や空文字で代用しません。
ACCEPT一覧はソース順の候補であり、分岐・反復の実消費順は利用者が指定します。
応答例は `ScriptedDependencies.Expect*` を使い、ターゲットや引数をmatcherで検査します。
FILE正常READはレコード必須、EOFはレコード出力を省略します。SQL正常、no-data、失敗を
状態コードとoutcomeで区別し、負のNULL指示変数ではHost出力を省略します。
内部未対応文のContinueやCALL NoOpを自動設定しません。

新規生成の `sample` は `Executor.Run(input, options: options)` で編集済みStubsを使用します。
`sample-scripted` は別クラスの `Executor.Run(input, fake, options)` でケース専用応答を使用します。
不足・matcher不一致・順序違い・余剰応答は失敗し、Stubsへ迂回しません。
既定テストはsampleです。fakeで検査する場合はテストの呼出しを
`<program>ScriptedScenarios.Sample(new ExecutionOptions())` へ切り替えます。
入力・応答・期待値は独立した根拠で記入し、`Tests/review.json` のfiles一覧に使用する実装を明示します。
初期一覧にはプログラムのStubs、両シナリオとResponseExamplesも保守的に含めます。
独自ヘルパーなどの依存ファイルは手動で追加してください。実行だけで期待値・hash・reviewedは更新しません。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- run out/payment --scenario sample
dotnet run --project src/CobolCS.Cli --no-build -- run out/payment --scenario sample-scripted
```

再生成は編集・削除を保存します。既存応答例は自動パッチされません。
レポートの `exampleMethod` / `exampleStatus` と `reports/contract-changes.json` を確認し、
新しい文の登録箇所を手で追加してください。参照レイアウト変更も編集済みスタブの再束縛を拒否します。
旧形式のsampleは元のファイルと挙動を保持します。編集済みStubsを使う場合はexternal引数を省略してください。
既存出力へのscripted自動登録は保守的に見送ります。追加・保持されたScriptedScenariosファイルの型とメソッドを確認し、
レポートの手順で `UserCode/ScenarioRegistration.cs` へ登録してください。同名ファイルを上書きしません。
登録名が重複した場合は失敗します。利用者側の登録名を変更するか重複登録を削除してください。
削除済みシナリオを自動復元せず、管理登録からの参照も外します。利用者テスト・登録側の参照は手動で整理してください。
旧版にレイアウト束縛情報がない場合は、検証済みの旧IRから参照宣言を照合し、原文・契約・レイアウトがすべて同一の場合だけ移行します。
未対応の外部依存は `unsupported-call/file/sql` として内部未対応文から分離し、型付き応答を生成できない場合は元の編集可能スタブへ案内します。

## 値契約

`assertionCandidates` はIRから抽出した更新可能な基本項目の一覧です。MOVE・算術の全受取先、
余り、入力先、初期化対象、STRINGの受取先／POINTER、SET・VARYING、外部CALLのBY REFERENCE／RETURNING、
FILE・SQLの出力／状態項目を `symbolId` ごとにまとめ、更新元の文ID・位置・理由を残します。
未対応文でも、IRに保持されたUNSTRINGの受取先・補助項目、INSPECTの更新先、SORTの転送先、
SEARCHの索引などは候補へ含めます。これらの候補があることは、その文の実行対応を意味しません。
実行経路や更新が実際に発生したことは保証しません。更新なしのプログラムで無関係な先頭項目を補いません。

既知の候補には `Preparation.Required<T>` による期待値記入箇所を生成します。未記入でもビルドできますが
テスト時には失敗します。固定グループは基本項目に展開し、Argumentsの結果で比較する項目はSnapshotに重複生成しません。
表は型付き期待値配列との一括比較です。一部だけを確認する場合は生成コメントを参考に
`Assert.AreEqual(expectedElement, result.Snapshot.<member>[zeroBasedIndex])` へ変更してください。

候補にはPIC・型・dimensions、検査先メンバー、テスト参照を記録します。`generated-template` は初回の比較雛形、
`manual-existing-tests` は既存または削除済みユーザーテストの保持、`manual-unknown` は未知の型／更新領域です。
後二者は手動確認が必要です。雛形の生成を「期待値設定済み」「レビュー済み」「網羅済み」と扱いません。
再生成は既存テスト・baseline・review hashを更新せず、新しい候補をレポートで案内します。

`Input.Arguments` の各USING引数はOptionalで必須指定を検査します。
固定グループは既存GroupSnapshotを拡張した公開DTOです。階層内の基本項目を一意なメンバー名で公開し、
固定表は外側から内側の順に平坦化した完全な値列を渡します。
公開constructorは全基本項目と表要素数・PICを検査します。FILLERもグループsnapshotに含みます。
名前は `reports/source-map.json` と生成APIから確認できます。
入力の配列をコピーし、読み取り専用snapshotを返します。`Copy()` / `ContentEquals()` が深いコピーと内容比較を提供します。

単体実行はBY REFERENCEと数値スカラーBY VALUEに限定します。後者も内部処理後の観測値は結果に含みますが、
呼出し元DTOは変更しません。宣言側BY CONTENT、未知PIC、REDEFINES、RENAMES、可変表、ポインタ、
欠落COPY等で確定できない引数は入口スタブを維持します。CANCEL、INITIAL、RECURSIVEやrun-unit全体の寿命は未実装です。
従来のLINKAGE用InitialStateプロパティはコンパイル互換を維持しますが、設定すると移行エラーになります。

CALLは固定グループ全体を型付きRequestへコピーします（内部固定表可）。
BY REFERENCE出力は外側Optionalで「更新なし」を表し、更新時は完全snapshotが必須です。
BY CONTENT/VALUEに応答の更新プロパティはありません。選択されたグループ表要素・グループ参照変更は未対応です。
同一または祖先・子孫の参照出力とRETURNING重複はスタブへ隔離します。
全出力の検証が済んでから状態へ書き込みます。既存スカラー応答constructorとfixtureは維持します。

## 内部文の規則と出典

次の一次資料を2026-10-11に確認しました。今回のportable subsetの根拠であり、ホスト完全互換ではありません。

- [富士通 COBOL2000 INITIALIZE](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/initialize-statement-cobol-compiler-reference-manual-2022-11-225-22036931.html): 追加句なしの既知固定領域を数値0／文字空白へ設定します。VALUEへの復元はしません。88とFILLERは受け手から除外します。国別文字は既存portableランタイムのBMPとU+0020を使い、JEF等の物理空白表現は再現しません。
- [富士通 COBOL2000 STRING](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/string-statement-cobol-compiler-reference-manual-2022-11-244-22036953.html): SIZEは送信全長、POINTERは1始まり、省略時1。格納した部分だけを変更し、格納文字数だけポインタを進めます。ちょうど収まれば正常、残りの文字を格納できなければ途中overflowです。不正な初期ポインタは無更新のoverflow、句がなければ次の文へ進みます。
- [IBM PROCEDURE DIVISION argument passing](https://www.ibm.com/docs/en/cobol-aix/5.1?topic=data-coding-procedure-division-passing-arguments): 宣言側のREFERENCE/VALUEと呼出し側CONTENTを区別します。今回のBY VALUEはfrontendで確認できる数値スカラーに限定します。

INITIALIZEのREPLACING等、STRINGの値区切り・国別文字混在・未対応参照は文全体のスタブです。
子ブロックを通常フローへ漏らしません。UNSTRING、INSPECT、CORRESPONDING、SORT等の範囲は拡張していません。

## 最短の合成デモ

リポジトリルートで実行します。検証対象はLinuxです。

```text
dotnet build CobolCS.sln -m:1
dotnet run --project src/CobolCS.Cli --no-build -- generate examples/WORKFLOW/ARGCALC.cbl --output out/argcalc
dotnet run --project tools/CobolCS.Tools --no-build -- prepare-readiness --out out/argcalc
dotnet test out/argcalc/Tests/CobolCS.Generated.Tests.csproj
dotnet run --project out/argcalc -- ARGCALC --scenario sample
dotnet run --project src/CobolCS.Cli --no-build -- adopt out/argcalc --output out/argcalc-adopted
```

補完スクリプトは実際のmappingを読み、ユーザー領域だけを編集します。既存編集や削除は上書きしません。
手計算の正常・ゼロ・上限値、および入力不足・コピー独立性を検査します。
期待値はhash束縛されたテストコードに置き、baselineの `expected.codeFile` がそのファイルを示します。
この合成仕様を確認済みとしたデモ専用の `independent-synthetic` レビューであり、実業務の自動承認ではありません。
一般の生成時・dotnet test時にreviewedを変更する処理はありません。

`dotnet test tests/CobolCS.IntegrationTests --no-build --filter FullyQualifiedName~ReadinessTests` は補完、編集・削除保持、adopt、元生成先なしの実行、
採用後の計算抽出、意図的な計算破壊の検出まで自動検証します。
検証結果と未実施項目は[測定記録](execution-readiness-results.md)に記載します。
