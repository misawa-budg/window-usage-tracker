# WinTracker 全体構成

このドキュメントは、現時点の `Collector / Shared / Viewer` の役割とデータフローを最短で把握するための案内です。

## プロジェクト構成
- `WinTracker.Collector`
  - 役割: Windowsのウィンドウ状態を収集し、SQLiteへ保存する常駐プロセス
  - 主な責務:
    - Win32イベントフック (`EVENT_SYSTEM_FOREGROUND / MINIMIZESTART / MINIMIZEEND`)
    - 低頻度再スキャン（設定値、既定300秒）
    - 状態判定（`Active / Open / Minimized`）
    - 区間イベントを `app_events` にバッチ保存（30件、既定15秒ごとにもflush）
    - 単一インスタンス制御（Mutex）
- `WinTracker.Shared`
  - 役割: Collector/Viewer共通の分析モデルを定義するライブラリ
  - 現在の主な内容:
    - 設定・保存先の解決（`Configuration/`）
    - 区間描画モデルと境界走査（`TimelineLayoutBuilder` / `IntervalSweep`）
    - 状態優先度の共通規則（`AppStatePriority`: Active > Open > Minimized）
    - `UsageQueryWindow`
    - `TimelineUsageRow`
    - `AppStateUsageRow`
    - `AppUsageSummaryRow`
    - タイムライン上の区間検索（`TrackHitTest`）
- `WinTracker.Viewer`
  - 役割: SQLiteログを読み取り可視化するGUI
  - UI方針: WinUI 3（Windows App SDK）
  - 画面構成:
    - 上段 `一覧タイムライン`（Active）
    - 下段 `アプリ別タイムライン`
    - 期間切替は `24h / 1week`（タブではなく同一画面を再構築）
  - 色ルール:
    - 一覧: `Active` 区間を連続描画（`Active` は同時刻に原則1アプリ）
    - アプリ別: 同一アプリ色相で `Active/Open/Minimized` を濃淡表示（連続区間描画）

## 状態モデルの前提
- `Active`: foreground window（同時刻に原則1ウィンドウ）
- `Open / Minimized`: 複数アプリが同時に成立しうる
- 同じexeの複数ウィンドウは `Active > Open > Minimized` で集約。Openが1つでも残れば、他が最小化されていてもOpenとする。

## 依存関係
- `WinTracker.Collector -> WinTracker.Shared`
- `WinTracker.Viewer -> WinTracker.Shared`
- `WinTracker.Shared` は他プロジェクトへ依存しない

## データの流れ
1. `WinTracker.Collector` がイベント受信時または再スキャン時に現在スナップショットを取得
2. アプリごとの状態区間を更新し、状態遷移・定期チェックポイント・終了時に `app_events` に保存
3. `WinTracker.Viewer` がSQLiteを読み取り、区間をクリップして連続時間ベースで画面に再構築

`source` 列は通常運用で `win_event / rescan / checkpoint / shutdown / observation_gap / session_unavailable`、ダミーデータ投入時に `demo-seed` を使用します。

## 主要ファイル
- Collector エントリ: `WinTracker.Collector/Program.cs`
- Win32イベントポンプ: `WinTracker.Collector/Collector/WinEventHookPump.cs`
- 状態スナップショット: `WinTracker.Collector/Collector/WindowSnapshotProvider.cs`
- 保存層: `WinTracker.Collector/Persistence/SqliteEventWriter.cs`
- 共通分析モデル: `WinTracker.Shared/Analytics/`
- Viewer 集計: `WinTracker.Viewer/SqliteTimelineQueryService.cs`
- Viewer 画面: `WinTracker.Viewer/MainWindow.xaml`, `WinTracker.Viewer/MainWindow.xaml.cs`

## 最初に追うコード（現行の1経路に絞る）

| 知りたいこと | 読む順番 |
| --- | --- |
| いつ観測するか | WinEventHookPump → ForegroundCollector |
| 何を1アプリとし、状態をどう決めるか | WindowSnapshotProvider → SharedのAppStatePriority |
| いつからいつまでを保存するか | AppIntervalTracker → SqliteEventWriter |
| DBがどう画面になるか | MainWindow.ReloadAsync → SqliteTimelineQueryService.QueryStateIntervals → TimelineLayoutBuilderのFromIntervals系 |
| 区間がどう描かれるか | TimelineViewModels → TimelineTrack（一覧）/ XAML（アプリ別） |

旧バケット方式の描画APIと、Viewerの未使用SQLは撤去済み。Collectorの `report` は時間バケットのサンプルを表示するため、そちらのQueryTimelineとTimelineUsageRowは現役であり残す。イベントキュー・チェックポイント・セッション判定・終了時flushも、保存の正しさを担うので残す。

追跡途中の区間は `AppIntervalTracker` 内部の「開始時刻＋最新AppSnapshot」で表す。確定前の終了時刻は保持せず、書込時にAppEventへ変換する。SQLiteに保存済みの区間やスキーマを変える整理ではない。

## 任意のブラウザ連携

`browser-extension/services.js` が前面の選択タブを分類し、`WinTracker.BrowserHost` がNative Messagingとユーザー限定の名前付きパイプを中継する。ホストはDBにアクセスしない。`BrowserServiceHub` / `BrowserServiceState` がOSの前面HWND・照会nonce・期限を照合してから既存の区間保存へ渡す。

固定サービスIDに加え、`storeBrowserHostnames` を明示的に有効化した場合だけ `host:<ホスト名>` を `service_id` へ保存する。設定は拡張への各照会・Collectorの採用判定・保存層の3か所で適用する。識別子の許可規則と表示名、前面区間の置換はSharedの `BrowserServices` に置き、既存のViewer描画へ流す。新しいUI描画系やテーブルは増やさない。完全URL・タイトルの取得を拡張から追加しない。

配布設定はブラウザ連携・ホスト名保存ともfalse。既知サービスの表示名編集、任意ホストのグループ化、保存期限は今回の範囲外。

## 関連ドキュメント

- ブラウザサービス連携: `docs/browser-services.md`（v0.3.1）
- Collector要件: `docs/requirements_collector.md`
- Viewer要件: `docs/requirements_viewer.md`
