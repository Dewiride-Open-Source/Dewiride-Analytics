// Makes one request to the WordPress under test, as a visitor would, and checks what came back and
// what the stand-in collector was sent because of it.
//
// Run by run.sh inside the stand-in collector's container, so the visitor is neither WordPress nor
// WP-CLI — whose addresses the plugin rightly takes for the site's own:
//
//   SMOKE_EXPECT=named|reported|ignored SMOKE_METHOD=GET SMOKE_PATH=/ SMOKE_STATUS=200 \
//   SMOKE_HEADERS='{"Sec-Fetch-Dest":"document"}' SMOKE_AGENT=… SMOKE_BODY=… SMOKE_KEY=… \
//   node visit.mjs
//
// `named` asserts a page sent under a name in its Server-Timing header and one report carrying that
// name; `reported` one report and no name; `ignored` no report and no name. Every batch must come
// with the key and name the plugin, and every report must hold the page's address without its query
// and the status the page was answered with. Prints `ok` when every assertion holds, and otherwise
// one line for each that failed.

import { existsSync, readFileSync } from 'node:fs';

/** Where the stand-in collector records each batch it was sent, one JSON line each. */
const RECORD = '/reports/requests.jsonl';

/** Where the WordPress under test is reached from beside it. */
const HOME = 'http://wordpress';

/** The user agent a request carries unless run.sh gives another: a crawler's. */
const CRAWLER = 'Mozilla/5.0 (compatible; SmokeBot/1.0; +https://smoke.example.test/)';

/**
 * A setting run.sh gives, or an empty string.
 *
 * @param {string} name
 * @returns {string}
 */
function setting(name) {
  return process.env[name] ?? '';
}

/**
 * Every batch the stand-in collector has recorded so far, with the key it came with.
 *
 * @returns {{ authorization: string, batch: { surface?: unknown, events?: unknown } }[]}
 */
function recorded() {
  if (!existsSync(RECORD)) {
    return [];
  }

  return readFileSync(RECORD, 'utf8')
    .split('\n')
    .filter((line) => line !== '')
    .map((line) => JSON.parse(line));
}

/**
 * The name a page was sent under, read from its Server-Timing header, or an empty string.
 *
 * @param {string | null} timing
 * @returns {string}
 */
function nameIn(timing) {
  return /(?:^|,)\s*dw;desc="([0-9a-f]{32})"/.exec(timing ?? '')?.[1] ?? '';
}

/**
 * What failed of what was expected of the request.
 *
 * @param {Response} response
 * @param {ReturnType<typeof recorded>} batches
 * @returns {string[]}
 */
function failuresOf(response, batches) {
  const failures = [];
  const expect = setting('SMOKE_EXPECT');
  const page = HOME + setting('SMOKE_PATH').split('?')[0];
  const name = nameIn(response.headers.get('server-timing'));
  const reports = batches.flatMap(({ batch }) => (Array.isArray(batch.events) ? batch.events : []));

  for (const { authorization, batch } of batches) {
    if (authorization !== `Bearer ${setting('SMOKE_KEY')}`) {
      failures.push('a batch came without the key');
    }

    if (batch.surface !== 'wordpress-plugin') {
      failures.push('a batch did not name the plugin');
    }
  }

  for (const report of reports) {
    if (report.url !== page || report.statusCode !== response.status) {
      failures.push(`a report said ${JSON.stringify(report)}`);
    }
  }

  if (setting('SMOKE_STATUS') !== String(response.status)) {
    failures.push(`the page was answered ${response.status}, not ${setting('SMOKE_STATUS')}`);
  }

  if ((expect === 'named') !== (name !== '')) {
    failures.push(
      name === '' ? 'the page was sent without a name' : 'the page was sent under a name',
    );
  }

  if (reports.length !== (expect === 'ignored' ? 0 : 1)) {
    failures.push(`the collector was sent ${reports.length} reports`);
  } else if (expect !== 'ignored' && (reports[0].correlationId ?? '') !== name) {
    failures.push(
      `the report was named "${reports[0].correlationId}" where the page was "${name}"`,
    );
  }

  return failures;
}

const before = recorded().length;
const response = await fetch(HOME + setting('SMOKE_PATH'), {
  method: setting('SMOKE_METHOD'),
  headers: {
    'user-agent': setting('SMOKE_AGENT') || CRAWLER,
    ...JSON.parse(setting('SMOKE_HEADERS') || '{}'),
  },
  body: setting('SMOKE_BODY') || undefined,
  redirect: 'manual',
});

// Read to its end: where PHP runs inside Apache, the page ends only once its report has been sent.
await response.arrayBuffer();

const failures = failuresOf(response, recorded().slice(before));

process.stdout.write(failures.length === 0 ? 'ok\n' : `${failures.join('\n')}\n`);
