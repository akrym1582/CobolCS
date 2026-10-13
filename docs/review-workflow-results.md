# レビュー修正・業務ケース支援の実装記録

採用した開始SHAは `ef554f8851bc10c162a4e0007540186e4eca7a2c`。初期チェックアウトの `8a516efb3aa7328fb4549f710c9cc2032ae2e521` は前提機能がないため、未コミット変更がないことを確認して origin/main を取得し、fast-forwardした。

最終の測定対象実装は `77c9cf8fa59ed6a96d6885b593fd0f13e0d3d6ec`。後続の変更は文書・測定記録だけとし、固定worktreeでbefore／afterを評価する。完了コミットはこの記録を含む最終HEADで確認する。

## 実装

| ID | 変更 | 実生成物での検証 |
|---|---|---|
| F1 | BusinessReviewReferenceでfilesとimplementationHashesを分離。全業務契約ゲートを維持し、prepareもケース別未承認行を更新 | paymentで生成参考JSONから明示レビュー→adopt→サービス置換。生成Programsの計算変更でもBusiness成功。参照Dataの改変は失敗。承認と利用者テンプレートを保持 |
| F2 | 初回mappingHashを保持し、最新の一件だけproposalへ保存。基準と一致したらプロパティを削除 | A→B→A／C、B再実行、編集・削除保持、stage競合拒否。新成果物は新contractId、versionだけでは増えない |
| B1 | 入力・結果・操作別応答の未完成構造、Pointer・PIC・固定数・根拠・元位置のguide | scalar、ネスト、固定2要素／101要素、複数操作、nullable応答、未選択正式引数。雛形はdiscovery外で、未記入は成功数に入らない |
| A1 | 任意acceptMode、acceptInputs metadata、共通Inspect／Strict検証、ケース専用BusinessAcceptScript、生成IAcceptInputとAdapter配線 | 反復10.25＋20.50＝30.75、分岐7.25、同symbolの異なる文、両生成経路、同値でも逆順失敗。不足・余剰・未知・PIC外・null・空白・未配線・旧キュー併用を拒否。サービス置換後も同じ契約・ケース・期待値・承認で成功 |
| M1 | RENAMESの保持宣言、参照先と親子関係から独立01を証明。共有／不明／重ね合わせ根は保守拒否 | INSPECT／STRING、構造化／dispatcher、無関係・共有・不明範囲・RENAMESなし。親IDだけでは判定しない |

元COBOLの実機一致は検証していない。paymentの参照Data回帰は、hash検証専用の合成採取資料であり、実機COBOLの採取を意味しない。独立手計算、固定fake、生成、build、Business比較を区別する。未対応の文種、実接続、実行時IR、新しいrunnerを追加していない。

## 検証ゲート

Linux、.NET SDK 10.0.401／Runtime 10.0.12で次を実行した。

| コマンド／照合 | 成功 | 失敗 | skip | 結果 |
|---|---:|---:|---:|---|
| dotnet build CobolCS.sln -m:1 | 1 | 0 | 0 | 警告・エラー0 |
| dotnet format whitespace CobolCS.sln --verify-no-changes --no-restore | 1 | 0 | 0 | 成功 |
| dotnet test tests/CobolCS.Tests --no-build | 47 | 0 | 0 | 成功 |
| dotnet test tests/CobolCS.Frontend.Tests --no-build | 194 | 0 | 0 | 成功 |
| dotnet test tests/CobolCS.IntegrationTests --no-build（全体初回） | 311 | 1 | 0 | 全312行実行。参考JSON追加に対するテストの旧選択条件が1行で複数一致 |
| 修正後の集中回帰（Category指定を含む実生成物） | 13 | 0 | 0 | 成功 |
| 最終E2E・FILE／SQL・所有権回帰 | 9 | 0 | 0 | 旧選択条件を修正した行も成功 |
| 結果キーと独自順序Pointerの境界回帰 | 3 | 0 | 0 | 成功 |
| 最終RENAMES回帰 | 4 | 0 | 0 | 成功 |
| Integrationの最新TRX行照合 | 312 | 0 | 0 | 同じtestIdを照合し、再実行した行だけを採用 |
| git diff --check | 1 | 0 | 0 | 成功 |

全体実行は修正前のテストアセンブリから開始したため、初回の1失敗は残して報告する。失敗した `BusinessExternalScaffoldTests.FileAndSqlUseStrictTypedBusinessFakes` の来歴JSON選択を修正し、9件の再実行で成功を確認した。全体・集中・E2E・境界・RENAMESのTRXを明示した順序で同じtestIdに対応付け、最新の全312行が成功したことを確認した。Theoryの配列引数はfriendly名が省略されて衝突するため、名前だけで行を統合せず、testIdをキーにし、discoveryの重複を含む行集合も照合した。TRX終了時刻だけでは、修正前に開始した全体実行を最新版として採用しない。

親 `MSBUILDDISABLENODEREUSE=0` の条件でIntegrationと生成物E2Eを実行した。xUnit 2.9.3／adapter 3.1.5／VSTest・TRXは維持した。最新のRuntime／Frontend／Integrationは計553件成功、失敗・skip 0。これは初回全体コマンドそのものが成功だったという意味ではない。

[TRX対応と件数](../tests/review-workflow-results/verification.json) と [CI分割](../tests/review-workflow-results/ci-partition.json)を保存した。discoveryの312行はbranch 12、regression-a 34、regression-b 26、generation 53、workflows-a 19、workflows-b 168へ重複・漏れなく入る。追加2クラスはworkflows-bに入る。

