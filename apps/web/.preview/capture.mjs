import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { startServer } from './serve.mjs';

/**
 * Drives the film fluidity harness in headless Chromium and says whether the film is fluid (`replay-v4`, M3).
 *
 *   node apps/web/.preview/capture.mjs <presentation.json> [--out <dir>] [--seconds <n>] [--min-fps <n>] [--fluidity-only]
 *
 * The presentation is a dump from `dotnet run --project tools/simulation-benchmarks -- replay 1 <seed> --dump
 * <file>`. It is played through the real playback, render loop, fade and renderer; this script reports
 *
 *   - the frame rate and the frame times, and how many frames dropped;
 *   - how far the ball, the outfield players and the keepers moved between two *drawn* frames, outside the cuts,
 *     and how many of those moves were faster than the film's own speed caps allow (a jump);
 *   - how much of the film the ball stands still for;
 *
 * and photographs the moments worth looking at. It exits non-zero if there is a jump, or if the frame rate is
 * under the floor — which is the plan's "no frame jumps outside cuts, and at least 58 FPS on a desktop".
 * A headless browser without a GPU is slower than a desktop one, so the floor is an option.
 */

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(join(here, '../../../tests/web-e2e/package.json'));
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const at = args.indexOf(name);

  return at >= 0 && args[at + 1] !== undefined ? args[at + 1] : fallback;
};
const presentation = args.find((value, index) => !value.startsWith('--') && !args[index - 1]?.startsWith('--'));

if (!presentation) {
  console.error('usage: node capture.mjs <presentation.json> [--out <dir>] [--seconds <n>] [--min-fps <n>]');
  process.exit(2);
}

const outputDirectory = resolve(option('--out', join(here, 'shots')));
const seconds = Number(option('--seconds', 12));
const minimumFps = Number(option('--min-fps', 58));

await mkdir(outputDirectory, { recursive: true });

const { url, close } = await startServer({ presentation });

// The browsers this container carries are pre-installed rather than fetched for a pinned version, so a
// version mismatch falls back to the installed binary instead of failing.
const executable = process.env.PW_CHROMIUM ?? ['/opt/pw-browsers/chromium'].find((path) => existsSync(path));
let browser;

try {
  browser = await chromium.launch();
} catch {
  browser = await chromium.launch({ executablePath: executable });
}

const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
const problems = [];

page.on('pageerror', (error) => problems.push(`page error: ${error.message}`));
page.on('console', (message) => {
  if (message.type() === 'error') {
    problems.push(`console error: ${message.text()}`);
  }
});

await page.goto(url, { waitUntil: 'load' });
await page.waitForFunction(() => window.film !== undefined, undefined, { timeout: 30_000 });

const info = await page.evaluate(() => window.film.info());
const clock = (milliseconds) => page.evaluate((value) => window.film.clockAt(value), milliseconds);
const minutes = (milliseconds) => `${Math.floor(milliseconds / 60_000)}:${String(Math.floor((milliseconds % 60_000) / 1_000)).padStart(2, '0')}`;

console.log(`\n== ${presentation} ==`);
console.log(
  `film ${minutes(info.durationMilliseconds)}  reel ${minutes(info.reelMilliseconds)}  pace ${info.paceMilli === null ? '?' : (info.paceMilli / 1000).toFixed(2) + 'x'}  ` +
    `passages ${info.passages}  cuts ${info.cuts.map((cut) => `${cut.kind}@${minutes(cut.startMilliseconds)}`).join(', ') || 'none'}`,
);
console.log(
  `goals ${info.goals.length}  shots ${info.markers.filter((marker) => marker.kind === 'shot').length}  cards ${info.cards.length}  slots with a substitute ${info.substitutions}  half-time ${info.halfTimes.map((interval) => minutes(interval.startMilliseconds)).join(', ') || 'none'}`,
);

if (args.includes('--fluidity-only')) {
  await reportFluidity();
  await browser.close();
  await close();
  process.exit(0);
}

// ---- Photographs ------------------------------------------------------------------------------------------

const firstShot = info.markers.find((marker) => marker.kind === 'shot' || marker.kind === 'goal');
const halfTime = info.halfTimes[0];
const moments = [
  { name: 'kickoff', at: 0 },
  { name: 'boundary', at: info.passageStarts[Math.min(8, info.passageStarts.length - 1)] },
  ...(firstShot ? [{ name: 'strike', at: firstShot.filmMilliseconds }] : []),
  ...(info.goals[0] ? [{ name: 'goal', at: info.goals[0].filmMilliseconds + 900 }] : []),
  ...(info.cards[0] ? [{ name: 'card', at: info.cards[0].filmMilliseconds + 600 }] : []),
  ...(halfTime ? [{ name: 'halftime', at: halfTime.startMilliseconds + 1_200 }] : []),
  ...(halfTime ? [{ name: 'secondhalf', at: halfTime.endMilliseconds + 1_800 }] : []),
];
const canvas = page.locator('#pitch');

for (const moment of moments) {
  await page.evaluate((at) => window.film.show(at), moment.at);
  await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => done())));
  await canvas.screenshot({ path: join(outputDirectory, `${moment.name}.png`) });
  console.log(`  photographed ${moment.name.padEnd(10)} at ${minutes(moment.at)}  clock ${await clock(moment.at)}`);
}

// ---- The clock at the seams ------------------------------------------------------------------------------------

if (halfTime) {
  console.log(
    `clock: before HT ${await clock(halfTime.startMilliseconds - 500)}  at HT ${await clock(halfTime.startMilliseconds + 500)}  ` +
      `after ${await clock(halfTime.endMilliseconds + 500)}  at the end ${await clock(info.durationMilliseconds)}`,
  );
}

