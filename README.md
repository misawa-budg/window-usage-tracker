# WinTracker

Windows 11 向けの軽量なアプリケーション利用時間トラッカー（学習用プロジェクト）です。  
`Collector` がアプリ状態（`Active / Open / Minimized`）を SQLite に自動蓄積し、`Viewer` が 24h / 1week のタイムラインで時間の使い方を可視化します。  
`Active` は Windows の foreground 特性上、同時刻に原則1アプリです。

## 構成

- `WinTracker.Collector`: 常駐収集プロセス（UIなし）
- `WinTracker.Viewer`: 可視化ダッシュボード（WinUI 3）
- `WinTracker.Shared`: 共通モデル

## 実行方法

配布されたZipファイル（例：`window-usage-tracker-portable-*.zip`）を展開し、中にある以下の `.cmd` ファイルをダブルクリックするだけで利用できます。
通常は実行環境同梱の **`window-usage-tracker-portable-win-x64-sc.zip`** を選んでください。ZIP内から直接実行せず、書き込み可能なフォルダに全体を展開します。Windows x64向けで、ARM64版は配布していません。
いずれも同じ設定ファイル（`data/collector.db`等）を共有して動作します。

1. **`Run-Collector.cmd`**: コンソール付きで収集を開始します。Ctrl+Cで停止します。
2. **`Run-Viewer.cmd`**: 記録されたデータをタイムラインダッシュボードとして表示します。
3. **`Stop-Collector.cmd`**: 新版Collectorへ正常停止を要求します（非表示起動時にも使用可能）。
4. **`Run-Demo.cmd`**: 実データとは別の `data/demo.db` に合成データを作り、DEMO表示付きViewerを起動します。

旧版の配布フォルダや実データを新規ビルドで自動更新することはありません。差し替え時は旧Collectorを停止し、DBと設定をバックアップしてから移行してください。旧版には `--stop` がありません。

### 既存データを引き継ぐ更新

1. 旧Viewerを閉じ、旧Collectorを正常停止します。対応版は `Stop-Collector.cmd`、コンソール起動の旧版はCtrl+Cを使い、終了を確認します。非表示の旧版が停止できない場合は強制終了や上書きをせず、停止方法を確認してください。
2. 旧フォルダ全体を別の場所へバックアップします。DBだけを稼働中にコピーしないでください。
3. 新ZIPを別フォルダへ展開し、旧版の `data` フォルダと `collector.settings.json` を引き継ぎます。DB保存先を変更している場合は、設定の `sqliteFilePath` の参照先も確認します。
4. 新版のViewerで過去の記録を確認してからCollectorを起動します。同じWindowsセッションではCollectorは1つだけ動作します。
5. 自動起動を別途登録している場合は、登録先パスとの一致を確認します。このZIPはタスクやサービスを自動登録しません。正常動作を確認するまでバックアップは残してください。

v0.2.0以降の既定ではウィンドウタイトルを保存しません。引き継いだ設定で `storeWindowTitles: true` を指定していれば保存が続きます。既存タイトルと過去の状態集約結果は自動変更されません。

## 主な機能（Viewer）

最新仕様のタイムライン表示に対応しています。

- **期間切替**: 「今日」 / 「直近7日」に切り替えて表示。
- **一覧タイムライン（Overview）**: `Active` の区間を連続時間として1レーン描画します（同時刻に `Active` は原則1つ）。
- **アプリ別タイムライン**: アプリごとの区間を連続時間として描画し、`Active` / `Open` / `Minimized` を同一色相の濃淡で表示します。
- **ツールチップ表示**: 時刻と継続時間は `HH:mm:ss` で表示します。

## 前提条件

- Windows 11
- 開発: .NET 10 SDK、WinUI 3をビルドできるVisual Studio / Windows SDK
- framework-dependent版: Collectorに.NET 10、Viewerに.NET 8とWindows App Runtime 2.5.1以降の互換ランタイム（x64）が必要
- self-contained版: .NET / Windows App SDKを同梱（Windows 11は必要）

## 開発・ビルド時の実行

```powershell
# Collector 起動（Ctrl+C で停止）
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj

# Viewer 起動
dotnet run --project .\WinTracker.Viewer\WinTracker.Viewer.csproj

# 非表示起動した新版Collectorへの正常停止要求
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- --stop
```

