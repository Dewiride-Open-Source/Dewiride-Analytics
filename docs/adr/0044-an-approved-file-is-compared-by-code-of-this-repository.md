# 0044 — An approved file is compared by code of this repository

- **Status**: accepted
- **Date**: 2026-09-18
- **Applies to**: both editions. Adds no column, collects nothing new, and widens no envelope.
- **Follows**: [0043](0043-what-is-out-of-date-is-proposed-not-applied.md), whose first weekly
  proposals moved this package to the last release under its old licence, and reading that
  proposal is what surfaced the terms of the release after it; and
  [0031](0031-a-change-is-checked-before-anybody-reads-it.md), which is where the comparison runs.

## Context

Two suites approve text rather than assert on it. The statements the analytics and session
compilers produce, and the messages the product sends, are each rendered as text and compared
with a file committed beside the test — fifty-two of them — so that a change to generated SQL or
to what lands in somebody's inbox is read and agreed to by a person before it ships, and a
statement that quietly re-cut a customer's history fails a build instead. Approving a change is a
deliberate act: read the received file, then move it over the approved one.

The comparison itself came from a package. From its thirty-third major release, that package's
core is published under a licence that must be accepted at install time and that asks for an
Open Source Maintenance Fee from any organisation above a revenue threshold, and its build
carries a check that fails the compile unless a sponsorship or an exemption has been declared.
The version table at the root of the tree says in its own header that a dependency which needs
a paid key or carries strong copyleft has no place in a public repository, whatever it offers,
and names the packages excluded on those grounds: a contributor cloning this repository must be
able to build and run every suite with nothing but the tree.

The alternatives considered:

- **Stay on the last release under the old licence.** A pin that can never move again, on a
  package whose adapter is compiled against the test framework. The day the framework changes
  what that adapter relies on, the two suites stop compiling, and the choice is made then in a
  hurry rather than now with time.
- **Declare an exemption or pay.** Either puts a term into the build that every fork inherits and
  every contributor has to reason about, on a repository whose whole point is that nobody has to.
- **Approve by hand-written assertions instead.** Fifty-two renderings, several of them hundreds
  of lines, asserted piece by piece would be a suite nobody reads, and a suite nobody reads
  approves nothing.

## Decision

**The comparison is this repository's own, and the files it compares do not change.** A test
project, `Dewiride.Analytics.Testing`, holds a single comparison and the tests that prove it.
A test hands it what was rendered; it finds the approved file from the test's own class and
method, in the directory of the test project, and passes when the two are the same. When they
are not, or nothing has been approved yet, it writes what it saw to a received file beside the
approved one and fails naming both, and the act of approving is exactly what it was.

**The failure is readable where it happened.** The message carries the first line that differs
and the whole of what was rendered, because on a build agent the received file is discarded with
the machine and the log is all that reaches a reader — and a proposal from the updater of 0043 is
built there first, before anybody has run the suite at a desk.

Three things about how it finds and reads the files are decisions rather than accidents:

**The directory is recorded at build time, not taken from the compiler.** Every project under
`backend/tests` writes the directory it was built from into its assembly's metadata, and the
comparison reads it from the test's assembly. The obvious alternative — the caller's source
path, which the compiler will supply — is wrong on a build agent: this repository builds there
with reproducible source paths, which map every path under the repository to a placeholder, and
a comparison looking for its files under that placeholder finds nothing. The package this
replaces avoided the problem by switching reproducible paths off for any project that
referenced it, silently.

**Line endings are not compared; everything else is.** The approved files are written with line
feeds and a signature, the renderers write whatever the platform uses, and source control on a
contributor's machine may rewrite either on the way in or out, so both sides are read as line
feeds. Trailing newlines and trailing spaces are compared as they are: a renderer that stops
ending its output the way it did is a change somebody should read. A received file is written
the way the approved ones are kept, so moving it over the approved one changes nothing but the
text.

**A test that takes arguments is refused.** It runs once per row of its data, and every row would
be compared against the one file. A snapshot belongs to a test without arguments, and the
comparison says so rather than letting the rows overwrite each other's received file.

## Consequences

Every approved file is byte-for-byte what it was, and every suite runs on a clean checkout with
nothing accepted and nothing declared.

**Approval has no diff tool.** The package would open one on a developer's machine; the suite
that approves statements had already switched that off, because on a build agent it opens a
window nobody sees and holds the run open until it is killed. The received file sits beside the
approved one, and any diff tool the developer already has reads the two.

**Nothing is scrubbed.** The package could replace dates, identifiers and machine names in what
it compared before writing it. No suite here used that, because what these suites render is
deterministic by construction: the clock is injected everywhere and an ambient read fails the
build. A suite that needs a value hidden renders it hidden, in its own report, where the choice
is visible.

**Nothing switches the compiler's reproducibility off.** The two suites compile with the same
flags as every other project in the tree. Every test assembly does carry the absolute directory it
was built from, so it is reproducible only for the checkout that built it, and that is accepted
because a test assembly ships nowhere.

**The version table names the package among the excluded.** That list is held by review alone,
and this is the first entry added to it because a package already in the tree changed its terms
rather than because one was proposed. The weekly proposals of 0043 are what made the
change visible within days of its release, which is the arrangement working as intended.
