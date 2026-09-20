import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

test("popup shows safe diagnostics, retries and handles an unavailable worker", async () => {
  const elements = Object.fromEntries(["report", "hint", "refresh", "retry"].map(id => [id, {
    listeners: {}, addEventListener(name, listener) { this.listeners[name] = listener; },
    select() { this.selected = true; }
  }]));
  const messages = [];
  let fail = false;
  globalThis.document = { getElementById: id => elements[id] };
  globalThis.chrome = { runtime: {
    id: "a".repeat(32), getManifest: () => ({ version: "0.3.0" }),
    async sendMessage(message) {
      messages.push(message);
      if (fail) throw new Error("private page title / local path");
      return { state: "DISCONNECTED", lastError: "HOST_NOT_FOUND", attempts: 1 };
    }
  } };
  try {
    await import("../popup.js");
    await new Promise(resolve => setImmediate(resolve));
    assert.match(elements.report.value, /HOST_NOT_FOUND/);
    assert.match(elements.hint.textContent, /ホスト登録/);
    await elements.retry.listeners.click();
    assert.equal(messages.at(-1).kind, "retry-connection");
    fail = true;
    await elements.refresh.listeners.click();
    assert.match(elements.report.value, /WORKER_UNAVAILABLE/);
    assert.ok(!elements.report.value.includes("private"));
    assert.equal(elements.retry.disabled, false);
    assert.equal(elements.refresh.disabled, false);
    globalThis.chrome.runtime.sendMessage = async () => ({ state: "CONNECTED", lastError: "HOST_EXITED" });
    await elements.refresh.listeners.click();
    assert.match(elements.hint.textContent, /接続中/);
    globalThis.chrome.runtime.sendMessage = () => new Promise(() => {});
    await elements.refresh.listeners.click();
    assert.match(elements.report.value, /WORKER_UNAVAILABLE/);
    assert.equal(elements.retry.disabled, false);
    elements.report.listeners.click();
    assert.equal(elements.report.selected, true);
  } finally {
    delete globalThis.document;
    delete globalThis.chrome;
  }
});

test("popup assets are wired and packaged without new extension permissions", async () => {
  const manifest = JSON.parse(await readFile(new URL("../manifest.json", import.meta.url), "utf8"));
  assert.equal(manifest.action.default_popup, "popup.html");
  assert.deepEqual(manifest.permissions, ["tabs", "nativeMessaging", "alarms"]);
  const html = await readFile(new URL("../popup.html", import.meta.url), "utf8");
  assert.match(html, /type="module" src="popup.js"/);
  const release = await readFile(new URL("../../release.ps1", import.meta.url), "utf8");
  const packageCheck = await readFile(new URL("../../scripts/Test-ReleasePackages.ps1", import.meta.url), "utf8");
  for (const file of ["popup.html", "popup.js", "diagnostics.js"]) {
    assert.ok(release.includes(`"${file}"`));
    assert.ok(packageCheck.includes(`"browser-extension/${file}"`));
  }
});
