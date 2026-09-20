import test from "node:test";
import assert from "node:assert/strict";
import { classifyService, readFocusedService } from "../services.js";

for (const [url, expected] of [
  ["https://www.youtube.com/watch?v=secret", "youtube"],
  ["https://music.youtube.com/", "youtube"],
  ["https://www.twitch.tv/private-channel", "twitch"],
  ["https://mail.google.com/mail/u/1/#inbox/private", "gmail"],
  ["https://drive.google.com/", "other-web"],
  ["https://github.com/private/repo", "github"],
  ["https://youtube.com.evil.example/", "other-web"],
  ["https://notyoutube.com/", "other-web"],
  ["https://example.com/?next=https://youtube.com", "other-web"],
  ["https://youtube.com@evil.example/", "other-web"],
  ["file:///C:/private.txt", null], ["chrome://settings", null],
  ["edge://newtab", null], [undefined, null], ["invalid", null]
]) test(`classification: ${url}`, () => assert.equal(classifyService(url), expected));

for (const [url, expected] of [
  ["https://www.netflix.com/watch/private", "netflix"],
  ["https://video.unext.jp/play/private", "unext"],
  ["https://chatgpt.com/c/private", "chatgpt"],
  ["https://chat.openai.com/", "chatgpt"],
  ["https://claude.ai/chat/private", "claude"],
  ["https://gemini.google.com/app/private", "gemini"]
]) test(`friendly service independent of host opt-in: ${expected}`, () => {
  assert.equal(classifyService(url), expected);
  assert.equal(classifyService(url, true), expected);
});

for (const [url, expected] of [
  ["https://keio.jp/private?q=secret", "host:keio.jp"],
  ["https://portal.keio.jp/", "host:portal.keio.jp"],
  ["https://drive.google.com/", "host:drive.google.com"],
  ["https://user:password@PORTAL.EXAMPLE.:8443/path?secret=1#fragment", "host:portal.example"],
  ["https://例え.example/", "host:xn--r8jz45g.example"],
  ["https://chatgpt.com.evil.example/", "host:chatgpt.com.evil.example"],
  ["http://127.0.0.1/", "other-web"], ["http://2130706433/", "other-web"],
  ["http://[::1]/", "other-web"], ["http://localhost/", "other-web"],
  ["https://bad_host.example/", "other-web"],
  ["https://-bad.example/", "other-web"]
]) test(`unknown host requires explicit opt-in: ${url}`, () => {
  assert.equal(classifyService(url), "other-web");
  assert.equal(classifyService(url, "true"), "other-web");
  assert.equal(classifyService(url, true), expected);
});

function fake({ focused = true, incognito = false, discarded = false, moved = false, lostFocus = false, count = 1, splitViewId = -1 } = {}) {
  const tab = { id: 10, windowId: 20, active: true, incognito, discarded, splitViewId, url: "https://mail.google.com/mail/u/0" };
  return {
    windows: {
      getLastFocused: async () => ({ id: 20, type: "normal", focused, incognito }),
      get: async () => ({ focused: !lostFocus, incognito })
    },
    tabs: {
      query: async () => Array.from({ length: count }, () => tab),
      get: async () => ({ ...tab, windowId: moved ? 21 : 20 })
    }
  };
}

test("reports service only for a focused selected tab", async () => {
  assert.equal(await readFocusedService(fake()), "gmail");
});
for (const options of [{ focused: false }, { incognito: true }, { discarded: true },
  { moved: true }, { lostFocus: true }, { count: 0 }, { count: 2 }, { splitViewId: 12 }]) {
  test(`does not attribute ambiguous/background/private tab: ${JSON.stringify(options)}`, async () => {
    assert.equal(await readFocusedService(fake(options)), null);
  });
}
