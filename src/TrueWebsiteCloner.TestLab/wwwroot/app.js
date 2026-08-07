const button = document.querySelector('#apiButton');
const output = document.querySelector('#output');
const status = document.querySelector('#apiStatus');

async function runSampleApi() {
  status.textContent = 'Loading…';
  try {
    const response = await fetch('/api/sample');
    const data = await response.json();
    output.textContent = JSON.stringify(data, null, 2);
    status.textContent = response.ok ? 'PASS' : `HTTP ${response.status}`;
  } catch (error) {
    status.textContent = 'FAIL';
    output.textContent = String(error);
  }
}

button.addEventListener('click', runSampleApi);

const gate = new URLSearchParams(location.search).get('gate');
if (gate === '0.2B' || gate === '0.3') {
  setTimeout(runSampleApi, 1200);
}
