# 生成したC#を編集して実行する

CobolCSは、処理本体だけでなく、起動用の`Program.cs`、入力設定用のシナリオ、スタブ、`.csproj`も生成します。起動用ソースを別途手書きする必要はありません。

基本の流れは「生成 → 入力とスタブを補完 → sampleを実行 → 期待値を設定してテスト → adoptしてリファクタリング」です。ビルド成功と、業務上正しい結果が得られることは別々に確認します。

## 1. CBLから一式を生成する

以下はCobolCSリポジトリのルートで、.NET SDK 10を使用する例です。`payment.cbl`を自分のファイルに置き換えてください。Pythonは不要です。

```sh
dotnet build CobolCS.sln -m:1
dotnet run --project src/CobolCS.Cli --no-build -- generate payment.cbl --output out/payment
```

COPYを参照する場合は、生成コマンドへ`-I copybooks`などを追加します。最初に現在の生成形を確認するときは、新しい出力フォルダを使ってください。以前の出力に再生成しても、利用者所有ファイルは新しい雛形に置き換わりません。

CLIを`cobolcs`として起動できる環境なら、生成コマンドは`cobolcs payment.cbl -o out/payment`と短く書けます。

## 2. 編集するファイルを確認する

`<program>`は説明用の表記です。実際のファイル名・型名には変換後の名前やハッシュが付きます。生成先のREADME、`reports/source-map.json`、`reports/execution-readiness.md`から対象を確認してください。

| ファイル | 役割 | 生成管理中に編集するか |
|---|---|---|
| `Program.cs` | 起動引数の処理、シナリオ選択、結果のJSON出力 | 通常は編集不要 |
| `CobolCS.Generated.csproj` | 生成プログラムのビルド・実行設定 | 通常は編集不要 |
| `Programs/*Program.g.cs` | COBOLから変換した処理本体 | 本文の改修はadopt後に行う |
| `Models/*State.g.cs`、`Api/*.g.cs` | データ項目、入力・結果・Executorの型 | 読んで利用する |
| `Contracts/*.g.cs` | 外部呼出しの依頼・応答の型 | 読んで利用する |
| `UserCode/Scenarios/<program>Scenarios.cs` | sampleの入力を作って実行 | 入力を記入する |
| `Stubs/<program>/*.cs` | 外部依存と未対応内部処理の実装箇所 | 必要な処理・応答を実装する |
| `UserCode/Scenarios/<program>ScriptedScenarios.cs` | fakeを使う別シナリオ | ケース別の応答を使う場合に編集 |
| `UserCode/Scenarios/<program>ResponseExamples.cs` | 文ごとのmatcher・応答の記入例 | scripted側で必要な例を補完する |
| `Tests/Cases/BranchGenerated/<runId>/*.g.cs` | 固定入力・fake・観測期待値の検査 | 仕様と照合してレビューする |
| `Tests/Templates/*.cs.txt` | 未記入の手動検査の参考 | Tests/Casesへコピーして期待値を設定する |
| `reports/execution-readiness.md` | 入力・依存・未対応箇所と編集先 | 補完作業の案内として読む |

`Preparation.Required<T>(...)`は「この値を記入してください」という目印です。未記入でもビルドできますが、その式を実行すると停止します。仮の0や空文字を自動設定しているわけではありません。

## 3. まずはsampleで動かす

`Scenarios.cs`の`Sample`で、このケースに必要な入力を設定します。

- `AcceptInputs`：ACCEPTが順に消費する文字列。分岐や繰り返しを考慮して並べます。
- `Arguments`：PROCEDURE USINGに対応する正式な引数。LINKAGEはここへ設定します。
- `InitialState`：試験用のWORKING-STORAGE初期値。必要な場合だけ設定します。

次に`Stubs`を編集します。外部CALL・FILE・SQLには明示的な応答を用意し、未対応の内部文にはその文の処理を実装します。内部文を単に空処理にすると、元の処理を再現したことにはなりません。

新規生成されたsampleは、外部依存を省略してExecutorを呼ぶため、編集したStubsを使用します。実際の型名は生成されたソースをそのまま使ってください。

```csharp
// 生成シナリオ内の呼出し。inputはその上で設定します。
return new ActualGeneratedExecutor().Run(input, options: options);
```

上の`ActualGeneratedExecutor`は説明用の名前です。

補完したら次のコマンドで実行します。

```sh
dotnet run --project out/payment -- PAYMENT --scenario sample
```

`PAYMENT`にはCBLの`PROGRAM-ID`を指定します。ファイル名ではありません。`--`より後は生成プログラムに渡す引数です。正常終了時には表示・最終状態などがJSONで出力されます。

