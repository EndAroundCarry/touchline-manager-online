// A tiny static server for the production web build, so the PWA journey can run against a stack where the
// service worker is actually enabled (it is disabled under `ng serve`). It serves `dist/web/browser`, falls
// back to `index.html` for client-side routes, and proxies `/api` to the running API so the built app's
// relative `/api/v1` calls reach it. No third-party dependency: the point is a faithful origin, not a
// general-purpose server.
import { readFile, stat } from 'node:fs/promises';
import { createServer, request as httpRequest } from 'node:http';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const repositoryRoot = resolve(here, '..', '..', '..');
const browserRoot = join(repositoryRoot, 'apps', 'web', 'dist', 'web', 'browser');

const port = Number(process.env.PWA_PORT ?? 4201);
const apiOrigin = process.env.PWA_API_ORIGIN ?? 'http://localhost:5080';

const contentTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.mjs', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.map', 'application/json; charset=utf-8'],
  ['.webmanifest', 'application/manifest+json'],
  ['.ico', 'image/x-icon'],
  ['.png', 'image/png'],
  ['.jpg', 'image/jpeg'],
  ['.jpeg', 'image/jpeg'],
  ['.svg', 'image/svg+xml'],
  ['.woff', 'font/woff'],
  ['.woff2', 'font/woff2'],
  ['.ttf', 'font/ttf'],
  ['.txt', 'text/plain; charset=utf-8'],
]);

/** Maps a request path to a file under the build, or null when there is nothing to serve. */
async function resolveFile(pathname) {
  const decoded = decodeURIComponent(pathname);
  const candidate = join(browserRoot, normalize(decoded));

  // Never serve outside the build directory.
  if (candidate !== browserRoot && !candidate.startsWith(browserRoot + sep)) {
    return null;
  }

  try {
    const info = await stat(candidate);

    if (info.isDirectory()) {
      return join(candidate, 'index.html');
    }

    return candidate;
  } catch {
    // A client-side route such as `/dashboard` has no file; the app shell handles it.
    return pathname.includes('.') ? null : join(browserRoot, 'index.html');
  }
}

function contentTypeFor(path) {
  return contentTypes.get(extname(path).toLowerCase()) ?? 'application/octet-stream';
}

async function serveFile(res, filePath) {
  try {
    const body = await readFile(filePath);
    const immutable = /\.[0-9a-f]{8,}\./.test(filePath) || filePath.includes(`${sep}media${sep}`);
    const cacheControl = filePath.endsWith('index.html') || filePath.endsWith('ngsw.json')
      ? 'no-cache'
      : immutable
        ? 'public, max-age=31536000, immutable'
        : 'no-cache';

    res.writeHead(200, {
      'Content-Type': contentTypeFor(filePath),
      'Cache-Control': cacheControl,
    });
    res.end(body);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Not found');
  }
}

function proxy(req, res, url) {
  const target = new URL(url.pathname + url.search, apiOrigin);
  const upstream = httpRequest(
    target,
    { method: req.method, headers: { ...req.headers, host: target.host } },
    (upstreamRes) => {
      res.writeHead(upstreamRes.statusCode ?? 502, upstreamRes.headers);
      upstreamRes.pipe(res);
    },
  );

  upstream.on('error', () => {
    res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Bad gateway');
  });

  req.pipe(upstream);
}

const server = createServer((req, res) => {
  void (async () => {
    const url = new URL(req.url ?? '/', `http://localhost:${port}`);

    if (url.pathname.startsWith('/api/')) {
      proxy(req, res, url);

      return;
    }

    const filePath = await resolveFile(url.pathname);

    if (filePath === null) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('Not found');

      return;
    }

    await serveFile(res, filePath);
  })();
});

server.listen(port, () => {
  process.stdout.write(`PWA static server on http://localhost:${port}\n`);
});
