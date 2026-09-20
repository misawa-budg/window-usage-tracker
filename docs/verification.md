# 監査対応と検証メモ（2026-09-16）

## 対応した項目

| 監査項目 | 対応 |
| --- | --- |
| F01 保存遅延 | 継続区間を既定15秒ごとにチェックポイント化し、小量バッチもflush |
| F02 終了 | 背景収集の `--stop`、終了ハンドラーの最大3秒待機、定期保存。OS強制終了の完全保証ではない |
| F03 例外後の待機 | producerをfinallyでキャンセル。フック障害も伝播。失敗したバッチの再試行で重複しない |
| F04 未観測時間 | 入力デスクトップ不可・観測中断・時刻逆行を検出し、区間を切り直す |
| F05 プライバシー | タイトル保存はopt-in、イベントのタイトル入り標準出力を廃止 |
| F06 依存 | ネイティブSQLiteを3.53.3に固定し、実ロード版のテストと依存監査を実施 |
| F07 表示 | 5分フィルタ/8件制限を撤去、週と日の整合、Other配色・状態凡例を修正 |
| F08 保存先 | CollectorとViewerの設定/ルート解決を共通化。Portable直接起動にも対応 |
| F09 資料 | README・構成・要件を現状に合わせ、必要な資料だけGit管理へ追加 |

追加: デモDB分離、通常集計から旧seedを除外、デモのActive重複除去、前面ダイアログ収集、最終バケットのクリップ、検索インデックス、区間描画の境界走査、不要なSQL削除、非破壊の配布生成。

## 自動検証

- 監査対応時: ビルドは警告0・エラー0。Collector.Tests 23件、Viewer.Tests 25件の計48件が成功。依存監査では既知の脆弱なパッケージは検出されなかった。
- Collector.Tests: 保存・区間・設定解決・SQLite・Collector/Viewer両方のSQLを検証。ViewerのSQLソースをリンクし、WinUIを起動せず実装そのものをテストする。
- Viewer.Tests: 既存の表示テストに短時間・9アプリ・週表示・1万件の連続区間・ランダムな重複区間と基準計算の比較を追加。
- 収集OS部分とループを分離し、時刻とセッション可用性を注入してスリープ相当の時刻飛び・ロック/復帰・時刻逆行を再現する。
- SQLトリガーによる保存失敗を使い、失敗した30件目を再試行しても30件のままであることを確認する。
- SQLiteは `SELECT sqlite_version()` でも3.53.3以上を確認する。

## 実機で確認した範囲

- Windows 11 / .NET SDK 10.0.401でビルド。
- release.ps1でFD/SCの両構成を空の検証フォルダへ生成（`-NoZip`）。既存の非空出力先はビルド前に拒否することを確認。
- 合成データだけのDBを使い、self-contained ViewerでDEMO表記、24h/1weekの表示を確認。
- 追加の表示モード確認中にユーザーがEscで画面操作を停止したため、そこでUI操作を終了。状態切替の追加実機検証は未完了で、テスト用Viewerは閉じていない。
- Desktopの旧Collector・実DB・自動起動タスクには変更を加えていない。

## 残る制約と未実施の検証

- 実際のPCロック・スリープ・ログオフ・OSシャットダウン試験は未実施。作業中のPCを停止しないため、模擬時刻/セッションでロジックを検証した。特にuser32を使うコンソールのログオフ通知には制約があり、最終flushだけには依存しない。
- 旧Collectorが稼働中のため、新版のライブ収集と `--stop` のプロセス間実機試験は未実施。差し替え後に正常停止・再起動・二重起動を確認する。
- 更新後も過去のタイトルや誤集計済みの区間は書き換えない。必要なデータ移行/削除は別途バックアップと合意が必要。
- 強制終了・電源断・ディスク障害時の無損失は保証しない。15秒は通常時の保存周期であり、OSスケジューリング/ディスク待ちを含む厳密な最大遅延ではない。
- 定期区間保存によりレコード数は増える。自動保管期限・削除・圧縮は未実装。長期容量、CPU、メモリの実測が次の課題。
- `Process detected` はDB保存のheartbeatではない。保存成功時刻の監視は今後の運用改善。
- UIのレイアウト作成はUIスレッドに残る。計算量は改善したが、大量の実データによる長時間負荷試験は未実施。
- ローカル日境界は固定オフセットを使うため、DST移行日の23/25時間表示は未対応（日本の通常利用には影響しない）。
- 無操作時間、ブラウザ内URL、ウィンドウのないプロセス、完全なOSイベント履歴は対象外。
- ZIP圧縮経路・クリーンな別PCへのインストール不要実行・自動CIは今回の検証範囲外。

