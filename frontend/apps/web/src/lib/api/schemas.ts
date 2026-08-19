import { z } from 'zod';

/**
 * The shapes the engine answers with.
 *
 * Every answer is checked against one of these before it reaches a screen. The engine and the
 * dashboard ship together, so a mismatch is never a version difference — it is a change that was
 * made on one side and not the other, and it is far cheaper to find here than three components
 * later as an undefined value.
 */

const timestamp = z.iso.datetime({ offset: true });

export const signedInUserSchema = z.object({
  id: z.uuid(),
  emailAddress: z.string(),
  displayName: z.string(),
});

export const sessionSchema = z.object({
  setupCompleted: z.boolean(),
  user: signedInUserSchema.nullable(),
  token: z.string(),
});

export const installationSchema = z.object({
  siteId: z.uuid(),
  user: signedInUserSchema,
  token: z.string(),
});

/** The three things somebody may be allowed to do with a website. */
export const siteRoleSchema = z.enum(['viewer', 'editor', 'owner']);

export const siteSchema = z.object({
  id: z.uuid(),
  domain: z.string(),
  displayName: z.string(),
  timeZoneId: z.string(),
  role: siteRoleSchema,
});

export const sitesSchema = z.array(siteSchema);

export const overviewSchema = z.object({
  from: timestamp,
  to: timestamp,
  pageViews: z.number().int(),
  visitors: z.number().int(),
  events: z.number().int(),
});

/** One measure counted in buckets across a period. */
export const seriesSchema = z.object({
  from: timestamp,
  to: timestamp,
  metric: z.enum(['pageviews', 'visitors']),
  granularity: z.enum(['hour', 'day']),
  points: z.array(z.object({ bucketStart: timestamp, value: z.number().int() })),
});

/** One page and how much of a period's traffic went to it. */
export const sitePageSchema = z.object({
  path: z.string(),
  pageViews: z.number().int(),
  visitors: z.number().int(),
});

/**
 * One slice of the pages a period's traffic went to.
 *
 * The three figures beside the rows all describe the whole period rather than the slice: what was
 * read altogether, how many addresses there were, and how much the busiest of them had. A share
 * worked out from the rows alone would put the busiest page of a large site at several times the
 * share it has, and a bar measured against the slice would start every slice with a full one.
 */
export const pagesSchema = z.object({
  from: timestamp,
  to: timestamp,
  pageViews: z.number().int(),
  totalPaths: z.number().int(),
  mostPageViews: z.number().int(),
  pages: z.array(sitePageSchema),
});

/** What a row of a place list stands for. */
export const locationGroupingSchema = z.enum(['country', 'town']);

export const siteLocationSchema = z.object({
  place: z.string(),
  countryCode: z.string(),
  visitors: z.number().int(),
  pageViews: z.number().int(),
});

/**
 * One slice of the places a period's audience was in.
 *
 * Counted per person rather than per page, so a country is ranked by how many readers were in it
 * rather than by how much browsing they did. The figures beside the rows describe the whole
 * period on the same terms as the page list, so a share stays true on the fourth screenful.
 *
 * A place that resolved to nothing is a row with an empty name rather than an absence. A site
 * behind a proxy that does not pass its visitors' addresses through resolves nothing at all, and
 * it needs to be able to see that.
 */
export const locationsSchema = z.object({
  from: timestamp,
  to: timestamp,
  grouping: locationGroupingSchema,
  visitors: z.number().int(),
  totalPlaces: z.number().int(),
  mostVisitors: z.number().int(),
  places: z.array(siteLocationSchema),
});

/**
 * The kinds of device the engine tells apart.
 *
 * A closed set, and `unknown` is one of its members rather than a gap in it: much of what reaches
 * a website is not a device at all, and each of these names a phrase in the message catalogue.
 */
export const deviceKindSchema = z.enum(['phone', 'tablet', 'desktop', 'other', 'unknown']);

export const siteDeviceSchema = z.object({
  kind: deviceKindSchema,
  visitors: z.number().int(),
  pageViews: z.number().int(),
});

/**
 * How a period's audience divides between kinds of device.
 *
 * Unpaged, because the kinds are five. Every visitor is on exactly one row, which is what lets
 * the card state one total and draw shares that add up to it.
 */
