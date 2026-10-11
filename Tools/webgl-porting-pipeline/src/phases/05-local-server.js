'use strict';

// Phase 5 - Local HTTP server for a WebGL build
// Pure Node `http` static file server, zero dependencies. Serves a Unity
// WebGL build directory with the headers a Unity WebGL build actually needs:
// correct Content-Type/Content-Encoding for Gzip/Brotli-compressed build
// files, and COOP/COEP when the project has webGLThreadsSupport enabled
// (required for SharedArrayBuffer, which multithreaded WebGL builds need).
// Refuses to start against a directory that doesn't look like a real build
// output (no index.html) rather than silently serving nothing useful.

const fs = require('fs');
const path = require('path');
const http = require('http');

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.mem': 'application/octet-stream',
  '.unityweb': 'application/octet-stream',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
};

function contentTypeFor(filePath) {
  const lower = filePath.toLowerCase();
  for (const [ext, type] of Object.entries(CONTENT_TYPES)) {
    if (lower.endsWith(ext)) return type;
  }
  return 'application/octet-stream';
}

// Returns { encoding, innerPath } - innerPath has the compression extension
// stripped off so its own extension still drives the Content-Type (Unity
// WebGL build files are commonly named e.g. "foo.wasm.gz" or "foo.data.br").
function encodingFor(filePath) {
  if (filePath.toLowerCase().endsWith('.gz')) return { encoding: 'gzip', innerPath: filePath.slice(0, -3) };
  if (filePath.toLowerCase().endsWith('.br')) return { encoding: 'br', innerPath: filePath.slice(0, -3) };
  return { encoding: null, innerPath: filePath };
}

function safeResolve(buildDir, urlPath) {
  const decoded = decodeURIComponent(urlPath.split('?')[0]);
  const resolved = path.normalize(path.join(buildDir, decoded));
  const normalizedRoot = path.normalize(buildDir + path.sep);
  if (resolved !== path.normalize(buildDir) && !resolved.startsWith(normalizedRoot)) return null; // path traversal guard
  return resolved;
}

function startLocalServer({ buildDir, port = 0, threadsSupport = false, host = '127.0.0.1' }) {
  if (!fs.existsSync(path.join(buildDir, 'index.html'))) {
    return Promise.reject(
      new Error(
        `${buildDir} does not look like a WebGL build output directory ` +
        `(no index.html found). Point --build-path at the folder Phase 3's ` +
        `--output-path produced.`
      )
    );
  }

  const server = http.createServer((req, res) => {
    let filePath = safeResolve(buildDir, req.url === '/' ? '/index.html' : req.url);
    if (!filePath) {
      res.writeHead(400);
      res.end('Bad request');
      return;
    }
    if (fs.existsSync(filePath) && fs.statSync(filePath).isDirectory()) {
      filePath = path.join(filePath, 'index.html');
    }

    fs.readFile(filePath, (err, data) => {
      if (err) {
        res.writeHead(404);
        res.end('Not found');
        return;
      }
      const { encoding, innerPath } = encodingFor(filePath);
      // Set explicitly - Unity's WebGL loader (UnityCache) warns without it,
      // and relying on Node's own default would still omit it once
      // Content-Encoding is present.
      const headers = {
        'Content-Type': contentTypeFor(innerPath),
        'Content-Length': data.length,
        // Unity's WebGL loader caches the main .data file in IndexedDB as
        // "must-revalidate", and the build output here uses fixed,
        // non-content-hashed filenames (Build/WebGL.data.br etc.) - without
        // an explicit no-store, repeated test cycles against this same URL
        // (the common case during iterative device testing) can keep
        // replaying a stale build with no visible error. This server only
        // ever serves one on-disk build at a time, so there is never a
        // reason for the browser (or Unity's own cache) to reuse a
        // previous response instead of re-reading the file.
        'Cache-Control': 'no-store, must-revalidate',
      };
      if (encoding) headers['Content-Encoding'] = encoding;
      if (threadsSupport) {
        headers['Cross-Origin-Opener-Policy'] = 'same-origin';
        headers['Cross-Origin-Embedder-Policy'] = 'require-corp';
      }
      res.writeHead(200, headers);
      res.end(data);
    });
  });

  return new Promise((resolve, reject) => {
    server.on('error', reject);
    server.listen(port, host, () => {
      const actualPort = server.address().port;
      resolve({
        server,
        url: `http://${host === '0.0.0.0' ? '127.0.0.1' : host}:${actualPort}/`,
        host,
        port: actualPort,
        stop: () => new Promise((res) => server.close(() => res())),
      });
    });
  });
}

module.exports = { startLocalServer };