## 表示名・週表示の追加確認

- `.exe` は表示だけで省略し、識別キーを保持する7ケースを追加。Collector.Tests 23件＋Viewer.Tests 32件の計55件が成功。ビルドは警告0・エラー0。
- 週表示のDB取得・区間計算を旧コードと比較した。条件と3回の結果は [interview-notes](interview-notes.md) を参照。
- 最新コードを `artifacts/viewer-update-20260916` にFD/SCで生成（ZIPなし）。今回の追加変更について実画面の操作試験・クリックから描画完了までの計測は行っていない。
- ローカル検証用にSCバンドルへ `Run-Viewer-With-Desktop-Data.cmd` を追加。Desktopの設定を参照して新版Viewerだけを起動し、旧Collector・設定・DBを置換しない。これはこのPC専用のランチャーで、配布用ソースには含めない。

## Desktop実データでのUI計測と整理（2026-09-16、追加）

### 条件と測定範囲

Desktopの実DB（約41.2 MiB）をReadOnlyで参照。2026-09-10〜09-16の7日間、5,142区間で測定。Collectorは既存プロセスを継続し、DBの複製・書換え・VACUUM・インデックス追加はしていない。比較中の対象件数は同じだったが、稼働中DBなので完全に固定した入力ではない。

変更前は `cdf19d3` 相当のViewerに任意計測だけを加えた `b31f01f`、変更後は `0d65f41`。いずれもRelease / win-x64 / self-contained、同じPC・同じウィンドウ寸法。起動直後の24hを除外し、1weekへの初回切替と更新ボタンによる再読込2回を測定。前→後の順なのでOS/DBキャッシュと実行順の影響は排除していない。

`WINTRACKER_PROFILE_OUTPUT` を指定した場合だけ、`ReloadMeasurement` が時間・件数・visual tree要素数をJSON Linesへ出力する。通常起動時は無効。アプリ名・タイトル・DBパスは計測ログに含めない。明示した出力ファイルへ追記するため、計測後は環境変数を解除する。

| 段階 | 変更前 1 / 2 / 3回目（ms） | 変更後 1 / 2 / 3回目（ms） |
| --- | --- | --- |
| 読込開始〜DB取得後のUI復帰 | 47.57 / 101.56 / 104.97 | 36.77 / 39.58 / 25.43 |
| 区間計算・ViewModel・ItemsSource更新等 | 64.61 / 55.77 / 41.82 | 65.82 / 67.31 / 25.27 |
| 強制UpdateLayout | 1,183.48 / 1,930.35 / 2,492.90 | 39.77 / 24.16 / 27.15 |
| 開始〜最初のRenderingコールバック | 1,369.60 / 2,088.50 / 2,640.02 | 143.21 / 131.76 / 78.40 |
| visual tree要素数 | 各28,561 | 各1,024 |

主な残存ボトルネックはDB取得ではなくXAML生成・配置だった。前回のSQL最適化とは別のボトルネックであり、両者の改善倍率を単純合算しない。

この値には画面の配置まで含むが、GPU present・ディスプレイ表示完了や入力イベント配送待ちは含まない。`UpdateLayout` を診断時だけ明示呼出ししており、自然なフレーム進行への影響もある。最初のコールバックはアニメーション終了も保証しない。そこで最終版では行追加のアニメーションを無効化し、直後と安定後の実画面も別に確認した。「厳密なクリックから表示完了まで」「全PCで常に0.1秒」とは主張しない。

