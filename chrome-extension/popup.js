const $ = (s) => document.querySelector(s);
$('#extensionId').textContent = chrome.runtime.id;

async function send(type, extra = {}) {
  try { return await chrome.runtime.sendMessage({ type, ...extra }); }
  catch (error) { return { ok: false, message: String(error) }; }
}

function show(message, ok = true) {
  $('#status').textContent = message;
  $('#status').style.color = ok ? '#9be7ae' : '#ffb0b0';
}

async function refresh() {
  const state = await send('capture.status');
  $('#captureState').textContent = state?.active ? 'ON' : 'OFF';
  $('#eventCount').textContent = String(state?.eventCount || state?.lastEventCount || 0);
  $('#startButton').disabled = !!state?.active;
  $('#reloadButton').disabled = !!state?.active;
  $('#stopButton').disabled = !state?.active;
  if (state?.lastError) show(state.lastError, false);
}

$('#startButton').addEventListener('click', async () => { const r = await send('capture.start', { reload: false }); show(r.message || 'Done', !!r.ok); await refresh(); });
$('#reloadButton').addEventListener('click', async () => { const r = await send('capture.start', { reload: true }); show(r.message || 'Done', !!r.ok); await refresh(); });
$('#stopButton').addEventListener('click', async () => { const r = await send('capture.stop'); show(r.message || 'Done', !!r.ok); await refresh(); });
$('#testButton').addEventListener('click', async () => { const r = await send('test-desktop-connection'); show(r?.message || 'No response', !!r?.ok); });
refresh();
setInterval(refresh, 1000);
