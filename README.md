# WinTracker

Windows 11 向けの軽量なアプリケーション利用時間トラッカー（学習用プロジェクト）です。  
`Collector` がアプリのアクティブ状態を SQLite に自動蓄積し、`Viewer` が 24h / 1week のタイムラインで時間の使い方を可視化します。

## 構成
- `WinTracker.Collector`: 常駐収集プロセス（UIなし）
- `WinTracker.Viewer`: 可視化ダッシュボード（WinUI 3）
- `WinTracker.Shared`: 共通モデル

## 実行方法

配布されたZipファイル（例：`window-usage-tracker-portable-*.zip`）を展開し、中にある以下の `.cmd` ファイルをダブルクリックするだけで利用できます。
いずれも同じ設定ファイル（`data/collector.db`等）を共有して動作します。

1. **`Run-Collector.cmd`**: アプリの利用時間の記録を開始します。（バックグラウンドで常駐）
2. **`Run-Viewer.cmd`**: 記録されたデータをタイムラインダッシュボードとして表示します。

## 主な機能（Viewer）

最新仕様のタイムライン表示に対応しています。

- **期間切替**: `24h`（当日） / `1week`（直近7日）に切り替えて表示。
- **一覧タイムライン（Overview）**: その時間帯の「最もアクティブだった単一アプリ（最前面）」だけを抽出して1本のバーに表示し、何をやっていたか直感的に把握できます。
- **アプリ別タイムライン**: アプリごとの稼働状況を `Active` / `Open` / `Minimized` の濃淡で詳細に表示します。

## 前提条件
- Windows 11
- .NET 8 ランタイム 及び Windows App SDK（WinUI 3 実行環境）

## 開発・ビルド時の実行
```powershell
# Collector 起動（Ctrl+C で停止）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj

# Viewer 起動
dotnet run --project .\WinTracker.Viewer\WinTracker.Viewer.csproj
```

**テストデータの投入（シード）**
UIの確認用にダミーデータを流し込むことができます。
```powershell
# テストデータの投入
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 24h mixed --replace
```

## 配布パッケージの作成

`release.ps1` を実行することで、再配布可能な実行ファイルと `.cmd` が各構成（Portable形式・個別アプリ形式など）で `.zip` 生成されます。

```powershell
# Releaseビルドを行い全zipパッケージを生成
powershell -ExecutionPolicy Bypass -File .\release.ps1
```

## License
MIT License (`LICENSE`)