初回24hの参考値は、変更前543ms、変更後439ms。週の温まった状態より初回DB/ランタイム処理が大きい。これはプロセス起動全体の時間ではない。

### 変更と検証

- 一覧タイムラインの区間別の入れ子ItemsControlを、色ごとのPathとRectangleGeometryへ変更。時刻の小数座標・欠測・色・総時間を保ち、間引きは行わない。マウス位置の詳細検索は二分探索。
- 常に非表示だった旧週UI、対応するViewModel・コレクション・変換、機能のないカード拡大アニメーションを削除（この整理だけで445行削除、3行追加）。区間計算の未参照privateメソッドとSharedPlaceholderも削除。削除したコードはGitから復元可能。
- 通常Collectorのログは起動・停止・異常系が中心で、イベントごとの大量出力は現状ない。障害原因のログや明示的なreportコマンドの出力を、単に行数削減のため消してはいない。
- 追加10ケースで詳細検索の半開区間・境界・ゼロ幅・空・非有限座標・nullを検証。Collector.Tests 23件 + Viewer.Tests 42件 = 65件成功。通常Releaseビルドは警告0・エラー0。
- 実画面で24h/1week、週の更新、Running/State詳細切替、区間ツールチップ、Tabで行内へ移動後の右矢印による詳細表示を確認。行の縮小/拡大をなくしても時間と凡例が対応していることを確認。
- 通常計測を無効化した起動口は、ローカル専用の `artifacts/week-optimized-20260916/Run-Viewer-With-Desktop-Data.cmd`。Desktop版そのものは上書きしない。

### 静的解析の扱い

標準Roslyn/.NET解析を利用。`dotnet format ... analyzers/style --verify-no-changes --diagnostics IDE0051 IDE0052 --severity info` の限定実行では指摘なし。そのため未使用判断は検索・XAML参照・ビルドも併用した。

さらに `dotnet build ... -p:AnalysisLevel=latest-all -p:EnforceCodeStyleInBuild=true -p:ErrorLog=<local path>` を一時指定。ViewerとSharedの全規則ビルドでは54警告、0エラー。通常のビルド規則とは別で、全件解消済みではない。新規TrackHitTestのnull検証は追加済み。

- CA2007: UI更新を続けるawaitに機械的なConfigureAwait(false)を入れない。
- CA5392 / CS0169: パッケージ提供・XAML生成コードの箇所を識別し、生成物を直接編集しない。
- CA1305: 保存時刻のParseのカルチャ明示は今後の修正候補。表示時刻のローカライズと保存形式の解析を分ける。
- CA1031: 最上位UIのエラー表示と、汎用catchによる原因隠蔽を別に評価する。
- APIの可視性、static化、命名規約、AOT助言は対象要件に照らして選ぶ。警告をゼロにするためだけの大規模改名やNuGet追加は行わない。

長期間のヒープ/ネイティブメモリ増加、百万件級DB、別PC、実Collectorの運用試験は引き続き未検証。

根拠: [WinUIのXAML配置最適化](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-xaml-layout)、[Renderingイベントと購読解除](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.compositiontarget.rendering?view=windows-app-sdk-1.7)。

## 状態規則の統一と3プロジェクトの整理（2026-09-16、追加）

