-- ---------------------------------------------------------------------------
-- 0001_events — the raw telemetry table.
--
-- Column types mirror the .NET property types on RawEvent exactly, so that no
-- value is silently narrowed on the way in. Where the domain models a value as
-- optional, the column is Nullable rather than carrying a sentinel: a sentinel
-- that happens to be a legal reading (nought bytes, nought per cent scrolled)
-- destroys the distinction between "measured as nought" and "not observed".
--
-- The three interaction columns are three-state for the same reason. A surface
-- that cannot see pointer activity must record that it could not see it, not
-- that there was none.
--
-- Two retention rules are enforced by the engine rather than by a job, so they
-- hold even if the application is never started again:
--   * ip_address is cleared 72 hours after the event was received.
--   * the whole row is dropped 12 months after the event was received.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS events
(
  event_id                 UUID,
  site_id                  UUID,
  kind                     Enum8('PageView' = 1, 'Engagement' = 2, 'Exit' = 3),
  surface                  Enum8('Unknown' = 0, 'BrowserTracker' = 1, 'NoScriptPixel' = 2, 'CloudflareWorker' = 3, 'WordPressPlugin' = 4, 'NetlifyEdge' = 5, 'VercelEdge' = 6, 'AspNetCoreMiddleware' = 7, 'NextJsMiddleware' = 8, 'LogImport' = 9),

  -- Stamped by the collector on receipt. The only timestamp any query may order
  -- or bucket by.
  server_ts                DateTime64(3, 'UTC'),

  -- Whatever the client claimed, retained only so that clock_skew_ms means
  -- something. Never trusted for ordering.
  client_ts                Nullable(DateTime64(3, 'UTC')),
  clock_skew_ms            Int32,

  -- Empty when the surface could not derive one, which is a distinct state from
  -- "a visitor whose key is the empty string" — no key is ever empty.
  visitor_key              String,

  host                     LowCardinality(String),
  path                     String,
  query_string             String,
  referrer                 String,
  referrer_domain          LowCardinality(String),
  user_agent               String,

  -- Observable only by the server-side and log-import surfaces, and the primary
  -- evidence for security scanners: they are recognised by streams of requests
  -- to paths that do not exist.
  status_code              Nullable(Int16),
  content_type             LowCardinality(String),
  response_bytes           Nullable(Int64),

  -- Personal data. Cleared to the empty string once the retention window closes,
  -- by which time the derived network attributes have been resolved from it.
  ip_address               String TTL toDateTime(server_ts) + INTERVAL 72 HOUR,

  viewport_width           Nullable(Int32),
  viewport_height          Nullable(Int32),
  language                 LowCardinality(String),
  timezone_offset_minutes  Nullable(Int16),

  engaged_ms               Nullable(Int32),
  scroll_depth_percent     Nullable(UInt8),

  -- Presence of interaction, never its content.
  had_pointer_interaction  Enum8('Unobserved' = 0, 'No' = 1, 'Yes' = 2),
  had_keyboard_interaction Enum8('Unobserved' = 0, 'No' = 1, 'Yes' = 2),
  declared_web_driver      Enum8('Unobserved' = 0, 'No' = 1, 'Yes' = 2),

  correlation_id           String
)
ENGINE = MergeTree
PARTITION BY toYYYYMM(server_ts)
ORDER BY (site_id, server_ts, event_id)
TTL toDateTime(server_ts) + INTERVAL 12 MONTH
SETTINGS index_granularity = 8192, merge_with_ttl_timeout = 3600;