export const devicesSchema = z.object({
  from: timestamp,
  to: timestamp,
  visitors: z.number().int(),
  devices: z.array(siteDeviceSchema),
});

/** What a row of a software list stands for. */
export const softwareGroupingSchema = z.enum(['browser', 'system']);

export const siteSoftwareSchema = z.object({
  name: z.string(),
  visitors: z.number().int(),
  pageViews: z.number().int(),
});

/**
 * One slice of the software a period's audience used.
 *
 * Left open where the device kinds are closed: browsers are released, renamed and forked, and a
 * name arriving that this dashboard has never seen is a name to show rather than an error. The
 * engine spells it from its own catalogue, never from what the client claimed.
 */
export const softwareSchema = z.object({
  from: timestamp,
  to: timestamp,
  grouping: softwareGroupingSchema,
  visitors: z.number().int(),
  totalNames: z.number().int(),
  mostVisitors: z.number().int(),
  names: z.array(siteSoftwareSchema),
});

/** What a row of a list of operated controls stands for. */
export const actionGroupingSchema = z.enum(['control', 'destination']);

/**
 * What sort of thing a visitor operated.
 *
 * Closed, because the engine resolves whatever a page called its control into this set on the way
 * in. A page may describe its controls however it likes; none of its spelling is stored, and none
 * of it reaches a screen.
 */
export const controlKindSchema = z.enum(['unknown', 'link', 'button', 'field']);

export const siteActionSchema = z.object({
  name: z.string(),
  control: controlKindSchema,
  presses: z.number().int(),
  visitors: z.number().int(),
});

/**
 * One slice of what a period's visitors operated, most pressed first.
 *
 * Read exactly like the page, place and software lists: the figures beside the rows describe the
 * whole period rather than the slice, so a share and a bar mean the same thing on every screenful.
 */
export const actionsSchema = z.object({
  from: timestamp,
  to: timestamp,
  grouping: actionGroupingSchema,
  presses: z.number().int(),
  totalControls: z.number().int(),
  mostPresses: z.number().int(),
  controls: z.array(siteActionSchema),
});

/** What a website collects, as far as its owner decides it. */
export const siteSettingsSchema = z.object({
  captureClicks: z.boolean(),
});

/**
 * How a period's pages were actually read.
 *
 * Only the browser tracker can observe any of this, so how many readings could be measured
 * arrives beside how many there were: every other figure is taken over the measured ones alone,
 * and a website measured only from its own server has nothing measured rather than nobody
 * engaged.
 */
export const engagementSchema = z.object({
  from: timestamp,
  to: timestamp,
  readings: z.number().int(),
  measured: z.number().int(),
  medianEngagedMs: z.number().int(),
  interacted: z.number().int(),
  depths: z.object({
    top: z.number().int(),
    quarter: z.number().int(),
    half: z.number().int(),
    whole: z.number().int(),
  }),
});

/** What a reading list is ordered by. */
export const engagementRankingSchema = z.enum(['attention', 'depth']);

export const pageEngagementRowSchema = z.object({
  path: z.string(),
  readings: z.number().int(),
  medianEngagedMs: z.number().int(),
  medianDepthPercent: z.number().int(),
  interacted: z.number().int(),
});

/**
 * One slice of a period's pages ranked by how they were read.
 *
 * Only pages at least one reading could be measured on are on the list at all, so the total
 * beside it is smaller than the number of pages that had traffic.
 */
export const pageEngagementSchema = z.object({
  from: timestamp,
  to: timestamp,
  ranking: engagementRankingSchema,
  totalPages: z.number().int(),
  longestMedianEngagedMs: z.number().int(),
  pages: z.array(pageEngagementRowSchema),
});

/**
 * How a period's finished visits were shaped.
 *
 * A visit is one reader's activity up to the first half-hour of silence. Only visits that had
 * finished when the question was asked are counted: one still under way has an unfinished page
 * count, and a handful of those would decide the answer on a quiet website.
 */
export const visitTotalsSchema = z.object({
  from: timestamp,
  to: timestamp,
  visits: z.number().int(),
  singlePageVisits: z.number().int(),
  pageViews: z.number().int(),
});

/** Which end of a visit a page list stands for. */
export const visitPositionSchema = z.enum(['entry', 'exit']);

export const visitPageRowSchema = z.object({
  path: z.string(),
  visits: z.number().int(),
});

