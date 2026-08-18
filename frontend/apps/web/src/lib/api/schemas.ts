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

export type SignedInUser = z.infer<typeof signedInUserSchema>;
export type Session = z.infer<typeof sessionSchema>;
export type Installation = z.infer<typeof installationSchema>;
export type SiteRole = z.infer<typeof siteRoleSchema>;
export type Site = z.infer<typeof siteSchema>;
export type Overview = z.infer<typeof overviewSchema>;
export type Series = z.infer<typeof seriesSchema>;
export type SeriesMetric = Series['metric'];
