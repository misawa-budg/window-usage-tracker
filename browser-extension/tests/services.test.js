import test from "node:test";
import assert from "node:assert/strict";
import { classifyService, readFocusedService } from "../services.js";

import { readFileSync } from "node:fs";

const cases = JSON.parse(readFileSync(new URL("../../tests/browser-services.json", import.meta.url), "utf8"));
for (const sample of cases) {
  test(`shared classification contract: ${sample.url}`, () => {
    assert.equal(classifyService(sample.url), sample.defaultId);
    assert.equal(classifyService(sample.url, false), sample.defaultId);
    assert.equal(classifyService(sample.url, "true"), sample.defaultId);
    assert.equal(classifyService(sample.url, true), sample.hostId);
  });
}
test("missing URL is not attributed", () => assert.equal(classifyService(undefined, true), null));

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
