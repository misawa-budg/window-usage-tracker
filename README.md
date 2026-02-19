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
# ダミーデータ投入（粗い粒度: 1時間ベース）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 24h hourly --replace

# ダミーデータ投入（混在粒度: 1分〜45分）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 24h mixed --replace

# ダミーデータ投入（細かい粒度: 1〜3分）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 24h minute --replace

# 期間内の全データを削除してseedだけにする（検証用・破壊的）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 24h mixed --replace-all

# 集計レポート（CLI）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- report 24h
```

注:
- `seed` で投入される `source=demo-seed` データは Viewer では表示されません。
- `--replace` は `source=demo-seed` のみ置換し、`--replace-all` は期間内の全sourceを削除してから投入します。

Viewer 表示仕様（現行）:
- `24h`
  - 一覧: `Running` 1行（同時アプリは縦分割）
  - アプリ別: アプリごとに1行
- `1week`
  - 一覧: 日付ごとの `Running` 行（7行）
  - アプリ別: 選択アプリの日付行（7行）
- アプリ別の色は「アプリ固有色 + 状態トーン（Active/Open/Minimized）」です。

## テスト
```powershell
dotnet test .\WinTracker.Collector.Tests\WinTracker.Collector.Tests.csproj
dotnet test .\WinTracker.Viewer.Tests\WinTracker.Viewer.Tests.csproj
```

## 生成データ
- DB: `data/collector.db`（カレントディレクトリ基準）
- Collector 設定: `WinTracker.Collector/collector.settings.json`

## 配布（推奨）
配布先で確実にDB共有させるには、`release.ps1` が生成する **portable zip** を使ってください。

- `wintracker-portable-win-x64-fd.zip`
- `wintracker-portable-win-x64-sc.zip`

これらは次の構成で同梱されます。

- `collector\`（Collector本体）
- `viewer\`（Viewer本体）
- `data\`（共有DB置き場）
- `collector.settings.json`（ルート配置）
- `Run-Collector.cmd`
- `Run-Viewer.cmd`

起動は `Run-Collector.cmd` / `Run-Viewer.cmd` をダブルクリックしてください。  
この起動方法なら、両方が同じ `data\collector.db` を参照します。

補足:
- `collector-*.zip` と `viewer-*.zip` を別々に配る方式も可能ですが、利用者が作業ディレクトリを揃えないとDB共有できません。

## 一括配布スクリプト
`release.ps1` で以下を一括生成できます。
- `collector/viewer` の `framework-dependent`（`-fd`）版
- `collector/viewer` の `self-contained`（`-sc`）版
- `portable`（Collector+Viewer+共有data+起動cmd同梱）の `-fd / -sc` 版
- 各成果物の `.zip`

注:
- Collector は `dotnet publish` を使用
- Viewer は WinUI 3 の安定性のため `dotnet build` 産物を配布フォルダへコピー

```powershell
# 既定: Release / win-x64 / artifacts 出力（zipあり）
powershell -ExecutionPolicy Bypass -File .\release.ps1

# zip不要の場合
powershell -ExecutionPolicy Bypass -File .\release.ps1 -NoZip

# 実行中WinTrackerプロセスを停止せずに実行する場合
powershell -ExecutionPolicy Bypass -File .\release.ps1 -StopRunningApps:$false
```

## 注意
- Viewer は WinUI 3 実行環境（Windows App SDK）が必要です。
- Collector は単一インスタンス起動です（多重起動すると終了します）。

## License
MIT License (`LICENSE`)
