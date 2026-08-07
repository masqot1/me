const NATIVE_HOST = 'com.truewebsitecloner.host';
const DEBUGGER_VERSION = '1.3';
let nativePort = null;
let activeCapture = null;
let eventCount = 0;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

chrome.storage.local.get('captureState').then(({ captureState }) => {
  if (captureState?.active) {
    activeCapture = captureState;
    eventCount = captureState.eventCount || 0;
  }
});

function ensureNativePort() {
  if (nativePort) return nativePort;
  nativePort = chrome.runtime.connectNative(NATIVE_HOST);
  nativePort.onMessage.addListener((message) => {
    if (message?.ok === false) setLastError(message.message || 'Native bridge error');
  });
  nativePort.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError?.message;
    nativePort = null;
    if (error) setLastError(error);
  });
  return nativePort;
}

function postNative(message) {
  try {
    ensureNativePort().postMessage(message);
    return true;
  } catch (error) {
    setLastError(String(error));
    return false;
  }
}

async function persistState(extra = {}) {
  const state = activeCapture
    ? { ...activeCapture, active: true, eventCount, ...extra }
    : { active: false, eventCount: 0, ...extra };
  await chrome.storage.local.set({ captureState: state });
  return state;
}

async function setLastError(message) {
  await persistState({ lastError: message });
}

async function getActiveTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id) throw new Error('No active tab was found.');
  if (!/^https?:/i.test(tab.url || '')) throw new Error('Only http:// and https:// tabs can be captured.');
  return tab;
}

async function waitForTabComplete(tabId, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const tab = await chrome.tabs.get(tabId);
    if (tab.status === 'complete') return tab;
    await sleep(150);
  }
  throw new Error(`Tab ${tabId} did not finish loading.`);
}

async function startCaptureForTab(tab, reload) {
  if (activeCapture) return { ok: false, message: 'A capture is already active.' };
  if (!tab?.id || !/^https?:/i.test(tab.url || '')) return { ok: false, message: 'Target tab is not capturable.' };

  ensureNativePort();
  await chrome.debugger.attach({ tabId: tab.id }, DEBUGGER_VERSION);
  try {
    await chrome.debugger.sendCommand({ tabId: tab.id }, 'Network.enable', {});
  } catch (error) {
    await chrome.debugger.detach({ tabId: tab.id }).catch(() => {});
    throw error;
  }

  eventCount = 0;
  activeCapture = { tabId: tab.id, url: tab.url || '', title: tab.title || '', startedAt: new Date().toISOString() };
  await persistState({ lastError: null });
  postNative({ type: 'capture.start', tabId: tab.id, targetUrl: tab.url || '', title: tab.title || '', startedAt: activeCapture.startedAt });
  if (reload) await chrome.tabs.reload(tab.id);
  return { ok: true, message: reload ? 'Capture started and tab reloaded.' : 'Capture started.', state: await persistState() };
}

async function startCapture(reload) {
  return startCaptureForTab(await getActiveTab(), reload);
}

async function stopCapture(reason = 'user') {
  if (!activeCapture) return { ok: false, message: 'No active capture.' };
  const capture = activeCapture;
  activeCapture = null;
  try { await chrome.debugger.detach({ tabId: capture.tabId }); } catch (_) {}
  postNative({ type: 'capture.stop', tabId: capture.tabId, reason, stoppedAt: new Date().toISOString() });
  const count = eventCount;
  eventCount = 0;
  await persistState({ lastEventCount: count, lastError: null });
  return { ok: true, message: `Capture stopped. ${count} metadata events sent.`, eventCount: count };
}

function sendCaptureEvent(message) {
  if (!activeCapture) return;
  eventCount += 1;
  postNative(message);
  persistState().catch(() => {});
}

