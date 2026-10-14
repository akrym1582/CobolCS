# config.json の全設定

`--config path/to/config.json` に渡す JSON のリファレンスです。ファイル名は任意です。トップレベルの全23プロパティ、`testGeneration` の全5プロパティ、手動候補の全5プロパティを以下に示します。

設定の受理は、その値に対応する COBOL の実行や C# 生成を保証しません。解析用・予約用の値もあります。生成の制約は「組み合わせと実装範囲」を参照してください。

## ファイルの規則と適用先

- UTF-8 の JSON オブジェクトを指定します。コメントと末尾カンマは使えません。
- キーと列挙文字列は大文字・小文字を区別します。未知キーはトップレベル、探索設定、手動候補の各階層で拒否します。
- 省略したトップレベル項目には下記の既定値が入ります。通常の列挙項目と数値項目には `null` を指定できません。
- `generate`（入力ファイルを直接渡す形式を含む）と `analyze` は設定全体を受け付けます。`explore DIRECTORY --config path` では `testGeneration` だけを指定します。解析・実行の設定変更は再生成で行います。
- `--syntax-recovery standard|tolerant`、`--backend`、`--mode`、`-o`、`-I` は CLI オプションです。例えば `"syntaxRecovery"` を config.json に書くと未知キーとして拒否されます。IR・manifest に保存される解析方針メタデータと設定ファイルは別の契約です。
- 設定は既定値を合成し、キー順を正規化して `configHash` に記録します。COBOL の `DECIMAL-POINT IS COMMA` と `CURRENCY SIGN IS` はそれぞれ `decimalPoint` と `currencySymbol` を上書きし、最終設定に反映されます。

CLI の詳細は [CLI 仕様](cli.md)、探索の運用は [分岐探索と xUnit](branch-test-generation.md)を参照してください。

## トップレベルの全プロパティ

表中の候補は JSON の値です。文字列には引用符、真偽値・数値・`null` には引用符を付けません。

