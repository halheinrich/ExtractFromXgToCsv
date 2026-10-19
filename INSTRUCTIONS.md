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
  which both modes read every file through (`ReadFileAsync` in Local mode,
  `ReadAsync` over a browser upload in Web mode; see "Reading, converting,
  skipping and committing"); `XgFileReader` for discovery
  (`EnumerateXgFormatFiles`); `XgDecisionIterator`, which Web mode converts
  each decoded file through, under `XgIteratorOptions` (the run's ranking and
  the optional `OpeningBook`; see "The ranking" and "Opening-book
  enrichment"); `XgpExporter` (slice and anonymize-copy surfaces) for the XGP
  export's payloads; `OpeningBook`.
- **XgFilter_Lib** — `FilterConfig.Build()` / `DecisionFilterSet` for the
  filter pipeline; `FilteredDecisionIterator`, whose parsed-file filtering
  operation (`IterateFile`, `IterateFileDiagrams`) Local mode filters every
  decoded file through, the filter set's early-exit hints wired.
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

- **Hosting** — `Program.cs`: the `AppMode` guard that registers the
  Local-mode services only in Local mode, and the QuestPDF license. With it
  `Components/` (the root document, router, layout and server error page that
  host the WASM app, `prerender:false`; the root document links Bootstrap,
  `app.css` and this host's scoped-CSS bundle) and `appsettings*.json`
  (`AppMode`, and the optional `OpeningBookPath`).
- **HTTP surface** — `Controllers/`: the Local job protocol (start, status,
  cancel, acknowledge, ledger, settle), app mode, opening-book status, the
  saved-filters file relay, shutdown. Thin pass-throughs to the services (see
  Public API).
- **Local-mode services** — `Services/`: `LocalFolderProcessor` (the four
  output pathways over a folder), its destination seam `IOutputDestination`
  (`FileSystemDestination`, which classifies its own create refusal as
  `OutputFileExistsException`; `DestinationFailures.cs` holds the run-level
  destination failures — an existing XGP file, an unfinished one, an
  unfinished output file, and a cancellation that left one — and their
  shared wording) and `RunRecord` (one run's record:
  what it skipped and, for XGP, what it committed), `ServerInstance` (this
  process's instance) and `JobStore` (one instance's jobs, start
  dispositions and XGP ledger — see "Recovery"), `OpeningBookProvider`
  (resolves and loads the book), `FilterDocumentStore` (the named-file IO
  behind the relay) and `AppModeService` (the configured mode).
- **Static assets** — `wwwroot/`: `app.css` (the busy-cursor rule, the app's
  only bespoke CSS), `app.js`, and vendored Bootstrap.

**`ExtractFromXgToCsv.Client/`** — the Blazor WebAssembly app: all UI in both
modes, and all processing in Web mode. `Program.cs` registers the
`HttpClient` and calls `AddClientServices`
(`ClientServiceCollectionExtensions`), the one registration of what the
components are rendered with — the test suite calls it too. Three areas:

- **Components** — `Pages/Home.razor` (the shell, the `FilterSurface` host
  and the source reporter), the two mode panels, `LocalModePanel` and
  `WebModePanel`, `SkipListNotice`, the one rendering of a skip record, and
  `XgpNumberingHints`, the hint both panels give while the numbering record
  cannot be read (see Components).
- **Services** — the Web-mode pipeline and the client's own state:
  `WebSession` (the panel's selection, conversion cache, book and last
  download, and the ownership rules over them) with its types
  (`WebSelection` and `DecodedSource`; `SourceConversion` with the
  `ISourceConverter` seam and its application implementation
  `XgSourceConverter`; `WebDownload`), `XgProcessingService` (the output
  builders — CSV, Diagram JSON, the XGP archive — and the book loader),
  `FilteredRowCache` (retained rows and records and their filtered
  projections),
  `HttpDocumentStorage` (the storage seam over the server's file relay),
  `SettingsStorage` (this app's settings keys over BgUiPrimitives'
  `BrowserStorage`, reporting every refusal) and `BrowserStorageCondition`
  (this app's one storage condition and its notice's dismissal; the filter
  surface's registered refusal sink). And Local mode's side of the recovery
  protocol (see "Recovery"): `ILocalJobs` and its `LocalJobsClient` (the
  protocol's wire), `LocalReconciler` (Home's reconciliation with the
  server, and the `LedgerTaking` it offers the controls),
  `XgpNumberingRecord`, `XgpNumberingStore` (with `NumberingRead`, a read's
  four outcomes) and `XgpNumberingKeeper` (Home's numbering record: its
  value, its one document, and its one owner's writes), `XgpEditClock`
  (what counts as an edit, and whether the controls follow a taking),
  `LocalJobTiming` and `IJobIdentities` (seams for a test's waits and
  identities).
- **Shared** — the wire types the server references too (`ProcessRequest`,
  `ProcessingProgress` with `SkipRecord`, `SkippedItem` and
  `XgpExportCommit`, `JobPhase`, `OutputFormat` and `OutputFormatNames`,
  `AppModeResponse`, `OpeningBookStatus`, the strict enum converter; the
  protocol's `StartRequest`, `StartResponse`, `ProtocolRefusal`,
  `LedgerMark`, `LedgerEntry`, `LedgerSnapshot` and the settle types; and the
  Local panel's `LocalStartState` and `OwedXgpJob`), the
  `.xgp` naming engine (`XgpExportOptions`, `XgpNameAllocator` and its
  internal parts) with the name preview's sample (`XgpNamePreview`),
  `XgpPayload` (both modes' one statement of how a decision becomes an
  `.xgp` payload), and `AppRanking`, the application's one ranking, which
  both modes read. See Client-shared types.

**`ExtractFromXgToCsv.Tests/`** — xUnit, one class per contract: direct tests
of the services and the naming engine, bUnit tests of `Home` and both panels,
hosted `WebApplicationFactory` pins for the file relay, and processor runs
over real files. Records and rows a test states come from the producer's
`TestRecords`. Shared support: `bUnitTestHelpers` (private-field access, stub
HTTP handlers, and a Local run to completion), `HomeTesting` (Home's
registration, render and gestures, and `HomeStoragePlan` — Home's storage
stated on a `BrowserStoragePlan`), `NoticeTesting` (a dismissal through
BgUiPrimitives' `NoticeBox`, settled before its outcome is read, and the
bound on awaiting an operation), `CapturingLogger<T>` and `ThrowingLogger<T>`
(a logger every entry of which fails), `FixtureHelper`, `MalformedXg`
(the synthesized unreadable input skip tests write), `XgpAnonymizeAssert`,
`WebSources` (a fixture decoded and converted as Web mode holds it),
`ScriptedConverter` (the real Web-mode converter wrapped to count, fail or
hold conversions), `ScriptedDestination` (real output files whose create,
flush, write, close or removal a test can fail, and which records every
removal asked of it) and `LocalProtocolTesting`
(the real Local server in memory, which can restart as a new instance over
the same folders and run a job straight against itself, behind a
`ProtocolWire` that can drop a response, fail a request, hold a request —
delivered even if its sender stops waiting — hold a response, or answer a
status itself; a `FileGate` that holds a run as it opens its next output
file, over the file system or another destination; `MemoryBrowserStorage`;
`LocalPage` — one page of the app over both, so a reload, disposing the
first page, is another page — and `AnswerHeldJobs`, a page's jobs client
holding its first start's answer after it has arrived).
Fixtures are the umbrella's `TestData/FixtureFiles`,
linked into the build output rather than kept in this repo (see Test project,
and the fixture pitfall).

## Architecture

### Server host — thin by design

The server project is a launcher plus an HTTP surface for Local mode. It does
not parse .xg files for Web/Azure mode. In Local mode it runs
`LocalFolderProcessor` on a background task and exposes job status; in Web
mode it serves the WASM payload and nothing else.

`AppMode` is configured in `appsettings.json` (`"Local"` or `"Web"`). Local
mode registers `ServerInstance` and `JobStore` (singletons; the instance is
constructed as the process starts) and `LocalFolderProcessor` (scoped)
inside a mode guard so Web deployments don't carry server-side processing
dependencies they can't use.

### Client WASM — owns processing in Web mode

All rendering is `InteractiveWebAssembly` with `prerender:false`. In Web mode
the browser does the whole pipeline: read file, iterate decisions, apply
filters, emit CSV or Diagram JSON.

PPTX and PDF are **not** offered in Web mode — `BackgammonDiagram_Lib`'s
deck paths go through `SkiaSharp` to rasterize SVG into the slide/page PNG,
and Skia's native binary isn't available under Blazor WASM. The PPTX and
PDF radios are disabled client-side when `AppMode != "Local"`. A
persisted `xg_outputFormat` of `Pptx` or `Pdf` is sanitized to `Csv` on
load. (See Pitfalls.)

How Web mode reads, converts and exports — once per file, once per key, and
the records only when Diagram JSON needs them — is the next section's.

### Reading, converting, skipping and committing

The umbrella's `../SPEC-notices.md` §7 is the ratified model (v1.12.2 Arc 3,
halheinrich/backgammon#374): what a skip is, who owns each skip record and
how long it lasts, Web mode's two lists and conversion states, the ownership
of late completions, readiness, the commit boundaries and the XGP
numbering. This section says how the code meets it, not what it rules.

- **One entry, one record, one rendering, both modes** (§7.1). A skipped unit
  is a `SkippedItem` — a file, or a file and its `DecisionId` — in a
  `SkipRecord`, the immutable record type both modes keep; `SkipListNotice`
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
  midway contributes nothing), and the commit — the CSV writer, the JSON
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
- **Web mode** (`WebSession`, §7.3). A pick reads each upload once
  (`XgSourceReader.ReadAsync`, format `Xg`) into a `WebSelection` of every
  entry with its read outcome, and converts the decoded files to rows under
  the book in force. Each decoded file is retained (`DecodedSource`), with
  its exact bytes only where an export copies it verbatim — which the rows
  the producer stamped decide (`XgpPayload.CopiesSourceVerbatim`); a file
  whose rows failed keeps them, since a later book's rows might need them.
  Conversion results are cached per selection and key (rows or records, plus
  the book; the ranking is fixed), each file within its own boundary, in the
  states `ConversionState` names. Rows convert on a pick and on a book
  change; records only when Diagram JSON is selected
  (halheinrich/backgammon#74). Refiltering reads only what is retained
  (`FilteredRowCache`). Every asynchronous completion carries a ticket and
  publishes only while its owner says so — its failure included: a
  conversion's failure carries the selection, representation and book it
  converted for (`ConversionFailedException`, judged by `WebSession.Owns`
  against the keys the selected output needs — rows always, records for
  Diagram JSON — the one definition, `NeededBy`, that scheduling reads
  too), whichever gesture asked for it, and a superseded failure, or one
  for a key the selected output no longer needs, is logged, never the run
  error. Recording what a delivered XGP archive committed comes after the
  attempt's failure handling: its failure is the run error, and the download
  stays completed. "This
  selection" (`SelectionSkips`) and "the last download" (`WebDownload`, its
  label naming the attempt's format and ending) are the two lists. A
  download attempt begins at the click, before the busy wrapper's yield: the
  session's `BeginDownload(representation)` refuses a representation not
  converted, and its ticket captures the filtered projection the attempt
  builds from; the gesture then ends, as every gesture does, by converting
  what the selected output now needs. The XGP archive
  (`XgProcessingService.BuildXgpArchive`) is built from the attempt's
  captured decisions, skips a decision whose payload cannot be prepared,
  and commits at the finalized archive's hand-off to the browser.
- **XGP numbering, both modes** (§7.1, §7.5). Only committed files consume
  numbers, and the numbering lives in Home's numbering record — one storage
  document, with the mark and the owed jobs. Web mode hands Home
  `OnXgpExported(XgpExportCommit)` at a successful hand-off; Local mode's
  accounting reaches the record through the server's XGP ledger (see
  "Recovery"). Either way the record takes it in one checkpoint, and the
  controls follow the record unless the user edited since the export
  started (see "XGP export").

### Recovery: a Local XGP run's accounting survives losing the job

The umbrella's `../SPEC-notices.md` §7.5, as amended at the leg's second
review and clarified at its fourth (umbrella `3b7656d`: only a job from an
earlier instance holds an XGP start back; the third and fourth reviews
otherwise corrected the code, not the section), is the
ratified protocol (halheinrich/backgammon#397): what each
owner holds and for how long, how a start is judged and admitted and what
a refusal does to a disposition, how a page follows a job and finds it
again, a pass's authority over another instance's mark, owed jobs learned
from storage, a start refused on every attempt, Stop before the start is
answered, how accounting is applied before acknowledgement, what counts as
an edit, why a replay never rolls back, what a job lost with its instance
and a record that cannot be read each block, and why Local XGP files are
never overwritten. This section says where the code holds each part, not
what it rules.

- **The server** (`Services/`). `ServerInstance` is minted as the process
  starts. `JobStore` holds, under one lock, the jobs (each under the
  identity the browser minted, kept until acknowledged — a status read never
  removes one; a job's state is the store's private `HeldJob`, while its
  runner holds an immutable `JobEntry`), the start dispositions (`Admitted`
  holding the request, or `Retired`; kept for the instance's lifetime) and
  the XGP ledger (one `LedgerEntry` per admitted XGP job, ended in the same
  step as its terminal result). `Start` judges the instance, then the
  identity, then — for XGP — admission (`AdmissionRefusal`), creating job,
  disposition and entry in one step; a refusal at admission retires the
  identity, and no refusal changes a disposition already held. `Settle`
  retires an unseen identity or answers admitted; `Acknowledge` releases
  only the result. A job's snapshot (`ProcessingProgress`) is immutable, so
  the store keeps the one reported, serves it to every status read and
  hands it to a terminal result's builder as it is. `ProcessController`
  carries these over the wire (see Public API); a failed job's terminal
  result is recorded before it is logged, and a stopped run's close failure
  is handed to its terminal result.
- **The browser's wire** (`ILocalJobs`, implemented by `LocalJobsClient`):
  one typed call per endpoint; it retries nothing. Its cancel tells
  delivered, refused (404) and failed (any other answer, or none) apart. A
  service behind an interface, as the Web conversion is, so a test can hold
  an answer at the point the page takes it.
- **The numbering record** (Home's, `XgpNumberingRecord` /
  `XgpNumberingStore` / `XgpNumberingKeeper`): the document
  `xg_xgpNumbering` holds the numbering, the mark (`LedgerMark`) and the owed
  jobs (`OwedXgpJob`).
  - A read has §7.5's four outcomes (`NumberingRead`). An unreadable
    document is kept (`XgpNumberingKeeper.IsUnreadable`): migration runs
    only over a missing record, and nothing writes over it, nor applies an
    entry, while the page knows it is stored — a refused read cannot show
    it replaced — until the user's resolution (`ResolveUnreadableAsync`)
    authorizes replacing that document, and only that one, with a fresh
    record. The replacement is written only once a fresh read shows that
    document, or nothing; a read showing another page's record takes it and
    writes nothing. The authorization ends once the fresh record is stored,
    or once a read shows anything else.
  - Every write is a checkpoint — a fresh read, merged, the change applied,
    one write — and only from a fresh read that showed what is stored: under
    a refused read nothing is written, and the change stands in the page, as
    a refused write's does. The merge compares the stored record with what
    this page last saw stored: unchanged, this page's record stands, pending
    changes and all; changed, its numbering and mark are taken together, by
    number within an instance and outright across instances, which no mark
    orders (`LedgerMark.Follows`).
  - The keeper runs one operation at a time (a `SemaphoreSlim` held from an
    operation's first read to its last write), so a pass's guard compares
    the live record and no write marks as seen a job another operation has
    just recorded.
  - Owed jobs merge as the stored ones and those this page recorded and has
    not yet seen stored, less those it resolved.
  - The three keys it replaced, and the legacy prefix before them, migrate
    once, every one removed once the document is stored.
- **Reconciliation** (`LocalReconciler`, owned by Home, which starts it once
  its restore has resolved Local mode). A pass reads the record first
  (`BeginPassAsync`, whose `PassStart` bounds the pass's authority), then
  the ledger. It applies each ended entry the record does not hold
  (`XgpNumberingRecord.Holds`) through the keeper's `ApplyAsync`: an entry
  of another instance only from the state the pass started with, both
  halves unchanged (`Applying`'s `underPassAuthority`), and otherwise
  nothing is written and the pass reconciles again from the changed state.
  When the state the pass started from changed under it, it shows
  `Checking` until it has reconciled again. It follows a running entry, and
  settles an owed job of this instance with no entry (retired resolves it,
  admitted keeps it owed). The controls are offered each taking — a
  `LedgerTaking`, with the numbering the record held at it, which is what
  they follow (Home's `FollowRecordAfterPassAsync`), never the record's
  numbering as it stands then. An `Ordered` taking is the entries of the
  pass's ledger the record's mark has moved past since the last offer, by
  an entry applied or by another page's accounting taken from storage, as
  far as that ledger shows them ended; its `Export` is the last of them
  that committed something, and its numbering that export's. An
  `Unordered` taking is a mark of an instance the pass's ledger does not
  show — another page's record taken across a restart — with the numbering
  the record took with it. A pass offers what the record took from storage
  before it applies anything, again after each entry it applies, and at
  its end. The offer moves only once the controls have followed, so a
  follow that throws leaves the taking to be offered again; where the
  record holds entries the ledger does not yet show, the pass publishes
  `Checking` and reconciles again after `PollInterval`. The event is the
  mark moving, never a change in the numbering's value, so a replay offers
  nothing and a new export offers itself though its numbering equals the
  record's. A result the panel's follower received goes through a pass
  (`ApplyEndedAsync`). Recording an XGP job as owed (`RecordOwedAsync`)
  answers whether its start may be sent — the record's half of admission
  (`HoldsBack`, §7.5 as clarified at umbrella `3b7656d`): not while the
  state is unknown, the record unreadable, or a job owed of an instance
  other than the one the page reconciled with; then the job is resolved
  as never sent. A job of the current instance, this page's or another's,
  holds nothing back: admission answers for it. A pass that fails for any
  other reason than the
  server's absence publishes `Checking` and schedules its retry before it
  logs, and a failure to log stops neither. Its `State` (`LocalStartState`: the instance,
  the mark, and why no XGP start may be sent — `Checking`, `Running`,
  `LostExport` or `UnreadableRecord`) is what the panel's Run gate reads;
  its `LostJobs` are the owed jobs of earlier instances Home's gate reason
  names. While the record cannot be read, a pass learns the instance and
  applies nothing. It retries every `LocalJobTiming.RetryDelay` while it
  cannot reach the server, and follows every `PollInterval` while an entry
  runs, an owed job is unresolved, or the offer has not reached the
  record's mark.
- **The panel's run** (`LocalModePanel.RunLocalAsync`), past its gate
  (`RunReady`, the one definition the Run button and its click share): it
  mints the identity (`IJobIdentities`), has Home record an XGP job as owed
  and answer whether its start may be sent (`OnXgpJobStarting`, a
  `Func<OwedXgpJob, Task<bool>>`; held back, nothing is sent and the page
  reconciles), sends the start — retried under the same identity until
  answered, noting whether every attempt was answered — follows the job's
  status with retries (a persistent failure is the run error, and following
  goes on), tells Home the job ended (`OnXgpJobEnded`) and only then
  acknowledges it. A start refused on every attempt resolves its job
  (`OnXgpJobNeverStarted`); one with an unanswered attempt stays owed.
- **Stop** is the run's own: while the job is not known, the Stop button
  cancels the run's source, which ends the start's sending; an answer that
  admits the job after that has the Stop delivered before the job is
  followed. Stop before the answer settles the identity (`SettleAsync`):
  retired, nothing ran; admitted, Stop cancels and the run follows the job
  to its stopped result; refused as another instance's, the run error says
  the server no longer has the run, and the page reconciles — in every
  format, as for a job the server no longer holds. Each cancel's answer is
  its run's: the panel holds the job the run follows as a `JobInHand`, one
  per run and compared by reference, and applies an answer only while that
  job is in hand; once a cancel for it is confirmed delivered, no cancel's
  failure undoes that, even one for a request sent later. A cancel refused
  or not confirmed, with none delivered, is the run error, and following
  goes on. What the run error shows of a run in hand is made from two facts
  (`ShowInFlight`): a Stop failure standing — through a persistent failure
  and contact returning, until a Stop is delivered, the run ends or the
  user closes it — or else the persistent-failure notice while it is shown.
  That notice's state in the current failure streak (`StillTryingNotice`)
  is `None`, `Suppressed` (due, with a Stop failure in its place), `Shown`
  or `Dismissed`. It is due at every failure at or past the threshold; a
  Stop delivered over it shows it at once; a Stop failure the user closes
  over it leaves it due at the next failure; once closed it stays closed
  for the rest of its streak; and contact returning ends the streak. A
  refused start, or a job
  the server no longer holds, ends the run with the run error and asks Home
  to reconcile (`OnReconcileNeeded`). What it shows still follows §7.2: a
  job recovered after a reload contributes its accounting and nothing else.
- **What counts as an edit** (`XgpEditClock`, Home's): a clock of the
  page's own events — never wall-clock time — notes each user edit of the
  next number or the pattern, each export's start (a Local job as it is
  recorded owed, a Web download as the Web panel raises
  `OnXgpExportStarting`) and each time the page sets the controls itself.
  The controls keep an edit made since the export started (`Follows`,
  `FollowsWebDownload`). For an export this page did not start, the start
  is bounded below only by the latest entry this page started that the same
  ledger admitted before it, or by the moment the page last set the
  controls, if later; a refused request, or another instance's, orders
  nothing. An `Unordered` taking is ordered by nothing at all, so only the
  moment the page last set the controls bounds it: it is followed only if
  the user has not edited since. At each taking the clock forgets the
  starts no later taking can need (`Prune`: it keeps the jobs still owed
  and the latest admitted one not owed), and a job that never ran is
  forgotten at once.

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

- **Local mode** — the composition root (`Program.cs`) hands
  `AppRanking.Ranking` to `LocalFolderProcessor`'s constructor, where the
  ranking is a required argument (a defined value, or
  `ArgumentOutOfRangeException`): the processor states no ranking of its own.
  It builds each run's `FilteredDecisionIterator` for that ranking (with the
  loaded book), which builds every row for it and filters every record
  through its view for it; the deck draws each record through
  `DiagramRequest.ForDecision(record, _ranking)`.
- **Web mode** — `WebModePanel` builds its `WebSession` for the value, whose
  `FilteredRowCache` is built for it, and the session converts for the
  cache's ranking: `XgSourceConverter` (which takes the ranking as a required
  argument, as the producers do) builds the rows for the ranking the cache
  filters the records under. The cache refuses a row built for another
  ranking.
- **The name preview** — its sample row is `DecisionRow.From(record,
  AppRanking.Ranking)`.

**What fails if a producer default stands in.** Of the producer operations
above, `ViewFor`, `DecisionRow.From`, `DiagramRequest.ForDecision` and the
`FilteredDecisionIterator` constructor require a ranking, so the compiler
holds them. `XgIteratorOptions`, which `XgSourceConverter` builds, does not:
its ranking defaults to `Equity`, which is also this app's choice, so a run
under `Equity` cannot tell the app's ranking from the default. The pins
therefore run each mode under `DepthFirst`, over a fixture and filter
(`MatchTest.xg`, "erred by at least 0.001") that each test first proves, from
the producer's own read, admits different decisions under the two rankings:

- `LocalFolderProcessorRankingTests` — one test per pathway, each against the
  producer's reference for `DepthFirst`: each fails if the run's filtering
  operation is built for any ranking but the processor's.
- `WebModeRankingTests` — the conversions the session runs
  (`XgSourceConverter`'s two, for the cache's ranking, then
  `FilteredRowCache`) under `DepthFirst`. If the converter's options dropped
  the ranking, the rows would be built for `Equity` and the cache would
  refuse them.

Beside those, `FilteredRowCacheTests` pins the cache's own rule over
hand-built rows under either ranking, and `WebModePanelFilteringTests` pins
that the panel builds its cache for `AppRanking.Ranking`.

### XGP export

Selecting the **XGP positions** output format emits the filtered decisions
as per-decision `.xgp` position files — available in both modes. In Web
mode the download is one `.zip` with a `.xgp` entry per prepared decision,
built by `XgProcessingService.BuildXgpArchive(decisions, options, filters,
anonymize, skipped)` from the selection's retained decoded files: the
attempt's ticket hands it each filtered row paired with its file, captured
when the attempt began (`WebSession.BeginDownload`), and the panel never
touches an `XgFile`. In Local mode `LocalFolderProcessor.ProcessXgpAsync`
writes the files server-side into the output **folder** (`OutputPath` is a
directory for this format, created if absent), each **created new**: an
existing file is never overwritten — the decision's file takes the next
free name, ` (2)`, ` (3)`, …, and the run carries on (`SPEC-notices.md`
§7.5; see "Reading, converting, skipping and committing"). Per decision,
both pathways
prepare the payload through one statement of the routing rule,
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
  `Create(options, filters)` (the single options-validation throw point for
  both pathways). Appends `.xgp` and uniquifies duplicate rendered names
  Windows-style (`name.xgp`, `name (2).xgp`, …; case-insensitive
  bookkeeping). Local mode uses the `Peek`/`Commit` split, committing a slot
  once its file is written; Web mode takes a name with `Next` once a payload
  is prepared, provisionally — the archive commits at its hand-off.
  `CommittedCount` is the numbers a run has used.

The default pattern `pos{n}` names files byte-identically to the pre-pattern
`{prefix}{number:D{suffixLength}}.xgp` rule; `StartNumber`/`SuffixLength`
govern `{n}`. `Home` owns the option state and its persistence: the
numbering record's numbering (pattern, last number, length — the document
`xg_xgpNumbering`, see "Recovery"), from which the "next number" field
defaults to the last number + 1 while the pattern matches the recorded
pattern (else 1). The keys it replaced (`xg_xgpPattern`, `xg_xgpLastNumber`,
`xg_xgpSuffixLength`) migrate once into the document, as does the legacy
`xg_xgpPrefix` before them (`{prefix}{n}`, falling back to `pos{n}` if the
prefix breaks the grammar); every one of them is removed once the document
is stored, the prefix too where a pattern superseded it. A document that
cannot be read is never migrated over (see "Recovery").

**The committed result** (`SPEC-notices.md` §7.1, §7.5). An export's
accounting is one `XgpExportCommit`: the options it captured when it started
and the files it committed (`NextNumber` derived). Local mode: the
processor's `RunRecord` holds it from the moment the options validate and
replaces it as each file is written and closed; every snapshot carries it
(`ProcessingProgress.XgpCommit`), the job runner's cancelled and failed
results included, and the job's XGP ledger entry ends with it in the same
step. The panel tells Home the job ended (`OnXgpJobEnded`) for every
ending, whatever it shows, and a pass applies its entry from the ledger, as
it does for a page that lost the job (see "Recovery"). Web mode: the
archive's commit is handed on only after its hand-off succeeds
(`OnXgpExported`); a failed hand-off advances nothing. Either way Home
records the numbering from the commit's options — never from its controls —
in one checkpoint, and the next-number control follows the record's taking
of the export, with that taking's own numbering (`Follow`; for a Web
download, the numbering `XgpNumberingKeeper.RecordExportAsync` returns as
the one it took) — the event, though the numbering's value may not
change — unless the user has edited the next number or the pattern
since the export started (see "Recovery": what counts as an edit). A
length change is no such edit — it says nothing about which number comes
next. A commit of nothing changes no number, the record's own rule
(`XgpNumberingRecord.WithCommit`, `XgpExportCommit.CommittedNothing`), so
the record takes nothing and nothing follows it. The next number is stated
once, by the numbering (`XgpNumbering.NextNumber`).

### Components

- **`Home.razor`** — shell, the `FilterSurface` host, and the reporter of
  the filter source. Detects the app mode, owns the output format, the XGP
  options and the Local folder, renders the output-format radio and the
  producer composite, and delegates to `LocalModePanel` or `WebModePanel`.
  Its filter half is XgFilter_Razor's host contract, and nothing else (see
  "Filter setup"):
  - **It reports the source** to the app-scoped `FilterSetup` where it
    latches one: the restore resolving the mode and the folder, a Local
    folder commit, a Web selection change. `Source` is the one property
    reports and reads both use.
  - **It reads the filter in effect** from the owner, once, in
    `FilterInEffect` (`Source is { } s ? FilterSetup.Current.ConfigInEffectFor(s) : null`)
    — the one filter fact both mode panels are handed. Home keeps no copy of
    a config: there is no last-committed field, and the XGP preview reads
    the filter in effect (or the empty one).
  - **It re-renders off notifications**, not off component events: it
    attaches to `FilterSetup` (a commit, an edit, a restoration) and to
    `BrowserStorageCondition` (a refusal from anywhere) in
    `OnInitialized`, and disposes both attachments.
  - **The saved-filters seam.** One `HttpDocumentStorage` instance,
    constructed over a delegate reading the latched path (the composite
    rebuilds its store when the bound `Storage` *reference* changes, so the
    live folder rides the delegate). Bound in Local mode while a folder is
    latched; null otherwise — a blank path renders no saved-filters
    section, never a load failure — and always null in Web mode (ruled: a
    second store is forbidden drift; no localStorage fallback). It is the
    only parameter Home binds on `FilterSurface`.
  - **Source identity.** Local mode's source is the input folder path:
    `_folderPathText` follows every keystroke (and persists under
    `xg_folderPath`), while the separate `_localSourcePath` latches only at
    the input's `@onchange` boundary — and once at the restore, which is a
    committed value. The token is minted from the latch only (`FromPath`,
    handed the path in the spelling Home holds), never from the live text —
    a half-typed path is not a source. **Path identity is the token's, not
    this host's** (halheinrich/backgammon#94): `FromPath` folds case and
    makes trailing separators insignificant, so Home pre-folds nothing and
    the latched path keeps the user's spelling for IO and display. Web
    mode's source is the file selection, whose token `WebModePanel` mints
    and hands up (see below). The output path is **not** a source
    (umbrella-ratified).
  - **The mode panel waits for the mode.** No mode panel renders until the
    restore has resolved the mode: the panels are where a source is latched,
    and the mode decides which source there is. Rendering Web's panel while
    `/api/appmode` is unanswered let a pick land in a Local app, and read
    and wrote the Web panel's settings on every Local visit.
  - **Its own settings** — the output format, the folder path and the
    anonymize flag, and the numbering record (one document holding the
    numbering, the mark and the owed jobs, which replaced the pattern,
    counter and length keys and migrates them once) — go through
    `SettingsStorage` (see "Browser storage"). The restore runs to its end
    whatever storage answers: each key lands on its stored value or its own
    documented default.
  - **It is the numbering record's one owner** (`XgpNumberingKeeper`), and in
    Local mode it owns the page's reconciliation with the server
    (`LocalReconciler`, started once the restore has resolved Local mode):
    it hands the Local panel the reconciliation's `State` and binds its
    hand-offs (`OnXgpJobStartingAsync` noting the job's start before
    `RecordOwedAsync` and answering whether the start may be sent,
    `ApplyEndedAsync`, `OnXgpJobNeverStartedAsync` forgetting the start
    before `ResolveNeverStartedAsync`, `ReconcileAsync`). While XGP is
    selected it renders the **lost-export gate reason** for each job owed
    from an earlier instance, and, in either mode, the **unreadable-record
    gate reason** while the record cannot be read — in Local mode with a
    sentence more, about the output folder (see "Recovery" and "Notices").
    Its controls follow the record's takings by the edit rule
    (`XgpEditClock`, `Follow`; see "Recovery").
  - **The storage condition's notice**, `#storageUnavailableNotice`, rendered
    from `BrowserStorageCondition` (see "Notices").
- **`LocalModePanel.razor`** — folder/output-path inputs, Run/Stop/Exit
  buttons, the run's start and following, progress bar (determinate, plus
  the two indeterminate states in "Busy affordance"). Parameters:
  `OutputFormat`, `Source` (the token Home reports), `FilterInEffect` (the
  filter in effect for it, or null — the whole filter half of the Run gate,
  and the config the run sends), the folder input's controlled-component
  trio (`FolderPath`, `FolderPathChanged` per keystroke,
  `OnFolderPathCommitted` at the change boundary — Home owns the value; all
  three `[EditorRequired]`, they are the source report's wiring), the XGP
  members, and the recovery protocol's five (all `[EditorRequired]`):
  `StartState` (Home's reconciliation's `LocalStartState` — the Run gate's
  server half, and the hint saying why it waits), `OnXgpJobStarting` (a
  delegate, since the panel needs its answer), `OnXgpJobEnded`,
  `OnXgpJobNeverStarted` and `OnReconcileNeeded` (see "Recovery"). The Run
  button and its click share one gate (`RunReady`). A stopped run's close
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
  bookkeeping (Home's checkpoint) is pending. That bookkeeping is the job's
  own: Home is told the job ended for every ending — completed, cancelled
  or failed — whatever is on screen, and applies its entry from the ledger
  (see "XGP export"). Watching "a
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
- **`WebModePanel.razor`** — file picker, preview table, download; every slow
  gesture runs through `RunBusyAsync` (see "Busy affordance"). Parameters:
  `OutputFormat`, `FilterInEffect` (as Local's), `OnSelectionChanged`
  (`[EditorRequired]`, an `EventCallback<FilterSourceToken?>`: raised once
  per file-selection gesture — every `HandleFileSelectionAsync` invocation,
  since each one replaces or clears the retained selection — with the new
  selection's token, or null when the gesture left nothing selected), and
  the XGP members — among them `XgpNumberingUnreadable`, which holds an XGP
  download back while Home's record cannot be read, and
  `OnXgpExportStarting`, raised as an XGP attempt begins so Home can tell
  the user's later edits (both `[EditorRequired]`; see "Recovery"). Its
  settings — the download file name and the folder
  hint — go through `SettingsStorage`; the hint's write never stands between
  the user and their files (see "Browser storage").
  **The selection, and its token, are the panel's.** The panel's
  `WebSession` (Client `Services/`) holds the selection, its conversions,
  the book in force, the projections and the last download, and dies with
  the panel; an operation still running when the panel goes is cancelled by
  the panel's lifetime and publishes nothing. The token is minted when a
  gesture selects files, held by the selection, and gone with it. Two
  selections never share a token, also across panel instances — the filter
  setup outlives every page, so a page-lived counter would mint an earlier
  pick's token again after a navigation, and a new pick would inherit that
  pick's consent. The counter behind `FromGeneration` is therefore
  process-wide (one per loaded app), and only moves forward.
  `XgProcessingService` keeps no selection.
  **Gestures run through the session** (see "Reading, converting, skipping
  and committing"): a pick begins by ending the old selection, reads and
  converts rows, then converts what the selected output needs; a book pick
  publishes the book and reconverts rows under it (and records, when Diagram
  JSON is selected); a format switch to Diagram JSON converts the records
  the first time (`OnParametersSetAsync`, "Converting decisions for Diagram
  JSON…"), and each gesture ends by converting what the format selected then
  needs (`ConvertWhatTheSelectedOutputNeedsAsync`) — a download included,
  once its attempt has ended. A download attempt begins at the click, before
  the busy wrapper's yield (`BeginAttempt`), and only past the gate the
  Download button and its click share (`DownloadReady`): its format, filter,
  options, file name and the projection it builds from are captured then,
  and it begins only from a converted representation. An XGP attempt raises
  `OnXgpExportStarting` to Home as it begins. A file that cannot be read is
  no run error: it is named in "this selection". `_runError` is for a
  gesture that itself failed — a download that could not be built or handed
  over, or a selection that could not be taken — and only while its owner
  still owns it (`GuardedAsync`: a conversion's failure by its selection and
  book while its key is one the selected output needs, `WebSession.Owns`; anything
  else by its gesture; a download's by its record): a superseded failure is
  logged instead. A failure recording a
  delivered archive's numbering is the host's, after the attempt: it is
  shown, and the download stays completed. The session's projections
  are a `FilteredRowCache`; the panel's own
  filtering logic is one gate: `OnParametersSetAsync` calls
  `Refilter(FilterInEffect)` only while a filter is in effect. The cache is
  built for the run's one ranking (see "The ranking"): it accepts only rows
  built for it and filters each record through its view for it, built once
  when the records arrive. It builds the filter via `FilterConfig.Build()`
  once per config **value** — the owner's `ConfigInEffectFor` returns a new
  instance on every call, so a reference key would rebuild on every render —
  and keeps its own copy as the key; `ReplaceRows` and `ReplaceRecords`
  re-project each set through the filter already in effect, each leaving the
  other alone. Every list it hands out is an immutable array, replaced whole,
  never a live list (the collection rider of halheinrich/backgammon#273).
  The panel exposes the cache (`RowCache`) and the session (`Session`)
  internally as wire-test seams.
  **Readiness and counts follow the selected output's representation**
  (§7.3): rows for CSV and XGP, records for Diagram JSON. The download block
  shows for any selection with a decoded file; Download, the match line ("N of
  M rows match current filters", or "… decisions …" for records) and the
  zero-match notice read the selected key's state, and a key not converted
  yet shows no count at all — never zero matches. The "N rows loaded" line
  counts the rows that converted. When a filter in effect excludes every
  converted item the panel renders an explicit zero-match notice in place of
  the match line, so a filtered-to-zero result reads as a result rather than
  a silent success. The "filters are active" signal is emergent, not a
  `FilterConfig` re-inspection: an empty (inactive) set passes everything, so
  zero survivors from a non-empty set can only mean the filter is non-empty.
  The XGP hint "No decisions match the filters" reads the same state, so rows
  not converted yet never show it. The preview table shows the rows an
  output is built from, so it is there only while the selected output is
  built from rows (CSV, XGP) and they are converted; Diagram JSON has none
  (halheinrich/backgammon#399 item 4). "This selection" renders above the
  run error and "the last download" below the download block, both through
  `SkipListNotice`; the last download's label (`WebDownload.Label`) names the
  attempt's format by its short name (`OutputFormatNames`) and a failed or
  stopped ending — "The last download (XGP)", "The last download (CSV,
  stopped)".

The Run/Download gate's filter term is `FilterInEffect is not null`, and
nothing else filter-shaped — one fact, one owner. Each panel adds its own
gates beside it (a folder and an output path; the selected representation
converted, within the size limit; valid XGP options); those stay the
panels'. A source change (a new
folder commit, a new file selection) ends the setup: an applied selection is
dropped and needs Apply again against the new source, while the empty
selection stays in effect without one.

### Filter setup

XgFilter_Razor's `FilterSetup` is the app-scoped owner of everything
`../SPEC-filtering.md` governs here — the draft, the applied baseline, this
boot's restoration and its two notices, and whether a filter is in effect —
and this host adopted it in v1.12.2 (halheinrich/backgammon#374, Arc 2). Its
"Host contract" is this host's contract; what follows is how Extract meets
it, not a restatement.

- **Registration.** `AddClientServices` registers `BrowserStorageCondition`
  scoped and calls `AddFilterSurface<BrowserStorageCondition>()`, so the
  condition is the sink the surface reports its refusals to, and the
  instance Home injects is the sink. Only the WebAssembly client renders the
  surface or injects the owner: Home renders `InteractiveWebAssembly` with
  `prerender: false`, and the server renders none of either.
- **The source is reported, never bound, and never guessed.** Home reports
  at the moments listed under Components. `ReportSource(null)` is a real
  answer — it ends an existing setup — so "not yet known" is never reported
  as null: until the restore has resolved the mode and the folder
  (`_sourceResolved`), Home reports nothing and renders no mode panel. A
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
- **Derived facts are keyed by their inputs.** Local's result by its run's
  source and filter values, and its run record by its run's source; Web's
  filter cache by the filter's value, and its conversions by selection and
  conversion key. Nothing compares configs by reference.

### Browser storage

Every storage call this app makes — Home's keys, both panels', and the filter
surface's own — goes through BgUiPrimitives_Razor's `BrowserStorage` (Hal's
ruling on halheinrich/backgammon#374; the library's "Browser storage
access"). No Extract code calls storage interop directly, and nothing
latches: a refusal disables nothing, and the next call is simply made.

- **`SettingsStorage`** is this app's access to its own keys in the local
  area. A read answers the stored string, or null for "nothing stored" and
  "refused" alike — every caller already reads null as "use my default", and
  each keeps its own parsing and default — and a refused write returns
  normally, leaving the caller's in-memory value standing. Every refusal is
  reported to `BrowserStorageCondition` before the call returns, and the
  call still returns its result normally if logging the refusal fails (the
  condition records the occurrence first and contains its logger). Anything
  that is not the browser's refusal propagates.
- **`BrowserStorageCondition`** is the one storage condition, host-owned
  and app-scoped. Every refusal — `SettingsStorage`'s, and the filter
  surface's through the registered sink, also after the page that started
  the call has gone — is one occurrence per visit: the first begins it,
  later ones are the same occurrence, and a later success begins and ends
  nothing (a read served says nothing about writes; one key's success says
  nothing about another's). It records the occurrence and the notice's
  dismissal, never permission to stop calling. Home attaches to it, so a
  refusal from a panel or the surface shows on Home without a gesture of
  Home's, and a refusal while Home was absent is there on its return. A
  full reload constructs a fresh holder.
- **A refused write never stops the gesture that made it.** At v1.12.1 the
  Web panel awaited an unguarded write of its folder hint before reading the
  selected files, so a browser that refused storage failed every Web-mode
  upload; the panels' unguarded reads threw out of their first render too
  (halheinrich/backgammon#102, Extract's portion, closed here). Both now go
  through `SettingsStorage`.

The keys: Home's `xg_outputFormat`, `xg_folderPath`, `xg_xgpAnonymize` and
the numbering record `xg_xgpNumbering` (one JSON document — version,
numbering, mark, owed jobs — always written whole, from a fresh read, in one
write; `SPEC-notices.md` §7.5), with the keys it replaced read once — only
while no document is stored — to migrate, and every one removed once it is
stored (`xg_xgpPattern`, `xg_xgpLastNumber`, `xg_xgpSuffixLength`, and the
legacy `xg_xgpPrefix` before them); `LocalModePanel`'s `xg_outputPath`;
`WebModePanel`'s `xg_csvFileName` and `xg_lastFolderHint`. The filter
surface's keys are its own and are not named here.
`SettingsStorage.WriteAsync` answers whether the browser stored the value,
which the record's checkpoint and its migration need; most callers do not
ask. The record is also the one caller that tells a refused read from
nothing stored (`SettingsStorage.ReadEntryAsync`): its read has four
outcomes, and a refused one establishes nothing.

### Notices

The umbrella's `../SPEC-notices.md` is the ratified model
(halheinrich/backgammon#248); BgUiPrimitives_Razor's `Notice` is how a box
presents. This host decides only what each box **is**, because that — never
its colour — decides whether it dismisses, and, for a dismissible box, **who
holds the dismissal**. No `.razor` file here types alert markup.

| Box | What it is | Dismissible | Announced |
| --- | --- | --- | --- |
| `#storageUnavailableNotice` (Home) | condition notice | yes, per occurrence | assertive |
| the run error (both panels) | error | yes | assertive |
| `zero-match-notice` (both panels) | gate reason — the run's result | no | assertive |
| `skipped-notice` (both panels, `SkipListNotice`) | gate reason — the record of what was lost: Local's last run; Web's "this selection" and "the last download" | no — pinned after the caller's attributes | assertive |
| `lost-export-notice` (Home, XGP selected) | gate reason — a Local XGP export lost with an earlier server instance blocks XGP until the user resolves it, with its action | no | assertive |
| `unreadable-numbering-notice` (Home, XGP selected, both modes) | gate reason — a numbering record that cannot be read blocks XGP until the user resolves it, with its action; it suggests no number, and in Local mode adds a sentence about the output folder (Hal's wording, 2026-10-12) | no | assertive |
| the selected-size limit (Web) | gate reason — why nothing loaded and Download is dark | no | assertive |
| `busy-notice` (Web) | live indicator | no | polite |

**A dismissal belongs to one occurrence, lives as long as it, is never
stored, and has one holder — the occurrence's owner**, because only the owner
knows when a new occurrence begins and whether the old one outlives the page.
Every dismissible box here is caller-held (`@bind-Dismissed:get/set`), so the
component keeps no bit that could disagree with the owner's:

- **The run error — the panel's `_runError`.** One occurrence is one failed
  gesture: in Local mode a start the server refused (each reason says why),
  a job the server no longer holds, a Stop the server refused or never
  answered (following goes on), or — while it lasts — a start, a settle or
  a status read that keeps failing, which says the result hasn't arrived yet
  and comes down when contact returns while following goes on; in Web mode a
  download that could not be built or handed to the browser, or a selection
  or conversion that could not be taken, published only while its owner
  still owns it, and a failure recording a delivered archive's numbering (a
  file that cannot be read is a skip, not a run error).
  `_runError` (nullable) is
  non-null exactly while one stands, so it is the box's presence, its text
  and its dismissal at once: closing the box assigns null, exactly as the next
  gesture and (Local) inputs that no longer match the run's already do, and
  **there is no dismissed bit anywhere**. A second failure assigns the field again and shows
  fresh although its text may be identical — the text was never the
  occurrence. It replaced a `_hasError` flag beside a `_statusMessage` string:
  two fields for one fact, to which a dismissal would have added a third.
  The occurrence dies with the panel, so the panel is the right owner; it is
  bound rather than left component-held with an `OccurrenceKey` because a key
  would need a run counter — a new fact invented to name one the panel
  already holds. Local mode's `_progress.ErrorMessage` is a different fact
  (the server's report of a job that started and then failed) and is not a
  box.
- **Storage trouble — the app-scoped `BrowserStorageCondition`.** One
  occurrence per visit: it begins at the first refusal from any reporter —
  Home's settings, either panel's, or the filter surface's — and every later
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

**The selected-size limit is one fact with one box.** `HandleFileSelectionAsync`
used to write the over-limit refusal into the run error *as well as* letting
the gate reason by the picker render from `_totalSelectedBytes` — one fact in
two boxes, and under the model the second copy would have been a dismissible
statement of a gate reason. The handler now simply returns; the gate reason is
the refusal's only statement, which is also why it is **assertive**: it carried
no role before, and it is now the one thing telling the user that the
selection they just made was refused.

**The run errors carried no role at all** before the adoption — a failed
gesture nobody announced. They are assertive, per the model.

**The busy notice's layout is the panel's, on an element inside the notice.**
The box's children are the component's (its content wrapper, and a close
button on a dismissible box), so the `d-flex align-items-center gap-2` that
used to sit on the hand-written box now sits on a `div` of the panel's own
around the spinner and the text. On the box those classes would lay out the
component's structure and reach neither. Measured in a browser at the adoption:
box, spinner and text geometry are identical to the pre-adoption markup's. Its
old `aria-live="polite"` is gone — the box refuses it, and `status` already
implies it. `RunBusyAsync`'s paint contract is untouched: the notice is a
child rendered in the same batch as the panel.

### Busy affordance

Measured on a 266-file / 14.4 MB corpus (issue halheinrich/backgammon#53).
Web mode is the severe half: the WASM interpreter runs this pipeline ~30–80×
slower than native, so selecting five files freezes the tab for **3.8 s
cold** (~760 ms/file cold, ~240 ms/file once the jiterpreter warms), the
full corpus for **57 s**, a `.xgp` zip of 386 decisions for **6.6 s**, and a
6,515-row Diagram JSON for **15.6 s**. Apply Filter, by contrast, is 30–71
ms and deliberately gets nothing.

`WebModePanel` routes every slow gesture — file selection, opening-book pick,
the first switch to Diagram JSON (the records conversion), and all three
download formats — through one private `RunBusyAsync(message, body)`, which
raises the busy state and `_busyMessage`, renders, **yields**, runs the body,
and releases in a `finally` that covers everything after the depth is raised
— its own render request and yield as well as the body, so nothing that
throws strands the panel busy. The busy state is a depth count (`IsBusy`
while it is above zero), since a gesture can begin while another's await is
pending. It drives a single `busy-notice` box at the top of the panel (the
Download button's own "Building…" label is the local half of the same
state). The yield is load-bearing, not stylistic — see Pitfalls; a download
captures its attempt before it, at the click. `RunBusyForTest` and
`DownloadForTest` (a click that reaches the download past its gate) are the
test seams, siblings of `RowCache`.

Local mode's progress bar already covers the CSV, Diagram JSON and Xgp
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
pointer stayed a plain arrow. Both panels put an `is-busy` class on their
**root** element for exactly as long as their own busy state is up — `_busy`
in Local's panel, `IsBusy` in Web's — and `wwwroot/app.css` turns that one class into
`cursor: progress` for the root and everything under it. Two details are
load-bearing:

- **It rides the flag, not a window.** Local mode's pre-first-poll gap, its
  determinate stretch and the atomic render are all inside one `_busy`, as is
  every gesture `RunBusyAsync` wraps in Web mode — so all of them are covered by
  one rule, and a busy window added later is covered without a second one.
- **The rule's descendant half and its `!important` are not defensive.**
  Bootstrap dresses the pointer over exactly the controls the wait started from
  (`button:not(:disabled)`, `.form-control[type=file]:not(:disabled)…`), so
  inheritance from the root reaches nothing that matters — and those selectors
  outrank anything a single state class can raise its specificity to (0,1,1 and
  0,4,0 against 0,1,0). Winning over every component's own cursor, everywhere,
  for as long as the state holds, is what `!important` is for; a specificity
  arms race would be the worse answer here, not the purer one.

`progress` rather than `wait` across both panels: it is the keyword for
"working, but the interface still responds", which is Local mode exactly (Stop
and Exit stay live). Web mode's WASM gestures do block the thread and are
strictly `wait` — one keyword is the price of one discipline. `BusyCursorTests`
pins the state→class binding for both panels.

### Modes

#### Local (`"AppMode": "Local"`)

- Folder path + output path inputs. Output format: CSV, Diagram JSON, PPTX,
  or PDF; persisted to `localStorage` under key `xg_outputFormat`.
- Run → the page mints the job's identity → `POST /api/process/start`
  (a `StartRequest` naming the instance the page reconciled with, and for
  XGP its mark). The client reads `GET /api/process/{jobId}/status` every
  second until the terminal result, then acknowledges it (see "Recovery"
  and "Job lifecycle").
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
  before that, the run settles the identity (see "Recovery"). Exit →
  `ShutdownController`.

#### Web/Azure (`"AppMode": "Web"`)

- Browser file picker, 50 MB cap.
- WASM processes everything client-side: a pick reads each file once and
  converts it to rows; records convert when Diagram JSON is first selected
  (see "Reading, converting, skipping and committing").
- Download builds CSV, Diagram JSON or the XGP archive from what the session
  retains, for the format selected when the download starts.
- PPTX and PDF are unavailable (radios disabled) — see "Client WASM" above.

### Job lifecycle (Local mode)

`JobStore` is a singleton, one per server instance, holding under one lock
the jobs, the start dispositions and the XGP ledger (see "Recovery"; the
protocol is `SPEC-notices.md` §7.5). The runner holds a `JobEntry`, an
immutable handle: its identity (the browser's), its XGP ledger number and
the token its work runs under. The job's `ProcessingProgress` snapshot —
final once terminal, and immutable, so the store keeps and serves the one
reported as it is — and its `CancellationTokenSource` are the store's
private `HeldJob`, changed only under its lock. The client reads the status
once per second (the processor reports every tenth file, on every pathway).

**A status read never removes a job** (it once did, serving the terminal
snapshot exactly once, so one lost response lost the result). The job and
its result are kept until the page that started it acknowledges it
(`POST /acknowledge`, which releases the result and disposes the source, and
succeeds for a job the store no longer holds), or the instance ends. The
runner records each snapshot through the store — `Report` while the run
goes, `Complete` for its terminal result, which ends an XGP job's ledger
entry in the same step (a cancelled result is built from the last report in
that same step); the first terminal result stands. A failed job's terminal
result is recorded before the failure is logged, so a logger that throws
cannot leave its ledger entry running. A `POST /cancel` for a job that has
ended or is not held no-ops (404), and the client says Stop was refused. The
controller's actions are thin pass-throughs, so the lifecycle stays inside
`JobStore`. A job whose page never acknowledges it — a reload, a lost
acknowledgement — is kept for the instance's lifetime, as §7.5 rules.

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
app only *locates* the book and hands the loaded instance on — to each Local
run's `FilteredDecisionIterator` (the book alone), and to Web mode's
conversions through `XgIteratorOptions.OpeningBook`. Enrichment is strictly additive — it never
changes which decisions or candidates are emitted, only their depth labels and
levels — so a missing book degrades cleanly and never fails a run.

**Local mode (server).** `OpeningBookProvider` (singleton, Local-guard only)
resolves the path and loads the book once (~13.6 MB, immutable, concurrent-read
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

**Web mode (client).** The browser parses `.xg` files, so it loads the book too
— from an optional **Opening book (.ob)** `InputFile` beside the `.xg` picker.
`XgProcessingService.TryLoadOpeningBook(bytes, out book)` is the seam: the
producer's loader is path-based and WASM has no real path for picked bytes, so
it stages the image to an Emscripten MEMFS temp file, calls
`OpeningBook.TryLoad`, and deletes it — keeping components off both the path API
and the temp-file mechanics. `XgSourceConverter` takes the run's ranking and
the book in force and always states options, so the iterator's own default
ranking never stands in. The book is part of each conversion key: publishing
a different book (`WebSession.CompleteBookLoad`, owned by its load's ticket)
drops the old book's results and **reconverts** the selection's rows under the
new one, each file within its own boundary (halheinrich/backgammon#364), and
its records when Diagram JSON needs them — so a book chosen after the files
still enriches them, and pick order is irrelevant. A status line reports the
entry count (or an invalid-file error).

### Filter pipeline

`FilterConfig.Build()` (lib-owned, in `XgFilter_Lib.Filtering`) materializes
a `DecisionFilterSet` from the serializable DTO. The server calls
`request.Filters.Build()` in `ProcessController.Start`; Web mode's build
lives in `FilteredRowCache.Refilter`, driven by `WebModePanel`'s
`OnParametersSetAsync` gate. Same method, two call-sites —
see "FilterConfig materialization differs by mode" in Pitfalls. Local mode's
four pathways filter each decoded file through XgFilter_Lib's filtering
operation (`FilteredDecisionIterator.IterateFile` / `IterateFileDiagrams`,
the set's early-exit hints wired); Web mode refilters what it retains with
`DecisionFilterSet.Matches`. Both are that library's rules — "two
strategies, one rule set", which agree because early exit never changes what
passes — through `IDecisionFilterData`: one filter implementation, every
output.
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
derivation SSOT. Local mode's wire carries all six, pinned by
`LocalModePanelFilterWireTests`.

### Diagram JSON output

Emitted as a single in-memory JSON array, not NDJSON. Simpler consumer side;
streaming is deferred (see next steps) and will matter only for very large
corpora.

### Deck output (PPTX/PDF)

Local mode only. Filtered `BgDecisionData` is buffered in memory, mapped to
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

- `XgProcessingServiceTests` — the service's CSV and Diagram JSON builders
  over producer-built rows and records (`TestRecords`): the CSV's header and
  row count, and the JSON read back as records of both kinds.
- `XgpExportServiceTests` — the Web XGP archive over decoded fixtures: entry
  naming (pattern, start number, length, tokens, the uniquifier), a sliced
  decision and a cube re-reading to their source's XGID, an `.xgp` copied
  verbatim from its retained bytes, numbering in the given order, anonymizing
  both kinds; and the export skip — a decision whose payload the producer
  refuses is skipped, reported with its file and id, and consumes no number,
  the archive's commit counting its entries; a verbatim copy without its bytes
  is skipped while an anonymized copy needs none.
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
  under its own identity, naming the instance the ledger answers, and reads
  the real status endpoint as the panel does.
- `JobStoreTests` — one instance's job store directly (§7.5): a status read
  never removes a job and the acknowledgement releases only its result; the
  first terminal result stands; a start is judged by its identity first — a
  repeat answers its disposition and starts nothing, a changed request, a
  retired identity and another instance are refused, in both an XGP and a
  CSV case; settling retires an unseen identity for good and answers
  admitted for an admitted one; and XGP admission refuses while any entry
  runs (a mark naming the latest included) and unless the mark names the
  latest entry, admits exactly one of two simultaneous starts, and leaves
  other formats alone; a refusal at admission retires the identity, a
  conflicting retry's refusal leaves the admitted disposition, and a start
  naming another instance changes nothing here; the snapshot reported is
  served as reported and is what a terminal result's builder receives.
- `LocalJobEndpointTests` — the server's side of the protocol through the
  real wire (`LocalServer`, `ProtocolWire`): a lost start response's retry
  finds the same job and no second runs; a delayed start settled first is
  retired and refused when it arrives, and one arriving first is admitted
  and settles as admitted; a duplicate after the acknowledgement answers its
  disposition and runs nothing; changed requests and other instances are
  refused; a status read removes nothing; admission while an entry runs, and
  two simultaneous starts; a collision's run error names the file; a
  malformed identity (a trailing newline included) is a bad request; a
  cancel ends the job and its ledger entry; and a failed job ends with its
  entry even when logging the failure throws.
- `LocalRecoveryTests` — §7.5 through real pages (`LocalPage`: Home with the
  real panel over the real server and shared in-memory browser storage, a
  reload disposing one page before opening the next), at each failure
  point: a lost start response (retried, applied once); a delayed start in
  both orders (settled first: retired, no export, the refusal said;
  admitted first: owed and blocked while it runs, then followed and applied
  once); a reload while a job runs (XGP blocked, its accounting applied when
  it ends, nothing else shown); a refused checkpoint (applied in memory,
  export usable, a reload applying the entries again) and a failed write
  (the old document whole); a lost acknowledgement then newer accounting
  (no rollback, no write for a replay, the newer choice standing); a stale
  tab (refused, reconciled, admitted); a job owed from an earlier instance
  (the gate reason and its action); the server or a settle out of reach
  (unknown, Run waiting); a status read that keeps failing (the run error,
  following on). And the amendment's transitions: a pass paused across a
  restart, a pass held across a memory-only change to this page's record, a
  stale tab's pre-start write after a restart, and a follower's result from
  an ended instance (none writes over the newer record or rolls the
  controls back); learned owed jobs dropped once another page resolved them,
  and kept through a refused read; a start refused on its only attempt (no
  gate) and an unanswered attempt then a refused one (owed); Stop before the
  answer in both orders, and a cancel not delivered (shown, following on);
  what counts as an edit (a reload's restored value follows; away and back
  stays; a set-back number stays through a replay); and a record that
  cannot be read (gated, kept, replaced only by the action, which still
  waits on reconciliation; a refused read gating nothing). And the third
  review's: a new entry whose numbering equals the record's advancing the
  control, and a replay of another page's export leaving a later edit; the
  P/J/Q/K sequence following, and an entry of another instance bounding
  nothing; the owe step finding the record unreadable, or learning of a
  lost job, sending no start; a changed pass showing checking until it has
  reconciled again; and Stop — after the admitted answer arrived (the
  answer held by `AnswerHeldJobs`), a failure standing through a persistent
  failure and contact returning, a CSV Stop before the answer, a settle
  going unanswered, a settle answered by another instance in CSV and XGP,
  Stop pressed twice, and a stopped run's close failure reaching the page.
  The start-winning Stop test holds the cancel's response until the server
  has answered it before letting the job past its first file. And the
  fourth review's: another page's owed job of this instance not holding the
  start back (one export runs); an idle page after another page's export
  and a restart following its numbering only if unedited, its next run
  writing past that export's files; a held ledger response then a newer
  stored checkpoint, each taking followed once with its own numbering;
  two Stops answered in either order (a confirmed delivery stands) and a
  stale cancel's failure after the next run starts (nothing shown), the
  cancels answered by `CancelAnsweringJobs`; and the persistent-failure
  notice held back by a Stop failure then shown once a Stop is delivered,
  closed and staying closed for its streak, and due again at the next
  failure when the Stop failure holding it back is closed.
- `LocalReconcilerTests` — the reconciliation driven directly over the real
  server and an in-memory browser storage, for what no page shows: each
  entry the record takes offered once, a replay offering nothing, a
  restored record offering nothing; a record merged past the ledger's end
  offered once a ledger shows it, each taking with its own numbering; a
  taking whose follow throws offered again until followed; the owe step
  holding a start back before the page has reconciled but not for another
  page's job of this instance; and a pass that fails with a logger that
  throws, still leaving the state unknown and tried again.
- `XgpEditClockTests` — what counts as an edit, rule by rule: an export
  this page started, another page's ordered by an earlier own entry of its
  ledger (and not by a later one, a start no ledger holds, or a forgotten
  one), the controls' setting as the bound and the later of the two, a
  taking that committed nothing, a Web download; and pruning keeping the
  owed starts and the latest admitted one not owed.
- `XgpNumberingRecordTests` / `XgpNumberingStoreTests` /
  `XgpNumberingKeeperTests` — the numbering record (§7.5): applying an entry
  (a newer one sets numbering and mark and resolves its job; one at or
  before the mark changes neither; one of another instance only under a
  pass's authority; one that committed nothing moves only the mark), the
  marks' order within an instance and none across, what the record already
  holds, owing and resolving; the document's exact shape, a read's four
  outcomes, and the migration — only over a missing record, every key
  removed once stored, kept when its write is refused; and the keeper's
  merge (a stored record unchanged since it was seen leaves the page's
  standing; a changed one supplies mark and numbering together), owed jobs
  learned and recorded, a pass's authority (both halves, refusal), and a
  record that cannot be read kept until its resolution; nothing written
  without a fresh read (over an in-memory storage refusing reads and taking
  writes: a checkpoint, a known-unreadable document, the resolution), the
  resolution's authorization ending, a competing resolution queued behind a
  pass's held read running after it and preserved, an identical Web
  download still taken and written; and the next number, derived and not
  stored.
- `WebDownloadTests` — "the last download"'s label names the attempt's
  format and ending, for every format and ending, and the formats' short
  names.
- `HomeWiringTests` — bUnit wire tests pinning the `FilterSetup` → `Home`
  integration through the composite's rendered DOM (the panels are
  producer-internal; `FindComponent` over them is banned, host tests
  included), reading the owner's snapshot as Home does: readiness (the empty
  selection is in effect without Apply once a folder is committed or files
  are selected, and Apply is off with no source), a non-empty selection in
  effect only after Apply and handed to both panels by value, an edit
  closing the gate and its undo reopening it, the per-mode source-change
  rule (a folder commit or a new selection ends the setup — generation
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
  reloads the context, Web mode renders no section and never calls the
  relay). Storage is incidental here (Loose). Uses `StubAppModeHandler` from
  `bUnitTestHelpers` — which also serves an in-memory edition of the
  filterdocument relay — to drive the mode branch and the saved-filters
  context deterministically.
- `HomeSourceReportTests` — how Home reports its source
  (halheinrich/backgammon#374, Arc 2), with storage planned: the surface is
  up from the first render and nothing is reported until `/api/appmode`
  answers (held open by the stub, so the window is observed rather than
  raced); **a Local return with a non-empty committed filter, the app-mode
  response held, released with the same folder, preserves the setup's
  generation and baseline** — the draft on screen, Apply with nothing to
  do, Run open over the applied filter; a different folder restored, and no
  folder restored, each end the setup; blanking the folder reports no source
  with the surface still mounted; a Web return reports no source, and a pick
  after it — of the same file — is a new token without the earlier pick's
  consent; and a Web clear (a dismissed picker) reports no source, the next
  pick again inheriting nothing. And a disposed page: a Home whose mode
  response is held while a newer Home establishes folder B and applies its
  filter has its request cancelled, and reports nothing — B's source,
  generation and baseline stand; and a disposed Home's in-flight read,
  refused, still reaches the condition while its restore makes no further
  call.
- `HomeStorageUnavailableTests` — the storage condition as Home shows it,
  with storage planned. A browser that refuses **everything** — Home, both
  panels, the filter surface — leaves a working page and the notice, every
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
- `BrowserStorageConditionTests` — the condition's own contract: a fresh
  holder (a reload) has no occurrence; the first refusal begins it and
  notifies once, later ones are the same occurrence; a dismissal cannot
  precede its occurrence (throws) and survives further refusals; `Attach`
  delivers the current state before it returns and then each change; a
  disposed attachment hears nothing; and a logger that throws still lets
  the occurrence begin and its observers hear of it, and a storage call's
  refused write, read and entry read return their results normally.
- `WebModePanelStorageTests` / `LocalModePanelStorageTests` — each panel's
  own keys under a refusing browser, planned. **A refused folder-hint write
  and the upload still reads its files** — at v1.12.1 that write threw out of
  the selection handler before any file was read; a refused restore lands on
  the defaults and is reported; a refused output-path write keeps the typed
  value and is reported; and working storage restores, saves and reports
  nothing.
- `WebModePanelNoticeTests` / `LocalModePanelNoticeTests` — what kind of box
  each of a panel's notices is (see "Notices"), through real DOM events, every
  box read through BgUiPrimitives' `NoticeBox` (halheinrich/backgammon#385):
  `ShouldBe` states each call site's kind, announcement, dismissibility and
  attributes — the splat trap included (see Pitfalls) — and the reader
  enforces the box's structure. The run error dismissing by either gesture
  and **showing fresh on the next failure although its text is identical**
  (Local's failure is a refused start — a start that cannot be made is
  retried, not failed);
  the busy notice's flex row inside the content and not on the box; and the
  over-limit selection stated once, with no run error beside it. What the
  zero-match and skipped notices *say*, and when, stays with their own
  classes. **Deterministic by construction** (halheinrich/backgammon#374):
  each gesture's own completion is awaited before its outcome is read — a
  run or download by its click's task, a dismissal by a settle on the
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
  hosting observes: the Local-config round-trip landing on disk at the real
  route, 204 for absent, 400 for non-simple names, and the Web-config 404
  from the explicit action guard.
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
  planned, its bookkeeping's apply checkpoint held: a run whose folder
  changed while it ran shows no "Done" while that write is pending nor after
  it completes, and the export counter still advances by the job's commit;
  the same held run over unchanged inputs shows its result throughout, as
  the control.
- `HomeXgpAccountingTests` — accounting reaching the numbering record
  through the real page, storage planned (the owed checkpoint, then the
  pass that applies the entry: its first read and the entry's checkpoint;
  identities from
  `SequentialJobIdentities`): a failed Local run and a cancelled one each
  advance the numbering by their commit; a control changed while a Local
  run ran, or while a Web download's hand-off was pending, stands, while the
  export's numbering is recorded from its captured options; a length change
  alone still advances the next number (correction 1); a run that
  committed nothing changes no number; a next number typed before a Web
  download's click is followed past it, which needs the panel to raise the
  download's start; and a Web download that committed nothing leaves a
  next number set before it, under the saved pattern but not at the saved
  next number, as set.
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
  is still told it ended, before it is acknowledged — with a run whose filter
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
- `FilteredRowCacheTests` — direct tests of `FilteredRowCache`: no build
  before the first `Refilter`; build on the first; cache hit on an equal
  `FilterConfig` value in a new instance; rebuild on a different value, and
  on the handed-in instance mutated afterwards (the key is the cache's own
  copy); `ReplaceRows` and `ReplaceRecords` each re-project through the set
  in effect without rebuilding and leave the other set alone; `Clear` empties
  rows, records and projections but keeps the materialized filter. **One run, one
  ranking**: over a decision whose best play differs by ranking, "erred by at
  least 0.1" admits the row and the record together under equity and neither
  under depth first — the second case is the one a record judged under the
  equity default would fail; a row built for another ranking is refused
  whole; an undefined ranking is refused. And every list the cache hands out
  refuses a write through `IList<T>` (the collection rider).
- `WebModePanelFilteringTests` — bUnit wire tests pinning that
  `WebModePanel` routes live filtering through `FilteredRowCache` under
  its `FilterInEffect` gate (no materialization while none is in effect,
  materialization when one is, a cache hit on an equal config in a new
  instance — what every host render hands down — a rebuild on a different
  value, a filter no longer in effect leaves the cache untouched — the shape
  an edit arrives in), observed through the panel's
  internal `RowCache` seam. Guards the migration failure mode of a panel
  that compiles but filters beside the cache instead of through it. And a
  real file selection with Diagram JSON selected loads under
  `AppRanking.Ranking`: the cache's ranking, every row's, and the same
  decisions in both projections.
- `WebSessionTests` — the session's rules directly (§7.3), with a held
  conversion (`ScriptedConverter`) for the late completions: a pick reads
  each file once, names a failed read with the reader's message and converts
  rows only; verbatim bytes are kept only where an export copies the file; an
  all-failed pick still owns its failures; a superseded pick publishes
  nothing; records convert only when asked and a completed result is reused;
  a records conversion overlapped by a pick, a book change, a format switch
  or a cancellation publishes nothing, or is cached, as its owner says; "this
  selection" is the read skips and the selected key's conversion skips in
  pick order, unchanged by refiltering; a conversion's failure is owned by
  every output that needs its key (rows by both representations, records by
  records alone); the last download is replaced from the next attempt's
  start and refused after a newer attempt or pick; and an attempt begins
  only from a converted representation — a refused begin leaving the last
  download standing — and builds from the projection it captured, whatever
  a later refilter does.
- `WebModePanelReadTests` — halheinrich/backgammon#364 through the panel: a
  pick with one unreadable file loads the good ones and names the bad one in
  "this selection"; an all-failed pick still shows its failures; the book's
  re-extract handles a file per file (a decoded file whose rows the producer
  refuses is named again under the new book).
- `WebModePanelConversionTests` — halheinrich/backgammon#74 through the
  panel: a CSV-only pick and download never call the records conversion (the
  call itself counted, since a timing cannot show it); switching to Diagram
  JSON converts records once and later switches reuse both results; a row
  conversion failure keeps converted records downloadable; "not converted
  yet" is never zero matches, nor under XGP "No decisions match the
  filters" while a book's re-extract holds the rows; the preview follows the
  selected representation; a records conversion overlapped by a pick or a
  book change publishes nothing; a superseded pick's failure is not shown,
  and a conversion's failure is judged by its selection and book, not its
  gesture (a book load's reconversion overtaken by a pick, a records
  conversion under a replaced book), and by the keys the selected output
  needs (a records conversion failing after a switch back to CSV shows
  nothing; a rows conversion failing under Diagram JSON shows, the records
  left unconverted; a failure of the selected representation shows under
  CSV and Diagram JSON); refiltering changes no skip and converts nothing.
- `WebModePanelDownloadTests` — a download attempt (§7.1, §7.3): an XGP
  decision whose preparation fails is skipped, consumes no number, and is
  named in "the last download (XGP)", the host receiving the archive's
  commit, not the selected count; a failed hand-off exports nothing and
  advances no number, keeping its skips under a failed label; options
  changed while the hand-off is pending leave the attempt's captured ones
  standing; a format switch inside the click's yield window or during the
  hand-off leaves the attempt building the format it was clicked for, and
  the gesture ends with the new format converted; a click while the selected
  format is not converted builds nothing, and nor does a click past any one
  of the Download gate's conditions (a gesture in hand, no filter, an
  unreadable record, nothing matching, invalid options), which raises no
  export to the host; a held download that fails after a
  newer pick shows no error, while one that succeeds still reaches the host's
  numbering; an accounting failure after the hand-off is shown, the download
  staying completed; an XGP download waits while the host's record cannot be
  read; the next attempt replaces the record and a new pick clears it.
- `WebModePanelXgpExportTests` — the Web XGP export chain over real
  fixture bytes: the archive handed over and the commit raised, anonymized
  positions, the options gate; and the download's start raised to the host
  as an XGP attempt begins, once, before anything is handed over, and for no
  other format.
- `WebModePanelBusyAffordanceTests` — the busy contract, pinned as two
  independent halves because the failure mode is ordering, not duration: the
  busy state is *rendered* before the body runs (drop the `StateHasChanged`
  and it fails), and the wrapper *yields* before the body runs (drop the
  `Task.Yield` and it fails). The yield half checks on the same synchronous
  stack that called the wrapper — if the body already ran by the time the
  returned task is in hand, the thread was never handed back, which is exactly
  the state in which a queued render can't paint. A failure in the wrapper
  before the body — its own render request made off the renderer's
  dispatcher — still releases the busy state. Plus one pin per gesture
  (file pick, book pick, each download format) that it routes through the
  wrapper at all, and Hal's three ruled busy messages word for word. A
  component test can only pin the wiring; the paint itself
  was pinned against a real browser during the issue halheinrich/backgammon#53
  measurement pass.
- `LocalModePanelBusyAffordanceTests` — the two no-fraction progress states:
  busy-before-first-poll and `JobPhase.Rendering` both render an indeterminate
  striped bar, the rendering branch suppresses the frozen elapsed/throughput
  figures, and — the pin that catches a regression to prefix-matching — the
  same snapshot with a "Rendering…" `FileName` but `Phase = Processing` keeps
  the determinate bar and its figures.
- `BusyCursorTests` — the busy-cursor binding for both panels in one file,
  because it is one contract: each panel's root carries `is-busy` exactly while
  its own busy flag is up (Local mode's pre-first-poll gap and its atomic-render
  branch; Web mode's whole `RunBusyAsync` body, throwing bodies included), and
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
- `WebModeRankingTests` — the same in Web mode: the converter's two
  conversions for the cache's ranking and the cache's filtering, under
  `DepthFirst`, admit exactly the producer's `DepthFirst` decisions in both
  projections.
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
  the one-time migration of the keys the numbering record replaced — the
  three keys, and the legacy prefix (including the brace-prefix fallback)
  — into the document, a stored document read alone, post-export
  persistence as one checkpoint, a number typed before a Web download
  followed and one changed away and back during it kept, two downloads
  whose numbering equals the record's each taken and each followed, and an
  unreadable record gating Web XGP until its action replaces it, its text
  exactly Hal's both-modes wording — storage planned, so
  the migration's reads, write and removals and the export's read and write
  are each declared and verified.
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
- `XgProcessingServiceOpeningBookTests` — the WASM counterpart: the bytes→book
  bridge (`TryLoadOpeningBook` loads valid `.ob` bytes, rejects garbage without
  throwing) and that `XgSourceConverter`'s two conversions enrich the
  `ajhhBG0407` book decision with a book and degrade without one.
- `WebModePanelOpeningBookTests` — bUnit wire tests for the `.ob` input: the
  status line reports a loaded / invalid book, and a book chosen after the `.xg`
  files reconverts the retained decoded files (pick order doesn't affect
  enrichment).
- `WebModePanelRefilterOnLoadTests` — bUnit tests pinning the *panel-layer*
  projection rule: rows handed to the cache while a filter is held in effect
  are projected through the set in effect (`ReplaceRows`, no rebuild). End
  to end, a new selection is a new source: an applied selection is dropped,
  while the empty one stays in effect (`HomeWiringTests` pins both); these
  isolate what the cache routing guarantees whatever the host keeps in
  effect across a selection.

## Public API

### HTTP endpoints (Local mode only)

The `/api/process` endpoints are the Local job protocol of the umbrella's
`SPEC-notices.md` §7.5 (see "Recovery"). A job's identity is the browser's,
at most 64 letters, digits and hyphens and nothing after them (the pattern
ends at `\z`, since `$` would let a final newline through); a refusal is
409 with a
`ProtocolRefusal { Reason }`, `Reason` one of `OtherInstance`,
`ChangedRequest`, `Retired`, `XgpRunning`, `StaleMark`.

```
POST /api/process/start
  body:  StartRequest { JobId, Instance, Mark?, Run }
           Run:  ProcessRequest { FolderPath, OutputPath, Filters,
                                  OutputFormat, XgpOptions, Anonymize }
           Mark: LedgerMark { Instance, Number } — XGP only: the last ledger
                 entry the page applied; null for none, and for other formats
  200 →  StartResponse { JobId, LedgerNumber? }
         — admitted: a new job, or a repeat of an admitted identity with the
           same request, which starts nothing; LedgerNumber is an XGP job's
           ledger entry.
  409 →  ProtocolRefusal — another instance; the identity admitted for a
         different request; a retired identity; for XGP, an entry running,
         or a mark not naming the instance's latest entry. Nothing starts.
         A refusal at XGP admission retires the identity; no refusal
         changes a disposition already held, and another instance's start
         changes nothing.
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
  404 →  the instance does not hold the job: never started here, released
         by its acknowledgement, or lost with an earlier instance.

POST /api/process/{jobId}/cancel
  200 →  (empty)   404 → the job has ended or is not held
         — the client tells the two apart from a failure (any other answer,
           or none), and shows a cancel refused or failed, unless one for
           the job was already confirmed delivered, which no later failure
           undoes: Stop is never shown as delivered when it was not.

POST /api/process/{jobId}/acknowledge
  204 →  always: the job's result is released; a job not held, or still
         running, changes nothing. Dispositions and the ledger are untouched.

GET  /api/process/ledger
  200 →  LedgerSnapshot { Instance, Entries[] }
           Entries: LedgerEntry { Number, JobId, Running, Commit? } in
                    admission order, kept for the instance's lifetime

POST /api/process/{jobId}/settle
  body:  SettleRequest { Instance }
  200 →  SettleResponse { Outcome: Retired | Admitted }
         — Retired: never admitted, and now never can be (the owed job is
           resolved); Admitted: its start arrived (the job stays owed).
  409 →  ProtocolRefusal OtherInstance.

GET  /api/appmode
  200 →  { Mode: "Local" | "Web" }

GET  /api/openingbook/status                             (Local mode only)
  200 →  OpeningBookStatus { Loaded, EntryCount }
         — whether the server loaded the opening book, and its entry count
           (0 when none). Local-only, like /api/process: OpeningBookProvider is
           registered only in the Local guard, so the client only calls it in
           Local mode.

GET  /api/filterdocument?folder=…&name=…                 (Local mode only)
  200 →  the named file's text (text/plain)
  204 →  file (or folder) absent — a value, not an error: the client maps
         it to the storage seam's null-for-absent contract
  400 →  blank folder, or name that isn't a simple file name
  404 →  Web mode (see the action-guard Pitfall)

PUT  /api/filterdocument?folder=…&name=…                 (Local mode only)
  body:  the file's full text, written verbatim (overwrite)
  200 →  (empty)   400/404 → as GET; IO failures (e.g. missing folder) → 500,
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
  (`PlayRanking.Equity`): the application's one ranking, stated once, which
  both modes read (see "The ranking"). Public because the server's
  composition root reads it, to hand it to the Local-mode processor.
- `AppModeResponse` — response record for `GET /api/appmode`
  (`record AppModeResponse(string Mode)`). Server returns it; client
  deserializes against it. Object-wrapped rather than a bare string so the
  shape stays extensible without breaking consumers.
- `OpeningBookStatus` — response record for `GET /api/openingbook/status`
  (`record OpeningBookStatus(bool Loaded, int EntryCount)`). Its own DTO rather
  than folded into `AppModeResponse`: book status is Local-only, and
  `AppModeController` serves both modes (where the book provider isn't
  registered), so the concerns can't share an endpoint.
- `JobPhase` — enum `Processing | Rendering`. Which stage of a Local-mode run a
  `ProcessingProgress` snapshot describes, and the SSOT the client branches its
  progress bar on. Deliberately has no terminal member: `Complete` /
  `Cancelled` / `ErrorMessage` already are the terminal-state SSOT and a second
  one could disagree with them. `Processing` is `0`, so every pathway with only
  that one stage needs no assignment.
- `OutputFormat` — enum `Csv | DiagramJson | Pptx | Pdf | Xgp`. Server
  references it too, which is why it lives under `Client/Shared` rather than
  being duplicated. `Pptx` and `Pdf` are Local-mode only; `Xgp` works in
  both modes (see the XGP export section). `OutputFormatNames.ShortName` is
  the one statement of the short name the copy gives each ("CSV", "Diagram
  JSON", "PPTX", "PDF", "XGP").
- `StrictJsonStringEnumConverter<T>` — bundled by type-level attribute onto
  `OutputFormat`, `JobPhase`, `RefusalReason` and `SettleOutcome`, so every
  enum on the Local-mode wire crosses as
  its declaration name and a numeric ordinal is rejected. Neither end of that
  wire configures a serializer: the panel posts with `HttpClient`'s stock
  options and the server binds with a bare `AddControllers()`. Putting the rule
  on the type is what makes that work — and is why `LocalModePanel` no longer
  owns a `_wireOptions`. `XgFilter_Lib`'s filter enums and `BgDataTypes_Lib`'s
  `AnalysisLevel` carry their own equivalents, so one payload has one spelling
  convention throughout (halheinrich/backgammon#164,
  halheinrich/backgammon#37).
- `XgpExportOptions` — naming options for a batch of per-decision `.xgp`
  exports (`NamePattern`, `StartNumber`, `SuffixLength`;
  `DefaultNamePattern` = `"pos{n}"`). Rendering lives in the naming engine —
  both export pathways name files through an `XgpNameAllocator` created from
  these options. Permissive wire DTO — validation is the separate
  `TryValidate(out error)` (the UI gates on it; export pathways throw
  `ArgumentException` via `XgpNameAllocator.Create`).
- `XgpNameAllocator` — the naming engine's one public entry point: a stateful
  per-run name source created via `Create(options, filters)` (the single
  options-validation throw point for both export pathways). The server's
  Local-mode processor and the WASM archive builder both name files through
  it; `CommittedCount` is the numbers a run has used.
- `XgpExportCommit` — `sealed record XgpExportCommit(XgpExportOptions
  Options, int Count)`: what one XGP export committed — the options it
  captured and the files it committed — valid by construction (valid options,
  a non-negative count), `NextNumber` and `CommittedNothing` (the one
  statement of "consumed no number") derived and not serialized. Both modes'
  accounting to Home (see "XGP export"); on the wire as
  `ProcessingProgress.XgpCommit`.
- `XgpPayload` — static: `Prepare(DecisionId, XgFile, ReadOnlyMemory<byte>?
  sourceBytes, bool anonymize)` prepares one decision's `.xgp` payload from
  its decoded source — the routing rule both modes share — and
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
  it, because it reads `AppRanking`, an app type. `XgProcessingService`,
  `WebSession`, the numbering record's types, `ILocalJobs` and
  `LocalJobsClient`, `LocalReconciler`, `XgpEditClock` and their
  companions (`Client/Services`) are `internal`
  for the same reason — client-assembly implementation details, not public
  surface.
- `ProcessRequest` — what one Local run does, the `Run` of a start request
  (`FolderPath`, `OutputPath`, `Filters` of type
  `XgFilter_Lib.Filtering.FilterConfig`, `OutputFormat`, `XgpOptions`,
  `Anonymize`). A `sealed record` with init-only members, so a repeated
  start compares with the admitted one by value (`FilterConfig` and
  `XgpExportOptions` compare by value too).
- The protocol (`SPEC-notices.md` §7.5): `StartRequest(JobId, Instance,
  Mark?, Run)` and `StartResponse(JobId, LedgerNumber?)`;
  `ProtocolRefusal(Reason)` with `RefusalReason`; `LedgerMark(Instance,
  Number)` — validated, with `Follows(other)`, the one statement of the
  marks' order: by number within an instance, none across instances;
  `LedgerEntry(Number, JobId, Running,
  Commit?)` and `LedgerSnapshot(Instance, Entries)`; `SettleRequest`,
  `SettleResponse` and `SettleOutcome`. Records, so a start compares by
  value.
- The Local panel's protocol parameters: `LocalStartState(Instance?, Mark?,
  XgpBlock?)` with `XgpStartBlock` (`Checking | Running | LostExport |
  UnreadableRecord`) and `Allows(format)`; and `OwedXgpJob(JobId, Instance,
  Options, Folder)`, also an entry of the numbering record. Public because a
  component's parameters are.
- `ProcessingProgress` — status-endpoint payload; an immutable record (every
  property init-only; a changed snapshot is a new one, made with `with`) the
  processor makes and the panel reads, so the job store can keep, serve and
  hand on a snapshot as it is. `Skipped` (`SkipRecord`, empty by default,
  never null) is the run's skip record as of the snapshot; `XgpCommit`
  (`XgpExportCommit?`) an XGP run's committed result; `WrittenPath`, on a
  successful terminal snapshot only, where the run wrote its output, an
  output fact the request's `OutputPath` never stands in for.
  `PercentComplete` is get-only and derived.
- `SkipRecord` — the one record type of skipped units, both modes
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
  `FileName` is the file as the user named it — in Local mode its path
  relative to the run's input folder, unambiguous under the recursive search,
  whereas rows and logs keep the bare name (see Job lifecycle for why the two
  differ); in Web mode the picked file's name. `DecisionId` is null for a
  whole-file skip and set for a decision skip, crossing in the library's
  canonical string form through `DecisionId`'s own bundled converter;
  `Reason` is the failure's message. A positional record like
  `AppModeResponse` and `OpeningBookStatus`: an entry is a fact about what
  happened and has no reason to change.

`FilterConfig` is **not** in `Client/Shared` — it lives in
`XgFilter_Lib.Filtering` (lib-owned). Both client and server reference the
lib type directly; nothing in this subproject duplicates or shadows it.

## Pitfalls

- **WASM can't stream HTTP responses.** Local-mode progress is delivered by
  client polling, not server-pushed streaming. Don't "fix" this by adding an
  `IAsyncEnumerable` endpoint expecting WASM to consume it.
- **A Web-mode busy state raised immediately before its work never reaches
  the screen.** WASM runs Blazor on one thread and `StateHasChanged` only
  *queues* a render — the queue drains when the thread is handed back. So
  `_busy = true; StateHasChanged(); DoSynchronousWork();` paints nothing,
  while looking correct in review. `RunBusyAsync`'s `await Task.Yield()` is
  the load-bearing line; don't "simplify" it away, and route new slow gestures
  through the wrapper rather than raising `_busy` by hand. Measured before the
  fix (issue halheinrich/backgammon#53): on a 6.6 s zip build the "Building…"
  label entered the DOM **14 ms after the build finished** and reverted 22 ms
  later; on CSV and JSON it never entered the DOM at all.
  `WebModePanelBusyAffordanceTests` pins both halves separately — that the
  busy state is rendered before the body runs, and that the wrapper yields
  before it — because a bUnit assertion on the markup alone passes with or
  without the yield.
- **The busy cursor is one class and one rule — don't add per-spot cursor CSS.**
  The affordance is `is-busy` on each panel's root element plus the single
  `wwwroot/app.css` rule. A new slow gesture inherits it by raising the busy
  state the panel already holds (in Web mode: by going through `RunBusyAsync`);
  putting `cursor:` on an individual button, notice or progress bar forks the
  discipline and drifts. Corollary: nothing links the class to the rule at
  compile or test time — `BusyCursorTests` pins state→class only — so the name
  must be changed in the panels and the stylesheet together. That is the price
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
  acknowledges is kept for the instance's lifetime, by rule.
- **A start is judged by its identity before anything else, under the
  store's lock** — another instance, then a repeat (its disposition, nothing
  started), a changed request or a retired identity, and only then XGP
  admission. Don't move an admission check ahead of the identity, or a
  retried start whose first attempt was admitted would be refused as
  "running".
- **Don't resolve an owed job from a ledger read alone.** Its start may be
  delayed rather than lost; only settling — one server step under the start
  lock — can say "retired". An admitted answer keeps the job owed until its
  entry is applied (`LocalRecoveryTests`'s delayed-start pair).
- **Whether an entry changes the numbering record is the record's own
  rule** (`XgpNumberingRecord.Applying`, the one mark check). The
  reconciler asks it rather than repeating it; a replayed entry at or before
  the mark changes nothing — not even for a moment, since a pass replays
  every ended entry in order and a rollback followed by its correction would
  still be two wrong writes.
- **`prerender:false` is required.** Filter state and file pickers live in
  the WASM runtime; a prerendered server pass would double-init components
  and lose state. Don't enable prerendering on the routable components.
- **Server project has no .xg parsing in Web mode.** `ServerInstance`,
  `JobStore`, `LocalFolderProcessor`, and `XgFilter_Lib` wiring are
  registered only inside the `Local` mode guard in `Program.cs`. Moving that
  registration outside the guard will break Azure deployment.
- **Server services stay `public` by framework constraint, not by need.**
  `ServerInstance`, `JobStore` (with `JobEntry`, `StartJudgement` and
  `SettleJudgement`), `LocalFolderProcessor`, and `AppModeService` are
  `public` even though nothing outside the server assembly consumes them,
  because the MVC controllers constructor-inject them: a `public` controller
  can't take an `internal` constructor parameter (CS0051), and controller
  discovery only finds `public` classes, so the controllers can't be
  internalized to match either. Best-practice constructor injection is worth
  more than the surface narrowing here — don't re-flag these in a surface
  audit, and don't reach for interface indirection, a service-locator, or a
  custom controller feature-provider to force them `internal`. The client-side
  naming engine (`XgpNameTemplate` et al., `XgProcessingService`) has no such
  constraint — it's injected only into Razor components (whose generated
  `@inject` properties are non-public) or reached statically, so it is
  `internal`.
- **Deck output (PPTX/PDF) requires server-side rendering.** Both deck
  paths go through `BackgammonDiagram_Lib.ExportRaster.DiagramRasterRenderer`
  (`RenderPptx` / `RenderPdf`), which renders SVG via the native-free core
  and then rasterizes it via SkiaSharp before assembling the deck. SkiaSharp's
  native binary isn't available under Blazor WASM, so PPTX and PDF are offered
  in Local mode only. Don't add a `BackgammonDiagram_Lib.ExportRaster`
  reference to `ExtractFromXgToCsv.Client.csproj` — it would either fail at
  runtime or pull `SkiaSharp.NativeAssets.WebAssembly` into the WASM payload
  (multi-MB bloat, AOT/Skia friction). The SVG-only core
  `BackgammonDiagram_Lib` is WASM-safe, but the client has no need for it.
- **QuestPDF license must be set at server startup.** `RenderPdf` throws if
  `QuestPDF.Settings.License` is unset. `Program.cs` assigns
  `LicenseType.Community` unconditionally (Web mode never calls
  `RenderPdf`, so there's no harm in setting it always). Community is
  appropriate for the current non-commercial posture
  (revenue ≤ $1M / year); revisit if commercial distribution ever becomes
  a goal.
- **`FilterConfig` materialization differs by mode.** Both panels take the
  filter in effect as a `FilterConfig` — but where it's materialized into a
  `DecisionFilterSet` via `FilterConfig.Build()` differs. Local mode POSTs
  the unbuilt config to the server over HTTP; `ProcessController` calls
  `request.Filters.Build()` to materialize server-side before handing the
  set to `LocalFolderProcessor`. Web mode never crosses an HTTP boundary;
  the build lives in `FilteredRowCache.Refilter`, which `WebModePanel`
  calls from `OnParametersSet` while a filter is in effect. The cache keys
  the build on the config's **value**, so it runs once per filter, not once
  per render and not once per `Matches` call. Same `FilterConfig.Build()`
  method on both sides; the materialization point is the only difference.
- **Compare configs by value, never by reference.** The owner's
  `ConfigInEffectFor` returns a new instance on every call, so Home hands
  each panel a new instance on every render. A reference comparison — a
  cache key, a "did the filter change" check, an `Assert.Same` in a test —
  reads every render as a change. `FilterConfig` has value equality
  (`Equals`, order-insensitive over its lists) and no `==` operator; it is
  mutable, so a key held across calls is a copy the holder owns
  (`FilteredRowCache` round-trips it through the canonical JSON).
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
- **Run/Download gating is the owner's, read once — don't re-derive it, and
  don't re-split it.** Both panels' filter term is `FilterInEffect`, which
  Home reads from `FilterSetup` source-relatively, in one property, and
  from nothing else. Don't call `ConfigInEffectFor` at a use site with a
  token minted there — a second mint is a second chance to mint it
  differently — and don't hand a panel a config beside a separate "applied"
  flag: one nullable is one fact, and two parameters can disagree. If Run
  or Download is ever enabled with the selection on screen not in effect, or
  with what is in effect for a *different* source, that's a regression: the
  owner is the gate, not a cosmetic hint. Don't add an empty-filter
  exception either — the owner's readiness already puts the empty selection
  in effect without Apply (halheinrich/backgammon#266).
- **A source change ends the setup — and only Home says so.** Local mode
  latches the folder path at the input's `@onchange` boundary (plus the
  restore); Web mode's panel mints a new token per selection. Home reports
  the token (`ReportSource`) whether or not the surface is mounted, and the
  owner ends the setup on any different token, `null` included: the applied
  selection is dropped, the draft kept, and the saved-filters context
  reloads — one gesture drives both because the source folder *is* the
  filter store's folder. An equal token ends nothing, which is how a remount
  and a same-folder recommit keep their setup; A → B → A is two endings, so
  re-entering a folder never brings its old consent back. Three
  consequences that look like bugs but are the contract: a re-selection in
  Web mode drops an applied filter until re-Apply (while the empty
  selection stays in effect); Apply is off before any source exists; and the
  output path never ends anything — it is not a source (umbrella-ratified).
  Don't wire the latch to `@oninput` — a half-typed path is not a source.
- **Never report a source before it is known.** `ReportSource(null)` is a
  real answer: it ends an existing setup. Home's mode and folder arrive in
  its first-render restore — the folder from storage, the mode from
  `/api/appmode` — so until both have arrived the answer is "not yet known",
  which is never reported. Reporting early would end the setup on every
  return to the page and then report the restored folder as a new source:
  the baseline gone, Apply re-armed, Run dark. Hence `_sourceResolved`, and
  hence no mode panel renders before it (a panel is where a source is
  latched). The surface itself mounts from the first render: the owner
  outlives the page, so it shows the existing setup as it stands until the
  report arrives. Don't "simplify" this into a report from `OnInitialized`
  or `OnParametersSet`. `HomeSourceReportTests` holds `/api/appmode` open to
  pin it. **Nor after the page has gone**: the restore's awaits belong to the
  page's lifetime (see "Filter setup"), so don't add an await to the restore
  that is not handed the lifetime or checked against it, and don't swallow
  an `OperationCanceledException` from the mode request into the `"Web"`
  fallback — the catch is filtered on the lifetime for that reason.
- **A Web selection's token must never repeat.** The owner outlives every
  page, and the panel holding the selection does not; a counter that
  restarts with the page or the panel would mint an earlier pick's token
  again, and the new pick would inherit that pick's consent. The counter is
  process-wide and only moves forward. Don't move it into the panel or Home,
  and don't derive the token from the file names — the same files picked
  again are a new selection.
- **The restore runs to its end, over results.** Every read in Home's
  restore answers a value or "nothing stored" (a refusal is reported and
  answers "nothing stored" too), so the restore always reaches the mode probe
  and the source report with facts, not with reads that never landed. Don't
  wrap the restore in a method-wide `try`: it would have to restate every
  default in its handler (a second copy to drift from the field
  initializers), and would swallow the app-mode probe's own guarded failure
  along with it. The probe stays separate deliberately: its fallback
  (`"Web"`) is *not* truthful on a Local install, so it must be attempted
  whatever storage does.
- **Every storage call goes through `BrowserStorage`, and nothing latches.**
  This app's keys go through `SettingsStorage`; the surface's go through
  its own seam to the same accessor. Don't call `localStorage` through
  `IJSRuntime`, don't add a "storage is unavailable" flag that skips a call,
  and don't let a refused write stop the gesture that made it — the Web
  upload did exactly that at v1.12.1. `BrowserStorageCondition` remembers
  that a refusal *happened*, for its notice; it never gates a call. And
  `read ?? default` on a raw result loses the refusal — `SettingsStorage`
  reports before it returns null, which is why callers may coalesce on it.
- **The filterdocument Local guard is an explicit action guard —
  deliberately unlike `OpeningBookController`.** The processing services
  stay DI-guarded (registered only inside the Local branch), but
  `FilterDocumentStore` registers unconditionally and the controller
  answers 404 itself when `AppModeService.IsLocal` is false. The deviation
  is the point: "this endpoint does not exist in Web mode" is observable
  and pinned end to end (`FilterDocumentEndpointTests`), where an
  unresolvable constructor dependency is only a 500. Don't "tidy" the store
  registration into the Local guard — that converts the 404 back into a
  container failure.
- **`HttpDocumentStorage` is one stable instance over a delegate —
  and must never be called without a folder.** The composite rebuilds its
  saved-filters store when the bound `Storage` reference changes, so Home
  constructs the adapter once and the live folder rides the
  `Func<string?>`. While the latched path is blank (and always in Web
  mode) Home binds `Storage = null` — no section renders and nothing calls
  the relay; a call with no folder is therefore an adapter-contract bug
  and throws `InvalidOperationException` unwrapped, while everything that
  means "the I/O failed" wraps in `DocumentStorageException` so the store
  degrades instead of faulting the page.
- **`WebApplicationFactory` marker: never `Program`.** The Client's
  top-level entry point generates its own `Program`, visible to the test
  project through the `InternalsVisibleTo` grant — naming `Program` as the
  factory's entry-point type draws CS0433 (ambiguous between Client and
  server). `FilterDocumentEndpointTests` uses a server-assembly marker type
  (`AppModeService`) instead; the factory only needs *a* type from the
  entry assembly.
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
  `XgpExportServiceTests`. The bytes must have been kept: Local mode reads
  with them on the XGP pathway when not anonymizing; Web mode keeps them only
  for a file whose rows the producer stamped as `.xgp` positions
  (`CopiesSourceVerbatim`), or whose rows failed. Don't decide by the file's
  extension: the stamping rule is the producer's.
- **The XGP counter is client-owned, and fed only by commits.** The
  numbering lives in Home's numbering record (`xg_xgpNumbering`) and
  advances only from a commit's captured options — a Web hand-off's
  `XgpExportCommit`, or a Local job's ledger entry. Don't reconstruct a count
  from `TotalRows`, a progress snapshot or the selected rows, don't persist
  from Home's current controls, and don't overwrite a control the user
  edited since the export started — judged by the user's own edits, never
  by comparing the control with the export's captured start (a reload's
  restored value is not an edit; away and back is one; a length change is
  not a choice about the next number), and the controls follow only what
  the record took (`Follow`; `LocalRecoveryTests`,
  `HomeXgpAccountingTests`). Follow the taking — the event — not a change
  in the numbering's value: an export whose numbering equals the record's
  is a taking all the same, and a replay is none (`LedgerTaking`;
  `LocalReconcilerTests`). Follow the taking's own numbering, never the
  record's as it stands when the controls follow: a newer record merged
  since is a later taking, judged against its own start. For an export another page started, don't take
  any start of this page's as its bound: only an entry this page started
  that the same ledger admitted before it orders it (`XgpEditClock`).
  Nothing scans previously exported files, and no number is ever inferred
  from file names (Hal's F3 ruling): a user who bypasses the counter (edits
  "next number" backwards, exports into the same place twice with a reset
  pattern) gets colliding names, and in Local mode each such file is
  written under the next free name instead of replacing the existing one.
- **Write the numbering record only through `XgpNumberingKeeper`.** Each
  checkpoint reads afresh, merges, and writes the whole document once;
  writing a field on its own, or from the page's copy without the fresh
  read, can undo another page's export or drop its owed job. A refused read
  is no fresh read: write nothing then. And keep the keeper's operations one
  at a time — a second awaiting storage beside the first breaks its guard.
- **Never order marks of different instances** (`SPEC-notices.md` §7.5) —
  not by which instance the page believes current, and not by wall-clock
  time, which can be set back. Across instances an entry is newer only
  under a reconciliation pass's authority: written from the state the pass
  started with, while the stored record and this page's record both stand
  (`XgpNumberingKeeper.ApplyAsync`). Don't apply a result the panel's
  follower received outside a pass; don't let a merge pair one record's
  mark with another's numbering.
- **A learned owed job is not a recorded one.** A job this page learned
  from storage goes when a readable record no longer holds it; one it
  recorded survives until seen stored. Don't merge owed jobs as a plain
  union (a resolved job would come back as a false lost export), and don't
  let a refused or unreadable read drop anything.
- **Never read an unreadable record as nothing stored.** It may hold owed
  exports: migration, checkpoints and passes all leave it as it is, and only
  the user's resolution authorizes replacing it — that document, once, and
  only once a fresh read shows it.
- **Log after recording, and contain the log.** Where a log reports state
  the user depends on — a skip, a reconciliation's retry, a storage refusal
  — record the state first and catch what the logger throws: a logging
  failure must never end the batch, the retry or the storage call.
- **The ranking is stated once, and always stated.** Every producer
  operation that takes a ranking gets `AppRanking.Ranking` (or, in Web mode,
  the cache's, which is built from it) — never a `PlayRanking` literal, and
  never nothing: a producer whose ranking is optional (`XgIteratorOptions`,
  whose default is `Equity`) is still handed the app's, so
  `XgSourceConverter` builds options even with no book. That today's choice and
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
  collide with what's on disk. In Web mode, take a name only once a payload
  is prepared.
- **The name uniquifier is per-run only.** `XgpNameAllocator` appends
  ` (2)`, ` (3)`, … within one export run; it never looks at the output
  folder. Across runs the counter (`{n}`) keeps names apart, and in Local
  mode the create-new write is the last guard: a pattern without `{n}`, a
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
- **The pattern, last-number, length and prefix keys are read-once legacy.**
  They exist only as migration input while no `xg_xgpNumbering` document is
  stored, and are removed once it is — never write them again.
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
  previous render (`LocalPage.WaitForScreen`). And a Local Home test waits
  for the page's reconciliation (`HomeTesting.WaitForModePanel` does), plans
  its fresh read of the numbering record (`ExpectLocalReconcile`), and names
  its job identities (`SequentialJobIdentities`) wherever the owed job's
  identity reaches a planned document.
- **Opening-book config: absent ≠ empty.** `OpeningBookProvider.ResolvePath`
  reads `configuration["OpeningBookPath"]`: `null` (key absent) auto-detects the
  default install path; `""` (key present, empty) *disables* enrichment. Don't
  add an empty `"OpeningBookPath": ""` to `appsettings.json` thinking it's a
  harmless placeholder — it turns enrichment off. Leave the key out to keep
  auto-detect, or set a real path.
- **The opening book is Local-only server-side.** `OpeningBookProvider` and
  `OpeningBookController` are registered/reached only in the Local guard (Web
  mode parses client-side and loads its own book via the browser). Don't move
  the provider registration outside the guard, and don't inject it into a
  both-modes controller like `AppModeController` — Web deployments have no
  provider to resolve.
- **Web-mode book loading bridges through a temp file, by necessity.**
  `OpeningBook`'s only public loader is path-based (`Load`/`TryLoad`); its
  byte-image seam is `internal`. WASM has no real path for picked bytes, so
  `XgProcessingService.TryLoadOpeningBook` stages the `.ob` bytes to an
  Emscripten MEMFS temp file and loads that. Don't "simplify" this into a
  UI-side `OpeningBook.Load(path)` call — components have neither a path nor any
  business touching the loader — and don't reach for the lib's internal image
  seam from here (that's an upstream change, out of scope).
- **A late-chosen Web book must reconvert.** The book is part of every
  conversion key, so publishing a new one drops the old book's results and
  reconverts the retained decoded files, and the status line ("Book loaded")
  never contradicts the data (enrichment applied). Keeping a result across a
  book change would silently leave files converted before the book
  unenriched while the UI claims otherwise.
- **Records convert only when an output needs them.** Don't convert records
  on a pick or a book change for every format "to keep the formats in sync":
  that is halheinrich/backgammon#74's cost, and
  `WebModePanelConversionTests.ACsvOnlyPick_NeverCallsTheRecordsConversion`
  counts the calls. A format switch reuses a completed result and never
  discards the other representation's.
- **Every Web-mode completion publishes through its owner.** A pick, a book
  load, a conversion and a download each carry a ticket from their start;
  their completion publishes only while their owner says so
  (`SPEC-notices.md` §7.3). Don't publish a result because the controls
  still show the same values, and don't read the format, options or filter
  at a download's end — capture them at its start. And don't gate readiness
  on `Rows.Count`: Download, the counts and the zero-match notice read the
  selected representation's state, so a row failure never hides converted
  records and "not converted yet" is never zero matches.
- **Local mode's result and its run record live by different rules.** The
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
  before serializing. Measured (issue halheinrich/backgammon#53, Web
  mode/WASM): 6,515 filtered rows build a **50.9 MB** document in **15.6 s**
  of blocked main thread — the in-memory build is the cost, and it is now
  behind a busy affordance rather than fixed.
- **Unacknowledged jobs in `JobStore`.** A job is kept until the page that
  started it acknowledges it, or the instance ends (`SPEC-notices.md` §7.5),
  so a job whose page reloaded or lost its acknowledgement stays for the
  process lifetime — by rule, since a sweep could release a result a page
  is still owed. The dispositions and the ledger are kept for the instance's
  lifetime too. A single-user local app tolerates both; any expiry would
  need a ruling against §7.5 first.
- **Pipeline performance.** The Local-mode pipeline is I/O-dominated but has
  not been profiled. Candidate wins: parallelizing per-file work, reducing
  per-decision allocations in `XgDecisionIterator` consumers, and cutting
  reflection or LINQ in the filter hot path.
- **CSV download button for Azure/browser mode.**
- **PPTX download for Azure/browser mode** — SkiaSharp native isn't available
  under Blazor WASM.
- **`ColumnSelector` wired into UI.**
