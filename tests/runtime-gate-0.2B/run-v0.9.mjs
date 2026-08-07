import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import puppeteer from 'puppeteer';
import pixelmatch from 'pixelmatch';
import { PNG } from 'pngjs';

const sourceBase = (process.argv[2] || 'http://127.0.0.1:7843').replace(/\/$/, '');
const replayBase = (process.argv[3] || 'http://127.0.0.1:7854').replace(/\/$/, '');
const outputDir = path.resolve(process.argv[4] || './visual-output');
const maxMismatchPercent = 0.15;
fs.mkdirSync(outputDir, { recursive: true });

const browser = await puppeteer.launch({
  headless: true,
  pipe: true,
  defaultViewport: { width: 1280, height: 900, deviceScaleFactor: 1 }
});

async function render(base, name) {
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900, deviceScaleFactor: 1 });
  await page.emulateMediaFeatures([{ name: 'prefers-reduced-motion', value: 'reduce' }]);
  const allowedOrigin = new URL(base).origin;
  const externalRequests = [];
  page.on('request', (request) => {
    const url = request.url();
    if (/^https?:/i.test(url) && new URL(url).origin !== allowedOrigin) externalRequests.push(url);
  });

  await page.goto(`${base}/?visual=1`, { waitUntil: 'networkidle0', timeout: 30000 });
  await page.waitForFunction(() => document.querySelector('#apiStatus')?.textContent === 'PASS', { timeout: 15000 });
  await page.evaluate(async () => { if (document.fonts?.ready) await document.fonts.ready; });
  await new Promise((resolve) => setTimeout(resolve, 250));

  const apiOutput = await page.$eval('#output', (element) => element.textContent || '');
  if (!apiOutput.includes('test-lab')) throw new Error(`${name} page did not render the API fixture.`);
  const screenshotPath = path.join(outputDir, `${name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true });
  const dimensions = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight }));
  await page.close();
  return { screenshotPath, externalRequests: [...new Set(externalRequests)], dimensions };
}

try {
  const source = await render(sourceBase, 'source');
  const offline = await render(replayBase, 'offline');
  if (source.externalRequests.length) throw new Error(`Source Test Lab made unexpected external requests: ${source.externalRequests.join(', ')}`);
  if (offline.externalRequests.length) throw new Error(`Offline page made external requests: ${offline.externalRequests.join(', ')}`);

  const sourcePng = PNG.sync.read(fs.readFileSync(source.screenshotPath));
  const offlinePng = PNG.sync.read(fs.readFileSync(offline.screenshotPath));
  if (sourcePng.width !== offlinePng.width || sourcePng.height !== offlinePng.height) {
    throw new Error(`Screenshot dimensions differ: source=${sourcePng.width}x${sourcePng.height}, offline=${offlinePng.width}x${offlinePng.height}`);
  }

  const diff = new PNG({ width: sourcePng.width, height: sourcePng.height });
  const differentPixels = pixelmatch(
    sourcePng.data,
    offlinePng.data,
    diff.data,
    sourcePng.width,
    sourcePng.height,
    { threshold: 0.1, includeAA: false }
  );
  const totalPixels = sourcePng.width * sourcePng.height;
  const mismatchPercent = Number((differentPixels * 100 / totalPixels).toFixed(6));
  const diffPath = path.join(outputDir, 'diff.png');
  fs.writeFileSync(diffPath, PNG.sync.write(diff));

  const sha = (file) => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  const report = {
    version: '0.9.0',
    result: mismatchPercent <= maxMismatchPercent ? 'PASS' : 'FAIL',
    viewport: { width: 1280, height: 900, deviceScaleFactor: 1 },
    screenshot: { width: sourcePng.width, height: sourcePng.height },
    differentPixels,
    totalPixels,
    mismatchPercent,
    maxMismatchPercent,
    sourceSha256: sha(source.screenshotPath),
    offlineSha256: sha(offline.screenshotPath),
    sourceExternalRequests: source.externalRequests,
    offlineExternalRequests: offline.externalRequests,
    sourceDocumentDimensions: source.dimensions,
    offlineDocumentDimensions: offline.dimensions
  };
  const reportPath = path.join(outputDir, 'visual-report.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));

  console.log(`Different pixels: ${differentPixels}/${totalPixels}`);
  console.log(`Mismatch: ${mismatchPercent}% (max ${maxMismatchPercent}%)`);
  console.log(`Report: ${reportPath}`);
  if (report.result !== 'PASS') throw new Error(`Visual mismatch ${mismatchPercent}% exceeds ${maxMismatchPercent}% threshold.`);
  console.log('PASS  Source and offline screenshots rendered');
  console.log('PASS  Visual mismatch is within threshold');
  console.log('PASS  API-rendered state is present in both pages');
  console.log('PASS  Offline page made no external HTTP requests');
  console.log('RESULT: GATE 0.9 PASS');
} finally {
  await browser.close();
}