**テストデータの投入（シード）**
`--demo` が必須です。通常DBへのシード投入と `--replace-all` は廃止しています。

```powershell
dotnet run --project .\WinTracker.Collector\WinTracker.Collector.csproj -- seed 1week mixed --demo --replace
dotnet run --project .\WinTracker.Viewer\WinTracker.Viewer.csproj -- --demo
```

## 保存・集計・プライバシー

- 設定はCollector / Viewerで共通です。開発時はソリューション配下の `WinTracker.Collector/collector.settings.json`、Portable版はルートの設定を使います。
- 相対DBパスの基準は開発時にソリューションルート、Portable版ではバンドルルートです。明示する場合は両プロセスに `WINTRACKER_HOME` を渡してください。いずれも見つからない場合は `%LOCALAPPDATA%/WinTracker` を使います。
- `rescanIntervalSeconds` は全列挙の再確認間隔（既定300秒）。`checkpointIntervalSeconds` は継続区間の保存間隔（既定15秒、1〜300秒）です。状態遷移で閉じた区間は30件またはチェックポイントでflushします。
- `storeWindowTitles` は既定falseです。タイトルはフィルタ判定に一時使用しますが、標準出力には出しません。trueにするとDBへ平文保存されます。過去に保存したタイトルは自動削除されません。
- ロック等で入力デスクトップが利用できない場合、または観測間隔が保存間隔の2倍を超えた場合は、最後の観測時刻で区間を切ります。未観測時間を使用時間に補完しません。ロック境界には保存間隔程度の誤差があり、正常終了時も最後の観測以降を足しません。
- `Active` は前面アプリであり、キー入力や実作業時間の証明ではありません。放置中の無操作判定は未実装です。複数ウィンドウの集約は `Active > Open > Minimized`。前面がなくても、開いている対象ウィンドウが1つあればOpenです（新版Collectorから適用、過去データは変更しません）。
- `Running` は観測対象ウィンドウの3状態を統合した表示であり、ウィンドウのない全プロセスの稼働時間ではありません。
- 短時間の利用も表示します。アプリ別一覧は8件に制限しません。Overviewの凡例・描画は上位8件＋Otherです。合計はHH:mm、詳細ツールチップは秒単位です。
- 通常画面・レポートでは過去の `demo-seed` 行も集計から除外します。DEMO画面は合成データで、当日分には未来時刻を含むサンプルもあります。
- `Collector 起動中` は起動の検出で、保存処理の健全性までは保証しません。Viewerのデータ更新は更新ボタンで行います。
- 夏時間（DST）の切替をまたぐ日付境界、高DPI、別PCでの動作は検証が限定的です。厳密な勤怠・課金の根拠には使わないでください。

## 検証

```powershell
dotnet build .\WinTracker.slnx
dotnet test .\WinTracker.Collector.Tests\WinTracker.Collector.Tests.csproj
dotnet test .\WinTracker.Viewer.Tests\WinTracker.Viewer.Tests.csproj
dotnet list .\WinTracker.slnx package --vulnerable --include-transitive
```

構成は [architecture](docs/architecture.md)、検証範囲と残課題は [verification](docs/verification.md) を参照してください。
表示上はアプリ名の末尾の `.exe` を省略しますが、内部の識別子と詳細ツールチップは元の名前を保持します。
週表示の性能比較と説明できる設計上の特徴は [interview-notes](docs/interview-notes.md) にまとめています。

## 配布パッケージの作成

`release.ps1` を実行することで、再配布可能な実行ファイルと `.cmd` が各構成（Portable形式・個別アプリ形式など）で `.zip` 生成されます。

```powershell
# Releaseビルドを行い全zipパッケージを生成
powershell -ExecutionPolicy Bypass -File .\release.ps1
```

毎回 `artifacts/release-日時` へ作成します。既存の非空出力先は拒否し、既存DBの削除や稼働中アプリの強制終了はしません。FD/SCのViewerビルド出力も分離しています。
各パッケージにREADME・LICENSE・設計／検証資料を同梱します。DB・イベントログ・診断ログを検出した場合はZIP作成を中止します。リリースのSHA-256と照合するには `Get-FileHash <ZIPのパス> -Algorithm SHA256` を使います。

## License
MIT License (`LICENSE`)

