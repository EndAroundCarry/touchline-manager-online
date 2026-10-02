import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Drives the Stage 6 harness in headless Chromium: seeks to the moments worth looking at, writes a PNG of
 * each, and reports the frame rate, the renderer's own per-frame cost, and heap growth over a long run.
 */

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(join(here, '../../../tests/web-e2e/package.json'));
const { chromium } = require('playwright');

const url = process.argv[2] ?? 'http://127.0.0.1:8123/stage6-demo.html';
const outputDirectory = join(here, 'shots');

await mkdir(outputDirectory, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });

await page.goto(url, { waitUntil: 'load' });
await page.waitForTimeout(1500);

// Measure the real frame rate for two seconds, the way the 60 FPS checkpoint asks.
const frameRate = await page.evaluate(
  () =>
    new Promise((resolve) => {
      let frames = 0;
      const start = performance.now();

      function tick(now) {
        frames += 1;

        if (now - start >= 2000) {
          resolve({ frames, milliseconds: now - start, fps: (frames * 1000) / (now - start) });
          return;
        }

        requestAnimationFrame(tick);
      }

      requestAnimationFrame(tick);
    }),
);

const heapBefore = await page.evaluate(() => performance.memory?.usedJSHeapSize ?? 0);

const moments = [
  { name: 'kickoff', at: 2000 },
  { name: 'strike', at: 11000 },
  { name: 'flight', at: 11800 },
  { name: 'celebration', at: 12600 },
  { name: 'recovered', at: 17500 },
];

const stats = [];
const canvas = page.locator('#pitch');

for (const moment of moments) {
  const metrics = await page.evaluate(async (at) => {
    window.stage6.render(at);
    await new Promise((resolve) => requestAnimationFrame(resolve));

    const element = document.getElementById('pitch');
    const context = element.getContext('2d');
    const image = context.getImageData(0, 0, element.width, element.height).data;
    const counts = { grass: 0, light: 0, homeKit: 0, awayKit: 0, shot: 0, card: 0, badge: 0 };
    let samples = 0;

    for (let index = 0; index < image.length; index += 16) {
      const red = image[index];
      const green = image[index + 1];
      const blue = image[index + 2];

      samples += 1;

      if (green > 90 && green > red * 1.6 && green > blue * 1.6) counts.grass += 1;
      if (red < 60 && green > 110 && blue < 90) counts.light += 1;
      if (near(red, green, blue, 31, 78, 121)) counts.homeKit += 1;
      if (near(red, green, blue, 140, 47, 57)) counts.awayKit += 1;
      if (red > 180 && green > 160 && blue < 110) counts.shot += 1;
      if (red > 200 && green > 150 && green < 220 && blue < 90) counts.card += 1;
      if (red < 80 && green > 150 && blue > 100 && blue < 200) counts.badge += 1;
    }

    return { counts, samples, metrics: window.stage6.metrics(), ascii: ascii(element, context) };

    function near(red, green, blue, targetRed, targetGreen, targetBlue) {
      return (
        Math.abs(red - targetRed) < 40 &&
        Math.abs(green - targetGreen) < 40 &&
        Math.abs(blue - targetBlue) < 40
      );
    }

    function ascii(element, context) {
      const columns = 96;
      const rows = 30;
      const image = context.getImageData(0, 0, element.width, element.height).data;
      const cellWidth = element.width / columns;
      const cellHeight = element.height / rows;
      const lines = [];

      for (let row = 0; row < rows; row += 1) {
        let line = '';

        for (let column = 0; column < columns; column += 1) {
          let home = 0;
          let away = 0;
          let yellow = 0;
          let teal = 0;
          let white = 0;
          let grass = 0;

          for (let y = Math.floor(row * cellHeight); y < Math.floor((row + 1) * cellHeight); y += 1) {
            for (let x = Math.floor(column * cellWidth); x < Math.floor((column + 1) * cellWidth); x += 1) {
              const index = (y * element.width + x) * 4;
              const red = image[index];
              const green = image[index + 1];
              const blue = image[index + 2];

              if (red < 80 && green > 150 && blue > 100 && blue < 200) teal += 1;
              else if (red > 180 && green > 160 && blue < 110) yellow += 1;
              else if (near(red, green, blue, 31, 78, 121)) home += 1;
              else if (near(red, green, blue, 140, 47, 57)) away += 1;
              else if (red > 210 && green > 210 && blue > 200) white += 1;
              else if (green > 90 && green > red * 1.6 && green > blue * 1.6) grass += 1;
            }
          }

          line +=
            teal > 2
              ? '#'
              : yellow > 2
                ? 'Y'
                : home > 2 && home > away
                  ? 'H'
                  : away > 2
                    ? 'A'
                    : white > 1
                      ? '+'
                      : grass > 0
                        ? '.'
                        : ' ';
        }

        lines.push(line);
      }

      return lines.join('\n');
    }
  }, moment.at);

  await canvas.screenshot({ path: join(outputDirectory, `${moment.name}.png`) });

  if (moment.name === 'strike' || moment.name === 'celebration') {
    console.log(`\n--- ${moment.name} ---\n${metrics.ascii}\n`);
  }

  stats.push({
    moment: moment.name,
    at: moment.at,
    grass: metrics.counts.grass,
    asciiRows: metrics.ascii.split('\n').length,
    homeKit: metrics.counts.homeKit,
    awayKit: metrics.counts.awayKit,
    shot: metrics.counts.shot,
    card: metrics.counts.card,
    badge: metrics.counts.badge,
    renderMs: Number(metrics.metrics.milliseconds.toFixed(2)),
  });
}

// Play for twenty seconds at 8x, then compare the heap: a loop that leaked a listener or a frame would show.
await page.evaluate(() => {
  window.stage6.render(0);
  document.querySelector('button[data-speed="8"]').click();
  document.getElementById('play').click();
});
await page.waitForTimeout(20000);
await page.evaluate(() => {
  document.getElementById('play').click();
});

const heapAfter = await page.evaluate(() => performance.memory?.usedJSHeapSize ?? 0);

console.log(
  JSON.stringify(
    {
      fps: Number(frameRate.fps.toFixed(1)),
      frames: frameRate.frames,
      heapMegabytesBefore: Number((heapBefore / 1048576).toFixed(1)),
      heapMegabytesAfter: Number((heapAfter / 1048576).toFixed(1)),
      stats,
    },
    null,
    2,
  ),
);

await browser.close();