// ---- The clock against the events -------------------------------------------------------------------------------

const clockChecks = await page.evaluate(() => window.film.clockChecks());

console.log(
  `clock at events: ${clockChecks.checked - clockChecks.mismatches.length}/${clockChecks.checked} goals and cards read the minute the engine stamped on them` +
    (clockChecks.mismatches.length > 0
      ? `\n  mismatches: ${clockChecks.mismatches.map((mismatch) => `${mismatch.what} stamped ${mismatch.stamped} shown ${mismatch.shown}`).join('; ')}`
      : ''),
);

// ---- The ball standing still -------------------------------------------------------------------------------------

const still = await page.evaluate(() => window.film.still());

console.log(
  `ball still: ${(still.allShare * 100).toFixed(1)}% of the whole film, ${(still.outsideHoldsShare * 100).toFixed(1)}% outside half-time, celebrations and cuts; longest stretch ${still.longestStillSeconds.toFixed(1)} s`,
);

// ---- Stutters and covered tokens --------------------------------------------------------------------------------
// Stepped through the film, not played, so it is the same on every machine. Reported, not failed on: it is the baseline the
// interpolator and de-overlap work is measured against (tick-film-v1).

async function reportFluidity() {
  const fluidity = await page.evaluate(() => window.film.fluidity());

  console.log(
    `stutters: ${fluidity.stutters} (${fluidity.stuttersPerFilmMinute.toFixed(1)} per film minute)  ` +
      `covered tokens: ${(fluidity.coveredShareOfSteps * 100).toFixed(1)}% of steps, ${fluidity.coveredPairsPerFilmMinute.toFixed(0)} pair-steps per film minute  ` +
      `(token radius ${fluidity.tokenRadiusMetres.toFixed(2)} m)`,
  );
}

await reportFluidity();

// ---- Real-time runs ----------------------------------------------------------------------------------------------

const runs = [
  { label: 'kick-off, 1x', fromMilliseconds: 0, seconds, speed: 1 },
  ...(info.goals[0]
    ? [{ label: 'a goal and its celebration, 1x', fromMilliseconds: Math.max(0, info.goals[0].filmMilliseconds - 5_000), seconds, speed: 1 }]
    : []),
  ...(halfTime
    ? [{ label: 'across the half-time cut, 1x', fromMilliseconds: Math.max(0, halfTime.startMilliseconds - 3_000), seconds: Math.max(seconds, 9), speed: 1 }]
    : []),
  { label: 'eight times speed through the match', fromMilliseconds: 30_000, seconds: Math.max(seconds, 30), speed: 8 },
  { label: 'the highlights reel, 1x', fromMilliseconds: 0, seconds, speed: 1, mode: 'reel' },
  // At 8x the reel plays on through its clip boundaries, which is where the screen dips and the playhead jumps.
  { label: 'the highlights reel across its clips, 8x', fromMilliseconds: 0, seconds: Math.max(seconds, 25), speed: 8, mode: 'reel' },
];

let totalJumps = 0;
let worstFps = Infinity;
const format = (value, digits = 2) => value.toFixed(digits);

console.log('');

for (const run of runs) {
  const report = await page.evaluate((options) => window.film.run(options), run);

  totalJumps += report.jumps;
  worstFps = Math.min(worstFps, report.fps);

  console.log(`-- ${run.label} (from ${minutes(report.fromMilliseconds)}, ${format(report.realSeconds, 1)} s real = ${format(report.filmSeconds, 1)} s of film)`);
  console.log(
    `   ${format(report.fps, 1)} fps  frame ms p50 ${format(report.frameMilliseconds.p50, 1)} p95 ${format(report.frameMilliseconds.p95, 1)} max ${format(report.frameMilliseconds.max, 1)}  dropped ${report.droppedFrames}  draw ms p50 ${format(report.drawMilliseconds.p50)} p95 ${format(report.drawMilliseconds.p95)} max ${format(report.drawMilliseconds.max)}`,
  );
  console.log(
    `   per drawn frame (m): ball p99 ${format(report.ballMetresPerFrame.p99)} max ${format(report.ballMetresPerFrame.max)} | players p99 ${format(report.playerMetresPerFrame.p99)} max ${format(report.playerMetresPerFrame.max)} | keepers max ${format(report.keeperMetresPerFrame.max)}  (${report.comparedFrames} comparisons, ${report.cutsCrossed} cuts crossed, ${report.jumpsSkipped} reel jumps)`,
  );
  console.log(`   jumps above the speed caps: ${report.jumps}${report.jumps > 0 ? `  worst: ${report.worstJump}` : ''}`);
}

await canvas.screenshot({ path: join(outputDirectory, 'last-frame.png') });

console.log(`\nshots in ${outputDirectory}`);

if (problems.length > 0) {
  console.log(`\n${problems.length} problem(s) in the page:\n  ${[...new Set(problems)].join('\n  ')}`);
}

await browser.close();
await close();

const failures = [];

if (totalJumps > 0) {
  failures.push(`${totalJumps} frame jump(s) outside the cuts`);
}

if (worstFps < minimumFps) {
  failures.push(`the slowest run was ${format(worstFps, 1)} fps, under the ${minimumFps} fps floor`);
}

if (clockChecks.mismatches.length > 0) {
  failures.push(`${clockChecks.mismatches.length} event(s) read a different minute from the one the engine stamped`);
}

if (problems.length > 0) {
  failures.push('the page reported errors');
}

console.log(failures.length === 0 ? '\nFLUID: no jumps outside cuts, and the frame rate holds.' : `\nNOT FLUID: ${failures.join('; ')}.`);
process.exit(failures.length === 0 ? 0 : 1);