async function runRuntimeGate() {
  if (activeCapture) return { ok: false, message: 'Another capture is already active.' };
  let targetId = null;
  try {
    const created = await chrome.tabs.create({ url: 'http://127.0.0.1:7843/?gate=0.2B', active: false });
    if (!created.id) throw new Error('Could not create Test Lab tab.');
    targetId = created.id;
    const target = await waitForTabComplete(targetId);
    const start = await startCaptureForTab(target, true);
    if (!start.ok) return start;
    await sleep(6500);
    const stop = await stopCapture('runtime-gate-0.2B');
    if (!stop.ok) return stop;
    return { ok: true, message: 'Real Chrome capture completed.', eventCount: stop.eventCount, tabId: targetId };
  } finally {
    if (targetId !== null) await chrome.tabs.remove(targetId).catch(() => {});
  }
}

chrome.debugger.onEvent.addListener((source, method, params) => {
  if (!activeCapture || source.tabId !== activeCapture.tabId) return;
  const tabId = source.tabId;
  if (method === 'Network.requestWillBeSent') {
    sendCaptureEvent({ type: 'capture.request', tabId, requestId: params.requestId, loaderId: params.loaderId, url: params.request?.url, method: params.request?.method, resourceType: params.type, documentUrl: params.documentURL, timestamp: params.timestamp, wallTime: params.wallTime });
  } else if (method === 'Network.responseReceived') {
    const r = params.response || {};
    sendCaptureEvent({ type: 'capture.response', tabId, requestId: params.requestId, url: r.url, status: r.status, statusText: r.statusText, mimeType: r.mimeType, resourceType: params.type, protocol: r.protocol, fromDiskCache: !!r.fromDiskCache, fromServiceWorker: !!r.fromServiceWorker, encodedDataLength: r.encodedDataLength, timing: r.timing, timestamp: params.timestamp });
  } else if (method === 'Network.loadingFinished') {
    sendCaptureEvent({ type: 'capture.finished', tabId, requestId: params.requestId, encodedDataLength: params.encodedDataLength, timestamp: params.timestamp });
  } else if (method === 'Network.loadingFailed') {
    sendCaptureEvent({ type: 'capture.failed', tabId, requestId: params.requestId, errorText: params.errorText, canceled: !!params.canceled, blockedReason: params.blockedReason, resourceType: params.type, timestamp: params.timestamp });
  }
});

chrome.debugger.onDetach.addListener((source, reason) => {
  if (!activeCapture || source.tabId !== activeCapture.tabId) return;
  const tabId = activeCapture.tabId;
  activeCapture = null;
  postNative({ type: 'capture.stop', tabId, reason: `debugger-detached:${reason}`, stoppedAt: new Date().toISOString() });
  eventCount = 0;
  persistState({ lastError: `Debugger detached: ${reason}` }).catch(() => {});
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === 'test-desktop-connection') {
    chrome.runtime.sendNativeMessage(NATIVE_HOST, { type: 'foundation.ping', extensionId: chrome.runtime.id, extensionVersion: chrome.runtime.getManifest().version, sentAt: new Date().toISOString() }, (response) => {
      const error = chrome.runtime.lastError?.message;
      sendResponse(error ? { ok: false, message: error } : (response ?? { ok: false, message: 'Empty native response.' }));
    });
    return true;
  }
  if (message?.type === 'capture.start') {
    startCapture(!!message.reload).then(sendResponse).catch((error) => sendResponse({ ok: false, message: String(error.message || error) }));
    return true;
  }
  if (message?.type === 'capture.stop') {
    stopCapture('user').then(sendResponse).catch((error) => sendResponse({ ok: false, message: String(error.message || error) }));
    return true;
  }
  if (message?.type === 'capture.status') {
    chrome.storage.local.get('captureState').then(({ captureState }) => sendResponse(captureState || { active: false }));
    return true;
  }
  if (message?.type === 'runtime.gate.start' && message?.gate === '0.2B') {
    runRuntimeGate().then(sendResponse).catch((error) => sendResponse({ ok: false, message: String(error.message || error) }));
    return true;
  }
  return false;
});
