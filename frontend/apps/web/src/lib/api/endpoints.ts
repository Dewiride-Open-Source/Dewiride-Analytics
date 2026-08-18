import type { AnalyticsWindow } from '@/lib/analytics/period';
import { readResource, submitResource } from './client';
import {
  type Installation,
  installationSchema,
  type Overview,
  overviewSchema,
  type Session,
  sessionSchema,
  type Series,
  type SeriesMetric,
  seriesSchema,
  type Site,
  sitesSchema,
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

function siteAddress(siteId: string): string {
  return `${SITES}/${encodeURIComponent(siteId)}`;
}

function period(window: AnalyticsWindow): string {
  return new URLSearchParams({ from: window.from, to: window.to }).toString();
}