| プロパティ | 指定できる値・型 | 既定値 | 意味・現在の適用範囲 |
| --- | --- | --- | --- |
| `profile` | `"portable-test"`, `"fujitsu-review"` | `"portable-test"` | 移植可能な観測用設定、またはホスト依存のレビュー用設定。後者の明示必須項目と生成制約は下記参照。 |
| `sourceFormat` | `"free"`, `"fixed"` | `"free"` | ソースの行形式。fixed は1〜6列を連番、7列を標識、8〜72列を本文、73列以降を識別領域として扱う。 |
| `sourceEncoding` | `"utf-8"`, `"cp932"`, `"shift_jis"` | `"utf-8"` | COBOL ソースの復号方式。cp932 は Windows 拡張を含み、shift_jis はその拡張を含まない厳密な方式。データ項目の符号化とは別。 |
| `fixedColumnPolicy` | `"unicode-ascii"`, `"unicode-columns"` | `"unicode-ascii"` | fixed の列は Unicode コードポイントで数える。unicode-ascii は非コメント行の非 ASCII に `FIXED_JAPANESE_COLUMNS` 診断を出す。unicode-columns はこの注意を抑制するが、ベンダーのバイト列・全角列規則との一致は保証しない。 |
| `dataEncoding` | `"ascii"`, `"fujitsu-ebcdic-kana"` | `"ascii"` | データの符号化プロファイル。現在の C# 生成は ascii のみ許可。 |
| `nationalEncoding` | `"utf-16be"`, `"utf-16le"` | `"utf-16be"` | NATIONAL 格納方式の指定。現在は設定・来歴として保持し、独立ランタイムの格納方式を切り替える実装はない。 |
| `collation` | `"ascii"`, `"host-byte-order"` | `"ascii"` | 照合順序のプロファイル。現在の C# 生成は ascii のみ許可。 |
| `binaryLayout` | `"big-endian"`, `"little-endian"` | `"big-endian"` | バイナリ格納方式の指定。現在は設定・来歴として保持し、ホストのバイト配置を再現する実装はない。 |
| `signStorage` | `"leading-separate"`, `"trailing-overpunch"` | `"leading-separate"` | 符号格納プロファイル。overpunch は EBCDIC kana との組み合わせが必須。現在の C# 生成は leading-separate のみ許可。 |
| `arithmeticProfile` | `"exact-rational"`, `"approximate-math"` | `"exact-rational"` | 算術プロファイル。現在の C# 生成は厳密有理数の exact-rational のみ許可。 |
| `controlFlowMode` | `"native"`, `"stub-control"` | `"native"` | native は対応する制御を生成。stub-control は EVALUATE や段落を呼ぶ out-of-line PERFORM を制御スタブに保持。複合 EVALUATE は native でもスタブ。 |
| `sqlcaInclude` | `"none"`, `"portable-minimal"` | `"none"` | portable-minimal は検索先にない SQLCA INCLUDE に最小の移植用定義を補う。SQL 接続・SQL 実行は提供しない。 |
| `p64` | `true`, `false`, `null` | `null` | `$IF P64 SET` の明示値。ホストのビット数から推測しない。未指定の条件は不明として保持し、レイアウトが曖昧になる場合は拒否。 |
| `whenCompiled` | 任意の JSON 値（未検証の予約項目） | `null` | 現在は値を検証せず設定・ハッシュに保持するだけ。日時形式の契約はなく、値を指定しても WHEN-COMPILED の native 実装は有効にならない。通常は null のままにする。 |
| `redefinesSizePolicy` | `"equal-fixed"`, `"max-family"` | `"equal-fixed"` | REDEFINES サイズ方針の予約項目。現在は設定・来歴として保持し、格納領域の方針を切り替える実装はない。 |
| `decimalPoint` | `"dot"`, `"comma"` | `"dot"` | 数値リテラル・PIC の小数点規則。COBOL ヘッダーの指定が優先。 |
| `currencySymbol` | Unicode コードポイント1個の文字列 | `"$"` | 例 `"$"`, `"¥"`, `"€"`。ASCII の数字、`.`、`,`、`+`、`-`、半角スペースは禁止。空文字と2文字以上も不可。COBOL ヘッダーの指定が優先。 |
| `mathPrecision` | 整数 `1`〜`100` | `34` | 数学精度の予約値。現在の exact-rational 計算をこの桁数で丸める実装はない。 |
| `maxSortBytes` | 正の整数 | `16777216` | SORT バイト数上限の予約値。現在は設定・来歴として保持し、SORT 処理のメモリー上限として消費する実装はない。 |
| `maxRecords` | 正の整数 | `100000` | レコード数上限の予約値。現在は設定・来歴として保持し、実行レコード数を制限する実装はない。 |
| `maxSteps` | 正の整数。実用範囲 `1`〜`2147483647` | `1000000` | 1回の生成 C#／インタープリター実行のステップ上限。下記の32ビット制限を参照。 |
| `timeoutMs` | 正の整数。実用範囲 `1`〜`2147483647` | `10000` | 1回の実行の時間上限（ミリ秒）。探索全体の時間上限とは別。下記の32ビット制限を参照。 |
| `testGeneration` | オブジェクト（下記参照） | 省略 | 生成・再探索の有限探索予算と手動候補。プロパティ自体の null は拒否。 |

トップレベルの5つの数値項目は、JSON 数値を正の整数表記で指定します。`"1000"`、`1.0`、`1e3`、`0`、負数は拒否されます。設定の検証では `mathPrecision` 以外に上限を設けていませんが、実行側の `maxSteps` と `timeoutMs` の読取は符号付き32ビットです。`2147483647` を超える値は設定として受理されても実行側で既定値に戻るため、実用範囲を守ってください。

## 組み合わせと実装範囲

`fujitsu-review` は次の7項目を**すべて明示**する必要があります。既定値と同じ値でも省略できません。

`fixedColumnPolicy`, `dataEncoding`, `nationalEncoding`, `collation`, `binaryLayout`, `signStorage`, `arithmeticProfile`

`signStorage: "trailing-overpunch"` は `dataEncoding: "fujitsu-ebcdic-kana"` の場合にだけ受理します。

現在の C# バックエンドは、以下のすべてを満たす場合にだけ生成できます。source／interpreter、strict／scaffold のいずれにも適用され、満たさない場合は `CS_PROFILE` で拒否します。許容解析を有効にしてもこの制約は解除されません。

| 項目 | C# 生成で必要な値 |
| --- | --- |
| `profile` | `"portable-test"` |
| `dataEncoding` | `"ascii"` |
| `collation` | `"ascii"` |
| `signStorage` | `"leading-separate"` |
| `arithmeticProfile` | `"exact-rational"` |

それ以外の受理されるプロファイルは解析・レビュー用です。予約項目に値を指定しても、未対応の NATIONAL、バイナリ配置、REDEFINES、SORT 等の実行対応が増えるわけではありません。構文ごとの範囲は [対応範囲](supported-subset.md)を参照してください。

