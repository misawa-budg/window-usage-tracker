import test from "node:test";
import assert from "node:assert/strict";

test("worker emits only service samples, invalidates asynchronous reads and reconnects", async () => {
  const event = () => ({ listeners: [], addListener(fn) { this.listeners.push(fn); } });
  const messages = [];
  const titles = [];
  const ports = [];
  const tab = { id: 1, windowId: 2, active: true, incognito: false, url: "https://mail.google.com/mail/u/0/#secret" };
  const api = {
    runtime: {
      lastError: null, onStartup: event(), onInstalled: event(),
      connectNative(name) {
        assert.equal(name, "com.wintracker.browser");
        const port = { onDisconnect: event(), onMessage: event(), postMessage(value) { messages.push(value); } };
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
  } finally { delete globalThis.chrome; }
});
