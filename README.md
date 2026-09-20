# WinTracker

Windowsの前面アプリとウィンドウ状態を記録し、時間の使い方を振り返るローカルアプリです。
C# / WinUI 3 / SQLiteを使用。Windows 11 x64向けの個人開発プロジェクトです。

## できること

- 今日・直近7日の記録を、アプリ別・状態別のタイムラインで表示。
- Edge / Chromeの前面タブをサービス別に表示。任意設定で未対応サイトもホスト名で分類。
- 実データとは別のDEMOで動作確認。前面時間は作業・集中時間を示すものではありません。

## はじめる

1. [Releases](https://github.com/misawa-budg/window-usage-tracker/releases/latest)の `window-usage-tracker-portable-win-x64-sc.zip` を、書き込み可能な場所へ全体展開します。
2. `Run-Collector.cmd` で記録、`Run-Viewer.cmd` で表示。停止は `Stop-Collector.cmd`、デモは `Run-Demo.cmd` です。
3. ブラウザ連携は[拡張・ホストの導入](docs/browser-services.md)が別途必要です。自動起動は自動登録されません。

## 保存とプライバシー

- 記録は `data/collector.db`、設定は `collector.settings.json`。DBはローカル平文です。
- 配布時はブラウザ連携・ホスト名保存・タイトル保存が無効。ホスト名にも機微な情報が含まれ得ます。
- 拡張は完全URL・検索クエリ・タイトル・本文を送信しません。設定を無効にしても過去の記録は消えません。

## 更新する

1. Viewerを閉じ、Collectorを正常停止して、DB・設定をバックアップします。
2. 新ZIPへデータと設定を引き継ぎます。ブラウザ連携はCollector・BrowserHost・拡張を同じ版に揃え、拡張を再読み込みします。
3. 起動先・ホスト登録と過去の表示を確認します。[更新手順](docs/usage.md#更新)に沿って進め、確認までバックアップを残してください。

## 構成と開発

- Collectorが収集、Viewerが表示、Sharedが共通規則を担当。BrowserHostと拡張は任意のローカル連携です。
- 開発には.NET 10 SDKとWinUI 3対応のビルド環境を使用します。[起動・テスト・配布手順](docs/usage.md#開発)
- 実装の入口は[全体構成](docs/architecture.md)、検証範囲は[検証記録](docs/verification.md)を参照してください。

## 詳細

- [設定・操作・更新](docs/usage.md)
- [ブラウザ連携と接続診断](docs/browser-services.md)
- [設計判断と面接での説明](docs/interview-notes.md)

## License

[MIT](LICENSE)