- ユーザー判断によりActive > Open > Minimizedへ統一。定義はSharedのAppStatePriorityに集約。Open/Minimizedの列挙順逆転、Active優先、全最小化、別アプリの独立性を検証し、同じケースのCollector集約とViewer重複解決が一致することを確認した。
- 新Collectorで収集するデータから新規則を適用する。稼働中のDesktop Collector・既存DB・自動起動設定は変更していない。実運用に反映するには、停止・バックアップを含めてCollectorを新ビルドへ切り替える必要がある。過去の集約前ウィンドウ状態は復元できない。
- Shared: 旧バケット描画APIと専用ヘルパー・モデルを削除。TimelineLayoutBuilderは約1,800行から837行に縮小。現在使う区間計算の本体は維持した。
- Viewer: QueryTimeline/QueryActiveIntervalsは実画面から呼ばれないため削除。SQL取得クラスは82行になり、QueryStateIntervalsの1経路に絞った。SQLテストは現行APIへ移し、デモ除外・期間クリップ・入力検証を維持した。
- Collector: AppIntervalの重複フィールドと読まれない終了時刻を廃止し、Tracker内部の開始時刻＋最新Snapshotへ整理。状態が同じ間は開始時刻・exe表記を保ち、PID/handle/titleのみ最新にする既存の挙動もテストした。
- Collectorのreportで使う時間バケット集計は削除していない。停止制御・チェックポイント・イベントキュー・セッション判定・異常系テストも維持。NuGet追加やUIフレームワーク移行は行っていない。
- 旧描画API専用17テストを外し、現行方式の色・幅・順序・欠測・競合に7テストを追加。状態統一とTrackerに9ケースを追加し、最終結果はCollector 32件＋Viewer 32件＝64件成功。前回65件から件数が減るのは、使われなくなった仕様のテストを撤去したため。
- 固定seedの合成2,000区間で、現行のアプリ名・凡例・日/週の一覧・アプリ別モデルをJSON化して整理前後を比較。SHA-256は双方 `050C3EA9C33C56AF4600B9BC878216BA078A71CC95FA1ABE66F720650EA74F25` で一致。同一環境での回帰比較であり、時刻ラベルを含むため全タイムゾーン共通のgolden値ではない。
- release.ps1でFD/SC両方を `artifacts/refactor-20260916` へ生成。Viewerは警告0・エラー0。Desktopへの差替え、今回の再UI操作・再性能計測・実Collector起動停止は未実施（今回XAML/描画コントロールは変更していない）。
- 削除コードはGit履歴から復元可能。今後の整理は、UI依存の色変換、日境界/DST、日時解析のカルチャ、reportの集計契約など、要件とテストを確認して進める。行数だけを目標に、異常系や責務分離を削らない。

## Viewerの表示整理（2026-09-16）

- 二段構成と集計方式は維持。「最前面アプリの推移」と「アプリ別のウィンドウ記録」に見出しを分け、後者は複数アプリの時間が重なることを説明した。
- 今日／直近7日、状態をまとめる／状態別に見るへ表示用語を整理。ComboBoxのTagを判定に使い、表示文言の変更で期間・表示モードが変わらないようにした。Active/Open/Minimized/Runningの保存・計算上のキーは変更していない。
- 大きな製品名・期間カード・凡例や行の入れ子カードを撤去。日付と操作を上部に固定し、28pxの帯＋細い行区切りに整理。アプリの色分けとツールチップ、上段のキーボード操作は維持した。
- 時間軸は共通DataTemplateと4等分Gridにし、6時・18時を固定100pxずらす補正を削除。上下の軸と行は同じ列幅を使う。凡例は横一列固定から折り返しへ変更した。
- 読込件数は更新時刻のツールチップへ移し、記録ゼロ・読込エラーは文言で表示。Collector起動中はプロセス検出であり、保存成功や記録の新鮮さを保証しないことを補足した。
- 使われなくなったカード背景・影・状態色・角丸・余白のリソースを撤去。外部依存の追加、WPF移行、CollectorやDesktop配布版への変更は行っていない。
- Collector 32件＋Viewer 36件＝68件成功。追加4件はXAMLの選択Tag、軸と行の列幅、四分点の配置を検証する静的な契約テストで、実画面テストの代用ではない。ViewerのRelease/self-containedビルドは警告0・エラー0。
- ユーザーの「画面操作は後にしてほしい」に従い一時停止し、再開許可後に最終版を起動。1426×746のダーク表示・合成DBで、日付・期間・更新操作、上下の時間軸、上段の1行、下段の5アプリが同時に表示され、見切れがないことを確認。最下部の欠測説明はスクロールが必要。最初に取得された別アプリの画像は検証証拠に採用していない。
- 自動入力は `SendInput sent 0 of 1 events; GetLastError=87` および入力競合で失敗。その後ユーザーが手動で直近7日・状態別表示へ切替。取得した画面で、期間が9/10〜9/16に変わり、選択アプリの7行・状態の濃淡・凡例・合計・欠測説明が表示されることを確認した。スクロール後も上部の日付・操作欄は維持。自動操作成功とは扱わない。
- 最小サイズ・ライト表示・実データの多数アプリ時の凡例折り返し、ツールチップ・キー操作の回帰確認と性能再計測は未実施。合成5アプリでの視覚確認を、全条件の検証済みとは扱わない。
- 最終確認用ビルド: `artifacts/visual-cleanup-20260916-final/viewer`。同じフォルダの `Run-Viewer-With-Desktop-Data.cmd` はDesktop DBを読み取り専用で開くViewerのみを起動し、Collectorの差替えや起動・停止を行わない。`Run-Viewer-With-Existing-Demo.cmd` は既存の合成DBを利用し、seedや置換は行わない。

