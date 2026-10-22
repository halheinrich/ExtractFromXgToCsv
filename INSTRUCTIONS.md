# ExtractFromXgToCsv

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Blazor WebAssembly / xUnit. Visual Studio 2026 on Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv\ExtractFromXgToCsv.slnx`

## Repo

https://github.com/halheinrich/ExtractFromXgToCsv — branch `main`.

## Depends on

- **ConvertXgToJson_Lib** — `XgSourceReader`, the one read of one source,
  which every file is read through (`ReadFileAsync`; see "Reading, converting,
  skipping and committing"); `XgFileReader` for discovery
  (`EnumerateXgFormatFiles`); `XgDecisionIterator`, under
  `XgIteratorOptions` (the run's ranking and the optional `OpeningBook`; see
  "The ranking" and "Opening-book enrichment"); `XgpExporter` (slice and anonymize-copy surfaces) for the XGP
  export's payloads; `OpeningBook`.
- **XgFilter_Lib** — `FilterConfig.Build()` / `DecisionFilterSet` for the
  filter pipeline; `FilteredDecisionIterator`, whose parsed-file filtering
  operation (`IterateFile`, `IterateFileDiagrams`) every decoded file is
  filtered through, the filter set's early-exit hints wired.
- **BgDataTypes_Lib** — `DecisionRow`, the decision records (`BgDecisionData`
  and its two kinds, `CheckerPlayDecision` and `CubeDecision`),
  `IDecisionFilterData`, `PlayRanking` and constituent types. All four output
  pathways (CSV, Diagram JSON, PPTX, and PDF) share the filter pipeline via
  `IDecisionFilterData`: a row is its own view, and a record is filtered
  through its view for the run's ranking (`ViewFor`).
- **XgFilter_Razor** — the app-scoped filter-setup owner `FilterSetup` (the
  draft, the applied baseline, this boot's restoration and its notices, and
  the gate's filter term, read as `FilterSetupSnapshot`), its registration
  `AddFilterSurface<TRefusalSink>` and the sink interface
  `IFilterStorageRefusalSink`; `FilterSurface` (the one consumer-facing
  filter composite: filter panel + saved-filters panel, over the owner);
  `FilterSourceToken`, `IDocumentStorage` / `DocumentStorageException`,
  `SavedFiltersDocument`. The host's contract is that library's "Host
  contract": register, report the source, read the gate, key derived facts
  by their inputs, show the storage condition from the sink (see "Filter
  setup").
  Referenced by the WASM Client csproj only — the server has no filter UI to
  host, and its saved-filters file relay must stay ignorant of the document
  names (see Pitfalls).
- **BgUiPrimitives_Razor** — `Notice`, the one component every alert box in
  this host renders through, with the two selectors its callers name:
  `NoticeKind` (what the notice tells the user, and so its colour) and
  `NoticeAnnouncement` (polite, assertive, or by an enclosing region). It
  provides the box, the close button, the whole-box dismiss target, the
  live-region role and where that role sits; this host provides what each box
  *is* and, for a dismissible one, the dismissal's holder (see "Notices").
  And `BrowserStorage`, the one guarded access to the browser's storage
  areas (registered with `AddBrowserStorage`): every call is attempted,
  there is no latch, and a refusal is a result, never an exception. Every
  storage call this host makes goes through it (see "Browser storage").
  Referenced **directly** by the WASM Client csproj, although it also arrives
  transitively through XgFilter_Razor: this host's own components and
  services use it, and a dependency a project uses is one it states. It
  places two obligations on the host — Bootstrap's stylesheet, and a link to
  the host's scoped-CSS bundle (see Pitfalls).
- **BgUiPrimitives_Razor.TestSupport** — `BrowserStoragePlan`
  (halheinrich/backgammon#377), which answers the real `BrowserStorage`'s
  interop calls from expectations stated in the accessor's terms, and
  `NoticeBox`, the notice reader. So no test here spells a storage interop
  identifier. **Referenced by the test csproj only**; the assembly declares
  itself unsupported on the browser platform.
- **XgFilter_Razor.TestSupport** — `FilterSurfaceStorage`, which states the
  filter surface's own storage calls on a `BrowserStoragePlan` — this boot's
  restoration, each panel mount's preference reads, each commit's write —
  without naming the surface's keys; and `FilterRestorationMarker`, for
  browser tests. **Referenced by the test csproj only** — it is
  `IsPackable=false`, ships beside nothing, and no app-graph project may
  reference it.
- **BgDataTypes_Lib.TestSupport** — the producer's record builders,
  `TestRecords`: the one way this repo's tests build a record or a row, so no
  test restates a record's construction. **Referenced by the test csproj
  only**, like the two above; the assembly declares itself
  unsupported on the browser platform, so a use from the Client is a CA1416
  build error. Production code builds records only through the producers'
  public surface (the name preview's sample, see "XGP export").
- **BackgammonDiagram_Lib** — `DiagramRequest.ForDecision(record, ranking)`
  and the diagram model/options types. Native-free (SVG-only) core.
- **BackgammonDiagram_Lib.ExportRaster** — `DiagramRasterRenderer.RenderPptx`
  / `RenderPdf` for the PPTX and PDF output pathways. Server-side only; the
  client csproj does not reference it (the native rasterization deps —
  SkiaSharp, QuestPDF, OpenXml — live here and aren't available under WASM,
  see Pitfalls).
- **QuestPDF** (transitive, via BackgammonDiagram_Lib.ExportRaster) — PDF
  builder. A license must be configured at server startup before `RenderPdf`
  is invoked; see Pitfalls.

## Layout

Three projects under `ExtractFromXgToCsv.slnx`, governed by repo-root
`Directory.Build.props` (TFM, nullable, `TreatWarningsAsErrors`, XML doc
generation) and `Directory.Packages.props` (Central Package Management).

**`ExtractFromXgToCsv/`** — the server host, thin by design (see
Architecture). Four areas:

- **Hosting** — `Program.cs`: the services' registrations and the QuestPDF
  license. With it `Components/` (the root document, router, layout and server
  error page that host the WASM app, `prerender:false`; the root document
  links Bootstrap, `app.css` and this host's scoped-CSS bundle) and
  `appsettings*.json` (the optional `OpeningBookPath`).
- **HTTP surface** — `Controllers/`: the Local job protocol (start, status,
  cancel, acknowledge), opening-book status, the
  saved-filters file relay, shutdown. Thin pass-throughs to the services (see
  Public API).
- **Local-mode services** — `Services/`: `LocalFolderProcessor` (the four
  output pathways over a folder), `CsvLayout` (the CSV's columns and cells,
  stated once), its destination seam `IOutputDestination`
  (`FileSystemDestination`, which classifies its own create refusal as
  `OutputFileExistsException`; `DestinationFailures.cs` holds the run-level
  destination failures — an existing XGP file, an unfinished one, an
  unfinished output file, and a cancellation that left one — and their
  shared wording) and `RunRecord` (one run's record:
  what it skipped and, for XGP, what it committed), `JobStore` (the jobs and
  the starts admitted — see "A Local run's job"), `OpeningBookProvider`
  (resolves and loads the book), `FilterDocumentStore` (the named-file IO
  behind the relay).
- **Static assets** — `wwwroot/`: `app.css` (the busy-cursor rule, the app's
  only bespoke CSS), and vendored Bootstrap.

**`ExtractFromXgToCsv.Client/`** — the Blazor WebAssembly app: all UI.
`Program.cs` registers the
`HttpClient` and calls `AddClientServices`
(`ClientServiceCollectionExtensions`), the one registration of what the
components are rendered with — the test suite calls it too. Three areas:

- **Components** — `Pages/Home.razor` (the shell, the `FilterSurface` host
  and the source reporter), the panel, `LocalModePanel`, and
  `SkipListNotice`, the one rendering of a skip record (see Components).
- **Services** — the client's own state: `HttpDocumentStorage` (the storage
  seam over the server's file relay; this app's settings keys go through
  BgUiPrimitives_Razor's `ReportingBrowserStorage`, and the storage
  condition is BgUiPrimitives_Razor's `BrowserStorageCondition` — see
  "Browser storage"). And the page's side of the job protocol (see "A Local
  run's job"): `ILocalJobs` and its `LocalJobsClient` (the protocol's wire),
  `LocalJobTiming` and `IJobIdentities` (seams for a test's waits and
  identities).
- **Shared** — the wire types the server references too (`ProcessRequest`,
  `ProcessingProgress` with `SkipRecord`, `SkippedItem` and
  `XgpExportCommit`, `JobPhase`, `OutputFormat` and `OutputFormatNames`,
  `OpeningBookStatus`, the strict enum converter; and the
  protocol's `StartRequest`, `StartResponse` and `ProtocolRefusal`), the
  `.xgp` naming engine (`XgpExportOptions`, `XgpNameAllocator` and its
  internal parts) with the name preview's sample (`XgpNamePreview`),
  `XgpPayload` (the one statement of how a decision becomes an `.xgp`
  payload), and `AppRanking`, the application's one ranking. See
  Client-shared types.

**`ExtractFromXgToCsv.Tests/`** — xUnit, one class per contract: direct tests
of the services and the naming engine, bUnit tests of `Home` and the panel,
hosted `WebApplicationFactory` pins for the file relay, and processor runs
over real files. Records and rows a test states come from the producer's
`TestRecords`. Shared support: `bUnitTestHelpers` (private-field access, stub
HTTP handlers, and a Local run to completion), `HomeTesting` (Home's
registration, render and gestures, and `HomeStoragePlan` — Home's storage
stated on a `BrowserStoragePlan`), `NoticeTesting` (a dismissal through
BgUiPrimitives' `NoticeBox`, settled before its outcome is read, and the
bound on awaiting an operation), `FixtureHelper`, `MalformedXg`
(the synthesized unreadable input skip tests write), `XgpAnonymizeAssert`,
`ProcessorDecisions` (a fixture's rows and records as the processor reaches
them), `ScriptedDestination` (real output files whose create,
flush, write, close or removal a test can fail, and which records every
removal asked of it) and `LocalProtocolTesting`
(the real Local server in memory, which can restart — a new process, its
job store empty — over the same folders, behind a
`ProtocolWire` that can drop a response, fail a request, hold a request —
delivered even if its sender stops waiting — hold a response, or answer a
status itself; a `FileGate` that holds a run as it opens its next output
file, over the file system or another destination; `MemoryBrowserStorage`;
`LocalPage` — one page of the app over both, so a reload, disposing the
first page, is another page — and `AnswerHeldJobs`, a page's jobs client
holding its first start's answer after it has arrived). The test loggers —
`RecordingLogger<T>` (each entry's level, message and exception, as a
`LogEntry`) and `ThrowingLogger<T>` (a logger every entry of which fails) —
are BgDataTypes_Lib.TestSupport's, not this project's: common code has one
shared owner (Hal's ruling, halheinrich/backgammon#424, 2026-10-19).
Fixtures are the umbrella's `TestData/FixtureFiles`,
linked into the build output rather than kept in this repo (see Test project,
and the fixture pitfall).

## Architecture

### Server host — thin by design

The server project is a launcher plus an HTTP surface. It runs
`LocalFolderProcessor` on a background task and exposes job status, and it
serves the WASM payload. It registers `JobStore` (a singleton: the jobs live
as long as the process) and `LocalFolderProcessor` (scoped). Beside the processor, `CsvLayout` is the
one statement of the CSV's columns and cells; the processor writes what it
formats (see "CSV output"). All rendering is `InteractiveWebAssembly`
with `prerender:false`; nothing renders interactively on the server.

### Reading, converting, skipping and committing

The umbrella's `../SPEC-notices.md` §7 is the ratified model (v1.12.2 Arc 3,
halheinrich/backgammon#374): what a skip is, who owns each skip record and
how long it lasts, the ownership of late completions, readiness, the commit
boundaries and the XGP numbering. This section says how the code meets it, not what it rules.

- **One entry, one record, one rendering** (§7.1). A skipped unit
  is a `SkippedItem` — a file, or a file and its `DecisionId` — in a
  `SkipRecord`, the immutable record type; `SkipListNotice`
  renders any of them with the same headline and entry wording and, where
  the list needs one, a label naming its owner.
- **Local mode** (`LocalFolderProcessor`, §7.2). Each output run reads each
  discovered file once (`XgSourceReader.ReadFileAsync`, format `Xg`, the
  run's token; exact bytes kept only on the XGP pathway when not
  anonymizing) and filters each decoded file through a
  `FilteredDecisionIterator` built with the run's filters, ranking and
  opening book. All four pathways share one walk, `ForEachFileAsync`: a
  failed read is a read skip, the file's contribution is staged whole inside
  the file's boundary (a failure is a conversion skip, so a file that fails
  midway contributes nothing), and the commit — the CSV writer (each line
  formatted by `CsvLayout`), the JSON
  write, the deck build, an XGP file write — is outside it and fails the
  run. Every output file is opened through the destination seam
  (`IOutputDestination`), **created new, never overwriting** (§7.5): each
  attempt is one create-new (`IOutputDestination.CreateNew`), the
  destination classifies its own refusal (`OutputFileExistsException`), and
  a collision moves the run on to the next free name of the file's sequence
  (`LocalFolderProcessor.CreateUnderFreeName`, numbered by
  `NumberedFileName`, the one statement of the ` (n)` rule), and the run
  carries on. Every output is finished before the run reports success: the
  CSV writer, and the one file a Diagram JSON or deck run writes, are
  written and then closed whatever the write did (`WriteThenCloseAsync`),
  and only then does the run report "Done" with the path the file was
  created at (`RunProgress.ReportDone`, carried as
  `ProcessingProgress.WrittenPath`; an XGP run reports its folder),
  so a failure flushing one is the run's error, never a success already
  published. The write's own ending stands (§7.1): a cancellation stays a
  stopped run, with a failure closing the unfinished file reported beside it
  (`CancelledWithUnfinishedOutputException`, carried to the client as
  `ProcessingProgress.CloseFailureMessage`), and a failed write stays the
  run's error, the close failure beside it (`UnfinishedOutputException`);
  that one file is never removed. An XGP payload is
  prepared within its decision's own boundary (`XgpPayload`; a failure is an
  export skip and consumes no number) and written outside it, created new
  under the allocator's name or the next free one: a file the run created
  but could not write or close is removed under the name it was created
  with, any failure to close or remove it reported beside the primary one
  (`UnfinishedFileException`, in the wording `DestinationFailureText` states
  once); a failure to create removes nothing, and a file that existed
  before the run is never touched. The run's
  `RunRecord` holds its skips and, for XGP, its `XgpExportCommit`, replaced
  as each file is written and closed; the job runner's cancelled and failed
  snapshots are built from it (`CancelledSnapshot`, `FailedSnapshot`).
  `LocalModePanel` keeps the run's skip record apart from its current-input
  result, and both belong to the source *visit* the run started in: a return
  to a folder is a new visit, so a superseded run stays superseded (see
  Components).
- **XGP numbering** (§7.1, §7.5). Only committed files consume
  numbers. A run's terminal result carries what it committed, and Home's
  next-number control follows it unless the user changed the pattern or the
  next number since the run started; the numbering is three remembered
  settings, not a record (Hal's ruling, 2026-10-19, halheinrich/backgammon#424;
  see "A Local run's job" and "XGP export").

### A Local run's job, and the numbering it leaves

The umbrella's `../SPEC-notices.md` §7.5 is the model. Hal ruled on
2026-10-19 (halheinrich/backgammon#424) that reliable continuation of XGP
numbering is not a requirement: the recovery accounting goes, there is no
numbering document anywhere, and create-new with a ` (n)` suffix is what
protects files. Following's second ending, a status read answered 404, was
ratified on 2026-10-22. The recovery protocol of 2026-10-12
(halheinrich/backgammon#397) — a per-instance ledger, marks, owed jobs,
settling, reconciliation, the numbering record and its edit clock — stood
here until the Extract arc's increment 4 retired it; git history keeps it.
This section says where the code meets §7.5, not what it rules.

- **The job and its result** (`Services/JobStore`). Under one lock, the
  jobs — each under the identity the browser minted, kept until the page
  that started it acknowledges it or the process ends; a status read never
  removes one; a job's state is the store's private `HeldJob`, its runner
  holding an immutable `JobEntry` — and the request admitted under each
  identity, kept for the process's lifetime. A job's snapshot
  (`ProcessingProgress`) is immutable, so the store keeps the one reported,
  serves it to every status read and hands it to a terminal result's builder
  as it is; the first terminal result stands. `ProcessController` carries
  these over the wire (see Public API); a failed job's terminal result is
  recorded before it is logged, and a stopped run's close failure is handed
  to its terminal result.
- **The start is judged by its identity** (`JobStore.Start`). An admitted
  identity returns its admission and starts nothing, so a repeat never runs
  twice; the same identity with a different request is refused
  (`RefusalReason.ChangedRequest`) and changes nothing. Nothing else is
  judged: two identities both run, and two XGP exports into one folder write
  incremented names — the ruled cost.
- **The browser's wire** (`ILocalJobs`, implemented by `LocalJobsClient`):
  one typed call per endpoint; it retries nothing. Its cancel tells
  delivered, refused (404) and failed (any other answer, or none) apart. A
  service behind an interface, so a test can hold an answer at the point the
  page takes it.
- **The panel's run** (`LocalModePanel.RunLocalAsync`), past its gate
  (`RunReady`, the one definition the Run button and its click share;
  nothing about the server, since the start is the first contact): it mints
  the identity (`IJobIdentities`) and sends the start, re-sending a request
  that could not be made under the same identity every
  `LocalJobTiming.RetryDelay` — from the `FailuresBeforeError`th failure the
  persistent-failure run error shows, and the start goes on. It follows the
  job's status, hands an XGP job's commit to Home (`OnXgpJobEnded`), and
  only then acknowledges the job.
- **Following, and its two endings** (`FollowAsync`). A status read that
  fails to communicate is retried; a failure that persists is the run error,
  saying the result hasn't arrived yet, and following goes on. Following
  ends at the terminal result, or when the server answers 404 — it no longer
  holds the job, as after a restart: the run error says the result can't be
  shown, the panel leaves its busy state, and the files written before stay
  on disk under their names. It never ends quietly.
- **Stop** is the run's own. Once the job is known, Stop cancels it
  (`DeliverStopAsync`) and the run follows the job to its stopped result.
  Pressed while the start is unanswered, it is pending (`_stopPending`): the
  start goes on being sent, and once an answer admits the job the Stop is
  delivered before the job is followed; a refused start ran nothing. Nothing
  says it was delivered until it was, so a lost start reply is never shown
  as a confirmed cancellation. Each cancel's answer is its run's: the panel
  holds the job the run follows as a `JobInHand`, one per run and compared
  by reference, and applies an answer only while that job is in hand; once
  a cancel for it is confirmed delivered, no cancel's failure undoes that,
  even one for a request sent later. A cancel refused or not confirmed, with
  none delivered, is the run error, and following goes on. What the run
  error shows of a run in hand is made from two facts (`ShowInFlight`): a
  Stop failure standing — through a persistent failure and contact
  returning, until a Stop is delivered, the run ends or the user closes it
  — or else the persistent-failure notice while it is shown. That notice's
  state in the current failure streak (`StillTryingNotice`) is `None`,
  `Suppressed` (due, with a Stop failure in its place), `Shown` or
  `Dismissed`. It is due at every failure at or past the threshold; a Stop
  delivered over it shows it at once; a Stop failure the user closes over it
  leaves it due at the next failure; once closed it stays closed for the
  rest of its streak; and contact returning ends the streak.
- **The numbering, as remembered controls** (Home's). The pattern, the next
  number and the number length are three settings under their own keys,
  read at the restore and written when they change — by the user or by a
  run's commit — as the other settings are (see "Browser storage"). A key
  absent, refused or not a number leaves its control at its default.
  Nothing reads the keys of the protocol this replaced, and nothing removes
  them: there is no migration (§7.5). A pattern edit keeps the remembered
  next number while the pattern is the one it was remembered under — the
  restored pair, or the one the last commit moved it to — and otherwise
  resets it to 1; the user can override it.
- **The commit reaching the controls** (Home's `FollowAsync`). The terminal
  result's `XgpExportCommit` — completed, cancelled or failed alike —
  reaches Home before the job is acknowledged. A commit of nothing moves
  nothing (`CommittedNothing`); otherwise, while the pattern and the next
  number still equal the options the run captured, the next number becomes
  `NextNumber` and is remembered. "Not changed since the run started" is
  that value comparison, so a control changed away and back follows; the
  number length decides nothing. A reload during a run shows the last
  remembered values, and a run that ends after it contributes nothing to the
  screen (§7.2): its files are on disk under their names, and the next run
  into that folder writes incremented names, the ruled cost.

### The ranking: one run, one ranking

Which checker play is best — and so a play's error, the best play's depth
and after-board, and the order a diagram draws — is a ranking's
(`PlayRanking`; the umbrella's `SPEC-scoring.md` §2a, ruled on
halheinrich/backgammon#282). This app has no ranking setting, so it selects
the specified default, `Equity` ("Apps without the setting use the default"),
**once**: `AppRanking.Ranking` in `Client/Shared`. Every producer operation
whose meaning depends on a ranking is handed that same value, so one run's
rows, its filtering and its diagrams are interpreted under one ranking; no
producer's own default stands in for it (Hal, 2026-09-29, on
halheinrich/backgammon#273). What a ranking means is BgDataTypes_Lib's, and
nothing here restates it. The sites:

- **The processor** — the composition root (`Program.cs`) hands
  `AppRanking.Ranking` to `LocalFolderProcessor`'s constructor, where the
  ranking is a required argument (a defined value, or
  `ArgumentOutOfRangeException`): the processor states no ranking of its own.
  It builds each run's `FilteredDecisionIterator` for that ranking (with the
  loaded book), which builds every row for it and filters every record
  through its view for it; the deck draws each record through
  `DiagramRequest.ForDecision(record, _ranking)`.
- **The name preview** — its sample row is `DecisionRow.From(record,
  AppRanking.Ranking)`.

**What fails if a producer default stands in.** Of the producer operations
above, `ViewFor`, `DecisionRow.From`, `DiagramRequest.ForDecision` and the
`FilteredDecisionIterator` constructor require a ranking, so the compiler
holds them. `XgIteratorOptions`, which the filtering operation builds, does
not: its ranking defaults to `Equity`, which is also this app's choice, so a
run under `Equity` cannot tell the app's ranking from the default. The pins
therefore run under `DepthFirst`, over a fixture and filter
(`MatchTest.xg`, "erred by at least 0.001") that each test first proves, from
the producer's own read, admits different decisions under the two rankings:

- `LocalFolderProcessorRankingTests` — one test per pathway, each against the
  producer's reference for `DepthFirst`: each fails if the run's filtering
  operation is built for any ranking but the processor's.

### XGP export

Selecting the **XGP positions** output format emits the filtered decisions
as per-decision `.xgp` position files. `LocalFolderProcessor.ProcessXgpAsync`
writes the files server-side into the output **folder** (`OutputPath` is a
directory for this format, created if absent), each **created new**: an
existing file is never overwritten — the decision's file takes the next
free name, ` (2)`, ` (3)`, …, and the run carries on (`SPEC-notices.md`
§7.5; see "Reading, converting, skipping and committing"). Per decision,
it prepares the payload through one statement of the routing rule,
`XgpPayload.Prepare`, within the decision's own boundary (a failure skips
that decision and consumes no number), and commit outside it:

- `XgDecisionId` (from a `.xg` source) → `XgpExporter.ToBytes(xgFile, id)` —
  the producer's **slice** surface, analysis panes carried through,
  XG-SaveAs-equivalent, re-consumable by our own iterator.
- `XgpDecisionId` (from an `.xgp` source) → the source file's bytes
  **verbatim**, as read and retained (see Pitfalls).
- Anonymizing applies the producer's role preset to both (the slice's
  options-bearing overload; the `.xgp`'s whole-file anonymize-copy, which
  needs no bytes).

Entry naming is a user-editable **name pattern** — literal text plus
`{token}` placeholders (e.g. `Move{min-move}_{dice}_{score}` →
`Move5_31_3a5a.xgp`), carried as `XgpExportOptions.NamePattern` and rendered
by the naming engine in `Client/Shared`:

- `XgpNameTokens.All` — the token registry, the SSOT for which tokens exist
  (`{n}` counter, `{min-move}` from the active filters, `{dice}`, `{score}`).
  Validation, rendering, and the UI's insert-token dropdown all derive from
  it; adding a token is one added element. Tokens render against an
  `XgpNameContext` (primitives + lib types only — the engine is deliberately
  lib-ready). `{dice}` is the row's roll, two digits in rolled order; a cube
  decision has no roll (the row states none), and the token renders `00` for
  it — the token's established file-name spelling of that absence,
  presentation only, never a roll handed to any API (Hal, 2026-09-29,
  halheinrich/backgammon#273). `{score}` is the row's `MatchScore`: `3a5a`,
  `1a5aC` in a Crawford game, and for money `moneyJ` or `moneyNJ` by its
  Jacoby rule. The bare `money` a row with no stated rule once rendered is
  gone with the producer's change — every money session states its rule —
  and is taken as is, with no compatibility spelling (Hal, 2026-09-29).
- `XgpNamePreview.SampleRow` — the live preview's sample decision, a 3-1 at
  3-away/5-away in a 7-point match, game 2, move 5. A producer-built row: a
  record built through the producers' public surface and projected by
  `DecisionRow.From` for `AppRanking.Ranking`, so it holds every rule the
  producer enforces on a record and its row. It is not a position from a
  real game — those rules do not relate a board to its move number, and the
  sample puts the standard start at move 5. App-level, beside the engine
  rather than in it: it reads the app's ranking, and the engine stays
  lib-ready.
- `XgpNameTemplate.TryParse` — the pattern grammar (unmatched/empty braces,
  unknown tokens, filename-illegal literals are parse-time errors; braces
  are delimiters only, with no escape syntax). `Render` never fails; token
  output is sanitized (illegal/control chars → `_`). Empty token values
  render empty.
- `XgpNameAllocator` — stateful per-run name source created via
  `Create(options, filters)` (the single options-validation throw point).
  Appends `.xgp` and uniquifies duplicate rendered names Windows-style
  (`name.xgp`, `name (2).xgp`, …; case-insensitive bookkeeping). The
  processor uses the `Peek`/`Commit` split, committing a slot once its file
  is written; the name preview takes names with `Next`. `CommittedCount` is
  the numbers a run has used.

The default pattern `pos{n}` names files byte-identically to the pre-pattern
`{prefix}{number:D{suffixLength}}.xgp` rule; `StartNumber`/`SuffixLength`
govern `{n}`. `Home` owns the option state and its persistence: the
pattern, the next number and the number length are remembered settings
under `xg_xgpPattern`, `xg_xgpNextNumber` and `xg_xgpSuffixLength` (see "A
Local run's job"). A pattern edit keeps the remembered next number while the
pattern is the one it was remembered under, else resets it to 1. The retired
numbering record's key, and the last-number and prefix keys before it, are
neither read nor removed: Hal's ruling of 2026-10-19
(halheinrich/backgammon#424) has no migration, and a stale key costs nothing.

**The committed result** (`SPEC-notices.md` §7.1, §7.5). An export's
accounting is one `XgpExportCommit`: the options it captured when it started
and the files it committed (`NextNumber` derived). The
processor's `RunRecord` holds it from the moment the options validate and
replaces it as each file is written and closed; every snapshot carries it
(`ProcessingProgress.XgpCommit`), the job runner's cancelled and failed
results included. The panel hands the terminal result's commit to Home
(`OnXgpJobEnded`) for every ending, whatever it shows, before it
acknowledges the job, and Home's next-number control follows it
(`FollowAsync`) unless the user has changed the next number or the pattern
since the run started — compared by value with the options the run
captured. A length change is no such change: it says nothing about which
number comes next. A commit of nothing changes no number
(`XgpExportCommit.CommittedNothing`). The next number is stated once, by the
commit (`XgpExportCommit.NextNumber`).

### Components

- **`Home.razor`** — shell, the `FilterSurface` host, and the reporter of
  the filter source. Owns the output format, the XGP options and the
  folder, renders the output-format radio and the producer composite, and
  delegates to `LocalModePanel`.
  Its filter half is XgFilter_Razor's host contract, and nothing else (see
  "Filter setup"):
  - **It reports the source** to the app-scoped `FilterSetup` where it
    latches one: the restore resolving the folder, and a folder commit.
    `Source` is the one property
    reports and reads both use.
  - **It reads the filter in effect** from the owner, once, in
    `FilterInEffect` (`Source is { } s ? FilterSetup.Current.ConfigInEffectFor(s) : null`)
    — the one filter fact the panel is handed. Home keeps no copy of
    a config: there is no last-committed field, and the XGP preview reads
    the filter in effect (or the empty one).
  - **It re-renders off notifications**, not off component events: it
    attaches to `FilterSetup` (a commit, an edit, a restoration) and to
    BgUiPrimitives_Razor's `BrowserStorageCondition` (a refusal from anywhere) in
    `OnInitialized`, and disposes both attachments.
  - **The saved-filters seam.** One `HttpDocumentStorage` instance,
    constructed over a delegate reading the latched path (the composite
    rebuilds its store when the bound `Storage` *reference* changes, so the
    live folder rides the delegate). Bound while a folder is latched; null
    otherwise — a blank path renders no saved-filters section, never a load
    failure. It is the only parameter Home binds on `FilterSurface`.
  - **Source identity.** The source is the input folder path:
    `_folderPathText` follows every keystroke (and persists under
    `xg_folderPath`), while the separate `_localSourcePath` latches only at
    the input's `@onchange` boundary — and once at the restore, which is a
    committed value. The token is minted from the latch only (`FromPath`,
    handed the path in the spelling Home holds), never from the live text —
    a half-typed path is not a source. **Path identity is the token's, not
    this host's** (halheinrich/backgammon#94): `FromPath` folds case and
    makes trailing separators insignificant, so Home pre-folds nothing and
    the latched path keeps the user's spelling for IO and display. The
    output path is **not** a source (umbrella-ratified).
  - **The panel waits for the restore.** No panel renders until the restore
    has resolved the source: the panel is where a folder is committed, and
    a commit made before the restored folder had arrived would be a source
    the page could not yet place.
  - **Its own settings** — the output format, the folder path, the
    anonymize flag, and the XGP pattern, next number and number length —
    go through `ReportingBrowserStorage`, each call with its own consequence
    phrase (see "Browser storage"). The restore runs to its end
    whatever storage answers: each key lands on its stored value or its own
    documented default.
  - **Its next-number control follows a run's commit** (`FollowAsync`,
    bound to the panel's `OnXgpJobEnded`): to the number after the last the
    run used, unless the user changed the pattern or the next number since
    the run started (see "A Local run's job"). Until Hal's ruling of
    2026-10-19 (halheinrich/backgammon#424) Home kept a numbering record and
    reconciled it with the server, with two gate reasons of its own; both
    went with it.
  - **The storage condition's notice**, `#storageUnavailableNotice`, rendered
    from the shared `BrowserStorageCondition` (see "Notices").
