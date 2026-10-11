# 独立レビュー候補の再現と回帰

比較元は PR #3 の `5234e2b84b4e93983c4d7abb0764a44a5df91953`。
以下は実際の CLI 生成と dotnet ビルド／実行で確認した。COBOL 実機結果を取得したという意味ではない。

## 1. 入れ子の一時変数

`01 N PIC 9.` に対して、次の3本はいずれも C# ビルドが `CS0136` になった。

```cobol
EVALUATE N WHEN 0
  EVALUATE N WHEN 0 DISPLAY "ok" END-EVALUATE
END-EVALUATE GOBACK.
```

```cobol
ADD 10 TO N ON SIZE ERROR
  ADD 1 TO N END-ADD
END-ADD GOBACK.
```

```cobol
CALL "A" USING N ON EXCEPTION
  CALL "B" USING N END-CALL
END-CALL GOBACK.
```

source 生成器で subject / sizeError / response と外部応答の格納位置を一意化した。
Bank-of-Z の BANKDATA で同じ `control` 衝突も確認し、未解決制御とヘッダースタブの変数も一意化した。
回帰はビルドだけでなく、入れ子分岐の表示・更新結果と CALL の配列参照更新を比較する。

```sh
python3 tests/regression.py Regression.test_nested_source_temporaries Regression.test_nested_call_response_and_reference_temporaries Regression.test_unresolved_flow_and_header_stubs_have_unique_control_names
```

## 2. 編集済みスタブの誤接続

同型の `CALL "A" USING N` と `CALL "B" USING N` を生成し、C# スタブを編集した後で CALL の順序を交換した。
PR #3 では再生成が成功し `contractChanged=false` のまま、同じ連番メソッドへ旧実装が保持されることを確認した。

新実装は、文・原文位置・依存先・引数等を含むスタブ記録の hash と初期スタブ本文の hash を manifest に保存する。
同じファイル名に違う意味の契約が割り当たり、そのファイルが編集済みなら、成果物の入替え前に再生成を拒否する。
削除後に残したスタブも結び付けを保持する。旧版は初期 hash がないため保守的に扱う。
単なる型一致や `contractChanged` 通知だけで、旧実装を実行可能な状態へ自動で結び直さない。

同じ入力での編集保持、異なる CALL／引数での拒否、拒否時に全成果物のバイトが変わらないことを検証する。
利用者は新規出力へ生成し、契約を見て明示的に移植する。原文位置のみの変更も拒否する保守的な制約がある。

```sh
python3 tests/regression.py Regression.test_call_stub_replacement_and_regeneration Regression.test_edited_stub_reordering_is_refused_atomically
```

## 3. NOT ON SIZE ERROR 単独

```cobol
COMPUTE N = 10
  NOT ON SIZE ERROR DISPLAY "WRONG"
END-COMPUTE GOBACK.
```

`N PIC 9` で PR #3 は実際に `WRONG` を表示した。source 生成器が NOT 側の存在でも桁あふれを検査するよう変更した。
桁あふれ／ゼロ除算で NOT 側へ進まないこと、格納可能な値では NOT 側へ進むことを、段落あり・なしで検証する。
ON SIZE ERROR がない場合の異常な受信値は未定義のため、COBOL 同等性の期待値にしない。
根拠は [Fujitsu の規則 6–7](https://bs2manuals.ts.fujitsu.com/psCOBOL2000V16en/on-size-error-phrase-cobol-compiler-reference-manual-2022-11-197-22036903.html)。

```sh
python3 tests/regression.py Regression.test_not_on_size_error_only
```

## コーパスから見つかった追加事項

- FILLER の fixture ラベル重複 `CS0152`。初期化は残し、名前付き fixture の対象から除外。
- 完全修飾名が重複する項目も fixture で勝手に選ばず拒否し、snapshot はシンボル ID を添えて両方残す。
- 番号で始まる段落名、文書用ヘッダー、固定 FD メタデータ、ADD GIVING、PROCEDURE USING の区切りを対応。
- 解析プログラムがゼロ／profile 拒否でも、スタブ一覧が存在すると仮定せず診断を残す。

標準 CI の全回帰と公開コーパスジョブが上記を含む。各工程の件数は [実測資料](public-corpus-results.md) を参照。
