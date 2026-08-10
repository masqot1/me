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

async function runRequestPayloadGate() {
  const endpoint = '/' + ['api', 'echo'].join('/');
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-cache',
      'Authorization': 'Bearer RUNTIME-AUTH-HEADER-MUST-NOT-PERSIST',
      'X-API-Key': 'RUNTIME-APIKEY-HEADER-MUST-NOT-PERSIST'
    },
    body: JSON.stringify({
      gate: '1.2',
      source: 'real-chrome-runtime',
      username: 'runtime-user',
      password: 'RUNTIME-PASSWORD-MUST-NOT-PERSIST',
      nested: { access_token: 'RUNTIME-TOKEN-MUST-NOT-PERSIST' }
    })
  });
  if (!response.ok) throw new Error(`Request payload gate returned HTTP ${response.status}`);
  await response.json();
}

button.addEventListener('click', runSampleApi);

const params = new URLSearchParams(location.search);
const gate = params.get('gate');
if (gate === '0.2B' || gate === '0.3' || params.get('visual') === '1') {
  setTimeout(runSampleApi, 1200);
}
if (gate === '0.3') {
  setTimeout(() => runRequestPayloadGate().catch((error) => console.error('Gate 1.2/1.3 runtime fixture failed', error)), 1800);
}
