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

/**
 * The three things somebody may be allowed to do across a whole account.
 *
 * Deliberately not the same list as the one for a single website. A standing in an account and a
 * role on one of its websites answer different questions, and the words a reader sees for each
 * come from the catalogue rather than from these.
 */
export const organizationRoleSchema = z.enum(['member', 'admin', 'owner']);

export const personSchema = z.object({
  id: z.uuid(),
  emailAddress: z.string(),
  displayName: z.string(),
  role: organizationRoleSchema,
  joinedAt: timestamp,
});

/** Somebody who has been asked to join and has not yet. */
export const pendingInvitationSchema = z.object({
  id: z.uuid(),
  emailAddress: z.string(),
  role: organizationRoleSchema,
  invitedAt: timestamp,
  expiresAt: timestamp,
});

export const organizationSchema = z.object({
  id: z.uuid(),
  name: z.string(),
  role: organizationRoleSchema,
  people: z.array(personSchema),
  invitations: z.array(pendingInvitationSchema),
});

/** What an invitation is for, as the person holding it is shown. */
export const invitationPreviewSchema = z.object({
  organizationName: z.string(),
  emailAddress: z.string(),
  needsAccount: z.boolean(),
});

/** What came of taking an invitation up. */
export const joinSchema = z.object({
  signedIn: z.boolean(),
  user: signedInUserSchema.nullable(),
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

/**
 * One measure counted in buckets across a period.
 *
 * Counting is complete up to `completeTo`. Asked of everybody that is the end of the period; asked
 * of people it is where the judging has reached, because a visit is only judged once it has
 * finished and the buckets after it are still filling.
 */
export const seriesSchema = z.object({
  from: timestamp,
  to: timestamp,
  metric: z.enum(['pageviews', 'visitors']),
  granularity: z.enum(['hour', 'day']),
  completeTo: timestamp,
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
export const locationGroupingSchema = z.enum(['country', 'town', 'network']);

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

/** What a row of a source list stands for. */
export const sourceGroupingSchema = z.enum(['kind', 'site', 'page']);

/**
 * The kinds of thing that send a visitor.
 *
 * A closed set, and each name is a phrase in the message catalogue. A list grouped this way uses
 * the same empty name every other grouping does for an arrival that named nowhere, so one row is
 * written one way wherever it appears.
 */
export const sourceKindSchema = z.enum(['search', 'assistant', 'social', 'link']);

export const siteSourceSchema = z.object({
  source: z.string(),
  site: z.string(),
  visitors: z.number().int(),
  pageViews: z.number().int(),
});

/**
 * One slice of where a period's visitors came from before they arrived.
 *
 * Counted per person on the same terms as the place list, and a person is credited to one source:
 * only a visit's first page names anywhere else, and everything after it was reached from the
 * website being measured. The website's own address takes no part, so it never heads its own list.
 *
 * Arrivals that named nowhere are a row with an empty name rather than an absence. Typing an
 * address in, opening a bookmark and following a link from an application all look the same here,
 * and together they are usually the largest row on the list.
 */
export const sourcesSchema = z.object({
  from: timestamp,
  to: timestamp,
  grouping: sourceGroupingSchema,
  visitors: z.number().int(),
  totalSources: z.number().int(),
  mostVisitors: z.number().int(),
  sources: z.array(siteSourceSchema),
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

/** Everything about one website that its owner decides: what it is called, and what it records. */
export const siteSettingsSchema = z.object({
  displayName: z.string(),
  timeZoneId: z.string(),
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

/**
 * What sort of thing sent a visitor, as one visit reports it.
 *
 * Wider than the list a source card is grouped by, because a single visit has to be able to say
 * that nothing named a sender — which on a list is a row rather than a kind.
 */
export const visitSourceKindSchema = z.enum(['direct', 'search', 'assistant', 'social', 'link']);

/**
 * What could be established about the visitor behind one visit.
 *
 * Every field is empty rather than absent where nothing established it, and empty is an answer
 * this dashboard writes out rather than a gap it leaves. A site behind something that does not
 * pass the visitor's address along places nobody at all; a visit only a website's own server saw
 * carries no browser. Neither is a fault, and neither is a reason to show a blank.
 */
export const visitContextSchema = z.object({
  source: z.string(),
  kind: visitSourceKindSchema,
  countryCode: z.string(),
  town: z.string(),
  network: z.string(),
  device: deviceKindSchema,
  browser: z.string(),
  system: z.string(),
});

/** One visit: who it was, and what it did in the order it did it. */
export const visitJourneySchema = z.object({
  visit: z.string(),
  context: visitContextSchema,
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

/** One category's count in every bucket, in the order the buckets are named. */
export const trafficCategorySeriesSchema = z.object({
  category: trafficCategorySchema,
  sessions: z.array(z.number().int()),
  pageViews: z.array(z.number().int()),
});

/**
 * What generated a period's traffic, bucket by bucket.
 *
 * A table rather than a list of cells. The buckets are named once and each category's counts run
 * alongside them in the same order, so a month counted an hour at a time is a handful of arrays
 * rather than ten thousand small objects each repeating its own category.
 *
 * Every category the period actually held runs the whole length of it, zeroes included; one it
 * never held is absent altogether rather than present as a row of nothing. Counting stops at
 * `completeTo`, because a visit is only judged once it has finished — the buckets after it are
 * reported and still filling.
 */
export const trafficSeriesSchema = z.object({
  from: timestamp,
  to: timestamp,
  granularity: z.enum(['hour', 'day']),
  completeTo: timestamp,
  buckets: z.array(timestamp),
  groups: z.array(trafficCategorySeriesSchema),
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
  ruleset: z.string(),
  supporting: z.array(visitReasonSchema),
  contradicting: z.array(visitReasonSchema),
  /**
   * Who the visitor was, where that could be established, and empty where it could not — the same
   * account the journey carries, so a row and the panel under it never disagree.
   */
  context: visitContextSchema,
});

export const visitsSchema = z.object({
  from: timestamp,
  to: timestamp,
  totalVisits: z.number().int(),
  visits: z.array(visitSchema),
});

/** One value a period's visits held, and how many of them held it. */
export const visitDetailRowSchema = z.object({
  value: z.string(),
  visits: z.number().int(),
});

/**
 * What a period's judged visits actually held, one list per detail, commonest first.
 *
 * Only what occurred is offered. Seven of the nine are open sets with no list anybody could write
 * down in advance — nobody can guess whether a website's traffic recorded a search engine as one
 * name or another — and a choice that can only ever come back empty reads as a fault in the
 * product rather than as an honest limit.
 *
 * An empty value stands for the visits nothing was established about, which is an answer rather
 * than a gap: a visitor behind something that passes on no address is still a visitor.
 *
 * Counted per visit, and across the whole period rather than across whatever is left after the
 * rest of the narrowing, so a control offering a figure hands back exactly that many.
 */
export const visitFacetsSchema = z.object({
  from: timestamp,
  to: timestamp,
  devices: z.array(visitDetailRowSchema),
  sourceKinds: z.array(visitDetailRowSchema),
  browsers: z.array(visitDetailRowSchema),
  systems: z.array(visitDetailRowSchema),
  countries: z.array(visitDetailRowSchema),
  towns: z.array(visitDetailRowSchema),
  networks: z.array(visitDetailRowSchema),
  sources: z.array(visitDetailRowSchema),
  entryPages: z.array(visitDetailRowSchema),
});

/** One minute of a website's reading, including the ones nothing happened in. */
export const liveMinuteSchema = z.object({
  start: timestamp,
  pageViews: z.number().int(),
});

/**
 * One page, and how many of the visitors here were last on it.
 *
 * Every visitor is on exactly one, so the pages of the half hour add up to the count above them,
 * though only the leading ones are carried.
 */
export const livePageSchema = z.object({
  path: z.string(),
  visitors: z.number().int(),
});

/**
 * One visitor who has been on a website in the last stretch of minutes.
 *
 * A visitor rather than a visit, because a visit ends only after it has been quiet long enough and
 * so cannot say who is here now.
 *
 * The conclusion is absent rather than hedged. Most visitors carry no category and no strength at
 * all, and that is the honest answer while their visit is still running: a naming is only offered
 * where nothing the visitor does next could withdraw it. A row with nothing said about it is not a
 * gap in the answer — it is the answer.
 */
export const liveVisitorSchema = z.object({
  visitor: z.string(),
  firstSeen: timestamp,
  lastSeen: timestamp,
  pageCount: z.number().int(),
  currentPath: z.string(),
  category: trafficCategorySchema.nullable(),
  strength: evidenceStrengthSchema.nullable(),
  ruleset: z.string().nullable(),
  supporting: z.array(visitReasonSchema),
  contradicting: z.array(visitReasonSchema),
  operator: z.string(),
  network: z.number().int(),
  context: visitContextSchema,
});

/**
 * What is happening on a website at this moment.
 *
 * How many visitors there have been and how many of them one answer carried are separate figures,
 * because a sweep putting three hundred visitors on a website in ten minutes has to be reported as
 * three hundred rather than as however long a list was allowed to be.
 *
 * Everything about how long ago something happened is measured against the moment the reading was
 * taken rather than against the reader's own clock, which on a machine that is an hour out would
 * otherwise report visitors arriving in the future.
 */
export const liveSchema = z.object({
  at: timestamp,
  from: timestamp,
  visitorsSeen: z.number().int(),
  visitors: z.array(liveVisitorSchema),
  minutes: z.array(liveMinuteSchema),
  pages: z.array(livePageSchema),
});

/**
 * What one visitor who is here has been doing, in the order they did it.
 *
 * It carries no account of who they are. That travels on the row it was opened from, settled over
 * the same minutes; answering the question a second time moments later would be a second answer
 * free to disagree with the first.
 */
export const liveTrailSchema = z.object({
  visitor: z.string(),
  at: timestamp,
  steps: z.array(visitJourneyStepSchema),
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
export type OrganizationRole = z.infer<typeof organizationRoleSchema>;
export type Person = z.infer<typeof personSchema>;
export type PendingInvitation = z.infer<typeof pendingInvitationSchema>;
export type Organization = z.infer<typeof organizationSchema>;
export type InvitationPreview = z.infer<typeof invitationPreviewSchema>;
export type Join = z.infer<typeof joinSchema>;
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
export type SourceGrouping = z.infer<typeof sourceGroupingSchema>;
export type SourceKind = z.infer<typeof sourceKindSchema>;
export type SiteSource = z.infer<typeof siteSourceSchema>;
export type Sources = z.infer<typeof sourcesSchema>;
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
export type TrafficSeries = z.infer<typeof trafficSeriesSchema>;
export type VisitReason = z.infer<typeof visitReasonSchema>;
export type Visit = z.infer<typeof visitSchema>;
export type Visits = z.infer<typeof visitsSchema>;
export type VisitDetailRow = z.infer<typeof visitDetailRowSchema>;
export type VisitFacets = z.infer<typeof visitFacetsSchema>;
export type VisitTotals = z.infer<typeof visitTotalsSchema>;
export type VisitPosition = z.infer<typeof visitPositionSchema>;
export type VisitPageRow = z.infer<typeof visitPageRowSchema>;
export type VisitPages = z.infer<typeof visitPagesSchema>;
export type VisitJourneyStep = z.infer<typeof visitJourneyStepSchema>;
export type VisitJourney = z.infer<typeof visitJourneySchema>;
export type VisitContext = z.infer<typeof visitContextSchema>;
export type VisitSourceKind = z.infer<typeof visitSourceKindSchema>;
export type LiveMinute = z.infer<typeof liveMinuteSchema>;
export type LivePage = z.infer<typeof livePageSchema>;
export type LiveVisitor = z.infer<typeof liveVisitorSchema>;
export type Live = z.infer<typeof liveSchema>;
export type LiveTrail = z.infer<typeof liveTrailSchema>;
export type ServerKey = z.infer<typeof serverKeySchema>;
export type IssuedServerKey = z.infer<typeof issuedServerKeySchema>;