/**
 * One slice of the pages a period's visits began or ended on.
 *
 * Counted per visit rather than per page view, so a busy page is not a common doorway unless
 * people actually arrived through it.
 */
export const visitPagesSchema = z.object({
  from: timestamp,
  to: timestamp,
  position: visitPositionSchema,
  totalVisits: z.number().int(),
  totalPaths: z.number().int(),
  mostVisits: z.number().int(),
  pages: z.array(visitPageRowSchema),
});

/** What sort of place an operated control pointed at. */
export const targetKindSchema = z.enum(['none', 'internal', 'external', 'contact']);

/** One control a visitor operated, as it appears inside a visit. */
export const visitPressSchema = z.object({
  name: z.string(),
  control: controlKindSchema,
  target: z.string().nullable(),
  targetKind: targetKindSchema,
});

/**
 * One thing a visit did: arriving at a page, or operating a control on one.
 *
 * The three measurements are absent rather than nought where nothing observed them. A step only a
 * website's own server saw has no attention, which is a different fact from a reader who left
 * immediately, and the two are kept apart all the way to the screen. A step carrying a press is a
 * press rather than an arrival, which is what tells the two apart.
 */
export const visitJourneyStepSchema = z.object({
  at: timestamp,
  path: z.string(),
  statusCode: z.number().int().nullable(),
  engagedMs: z.number().int().nullable(),
  depthPercent: z.number().int().nullable(),
  press: visitPressSchema.nullable(),
});

/** What one visit did, in the order it did it. */
export const visitJourneySchema = z.object({
  visit: z.string(),
  steps: z.array(visitJourneyStepSchema),
});

/**
 * What generated a visit.
 *
 * A closed set, written out rather than left open, because every one of these names a sentence in
 * the message catalogue and a name with no sentence would reach a screen as itself. The engine and
 * this dashboard ship together, so a name arriving that is not here is a change made on one side
 * and not the other — and failing on it here is far cheaper than rendering it.
 *
 * A crawler this product has confirmed and one that merely says it is that crawler are separate
 * members and stay separate everywhere they are shown.
 */
export const trafficCategorySchema = z.enum([
  'insufficient-evidence',
  'likely-human',
  'known-search-crawler',
  'known-ai-crawler',
  'suspected-ai-crawler',
  'known-automated-service',
  'browser-automation',
  'generic-web-crawler',
  'content-scraper',
  'monitoring-or-synthetic',
  'security-scanner',
  'suspicious-automation',
  'likely-analytics-spam',
  'unknown',
]);

/**
 * How much weight stands behind a conclusion.
 *
 * A band and never a number. There is no labelled traffic to calibrate a percentage against, so a
 * percentage would look like a measurement while being an opinion.
 */
export const evidenceStrengthSchema = z.enum(['none', 'weak', 'moderate', 'strong', 'verified']);

/** What saw a visit. */
export const captureSurfaceSchema = z.enum([
  'unknown',
  'browser-tracker',
  'no-script-pixel',
  'cloudflare-worker',
  'wordpress-plugin',
  'netlify-edge',
  'vercel-edge',
  'aspnetcore-middleware',
  'nextjs-middleware',
  'log-import',
  'server-side',
]);

/** Which way one observation points. */
export const signalDirectionSchema = z.enum(['toward-human', 'neutral', 'toward-automation']);

/** One group of visits that reached the same conclusion with the same weight behind it. */
export const trafficGroupSchema = z.object({
  category: trafficCategorySchema,
  strength: evidenceStrengthSchema,
  sessions: z.number().int(),
  pageViews: z.number().int(),
});

/** Judged visits over a period, grouped by what generated them. */
export const trafficSchema = z.object({
  from: timestamp,
  to: timestamp,
  sessions: z.number().int(),
  pageViews: z.number().int(),
  groups: z.array(trafficGroupSchema),
});

/**
 * One observation behind a verdict.
 *
 * The code is left open rather than closed like the sets above. A detector added in a later
 * release would otherwise make every verdict on the screen unreadable rather than costing one
 * line of a list, and the sentence it is missing is a gap in the catalogue instead.
 */
export const visitReasonSchema = z.object({
  code: z.string(),
  direction: signalDirectionSchema,
  weight: z.number().int(),
  values: z.record(z.string(), z.string()),
});

