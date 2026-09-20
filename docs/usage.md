# 利用・開発ガイド

## 配布版

Windows 11 x64向けです。通常は.NET / Windows App SDK同梱のPortable SC版を選び、ZIP全体を展開してください。ARM64版・署名済みインストーラーはありません。
FD版はCollector / BrowserHostに.NET 10、Viewerに.NET 8と互換のWindows App Runtime（2.5.1以降、x64）が必要です。
起動は `Run-Collector.cmd` / `Run-Viewer.cmd`、停止は `Stop-Collector.cmd`。Collectorをコンソール起動した場合はCtrl+Cでも正常停止できます。

## 更新

1. Viewerを閉じ、Collectorを正常停止し、プロセス終了を確認します。`Stop-Collector.cmd` 非対応の旧版はCtrl+Cを使い、停止できない場合は上書き・強制終了をせず停止方法を確認します。
2. 停止後の `data` と `collector.settings.json` をバックアップします。独自の起動ファイルや `browser-host/native-host-*.json` も保持してください。稼働中のDBだけをコピーしないでください。
3. 新ZIPを別フォルダへ展開し、データと設定を移行します。保存先を変更している場合は `sqliteFilePath` の実際のDBも対象です。起動先や自動起動設定を確認し、正常動作を確認するまでバックアップを残します。

### ブラウザ連携を利用中の場合

Collector・BrowserHost・拡張を同じ版に揃え、読み込み済みの拡張フォルダを更新して再読み込みします。
同じ配置先ではホスト登録JSONを引き継ぎ、配置先・拡張IDが変わる場合は登録し直します。[登録・更新手順](browser-services.md)
設定や過去の行は自動リセットされません。引き継いだ設定でタイトル保存が有効なら保存が続き、過去のアプリ記録からサービス履歴を復元することはできません。

## 設定と保存先

Portable版はルートの `collector.settings.json`、開発時は `WinTracker.Collector/collector.settings.json` を共有します。設定変更後はCollectorを再起動してください。
相対DBパスの基準はPortableのルートまたはソリューションルートです。両プロセスに `WINTRACKER_HOME` を渡して明示もできます。いずれも解決できない場合は `%LOCALAPPDATA%/WinTracker` を使います。
既定値は以下のとおりです。ホスト名保存への同意と、拡張の導入・接続は別の設定です。

| 設定 | 既定値 | 用途 |
| --- | --- | --- |
| `sqliteFilePath` | `data/collector.db` | 記録の保存先 |
| `rescanIntervalSeconds` | `300` | ウィンドウ全列挙の再確認間隔 |
| `checkpointIntervalSeconds` | `15` | 継続区間の保存間隔（1〜300秒） |
| `storeWindowTitles` | `false` | タイトルの平文保存 |
| `enableBrowserTracking` | `false` | 任意のブラウザ拡張連携 |
| `storeBrowserHostnames` | `false` | 未対応サイトのホスト名保存（連携有効時のみ） |
| `excludedExeNames` | 同梱JSONを参照 | 収集対象から除外するexe名 |

### プライバシー

DBはローカル平文で、暗号化・保存期限・ホスト名のサイト別除外は未実装です。タイトルは保存無効でも対象判定に一時使用しますが、標準出力には出しません。
ホスト名から所属組織などが分かる場合があります。完全URLを保存しないことは匿名化を意味しません。DB共有・画面共有にも注意してください。
保存設定を無効にしても、過去のタイトル・ホスト名は削除されません。[ブラウザ側の保存範囲](browser-services.md#記録と表示)

## 表示と測定の意味

Activeは前面、Openは前面でも最小化でもない対象ウィンドウ、Minimizedは最小化です。同じexeの複数ウィンドウは `Active > Open > Minimized` で集約します。
Runningは上記3状態をまとめた表示で、全プロセスの稼働時間ではありません。無操作検出はなく、前面時間を実作業・集中時間や厳密な勤怠の根拠には使えません。
ロック・観測中断等では最後の観測時刻で区間を切り、欠測を使用時間に補完しません。境界には保存間隔程度の誤差があり、急な終了での全保存は保証しません。

### 画面の操作

今日／直近7日と、サービス別／状態をまとめる／状態別を切り替えられます。データの再読込は更新ボタンで行います。
一覧の色・凡例は上位8件＋その他、アプリ別一覧は件数制限なし。合計は時:分、詳細は秒単位です。`.exe` は表示だけ省略し、内部識別子は保持します。
「Collector 起動中」はプロセスの検出であり、保存成功の保証ではありません。高DPI・DST境界・別PC・長期負荷などの確認範囲は[検証記録](verification.md)にあります。

## デモ

`Run-Demo.cmd` は専用の `data/demo.db` に合成データを作り、DEMO表示で開きます。通常の記録DBへは投入しません。
通常画面では過去の `demo-seed` 行も除外します。合成データの当日分には未来時刻も含まれます。
コマンドで作成する場合も `--demo` が必須です。

```powershell
dotnet run --project WinTracker.Collector -- seed 1week services --demo --replace
dotnet run --project WinTracker.Viewer -- --demo
```

## 開発

.NET 10 SDK、WinUI 3対応のVisual Studio / Windows SDKが必要です。拡張テストにはNode.jsも使用します。
Collector / BrowserHostは.NET 10、Viewer / Sharedは.NET 8をターゲットにしています。
通常の起動・停止は以下です。CollectorとViewerは別々のターミナルで起動します。

```powershell
dotnet run --project WinTracker.Collector
dotnet run --project WinTracker.Viewer
dotnet run --project WinTracker.Collector -- --stop
```

### 検証

```powershell
dotnet build WinTracker.slnx
dotnet test WinTracker.Collector.Tests
dotnet test WinTracker.Viewer.Tests
node --test browser-extension/tests/*.test.js
dotnet list WinTracker.slnx package --vulnerable --include-transitive
```

### 配布物を作る

PowerShellで `./release.ps1` を実行すると `artifacts/release-日時` にFD/SCの全6ZIPを生成します。既存の非空出力先やDB・ログ混入を拒否し、稼働中アプリは停止しません。
README・設計／検証資料・LICENSEと、依存先が提供するライセンス／通知／NuGetメタデータを同梱します。配布用の既定設定を使い、個人設定や接続登録を入れないでください。
生成後の検査とSHA-256確認は以下です。公開済みZIPの文書は公開時点のもので、文書のみの更新ではタグ・バイナリを差し替えません。

```powershell
./scripts/Test-ReleasePackages.ps1 -OutputRoot artifacts/release-日時
Get-FileHash <ZIPのパス> -Algorithm SHA256
```
