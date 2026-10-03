import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

/**
 * Serves the film harness: its page, its bundle, and one dumped presentation.
 *
 * The bundle is built from `film-harness.ts` on start, straight from the app's own sources, so what the harness
 * measures is what the match center ships. The presentation is whatever file it is pointed at — the JSON that
 * `dotnet run --project tools/simulation-benchmarks -- replay 1 <seed> --dump <file>` writes.
 *
 *   node apps/web/.preview/serve.mjs <presentation.json> [port]
 */

const here = fileURLToPath(new URL('.', import.meta.url));

const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
};

/** Bundles the harness page's script, which imports the app's playback, loop and renderer. */
export async function bundleHarness() {
  const result = await build({
    entryPoints: [join(here, 'film-harness.ts')],
    bundle: true,
    format: 'esm',
    target: 'es2022',
    write: false,
    logLevel: 'warning',
  });

  return result.outputFiles[0].text;
}

/**
 * Starts the server.
 *
 * @param {{ presentation: string, port?: number }} options The dumped presentation to serve, and a port (0 picks one).
 * @returns {Promise<{ url: string, close: () => Promise<void> }>}
 */
export async function startServer({ presentation, port = 0 }) {
  const bundle = await bundleHarness();
  const presentationPath = resolve(presentation);

  const server = createServer(async (request, response) => {
    const path = normalize(decodeURIComponent((request.url ?? '/').split('?')[0]));

    try {
      if (path === '/film-harness.js') {
        response.writeHead(200, { 'content-type': types['.js'], 'cache-control': 'no-store' });
        response.end(bundle);

        return;
      }

      if (path === '/favicon.ico') {
        response.writeHead(204);
        response.end();

        return;
      }

      if (path === '/presentation.json') {
        response.writeHead(200, { 'content-type': types['.json'], 'cache-control': 'no-store' });
        response.end(await readFile(presentationPath));

        return;
      }

      // The harness's own folder only: nothing above it is reachable.
      const file = join(here, path === '/' ? 'film-harness.html' : path);

      if (!file.startsWith(here) || !['.html', '.css', '.png'].includes(extname(file))) {
        response.writeHead(404);
        response.end('not found');

        return;
      }

      response.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
      response.end(await readFile(file));
    } catch {
      response.writeHead(404);
      response.end('not found');
    }
  });

  await new Promise((done) => server.listen(port, '127.0.0.1', done));

  const address = server.address();

  return {
    url: `http://127.0.0.1:${address.port}/`,
    close: () => new Promise((done) => server.close(() => done())),
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const presentation = process.argv[2];

  if (!presentation) {
    console.error('usage: node serve.mjs <presentation.json> [port]');
    process.exit(2);
  }

  const { url } = await startServer({ presentation, port: Number(process.argv[3] ?? 8123) });

  console.log(`film harness on ${url}`);
}
