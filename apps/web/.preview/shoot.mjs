import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { startServer } from './serve.mjs';

/**
 * Photographs our own film at chosen moments, through the app's own playback and renderer, so a moment can be set
 * next to the same moment in a reference clip (`recordings/`).
 *
 *   node apps/web/.preview/shoot.mjs <presentation.json> <outDir> <name> <fromMs> <toMs> <stepMs>
 *
 * The presentation is a dump from `dotnet run -c Release --project tools/simulation-benchmarks -- replay 1 <seed> --dump <file>`.
 * `fromMs` and `toMs` are film milliseconds (the harness's `window.film.show`). One JPEG per step is written as
 * `<outDir>/<name>_000.jpg`, `<name>_001.jpg`, and so on. Tile them next to a reference sheet with ffmpeg: see
 * `recordings/reference-findings.md`, sections 8, 9 and 11.
 */

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(join(here, '../../../tests/web-e2e/package.json'));
const { chromium } = require('playwright');

const [presentation, outDir, name, from, to, step] = process.argv.slice(2);

if (!presentation || !outDir || !name || from === undefined || to === undefined || !step) {
  console.error(
    'usage: node shoot.mjs <presentation.json> <outDir> <name> <fromMs> <toMs> <stepMs>',
  );
  process.exit(2);
}

await mkdir(outDir, { recursive: true });

const { url, close } = await startServer({ presentation });
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });

await page.goto(url, { waitUntil: 'load' });
await page.waitForFunction(() => window.film !== undefined, undefined, { timeout: 30_000 });

const canvas = page.locator('#pitch');
let index = 0;

for (let at = Number(from); at <= Number(to); at += Number(step)) {
  await page.evaluate((value) => window.film.show(value), at);
  await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => done())));
  await canvas.screenshot({
    path: join(outDir, `${name}_${String(index).padStart(3, '0')}.jpg`),
    type: 'jpeg',
    quality: 92,
  });
  index += 1;
}

console.log(`${index} frame(s) in ${outDir}`);

await browser.close();
await close();