CobolCS経由で、管理ファイルの検査とビルドを行ってから起動する方法もあります。生成先に1プログラムだけなら`--program`は省略可能です。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- run out/payment --scenario sample
```

生成された`Program.cs`を直接起動する場合は、1プログラムでも`PROGRAM-ID`を指定します。また、`--scenario sample`を省略するとfixture経由の実行になり、Scenarios.csに書いた入力は使われません。

## 4. ケース別の応答を使う場合

| 選択 | 入力の編集先 | 外部依存の設定先 |
|---|---|---|
| `--scenario sample` | `Scenarios.cs` | `Stubs/*.cs` |
| `--scenario sample-scripted` | `ScriptedScenarios.cs` | `ScriptedDependencies`へのExpect登録。ResponseExamplesを利用できる |

2つのシナリオの入力は別々です。sampleに入力を記入してもscripted側へ自動反映されません。

scripted側では、必要な応答記入例を補完し、そのケースが消費する順に登録して実行します。ResponseExamplesのメソッドは自動登録されません。

```sh
dotnet run --project out/payment -- PAYMENT --scenario sample-scripted
```

fakeの応答不足・順序違い・matcher不一致・余剰応答はエラーです。編集済みStubsへ自動的に切り替わることはありません。未対応の内部文は、引き続き該当スタブの実装が必要です。

## 5. 動いた結果をテストで固定する

```sh
dotnet test out/payment/Tests/CobolCS.Generated.Tests.csproj
```

通常generateで探索・固定再生に成功した自動テストは、未レビューのC#挙動固定として実行できます。手動の未記入sampleはTests/Templatesにあり、自動発見しません。採用前にはbaselineの由来、具体的なreviewReason、Tests/review.jsonのreviewedと関連ファイルhashを整えてください。現在の実行結果をそのまま元COBOLの正解とみなさず、独立した期待値か、現在のC#挙動を固定するものかを区別します。

手動テンプレートはsampleを呼びます。Tests/Casesへ有効化した手動fakeのケースはScriptedScenarios.Sampleへ切り替え、入力・応答設定もレビュー対象へ含めます。sample側では使用するStubsも対象です。更新項目の比較雛形は検査候補であり、経路網羅の証明ではありません。

レビュー済みテストが通ったら、別ディレクトリへadoptします。

```sh
dotnet run --project src/CobolCS.Cli --no-build -- adopt out/payment --output adopted/payment
```

adopt後は処理本体の`.g.cs`も編集でき、同じテストを使いながら業務処理を抽出できます。レビューや期待値の詳細は[実行ワークフロー](execution-workflow.md)、補完済み合成例の再作成は[実行準備のデモ](execution-readiness.md#最短の合成デモ)を参照してください。デモ補完ツールは指定の合成例専用で、任意のCBLの業務値を推測するものではありません。

## 止まったときの確認先

| 状況 | 確認すること |
|---|---|
| `Preparation.Required`で停止 | メッセージにある入力・応答・期待値が未記入 |
| スタブのNotImplementedException | 原文位置とreports/stubs.jsonから、到達した実装箇所を確認 |
| `Missing or out-of-order response` | scripted側のExpect登録と実際の呼出し順を確認 |
| スタブを直したのに使われない | sample-scriptedを選んでいないか、旧sampleがfakeを注入していないか確認 |
| Scenarios.csの入力が使われない | `--scenario sample`を付けているか確認 |
| `Unknown PROGRAM-ID` | CBLのPROGRAM-IDと、生成先READMEの型対応を確認 |
| 再生成しても新しい雛形にならない | ユーザー領域の保持は仕様。新しい出力先で比較して必要部分を移す |
| 再生成後にsample-scriptedが見つからない | 旧出力では自動登録を控える場合がある。execution-readiness.mdの移行案内に従う |
| 管理ファイル変更のエラー | 生成管理中の本文を編集していないか確認。本文のリファクタリングはadopt後に行う |

旧シナリオのスタブ実行への切替は、入力を維持し、Executor.Runからexternalのfake引数を外して`options: options`を渡す形にします。既存レビュー・期待値は自動更新されないので、変更したケースを改めて確認してください。

通常の source `generate` は分岐探索と固定MSTestも出力します。未レビューの自動期待値は C# の挙動固定として実行でき、adopt にはレビューが必要です。未記入sampleは `Tests/Templates/` に置きます。出力先・有限予算・手動候補・未到達理由・再生成時の保持は[分岐探索とMSTest](branch-test-generation.md)を参照してください。
