import puppeteer from 'puppeteer';
import path from 'node:path';

const root = process.env.GITHUB_WORKSPACE || path.resolve('../..');
const extensionPath = path.join(root, 'chrome-extension');
const extensionId = 'ggcmdgdiopplpbcfinamhjdkbhiknfbk';

const browser = await puppeteer.launch({
  headless: true,
  pipe: true,
  enableExtensions: [extensionPath]
});

try {
  const workerTarget = await browser.waitForTarget(
    (target) => target.type() === 'service_worker' && target.url().endsWith('/service-worker.js'),
    { timeout: 30000 }
  );
  if (!workerTarget) throw new Error('TrueWebsiteCloner extension service worker did not start.');

  const page = await browser.newPage();
  await page.goto(`chrome-extension://${extensionId}/runtime-gate-0.3.html`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForFunction(
    () => {
      const text = document.querySelector('#status')?.textContent || '';
      return text.startsWith('PASS') || text.startsWith('FAIL');
    },
    { timeout: 50000 }
  );

  const result = await page.$eval('#status', (el) => el.textContent || '');
  console.log(result);
  if (!result.startsWith('PASS')) throw new Error(`Extension runtime Gate 0.3 failed: ${result}`);
} finally {
  await browser.close();
}