- **`LocalModePanel.razor`** — folder/output-path inputs, Run/Stop/Exit
  buttons, the run's start and following, progress bar (determinate, plus
  the two indeterminate states in "Busy affordance"). Parameters:
  `OutputFormat`, `Source` (the token Home reports), `FilterInEffect` (the
  filter in effect for it, or null — the whole filter half of the Run gate,
  and the config the run sends), the folder input's controlled-component
  trio (`FolderPath`, `FolderPathChanged` per keystroke,
  `OnFolderPathCommitted` at the change boundary — Home owns the value; all
  three `[EditorRequired]`, they are the source report's wiring), the XGP
  members, and `OnXgpJobEnded` (`[EditorRequired]`, an
  `EventCallback<XgpExportCommit>`: a terminal result's commit, raised before
  the job is acknowledged — see "A Local run's job"). The recovery
  protocol's other four parameters, and the start hint they fed, went with
  it (halheinrich/backgammon#424): the panel checks nothing before a start.
  The Run button and its click share one gate (`RunReady`). A stopped run's close
  failure shows beside its stopped line (`#closeFailure`). The output path
  stays panel-owned, under `xg_outputPath`.
  **A result describes the inputs it was computed from.** A run records
  them when it starts (`RunInputs`: the **source visit** it started in, the
  filter's values, the output path, and whether that filter restricted
  nothing), and the result on screen — progress, Done line, zero-match box,
  run error — is dropped as soon as the visit or the filter values differ
  from its run's (`OnParametersSet`; the filter by value: the host hands a
  new config on every render). A visit is the stretch over which the panel
  shows one source: replacing or removing the source ends it, and a return
  to a folder shown before is a new one, compared by reference — so after
  A→B→A the old run stays superseded (halheinrich/backgammon#398). Not while
  busy: a running job's progress and Stop stay with their own job. A run's
  outcome — its terminal snapshot or its failure — is **judged before it is
  published**: following renders only non-terminal progress, and when the
  run ends its outcome is shown only if the run still describes the inputs
  on screen. So a run that completes after its inputs were superseded is
  never shown as the new inputs' result, not even while the XGP export's
  bookkeeping (Home's write of the next number) is pending. That bookkeeping
  is the job's own: Home is handed the commit for every ending — completed,
  cancelled or failed — whatever is on screen (see "XGP export"). Watching "a
  filter is in effect" fall is not enough, and was what this replaced: the
  empty selection stays in effect across a folder change, so nothing falls
  while the folder the result was computed from has gone. The Done line
  names what the run wrote, as its completed snapshot carried it
  (`ProcessingProgress.WrittenPath`, §7.5): the one file as it was created,
  numbered where it had to be, or an XGP run's folder — never the requested
  path, and the panel neither rebuilds a numbered name nor looks for the
  file. The zero-match box asks the run's own filter, never the one on
  screen now.
  **The run's skip record lives by other rules** (`SPEC-notices.md` §7.2,
  ruled "Option B"), so it is its own state, `_lastRun`: the record the run's
  terminal snapshot carried, the source visit the run ran in, and how the
  run ended. It is published when the run ends while that visit is still the
  current one, whatever the filter now is; a filter change keeps it, an
  unapplied edit included; replacing or removing the source clears it, and a
  run from a former visit publishes none; the next run's start clears it,
  and mid-run it stays hidden while the record is still growing. It renders
  through `SkipListNotice` below the result, labelled with its run — "The last
  run", "The last run (stopped)", "The last run (failed)" — so once "Done" has
  gone it still reads as that run's history (halheinrich/backgammon#223 for
  the list itself). A file is shown by its path under the input folder, and a
  decision adds its canonical `DecisionId`. The zero-match notice **yields**
  to a non-empty record of the same run. That notice concludes "the filter
  matched nothing" from zero rows through an active filter, and the
  conclusion holds only if every file was read, so it renders only when the
  terminal's record is empty. Suppressing it, rather than rewording it to
  mention the skips, keeps two independent notices, each with one condition,
  instead of adding a third combined state.

The Run gate's filter term is `FilterInEffect is not null`, and nothing
else filter-shaped — one fact, one owner. The panel adds its own gates
beside it (a folder and an output path; valid XGP options); those stay the
panel's. A source change (a new folder commit) ends the setup: an applied selection is
dropped and needs Apply again against the new source, while the empty
selection stays in effect without one.

### Filter setup

XgFilter_Razor's `FilterSetup` is the app-scoped owner of everything
`../SPEC-filtering.md` governs here — the draft, the applied baseline, this
boot's restoration and its two notices, and whether a filter is in effect —
and this host adopted it in v1.12.2 (halheinrich/backgammon#374, Arc 2). Its
"Host contract" is this host's contract; what follows is how Extract meets
it, not a restatement.

- **Registration.** `AddClientServices` calls the parameterless
  `AddFilterSurface()`, which registers, scoped, BgUiPrimitives_Razor's
  `BrowserStorageCondition`, its `ReportingBrowserStorage` and the
  `BrowserStorage` under it, XgFilter_Razor's adapter over the condition as
  the sink the surface reports its refusals to, and the surface itself — so
  the condition Home injects is the one every refusal reaches. Until
  halheinrich/backgammon#424 (Hal's ruling, 2026-10-19: common code has one
  shared owner) this app registered its own condition as the sink with
  `AddFilterSurface<BrowserStorageCondition>()`. Only the WebAssembly client renders the
  surface or injects the owner: Home renders `InteractiveWebAssembly` with
  `prerender: false`, and the server renders none of either.
- **The source is reported, never bound, and never guessed.** Home reports
  at the moments listed under Components. `ReportSource(null)` is a real
  answer — it ends an existing setup — so "not yet known" is never reported
  as null: until the restore has resolved the folder
  (`_sourceResolved`), Home reports nothing and renders no panel. A
  return over the same folder therefore reports the same token once, which
  ends nothing: the generation and the baseline stand, and Run opens over
  the applied filter without a re-Apply. There is no mount gate around the
  surface, no copy-back of a resumed selection and no clear at a pick:
  reporting the source is the whole of it.
- **A disposed page reports nothing.** Home's async work is tied to its
  lifetime — one `CancellationTokenSource`, cancelled in `Dispose()`. The
  app-mode request is handed its token, so a navigation away cancels it;
  each restore read completes (a refusal still reaches the condition) and
  the restore stops after it; and `ReportSource()` publishes only while the
  lifetime is live. Without it, a page disposed while its mode response was
  pending would report its folder over a newer page's setup — advancing the
  generation and dropping that setup's baseline.
- **Readiness** (halheinrich/backgammon#266). With a source, a valid empty
  selection is in effect without Apply. Extract adds no empty-filter
  exception of its own, and no copy says Apply is needed for it: Local's
  "Apply filters first" shows only while a source exists and nothing is in
  effect for it — with no folder, the blank folder box is why Run is dark.
- **Derived facts are keyed by their inputs.** The run's result by its
  run's source and filter values, and its run record by its run's source.
  Nothing compares configs by reference.

### Browser storage

Every storage call this app makes — Home's keys, the panel's, and the filter
surface's own — goes through BgUiPrimitives_Razor's `BrowserStorage` (Hal's
ruling on halheinrich/backgammon#374; the library's "Browser storage
access"). No Extract code calls storage interop directly, and nothing
latches: a refusal disables nothing, and the next call is simply made.

Both types below are BgUiPrimitives_Razor's ("Browser storage access"), not
this app's: common code has one shared owner (Hal's ruling,
halheinrich/backgammon#424, 2026-10-19), and this app's own copies of the
access and the condition were retired for them. Registration is
`AddFilterSurface()` (see "Filter setup").

- **`ReportingBrowserStorage`** is this app's access to its own keys, each
  call naming `BrowserStorageArea.Local`, its key, and its **consequence
  phrase** — what a refusal costs that caller, in a few words ("the setting
  takes its default for this visit", "the output path holds for this visit
  only"). A refusal is reported to the condition first, then logged once,
  in the library's one template carrying the operation, the key and that
  phrase; a logger that throws is contained, so the call still returns its
  result. A read returns the accessor's result: most callers read
  `read.IsStored ? read.Value : null` and keep their own parsing and
  default; a refused write returns normally, leaving the caller's in-memory
  value standing. Anything that is not the browser's refusal propagates.
- **`BrowserStorageCondition`** is the one storage condition, app-scoped,
  with no logger of its own. Every refusal — this app's keys' through the
  access, and the filter surface's through XgFilter_Razor's adapter, also
  after the page that started the call has gone — is one occurrence per
  visit: the first begins it,
  later ones are the same occurrence, and a later success begins and ends
  nothing (a read served says nothing about writes; one key's success says
  nothing about another's). It records the occurrence and the notice's
  dismissal, never permission to stop calling. Home attaches to it, so a
  refusal from a panel or the surface shows on Home without a gesture of
  Home's, and a refusal while Home was absent is there on its return. A
  full reload constructs a fresh holder.
- **A refused write never stops the gesture that made it.** At v1.12.1 the
  panels' unguarded reads and writes could throw out of a gesture or a first
  render (halheinrich/backgammon#102, Extract's portion, closed here). They
  now go through `ReportingBrowserStorage`.

The keys: Home's `xg_outputFormat`, `xg_folderPath`, `xg_xgpAnonymize`,
`xg_xgpPattern`, `xg_xgpNextNumber` and `xg_xgpSuffixLength` (the XGP
numbering as three remembered controls, `SPEC-notices.md` §7.5; the two
numbers in invariant digits); and `LocalModePanel`'s `xg_outputPath`. The
filter surface's keys are its own and are not named here. The numbering
record's document, with its migration and its four-outcome read, went with
the recovery protocol (halheinrich/backgammon#424, 2026-10-19): nothing
reads or removes it, or the older keys it had replaced. No caller
here asks a write's result (`written.Succeeded`) or tells a refused read
from nothing stored.

### Notices

The umbrella's `../SPEC-notices.md` is the ratified model
(halheinrich/backgammon#248); BgUiPrimitives_Razor's `Notice` is how a box
presents. This host decides only what each box **is**, because that — never
its colour — decides whether it dismisses, and, for a dismissible box, **who
holds the dismissal**. No `.razor` file here types alert markup.

| Box | What it is | Dismissible | Announced |
| --- | --- | --- | --- |
| `#storageUnavailableNotice` (Home) | condition notice | yes, per occurrence | assertive |
| the run error (the panel) | error | yes | assertive |
| `zero-match-notice` (the panel) | gate reason — the run's result | no | assertive |
| `skipped-notice` (the panel, `SkipListNotice`) | gate reason — the record of what was lost: the last run's | no — pinned after the caller's attributes | assertive |

Home's two gate reasons of the recovery protocol, the lost-export and the
unreadable-numbering notices, retired with it (Hal's ruling, 2026-10-19,
halheinrich/backgammon#424): nothing blocks an XGP export any more.

**A dismissal belongs to one occurrence, lives as long as it, is never
stored, and has one holder — the occurrence's owner**, because only the owner
knows when a new occurrence begins and whether the old one outlives the page.
Every dismissible box here is caller-held (`@bind-Dismissed:get/set`), so the
component keeps no bit that could disagree with the owner's:

- **The run error — the panel's `_runError`.** One occurrence is one failed
  gesture: a start the server refused (its identity used for a different
  run), a job the server no longer holds, a Stop the server refused or never
  answered (following goes on), or — while it lasts — a start or a status
  read that keeps failing, which says the result hasn't arrived yet
  and comes down when contact returns while following goes on (a file that
  cannot be read is a skip, not a run error).
  `_runError` (nullable) is
  non-null exactly while one stands, so it is the box's presence, its text
  and its dismissal at once: closing the box assigns null, exactly as the next
  gesture and inputs that no longer match the run's already do, and
  **there is no dismissed bit anywhere**. A second failure assigns the field again and shows
  fresh although its text may be identical — the text was never the
  occurrence. It replaced a `_hasError` flag beside a `_statusMessage` string:
  two fields for one fact, to which a dismissal would have added a third.
  The occurrence dies with the panel, so the panel is the right owner; it is
  bound rather than left component-held with an `OccurrenceKey` because a key
  would need a run counter — a new fact invented to name one the panel
  already holds. The panel's `_progress.ErrorMessage` is a different fact
  (the server's report of a job that started and then failed) and is not a
  box.
- **Storage trouble — the app-scoped `BrowserStorageCondition`**
  (BgUiPrimitives_Razor's since halheinrich/backgammon#424). One
  occurrence per visit: it begins at the first refusal from any reporter —
  Home's settings, the panel's, or the filter surface's — and every later
  refusal, and every later success, is the same occurrence (see "Browser
  storage"). That outlives the page, so neither bit may be a page field. The
  condition (registered Scoped — one per loaded app under WebAssembly)
  holds the occurrence and `IsNoticeDismissed` together: a navigate-back
  remount shows a dismissed notice no more, a refusal made while Home was
  absent shows on its return, and a reload constructs a fresh holder, so
  everything returns. It never stops a storage call; it records that one
  was refused. `DismissNotice()` throws before any refusal — a dismissal
  recorded before its occurrence would hide it at birth. Dismissing hides
  nothing the screen needs (every control shows the value it holds, restored
  or not), which is what makes it a condition and not a gate reason. The
  wording is Hal's ruling (halheinrich/backgammon#374): "This page had
  trouble using your browser's storage. You can keep using it, but some
  settings may not be restored or saved." It replaced a claim that settings
  start from their defaults and changes will not be saved, untrue once reads
  can succeed while writes are refused. Before this the occurrence, a latch
  and the dismissal lived on Extract's own `BrowserStorage` seam, which
  stopped calling after the first refusal; the seam is retired, and the
  latch with it.

**The run errors carried no role at all** before the adoption — a failed
gesture nobody announced. They are assertive, per the model.

### Busy affordance

Measured on a 266-file / 14.4 MB corpus (issue halheinrich/backgammon#53).
Apply Filter is 30–71 ms and deliberately gets nothing.

The panel's progress bar already covers the CSV, Diagram JSON and Xgp
pathways end to end (2 s runs, reporting throughout). Two states carry no
fraction and get an indeterminate striped bar instead:

- **Before the first status read** (`_busy && _progress is null`) — following
  waits a poll interval (`LocalJobTiming.PollInterval`, a second) before its
  first read, which alone guarantees a second of it.
- **The deck pathway's atomic render** (`Phase == JobPhase.Rendering`) — 144 s
  for PPTX and 405 s for PDF at corpus scale, during which the determinate bar
  would sit solid at 100% beside an elapsed figure frozen near 1 s. The
  branch also suppresses those stale elapsed/throughput numbers.

**The cursor** is the half of the affordance the words can't cover, and the one
the user found missing (issue halheinrich/backgammon#77): through that
multi-minute deck render the bar and the phase message were right and the
pointer stayed a plain arrow. The panel puts an `is-busy` class on its
**root** element for exactly as long as its own busy state, `_busy`, is up,
and `wwwroot/app.css` turns that one class into
`cursor: progress` for the root and everything under it. Two details are
load-bearing:

- **It rides the flag, not a window.** The pre-first-poll gap, the
  determinate stretch and the atomic render are all inside one `_busy` — so
  all of them are covered by one rule, and a busy window added later is covered without a second one.
- **The rule's descendant half and its `!important` are not defensive.**
  Bootstrap dresses the pointer over exactly the controls the wait started from
  (`button:not(:disabled)`, `.form-control[type=file]:not(:disabled)…`), so
  inheritance from the root reaches nothing that matters — and those selectors
  outrank anything a single state class can raise its specificity to (0,1,1 and
  0,4,0 against 0,1,0). Winning over every component's own cursor, everywhere,
  for as long as the state holds, is what `!important` is for; a specificity
  arms race would be the worse answer here, not the purer one.

`progress` rather than `wait`: it is the keyword for "working, but the
interface still responds", which a run is exactly (Stop and Exit stay live).
`BusyCursorTests` pins the state→class binding.

### The page and its run

- Folder path + output path inputs. Output format: CSV, Diagram JSON, PPTX,
  or PDF; persisted to `localStorage` under key `xg_outputFormat`.
- Run → the page mints the job's identity → `POST /api/process/start`
  (a `StartRequest`: the identity and the run). The client reads
  `GET /api/process/{jobId}/status` every second until the terminal result,
  or a 404, then acknowledges a terminal result (see "A Local run's job" and
  "Job lifecycle").
- `ProcessController` dispatches via a switch on `ProcessRequest.OutputFormat`
  with `ProcessAsync` (CSV) as the default branch — unknown / future enum
  values fall through to CSV. Cases:
  `ProcessDiagramAsync` (Diagram JSON), `ProcessPptxAsync` (PPTX),
  `ProcessPdfAsync` (PDF), `ProcessXgpAsync` (XGP; also takes
  `request.XgpOptions`). The PPTX and PDF public methods are one-line
  wrappers around a shared private `ProcessDeckAsync` helper parameterized
  on the renderer delegate — both collect filtered decisions, map them via
  `DiagramRequest.ForDecision(record, ranking)` for the run's ranking, and
  expand into Problem/Solution pairs
  rendered via `DiagramRasterRenderer.RenderPptx` or `RenderPdf`.
- Stop → `POST /api/process/{jobId}/cancel` once the start is answered;
  before that, Stop waits for the answer and is delivered once it admits
  the job (see "A Local run's job"). Exit → `ShutdownController`.

### Job lifecycle (Local mode)

`JobStore` is a singleton, one per server process, holding under one lock
the jobs and the starts admitted (see "A Local run's job"; the protocol is
`SPEC-notices.md` §7.5). The runner holds a `JobEntry`, an immutable handle:
its identity (the browser's) and the token its work runs under. The job's
`ProcessingProgress` snapshot —
final once terminal, and immutable, so the store keeps and serves the one
reported as it is — and its `CancellationTokenSource` are the store's
private `HeldJob`, changed only under its lock. The client reads the status
once per second (the processor reports every tenth file, on every pathway).

**A status read never removes a job** (it once did, serving the terminal
snapshot exactly once, so one lost response lost the result). The job and
its result are kept until the page that started it acknowledges it
(`POST /acknowledge`, which releases the result and disposes the source, and
succeeds for a job the store no longer holds), or the process ends. The
runner records each snapshot through the store — `Report` while the run
goes, `Complete` for its terminal result (a cancelled result is built from
the last report in the same step); the first terminal result stands. A
failed job's terminal result is recorded before the failure is logged, so a
logger that throws cannot leave the job running. A `POST /cancel` for a job
that has ended or is not held no-ops (404), and the client says Stop was
refused. The controller's actions are thin pass-throughs, so the lifecycle
stays inside `JobStore`. A job whose page never acknowledges it — a reload,
a lost acknowledgement — is kept for the process's lifetime, as §7.5 rules;
a restart forgets every job, and a page still following one gets a 404,
which ends its following.

**Skips are recorded; a destination failure and a cancellation never are**
(halheinrich/backgammon#223, `SPEC-notices.md` §7.1). One bad file must not
kill a batch: a failed read (the read's outcome), a failed conversion (the
file's staged contribution) and, on the Xgp pathway, a decision whose payload
cannot be prepared are each skipped, and the run goes on. Every one of them
goes through one seam, `LocalFolderProcessor.RecordSkip`, and nothing else
logs a skip: the seam appends a `SkippedItem` (the file's path relative to
the input folder; the decision's id for a decision skip; the exception's
message as the reason) to the run's `RunRecord`, and then logs it, a
logger that fails contained so the batch goes on. Every snapshot
the run reports, progress and terminal alike, carries the record's skips as
`ProcessingProgress.Skipped`. A commit to the destination is outside every
skip boundary and fails the run. A cancellation propagates out of the read,
which no catch encloses, and every skip catch excludes
`OperationCanceledException`, so it cancels the run on all four pathways
rather than being recorded as a skipped file.

The record belongs to the run, so it reaches every terminal snapshot, the two
the job runner builds as well as the one the processor reports. The runner in
`ProcessController` creates one `RunRecord` per job and passes it to the
processor, and its cancel and error catches set the snapshot the record
builds (`CancelledSnapshot`, `FailedSnapshot` — new snapshots, swapped in
whole): its skips and, for XGP, its commit. They cannot take them from the
last reported snapshot: a run-ending exception reports nothing, and the
pathways report only every tenth file. The processor rejects a record that
already holds anything, since one run must not report another's. A failed
run therefore shows its error and its skips together, and accounts for every
file it wrote.

The runner's progress sink applies each report inline (`InlineProgress`),
not through `Progress<T>`. `Progress<T>` posts every report to the thread
pool, so a report posted just before a failure could be applied after the
catch had set the terminal snapshot. The store now refuses a report after a
terminal result, but the order is the run's, and the sink keeps it.

**Names differ on purpose.** The record names a file by its path relative to
the input folder, because discovery is recursive and two subfolders can hold
files of the same name, and the record is the only place a skipped file is
named. Rows, decision ids and the skip log line keep the bare filename, the
producer's convention (`DecisionId.Filename` holds no directory).

### Opening-book enrichment

`.xg` files stamp opening-book–analysed candidates with a bare 998/999 "book"
level that carries no rollout parameters. Supplying XG's opening-book database
(`OpeningBookV2.ob`) lets the iterator recover the cached rollout XG used:
book-analysed decisions then report an enriched `AnalysisDepth`
(`Book V2: 12960 trials. 4-ply`) and a real `AnalysisLevel` instead of the
degraded `BookRollout` + `Unknown` pair. The producer owns the enrichment; this
app only *locates* the book and hands the loaded instance on — to each
run's `FilteredDecisionIterator` (the book alone). Enrichment is strictly additive — it never
changes which decisions or candidates are emitted, only their depth labels and
levels — so a missing book degrades cleanly and never fails a run.

`OpeningBookProvider` (singleton) resolves the path and loads the book once (~13.6 MB, immutable, concurrent-read
safe). Resolution: the `OpeningBookPath` config key when set to a non-empty
path; **absent** → auto-detect the default install
(`C:\Program Files (x86)\eXtreme Gammon 2\OpeningBookV2.ob`); **empty** (`""`)
→ disabled. `ResolvePath` (the three-branch decision) is pure and separately
tested; loading is the IO half. The provider exposes the loaded `Book`
(null when none) and not iterator options: the options also carry the run's
ranking, which is the application's, not the provider's.
`LocalFolderProcessor` holds the book and builds each run's
`FilteredDecisionIterator` with it and the run's ranking, on all four
pathways (CSV, Diagram JSON, Xgp, deck). A null provider, a disabled key, a
missing file, or an unreadable book all resolve to the same null-book /
unenriched path.
`GET /api/openingbook/status` surfaces the load state so `LocalModePanel` can
show a status line by the folder input.

### Filter pipeline

`FilterConfig.Build()` (lib-owned, in `XgFilter_Lib.Filtering`) materializes
a `DecisionFilterSet` from the serializable DTO. The server calls
`request.Filters.Build()` in `ProcessController.Start`, and the four
pathways filter each decoded file through XgFilter_Lib's filtering
operation (`FilteredDecisionIterator.IterateFile` / `IterateFileDiagrams`,
the set's early-exit hints wired) — that library's rules, through
`IDecisionFilterData`: one filter implementation, every output.
A row is its own view, built for the run's ranking; a record is not a view,
and is filtered through `record.ViewFor(ranking)` for that same ranking (see
"The ranking").

The analysis-depth facet is three symmetric per-mode pairs (XgFilter_Lib
cbca4b3): `IncludeEvaluations`/`EvaluationLevels`,
`IncludeRollouts`/`RolloutLevels`, `IncludeBookRollouts`/`BookRolloutLevels`.
Build() unions one clause per enabled toggle, each qualified by its own level
list (empty = any level), and a level list whose toggle is off is **inert** —
it neither activates the facet nor constrains anything. Consumers here supply
the six members verbatim and never re-derive the clauses; `Build()` is the
derivation SSOT. The wire carries all six, pinned by
`LocalModePanelFilterWireTests`.

### CSV output

Eleven columns, in order:
`Xgid,Error,MatchScore,MatchLength,Player,SourceFile,Game,MoveNumber,Roll,AnalysisDepth,Equity`,
stated once in `CsvLayout` (one column table, from which the header and every line are
derived) and pinned by `CsvLayoutTests`. A cube decision's `Roll` is `0`;
every other absent fact is an empty cell. Hal ruled on 2026-10-19/20
(halheinrich/backgammon#424) that the CSV carries no `Kind`, `Ranking` or
`Result` column — an error here is the decision taken in the source game,
under the app's one ranking — and that a cube decision is identified by a
roll of 0. The layout is Extract's, not the shared `DecisionRow`'s, so
another app's need never reaches this file; the row keeps its null roll.

### Diagram JSON output

Emitted as a single in-memory JSON array, not NDJSON. Simpler consumer side;
streaming is deferred (see next steps) and will matter only for very large
corpora.

### Deck output (PPTX/PDF)

Filtered `BgDecisionData` is buffered in memory, mapped to
`DiagramRequest` via `DiagramRequest.ForDecision(record, ranking)` — the
ranking each was just filtered under, so the diagram orders, numbers and
marks the candidates by the run's one ranking — then expanded to a
Problem/Solution pair via `DiagramRequestExtensions.ToProblemSolutionPair`.
The pairs are flattened into a single deck (two slides/pages per decision —
problem first, then solution) via `DiagramRasterRenderer.RenderPptx` or
`RenderPdf`. Rendering defaults to `new DiagramOptions()` — default theme,
16:9 aspect, no pip count. Rendering is atomic (the lib returns `byte[]`);
cancel works during the per-file collect loop, not during rendering.

`ProcessPptxAsync` and `ProcessPdfAsync` are thin wrappers around a private
`ProcessDeckAsync` helper that takes a
`Func<IEnumerable<DiagramRequest>, DiagramOptions, byte[]>` renderer and a
format label used only in progress messages. The two public methods differ
only in which renderer they pass.

### Test project

xUnit, targets .NET 10. Fixture files live in the umbrella
`TestData/FixtureFiles/*.xg` directory and are referenced from the test
project via relative path — not duplicated here.

**The Local golden baseline runs only on purpose.** `LocalGoldenBaseline`
(category `Golden`) runs every `LocalFolderProcessor` pathway over the frozen
`TestData/Golden/input/` (a one-time copy of FixtureFiles' top-level `.xg` and
`.xgp`) into a new `TestData/Golden/<label>/`, with a `manifest.json` of each
output's size and SHA-256, so a change can be compared byte for byte against
an earlier label (halheinrich/backgammon#424). The suite is plain
`dotnet test`, where the baseline, its label unset, writes nothing and passes;
a baseline is `EXTRACT_GOLDEN_LABEL=<sha>-a dotnet test --filter
"Category=Golden"`, the label set per command. A label must name a folder
that does not exist yet; the harness never deletes. CSV, Diagram JSON and XGP
repeat byte for byte; PPTX and PDF differ by their timestamps alone, so
compare PPTX by its entries' hashes and slide count, and PDF by its
date-masked hash and page count.

**bUnit tests register what the app registers** — `AddClientServices`, the
Client's one registration, beside a stub `HttpClient` — so a component renders
over the app's own lifetimes. **Browser storage is planned where it is the
subject, and incidental elsewhere** (XgFilter_Razor's posture): a test whose
subject is storage, a restored value or the storage condition puts a
`BrowserStoragePlan` on its runtime, declares every call — this app's keys as
literals (`HomeStoragePlan` for Home's), the filter surface's through
`FilterSurfaceStorage` — and ends with `Verify`. A test where storage is
incidental runs `JSRuntimeMode.Loose`, whose answers reach the real
`BrowserStorage` as "nothing stored" and "landed". No test spells a storage
interop identifier or mirrors a producer storage key. A Local result is
reached by running a job against a stub server (`LocalPanelRuns`,
`CapturingProcessHandler`), never by seeding the progress it shows: the panel
records what a run was computed from when the run starts.

- `Make20PtSmokeTests` — wire test for the Make20Pt filter category.
  Exercises `FilterConfig.Build` + `FilteredDecisionIterator` under the
  app's ranking, against the real fixture
  `.xg` files; the reference set is computed directly from the lib's
  XOR semantics so adding/removing fixtures shifts both sides together.
- `OutputConsistencyTests` — verifies that the CSV pathway and the Diagram
  JSON pathway see the same decisions through the same filter set, under the
  app's ranking: each row the converter builds is built for
  `AppRanking.Ranking`, each record is read through its view for it, and
  every view member the two share agrees.
  Uses `IDecisionFilterData` explicitly (CA1859 suppressed — the interface
  contract is what's being tested).
- `LocalFolderProcessorPptxTests` — wiring test for the Local-mode PPTX
  pathway. Runs the processor against the fixture folder, asserts the
  written file is a valid OOXML zip with at least one slide, and that no
  fixture file was skipped — a non-zero row count alone passes with files
  dropped. Deck-level conformance is owned by `BackgammonDiagram_Lib`'s
  `PptxConformanceTests`.
- `LocalFolderProcessorPdfTests` — wiring test for the Local-mode PDF
  pathway. Runs the processor against the fixture folder, asserts the
  written file begins with the `%PDF-` magic bytes and that no fixture file
  was skipped. Document-level conformance is owned by
  `BackgammonDiagram_Lib`'s own tests.
- `LocalFolderProcessorIllegalPlayTests` — the consumer half of the
  illegal-play log contract: a real tournament file carrying XG's
  illegal-play marker renders a PDF with nothing skipped, and the illegal
  play surfaces as a contextual warning through the filtering operation's
  logger, which the processor hands the run's `FilteredDecisionIterator` and
  it forwards to the producer.
- `LocalFolderProcessorSkipTests` — the skip contract
  (halheinrich/backgammon#223) on all four pathways, twice each. *Skips are
  recorded*: a temp folder holds one fixture and one malformed file the test
  synthesizes (`MalformedXg`: garbage bytes under an `.xg` name, never
  committed, placed in a subfolder that sorts first so the good file comes
  after the skip); the run completes, the good file's rows are all there
  (checked against the producer read directly), and the bad file is recorded
  once, by its path under the input folder, with a non-empty reason. Two
  same-named files in different subfolders are recorded by their distinct
  relative paths while the log keeps the bare name; a caller's `RunRecord`
  holds exactly what the snapshots carried and is refused for a second run.
  *Cancellation is not a skip*: a one-file run cancelled from its
  first progress report throws `OperationCanceledException`, its last
  snapshot records no skip, and nothing logged the cancellation as a skip.
  The log half is what catches the Diagram JSON pathway: there, the old
  catches' swallow was re-thrown by the final write before any snapshot could
  show it. Uses a synchronous `IProgress<T>`, because `Progress<T>` posts its
  callback and would race the cancellation point.
- `LocalFolderProcessorBoundaryTests` — Local mode's boundaries
  (`SPEC-notices.md` §7.1): a file failing midway — a filter that throws on a
  chosen decision, which the filtering operation propagates — contributes no
  rows (CSV), records (Diagram JSON) or positions (XGP) and is a conversion
  skip; an unwritable destination fails the run with no skip (CSV, Diagram
  JSON, and an XGP write after a successful one, whose file and commit
  survive); a failed XGP payload preparation, through the processor's
  internal preparation seam, is a decision skip that consumes no number; and
  a cancellation records no skip and keeps the files the run committed, its
  XGP commit counting every file written; and the CSV file is closed, whole,
  when success is reported, while a failure flushing it — through the
  destination seam (`ScriptedDestination`) — is the run's error, with no
  success reported first and no skip; a cancellation whose close fails is
  still a stopped run, the close failure beside it, and a failed Diagram
  JSON write whose close fails keeps the write's error, the close failure
  beside it; and a logger that throws stops no run at any kind of skip, the
  record whole and the later files written.
- `LocalFolderProcessorNoOverwriteTests` — Local output files are never
  overwritten (`SPEC-notices.md` §7.5): an existing file is left as it was
  and the run's file takes the next free name and the run carries on; a
  failure to create the file — a folder of its name — asks no removal and
  leaves the folder's contents as they were; a write that fails partway
  and a failed close each remove the run's unfinished file, the files before
  it standing; a close or a removal that also fails is reported beside the
  write's failure, never in its place; and the destination's own
  classification of a create refusal decides a collision — the file
  system's included — whatever the disk holds.
- `ProcessingProgressWireTests` / `SkipRecordTests` — the skip record: its
  wire form under `JsonSerializerDefaults.Web` (the array of its entries,
  `DecisionId` in its canonical form, a body without the field reading as the
  empty record), and its own contract (immutability, the derived counts,
  equality, the copy taken of its input, null entries refused); and the
  snapshot itself has no property that can be set once it is made.
- `RunRecordTests` / `XgpExportCommitTests` — the job runner's cancelled and
  failed snapshots take the skips and the commit from the record over a
  trailing report; the commit's validation, `NextNumber`, equality and wire
  form.
- `ProcessEndpointTests` — hosted pins (`WebApplicationFactory`, Local
  config) for the terminal snapshot the job runner builds itself when a run
  fails, which nothing below the controller can observe. A Diagram JSON run
  that skips a file and then fails on an unwritable output path, and a deck
  run whose every file is skipped (nothing to render), both end with the
  error and the skip together; an XGP run whose second write fails ends with
  the error, no skip, and the commit of the one file written. Each starts
  under its own identity and reads the real status endpoint as the panel
  does.
- `JobStoreTests` — the job store directly (§7.5): a status read never
  removes a job and the acknowledgement releases only its result; the first
  terminal result stands; a start is judged by its identity — a repeat
  answers its admission and starts nothing, after the acknowledgement too,
  and a changed request is refused, in both an XGP and a CSV case, a
  conflicting retry's refusal leaving the admission; two simultaneous starts
  under different identities are both admitted (the ruled cost); the
  snapshot reported is served as reported and is what a terminal result's
  builder receives.
- `LocalJobEndpointTests` — the server's side of the protocol through the
  real wire (`LocalServer`, `ProtocolWire`): a lost start response's retry
  finds the same job and no second runs; a duplicate after the
  acknowledgement is answered as admitted and runs nothing; a changed
  request is refused; a status read removes nothing; two simultaneous XGP
  starts both run into one folder with no file overwritten; a collision's
  run error names the file; a malformed identity (a trailing newline
  included) is a bad request; a cancel ends the job; and a failed job ends
  even when logging the failure throws.
- `LocalStopTests` — truthful Stop and following under failure (§7.5),
  through real pages (`LocalPage`: Home with the real panel over the real
  server and shared in-memory browser storage): a lost start reply re-sent
  under its identity and run once; a status read failing once (the result
  still delivered) and persistently (the run error, following on); a 404
  after a restart (following ends with the run error, the panel leaves
  busy, the files stay); Stop before the start is answered — the answer
  held, or a lost reply's retry held — delivering no cancel and showing
  nothing until the job is admitted, then cancelling it, in XGP and CSV;
  Stop after the admitted answer arrived (the answer held by
  `AnswerHeldJobs`); a cancel not delivered (shown, following on); a
  stopped run's close failure reaching the page; a Stop failure standing
  through a persistent failure and contact returning; two Stops answered in
  either order (a confirmed delivery stands) and a stale cancel's failure
  after the next run starts (nothing shown), the cancels answered by
  `CancelAnsweringJobs`; and the persistent-failure notice held back by a
  Stop failure then shown once a Stop is delivered, closed and staying
  closed for its streak, and due again at the next failure when the Stop
  failure holding it back is closed. Its Stop and persistent-failure cases
  came from `LocalRecoveryTests`, which pinned the recovery protocol and
  went with it (halheinrich/backgammon#424), as did the reconciler's, the
  edit clock's and the three numbering-record suites.
- `HomeWiringTests` — bUnit wire tests pinning the `FilterSetup` → `Home`
  integration through the composite's rendered DOM (the panels are
  producer-internal; `FindComponent` over them is banned, host tests
  included), reading the owner's snapshot as Home does: readiness (the empty
  selection is in effect without Apply once a folder is committed, and
  Apply is off with no source), a non-empty selection in effect only after
  Apply and handed to the panel by value, an edit closing the gate and its
  undo reopening it, the source-change rule (a folder commit ends the
  setup — generation
  advanced, the applied selection dropped, Apply re-armed without an edit;
  A → B → A does not bring A's consent back — while the empty selection
  stays in effect across it), a folder change with the empty filter ready
  dropping the earlier run's result while Run stays open (through a real
  run), the not-a-source pins (output path; a same-value folder recommit;
  the same folder respelled with a trailing separator — the wire-layer half
  of halheinrich/backgammon#94, which the producer's `FromPath` unit test
  cannot give: it says the factory folds the spelling, this says nothing
  between the folder input and the owner re-introduces the distinction. It
  pins the contract forward, and deliberately claims no more: the WASM-only
  failure it descends from is **not** reproducible here, since bUnit runs on
  Windows .NET where the old `Path.TrimEndingDirectorySeparator` call
  trimmed `\` correctly — see the test's own remarks), and the saved-filters
  round-trip over the file relay (document rows render, save-as writes into
  the latched folder under the producer's file name, a folder change
  reloads the context); every format radio enabled; and the XGP options
  block, shown only while XGP is selected, handed to the panel. Storage is
  incidental here (Loose). Uses `StubServerHandler` from `bUnitTestHelpers`
  — which serves an in-memory edition of the filterdocument relay — to
  drive the saved-filters context deterministically.
- `HomeSourceReportTests` — how Home reports its source
  (halheinrich/backgammon#374, Arc 2), with storage planned: the surface is
  up from the first render and nothing is reported until the restore
  ends (its last read held by the storage plan, so the window is observed
  rather than raced); **a return with a non-empty committed filter, the
  restore held, released with the same folder, preserves the setup's
  generation and baseline** — the draft on screen, Apply with nothing to
  do, Run open over the applied filter; a different folder restored, and no
  folder restored, each end the setup; blanking the folder reports no source
  with the surface still mounted. And a disposed page: a Home whose restore
  is held while a newer Home establishes folder B and applies its filter
  reports nothing when its read completes — B's source, generation and
  baseline stand; and a disposed Home's in-flight read,
  refused, still reaches the condition while its restore makes no further
  call.
- `HomeStorageUnavailableTests` — the storage condition as Home shows it,
  with storage planned. A browser that refuses **everything** — Home, the
  panel, the filter surface — leaves a working page and the notice, every
  option on its documented default; working storage says nothing. The three
  reporters: a panel-only refusal after Home has settled (the keystroke
  re-renders the panel, not Home); a producer refusal (the commit's write
  held, so the click's own render has happened before the browser answers);
  and a producer refusal answered after Home has gone, shown on return. And
  readable storage with refused writes followed by a successful write: one
  occurrence throughout, and once dismissed, a further refusal does not bring
  it back. The notice as a box (`NoticeBox`): a warning, assertive,
  dismissible, its id and style on the box, saying Hal's ruled wording
  exactly; dismissed by either gesture with the condition standing; and the
  navigate-back row — a second `Home` stays silent after a dismissal **and
  still makes every storage call**, refused again, with the undismissed
  remount as the control.
- The condition's own contract — a fresh holder has no occurrence, the
  first refusal begins it, a dismissal cannot precede it, `Attach` and its
  disposal, and a throwing logger contained — is pinned in
  BgUiPrimitives_Razor's suite, the logger cases as the reporting access's
  (halheinrich/backgammon#424); this suite pins Home over it
  (`HomeStorageUnavailableTests`, above).
- `LocalModePanelStorageTests` — the panel's own key under a refusing
  browser, planned: a refused restore lands on the default and is reported; a refused output-path write keeps the typed
  value and is reported; and working storage restores, saves and reports
  nothing.
- `LocalModePanelNoticeTests` — what kind of box each of the panel's
  notices is (see "Notices"), through real DOM events, every
  box read through BgUiPrimitives' `NoticeBox` (halheinrich/backgammon#385):
  `ShouldBe` states each call site's kind, announcement, dismissibility and
  attributes — the splat trap included (see Pitfalls) — and the reader
  enforces the box's structure. The run error dismissing by either gesture
  and **showing fresh on the next failure although its text is identical**
  (the failure is a refused start — a start that cannot be made is
  retried, not failed). What the
  zero-match and skipped notices *say*, and when, stays with their own
  classes. **Deterministic by construction** (halheinrich/backgammon#374):
  each gesture's own completion is awaited before its outcome is read — a
  run by its click's task, a dismissal by a settle on the
  renderer's dispatcher — never a render with a time budget (see the
  synchronous-event pitfall).
- `ScopedCssBundleLinkTests` — hosted (`WebApplicationFactory`): the root
  document links this host's scoped-CSS bundle, the bundle is served and
  `@import`s BgUiPrimitives_Razor's, and that one is served and carries the
  `Notice`'s rules. It follows the links a browser would and reads what is
  served; it does **not** execute CSS — see the bundle pitfall for what that
  leaves unpinned.
- `HomeRestoreNoticeTests` — the §4 notice through this host, with the
  boot's restoration planned (`FilterSurfaceStorage.ExpectFilterRestore`): a
  restored selection is announced with Apply armed, an edit ends the notice,
  and a navigation back neither announces it again nor restores over the
  user's edit — the owner's app scope, as this host registers it. The
  notice's own mechanics are the producer's to pin and are pinned there.
- `FilterDocumentStoreTests` — the server relay's unit contracts: the
  filename-shape rule's accept/reject matrix, absent file *and* absent
  folder → null, the write round-trip + overwrite, the never-create-folder
  write failure, and the argument guards behind the controller's 400s.
- `FilterDocumentEndpointTests` — hosted wire pins
  (`WebApplicationFactory` over the real `Program` pipeline) for what only
  hosting observes: the round-trip landing on disk at the real route, 204
  for absent, and 400 for non-simple names.
- `HttpDocumentStorageTests` — the client adapter's producer contracts:
  204 → null (absence is a value), body round-trips, folder and name
  travel escaped with the folder read from the delegate at call time,
  non-success and network failures wrap in `DocumentStorageException`, and a
  call with no current folder propagates unwrapped as the adapter-contract
  bug it is.
- `LocalModePanelGateTests` — bUnit tests pinning `LocalModePanel`'s gate
  and what its result describes: the Run-button filter gate (`FilterInEffect`
  plus non-empty paths to enable); the `ErrorMessage` render branch (a
  `Complete + ErrorMessage != null` `ProcessingProgress` renders in a
  `.text-danger` span, not the in-progress slot); and, through real runs,
  the result's lifetime — kept across a re-render that changes neither input
  (an equal config in a new instance), dropped when the filter leaves effect
  or its values change, **dropped on a folder change while the empty filter
  stays ready**, and **not shown when the run completes after its inputs were
  superseded** — its progress and Stop staying with it while it runs — with
  the same held run, released unchanged, as the control; and a click past
  any one of the Run gate's conditions, or while a run is in hand, sends no
  start.
- `HomeRunResultTests` — a Local XGP run through the real page, storage
  planned, its bookkeeping's next-number write held: a run whose folder
  changed while it ran shows no "Done" while that write is pending nor after
  it completes, and the export counter still advances by the job's commit;
  the same held run over unchanged inputs shows its result throughout, as
  the control.
- `HomeXgpAccountingTests` — a run's commit reaching the next-number
  control through the real page, storage planned (each next-number write
  declared): a failed Local run and a cancelled one each advance the next
  number by their commit, and remember it; a control changed while a run
  ran stands, and one changed away and back follows (the value rule); a
  length change alone still advances the next number; and a run that
  committed nothing changes no number and writes nothing.
- `LocalModePanelZeroMatchTests` — the Local zero-match notice, through real
  runs against a zero-row server: shown beside the Done line for an active
  filter, not for the empty one, and not when an active filter comes into
  effect after a zero-row run through the empty one — the box asks the run's
  own filter.
- `LocalModePanelSkippedNoticeTests` — the client half of
  halheinrich/backgammon#223, through real runs: a completed run with skips
  shows the record beside the Done line, labelled "The last run", with the
  counts and every name and reason (a decision by its file's relative path
  and its canonical id); a cancelled and a failed run show theirs, labelled
  with the ending, a stopped one whose close failed showing that failure
  beside its stopped line; a run without skips shows nothing, and a run in
  flight shows nothing until it ends; and zero rows through an active filter
  with skips shows the skip record and not the zero-match notice.
- `LocalModePanelRunRecordLifetimeTests` — §7.2's lifetime through real runs:
  a filter change keeps the record and drops "Done" and the zero-match
  notice; replacing or removing the source clears it; a run completing after
  its source was replaced publishes none — nor after A→B→A, while the host
  is still handed its commit, before it is acknowledged — with a run whose filter
  changed instead as the control (its record, not its result); the next run
  replaces it from its start.
- `SkipListNoticeTests` — the one rendering: an empty record renders nothing,
  a record is an assertive, non-dismissible warning carrying the marker and
  the caller's classes — a caller's `Dismissible` cannot override it — the
  headline counts only the kinds of unit skipped (no "0 files", no "0
  decisions"), and the optional label sits above the headline.
- `LocalModePanelFilterWireTests` — bUnit wire tests pinning the filter half of
  the `/api/process/start` POST (the sibling `LocalModePanelXgpAnonymizeTests`
  covers its format/anonymize half): the applied `FilterConfig` reaches the
  server intact, the depth facet's three toggle+levels pairs cross verbatim
  under an asymmetric selection, the bound config activates the same
  `GetActiveFacets()` set the panel described, and untoggled level lists stay
  inert rather than reviving the retired "levels alone activate" semantics.
  Both halves of each pair are pinned because either one lost in transit widens
  the run silently — no error, just different output.
- `LocalModePanelBusyAffordanceTests` — the two no-fraction progress states:
  busy-before-first-poll and `JobPhase.Rendering` both render an indeterminate
  striped bar, the rendering branch suppresses the frozen elapsed/throughput
  figures, and — the pin that catches a regression to prefix-matching — the
  same snapshot with a "Rendering…" `FileName` but `Phase = Processing` keeps
  the determinate bar and its figures.
- `BusyCursorTests` — the busy-cursor binding: the panel's root carries
  `is-busy` exactly while its own busy flag is up (the pre-first-poll gap and
  the atomic-render branch), and
  carries nothing once the flag drops — pinned against a terminal snapshot,
  which keeps the progress block on screen after the flag is down, because a
  busy cursor stranded over an idle app is worse than the arrow this replaced.
  The marker is asserted to *enclose* the panel (the Run button included): a
  marker on a leaf would satisfy a bare "class is present" check and dress
  nothing. The cursor itself isn't observable from a component test, and the
  class→rule half is deliberately unpinned — see the Pitfalls entry.
- `LocalFolderProcessorPhaseTests` — the server half of the same contract: the
  deck pathway reports exactly one `Rendering` snapshot, it is non-terminal and
  the last thing sent before the terminal one, and the streaming pathways never
  report `Rendering`. Runs one small fixture in a temp folder behind a narrow
  filter — a full-corpus deck render costs minutes to learn the same thing.
- `LocalFolderProcessorRankingTests` — one run, one ranking in Local mode:
  a processor given `DepthFirst` produces, on each of the four pathway kinds
  (CSV, Diagram JSON, Xgp, deck), exactly the producer's `DepthFirst`
  result, over a fixture and filter first proven to admit different
  decisions under `Equity`; and an undefined ranking is refused. See "The
  ranking" for which mutation each pathway catches.
- `XgpNameTemplateTests` — the name-pattern grammar (every `TryParse`
  failure shape) and per-token rendering, including the counter-unification
  pin that `pos{n}` reproduces the old prefix naming, and `Sanitize`
  coverage (internal, via `InternalsVisibleTo`). `{dice}` renders `00` for a
  cube decision, whose row states no roll — file-name presentation, pinned;
  `{score}` renders `3a5a`, `1a5aC`, `moneyJ` and `moneyNJ`; and the preview's
  sample is the producer's row for `AppRanking.Ranking`, rendering `31_3a5a`
  as before.
- `XgpNameAllocatorTests` — per-run uniquifier (`name.xgp`, `name (2).xgp`,
  …), Peek idempotence and the Peek-without-Commit slot reuse that keeps
  failed decisions from consuming a number, `CommittedCount` counting commits
  and not peeks, `{n}` never uniquifying, and
  `Create`'s per-shape `ArgumentException`s. The case-insensitivity of the
  bookkeeping is pinned by comparer reflection — no current token can render
  case-differing base names, so it isn't behaviorally observable yet.
- `HomeXgpPatternTests` — bUnit tests for the pattern UI wiring: textbox
  binding, registry-driven insert-token dropdown, preview/error branches,
  and the numbering as remembered controls — a reload showing the stored
  pattern, next number and length (no older key read), a stored value that
  is not a number leaving the default, a pattern edit continuing the
  remembered number only under its pattern, and every change written —
  storage planned, so each read and write is declared and verified.
- `HomeOutputFormatRestoreTests` — the stored output format restored by
  declaration name only, never by ordinal (halheinrich/backgammon#164), with
  storage planned.
- `OpeningBookProviderTests` — the provider's pure path resolution (absent →
  auto-detect, empty → disabled, explicit) and load/degrade (real fixture book
  loads; empty config and a missing file both degrade to no book without
  throwing).
- `LocalFolderProcessorOpeningBookTests` — pins that the loaded book reaches
  every server processing pathway. CSV asserts the enriched depth label
  directly (with book) vs. the bare "Book V2" (without); Diagram JSON, whose
  wire carries the typed depth facts and not the derived label, is read back
  as records and asserts the same labels off their candidates; the Xgp and
  deck pathways — whose output carries no label — use a filter differential over
  a lone depth clause (`IncludeBookRollouts` qualified by
  `BookRolloutLevels = [Ply4]`): book present ⇒ the enriched decisions survive
  and are written / rendered; book absent ⇒ they degrade to Unknown and nothing
  does.
  Runs against a temp folder holding only `ajhhBG0407.xg` for deterministic
  counts. Enrichment correctness itself is owned by ConvertXgToJson_Lib.

## Public API

### HTTP endpoints

The `/api/process` endpoints are the Local job protocol of the umbrella's
`SPEC-notices.md` §7.5 (see "A Local run's job"). A job's identity is the
browser's, at most 64 letters, digits and hyphens and nothing after them (the
pattern ends at `\z`, since `$` would let a final newline through); a refusal
is 409 with a `ProtocolRefusal { Reason }`, `Reason` `ChangedRequest` — the
one refusal left once the recovery protocol's ledger, instance and settle
went (halheinrich/backgammon#424, 2026-10-19). The wire keeps its shape, an
enum on a 409, so a refusal stays self-describing.

```
POST /api/process/start
  body:  StartRequest { JobId, Run }
           Run:  ProcessRequest { FolderPath, OutputPath, Filters,
                                  OutputFormat, XgpOptions, Anonymize }
  200 →  StartResponse { JobId }
         — admitted: a new job, or a repeat of an admitted identity with the
           same request, which starts nothing.
  409 →  ProtocolRefusal ChangedRequest — the identity was admitted for a
         different request. Nothing starts, and the admission stands.
  400 →  an identity of the wrong shape.
         — for OutputFormat.Xgp, OutputPath names the output FOLDER;
           for every other format it names the output file.

GET  /api/process/{jobId}/status
  200 →  ProcessingProgress { Current, Total, Phase, FileName, TotalRows,
                              Complete, Cancelled, ElapsedSec, FilesPerSec,
                              ErrorMessage?, CloseFailureMessage?,
                              WrittenPath?, Skipped[], XgpCommit?,
                              PercentComplete (computed) }
         — as often as asked; a read removes nothing. PercentComplete is
           derived from Current/Total; ErrorMessage is non-null only on the
           terminal error state; CloseFailureMessage only on a cancelled one
           whose output file then failed to close; WrittenPath only on a
           completed successful one — the one file as it was created
           (numbered where a file of the requested name existed), or an
           XGP run's output folder; Phase is the JobPhase discriminator the
           client picks its progress bar from (FileName is presentation and
           is never parsed for it); Skipped is the run's skip record so far,
           the array of SkippedItem { FileName, DecisionId?, Reason } with
           FileName relative to the input folder, on every snapshot
           including the error one; XgpCommit { Options, Count } is an XGP
           run's committed result so far — null for every other format — on
           every snapshot, the cancelled and error ones included, taken from
           the run's record.
  404 →  the server does not hold the job: never started here, released
         by its acknowledgement, or lost with a restart. The page's
         following ends with the run error.

POST /api/process/{jobId}/cancel
  200 →  (empty)   404 → the job has ended or is not held
         — the client tells the two apart from a failure (any other answer,
           or none), and shows a cancel refused or failed, unless one for
           the job was already confirmed delivered, which no later failure
           undoes: Stop is never shown as delivered when it was not.

POST /api/process/{jobId}/acknowledge
  204 →  always: the job's result is released; a job not held, or still
         running, changes nothing. The admission is untouched.

GET  /api/openingbook/status
  200 →  OpeningBookStatus { Loaded, EntryCount }
         — whether the server loaded the opening book, and its entry count
           (0 when none).

GET  /api/filterdocument?folder=…&name=…
  200 →  the named file's text (text/plain)
  204 →  file (or folder) absent — a value, not an error: the client maps
         it to the storage seam's null-for-absent contract
  400 →  blank folder, or name that isn't a simple file name

PUT  /api/filterdocument?folder=…&name=…
  body:  the file's full text, written verbatim (overwrite)
  200 →  (empty)   400 → as GET; IO failures (e.g. missing folder) → 500,
         which the client adapter degrades to its WriteFailed state
```

The `filterdocument` pair is the saved-filters file relay: the client-side
`HttpDocumentStorage` adapter is its only caller, supplying file names
from `SavedFiltersDocument` (producer-owned) and the folder from Home's
latched source path — the server validates the name's *shape* but never
knows the names themselves.

```
POST /api/shutdown
  200 →  (empty; host begins graceful shutdown)
```

The protocol's types live in `ExtractFromXgToCsv.Client/Shared/` — the
client owns the wire shape and the server deserializes against it. The
server's `ProcessController` references the same types via the Client
project reference.

### Client-shared types (`ExtractFromXgToCsv.Client/Shared/`)

- `AppRanking` — `static class` with one member, `Ranking`
  (`PlayRanking.Equity`): the application's one ranking, stated once (see
  "The ranking"). Public because the server's composition root reads it, to
  hand it to the processor.
- `OpeningBookStatus` — response record for `GET /api/openingbook/status`
  (`record OpeningBookStatus(bool Loaded, int EntryCount)`).
- `JobPhase` — enum `Processing | Rendering`. Which stage of a Local-mode run a
  `ProcessingProgress` snapshot describes, and the SSOT the client branches its
  progress bar on. Deliberately has no terminal member: `Complete` /
  `Cancelled` / `ErrorMessage` already are the terminal-state SSOT and a second
  one could disagree with them. `Processing` is `0`, so every pathway with only
  that one stage needs no assignment.
- `OutputFormat` — enum `Csv | DiagramJson | Pptx | Pdf | Xgp`. Server
  references it too, which is why it lives under `Client/Shared` rather than
  being duplicated. `OutputFormatNames.ShortName` is
  the one statement of the short name the copy gives each ("CSV", "Diagram
  JSON", "PPTX", "PDF", "XGP").
- `StrictJsonStringEnumConverter<T>` — BgDataTypes_Lib's; this app's
  body-identical copy was retired for it, since common code has one shared
  owner (Hal's ruling, halheinrich/backgammon#424, 2026-10-19). Bundled by
  type-level attribute onto
  `OutputFormat`, `JobPhase` and `RefusalReason`, so every
  enum on the Local-mode wire crosses as
  its declaration name and a numeric ordinal is rejected. Neither end of that
  wire configures a serializer: the panel posts with `HttpClient`'s stock
  options and the server binds with a bare `AddControllers()`. Putting the rule
  on the type is what makes that work — and is why `LocalModePanel` no longer
  owns a `_wireOptions`. `BgDataTypes_Lib`'s own enums (`AnalysisLevel`
  among them) carry the same converter and `XgFilter_Lib`'s filter enums
  their equivalent, so one payload has one spelling
  convention throughout (halheinrich/backgammon#164,
  halheinrich/backgammon#37).
- `XgpExportOptions` — naming options for a batch of per-decision `.xgp`
  exports (`NamePattern`, `StartNumber`, `SuffixLength`;
  `DefaultNamePattern` = `"pos{n}"`). Rendering lives in the naming engine —
  the export names files through an `XgpNameAllocator` created from these
  options. Permissive wire DTO — validation is the separate
  `TryValidate(out error)` (the UI gates on it; export pathways throw
  `ArgumentException` via `XgpNameAllocator.Create`).
- `XgpNameAllocator` — the naming engine's one public entry point: a stateful
  per-run name source created via `Create(options, filters)` (the single
  options-validation throw point). The server's processor names files
  through it, and the name preview renders through it; `CommittedCount` is
  the numbers a run has used.
- `XgpExportCommit` — `sealed record XgpExportCommit(XgpExportOptions
  Options, int Count)`: what one XGP export committed — the options it
  captured and the files it committed — valid by construction (valid options,
  a non-negative count), `NextNumber` and `CommittedNothing` (the one
  statement of "consumed no number") derived and not serialized. A run's
  accounting to Home (see "XGP export"); on the wire as
  `ProcessingProgress.XgpCommit`.
- `XgpPayload` — static: `Prepare(DecisionId, XgFile, ReadOnlyMemory<byte>?
  sourceBytes, bool anonymize)` prepares one decision's `.xgp` payload from
  its decoded source — the one routing rule — and
  `CopiesSourceVerbatim(DecisionId)` says whose payload is its source's
  exact bytes. Public because the server's processor uses it.
- Naming engine internals (`XgpNameTokens`, `XgpNameToken`, `XgpTokenSource`,
  `XgpNameContext`, `XgpNameTemplate`) — **`internal`** to the client assembly
  (test-reachable via the existing `InternalsVisibleTo`); reached only through
  `XgpNameAllocator`. See the XGP export section. App-level home beside
  `XgpExportOptions`, but they couple only to primitives and lib types
  (`FilterConfig`, `DecisionRow`); the DTO stops at `XgpNameAllocator.Create`,
  so a future move into a library is relocation-only. `XgpNamePreview`
  (`internal`, the preview's sample row) sits beside the engine and not in
  it, because it reads `AppRanking`, an app type. `ILocalJobs` and
  `LocalJobsClient`, and their companions (`Client/Services`), are
  `internal` for the same reason — client-assembly implementation details,
  not public surface.
- `ProcessRequest` — what one Local run does, the `Run` of a start request
  (`FolderPath`, `OutputPath`, `Filters` of type
  `XgFilter_Lib.Filtering.FilterConfig`, `OutputFormat`, `XgpOptions`,
  `Anonymize`). A `sealed record` with init-only members, so a repeated
  start compares with the admitted one by value (`FilterConfig` and
  `XgpExportOptions` compare by value too).
- The protocol (`SPEC-notices.md` §7.5): `StartRequest(JobId, Run)` and
  `StartResponse(JobId)`; `ProtocolRefusal(Reason)` with `RefusalReason`
  (`ChangedRequest`). Records, so a start compares by value. The recovery
  protocol's types — the ledger's mark, entry and snapshot, the settle
  types, the panel's start state and owed job — went with it
  (halheinrich/backgammon#424, 2026-10-19).
- `ProcessingProgress` — status-endpoint payload; an immutable record (every
  property init-only; a changed snapshot is a new one, made with `with`) the
  processor makes and the panel reads, so the job store can keep, serve and
  hand on a snapshot as it is. `Skipped` (`SkipRecord`, empty by default,
  never null) is the run's skip record as of the snapshot; `XgpCommit`
  (`XgpExportCommit?`) an XGP run's committed result; `WrittenPath`, on a
  successful terminal snapshot only, where the run wrote its output, an
  output fact the request's `OutputPath` never stands in for.
  `PercentComplete` is get-only and derived.
- `SkipRecord` — the one record type of skipped units
  (`SPEC-notices.md` §7.1): an immutable, ordered `IReadOnlyList<SkippedItem>`
  with no null entry — `Empty`, `Of(items)`, collection expressions
  (`Create`), and `With(item)` returning a new record — whose `FileCount` and
  `DecisionCount` are derived from its entries, the one statement of "a file
  skip names no decision". Value equality (the same entries in order). On the
  wire it is the array of its entries, through its bundled converter, so a
  holder can never change the record another reads (the collection rider of
  halheinrich/backgammon#273).
- `SkippedItem` — `sealed record SkippedItem(string FileName, DecisionId?
  DecisionId, string Reason)`, one entry of a `SkipRecord`: a file skip (read
  or conversion) or a decision skip (an XGP payload whose preparation failed).
  `FileName` is the file as the user named it — its path relative to the
  run's input folder, unambiguous under the recursive search, whereas rows
  and logs keep the bare name (see Job lifecycle for why the two differ).
  `DecisionId` is null for a
  whole-file skip and set for a decision skip, crossing in the library's
  canonical string form through `DecisionId`'s own bundled converter;
  `Reason` is the failure's message. A positional record like
  `OpeningBookStatus`: an entry is a fact about what
  happened and has no reason to change.

`FilterConfig` is **not** in `Client/Shared` — it lives in
`XgFilter_Lib.Filtering` (lib-owned). Both client and server reference the
lib type directly; nothing in this subproject duplicates or shadows it.

## Pitfalls

- **WASM can't stream HTTP responses.** A run's progress is delivered by
  client polling, not server-pushed streaming. Don't "fix" this by adding an
  `IAsyncEnumerable` endpoint expecting WASM to consume it.
- **The busy cursor is one class and one rule — don't add per-spot cursor CSS.**
  The affordance is `is-busy` on the panel's root element plus the single
  `wwwroot/app.css` rule. A new slow gesture inherits it by raising the busy
  state the panel already holds; putting `cursor:` on an individual button, notice or progress bar forks the
  discipline and drifts. Corollary: nothing links the class to the rule at
  compile or test time — `BusyCursorTests` pins state→class only — so the name
  must be changed in the panel and the stylesheet together. That is the price
  of the affordance living in CSS rather than in the components, which is where
  it belongs.
- **The root document must link this host's scoped-CSS bundle — and nothing
  fails when it doesn't.** `Components/App.razor` links
  `@Assets["ExtractFromXgToCsv.styles.css"]` (through the static-assets
  manifest, because the bundle's own `@import` is fingerprinted). Nothing in
  this repository has scoped CSS of its own; the bundle exists to carry the
  `@import` of BgUiPrimitives_Razor's, and that CSS is half of how a
  dismissible `Notice` works: it makes the content wrapper transparent to the
  pointer, so a click on the notice's text lands on the box and dismisses.
  Without the link the notice still renders and its close button and padding
  still dismiss — only the large target is silently gone, and no bUnit test
  can see it, since component tests load no stylesheet. This host had never
  linked the bundle before halheinrich/backgammon#248. The bundle belongs to
  the **server** project, where the root document lives — not
  `ExtractFromXgToCsv.Client.styles.css`. `ScopedCssBundleLinkTests` pins the
  delivery chain, not that the rules *apply*: a dismissible box computing
  `cursor: pointer` and its content wrapper `pointer-events: none` on a real
  page load was checked in a browser at the adoption, and this repository has
  no browser-level suite to hold it. Re-check it in a browser after touching
  the root document's links or the static-assets pipeline.
- **A green build proves nothing about a `<Notice>` tag.** `Notice` captures
  unmatched attributes, so a parameter name it does not have — a misspelt
  `Announcement` or `Dismissible` — compiles clean and is splatted onto the box
  as a plain attribute, while the real parameter silently takes its default:
  polite, not dismissible. (Measured in the first adopter, 2026-09-21, and
  reproduced here at the adoption: `Anouncement=` built with no warning.)
  Every box is therefore pinned off the rendered DOM through BgUiPrimitives'
  `NoticeBox.ShouldBe`, which states the call site's kind, announcement and
  dismissibility and fails on any box attribute the call site did not name.
  A new box needs the same pins; the compiler will not ask for them. `Kind`
  and `ChildContent` are the exception — they are `[EditorRequired]`, so
  omitting one is a build error. Don't name `alert-*`, `btn-close`,
  `bg-notice-content` or a role in a test: those are the component's.
- **A synchronous bUnit event helper returns before a busy renderer runs the
  event.** `Click()` and the rest hand the event to the renderer's
  dispatcher; if earlier work still holds it — the tail of the gesture that
  put a notice up, a posted continuation on a loaded machine — the event is
  queued and the helper returns first. Diagnosed on
  halheinrich/backgammon#374 by holding the dispatcher busy: a synchronous
  dismissal returned with the box still on screen, and a synchronous
  Download followed by `WaitForAssertion` failed with "Check count: 0" —
  the check itself waits on the dispatcher, so a fixed budget measures the
  machine, not the code. Await the operation's own task (`ClickAsync`) and
  read the settled outcome. `NoticeBox.Dismiss` returns no task, so a
  dismissal is settled by `NoticeTesting.DismissAndSettleAsync`: the
  dispatcher runs work in order, and the dismissal is synchronous end to
  end, so an empty item queued after it completes only once it has run.
- **A gate reason is never also a run error.** A standing reason a control is
  dark renders from the state that makes it so (the selected-size limit from
  `_totalSelectedBytes`) and is not dismissible. Writing the same fact into
  `_runError` as well gives it a second, dismissible box — the defect the
  adoption removed from `HandleFileSelectionAsync`. `_runError` is for a
  gesture that threw, and for nothing that is still true after the box is
  closed.
- **Don't add a dismissed flag beside `_runError`, and don't hold the
  storage notice's dismissal in `Home`.** Each dismissal has exactly one
  holder, the occurrence's owner (see "Notices"). A panel-side flag would be
  a second bit that can disagree with the field; a page-side one would die on
  navigation while the condition it belongs to does not, so a navigate-back
  would re-show a notice the user had closed.
- **`ProcessingProgress.Phase` is the progress-stage SSOT; `FileName` is
  presentation.** The rendering line reads "Rendering PPTX (…)" for humans,
  but the client branches on `JobPhase.Rendering`. Don't re-derive the stage by
  prefix-matching `FileName` — the two would drift, and the string is the half
  that's meant to change freely. `LocalModePanelBusyAffordanceTests` pins the
  direction that catches it: the same snapshot with a "Rendering…" `FileName`
  but `Phase = Processing` keeps the determinate bar.
- **A unit that fails to be prepared is a recorded skip; a commit that fails
  and a cancellation never are.** A failed read comes from the read's
  outcome — don't wrap `XgSourceReader` in a catch, which would turn a usage
  error into a skipped file and a cancellation into a completed run. A
  file's contribution is staged whole (`.ToList()` inside its boundary):
  enumerating the filtering operation lazily during the commit would write a
  failing file's first rows and turn its failure into the run's. And keep
  every write to the destination outside the skip boundaries — inside one, a
  destination that refuses is reported as a skipped file or decision and the
  run goes on (`LocalFolderProcessorBoundaryTests`; see Job lifecycle).
- **The job runner's progress sink is inline — don't restore `Progress<T>`.**
  A posted report can land after the runner's catch has recorded the terminal
  result. The store now refuses a report after a terminal result, but the
  reports' order is the run's, and the sink keeps it (see Job lifecycle).
- **A status read never removes a job; only the acknowledgement releases
  it** (`SPEC-notices.md` §7.5). Don't restore removal on the terminal read —
  one lost response then lost the result and the XGP accounting with it
  (`JobStoreTests`, `LocalJobEndpointTests`). A job a page never
  acknowledges is kept for the process's lifetime, by rule.
- **A start is judged by its identity, under the store's lock** — a repeat
  (its admission, nothing started) or a changed request. Keep the admitted
  requests past the acknowledgement: a retried start whose reply was lost
  must find its job's admission, not run again (`JobStoreTests`,
  `LocalStopTests`).
- **A 404 ends following; a communication failure never does.** Don't
  retry a status read the server answered 404 — it no longer holds the job,
  and following would never end — and don't end following on a failure to
  communicate, which is retried with the persistent-failure notice
  (`LocalStopTests`).
- **Don't let Stop cancel the start's sending.** A Stop pressed before the
  start is answered is pending until the answer; abandoning the start would
  leave a job the page cannot name running, and a lost reply would read as
  a confirmed cancellation (`LocalStopTests`).
- **`prerender:false` is required.** Filter state lives in the WASM runtime; a prerendered server pass would double-init components
  and lose state. Don't enable prerendering on the routable components.
- **Server services stay `public` by framework constraint, not by need.**
  `JobStore` (with `JobEntry` and `StartJudgement`) and
  `LocalFolderProcessor` are
  `public` even though nothing outside the server assembly consumes them,
  because the MVC controllers constructor-inject them: a `public` controller
  can't take an `internal` constructor parameter (CS0051), and controller
  discovery only finds `public` classes, so the controllers can't be
  internalized to match either. Best-practice constructor injection is worth
  more than the surface narrowing here — don't re-flag these in a surface
  audit, and don't reach for interface indirection, a service-locator, or a
  custom controller feature-provider to force them `internal`. The client-side
  naming engine (`XgpNameTemplate` et al.) has no such
  constraint — it's injected only into Razor components (whose generated
  `@inject` properties are non-public) or reached statically, so it is
  `internal`.
- **Deck output (PPTX/PDF) requires server-side rendering.** Both deck
  paths go through `BackgammonDiagram_Lib.ExportRaster.DiagramRasterRenderer`
  (`RenderPptx` / `RenderPdf`), which renders SVG via the native-free core
  and then rasterizes it via SkiaSharp before assembling the deck. SkiaSharp's
  native binary isn't available under Blazor WASM, so the decks are rendered
  on the server. Don't add a `BackgammonDiagram_Lib.ExportRaster`
  reference to `ExtractFromXgToCsv.Client.csproj` — it would either fail at
  runtime or pull `SkiaSharp.NativeAssets.WebAssembly` into the WASM payload
  (multi-MB bloat, AOT/Skia friction). The SVG-only core
  `BackgammonDiagram_Lib` is WASM-safe, but the client has no need for it.
- **QuestPDF license must be set at server startup.** `RenderPdf` throws if
  `QuestPDF.Settings.License` is unset. `Program.cs` assigns
  `LicenseType.Community` at startup. Community is
  appropriate for the current non-commercial posture
  (revenue ≤ $1M / year); revisit if commercial distribution ever becomes
  a goal.
- **`FilterConfig` is materialized on the server.** The panel takes the
  filter in effect as a `FilterConfig` and POSTs it unbuilt;
  `ProcessController` calls `request.Filters.Build()` to materialize the
  `DecisionFilterSet` before handing it to `LocalFolderProcessor`.
- **Compare configs by value, never by reference.** The owner's
  `ConfigInEffectFor` returns a new instance on every call, so Home hands
  the panel a new instance on every render. A reference comparison — a
  cache key, a "did the filter change" check, an `Assert.Same` in a test —
  reads every render as a change. `FilterConfig` has value equality
  (`Equals`, order-insensitive over its lists) and no `==` operator; it is
  mutable, so a key held across calls is a copy the holder owns.
- **XgFilter_Razor is not referenced server-side — and the file relay
  depends on that.** The composite and its model types are consumed only by
  the WASM client; the server's bridge from a configured filter to a
  runnable filter set is `FilterConfig.Build()`, not the panel — see the
  materialization pitfall above. The saved-filters relay
  (`FilterDocumentStore` / `FilterDocumentController`) extends the rule
  into a **shape-vs-policy split**: the server rejects anything but a
  simple filename (no separators, no traversal, no rooted paths) but must
  never hardcode or learn the filter document names — those are
  `SavedFiltersDocument`'s (producer-owned), supplied per request by the
  client adapter. Adding an XgFilter_Razor reference to the server csproj
  "for the constants" would re-couple what the split exists to decouple.
- **Run gating is the owner's, read once — don't re-derive it, and don't
  re-split it.** The panel's filter term is `FilterInEffect`, which
  Home reads from `FilterSetup` source-relatively, in one property, and
  from nothing else. Don't call `ConfigInEffectFor` at a use site with a
  token minted there — a second mint is a second chance to mint it
  differently — and don't hand a panel a config beside a separate "applied"
  flag: one nullable is one fact, and two parameters can disagree. If Run
  is ever enabled with the selection on screen not in effect, or
  with what is in effect for a *different* source, that's a regression: the
  owner is the gate, not a cosmetic hint. Don't add an empty-filter
  exception either — the owner's readiness already puts the empty selection
  in effect without Apply (halheinrich/backgammon#266).
- **A source change ends the setup — and only Home says so.** Home latches
  the folder path at the input's `@onchange` boundary (plus the restore),
  and reports
  the token (`ReportSource`) whether or not the surface is mounted, and the
  owner ends the setup on any different token, `null` included: the applied
  selection is dropped, the draft kept, and the saved-filters context
  reloads — one gesture drives both because the source folder *is* the
  filter store's folder. An equal token ends nothing, which is how a remount
  and a same-folder recommit keep their setup; A → B → A is two endings, so
  re-entering a folder never brings its old consent back. Two
  consequences that look like bugs but are the contract: Apply is off
  before any source exists; and the
  output path never ends anything — it is not a source (umbrella-ratified).
  Don't wire the latch to `@oninput` — a half-typed path is not a source.
- **Never report a source before it is known.** `ReportSource(null)` is a
  real answer: it ends an existing setup. Home's folder arrives in its
  first-render restore, from storage, so until the restore has ended the
  answer is "not yet known", which is never reported. Reporting early would end the setup on every
  return to the page and then report the restored folder as a new source:
  the baseline gone, Apply re-armed, Run dark. Hence `_sourceResolved`, and
  hence no panel renders before it (the panel is where a source is
  latched). The surface itself mounts from the first render: the owner
  outlives the page, so it shows the existing setup as it stands until the
  report arrives. Don't "simplify" this into a report from `OnInitialized`
  or `OnParametersSet`. `HomeSourceReportTests` holds the restore's last
  read open to pin it. **Nor after the page has gone**: the restore's awaits
  belong to the page's lifetime (see "Filter setup"), so don't add an await
  to the restore that is not checked against the lifetime.
- **The restore runs to its end, over results.** Every read in Home's
  restore answers a value or "nothing stored" (a refusal is reported and
  answers "nothing stored" too), so the restore always reaches the source
  report with facts, not with reads that never landed. Don't wrap the
  restore in a method-wide `try`: it would have to restate every default in
  its handler (a second copy to drift from the field initializers).
- **Every storage call goes through `BrowserStorage`, and nothing latches.**
  This app's keys go through BgUiPrimitives_Razor's
  `ReportingBrowserStorage`, each call with its own consequence phrase; the
  surface's go through its own seam to the same accessor. Don't call `localStorage` through
  `IJSRuntime`, don't add a "storage is unavailable" flag that skips a call,
  and don't let a refused write stop the gesture that made it (v1.12.1's
  panels did, halheinrich/backgammon#102). `BrowserStorageCondition` remembers
  that a refusal *happened*, for its notice; it never gates a call. And
  calling `BrowserStorage` directly loses the refusal — the reporting
  access reports it before it returns, which is why its callers may read
  `read.IsStored ? read.Value : null` and coalesce on that.
- **`HttpDocumentStorage` is one stable instance over a delegate —
  and must never be called without a folder.** The composite rebuilds its
  saved-filters store when the bound `Storage` reference changes, so Home
  constructs the adapter once and the live folder rides the
  `Func<string?>`. While the latched path is blank Home binds
  `Storage = null` — no section renders and nothing calls
  the relay; a call with no folder is therefore an adapter-contract bug
  and throws `InvalidOperationException` unwrapped, while everything that
  means "the I/O failed" wraps in `DocumentStorageException` so the store
  degrades instead of faulting the page.
- **`WebApplicationFactory` marker: never `Program`.** The Client's
  top-level entry point generates its own `Program`, visible to the test
  project through the `InternalsVisibleTo` grant — naming `Program` as the
  factory's entry-point type draws CS0433 (ambiguous between Client and
  server). The hosted tests use a server-assembly marker type (`JobStore`)
  instead; the factory only needs *a* type from the entry assembly.
- **CA1859 in `OutputConsistencyTests`.** The interface usage is the thing
  under test (the shared-pipeline contract). Don't "fix" the warning by
  switching to concrete types — you'd defeat the test.
- **`.xgp`-sourced decisions export verbatim, not sliced.** In
  `XgpPayload.Prepare`, an `XgpDecisionId` decision with names kept is its
  source file's exact bytes: the source already is a single-position analyzed
  `.xgp`, so the verbatim copy is byte-identical, cheaper than a re-slice,
  and stays strictly more faithful — a re-slice would clear the match-level
  comments the copy preserves. Don't "unify" this branch through the slice
  surface — byte equality with the source is pinned by
  `LocalFolderProcessorXgpTests`. The bytes must have been kept: the run
  reads with them on the XGP pathway when not anonymizing. Don't decide by
  the file's extension: the stamping rule is the producer's
  (`CopiesSourceVerbatim`).
- **The XGP counter is client-owned, and fed only by commits.** The next
  number is a remembered control (`xg_xgpNextNumber`) and advances only
  from a terminal result's `XgpExportCommit` — the options the run captured
  and the files it committed. Don't reconstruct a count from `TotalRows`, a
  progress snapshot or the selected rows, and don't overwrite a control the
  user changed since the run started — compared by value with the options
  the run captured (a length change is not a choice about the next number;
  `HomeXgpAccountingTests`). Continuity is not promised across a reload,
  another tab or a restart (Hal, 2026-10-19, halheinrich/backgammon#424);
  don't add a record, a ledger or a migration back to promise it. Nothing
  scans previously exported files, and no number is ever inferred from file
  names (Hal's F3 ruling): a user who bypasses the counter (edits "next
  number" backwards, exports into the same place twice with a reset
  pattern) gets colliding names, and each such file is written under the
  next free name instead of replacing the existing one.
- **Log after recording, and contain the log.** Where a log reports state
  the user depends on — a skip, a storage refusal
  — record the state first and catch what the logger throws: a logging
  failure must never end the batch or the storage call.
- **The ranking is stated once, and always stated.** Every producer
  operation that takes a ranking gets `AppRanking.Ranking` — never a
  `PlayRanking` literal, and never nothing: a producer whose ranking is
  optional (`XgIteratorOptions`, whose default is `Equity`) is still handed
  the app's. That today's choice and
  the producers' defaults are both `Equity` is not an agreement to lean on
  (Hal, 2026-09-29): one run's rows, filtering and diagrams must be one
  ranking because they were handed one, whatever the defaults become.
- **`{dice}`'s `00` is presentation.** A cube decision has no roll, and the
  row states none. The token spells that absence `00` in a file name only;
  never turn it back into a roll, and never pass `00` (or 0) to a domain API
  as dice.
- **`XgpNameTokens.All` is the token SSOT.** Validation, rendering, and the
  insert-token dropdown all derive from the registry — add tokens only
  there, never as a special case in the template parser or the UI. Token
  render functions must not fail: an unavailable value renders empty.
- **The allocator's Peek/Commit split is load-bearing.** In
  `ProcessXgpAsync`, a decision's name is Peeked and the slot Committed only
  after its file is written — that's what keeps "a skipped or unwritten
  decision doesn't consume a number" true, and `CommittedCount` is what the
  run's commit carries to the persisted counter. Collapsing the split into an
  unconditional `Next` before the write makes the next batch's numbers
  collide with what's on disk.
- **The name uniquifier is per-run only.** `XgpNameAllocator` appends
  ` (2)`, ` (3)`, … within one export run; it never looks at the output
  folder. Across runs the counter (`{n}`) keeps names apart, and the
  create-new write is the last guard: a pattern without `{n}`, a
  reused number or a re-export into the same folder carries the issued
  name's sequence on past the existing files (`SPEC-notices.md` §7.5).
  Number a name only through `NumberedFileName`: the allocator and the
  free-name loop share its one ` (n)` rule, so don't format a suffix
  anywhere else.
- **Never check for a file and then write it.** Every Local output file is
  created with `FileMode.CreateNew` in one operation
  (`IOutputDestination.CreateNew`); an existence check followed by a write
  has a window another process can fill. The `File.Exists` after a refused
  create only classifies the refusal as a collision.
- **A file is the run's only once its create-new succeeds.** Remove an
  unfinished file only if this run created it (its write or close failed);
  a collision, or any other failure to create, removes nothing — the file
  there is someone else's.
- **The numbering keys are the three controls'.** `xg_xgpPattern` and
  `xg_xgpSuffixLength` keep their old names and meanings; the next number
  is `xg_xgpNextNumber`, not the old `xg_xgpLastNumber` (the last used).
  Don't read the retired record's document, the last-number key or the
  prefix key: §7.5 rules no migration.
- **The naming engine is deliberately lib-ready.** `XgpNameContext` carries
  primitives and lib types only; the app's `XgpExportOptions` DTO must not
  leak below `XgpNameAllocator.Create`. Keep it that way so a future move of
  the engine into a library stays relocation-only.
- **XGP accounting comes from the run's record, never from a progress
  snapshot.** The pathways report every tenth file, so the last report can
  trail what was written; the `RunRecord`'s commit is replaced as each file
  is written, and the job runner's terminal snapshots take it from there.
  (Until v1.12.2 the Xgp pathway reported on every file so its last
  `TotalRows` could stand in for the count; that special case is gone.)
- **Xgp `OutputPath` is a folder.** `ProcessXgpAsync` treats it as a
  directory (created if absent) and `LocalModePanel` skips the
  extension-swap logic for Xgp. No Local output file is overwritten (see
  above): the one file the other formats write takes the next free name
  beside the path the user named, as each XGP file does in its folder.
- **In a bUnit test, wait on the screen for what the screen shows.** A
  condition on a child component's parameter can hold before the child has
  re-rendered — Home's render sets the panel's parameters ahead of the
  panel's own render — so a DOM assertion after a state wait can read the
  previous render (`LocalPage.WaitForScreen`). And a planned Local XGP Home
  test declares the next-number write a run's commit makes
  (`HomeStoragePlan.ExpectNextNumberWrite`).
- **Opening-book config: absent ≠ empty.** `OpeningBookProvider.ResolvePath`
  reads `configuration["OpeningBookPath"]`: `null` (key absent) auto-detects the
  default install path; `""` (key present, empty) *disables* enrichment. Don't
  add an empty `"OpeningBookPath": ""` to `appsettings.json` thinking it's a
  harmless placeholder — it turns enrichment off. Leave the key out to keep
  auto-detect, or set a real path.
- **The run's result and its run record live by different rules.** The
  Done line, the zero-match notice and the run error go on any source or
  filter change; the run's skip record survives a filter change and goes
  with the source (`SPEC-notices.md` §7.2). Don't fold the record back into
  `DropResult`, and don't publish it only when the result is published.
- **Enriched depth isn't in the Xgp/deck output.** The book only changes a
  decision's depth metadata, not the exported `.xgp` bytes or the rendered deck.
  So the Xgp and deck pathways' book wiring is pinned by a *filter differential*
  (a depth facet of one clause — `IncludeBookRollouts` qualified by
  `BookRolloutLevels = [Ply4]` — admits the enriched decisions with a book and
  nothing without one), not by reading a label out of the output — see
  `LocalFolderProcessorOpeningBookTests`. The toggle is what activates the
  facet: the level list alone would be inert and the differential would vanish.
- **Fixture files are not in this repo.** They live in umbrella `TestData/`
  and are not tracked by git (contents are gitignored; structure is held by
  `.gitkeep`). A fresh clone of this subproject alone cannot run the tests
  without the umbrella `TestData/` present.

## Subproject-internal next steps

- **Streaming JSON write for large datasets.** Current Diagram JSON output is
  a single in-memory array. Very large corpora may need a streaming writer
  (NDJSON or JSON-array streaming) rather than building the full document
  before serializing.
- **Unacknowledged jobs in `JobStore`.** A job is kept until the page that
  started it acknowledges it, or the process ends (`SPEC-notices.md` §7.5),
  so a job whose page reloaded or lost its acknowledgement stays for the
  process lifetime — by rule, since a sweep could release a result a page
  is still owed. The admitted requests are kept for the process's lifetime
  too. A single-user local app tolerates both; any expiry would need a
  ruling against §7.5 first.
- **Pipeline performance.** The pipeline is I/O-dominated but has
  not been profiled. Candidate wins: parallelizing per-file work, reducing
  per-decision allocations in `XgDecisionIterator` consumers, and cutting
  reflection or LINQ in the filter hot path.
