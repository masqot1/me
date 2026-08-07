const status = document.querySelector('#status');

(async () => {
  try {
    const result = await chrome.runtime.sendMessage({ type: 'runtime.gate.start', gate: '0.3' });
    if (!result?.ok) throw new Error(result?.message || 'Runtime Gate 0.3 failed.');
    status.textContent = `PASS\n${result.message}\nEvents: ${result.eventCount}`;
    status.style.color = '#9be7ae';
  } catch (error) {
    status.textContent = `FAIL\n${String(error.message || error)}`;
    status.style.color = '#ffb0b0';
  }
})();