/** One judged visit and why it was judged that way. */
export const visitSchema = z.object({
  id: z.string(),
  startedAt: timestamp,
  endedAt: timestamp,
  pageCount: z.number().int(),
  surfaces: z.array(captureSurfaceSchema),
  category: trafficCategorySchema,
  strength: evidenceStrengthSchema,
  isProvisional: z.boolean(),
  ruleset: z.string(),
  supporting: z.array(visitReasonSchema),
  contradicting: z.array(visitReasonSchema),
});

export const visitsSchema = z.object({
  from: timestamp,
  to: timestamp,
  visits: z.array(visitSchema),
});

/** One key a website's own server may report with, described without its secret. */
export const serverKeySchema = z.object({
  id: z.uuid(),
  name: z.string(),
  preview: z.string(),
  createdAt: timestamp,
  lastUsedAt: timestamp.nullable(),
});

export const serverKeysSchema = z.array(serverKeySchema);

/** A key at the one moment its secret exists anywhere but in the holder's own storage. */
export const issuedServerKeySchema = z.object({
  key: serverKeySchema,
  secret: z.string(),
});

export type SignedInUser = z.infer<typeof signedInUserSchema>;
export type Session = z.infer<typeof sessionSchema>;
export type Installation = z.infer<typeof installationSchema>;
export type SiteRole = z.infer<typeof siteRoleSchema>;
export type Site = z.infer<typeof siteSchema>;
export type Overview = z.infer<typeof overviewSchema>;
export type Series = z.infer<typeof seriesSchema>;
export type SeriesMetric = Series['metric'];
export type SitePage = z.infer<typeof sitePageSchema>;
export type Pages = z.infer<typeof pagesSchema>;
export type LocationGrouping = z.infer<typeof locationGroupingSchema>;
export type SiteLocation = z.infer<typeof siteLocationSchema>;
export type Locations = z.infer<typeof locationsSchema>;
export type DeviceKind = z.infer<typeof deviceKindSchema>;
export type SiteDevice = z.infer<typeof siteDeviceSchema>;
export type Devices = z.infer<typeof devicesSchema>;
export type ActionGrouping = z.infer<typeof actionGroupingSchema>;

export type ControlKind = z.infer<typeof controlKindSchema>;

export type SiteAction = z.infer<typeof siteActionSchema>;

export type Actions = z.infer<typeof actionsSchema>;

export type SiteSettings = z.infer<typeof siteSettingsSchema>;

export type TargetKind = z.infer<typeof targetKindSchema>;

export type VisitPress = z.infer<typeof visitPressSchema>;

export type SoftwareGrouping = z.infer<typeof softwareGroupingSchema>;
export type SiteSoftware = z.infer<typeof siteSoftwareSchema>;
export type Software = z.infer<typeof softwareSchema>;
export type Engagement = z.infer<typeof engagementSchema>;
export type EngagementRanking = z.infer<typeof engagementRankingSchema>;
export type PageEngagementRow = z.infer<typeof pageEngagementRowSchema>;
export type PageEngagement = z.infer<typeof pageEngagementSchema>;
export type TrafficCategory = z.infer<typeof trafficCategorySchema>;
export type EvidenceStrength = z.infer<typeof evidenceStrengthSchema>;
export type CaptureSurface = z.infer<typeof captureSurfaceSchema>;
export type SignalDirection = z.infer<typeof signalDirectionSchema>;
export type TrafficGroup = z.infer<typeof trafficGroupSchema>;
export type Traffic = z.infer<typeof trafficSchema>;
export type VisitReason = z.infer<typeof visitReasonSchema>;
export type Visit = z.infer<typeof visitSchema>;
export type Visits = z.infer<typeof visitsSchema>;
export type VisitTotals = z.infer<typeof visitTotalsSchema>;
export type VisitPosition = z.infer<typeof visitPositionSchema>;
export type VisitPageRow = z.infer<typeof visitPageRowSchema>;
export type VisitPages = z.infer<typeof visitPagesSchema>;
export type VisitJourneyStep = z.infer<typeof visitJourneyStepSchema>;
export type VisitJourney = z.infer<typeof visitJourneySchema>;
export type ServerKey = z.infer<typeof serverKeySchema>;
export type IssuedServerKey = z.infer<typeof issuedServerKeySchema>;
