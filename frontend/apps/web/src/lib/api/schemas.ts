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
export type TrafficCategory = z.infer<typeof trafficCategorySchema>;
export type EvidenceStrength = z.infer<typeof evidenceStrengthSchema>;
export type CaptureSurface = z.infer<typeof captureSurfaceSchema>;
export type SignalDirection = z.infer<typeof signalDirectionSchema>;
export type TrafficGroup = z.infer<typeof trafficGroupSchema>;
export type Traffic = z.infer<typeof trafficSchema>;
export type VisitReason = z.infer<typeof visitReasonSchema>;
export type Visit = z.infer<typeof visitSchema>;
export type Visits = z.infer<typeof visitsSchema>;
export type ServerKey = z.infer<typeof serverKeySchema>;
export type IssuedServerKey = z.infer<typeof issuedServerKeySchema>;
