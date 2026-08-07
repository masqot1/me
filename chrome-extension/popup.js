const extensionId = document.querySelector('#extensionId');
const button = document.querySelector('#testButton');
const status = document.querySelector('#status');
extensionId.textContent = chrome.runtime.id;

button.addEventListener('click', async () => {
  button.disabled = true;
  status.textContent = 'Testing…';
  try {
    const response = await chrome.runtime.sendMessage({ type: 'test-desktop-connection' });
    if (response?.ok) {
      status.textContent = `PASS\n${response.message}`;
      status.style.color = '#9be7ae';
    } else {
      status.textContent = `FAIL\n${response?.message ?? 'Unknown error'}`;
      status.style.color = '#ffb0b0';
    }
  } catch (error) {
    status.textContent = `FAIL\n${String(error)}`;
    status.style.color = '#ffb0b0';
  } finally {
    button.disabled = false;
  }
});
