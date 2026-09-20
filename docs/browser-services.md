# ブラウザサービス記録（v0.3.1）

Edge / Chromeの前面ウィンドウで選択されたタブを、サービス単位で表示する任意機能です。拡張のインストールと `enableBrowserTracking: true` の両方が必要です。未対応サイトは追加の `storeBrowserHostnames: true` でホスト名ごとに表示できます。配布時は両方ともfalse。公開済みv0.2.0にはこの機能はなく、0.3.0はローカル検証版です。

## 記録と表示

- 拡張内でURLを分類し、既定では固定サービスIDだけをローカルのCollectorへ送ります。追加同意が有効な場合だけ、未対応サイトのホスト名も送信・保存します。完全URL、パス、クエリ、フラグメント、URL内のユーザー名・パスワード、タイトル、タブID、プロフィール名、ページ本文は送りません。クラウド送信・LLM・画面操作は使いません。ホスト名に含まれる組織名・識別情報は除去できず、匿名化機能ではありません。
- `tabs` はURL取得、`nativeMessaging` はローカル通信、`alarms` は30秒間隔の再接続確認に使います。コンテンツスクリプト、閲覧履歴API、全ページへのスクリプト注入はありません。
- 同じサービスはブラウザやタブをまたいで1行にまとまります。既定の「サービス別（最前面）」は前面区間だけを置換し、ブラウザとサービスを二重加算しません。「状態をまとめる」「状態別に見る」は元のアプリ単位です。
- 拡張未接続・内部ページ・権限不足・対応外の表示・過去データは `Edge（未取得）` / `Chrome（未取得）`。未対応HTTP(S)サイトは、追加同意が無効なら `その他のWeb`、有効ならホスト名で表示します。IPアドレス、localhost等の単一ラベル名、不正なホスト名は `その他のWeb` にまとめます。
- Incognito / InPrivateは対象外。既存Collectorがブラウザの存在を記録すること自体は変わりません。タイトル保存は既定falseのままにしてください。
- 前面時間は視聴時間・集中時間ではありません。バックグラウンド音声、Picture-in-Picture、PWA、分割表示の厳密な可視性は対象外。APIがsplitViewIdを提供する分割タブは未取得扱い。Edge等で同情報がない場合の動作は要実機検証。

## 導入（利用者が実行）

### 表示名とホスト名の扱い

YouTube、Twitch、Gmail、GitHub、Netflix、U-NEXT（video.unext.jp）、ChatGPT（Web）、Claude（Web）、Geminiは初期対応です。すべてのサービスを列挙する設計にはせず、未対応サイトはホスト名で補います。ユーザーによる名前の登録は必須ではありません。表示名編集・複数ホストの手動グループ化は未実装です。

例えばGmail、Gemini、`drive.google.com` は別行、`keio.jp` と `portal.keio.jp` も別行です。最上位のドメインへ一括集約せず、`www` も自動除去しません。英字は小文字化し末尾ドットを除去、国際化ドメインはASCII（Punycode）表記で保存・表示します。ホスト名の最大長は253文字です。初期対応サービス以外はサブドメインの数に応じて行が増えます。

ホスト名を記録する場合は、共通の `collector.settings.json` に以下を設定し、Collectorを再起動します。

```json
"enableBrowserTracking": true,
"storeBrowserHostnames": true,
"storeWindowTitles": false
```

これは既存JSONオブジェクトへ追加・変更する設定部分です。配布時の既定値はすべてfalse。ホスト名には所属組織や個人用サブドメインが含まれ得ます。DBはローカル平文で、暗号化・保存期限・サイト別除外は未実装です。記録したくないサイトがある場合はホスト名保存を有効にしないでください。設定は再起動時に反映され、無効化後も過去の記録は表示されます。過去に保存しなかったホスト名は復元できません。

### 拡張とホストの設定

表示だけを先に試す場合は検証版の `Run-Demo.cmd` を使います。専用の `data/demo.db` にサービス切替の合成データを作り、DEMO表示のViewerを開きます。拡張やホスト登録は不要です。

1. 新しいportable SC版を、稼働中のフォルダとは別の固定フォルダに展開します。ブラウザ連携用の `browser-host` / `browser-extension` / `scripts` が含まれています。現時点ではストア未公開の開発用拡張です。
2. Edgeの `edge://extensions` またはChromeの `chrome://extensions` で開発者モードを有効にし、「展開して読み込み」から `browser-extension` を選択します。その画面の拡張IDを控えます。必要な各ブラウザ／プロフィールで導入します。許可を組織が制限している環境では管理方針に従ってください。
3. 展開先でPowerShellを開き、下記を実行します。`-WhatIf` で書込先だけを先に確認できます。通常のユーザー権限でHKCUの専用キーとホストマニフェストを登録します。実行ポリシーを恒久的に弱めないでください。

