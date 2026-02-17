# Windows版スクリーンタイム（Collector）要件定義

## 概要
iPhoneのスクリーンタイムのように、使ったアプリをタイムラインで追えるようにするソフトを作る。  
本システムは以下2プロジェクトで構成される。

- Collector: Windowsのウィンドウ状態を収集してログを蓄積（本書の対象）
- Viewer: 蓄積ログを可視化（本書の対象外）

## スコープ
- 対象OS: Windows 11
- 対象粒度: アプリ単位（exe名）
- 対象状態: `Active / Open / Minimized`
- 対象外（初期版）: URL取得、ウィンドウタイトルの全文保存、サービス化、自動起動設定

## 状態定義（排他的）
アプリ状態は、任意時点で以下のいずれか1つのみ。

- Active: foreground window を持つアプリ
- Minimized: 代表ウィンドウが最小化されているアプリ
- Open: `Active` ではなく `Minimized` でもないアプリ（ウィンドウとして存在）

判定優先順位は `Active > Minimized > Open` とする。

## 用語
- hwnd: Windowsがウィンドウに付与するハンドル値（識別子）。C#では `IntPtr` で扱う。

## ログ設計（SQLite）
### 保存方式
- 保存先DB: SQLite
- タイムスタンプ: UTC（ISO 8601文字列またはUnix epoch ms）
- 1レコード = 1状態遷移イベント

### `app_events` テーブル必須項目
- `id`（INTEGER PRIMARY KEY AUTOINCREMENT）
- `event_at_utc`（TEXT）: イベント発生時刻（UTC）
- `exe_name`（TEXT）: 例 `Code.exe`
- `pid`（INTEGER）: プロセスID
- `hwnd`（TEXT）: 16進文字列（例 `0x001A08F2`）
- `state`（TEXT）: `Active | Open | Minimized`
- `source`（TEXT）: `win_event | rescan`

### インデックス
- `idx_app_events_time` on (`event_at_utc`)
- `idx_app_events_exe_time` on (`exe_name`, `event_at_utc`)

## 更新ルール（Collector）
### 基本方針
- 原則はイベント駆動で収集する
- 取りこぼし対策として低頻度再スキャンを併用する

### 監視イベント（Win32）
- `EVENT_SYSTEM_FOREGROUND`（前面ウィンドウ変更）
- `EVENT_SYSTEM_MINIMIZESTART`（最小化開始）
- `EVENT_SYSTEM_MINIMIZEEND`（最小化終了）

### 補完再スキャン
- 5分ごとに全体整合性チェックを実行
- `EnumWindows` でトップレベルウィンドウを列挙し、`Open/Minimized` を再評価
- 再スキャン由来のレコードは `source=rescan` として保存

### 書き込み制御
- 同一アプリで状態が変わらない連続イベントは保存しない（重複抑制）
- DB書き込みはバッチ化（1秒ごと、または50件到達時）
- 終了シグナル受信時にバッファをflushしてから終了

## 機能要件
- Collectorは `Active` 状態を検知できること
- Collectorは `Minimized` 状態を検知できること
- Collectorは `Open` 状態を検知できること
- Collectorは `app_events` の必須項目をSQLiteへ保存できること
- CollectorはUTCで時刻を保存できること
- Collectorは最低3年間のログ保持を前提に動作できること

## 非機能要件
### パフォーマンス（初期目標値）
- 平常時CPU使用率: 平均1%未満（15分平均）
- 常駐メモリ使用量: 150MB未満
- ディスク書き込み頻度: 1分あたり300件未満（平常利用時）

### 信頼性
- 例外発生時はプロセスを即終了せず、エラーログ記録後に継続を試みる
- DB書き込み失敗時はリトライし、失敗内容をログへ残す

### 測定不能時の扱い
- 目標値の測定が難しい場合でも、以下は必須とする
- タスクマネージャでCPU/メモリの15分観測を実施し、結果を記録する
- ログ件数（1分あたり）をCollector自身が定期出力する
- 目標超過時は「再スキャン間隔を延長」「重複抑制条件を強化」を優先して調整する

## データ保持・プライバシー
- ログ保持期間は3年
- 3年を超えたデータは日次バッチで削除する
- 初期版はウィンドウタイトルを保存しない（必要性が明確になるまで収集しない）