## testGeneration の全プロパティ

`testGeneration` を省略しても以下の実効既定値で探索します。`{}` も指定できます。各子プロパティの省略・`null` は実効既定値となりますが、指定したオブジェクトや null は正準設定に残るため、設定ハッシュは省略時と異なります。

| プロパティ | 指定できる値・型 | 実効既定値 | 意味 |
| --- | --- | --- | --- |
| `maxAttempts` | 整数 `1`〜`200` または null | `200` | プログラムごとの試行上限。確認用の2回の固定再生も予算に含む。 |
| `seed` | 整数 `0`〜`2147483647` または null | `1` | 有限探索の乱数 seed。 |
| `timeoutMs` | 整数 `1000`〜`600000` または null | `120000` | 探索全体の時間上限（ミリ秒）。対象プログラム間で共有する。ビルド・テスト等も残時間で制限する。 |
| `maxTraceEvents` | 整数 `1`〜`100000` または null | `10000` | 試行の分岐 trace イベント上限。不完全 trace を正常期待値に昇格させない。 |
| `candidates` | 最大200要素の配列または null | 候補なし（`[]` 相当） | 以下の形式の手動候補。空配列可。 |

整数項目は JSON の32ビット整数です。文字列・小数・真偽値は使いません。探索 worker の最大32試行ごとの回収等は内部仕様であり、設定プロパティではありません。

### candidates の各要素の全プロパティ

| プロパティ | 指定できる値・型 | 必須・既定 | 意味 |
| --- | --- | --- | --- |
| `programId` | 文字列 | 必須 | 対象の PROGRAM-ID。実在する対象を指定する。設定読込時は空文字も受理するが、探索時に対象と照合する。 |
| `input` | オブジェクト | 必須 | 生成された公開入力型の JSON。以下の表を参照。空オブジェクトも設定としては受理する。 |
| `responseMode` | 整数 `0`, `1`, `2`, `3` | 省略時 `0` | 外部処理に共通の合成応答モード。以下参照。 |
| `responseModes` | オブジェクト | 省略時 上書きなし | `"Dependency_<statementId>"` をキー、整数 `0`〜`3` を値として文ごとのモードを上書き。例 `"Dependency_s000007": 1`。 |
| `responseValues` | オブジェクト | 省略時 上書きなし | 許可された外部応答項目の symbol ID をキー、文字列を値として指定。例 `"d000003": "42"`。 |

設定読込時に検証するのは候補のキー、`programId` の文字列型、`input` のオブジェクト型です。入力の詳細、応答の型・範囲・ID は探索時にも検証します。設定として読み込めても探索で不正になる候補は `invalidManualCandidates` 等に記録し、成功ケースにしません。statement ID、symbol ID、メンバー名は対象の `reports/branch-plans.json` と生成 API から取得してください。上の ID は形式例です。

### input の指定内容

`input` のキーは生成 API の綴りを使います。通常の候補は次の3項目を使い、任意の業務項目をトップレベルへ追加しません。

| 入力項目 | 値の形式 | 制約 |
| --- | --- | --- |
| `AcceptInputs` | 文字列配列 | ACCEPT に渡す値を消費順で指定。例 `["12", "YES"]`。 |
| `Arguments` | オブジェクト | 正式 LINKAGE 入力。キーは公開 API／branch-plans の `member`。固定グループは完全な入れ子オブジェクト、固定表は宣言順の完全な値配列。 |
| `InitialState` | オブジェクト | WORKING-STORAGE の試験用初期値。キーは `member`。LINKAGE は指定不可。表は `[{"Subscripts":[1],"Value":"12"}]` の形式。添字は外側から順に1始まり。 |

数値項目は `"12.34"` 等の数値文字列、または `{"numerator":"1","denominator":"3"}` の厳密有理数オブジェクトを使います。文字項目は文字列です。COBOL の原名や symbol ID は入力のメンバーキーではありません。未知メンバー、不完全な固定グループ、表の重複添字・範囲外・次元数不一致を拒否します。既知の基本項目・完全な固定グループ・固定表が対象であり、未知レイアウトを推測しません。数値は対象 PIC の符号・桁・小数桁の制約に従います。

### 外部応答のモードと値

