-- ---------------------------------------------------------------------------
-- 0013_presses_on_fields_without_names — removes the name stored with every
-- press on a field.
--
-- A field's name used to be read from the field: what the site declared for it
-- where it declared anything, and otherwise its text — which, for a text area on
-- a page built with React, is what somebody typed into it, kept in step as they
-- typed. A stored name cannot be told apart from the typing without reading it,
-- so every field's name goes. The press itself, the page it was on and the kind
-- of control it was are kept; presses on links and buttons are not touched.
--
-- An update of a column outside the sorting key, run in the background like
-- 0012. Running it again finds nothing to change.
-- ---------------------------------------------------------------------------

ALTER TABLE events
  UPDATE action_label = ''
  WHERE kind = 'Action' AND action_control = 'Field' AND action_label != '';
