import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

/** Serves this folder only, so the Stage 6 harness can be looked at in a browser. */

const root = new URL('.', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const port = Number(process.argv[2] ?? 8123);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
};

createServer(async (request, response) => {
  const path = normalize(decodeURIComponent((request.url ?? '/').split('?')[0]));
  const file = join(root, path === '/' ? 'stage6-demo.html' : path);

  try {
    const body = await readFile(file);

    response.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
    response.end(body);
  } catch {
    response.writeHead(404);
    response.end('not found');
  }
}).listen(port, '127.0.0.1', () => console.log(`harness on http://127.0.0.1:${port}/stage6-demo.html`));
