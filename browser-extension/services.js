// Only this fixed vocabulary leaves the extension. Do not send URLs, titles,
// account names, paths, search strings, tab IDs or browser profile names.
export function classifyService(rawUrl) {
  try {
    const url = new URL(rawUrl);
    if (url.protocol !== "https:" && url.protocol !== "http:") return null;
    const host = url.hostname.toLowerCase();
    const under = domain => host === domain || host.endsWith("." + domain);
    if (under("youtube.com")) return "youtube";
    if (under("twitch.tv")) return "twitch";
    if (host === "mail.google.com") return "gmail";
    if (host === "github.com") return "github";
    return "other-web";
  } catch {
    return null;
  }
}

// Two reads reduce focus/navigation races. Events invalidate pending reads as well.
// This reports selection, not visibility, playback, attention or productivity.
export async function readFocusedService(api) {
  const window = await api.windows.getLastFocused();
  if (!window.focused || window.incognito || window.type !== "normal") return null;
  const tabs = await api.tabs.query({ active: true, windowId: window.id });
  if (tabs.length !== 1 || tabs[0].incognito || tabs[0].discarded) return null;
  const [currentWindow, tab] = await Promise.all([
    api.windows.get(window.id), api.tabs.get(tabs[0].id)
  ]);
  if (!currentWindow.focused || currentWindow.incognito || !tab.active || tab.incognito ||
      tab.discarded || tab.windowId !== window.id) return null;
  // Where exposed, do not attribute split views to just one of their visible tabs.
  if (Number.isInteger(tab.splitViewId) && tab.splitViewId !== -1) return null;
  return classifyService(tab.url);
}
