// A stand-in for Dewiride Analytics in the WordPress plugin's smoke harness.
//
// It serves dw.js, so that Check connection finds an address that serves the tag, and records
// every batch sent to /collect/server, one JSON line each, with the key it came with, so the
// harness can read what WordPress reported. It answers a batch the way the collector does, naming
// the Website ID the harness set up, the one host the harness's WordPress is reached on, and the
// address the batch came from.
//
// Nothing here is meant to be started by hand: compose.yaml runs it, and run.sh reads what it
// records.

import { appendFileSync } from 'node:fs';
import { createServer } from 'node:http';

/** Where each batch received is recorded. */
const RECORD = '/reports/requests.jsonl';

/** The Website ID the answers name. */
const SITE_ID = process.env.SMOKE_SITE_ID ?? '';

/** The port the stand-in answers on. */
const PORT = 8080;

/**
 * Reads a request's whole body as text.
 *
 * @param {import('node:http').IncomingMessage} request
 * @returns {Promise<string>}
 */
function bodyOf(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    request.on('data', (chunk) => chunks.push(chunk));
    request.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    request.on('error', reject);
  });
}

/**
 * Answers with JSON.
 *
 * @param {import('node:http').ServerResponse} response
 * @param {number} status
 * @param {unknown} body
 */
function answer(response, status, body) {
  response.writeHead(status, { 'content-type': 'application/json' });
  response.end(JSON.stringify(body));
}

/**
 * Records a batch and answers it as the collector does.
 *
 * @param {import('node:http').IncomingMessage} request
 * @param {import('node:http').ServerResponse} response
 */
async function collect(request, response) {
  let batch;

  try {
    batch = JSON.parse(await bodyOf(request));
  } catch {
    answer(response, 400, { title: 'The body was not a batch.' });
    return;
  }

  const events = Array.isArray(batch?.events) ? batch.events : [];

  appendFileSync(
    RECORD,
    `${JSON.stringify({ authorization: request.headers.authorization ?? '', batch })}\n`,
  );

  answer(response, 200, {
    accepted: events.length,
    rejected: 0,
    site: SITE_ID,
    hosts: ['wordpress'],
    measuring: true,
    from: request.socket.remoteAddress ?? '',
  });
}

createServer((request, response) => {
  const path = new URL(request.url ?? '/', 'http://collector').pathname;

  if (request.method === 'GET' && path === '/dw.js') {
    response.writeHead(200, { 'content-type': 'text/javascript' });
    response.end('/* The tracker is not needed here; Check connection only asks for it. */\n');
  } else if (request.method === 'POST' && path === '/collect/server') {
    collect(request, response).catch(() => answer(response, 500, {}));
  } else {
    answer(response, 404, {});
  }
}).listen(PORT);
