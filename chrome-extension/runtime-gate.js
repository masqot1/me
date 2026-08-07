const status = document.querySelector('#status');

(async () => {
  try {
    const result = await chrome.runtime.sendMessage({ type: 'runtime.gate.start', gate: '0.2B' });
    if (!result?.ok) throw new Error(result?.message || 'Runtime gate failed.');
    status.textContent = `PASS\n${result.message}\nEvents: ${result.eventCount}`;
    status.style.color = '#9be7ae';
  } catch (error) {
    status.textContent = `FAIL\n${String(error.message || error)}`;
    status.style.color = '#ffb0b0';
  }
})();
