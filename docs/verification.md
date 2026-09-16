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

## その他の仕様の根拠

- [Windows console HandlerRoutine](https://learn.microsoft.com/en-us/windows/console/handlerroutine)
- [SetConsoleCtrlHandlerのログオフ/シャットダウン制約](https://learn.microsoft.com/en-us/windows/console/setconsolectrlhandler)
- [GetUserObjectInformation / UOI_IO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getuserobjectinformationw)
- [SQLitePCLRaw.lib.e_sqlite3 3.53.3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/3.53.3)
