const NATIVE_HOST = 'com.truewebsitecloner.host';
const DEBUGGER_VERSION = '1.3';
const MAX_BODY_BYTES = 512 * 1024;
let nativePort = null;
let activeCapture = null;
let eventCount = 0;
const requestInfo = new Map();
const responseInfo = new Map();
const pendingBodyTasks = new Set();

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

function normalizedMime(value) {
  return String(value || '').split(';', 1)[0].trim().toLowerCase();
}

function allowedMime(mime) {
  return mime.startsWith('text/') || [
    'application/json', 'application/ld+json', 'application/javascript', 'application/x-javascript',
    'image/svg+xml', 'image/png', 'image/jpeg', 'image/webp', 'image/gif'
  ].includes(mime);
}

function sameOrigin(urlA, urlB) {
  try { return new URL(urlA).origin === new URL(urlB).origin; }
  catch { return false; }
}

function bodyByteLength(body, base64Encoded) {
  if (!base64Encoded) return new TextEncoder().encode(body).length;
  const padding = body.endsWith('==') ? 2 : body.endsWith('=') ? 1 : 0;
  return Math.max(0, Math.floor(body.length * 3 / 4) - padding);
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

  requestInfo.clear();
  responseInfo.clear();
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

function sendCaptureEvent(message) {
  if (!activeCapture) return;
  eventCount += 1;
  postNative(message);
  persistState().catch(() => {});
}

function shouldCaptureBody(info) {
  if (!activeCapture || !info) return false;
  if (info.method !== 'GET') return false;
  if (info.status < 200 || info.status >= 400) return false;
  if (!sameOrigin(activeCapture.url, info.url)) return false;
  return allowedMime(info.mimeType);
}

async function captureResponseBody(source, requestId) {
  const info = responseInfo.get(requestId);
  if (!shouldCaptureBody(info)) return;
  try {
    const result = await chrome.debugger.sendCommand(source, 'Network.getResponseBody', { requestId });
    const body = result?.body;
    if (typeof body !== 'string') return;
    const base64Encoded = !!result.base64Encoded;
    const byteLength = bodyByteLength(body, base64Encoded);
    if (byteLength > MAX_BODY_BYTES) return;
    sendCaptureEvent({
      type: 'capture.body',
      tabId: source.tabId,
      requestId,
      url: info.url,
      mimeType: info.mimeType,
      resourceType: info.resourceType,
      status: info.status,
      base64Encoded,
      byteLength,
      body
    });
  } catch (_) {
    // Some responses cannot expose a body through CDP; metadata remains valid.
  }
}

async function stopCapture(reason = 'user') {
  if (!activeCapture) return { ok: false, message: 'No active capture.' };
  if (pendingBodyTasks.size) await Promise.allSettled([...pendingBodyTasks]);
  const capture = activeCapture;
  activeCapture = null;
  try { await chrome.debugger.detach({ tabId: capture.tabId }); } catch (_) {}
  postNative({ type: 'capture.stop', tabId: capture.tabId, reason, stoppedAt: new Date().toISOString() });
  const count = eventCount;
  eventCount = 0;
  requestInfo.clear();
  responseInfo.clear();
  await persistState({ lastEventCount: count, lastError: null });
  return { ok: true, message: `Capture stopped. ${count} events sent.`, eventCount: count };
}

async function runRuntimeGate(gate) {
  if (activeCapture) return { ok: false, message: 'Another capture is already active.' };
  let targetId = null;
  try {
    const created = await chrome.tabs.create({ url: `http://127.0.0.1:7843/?gate=${encodeURIComponent(gate)}`, active: false });
    if (!created.id) throw new Error('Could not create Test Lab tab.');
    targetId = created.id;
    const target = await waitForTabComplete(targetId);
    const start = await startCaptureForTab(target, true);
    if (!start.ok) return start;
    await sleep(gate === '0.3' ? 7500 : 6500);
    const stop = await stopCapture(`runtime-gate-${gate}`);
    if (!stop.ok) return stop;
    return { ok: true, message: `Real Chrome ${gate} capture completed.`, eventCount: stop.eventCount, tabId: targetId };
  } finally {
    if (targetId !== null) await chrome.tabs.remove(targetId).catch(() => {});
  }
}

chrome.debugger.onEvent.addListener((source, method, params) => {
  if (!activeCapture || source.tabId !== activeCapture.tabId) return;
  const tabId = source.tabId;
  if (method === 'Network.requestWillBeSent') {
    requestInfo.set(params.requestId, { method: params.request?.method || '', url: params.request?.url || '' });
    sendCaptureEvent({ type: 'capture.request', tabId, requestId: params.requestId, loaderId: params.loaderId, url: params.request?.url, method: params.request?.method, resourceType: params.type, documentUrl: params.documentURL, timestamp: params.timestamp, wallTime: params.wallTime });
  } else if (method === 'Network.responseReceived') {
    const r = params.response || {};
    const request = requestInfo.get(params.requestId) || {};
    const info = {
      method: request.method || '',
      url: r.url || request.url || '',
      status: Number(r.status || 0),
      mimeType: normalizedMime(r.mimeType),
      resourceType: params.type || ''
    };
    responseInfo.set(params.requestId, info);
    sendCaptureEvent({ type: 'capture.response', tabId, requestId: params.requestId, url: r.url, status: r.status, statusText: r.statusText, mimeType: r.mimeType, resourceType: params.type, protocol: r.protocol, fromDiskCache: !!r.fromDiskCache, fromServiceWorker: !!r.fromServiceWorker, encodedDataLength: r.encodedDataLength, timing: r.timing, timestamp: params.timestamp });
  } else if (method === 'Network.loadingFinished') {
    sendCaptureEvent({ type: 'capture.finished', tabId, requestId: params.requestId, encodedDataLength: params.encodedDataLength, timestamp: params.timestamp });
    const task = captureResponseBody({ tabId }, params.requestId)
      .finally(() => {
        pendingBodyTasks.delete(task);
        requestInfo.delete(params.requestId);
        responseInfo.delete(params.requestId);
      });
    pendingBodyTasks.add(task);
  } else if (method === 'Network.loadingFailed') {
    sendCaptureEvent({ type: 'capture.failed', tabId, requestId: params.requestId, errorText: params.errorText, canceled: !!params.canceled, blockedReason: params.blockedReason, resourceType: params.type, timestamp: params.timestamp });
    requestInfo.delete(params.requestId);
    responseInfo.delete(params.requestId);
  }
});

chrome.debugger.onDetach.addListener((source, reason) => {
  if (!activeCapture || source.tabId !== activeCapture.tabId) return;
  const tabId = activeCapture.tabId;
  activeCapture = null;
  postNative({ type: 'capture.stop', tabId, reason: `debugger-detached:${reason}`, stoppedAt: new Date().toISOString() });
  eventCount = 0;
  requestInfo.clear();
  responseInfo.clear();
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
  if (message?.type === 'runtime.gate.start' && ['0.2B', '0.3'].includes(message?.gate)) {
    runRuntimeGate(message.gate).then(sendResponse).catch((error) => sendResponse({ ok: false, message: String(error.message || error) }));
    return true;
  }
  return false;
});