```powershell
.\scripts\Register-BrowserHost.ps1 -Browser Edge `
  -HostExecutable .\browser-host\WinTracker.BrowserHost.exe `
  -ExtensionId '拡張画面に表示された32文字のID'
```

Chromeは `-Browser Chrome` に置換します。EdgeとChromeでIDが異なる場合は、それぞれのIDで登録します。スクリプトは別インストールの登録を無断で上書きしません。

「スクリプトの実行が無効」と出る場合は、管理者権限ではなくPowerShellの実行ポリシーを確認します。`Get-ExecutionPolicy -List` で `MachinePolicy` / `UserPolicy` が設定されている場合は組織の管理方針に従ってください。管理ポリシーがなく、配布元とスクリプト内容を確認できた場合は、次のように今回のプロセスだけ実行を許可できます。PC全体やユーザーの恒久設定は変更しません。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Register-BrowserHost.ps1 -Browser Edge -HostExecutable .\browser-host\WinTracker.BrowserHost.exe -ExtensionId '拡張画面に表示された32文字のID'
```

4. 旧Collectorを `Stop-Collector.cmd` で正常停止し、DB・設定をバックアップします。新フォルダへ `data` と `collector.settings.json` を移行し、その設定に `"enableBrowserTracking": true` を追加します。未対応サイトのホスト名も必要なら上記の追加設定を行います。`storeWindowTitles` はfalseを推奨。自動起動設定がある場合は起動先も確認してください。Collectorは同時に1つだけ起動します。
5. 新Collectorを起動し、拡張を再読み込みします。拡張ボタンのツールチップで接続状態を確認できます。「!」は接続待ちです。ブラウザ内でサービスを切り替え、保存周期（既定15秒）後にViewerを更新します。Viewer自体はこの版でもデータを自動再読込しません。

ソースからビルドする場合は `dotnet build WinTracker.BrowserHost -c Release`。ホストのexeは同プロジェクトの `bin/Release/net10.0/` にあります。FDホストの実行には.NET 10が必要で、SC配布物では同梱されます。

更新時はCollector・BrowserHost・拡張を同じ版へ揃えます。同じ配置先を維持する場合、登録済みの `browser-host/native-host-*.json` を引き継げば再登録は不要です。拡張も読み込み済みのフォルダを更新し、拡張管理画面で再読み込みしてください。ホストの配置先または拡張IDが変わる場合は、旧登録を解除して新しいパス・IDで登録し直します。ブラウザ自体の強制終了や恒久的な実行ポリシー変更は不要です。

### 接続できないとき

拡張を再読み込みしてから、ツールバーのWinTracker拡張ボタンを押すと「接続診断」が開きます。「再接続」を押し、数秒後に「状態を更新」で確認してください。サービスワーカーのConsole操作は不要です。

- `HOST_NOT_FOUND`: ネイティブホストの登録またはマニフェストが見つかりません。
- `HOST_FORBIDDEN`: 拡張IDの許可設定や管理ポリシーによる拒否です。ポリシーは回避しないでください。
- `HOST_START_FAILED`: 中継exeを起動できません。
- `HOST_EXITED`: 中継が終了しました。Collectorの起動と `enableBrowserTracking` を確認してください。
- `HOST_IO_ERROR`: 中継との通信エラーです。
- `WORKER_UNAVAILABLE`: 拡張の処理から応答がありません。再読み込みと拡張管理画面のエラーを確認してください。

開発ツール側で登録済みでも通常のEdgeが `HOST_NOT_FOUND` になる場合は、通常のユーザー起動元のPowerShellでも登録を確認してください。実行環境間でレジストリの見え方が異なる場合があります。通常起動側で未登録と確認できた場合は、その側で `Register-BrowserHost.ps1` を実行します。管理者権限への切り替えやポリシーの緩和は不要です。

`WAITING_FOR_COLLECTOR` は接続要求を送った後の応答待ち、`CONNECTED` はCollectorから応答を受け取った状態です。接続成功だけではサービス記録の保存成功を意味しません。`lastError` は直近の失敗として接続成功後も残ります。試行回数と応答回数は拡張の処理が再起動するとリセットされます。

診断結果は選択してコピーできます。表示するのは拡張ID・版・固定のエラー種別・接続状態・回数・接続関連の時刻のみです。生のエラー文、URL、タイトル、サービス履歴は出力せず、診断情報をファイルやストレージに永続化しません。追加の権限も要求しません。

開発時にブラウザ本体のログが必要な場合は、内容にURL等が含まれる可能性を確認したうえで、Edgeを完全終了してから `scripts/Start-EdgeDiagnostics.ps1` を手動実行します。通常の起動元でホスト登録を読み取り、既存プロフィールのEdgeを一時的なログ付きで起動します。既存のEdgeが残っている場合は起動を拒否し、強制終了やポリシー変更はしません。ログと登録チェック結果はGit管理外の `artifacts/edge-user-diagnostics/日時-ID` に保存し、自動送信しません。`-CheckOnly` は登録チェックだけを行います。診断後はEdgeを完全終了してログ採取を止め、通常のショートカットから起動してください。ログ全体を公開せず、WinTrackerに関係する行だけを確認します。

## 無効化・元に戻す

- 拡張を無効化／削除し、Collector設定をfalseにして再起動すると従来の記録へ戻ります。
- 登録解除は同じコマンドに `-Unregister` を付けます（ExtensionId不要）。専用HKCUキーだけを削除し、DBやフォルダは削除しません。
- 新Collectorは既存 `app_events` にnullableの `service_id` 列を一つ追加するだけで、過去行を書き換えません。旧Viewerも既知の列で読めます。旧Collectorへ戻す場合も先に新Collectorを停止してください。バックアップは保持してください。
- 過去のアプリ記録からサービスを正確に復元することはできません。

## 内部構造と限界

拡張 → Native Messagingホスト → 同一Windowsユーザー・セッション用の名前付きパイプ → Collectorの既存保存ループ → SQLite → Viewer。

新NuGet依存はありません。ホストはDBを開かず、標準入出力の長さ付きJSONを中継します。フレーム上限4096バイト、未知JSONフィールド拒否、Collectorで固定IDの許可リストと同意済みホスト名の形式を検証します。名前付きパイプはCurrentUserOnlyで、同じユーザーの悪意あるプロセスまで認証する仕組みではありません。

追加同意はCollectorの照会メッセージごとに通知し、拡張は厳密にbooleanのtrueを受け取った場合だけホスト名を送ります。Collectorの状態管理とSQLite保存層でも独立して設定と形式を検査します。固定ID以外は `host:<正規化したホスト名>` の形式のみ許可し、既存の `service_id` 列を利用するため0.3.0からのスキーマ変更はありません。Viewerは現在の記録設定にかかわらず保存済みの有効なホスト名を表示します。

ブラウザwindowIdとHWNDを直接対応付けません。OSの前面が変わったときに古い観測を破棄し、新しいnonce付き照会を拡張へ送ります。拡張は実際にフォーカスされた通常ウィンドウの選択タブを再取得し、Collectorは2秒以内かつ同じ前面HWNDの応答だけを採用します。接続ごとに区別し、複数プロフィールが同時にフォーカスを主張した場合は未取得にします。

10秒ごとの軽量な再確認、25秒の有効期限（1秒ごとにメモリ上で確認）、切断時の無効化を行います。変化のない再確認ではウィンドウ列挙は要求しません。ロックや観測中断でもキャッシュを破棄します。ブラウザとOSの通知は非同期なので、遷移直後に短い未取得区間や通知遅延があり、ミリ秒精度は保証しません。既存のスナップショット通知は合流するため、非常に短い連続切替をすべて保存するイベント台帳ではありません。ブラウザのフリーズやOSが検知していない短時間のロックにも検証上の限界があります。

## 検証

自動テスト: サービス切替による区間分割、旧スキーマ読取・追加列移行、プライバシーの保存制約、前面ウィンドウ変更／遅延応答／期限切れ／プロファイル競合、実名前付きパイプでの照会・切断、URL分類・フォーカス喪失・タブ移動、サービス別グラフの合計とラベル。

0.3.0の実Edgeでは、利用者による通常起動元でのホスト登録後、拡張のCONNECTEDと実DBのサービス記録を確認しました。開発ツール側で見える登録と通常起動元で見える登録が異なった事例であり、Windows内部の原因まで断定していません。0.3.1のホスト名記録は自動テストで検証し、Computer Useは使いません。Chrome実機、2ウィンドウ／複数プロフィール、ロック／復帰、長期負荷、今回のWinUI目視確認は未完了です。実際の利用環境で以下を確認してください。

| 操作 | 期待結果 |
| --- | --- |
| 同じウィンドウでYouTube → Gmail | サービスが切り替わり、二重加算なし |
| 別Edgeウィンドウ、Chromeへ切替 | 前面側だけ記録 |
| VS Codeを前面へ | Webサービスの前面時間は増えない |
| タブを別ウィンドウへ移動・閉じる | 古いサービスが残り続けない |
| 別プロフィール・InPrivate・内部ページ | 不確実な情報は未取得、プライベートURLは保存なし |
| 拡張停止・Collector停止／再起動 | 未取得へ切替し、再接続後に復帰 |
| 今日／直近7日、3表示モード切替 | 行・凡例・合計が対応し、旧データも表示 |
| ホスト名保存をtrueにして未対応サイトへ | サイトのホスト名が独立した行になり、URLのパス・検索語は残らない |
| falseに戻しCollector再起動 | 新規の未対応サイトはその他のWeb、過去のホスト名行は残る |

参考: [Tabs API](https://developer.chrome.com/docs/extensions/reference/api/tabs)、[Windows API](https://developer.chrome.com/docs/extensions/reference/api/windows)、[Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)、[Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging)。
