-- ---------------------------------------------------------------------------
-- 0004_visitor_context — where a visitor was, and what they were using.
--
-- Every column here is derived at ingest from a value that does not survive:
-- ip_address is cleared 72 hours after the event by the policy in 0001. So
-- these cannot be backfilled and there is no later job that could fill them.
-- Rows written before this migration keep the defaults below for ever, which is
-- correct — nothing was ever known about them — and is why the empty string and
-- 'Unknown' mean "not established" rather than "none".
--
-- Adding a column to a MergeTree is a metadata change. No part is rewritten and
-- no data is read, so this is safe to run against a self-hoster's existing rows
-- however many of them there are.
--
-- Deliberately absent: latitude and longitude. A country list and a town list
-- need neither, and a pair of coordinates locating a reader to within a few
-- streets is a different kind of record from the name of their nearest town.
-- See docs/adr/0011-visitor-location-and-network.md.
-- ---------------------------------------------------------------------------

ALTER TABLE events
  -- ISO 3166-1 alpha-2. The code rather than a name, so the interface can write
  -- it in the reader's own language and so the column's stored set stays small.
  ADD COLUMN IF NOT EXISTS country_code LowCardinality(String) AFTER ip_address,

  -- State, province or region: its standard code where it has one, its English
  -- name where it does not.
  ADD COLUMN IF NOT EXISTS subdivision LowCardinality(String) AFTER country_code,

  -- Town or city, in English — the free tier of the place data publishes no
  -- other language. Not LowCardinality: a busy site sees tens of thousands of
  -- towns, which is past the point where a dictionary per part helps.
  ADD COLUMN IF NOT EXISTS city String AFTER subdivision,

  -- Autonomous system, and who runs it. Zero and empty mean the address fell in
  -- no published range, which is the honest answer for a private address as
  -- well as for an unallocated one.
  ADD COLUMN IF NOT EXISTS autonomous_system UInt32 AFTER city,
  ADD COLUMN IF NOT EXISTS network_owner LowCardinality(String) AFTER autonomous_system,

  -- What the visit was made on. 'Unknown' is a real answer and a common one:
  -- much of what reaches a website is not a device at all.
  ADD COLUMN IF NOT EXISTS device_class
    Enum8('Unknown' = 0, 'Phone' = 1, 'Tablet' = 2, 'Desktop' = 3, 'Other' = 4)
    AFTER network_owner,

  -- Families without versions. A version number changes every few weeks, would
  -- make both of these unbounded, and narrows a visitor far more than knowing
  -- which browser they favour.
  ADD COLUMN IF NOT EXISTS browser_family LowCardinality(String) AFTER device_class,
  ADD COLUMN IF NOT EXISTS operating_system LowCardinality(String) AFTER browser_family,

  -- Whether the client declared itself handheld, from the low-entropy hint that
  -- browsers send of their own accord. Three-state for the same reason the
  -- interaction columns are: browsers outside one family send nothing, and that
  -- is not a claim about the device.
  ADD COLUMN IF NOT EXISTS declared_mobile
    Enum8('Unobserved' = 0, 'No' = 1, 'Yes' = 2)
    AFTER operating_system;
