# 監査対応と検証メモ（2026-09-16）

## 対応した項目

| 監査項目 | 対応 |
| --- | --- |
| F01 保存遅延 | 継続区間を既定15秒ごとにチェックポイント化し、小量バッチもflush |
| F02 終了 | 背景収集の `--stop`、終了ハンドラーの最大3秒待機、定期保存。OS強制終了の完全保証ではない |
| F03 例外後の待機 | producerをfinallyでキャンセル。フック障害も伝播。失敗したバッチの再試行で重複しない |
| F04 未観測時間 | 入力デスクトップ不可・観測中断・時刻逆行を検出し、区間を切り直す |
| F05 プライバシー | タイトル保存はopt-in、イベントのタイトル入り標準出力を廃止 |
| F06 依存 | ネイティブSQLiteを3.53.3に固定し、実ロード版のテストと依存監査を実施 |
| F07 表示 | 5分フィルタ/8件制限を撤去、週と日の整合、Other配色・状態凡例を修正 |
| F08 保存先 | CollectorとViewerの設定/ルート解決を共通化。Portable直接起動にも対応 |
| F09 資料 | README・構成・要件を現状に合わせ、必要な資料だけGit管理へ追加 |

追加: デモDB分離、通常集計から旧seedを除外、デモのActive重複除去、前面ダイアログ収集、最終バケットのクリップ、検索インデックス、区間描画の境界走査、不要なSQL削除、非破壊の配布生成。

## 自動検証

- 最終確認: ビルドは警告0・エラー0。Collector.Tests 23件、Viewer.Tests 25件の計48件が成功。依存監査では既知の脆弱なパッケージは検出されなかった。
- Collector.Tests: 保存・区間・設定解決・SQLite・Collector/Viewer両方のSQLを検証。ViewerのSQLソースをリンクし、WinUIを起動せず実装そのものをテストする。
- Viewer.Tests: 既存の表示テストに短時間・9アプリ・週表示・1万件の連続区間・ランダムな重複区間と基準計算の比較を追加。
- 収集OS部分とループを分離し、時刻とセッション可用性を注入してスリープ相当の時刻飛び・ロック/復帰・時刻逆行を再現する。
- SQLトリガーによる保存失敗を使い、失敗した30件目を再試行しても30件のままであることを確認する。
- SQLiteは `SELECT sqlite_version()` でも3.53.3以上を確認する。

## 実機で確認した範囲

- Windows 11 / .NET SDK 10.0.401でビルド。
- release.ps1でFD/SCの両構成を空の検証フォルダへ生成（`-NoZip`）。既存の非空出力先はビルド前に拒否することを確認。
- 合成データだけのDBを使い、self-contained ViewerでDEMO表記、24h/1weekの表示を確認。
- 追加の表示モード確認中にユーザーがEscで画面操作を停止したため、そこでUI操作を終了。状態切替の追加実機検証は未完了で、テスト用Viewerは閉じていない。
- Desktopの旧Collector・実DB・自動起動タスクには変更を加えていない。

## 残る制約と未実施の検証

- 実際のPCロック・スリープ・ログオフ・OSシャットダウン試験は未実施。作業中のPCを停止しないため、模擬時刻/セッションでロジックを検証した。特にuser32を使うコンソールのログオフ通知には制約があり、最終flushだけには依存しない。
- 旧Collectorが稼働中のため、新版のライブ収集と `--stop` のプロセス間実機試験は未実施。差し替え後に正常停止・再起動・二重起動を確認する。
- 更新後も過去のタイトルや誤集計済みの区間は書き換えない。必要なデータ移行/削除は別途バックアップと合意が必要。
- 強制終了・電源断・ディスク障害時の無損失は保証しない。15秒は通常時の保存周期であり、OSスケジューリング/ディスク待ちを含む厳密な最大遅延ではない。
- 定期区間保存によりレコード数は増える。自動保管期限・削除・圧縮は未実装。長期容量、CPU、メモリの実測が次の課題。
- `Process detected` はDB保存のheartbeatではない。保存成功時刻の監視は今後の運用改善。
- UIのレイアウト作成はUIスレッドに残る。計算量は改善したが、大量の実データによる長時間負荷試験は未実施。
- ローカル日境界は固定オフセットを使うため、DST移行日の23/25時間表示は未対応（日本の通常利用には影響しない）。
- 無操作時間、ブラウザ内URL、ウィンドウのないプロセス、完全なOSイベント履歴は対象外。
- ZIP圧縮経路・クリーンな別PCへのインストール不要実行・自動CIは今回の検証範囲外。

## 仕様の根拠

- [Windows console HandlerRoutine](https://learn.microsoft.com/en-us/windows/console/handlerroutine)
- [SetConsoleCtrlHandlerのログオフ/シャットダウン制約](https://learn.microsoft.com/en-us/windows/console/setconsolectrlhandler)
- [GetUserObjectInformation / UOI_IO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getuserobjectinformationw)
- [SQLitePCLRaw.lib.e_sqlite3 3.53.3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/3.53.3)
