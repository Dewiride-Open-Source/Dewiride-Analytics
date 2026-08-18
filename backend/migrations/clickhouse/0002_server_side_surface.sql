-- ---------------------------------------------------------------------------
-- 0002_server_side_surface — records reports that arrive from a site's own
-- server without naming a surface this product already knows about.
--
-- The named surfaces are the integrations shipped with the product. A customer
-- who writes their own reporter against the published wire format is not any of
-- them, and filing that traffic under 'Unknown' would make it indistinguishable
-- from an event whose provenance was never established — which is the one thing
-- the surface column exists to prevent.
--
-- An enumeration is widened by naming every member again, including the ones
-- that already exist and with their existing numbers. ClickHouse rewrites no
-- data for this: the stored value is the number, and the members that keep
-- theirs keep their rows.
-- ---------------------------------------------------------------------------

ALTER TABLE events
  MODIFY COLUMN surface Enum8(
    'Unknown' = 0,
    'BrowserTracker' = 1,
    'NoScriptPixel' = 2,
    'CloudflareWorker' = 3,
    'WordPressPlugin' = 4,
    'NetlifyEdge' = 5,
    'VercelEdge' = 6,
    'AspNetCoreMiddleware' = 7,
    'NextJsMiddleware' = 8,
    'LogImport' = 9,
    'ServerSide' = 10
  );
