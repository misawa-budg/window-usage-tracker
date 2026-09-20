// Never expose raw browser errors: they may contain local paths or page data.
export function classifyNativeError(message) {
  if (typeof message !== "string" || !message) return "DISCONNECTED";
  if (/host.*not found|host.*not registered/i.test(message)) return "HOST_NOT_FOUND";
  if (/forbidden|not allowed|access.*denied/i.test(message)) return "HOST_FORBIDDEN";
  if (/failed to start|could not.*start/i.test(message)) return "HOST_START_FAILED";
  if (/host has exited/i.test(message)) return "HOST_EXITED";
  if (/communicat|invalid.*message/i.test(message)) return "HOST_IO_ERROR";
  return "UNKNOWN_NATIVE_ERROR";
}

export const diagnosticHints = {
  NONE: "エラーは記録されていません。",
  DISCONNECTED: "接続が切れました。再接続を試してください。",
  HOST_NOT_FOUND: "ホスト登録とマニフェストのパスを確認してください。",
  HOST_FORBIDDEN: "拡張IDの許可設定、または組織のポリシーを確認してください。",
  HOST_START_FAILED: "中継exeの存在と実行環境を確認してください。",
  HOST_EXITED: "中継が終了しました。Collectorの起動と連携設定を確認してください。",
  HOST_IO_ERROR: "中継との通信が失敗しました。",
  UNKNOWN_NATIVE_ERROR: "分類できない接続エラーです。生のエラー文は保存していません。",
  WORKER_UNAVAILABLE: "拡張の処理から応答がありません。拡張を再読み込みしてください。"
};

export function formatDiagnostics(snapshot, extensionId, version) {
  // Explicit field selection keeps service samples and error payloads out of reports.
  return [
    "WinTracker diagnostics-1",
    `version: ${version}`, `extensionId: ${extensionId}`,
    `browser: ${snapshot.browser ?? "unknown"}`,
    `state: ${snapshot.state ?? "WORKER_UNAVAILABLE"}`,
    `attempts: ${snapshot.attempts ?? 0}`,
    `replies: ${snapshot.replies ?? 0}`,
    `lastError: ${snapshot.lastError ?? "WORKER_UNAVAILABLE"}`,
    `lastErrorAt: ${snapshot.lastErrorAt ?? "-"}`,
    `lastReplyAt: ${snapshot.lastReplyAt ?? "-"}`
  ].join("\n");
}
