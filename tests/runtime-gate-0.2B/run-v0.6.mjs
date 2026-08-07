import puppeteer from 'puppeteer';

const base = (process.argv[2] || 'http://127.0.0.1:7852').replace(/\/$/, '');
const browser = await puppeteer.launch({ headless: true, pipe: true });
const externalRequests = [];

try {
  const page = await browser.newPage();
  page.on('request', (request) => {
    const url = request.url();
    if (/^https?:/i.test(url) && url !== base && !url.startsWith(base + '/')) externalRequests.push(url);
  });

  await page.goto(`${base}/?gate=0.3`, { waitUntil: 'networkidle0', timeout: 30000 });
  await page.waitForFunction(
    () => document.querySelector('#apiStatus')?.textContent === 'PASS',
    { timeout: 15000 }
  );

  const apiOutput = await page.$eval('#output', (element) => element.textContent || '');
  if (!apiOutput.includes('test-lab')) throw new Error('Offline page did not render the recorded API response.');
  if (externalRequests.length) throw new Error(`External HTTP request observed: ${externalRequests.join(', ')}`);

  console.log('PASS  Offline page loaded in Chrome');
  console.log('PASS  Client JavaScript executed');
  console.log('PASS  Recorded API response rendered');
  console.log('PASS  No external HTTP requests observed');
  console.log('RESULT: GATE 0.6 BROWSER PASS');
} finally {
  await browser.close();
}
