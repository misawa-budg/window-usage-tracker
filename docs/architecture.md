# WinTracker 全体構成

このドキュメントは、現時点の `Collector / Shared / Viewer` の役割とデータフローを最短で把握するための案内です。

## プロジェクト構成
- `WinTracker.Collector`
  - 役割: Windowsのウィンドウ状態を収集し、SQLiteへ保存する常駐プロセス
  - 主な責務:
    - Win32イベントフック (`EVENT_SYSTEM_FOREGROUND / MINIMIZESTART / MINIMIZEEND`)
    - 低頻度再スキャン（設定値、既定300秒）
    - 状態判定（`Active / Open / Minimized`）
    - 区間イベントを `app_events` にバッチ保存（50件）
    - 単一インスタンス制御（Mutex）
- `WinTracker.Shared`
  - 役割: Collector/Viewer共通の分析モデルを定義するライブラリ
  - 現在の主な内容:
    - `UsageQueryWindow`
    - `TimelineUsageRow`
    - `AppStateUsageRow`
    - `AppUsageSummaryRow`
    - 互換のため `SharedPlaceholder` も残存
- `WinTracker.Viewer`
  - 役割: SQLiteログを読み取り可視化するGUI
  - UI方針: WinUI 3（Windows App SDK）
  - 画面構成:
    - 上段 `一覧タイムライン`（Running）
    - 下段 `アプリ別タイムライン`
    - 期間切替は `24h / 1week`（タブではなく同一画面を再構築）
  - 色ルール:
    - 一覧: アプリごとに固有色
    - アプリ別: 同一アプリ色相で `Active/Open/Minimized` を濃淡表示

## 依存関係
- `WinTracker.Collector -> WinTracker.Shared`
- `WinTracker.Viewer -> WinTracker.Shared`
- `WinTracker.Shared` は他プロジェクトへ依存しない

## データの流れ
1. `WinTracker.Collector` がイベント受信時または再スキャン時に現在スナップショットを取得
2. アプリごとの状態区間を更新し、区間が閉じた時点で `app_events` に保存
3. `WinTracker.Viewer` がSQLiteを読み取り、時間バケット集計結果を画面に再構築

`source` 列は通常運用で `win_event / rescan / shutdown`、ダミーデータ投入時に `demo-seed` を使用します。

## 主要ファイル
- Collector エントリ: `WinTracker.Collector/Program.cs`
- Win32イベントポンプ: `WinTracker.Collector/Collector/WinEventHookPump.cs`
- 状態スナップショット: `WinTracker.Collector/Collector/WindowSnapshotProvider.cs`
- 保存層: `WinTracker.Collector/Persistence/SqliteEventWriter.cs`
- 共通分析モデル: `WinTracker.Shared/Analytics/`
- Viewer 集計: `WinTracker.Viewer/SqliteTimelineQueryService.cs`
- Viewer 画面: `WinTracker.Viewer/MainWindow.xaml`, `WinTracker.Viewer/MainWindow.xaml.cs`

## 関連ドキュメント
- Collector要件: `docs/requirements_collector.md`
- Viewer要件: `docs/requirements_viewer.md`
