import { EVERY_JOURNEY, type JourneyFilters } from '@/lib/analytics/journeys';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import { discardResource, readResource, submitResource } from './client';
import {
  type ActionGrouping,
  type Actions,
  actionsSchema,
  type Devices,
  devicesSchema,
  type Installation,
  issuedServerKeySchema,
  type IssuedServerKey,
  installationSchema,
  type LocationGrouping,
  type Locations,
  locationsSchema,
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
  siteSchema,
  type SiteSettings,
  siteSettingsSchema,
  type SourceGrouping,
  type Sources,
  sourcesSchema,
  sitesSchema,
  type Engagement,
  type EngagementRanking,
  engagementSchema,
  type PageEngagement,
  pageEngagementSchema,
  type Software,
  type SoftwareGrouping,
  softwareSchema,
  type Traffic,
  trafficSchema,
  type Visits,
  visitsSchema,
  type VisitJourney,
  visitJourneySchema,
  type VisitPages,
  visitPagesSchema,
  type VisitPosition,
  type VisitTotals,
  visitTotalsSchema,
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

/**
 * One slice of the places a period's audience was in, busiest first.
 *
 * Read exactly like the page list, including the figures that describe the whole period rather
 * than the slice, so both cards can be moved through on the same terms.
 */
export function readLocations(
  siteId: string,
  window: AnalyticsWindow,
  grouping: LocationGrouping,
  limit: number,
  offset: number,
): Promise<Locations> {
  const asked = new URLSearchParams({
    grouping,
    limit: String(limit),
    offset: String(offset),
  });

  return readResource(
    `${siteAddress(siteId)}/locations?${asked}&${period(window)}`,
    locationsSchema,
  );
}

/**
 * One slice of where a period's visitors came from, busiest first.
 *
 * Read exactly like the place list, including the figures that describe the whole period rather
 * than the slice.
 */
export function readSources(
  siteId: string,
  window: AnalyticsWindow,
  grouping: SourceGrouping,
  limit: number,
  offset: number,
): Promise<Sources> {
  const asked = new URLSearchParams({
    grouping,
    limit: String(limit),
    offset: String(offset),
  });

  return readResource(`${siteAddress(siteId)}/sources?${asked}&${period(window)}`, sourcesSchema);
}

/**
 * How a period's audience divides between kinds of device.
 *
 * Nothing to page through: the kinds are a closed set of five, so the whole answer arrives at
 * once and the rows add up to the total beside them.
 */
export function readDevices(siteId: string, window: AnalyticsWindow): Promise<Devices> {
  return readResource(`${siteAddress(siteId)}/devices?${period(window)}`, devicesSchema);
}

/**
 * One slice of the browsers or operating systems a period's audience used, commonest first.
 *
 * Read exactly like the page and place lists, including the figures that describe the whole
 * period rather than the slice.
 */
export function readSoftware(
  siteId: string,
  window: AnalyticsWindow,
  grouping: SoftwareGrouping,
  limit: number,
  offset: number,
): Promise<Software> {
  const asked = new URLSearchParams({
    grouping,
    limit: String(limit),
    offset: String(offset),
  });

  return readResource(`${siteAddress(siteId)}/software?${asked}&${period(window)}`, softwareSchema);
}

/**
 * One slice of what a period's visitors operated, most pressed first.
 *
 * Read exactly like the page, place and software lists, including the figures that describe the
 * whole period rather than the slice.
 */
export function readActions(
  siteId: string,
  window: AnalyticsWindow,
  grouping: ActionGrouping,
  limit: number,
  offset: number,
): Promise<Actions> {
  const asked = new URLSearchParams({
    grouping,
    limit: String(limit),
    offset: String(offset),
  });

  return readResource(`${siteAddress(siteId)}/actions?${asked}&${period(window)}`, actionsSchema);
}

/** What is needed to start measuring another website. */
export interface NewSite {
  readonly domain: string;
  readonly timeZoneId: string;
}

/** Starts measuring another website, owned by whoever added it. */
export function addSite(site: NewSite, proof: string): Promise<Site> {
  return submitResource(SITES, 'POST', proof, siteSchema, site);
}

/**
 * Stops measuring a website, and takes everything measured for it with them.
 *
 * Answered with nothing at all, so it goes through the call that expects no body: reading an
 * empty answer as an object would fail on the very answer that means it worked.
 */
export function removeSite(siteId: string, proof: string): Promise<void> {
  return discardResource(siteAddress(siteId), 'DELETE', proof);
}

/** Everything about one website that its owner decides. */
export function readSiteSettings(siteId: string): Promise<SiteSettings> {
  return readResource(`${siteAddress(siteId)}/settings`, siteSettingsSchema);
}

/**
 * Changes one website's settings.
 *
 * A setting left out is left as it was, so this sends only what is being changed.
 */
export function updateSiteSettings(
  siteId: string,
  settings: Partial<SiteSettings>,
  proof: string,
): Promise<SiteSettings> {
  return submitResource(
    `${siteAddress(siteId)}/settings`,
    'PUT',
    proof,
    siteSettingsSchema,
    settings,
  );
}

/**
 * How a period's pages were actually read.
 *
 * One answer about one period rather than a list, and it carries how much of the period it could
 * be taken from alongside what it found.
 */
export function readEngagement(siteId: string, window: AnalyticsWindow): Promise<Engagement> {
  return readResource(`${siteAddress(siteId)}/engagement?${period(window)}`, engagementSchema);
}

/**
 * One slice of a period's pages ranked by how they were read, rather than by how often.
 *
 * Read exactly like the page, place and software lists, including the figures that describe the
 * whole period rather than the slice.
 */
export function readPageEngagement(
  siteId: string,
  window: AnalyticsWindow,
  ranking: EngagementRanking,
  limit: number,
  offset: number,
): Promise<PageEngagement> {
  const asked = new URLSearchParams({
    ranking,
    limit: String(limit),
    offset: String(offset),
  });

  return readResource(
    `${siteAddress(siteId)}/engagement/pages?${asked}&${period(window)}`,
    pageEngagementSchema,
  );
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
 *
 * What the reader narrowed to is asked of the engine rather than applied to what comes back. A
 * screen that filtered a page of rows would be filtering a slice of the period, so the figures
 * beside the list would describe a different list from the one on it.
 */
export function readVisits(
  siteId: string,
  window: AnalyticsWindow,
  limit: number,
  offset: number,
  filters: JourneyFilters = EVERY_JOURNEY,
): Promise<Visits> {
  const asked = new URLSearchParams({ limit: String(limit), offset: String(offset) });

  for (const category of filters.categories) {
    asked.append('category', category);
  }

  if (filters.leastStrength !== null) {
    asked.set('strength', filters.leastStrength);
  }

  if (filters.leastPages > 0) {
    asked.set('minPages', String(filters.leastPages));
  }

  return readResource(`${siteAddress(siteId)}/visits?${asked}&${period(window)}`, visitsSchema);
}

/**
 * How a period's visits went: how many there were, and how many were a single page.
 *
 * Counted from activity rather than from what the engine has judged, so it keeps step with the
 * headline totals instead of trailing them.
 */
export function readVisitTotals(siteId: string, window: AnalyticsWindow): Promise<VisitTotals> {
  return readResource(`${siteAddress(siteId)}/visits/totals?${period(window)}`, visitTotalsSchema);
}

/**
 * One slice of the pages a period's visits began or ended on, commonest first.
 *
 * Read exactly like the other lists, including the figures that describe the whole period rather
 * than the slice.
 */
export function readVisitPages(
  siteId: string,
  window: AnalyticsWindow,
  position: VisitPosition,
  limit: number,
  offset: number,
): Promise<VisitPages> {
  const asked = new URLSearchParams({
    position,
    limit: String(limit),
    offset: String(offset),
  });

  return readResource(
    `${siteAddress(siteId)}/visits/pages?${asked}&${period(window)}`,
    visitPagesSchema,
  );
}

/**
 * The pages one visit went through, in order.
 *
 * No period: a visit's identity already says when it began, and this is the whole of one visit
 * rather than a slice of a period.
 */
export function readVisitJourney(siteId: string, visit: string): Promise<VisitJourney> {
  return readResource(
    `${siteAddress(siteId)}/visits/${encodeURIComponent(visit)}/journey`,
    visitJourneySchema,
  );
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