## 表示注釈の削除（2026-09-16）

- ユーザー指示で上段の説明・下段の説明・最下部の欠測注釈を画面から削除。状態の意味と測定の限界はarchitecture/interview-notesに残す。見出し・凡例・集計ロジックは変更していない。
- Viewerテスト36件成功、Release/self-containedビルドは警告0・エラー0。今回の変更版は `artifacts/annotation-cleanup-20260916/viewer`。同じフォルダの `Run-Viewer-With-Desktop-Data.cmd` は既存のDesktopデータでViewerのみを開く。起動中のViewer・Desktop配布版は差し替えておらず、削除後の実画面は未確認。
- セクションの枠・背景色は検討のみ。Carbonの[背景階層の実例](https://carbondesignsystem.com/elements/color/usage/)とGrafanaの[パネル実例](https://grafana.com/docs/grafana/latest/visualizations/panels-visualizations/panel-editor-overview/)をブラウザで確認。現状のDarkはページ背景#202020、帯の背景#1F1F1Fとほぼ同色で、描画領域の境界が見えにくい。セクション単位の薄い境界と背景明度差を次の比較候補とする。

## ダーク表示の区切りとDesktop配布版の更新（2026-09-16）

- ユーザーの承認によりダーク表示を固定し、上下のグラフセクションだけを1pxの薄い枠・6pxの角丸で囲った。ページ背景#202020、セクション#292929、トラック#242424と明度差を付けた。行単位のカードや削除済みの注釈は戻していない。
- 共通テンプレートの6・12・18時の補助線をデータの背面へ追加。ヒットテスト対象から外し、区間の色・幅・集計・ツールチップの計算は変更していない。
- Collector 32件＋Viewer 38件＝70件成功。追加2件はセクション・テーマ指定と補助線の配置／非入力性のXAML契約テスト。`release.ps1 -OutputRoot artifacts/desktop-release-20260916 -NoZip` でFD/SC両方を生成し、Viewerは警告0・エラー0。
- 配布前後の実データ・1426×746の今日表示で、枠・背景差・補助線、実アプリが多い場合の凡例折り返し、注釈の撤去を実画面確認。全アプリを見るには従来どおり縦スクロールする。自動クリックは再び `SendInput sent 0 of 1 events; GetLastError=87` で失敗したため、今回の週／状態切替の実操作と性能再計測、最小サイズ・高DPIの検証は未実施。
- Desktopの `window-usage-tracker-portable-win-x64-sc` を同名の新版へ置換。Collector、Viewerとも更新し、旧設定JSONと既存の非表示起動VBSは内容を保持。ログオン時の既存タスクの設定は変更していない。
- 稼働中にSQLite backup APIで事前バックアップを取得。旧Collectorは停止イベントに未対応だったため、実行ファイルのパスとコンソール参加PIDを検査した限定的なCtrl+C通知で正常終了させ、終了後にもバックアップを取得した。強制終了は行っていない。
- 終了時の未保存25件を含む128,305件を移行。停止後のDBと配置先DBのファイルハッシュが一致。さらに全カラムをID順に正規化した移行前レコードのSHA-256が、配置前・配置後・新規記録追加後で一致し、`PRAGMA integrity_check` はすべて `ok`。データ本体やタイトルはGit・検証ログへ出力していない。
- 旧インストール全体と2つのDBバックアップを `Documents/WinTracker-backups/20260916-2204` に保存。旧版はDesktopから退避したが完全削除していないため復旧可能。Collector切替の記録空白は約1分。
- 新版の `--stop` による正常終了と、既存の `Run-Collector-Hidden.vbs` による再起動を確認。再起動後はDesktopのCollectorが1プロセスで稼働し、新規記録が増えることと移行済み行が不変なことを確認した。ログオンし直す試験や長時間の運用試験までは行っていない。
- 新規記録には今回までのActive > Open > Minimized、15秒チェックポイント、既定のタイトル非保存が適用される。過去のタイトルや状態は書き換えていない。既存データの意味と新規収集ルールの変更は区別する。

## その他の仕様の根拠

## v0.2.0公開前検証（2026-09-20）

- Windows App SDKを1.7.250606001から安定版2.5.1へ更新。依存先の要求に合わせWindows SDK BuildToolsを10.0.26100.4654へ更新した。WinUI 3、Collectorの.NET 10、Viewer/Sharedの.NET 8と既存の責務分離は維持。アプリバージョンはDirectory.Build.propsで0.2.0に統一。
- 更新理由は[公式サポート情報](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)に基づく。1.7系は2026-03-18にサポート終了。2.5.1は2.0系列の安定版で、[リリースノート](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0)とNuGet配信を確認した。
- Release/self-contained Viewerビルドは警告0・エラー0、Collector 32件＋Viewer 38件＝70件成功。`dotnet list WinTracker.slnx package --vulnerable --include-transitive` は全5プロジェクトで既知脆弱性の指摘なし。未知の欠陥やOS・全ランタイムの安全性を保証する検査ではない。
- `release.ps1` でCollector/Viewer単体およびPortableのFD/SC全6ZIPを生成。README、移行手順、プロジェクトLICENSE、設計・検証資料と、依存パッケージ提供のライセンス・通知・NuGetメタデータを同梱する。SCでは同梱.NETランタイムのライセンス・third-party noticesも含む。
- 作成前のDB/log混入拒否を合成の拡張子テストで確認し、非空OutputRoot拒否も確認した。作成後は `scripts/Test-ReleasePackages.ps1` で6ZIPすべての必須ファイル、DB/log/env不在、依存通知、ViewerのFD/SCランタイム構成、SCのWinUI DLLを確認。アーカイブ作成中の読み取りはファイルロックで拒否されたため、完了後に再実行して成功した。
- Portable SCのZIPを独立した `artifacts/release-smoke-20260920` に展開し、Collectorのseedコマンドで専用demo.dbへ合成2,793行を生成。展開先のViewerをDEMOモードで起動した。配布元フォルダへseedせず、配布ZIPにDBは含めない。
- Windows操作スキルで1426×746の実画面を確認。今回は自動入力が成功し、今日→直近7日→状態別表示、スクロール後の選択アプリ7行、固定日付・期間欄と更新ボタンの動作を確認した。起動中のDesktop Collectorと実DBには変更を行っていない。
- 別PC／クリーンOS、FD Viewerのランタイム導入、ARM64、高DPI・最小サイズ、DST境界、長期運用と更新後の性能再計測は未検証。DLL同梱・実機起動の確認を、全環境での保証とは扱わない。Viewerには無操作検出がなく、厳密な勤怠や課金用ではない。

### 参考リンク

- [Windows console HandlerRoutine](https://learn.microsoft.com/en-us/windows/console/handlerroutine)
- [SetConsoleCtrlHandlerのログオフ/シャットダウン制約](https://learn.microsoft.com/en-us/windows/console/setconsolectrlhandler)
- [GetUserObjectInformation / UOI_IO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getuserobjectinformationw)
- [SQLitePCLRaw.lib.e_sqlite3 3.53.3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/3.53.3)
