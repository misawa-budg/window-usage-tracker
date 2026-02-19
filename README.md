# WinTracker

Windows 11 向けの学習用プロジェクトです。  
`Collector` がアプリ状態を SQLite に蓄積し、`Viewer` が 24h / 1week タイムラインを表示します。

## 構成
- `WinTracker.Collector`: 常駐収集（UIなし）
- `WinTracker.Viewer`: 可視化 GUI（WinUI 3）
- `WinTracker.Shared`: 共通モデル

## 前提
- Windows 11
- .NET SDK 10（Collector / テスト）
- .NET SDK 8 + WinUI 3 実行環境（Viewer）

## 開発時の実行
```powershell
# Collector 起動（Ctrl+C で停止）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj

# Viewer 起動
dotnet run --project .\WinTracker.Viewer\WinTracker.Viewer.csproj
```

補助コマンド:
```powershell
# ダミーデータ投入
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 24h --replace

# 集計レポート（CLI）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- report 24h
```

## テスト
```powershell
dotnet test .\WinTracker.Collector.Tests\WinTracker.Collector.Tests.csproj
dotnet test .\WinTracker.Viewer.Tests\WinTracker.Viewer.Tests.csproj
```

## 生成データ
- DB: `data/collector.db`（カレントディレクトリ基準）
- Collector 設定: `WinTracker.Collector/collector.settings.json`

## 配布（最小手順）
1. publish
```powershell
dotnet publish .\WinTracker.Collector\WinTracker.Collector.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\collector
dotnet publish .\WinTracker.Viewer\WinTracker.Viewer.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\viewer
```
2. `collector.settings.json` を `artifacts\collector\` に配置する  
`WinTracker.Collector\collector.settings.json` をそのままコピーすればOKです。
3. 配布先では、Collector と Viewer が同じ `data\collector.db` を参照できるように配置する
4. 起動
```powershell
.\artifacts\collector\WinTracker.Collector.exe
.\artifacts\viewer\WinTracker.Viewer.exe
```

## 注意
- Viewer は WinUI 3 実行環境（Windows App SDK）が必要です。
- Collector は単一インスタンス起動です（多重起動すると終了します）。
