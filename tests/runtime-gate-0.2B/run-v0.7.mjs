import puppeteer from 'puppeteer';

const base = (process.argv[2] || 'http://127.0.0.1:7853').replace(/\/$/, '');
const browser = await puppeteer.launch({ headless: true, pipe: true });
const externalRequests = [];

try {
  const page = await browser.newPage();
  page.on('request', (request) => {
    const url = request.url();
    if (/^https?:/i.test(url) && url !== base && !url.startsWith(base + '/')) externalRequests.push(url);
  });

  await page.goto(`${base}/`, { waitUntil: 'networkidle0', timeout: 30000 });
  const link = await page.$('#recoveryLink');
  if (!link) throw new Error('Recovery link was not present in the offline document.');

  await Promise.all([
    page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 15000 }),
    link.click()
  ]);

  const text = await page.content();
  if (!text.includes('RECOVERED HELP RESOURCE')) throw new Error('Recovered resource did not load from Local Runtime.');
  if (!page.url().startsWith(base + '/recover/help.html')) throw new Error(`Unexpected recovery navigation URL: ${page.url()}`);
  if (externalRequests.length) throw new Error(`External HTTP request observed: ${externalRequests.join(', ')}`);

  console.log('PASS  Recovered link exists in offline HTML');
  console.log('PASS  Recovered resource opens in Chrome');
  console.log('PASS  Recovery navigation remains on Local Runtime');
  console.log('PASS  No external HTTP requests observed');
  console.log('RESULT: GATE 0.7 BROWSER PASS');
} finally {
  await browser.close();
}
