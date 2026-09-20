import { readFocusedService } from "./services.js";
import { classifyNativeError } from "./diagnostics.js";

const host = "com.wintracker.browser";
const browser = /Edg\//.test(navigator.userAgent) ? "edge" : "chrome";
let port = null;
let generation = 0;
const diagnostic = { browser, state: "STARTING", attempts: 0, replies: 0,
  lastError: "NONE", lastErrorAt: null, lastReplyAt: null };

function status(state) {
  diagnostic.state = state;
  const connected = state === "CONNECTED";
  chrome.action.setBadgeText({ text: connected ? "" : "!" });
  chrome.action.setTitle({ title: connected ? "WinTracker: 接続中" :
    `WinTracker: 接続待ち (${diagnostic.lastError === "NONE" ? state : diagnostic.lastError})` });
}

function failed(connection, error) {
  if (port !== connection) return;
  port = null;
  generation++;
  diagnostic.lastError = classifyNativeError(error?.message);
  diagnostic.lastErrorAt = new Date().toISOString();
  status("DISCONNECTED");
  connection?.disconnect();
}

function send(connection, message) {
  try { connection.postMessage(message); return true; }
  catch (error) { failed(connection, error); return false; }
}

function connect() {
  if (port) return;
  diagnostic.attempts++;
  status("CONNECTING");
  let connection;
  try { connection = chrome.runtime.connectNative(host); }
  catch (error) { failed(null, error); return; }
  port = connection;
  connection.onDisconnect.addListener(() => {
    // Read lastError inside its callback, retaining only an allowlisted category.
    failed(connection, chrome.runtime.lastError);
  });
  connection.onMessage.addListener(async message => {
    if (port !== connection || !message) return;
    if (message.kind !== "ready" &&
        (message.kind !== "probe" || !/^[0-9a-f]{32}$/.test(message.requestId ?? ""))) return;
    diagnostic.replies++;
    diagnostic.lastReplyAt = new Date().toISOString();
    status("CONNECTED");
    if (message.kind === "ready") return;
    const capturedGeneration = generation;
    let serviceId = null;
    try { serviceId = await readFocusedService(chrome); } catch { /* closed/navigated during the read */ }
    if (port !== connection || capturedGeneration !== generation) return;
    send(connection, { kind: "sample", requestId: message.requestId,
      focused: serviceId !== null, serviceId });
  });
  if (send(connection, { kind: "hello", browser })) status("WAITING_FOR_COLLECTOR");
}

function changed() {
  generation++;
  if (port) send(port, { kind: "changed" });
  else connect();
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id) return false;
  if (message?.kind === "retry-connection") {
    const previous = port;
    port = null;
    generation++;
    previous?.disconnect();
    connect();
  } else if (message?.kind !== "get-diagnostics") return false;
  respond({ ...diagnostic });
  return false;
});

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
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === "reconnect") connect(); });
chrome.alarms.create("reconnect", { periodInMinutes: 0.5 });
connect();
