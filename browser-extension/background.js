import { readFocusedService } from "./services.js";

const host = "com.wintracker.browser";
const browser = /Edg\//.test(navigator.userAgent) ? "edge" : "chrome";
let port = null;
let generation = 0;

function status(connected) {
  chrome.action.setBadgeText({ text: connected ? "" : "!" });
  chrome.action.setTitle({ title: connected ? "WinTracker: 接続中" : "WinTracker: Collectorへの接続待ち" });
}

function connect() {
  if (port) return;
  const connection = chrome.runtime.connectNative(host);
  port = connection;
  connection.onDisconnect.addListener(() => {
    // Consume the error without logging a potentially sensitive native payload.
    void chrome.runtime.lastError;
    if (port === connection) { port = null; generation++; status(false); }
  });
  connection.onMessage.addListener(async message => {
    if (message.kind === "ready") { status(true); return; }
    if (message.kind !== "probe" || !/^[0-9a-f]{32}$/.test(message.requestId ?? "")) return;
    const capturedGeneration = generation;
    let serviceId = null;
    try { serviceId = await readFocusedService(chrome); } catch { /* closed/navigated during the read */ }
    if (port !== connection || capturedGeneration !== generation) return;
    connection.postMessage({ kind: "sample", requestId: message.requestId,
      focused: serviceId !== null, serviceId });
    status(true);
  });
  connection.postMessage({ kind: "hello", browser });
}

function changed() {
  generation++;
  if (port) port.postMessage({ kind: "changed" });
  else connect();
}

chrome.tabs.onActivated.addListener(changed);
chrome.windows.onFocusChanged.addListener(changed);
chrome.tabs.onUpdated.addListener((_id, info, tab) => {
  if (tab.active && ("url" in info || "discarded" in info || info.status === "complete")) changed();
});
chrome.tabs.onRemoved.addListener(changed);
chrome.tabs.onAttached.addListener(changed);
chrome.tabs.onDetached.addListener(changed);
chrome.tabs.onReplaced.addListener(changed);
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
chrome.action.onClicked.addListener(changed);
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === "reconnect") connect(); });
chrome.alarms.create("reconnect", { periodInMinutes: 0.5 });
connect();
