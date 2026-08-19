import type { AnalyticsWindow } from '@/lib/analytics/period';
import { discardResource, readResource, submitResource } from './client';
import {
  type Installation,
  issuedServerKeySchema,
  type IssuedServerKey,
  installationSchema,
  type Overview,
  overviewSchema,
  type Pages,
  pagesSchema,
  type Session,
  sessionSchema,
  type Series,
  type SeriesMetric,
  seriesSchema,
  type ServerKey,
  serverKeysSchema,
  type Site,
  sitesSchema,
  type Traffic,
  trafficSchema,
  type Visits,
  visitsSchema,
} from './schemas';

/**
 * Every question this dashboard asks the engine, in one place.
 *
 * Screens call these rather than assembling addresses, so an address exists in exactly one file
 * and a change to the engine's shape is a change to this one.
 */

const SESSION = '/api/session';
const SETUP = '/api/setup';
const SITES = '/api/sites';

/** What is known before anybody has done anything: has this install an owner, and who is here. */
export function describeSession(): Promise<Session> {
  return readResource(SESSION, sessionSchema);
}

export interface Credentials {
  readonly emailAddress: string;
  readonly password: string;
  readonly staySignedIn: boolean;
}

export function signIn(credentials: Credentials, proof: string): Promise<Session> {
  return submitResource(SESSION, 'POST', proof, sessionSchema, credentials);
}

export function signOut(proof: string): Promise<Session> {
  return submitResource(SESSION, 'DELETE', proof, sessionSchema);
}

/** Everything the setup screen collects the first and only time it is used. */
export interface InstallationDetails {
  readonly emailAddress: string;
  readonly password: string;
  readonly displayName: string | null;
  readonly organizationName: string;
  readonly siteDomain: string;
  readonly timeZoneId: string;
}

export function claimInstall(details: InstallationDetails, proof: string): Promise<Installation> {
  return submitResource(SETUP, 'POST', proof, installationSchema, details);
}

export function listSites(): Promise<Site[]> {
  return readResource(SITES, sitesSchema);
}

export function readOverview(siteId: string, window: AnalyticsWindow): Promise<Overview> {
  return readResource(`${siteAddress(siteId)}/overview?${period(window)}`, overviewSchema);
}

export function readSeries(
  siteId: string,
  metric: SeriesMetric,
  window: AnalyticsWindow,
): Promise<Series> {
  const asked = new URLSearchParams({ metric, granularity: 'day' });

  return readResource(`${siteAddress(siteId)}/series?${asked}&${period(window)}`, seriesSchema);
}

/**
 * One slice of the pages a period's traffic went to, busiest first.
 *
 * How many to bring back and where to start are the screen's decisions, as they are for visits.
 * The answer also describes the whole period, so one slice still reports honest shares and still
 * knows how much of the list is ahead of it.
 */
export function readPages(
  siteId: string,
  window: AnalyticsWindow,
  limit: number,
  offset: number,
): Promise<Pages> {
  const asked = new URLSearchParams({ limit: String(limit), offset: String(offset) });

  return readResource(`${siteAddress(siteId)}/pages?${asked}&${period(window)}`, pagesSchema);
}

/** Judged visits over a period, grouped by what generated them. */
export function readTraffic(siteId: string, window: AnalyticsWindow): Promise<Traffic> {
  return readResource(`${siteAddress(siteId)}/traffic?${period(window)}`, trafficSchema);
}

/**
 * The most recent judged visits over a period, with the evidence behind each verdict.
 *
 * How many to bring back is decided by the screen rather than left to the engine's own default,
 * because it is the screen that knows how many rows a reader will actually work through.
 */
export function readVisits(
  siteId: string,
  window: AnalyticsWindow,
  limit: number,
): Promise<Visits> {
  const asked = new URLSearchParams({ limit: String(limit) });

  return readResource(`${siteAddress(siteId)}/visits?${asked}&${period(window)}`, visitsSchema);
}

/** The keys a website's own server may report with. */
export function listServerKeys(siteId: string): Promise<ServerKey[]> {
  return readResource(serverKeysAddress(siteId), serverKeysSchema);
}

export function createServerKey(
  siteId: string,
  name: string,
  proof: string,
): Promise<IssuedServerKey> {
  return submitResource(serverKeysAddress(siteId), 'POST', proof, issuedServerKeySchema, { name });
}

export function revokeServerKey(siteId: string, keyId: string, proof: string): Promise<void> {
  return discardResource(
    `${serverKeysAddress(siteId)}/${encodeURIComponent(keyId)}`,
    'DELETE',
    proof,
  );
}

function serverKeysAddress(siteId: string): string {
  return `${siteAddress(siteId)}/server-keys`;
}

function siteAddress(siteId: string): string {
  return `${SITES}/${encodeURIComponent(siteId)}`;
}

function period(window: AnalyticsWindow): string {
  return new URLSearchParams({ from: window.from, to: window.to }).toString();
}
