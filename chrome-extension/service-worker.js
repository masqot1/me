const NATIVE_HOST = 'com.truewebsitecloner.host';

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== 'test-desktop-connection') return false;

  chrome.runtime.sendNativeMessage(
    NATIVE_HOST,
    {
      type: 'foundation.ping',
      extensionId: chrome.runtime.id,
      extensionVersion: chrome.runtime.getManifest().version,
      sentAt: new Date().toISOString()
    },
    (response) => {
      const error = chrome.runtime.lastError?.message;
      if (error) {
        sendResponse({ ok: false, type: 'native_messaging_error', message: error });
        return;
      }
      sendResponse(response ?? { ok: false, type: 'empty_response', message: 'Native host returned no response.' });
    }
  );
  return true;
});
