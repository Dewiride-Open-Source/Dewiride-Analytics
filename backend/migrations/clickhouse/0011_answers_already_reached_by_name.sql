-- ---------------------------------------------------------------------------
-- 0011_answers_already_reached_by_name — keeps every answer the stored
-- verdicts still carry.
--
-- 0010 keeps each answer from the moment it is reached; one reached earlier
-- lives only in the verdict it produced. Every such verdict names the company
-- that vouched for the visit: as the operator of identity.confirmed_crawler,
-- or as the operator of identity.false_claim, which is what a visit naming one
-- company while arriving from another company's address is judged. Only the
-- detector that reads the settled company writes either code, so every stored
-- verdict carrying one records an answer this installation reached, and each
-- answer a verdict still holds is copied here once, beside its visit.
--
-- Across every ruleset, because a later set of rules may already have lost an
-- answer an earlier one reached: a visit judged again after its address was
-- cleared was judged on its claim alone. Per visit, the newest verdict that
-- still names a company is the one copied, stamped with the moment the first
-- of them was reached — so running this again writes the same rows, and the
-- table keeps one of each. An answer that a later verdict under the same rules
-- replaced is copied only while the store still holds the earlier verdict;
-- once a merge has discarded that verdict, the answer is gone with it.
--
-- The operator of identity.declared_crawler is never read. That is the company
-- the visitor named itself after, which is a claim.
--
-- Answers the collector marked on the activity itself are copied too — from a
-- published list, or from an address a name check had settled before the
-- visit arrived. The activity is read before anything kept here, so the copy
-- changes nothing about them, and telling them apart would mean reading every
-- visit's activity back, where a visit's span can hold another visit's reports.
--
-- One statement, bounded by the verdicts' own twelve-month retention rather
-- than by how long the installation has been running.
-- ---------------------------------------------------------------------------

INSERT INTO name_confirmations (site_id, session_key, started_at, operator, settled_at)
SELECT
    site_id,
    session_key,
    min(started_at) AS started_at,
    argMax(company, classified_at) AS operator,
    min(classified_at) AS settled_at
FROM
(
    SELECT
        site_id,
        session_key,
        started_at,
        classified_at,
        arrayFirst(
            (parameters, code) -> code IN ('identity.confirmed_crawler', 'identity.false_claim'),
            signal_parameters,
            signal_codes)['operator'] AS company
    FROM session_classifications
    WHERE hasAny(signal_codes, ['identity.confirmed_crawler', 'identity.false_claim'])
)
WHERE company != ''
GROUP BY site_id, session_key;
