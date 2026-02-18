# WinTracker 全体構成（最小版）

このドキュメントは、`Collector / Shared / Viewer` の役割を最短で把握するための案内です。

## プロジェクト構成
- `WinTracker.Collector`
  - 役割: Windowsのウィンドウ状態を収集し、SQLiteへ保存する常駐側
  - 主な責務: Win32収集、状態判定（`Active/Open/Minimized`）、永続化
- `WinTracker.Shared`
  - 役割: CollectorとViewerで共通利用する型・契約を置く場所
  - 現在: プレースホルダーのみ（今後、共通モデルやDTOを移動予定）
- `WinTracker.Viewer`
  - 役割: 蓄積データを可視化するGUI側
  - UI方針: WinUI 3（Windows App SDK）で実装する

## 依存関係（方針）
- `WinTracker.Collector -> WinTracker.Shared`
- `WinTracker.Viewer -> WinTracker.Shared`
- `WinTracker.Shared` は他プロジェクトへ依存しない

## データの流れ
1. `WinTracker.Collector` がイベント駆動で状態変化を収集
2. `app_events` テーブルへ保存（SQLite）
3. `WinTracker.Viewer` が必要時にSQLiteを読み、画面を再構築

## どこを見ればよいか
- 収集実装: `WinTracker.Collector/Collector/`
- 保存実装: `WinTracker.Collector/Persistence/`
- Win32境界: `WinTracker.Collector/Interop/`
- Collector要件: `docs/requirements_collector.md`
- Viewer要件: `docs/requirements_viewer.md`

## 補足
- `docs/requirements_collector.md` は **Collectorの要件定義書** です。
- `docs/requirements_viewer.md` は **Viewerの要件定義書** です。
