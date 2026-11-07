# 0067 — Work in the background ends when the host does, and not before

- **Status**: accepted
- **Date**: 2026-11-06
- **Applies to**: both editions. In the free product: the services that judge visits, keep finished
  months, sweep closed accounts, make the visitor key's salt and refresh the two sets of reference
  data, the mail handed over quietly, and what the store's driver writes in the log. In `ee/`: the
  hourly usage rollup and the arrangements of closed accounts. Changes no schema, collects nothing and
  retains nothing.
- **Extends**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md), whose service that
  keeps finished months already ended a failed run without ending its loop. Every timed service now
  does, on one rule.
- **Followed by**: [0068](0068-a-website-s-posts-are-read-from-the-sitemap-it-names.md), whose service
  that reads sitemaps keeps to the same rule, and whose waits are held to the same 49 days.

## Context

A background service that lets an exception out stops the host: that is the framework's default, and
the host here is the whole engine, collector included. Under a restart policy it comes back, and where
the cause is still there it stops again.

Three things let an exception out.

- **A run that could not start.** The service that judges visits listed the websites outside any
  `catch`, so a control plane restarting for a moment ended judging and the engine with it.
- **A cancellation nobody asked for.** The `catch` that kept judging going let a cancellation
  through on the strength of its type: `when (failure is not OperationCanceledException)`. A client
  that stops waiting for a statement reports exactly that type, so a store that was slow for longer
  than the client waits stopped the engine instead of costing one run. The same filter stood in the
  sweep of closed accounts, in the usage rollup, where one account's processor not answering ended the
  pass for every account after it, and in the mail handed over quietly, where a mail server that did
  not answer became an error on a request built to answer alike whether or not an address exists.
- **A setting no timer can keep.** A refresh interval over about 49 days passed validation and then
  failed the service as it started, because a timer cannot wait that long.

The log misled in the other direction. Stopping an engine with a statement in hand, or a reader
leaving a screen while it loaded, wrote errors with stack traces from the store's driver, and stopping
during a download of reference data wrote the release, the release before it and the network table as
three failed fetches. Beneath all of it the client the engine reaches its store through wrote four
lines for every statement: 137,644 of the 138,209 lines the hosted installation's engine wrote in
thirteen hours on 2026-11-06.

## Decision

**Whether work was cancelled is asked of the token it was given, never of the exception.** A `catch`
that keeps a loop alive filters on `!token.IsCancellationRequested`. A cancellation while that token is
live is somebody else's: a client out of time, a processor that did not answer. It is a failure like
any other, written down, and the next website, account or run goes ahead. An architecture test fails the
build on the old shape.

**A run that fails as a whole ends there.** It is written down (5003 for judging, 5207 for keeping
months, 3608 for the sweep, 5112 for the rollup) and the next tick tries again from what the stores
then hold.

**Once the host is stopping, whatever the work in hand ends in is part of stopping.** Each of the seven
timed services takes any exception while its stopping token is cancelled and says nothing of it itself:
a store that went down with the machine a moment before the engine is not a fault for the service to
report, and every one of them picks up from what the stores and the disk hold on the next start. The check of the
payment catalogue in `ee/`, which runs once as the engine starts, already asked its token and let
nothing out; it is unchanged, and still writes down a check that failed.

**Stopping during a download says nothing about the download.** However the download in hand ends, the
part that arrived is discarded, because its name carries its month and a later download would not write
over it; no failure is written, and the release before is not asked for.

**The store's driver is not allowed to call an abandoned statement an error.** The driver writes every
statement that ends in an exception as an error. It writes through `StoreDriverLogging`, which leaves
out a statement that ended in a cancellation and passes everything else through as it was said. A
statement the engine gave up on because it ran out of time is a failure, though it arrives as a
cancellation: a client out of time puts a `TimeoutException` inside it, and it is written, with the
statement's identifier, which is all that names a statement the store never answered.

**A refresh interval is between five minutes and 49 days**, refused at start with a sentence naming the
setting, like every other setting.

**The HTTP client's own lines are kept to warnings** in `appsettings.json`. What is left in the engine's
log is what the engine said.

## Considered and refused

- **Telling the host to carry on when a service faults.** A service that faulted would then be stopped
  for good and nobody would be told: an installation still collecting and no longer judging. Stopping
  the host stays as what happens to a fault nobody foresaw, where a restart is the honest answer.
- **Retrying inside the run.** The next tick is the retry, and every service here already resumes from
  what the stores hold.
- **Silencing the driver's log by level.** It would take the statement's identifier off every real
  failure as well.

## Consequences

- A store that is slow or away costs late verdicts, a month kept a quarter of an hour later, a sweep an
  hour later. It no longer stops the engine. Reports that arrive while the telemetry store is away are
  still lost, as they always were; the collector is storing again the moment the store is back, with no
  restart in between.
- A failure in the last moments of a shutdown is not written by the service it ended. The store's driver
  and the database client still write what they met, unless it was a cancellation, and the next start
  does the work again.
- A request cancelled by its own reader leaves no line from the driver, and neither does one the
  dashboard gave up waiting for. The dashboard's own log records the second.

## Tests that make this true

- `ClassificationWorkerTests`: `A_Website_That_Fails_Does_Not_Stop_The_Next_Or_The_Loop`,
  `A_Run_That_Cannot_List_The_Websites_Does_Not_Stop_The_Engine`,
  `Shutting_Down_Part_Way_Through_A_Run_Is_Not_A_Failure`, `Nothing_Is_Judged_While_Switched_Off`.
- `ConsolidationWorkerTests`: `Shutting_Down_Part_Way_Through_A_Run_Is_Not_A_Failure`.
- `ClosedAccountPurgeServiceTests`: `A_Pass_That_Fails_Does_Not_Stop_The_Engine`,
  `Shutting_Down_Part_Way_Through_A_Pass_Is_Not_A_Failure`.
- `ClosedAccountPurgeTests`: `A_Client_Giving_Up_On_One_Account_Leaves_The_Next_Deleted`.
- `QuietSendTests`: `A_Mail_Server_That_Refuses_Goes_No_Further`,
  `A_Mail_Server_The_Client_Stopped_Waiting_For_Goes_No_Further`, `Whoever_Asked_Going_Away_Ends_The_Send`.
- `VisitorKeySaltRotationServiceTests`: `A_Rotation_That_Fails_Does_Not_Stop_The_Engine`,
  `Shutting_Down_Part_Way_Through_A_Rotation_Is_Not_A_Failure`.
- `ReferenceDownloadTests`: `An_Installation_Does_Not_Start_With_A_Wait_No_Timer_Can_Keep`,
  `Stopping_During_A_Place_Download_Says_Nothing_Of_It_And_Leaves_No_Part_Behind`,
  `Stopping_During_A_Crawler_List_Download_Says_Nothing_Of_It`.
- `StoreDriverLoggingTests`: `A_Statement_Somebody_Stopped_Waiting_For_Is_Not_Written`,
  `A_Statement_The_Store_Refuses_Is_Still_Written`,
  `A_Statement_That_Ran_Out_Of_Time_Is_Not_One_Somebody_Abandoned`,
  `Everything_Else_The_Driver_Says_Is_Written_As_It_Was_Said`.
- `CancellationTests`: `No_Failure_Is_Let_Through_For_Being_A_Cancellation`.
- In the commercial edition's suites: `A_Run_That_Fails_Does_Not_Stop_The_Engine`,
  `Shutting_Down_Part_Way_Through_A_Run_Is_Not_A_Failure` and
  `The_Hourly_Pass_Carries_On_Past_An_Account_The_Processor_Did_Not_Answer_For`.