原文、観測値、固定期待IR、corpus lock、業務期待値を自動更新していない。生成物の変換係数・文字カウント・ACCEPT順・応答要求の意図的な破壊で失敗することを確認し、期待値へ上書きしなかった。


## 固定実CBL

beforeは基準SHA、afterは測定対象実装の別worktree。SDKは両方.NET 10.0.401。入力・COPY・lockは同一キャッシュを照合し、設定は既存evaluateのsourceFormatのみ、既定の探索予算・fixture集合を変更していない。optional GPLは除外した。

既存Toolsのevaluate --build、carddemo／bankの4分割、merge-evaluations、verify、内部スタブ集計を使用する。原文・大量生成物は外部cache／outに置き、共有記録はhash、工程件数、元位置とreasonCodeだけを保存する。未取得・生成不能・未実行を成功やスタブ0として扱わない。

全171入力を取得・照合した。beforeは5群を通しで評価し、afterは11分割（carddemo／bank各4＋他3）を評価・統合した。両方で5群のverifyが成功。時間値は並行作業の負荷が異なるため性能比較には使用しない。

| コーパス | 入力／取得 | before生成／build | after生成／build | 固定fixture実行／独立一致（両方） | 内部展開／物理（両方） |
|---|---:|---:|---:|---:|---:|
| course | 30／30 | 30／30 | 30／30 | 3／3 | 145／133 |
| fun | 2／2 | 2／2 | 2／2 | 0／0 | 6／6 |
| carddemo | 44／44 | 42／42 | 42／42 | 0／0 | 1200／1170 |
| bank | 43／43 | 43／43 | 43／43 | 0／0 | 1010／1008 |
| db2 | 52／52 | 52／52 | 52／52 | 0／0 | 400／400 |
| 合計 | 171／171 | 169／169 | 169／169 | 3／3 | 2761／2717 |

通常解析の成功は170件（db2の1件は通常解析が成立せずscaffold解析・生成は成立）。生成はstrict 5件＋scaffold 164件で、生成できた全169件をbuildした。既存の有限探索は各生成処理で同じ予算のまま使用し、実到達集計は現在のfingerprintと固定実行証跡に一致するものだけである。固定fixture実行は3件、編集した合成内部スタブ実行は1件、元COBOL実機比較は0件。

内部スタブはbefore／afterとも2761展開文、2717物理文。文位置・reasonCode・executionRole・layoutUnknownの変化は0件、工程の増加／減少、生成済みプロジェクトのbuild失敗、固定fixture実行失敗も0件だった。実到達は内部1文、実測最初の内部停止プログラムは0、未実行は2760文。0停止を全入力の到達安全性と解釈しない。

RENAMESの限定修正は正負の合成fixtureで確認したが、この固定171入力の内部スタブ削減は0件である。過去の171入力や合成3本の5→2を今回のbeforeとして使っていない。

内部スタブを測れない生成不能入力はbefore／afterとも `carddemo-792f46d55ee5` と `carddemo-da1614ca80f4`。未取得入力は0件、全171入力の工程評価は実施済みだが、この2件にスタブ0／生成成功を記録しない。これ以外の生成物でも全経路実行、実接続、実機COBOL同値性は未検証である。

共有JSONは [before](../tests/review-workflow-results/before.json)、[after](../tests/review-workflow-results/after.json)、[比較](../tests/review-workflow-results/comparison.json)。compiler commit／source hash／assembly hash、テンプレートhash、lock／入力／設定hash、工程件数、文種×理由×役割×配置不明の集計、原位置・COPY展開別の候補一覧を含む。原文や観測した業務値は含めない。物理位置はcorpus caseの接頭辞を維持し、異なるcaseを統合しない。

## 次の対応候補

実停止が0件なので、影響プログラム数と静的文数で上位3候補を整理する。実装採用は決定せず、原位置と理由をさらに分解して独立仕様を確定する。

| 候補 | 実停止 | 影響プログラム | 主な理由・展開文 | 独立仕様を確定する範囲 | 見込対象文数 |
|---|---:|---:|---|---|---|
| MOVE | 0 | 126 | MOVE_TYPE 1188、UNSUPPORTED_MOVE 1130（計2318） | 既知scalarのPIC間代入と精密数値→固定文字の変換を文別に区分。編集PIC／グループbyte配置／REDEFINESは別仕様 | 配置不明でない1332文以下の静的上限。実対応集合は未確定 |
| DISPLAY | 0 | 53 | UNSUPPORTED_DISPLAY 156 | 既知の単一値の表記と文字完全一致。未知式・編集PICの原因を区分し、端末の改行／カーソル互換は別検証 | 配置不明でない62文以下の静的上限。実対応集合は未確定 |
| INSPECT | 0 | 43 | CHAR_INSPECT_CLAUSES 66、CHAR_LAYOUT 13、CHAR_PATTERN 2（計81） | BEFORE／AFTERの範囲、CONVERTING、複数操作を分離し、固定ASCII・末尾空白・既存tally等を独立手計算で固定。既存fixtureで到達した1文も仕様照合対象 | 配置不明でない69文以下の静的上限。実対応集合は未確定 |

上限は見積りの確定値でも実到達件数でもない。今回UNSTRINGや未知の文種を追加せず、独立仕様が未確定のものを正常成功へ補っていない。
