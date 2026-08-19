-- ---------------------------------------------------------------------------
-- 0005_operated_controls — what a visitor pressed, for the sites that record it.
--
-- A fourth kind of report rather than a count on an existing one: the question
-- is which control was operated, and a number saying how many times somebody
-- pressed something answers none of it. Widening an Enum8 by adding a member at
-- the end, with every existing member keeping the number it already had, is a
-- metadata change — the same operation 0002 performed on the surface column.
--
-- What is kept of a press is deliberately small. The kind of control comes from
-- a closed set resolved at ingest, so no part of anybody's markup is stored.
-- The name is the site's own writing about its own control, cut to 64
-- characters, and never the contents of a field. The destination is a path for
-- somewhere on the same site and a host alone for anywhere else; an address to
-- write to or ring records only that it was used, because the address names a
-- person. There are no coordinates, no element identifiers and no selector
-- paths: none of them answers the question, and every one of them describes the
-- reader rather than the reading.
--
-- Rows written before this migration keep the defaults below, which is correct:
-- 'Unknown' and 'None' mean nobody was watching for this, and every one of those
-- rows is a page view or a reading rather than a press.
--
-- See docs/adr/0012-operated-controls.md.
-- ---------------------------------------------------------------------------

ALTER TABLE events
  MODIFY COLUMN kind Enum8(
    'PageView' = 1,
    'Engagement' = 2,
    'Exit' = 3,
    'Action' = 4);

ALTER TABLE events
  -- Held as a closed set rather than as whatever the page called the element,
  -- so that a value can be shown to somebody without either leaking a site's
  -- markup or writing prose around an arbitrary string.
  ADD COLUMN IF NOT EXISTS action_control Enum8(
    'Unknown' = 0,
    'Link' = 1,
    'Button' = 2,
    'Field' = 3) AFTER declared_web_driver,

  -- The site's own name for its own control. Not LowCardinality: a large site
  -- has thousands of distinct control names, which is past the point where a
  -- dictionary per part earns its keep.
  ADD COLUMN IF NOT EXISTS action_label String AFTER action_control,

  -- A path for somewhere on this site, a host alone for anywhere else, and
  -- empty for a control that pointed nowhere or whose destination names a
  -- person.
  ADD COLUMN IF NOT EXISTS action_target String AFTER action_label,

  ADD COLUMN IF NOT EXISTS action_target_kind Enum8(
    'None' = 0,
    'Internal' = 1,
    'External' = 2,
    'Contact' = 3) AFTER action_target;
