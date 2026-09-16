# Windows版スクリーンタイム（Collector）要件定義

> このファイルは **Collector の要件定義書** です（Viewer要件は別ファイル）。

## 概要
iPhoneのスクリーンタイムのように、使ったアプリをタイムラインで追えるようにするソフトを作る。  
本システムはCollector / Viewerと共通ライブラリSharedで構成される。

- Collector: Windowsのウィンドウ状態を収集してログを蓄積（本書の対象）
- Viewer: 蓄積ログを可視化（本書の対象外）

## スコープ
- 対象OS: Windows 11
- 対象粒度: アプリ単位（exe名）
- 対象状態: `Active / Open / Minimized`
- 対象外（初期版）: URL取得、サービス化、自動起動設定
- 実行形態: 単一インスタンス常駐（Mutex）

## 状態定義（排他的）
アプリ状態は、任意時点で以下のいずれか1つのみ。

- Active: foreground window を持つアプリ
- Minimized: 代表ウィンドウが最小化されているアプリ
- Open: `Active` ではなく `Minimized` でもないアプリ（ウィンドウとして存在）

判定優先順位は `Active > Open > Minimized` とする。前面ウィンドウがなく、追跡対象がすべて最小化されているときだけアプリをMinimizedとする。規則はSharedのAppStatePriorityに集約し、Viewerの重複区間解決とも共有する。旧Collectorの過去データは書き換えない。

補足:
- システム全体では、`Active` は同時刻に原則1つ（foreground windowの性質）
- `Open` / `Minimized` は同時刻に複数アプリが成立しうる

## 用語
- hwnd: Windowsがウィンドウに付与するハンドル値（識別子）。C#では `IntPtr` で扱う。

## ログ設計（SQLite）
### 保存方式
- 保存先DB: SQLite
- タイムスタンプ: UTC（ISO 8601文字列またはUnix epoch ms）
- 1レコード = 1アプリの状態区間（定期チェックポイントでも分割）

### `app_events` テーブル必須項目
- `id`（INTEGER PRIMARY KEY AUTOINCREMENT）
- `event_at_utc`（TEXT）: イベント発生時刻（UTC）
- `state_start_utc`（TEXT）: 状態区間の開始時刻（UTC）
- `state_end_utc`（TEXT）: 状態区間の終了時刻（UTC）
- `exe_name`（TEXT）: 例 `Code.exe`
- `pid`（INTEGER）: プロセスID
- `hwnd`（TEXT）: 16進文字列（例 `0x001A08F2`）
- `title`（TEXT）: ウィンドウタイトル
- `state`（TEXT）: `Active | Open | Minimized`
- `source`（TEXT）
  - 通常運用: `win_event | rescan | checkpoint | shutdown | observation_gap | session_unavailable`
  - 開発用シード: `demo-seed`

### インデックス
- `idx_app_events_time` on (`event_at_utc`)
- `idx_app_events_exe_time` on (`exe_name`, `event_at_utc`)
- `idx_app_events_interval_end` on (`state_end_utc`, `state_start_utc`)

## 更新ルール（Collector）
### 基本方針
- 原則はイベント駆動で収集する
- 取りこぼし対策として低頻度再スキャンを併用する

### 監視イベント（Win32）
- `EVENT_SYSTEM_FOREGROUND`（前面ウィンドウ変更）
- `EVENT_SYSTEM_MINIMIZESTART`（最小化開始）
- `EVENT_SYSTEM_MINIMIZEEND`（最小化終了）

### 補完再スキャン
- `collector.settings.json` の `rescanIntervalSeconds` に従って全体整合性チェックを実行（既定300秒）
- `EnumWindows` でトップレベルウィンドウを列挙し、`Open/Minimized` を再評価
- 再スキャン由来のレコードは `source=rescan` として保存

### 書き込み制御
- 同一状態はメモリ内でまとめ、既定15秒のチェックポイントで継続区間も保存する
- SQLite書き込みは30件またはチェックポイントでバッチflushする
- 終了シグナル受信時は未書き込みバッチをflushし、稼働中の区間を最後の観測時刻まで `source=shutdown` で確定保存して終了する

### 収集フィルタ
- 設定ファイルの `excludedExeNames` に含まれるプロセスは除外する
- 代表ウィンドウ選定では以下を除外する
  - owner付きウィンドウ（実際のforegroundダイアログは例外）
  - タイトル空（最小化以外）
  - cloakedウィンドウ（最小化以外）
  - 極小ウィンドウ（最小化以外、最小50x50）

## 機能要件
- Collectorは `Active` 状態を検知できること
- Collectorは同時刻に複数 `Active` を生成しないこと（原則）
- Collectorは `Minimized` 状態を検知できること
- Collectorは `Open` 状態を検知できること
- Collectorは `app_events` の必須項目をSQLiteへ保存できること
- CollectorはUTCで時刻を保存できること
- CollectorはCtrl+C時に安全に停止し、バッファをflushできること
- Collectorは同時起動を防止できること

## 非機能要件
### パフォーマンス（初期目標値）
- 平常時CPU使用率: 平均1%未満（15分平均）
- 常駐メモリ使用量: 150MB未満
- ディスク書き込み頻度: 1分あたり300件未満（平常利用時）

### 信頼性
- 致命的なフック・保存・収集例外は上位へ伝播し、producerをキャンセルして終了する。異常状態のまま無期限に待機しない。
- ロック/入力不可・長い観測中断を検出したら最終観測で区間を切る。停止通知だけに依存せず定期保存で損失を抑える。OSによる強制終了時の完全保存は保証しない。

## データ・プライバシー
- タイトルは既定で保存しない。`storeWindowTitles=true` の場合のみ平文で保存する。標準出力にタイトルを出さない。
- 既存DBの過去のタイトルは削除しない。デモは独立した `data/demo.db` を使う。
