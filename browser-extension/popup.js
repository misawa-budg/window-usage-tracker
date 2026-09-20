import { diagnosticHints, formatDiagnostics } from "./diagnostics.js";

const report = document.getElementById("report");
const hint = document.getElementById("hint");
const refresh = document.getElementById("refresh");
const retry = document.getElementById("retry");

async function update(kind = "get-diagnostics") {
  refresh.disabled = retry.disabled = true;
  let timeout;
  let snapshot;
  try {
    snapshot = await Promise.race([
      chrome.runtime.sendMessage({ kind }),
      new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error()), 3000); })
    ]);
    if (!snapshot || typeof snapshot.state !== "string") throw new Error();
  } catch {
    snapshot = { state: "WORKER_UNAVAILABLE", lastError: "WORKER_UNAVAILABLE" };
  } finally {
    clearTimeout(timeout);
    refresh.disabled = retry.disabled = false;
  }
  report.value = formatDiagnostics(snapshot, chrome.runtime.id, chrome.runtime.getManifest().version);
  hint.textContent = snapshot.state === "CONNECTED" ? "Collectorと接続中です。" :
    (diagnosticHints[snapshot.lastError] ?? diagnosticHints.UNKNOWN_NATIVE_ERROR);
}

refresh.addEventListener("click", () => update());
retry.addEventListener("click", () => update("retry-connection"));
report.addEventListener("click", () => report.select());
update();
