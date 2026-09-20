import test from "node:test";
import assert from "node:assert/strict";
import { classifyNativeError, formatDiagnostics } from "../diagnostics.js";

test("worker emits only service samples, invalidates asynchronous reads and reconnects", async () => {
  const event = () => ({ listeners: [], addListener(fn) { this.listeners.push(fn); } });
  const messages = [];
  const titles = [];
  const ports = [];
  const tab = { id: 1, windowId: 2, active: true, incognito: false, url: "https://mail.google.com/mail/u/0/#secret" };
  const api = {
    runtime: {
      id: "a".repeat(32), lastError: null, onStartup: event(), onInstalled: event(), onMessage: event(),
      connectNative(name) {
        assert.equal(name, "com.wintracker.browser");
        const port = { onDisconnect: event(), onMessage: event(), disconnected: false,
          disconnect() { this.disconnected = true; }, postMessage(value) { messages.push(value); } };
        ports.push(port);
        return port;
      }
    },
    action: { onClicked: event(), setBadgeText() {}, setTitle(value) { titles.push(value.title); } },
    alarms: { onAlarm: event(), create() {} },
    windows: { onFocusChanged: event(), getLastFocused: async () => ({ id: 2, type: "normal", focused: true }),
      get: async () => ({ focused: true }) },
    tabs: { onActivated: event(), onUpdated: event(), onRemoved: event(), onAttached: event(),
      onDetached: event(), onReplaced: event(), query: async () => [tab], get: async () => tab }
  };
  globalThis.chrome = api;
  try {
    await import("../background.js");
    assert.equal(messages[0].kind, "hello");
    const listener = ports[0].onMessage.listeners[0];
    await listener({ kind: "probe", requestId: "a".repeat(32) });
    assert.deepEqual(messages.at(-1), { kind: "sample", requestId: "a".repeat(32), focused: true, serviceId: "gmail" });
    assert.ok(!JSON.stringify(messages).includes("secret"));
    tab.url = "https://portal.example/private?secret=1";
    for (const consent of [undefined, false, "true", true, undefined]) {
      await listener({ kind: "probe", requestId: "a".repeat(32), storeBrowserHostnames: consent });
      assert.equal(messages.at(-1).serviceId, consent === true ? "host:portal.example" : "other-web");
    }
    assert.ok(!JSON.stringify(messages).includes("secret"));
    const before = messages.length;
    api.tabs.onUpdated.listeners[0](1, { url: "https://youtube.com/" }, { active: false });
    assert.equal(messages.length, before);

    let finishQuery;
    api.tabs.query = () => new Promise(resolve => { finishQuery = resolve; });
    const pending = listener({ kind: "probe", requestId: "b".repeat(32) });
    await new Promise(resolve => setImmediate(resolve));
    api.tabs.onActivated.listeners[0]();
    finishQuery([tab]);
    await pending;
    assert.equal(messages.at(-1).kind, "changed"); // no stale sample after selection changed

    ports[0].onDisconnect.listeners[0]();
    assert.match(titles.at(-1), /接続待ち/);
    api.alarms.onAlarm.listeners[0]({ name: "reconnect" });
    assert.equal(ports.length, 2);
    assert.equal(messages.at(-1).kind, "hello");

    const request = kind => {
      let response;
      api.runtime.onMessage.listeners[0]({ kind }, { id: api.runtime.id }, value => { response = value; });
      return response;
    };
    assert.equal(request("get-diagnostics").state, "WAITING_FOR_COLLECTOR");
    assert.equal(request("get-diagnostics").attempts, 2);
    api.runtime.lastError = { message: "Specified native messaging host not found. C:/private/secret" };
    ports[1].onDisconnect.listeners[0]();
    const diagnostic = request("get-diagnostics");
    assert.equal(diagnostic.lastError, "HOST_NOT_FOUND");
    assert.equal(diagnostic.state, "DISCONNECTED");
    assert.ok(diagnostic.lastErrorAt);
    assert.ok(!JSON.stringify(diagnostic).includes("secret"));
    assert.match(titles.at(-1), /HOST_NOT_FOUND/);

    request("retry-connection");
    assert.equal(ports.length, 3);
    assert.equal(request("get-diagnostics").attempts, 3);
    await ports[2].onMessage.listeners[0]({ kind: "ready" });
    assert.equal(request("get-diagnostics").state, "CONNECTED");
    assert.equal(request("get-diagnostics").replies, 8);
    assert.ok(request("get-diagnostics").lastReplyAt);
    // Delayed events from an old connection cannot change the new status.
    ports[1].onDisconnect.listeners[0]();
    await ports[1].onMessage.listeners[0]({ kind: "ready" });
    assert.equal(request("get-diagnostics").state, "CONNECTED");
    let responded = false;
    api.runtime.onMessage.listeners[0]({ kind: "retry-connection" }, { id: "unrelated" }, () => { responded = true; });
    assert.equal(responded, false);
    assert.equal(ports.length, 3);

    // A dead port and a synchronous connect exception are also visible, not uncaught.
    ports[2].postMessage = () => { throw new Error("Native host has exited. secret"); };
    api.tabs.onActivated.listeners[0]();
    assert.equal(request("get-diagnostics").lastError, "HOST_EXITED");
    api.runtime.connectNative = () => { throw new Error("Failed to start native messaging host. secret"); };
    request("retry-connection");
    assert.equal(request("get-diagnostics").lastError, "HOST_START_FAILED");
    assert.equal(request("get-diagnostics").state, "DISCONNECTED");
  } finally { delete globalThis.chrome; }
});

for (const [message, expected] of [
  [undefined, "DISCONNECTED"],
  ["Specified native messaging host not found.", "HOST_NOT_FOUND"],
  ["Access to the specified native messaging host is forbidden.", "HOST_FORBIDDEN"],
  ["Failed to start native messaging host.", "HOST_START_FAILED"],
  ["Native host has exited.", "HOST_EXITED"],
  ["Error when communicating with the native messaging host.", "HOST_IO_ERROR"],
  ["https://private.example/?secret=123", "UNKNOWN_NATIVE_ERROR"]
]) {
  test(`native failure classification: ${expected}`, () => {
    assert.equal(classifyNativeError(message), expected);
  });
}

test("diagnostic report excludes service samples, raw errors, URLs and extra fields", () => {
  const report = formatDiagnostics({ state: "DISCONNECTED", lastError: "HOST_EXITED",
    rawError: "secret", url: "secret", title: "secret", serviceId: "secret",
    samples: [{ page: "secret" }] }, "a".repeat(32), "0.3.0");
  assert.ok(!report.includes("secret"));
  assert.match(report, /lastError: HOST_EXITED/);
  assert.match(report, /diagnostics-1/);
});
