# 0083 — A figure is called what other analytics tools call it

- **Status**: accepted
- **Date**: 2026-11-27
- **Applies to**: both editions, and the whole of it is in the free product. Changes words in the
  dashboard's message catalogue and removes the second set a screen kept to people chose between;
  changes no statement, no answer on the wire, no column and no migration; collects nothing new and
  widens no retention envelope. Nothing in `integrations/` or in the tracker changes, because nothing
  about what is captured changes, and nothing in `ee/` moves: the hosted site's own pages describe the
  figure as daily visitors, which is what it counts, and name no view of the chart.
- **Follows**: [0081](0081-a-visitor-is-somebody-who-opened-a-page.md), which made a stretch's visitors
  the keys a page was delivered to in it, whatever the stretch. This record is the naming half of the
  same question.
- **Revises**: [0041](0041-one-population-for-the-whole-screen.md), whose figures kept to people carried
  the people in their names — "Page views by people", "Daily visitors judged to be people", "Pages per
  person". Kept to people, every figure keeps the name it has for everybody, and the sentence under the
  switch that 0041 placed there says whom it counts.
- **Revises**: [0063](0063-the-picture-is-drawn-by-the-day-the-week-or-the-month.md), whose "visitors stay
  daily visitors" keeps its counting and gives up the word: the figure is called "Visitors" by the hour,
  the day, the week, the month and the year.

## Context

On 2026-11-27 the owner could not tell what "Who came → People", "Page views by people" and "Daily
visitors judged to be people" were, or why the last was not the first, and asked what other analytics
tools call these figures. 0081 settled the counting. What remained was that one figure wore up to four
names: "Daily visitors" on the cards and the chart, "Visitors" on the chart by the hour, "Daily visitors
judged to be people" and "Visitors judged to be people" once the screen was kept to people. And the
chart's view whose total is visits was called "Who came", a question, beside a view called "How much".

The overview has three figures: visits, the sittings a page view opens (0034); visitors, the daily keys a
page was delivered to (0081); and page views. What the established tools call them, from their own
documentation on 2026-11-27:

| Tool                     | Distinct people | Sittings     | Pages           |
| ------------------------ | --------------- | ------------ | --------------- |
| Google Analytics 4       | Users           | Sessions     | Views           |
| Plausible                | Unique visitors | Total visits | Total pageviews |
| Matomo                   | Unique visitors | Visits       | Pageviews       |
| Fathom                   | Visitors        | —            | Views           |
| Jetpack Stats            | Visitors        | —            | Views           |
| Cloudflare Web Analytics | —               | Visits       | Page views      |

Sources: Google's [user metrics](https://support.google.com/analytics/answer/12253918) and
[dimensions and metrics](https://support.google.com/analytics/table/13948007); Plausible's
[metrics definitions](https://plausible.io/docs/metrics-definitions); Matomo's
[unique visitors](https://matomo.org/faq/general/faq_43/); Fathom's
[measurement](https://usefathom.com/docs/start/measurement); Jetpack's
[traffic dashboard](https://jetpack.com/support/jetpack-stats/traffic-dashboard/); Cloudflare's
[high-level metrics](https://developers.cloudflare.com/web-analytics/data-metrics/high-level-metrics/).

## Decision

**Visits, Visitors and Page views** (owner decision D2), the words most of the tools above use. Not
Google's Users and Sessions: a Google user is followed across days by a cookie, while a visitor here is a
key rotated every day (0064's retention envelope), as Fathom's and Plausible's are, and both call that
count visitors. The cards read "Page views", "Visitors" and "Pages per visitor". The chart's two views are
"Visits", the stack of who and what made them, and "Visitors & page views"; the way out of a period with
nothing judged in it is "See visitors and page views". The addresses are untouched: `show=who` and
`show=activity` mean what they meant, under their new labels.

**One name for a figure, whatever the cut.** Since 0081 an hour's visitors are the keys a page was
delivered to in that hour, exactly as a day's are those of that day, so the hour needs no name of its
own. "Daily" leaves the name and stays in its meaning: the card's note "Someone who returns tomorrow
counts again." and the caption of a week, a month or a year, "somebody who comes back on another day
counts again", say it where it matters.

**Kept to people, a figure keeps its name.** "People only" narrows whom every figure counts; it does not
make them other figures. The overview, Insights and a post's page each say so once, in "Every figure
counts only the visits judged to be people." under the switch, and the cards, the chart's lines and the
table's columns, the highlights of Insights and a post's views wear the names they wear for everybody.
The chart's own description, which a screen reader hears with nothing beside it, still says whom it
counts: "Page views and visitors among people on …".

**The verdicts keep their names.** People, Machinery, Unwanted and Can't say, and the switch "People
only", are unchanged (owner decisions D3 and D4). The question was about the figures.

## Consequences

- The catalogue loses `dashboard.metrics.people`, `dashboard.chart.activity.visitorsByHour`, the three
  measure names under `dashboard.chart.people.activity`, `insights.post.highlights.viewsPeople` and
  `insights.post.chart.measurePeople`. The overview's cards, the highlights of Insights and of a post no
  longer take whether the screen is kept to people, and the chart's measure names no longer depend on the
  cut. A translation has each figure's name to write once.
- No number moves: the same period reads the same figures before and after this record, under its names.
- The sentence under the switch is now the only place a figure kept to people says so, which is where
  0041 put it for every other figure on the screen.

## Tests that make this true

- `traffic-chart.test.tsx`: "keeps the cards’ names for the measures when the figures are the people’s",
  "calls the people of each hour by the cards’ name" and the same of each week, "calls an hour’s visitors
  by the cards’ name", "calls a week’s visitors by the cards’ name", "calls a month’s visitors by the
  cards’ name", "gives the people’s earlier period the cards’ names too", and the views pressed as
  "Visits" and "See visitors and page views".
- `dashboard.test.tsx`: "labels the cards as everybody’s are", and the view pressed as "Visitors & page
  views".
- `insights.test.tsx` "asks for people's figures and says so once" and `post-insights.test.tsx` "asks for
  people's views and says so once", which read the figures' plain names beside the one sentence.