| モード | CALL | FILE の読み込み | SQL |
| --- | --- | --- | --- |
| `0` | 正常 | 正常 | 正常 |
| `1` | 異常 | 即 EOF | no-data |
| `2` | 正常 | 異常 | 異常 |
| `3` | 正常 | 2レコード後 EOF | 正常 |

読み込み以外の FILE は正常応答になります。FILE STATUS、SQLCODE、SQLSTATE、SQL indicator 等の状態出力は契約から合成するため、`responseValues` の業務値キーには指定しません。

`responseValues` は `branch-plans.json` の `responseItems` で許可された既知の基本項目に限ります。BY REFERENCE の固定グループは基本項目へ展開し、グループ本体、88レベル条件名、未知項目、BY VALUE／BY CONTENT だけの入力項目は指定しません。対応する表の基本項目への応答値は各要素に同じ値を適用します。数値は文字列で対象 PIC の符号・桁・小数桁に正確に収まる必要があり、文字列には長さ・文字コードの制約があります。

## 設定例

### 全トップレベル項目と探索の既定値

以下は各項目の値を明示した例です。`testGeneration` を明示するため、設定ファイルを省略した場合とは設定ハッシュが異なります。

```json
{
  "profile": "portable-test",
  "sourceFormat": "free",
  "sourceEncoding": "utf-8",
  "fixedColumnPolicy": "unicode-ascii",
  "dataEncoding": "ascii",
  "nationalEncoding": "utf-16be",
  "collation": "ascii",
  "binaryLayout": "big-endian",
  "signStorage": "leading-separate",
  "arithmeticProfile": "exact-rational",
  "controlFlowMode": "native",
  "sqlcaInclude": "none",
  "p64": null,
  "whenCompiled": null,
  "redefinesSizePolicy": "equal-fixed",
  "decimalPoint": "dot",
  "currencySymbol": "$",
  "mathPrecision": 34,
  "maxSortBytes": 16777216,
  "maxRecords": 100000,
  "maxSteps": 1000000,
  "timeoutMs": 10000,
  "testGeneration": {
    "maxAttempts": 200,
    "seed": 1,
    "timeoutMs": 120000,
    "maxTraceEvents": 10000,
    "candidates": []
  }
}
```

### CP932 の固定形式と P64 の明示

```json
{
  "sourceFormat": "fixed",
  "sourceEncoding": "cp932",
  "fixedColumnPolicy": "unicode-columns",
  "p64": true
}
```

```sh
dotnet run --project src/CobolCS.Cli --no-build -- input.cbl --config config.json --syntax-recovery tolerant -o out/app
```

### 再探索の予算と手動 ACCEPT 候補

`programId` と ACCEPT の順序・値は対象プログラムに合わせます。この例は `explore` にも渡せるよう、`testGeneration` のみを記載しています。

```json
{
  "testGeneration": {
    "maxAttempts": 50,
    "seed": 7,
    "timeoutMs": 60000,
    "maxTraceEvents": 10000,
    "candidates": [
      {
        "programId": "PAYMENT",
        "input": { "AcceptInputs": ["12"] },
        "responseMode": 0,
        "responseModes": {},
        "responseValues": {}
      }
    ]
  }
}
```

```sh
dotnet run --project src/CobolCS.Cli --no-build -- explore out/app --config config.json
```

### ホスト依存の解析・レビュー用（C# 生成不可）

```json
{
  "profile": "fujitsu-review",
  "fixedColumnPolicy": "unicode-columns",
  "dataEncoding": "fujitsu-ebcdic-kana",
  "nationalEncoding": "utf-16be",
  "collation": "host-byte-order",
  "binaryLayout": "big-endian",
  "signStorage": "trailing-overpunch",
  "arithmeticProfile": "exact-rational"
}
```

## 実装上の参照先

- [FrontendConfiguration.cs](../src/CobolCS.Frontend/FrontendConfiguration.cs): トップレベルの既定値、候補、組み合わせ、正準ハッシュ。
- [BranchGenerationConfiguration.cs](../src/CobolCS.Frontend/BranchGenerationConfiguration.cs): 探索設定のキー・型・範囲と手動候補の基本検証。
- [Validation.cs](../src/CobolCS.Cli/Validation.cs): C# 生成プロファイルの制限。
- [BranchTestGeneration.cs](../src/CobolCS.Cli/BranchTestGeneration.cs): 実効探索既定値、応答・入力の検証と試行予算。

設定を追加・変更する際は、この一覧・例・CLI の説明を同時に更新してください。
