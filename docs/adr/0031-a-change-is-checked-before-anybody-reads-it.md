# 0031 — A change is checked before anybody reads it

- **Status**: accepted
- **Date**: 2026-08-26
- **Applies to**: both editions. Adds no column, collects nothing new, and widens no envelope.

## Context

Nothing in this repository had ever been checked by anything other than the person who wrote it.
`.github/` was empty. Every claim about a quality gate rested on somebody running the commands on
their own machine and remembering to, and the record of whether they had is the fact that they said
so.

That is a weak arrangement on any project and a bad one here, for two reasons particular to this
repository:

- **It is public.** A credential pushed to it is readable by everybody from that moment, and cannot
  be taken back by deleting it — the commit that added it is still there, and anybody watching the
  event stream already has it. The only honest response to a leak is to withdraw the credential,
  which for a payment key means an outage. Catching one before the push is worth a great deal;
  catching one an hour afterwards is worth almost nothing.
- **It takes contributions.** A contributor cannot run the checks a maintainer runs unless something
  runs them for the contribution, and a maintainer reviewing a change has no way to know whether the
  suites still pass except by fetching it and running them.

The alternatives considered:

- **Leave it to discipline.** It is what was happening. It works exactly until the day somebody is
  in a hurry, and there is no way to tell from the outside which day that was.
- **Check only what changed.** Cheaper, and it decides in advance which failures are possible. The
  suites here are minutes, not hours; there is nothing to buy.
- **One workflow that does everything.** Simpler to read and worse to use: a formatting complaint
  and a failing classification suite become the same red mark, and the person who caused one waits
  for the other.

## Decision

**Every push and every proposed change is compiled and put through every suite, and its whole
history is searched for credentials, before anybody is asked to read it.**

Two workflows, deliberately separate, so that a failure names itself:

- **Build** compiles the solution and runs all eight suites, and separately installs the workspace
  and runs the whole frontend gate. The two are independent jobs: an engine change and a dashboard
  change do not wait for each other, and a red mark says which half is red.
- **Secrets** searches the entire commit history — not the working tree, because a key removed in a
  later commit is still a key anybody can read out of an earlier one.

Three things follow from that, and each of them is a decision rather than a detail:

**There is no separate analysis pass, because there is nothing left for one to find.**
`SonarAnalyzer.CSharp` runs as a build analyser and warnings are errors, so a rule violation is
already a failed compile. Running an analyser over code that would not compile if it had anything
to say is a second answer to a settled question.

**The secret scanner is fetched as its own published binary, at a pinned version, checked against a
pinned hash.** The ready-made action for it asks an organisation to sign up for a licence key and
keep it as a repository secret — a credential introduced in order to go looking for credentials, and
one more thing that expires. The tool itself is MIT and needs nothing but the file. Pinning the hash
as well as the version means the step fetches one exact file and refuses anything else, which is the
whole of its supply chain and short enough to read.

**What the scanner may pass over is written down and narrow.** Its heuristic rule matches any long,
high-entropy string, and the test fixtures here are full of invented identifiers that exist so a
test reads the same way every time it runs. That rule alone is narrowed, and only inside the test
trees. Every rule that recognises a real provider's credential by its shape still applies there, so
a live key pasted into a test is still caught by the rule that knows what it is. Switching the
heuristic off, or allowing the whole repository, would have thrown that away too.

## Consequences

A change now cannot reach the default branch without compiling, passing every suite, and having its
history searched. A contributor gets the same answer a maintainer would, without either of them
having to trust the other's machine.

**A hosted analysis project is still not wired, and this does not pretend otherwise.** Reporting
findings and coverage to a SonarQube server, and keeping their history, needs a server to report to,
and there is none. The rules themselves are enforced — the C# ones at compile time, the frontend ones
by the lint and format steps — so what is missing is the record over time rather than the gate. It is
missing rather than approximated: a workflow that quietly did nothing when its credentials were
absent would be worse than no workflow, because it would be a green mark that means nothing.

Coverage is measured but not yet reported anywhere, for the same reason.
