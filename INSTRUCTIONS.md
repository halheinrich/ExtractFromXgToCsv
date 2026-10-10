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

- **ConvertXgToJson_Lib** — `XgDecisionIterator`, `XgFileReader`, `XgIteratorState`
  for .xg/.xgp reading and decision iteration; `XgpExporter` (slice surface)
  for the per-decision .xgp export pathway; `XgIteratorOptions`, which carries
  the run's ranking and the optional `OpeningBook` into the iterators (see
  "The ranking" and "Opening-book enrichment").
- **XgFilter_Lib** — `DecisionFilterSet`, `FilteredDecisionIterator`,
  `ColumnSelector`, `IDecisionFilter`, `IMatchFilter` for filter pipeline.
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
- **HTTP surface** — `Controllers/`: process start/status/cancel, app mode,
  opening-book status, the saved-filters file relay, shutdown. Thin
  pass-throughs to the services (see Public API).
- **Local-mode services** — `Services/`: `LocalFolderProcessor` (the four
  output pathways over a folder) and `SkipRecord` (one run's record of what
  it skipped), `JobStore` (the
  job registry and its self-cleaning status read), `OpeningBookProvider`
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
  and the source reporter) and the two mode panels, `LocalModePanel` and
  `WebModePanel` (see Components).
- **Services** — the Web-mode pipeline and the client's own state:
  `XgProcessingService` (WASM extraction and the `.xgp` zip),
  `FilteredRowCache` (loaded rows and their filtered projections),
  `HttpDocumentStorage` (the storage seam over the server's file relay),
  `SettingsStorage` (this app's settings keys over BgUiPrimitives'
  `BrowserStorage`, reporting every refusal) and `BrowserStorageCondition`
  (this app's one storage condition and its notice's dismissal; the filter
  surface's registered refusal sink).
- **Shared** — the wire types the server references too (`ProcessRequest`,
  `ProcessingProgress` and `SkippedItem`, `JobPhase`, `OutputFormat`,
  `AppModeResponse`, `OpeningBookStatus`, the strict enum converter), the
  `.xgp` naming engine (`XgpExportOptions`, `XgpNameAllocator` and its
  internal parts) with the name preview's sample (`XgpNamePreview`), and
  `AppRanking`, the application's one ranking, which both modes read. See
  Client-shared types.

**`ExtractFromXgToCsv.Tests/`** — xUnit, one class per contract: direct tests
of the services and the naming engine, bUnit tests of `Home` and both panels,
hosted `WebApplicationFactory` pins for the file relay, and processor runs
over real files. Records and rows a test states come from the producer's
`TestRecords`. Shared support: `bUnitTestHelpers` (private-field access, stub
HTTP handlers, and a Local run to completion), `HomeTesting` (Home's
registration, render and gestures, and `HomeStoragePlan` — Home's storage
stated on a `BrowserStoragePlan`), `NoticeAssert` (reads a rendered notice box back against
the shared component's documented shape), `CapturingLogger<T>`, `FixtureHelper`, `MalformedXg`
(the synthesized unreadable input skip tests write), `XgpAnonymizeAssert`. Fixtures are the umbrella's `TestData/FixtureFiles`,
linked into the build output rather than kept in this repo (see Test project,
and the fixture pitfall).

## Architecture

### Server host — thin by design

The server project is a launcher plus an HTTP surface for Local mode. It does
not parse .xg files for Web/Azure mode. In Local mode it runs
`LocalFolderProcessor` on a background task and exposes job status; in Web
mode it serves the WASM payload and nothing else.

`AppMode` is configured in `appsettings.json` (`"Local"` or `"Web"`). Local
mode registers `JobStore` (singleton) and `LocalFolderProcessor` (scoped)
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

Web mode extracts both `DecisionRow` (for CSV) and `BgDecisionData` (for
Diagram JSON) on file selection — keeps the two output formats in sync so
toggling the output-format radio doesn't require re-processing. It also
retains each selected file's **raw bytes** (keyed by bare filename) so the
XGP export pathway can re-parse its source files at download time — raw
zlib-compressed bytes are smaller than parsed record graphs and are already
bounded by the 50 MB selection cap.

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
  It builds its one `XgIteratorOptions` from that ranking and the loaded book,
  so every row is built for it; the Diagram JSON and deck pathways filter
  each record through `record.ViewFor(_ranking)`, and the deck draws each
  through `DiagramRequest.ForDecision(record, _ranking)`.
- **Web mode** — `WebModePanel` builds its `FilteredRowCache` for the value,
  and extraction reads the ranking back from the cache, so
  `XgProcessingService.ExtractDecisions` / `ExtractDiagramRequests` (which
  take the ranking as a required argument, as the producers do) build the rows
  for the ranking the cache filters the records under. The cache refuses a
  row built for another ranking.
- **The name preview** — its sample row is `DecisionRow.From(record,
  AppRanking.Ranking)`.

**What fails if a producer default stands in.** Of the producer operations
above, `ViewFor`, `DecisionRow.From` and `DiagramRequest.ForDecision`
require a ranking, so the compiler holds them. `XgIteratorOptions` does not:
its ranking defaults to `Equity`, which is also this app's choice, so a run
under `Equity` cannot tell the app's ranking from the default. The pins
therefore run each mode under `DepthFirst`, over a fixture and filter
(`MatchTest.xg`, "erred by at least 0.001") that each test first proves, from
the producer's own read, admits different decisions under the two rankings:

- `LocalFolderProcessorRankingTests` — one test per pathway, each against the
  producer's reference for `DepthFirst`. CSV and Xgp fail if the processor's
  options drop the ranking (their rows are built by the iterator); Diagram
  JSON and deck fail if records are filtered under any ranking but the
  processor's. Both halves were checked by mutation at the time of writing.
- `WebModeRankingTests` — the pipeline the panel runs (`XgProcessingService`'s
  two extractions for the cache's ranking, then `FilteredRowCache`) under
  `DepthFirst`. If `OptionsFor` dropped the ranking, the rows would be built
  for `Equity` and the cache would refuse them.

Beside those, `FilteredRowCacheTests` pins the cache's own rule over
hand-built rows under either ranking, and `WebModePanelFilteringTests` pins
that the panel builds its cache for `AppRanking.Ranking`.

### XGP export

Selecting the **XGP positions** output format emits the filtered decisions
as per-decision `.xgp` position files — available in both modes. In Web
mode the download is one `.zip` with a `.xgp` entry per decision, built by
`XgProcessingService.BuildXgpZip(sourceFiles, decisions, options)` — the
encapsulation seam: the panel hands it retained file bytes plus
`DecisionId`s; parsing stays inside the service and components never touch
`XgFile`. In Local mode `LocalFolderProcessor.ProcessXgpAsync` writes the
files server-side into the output **folder** (`OutputPath` is a directory
for this format, created if absent; same-named files are overwritten).
Per decision, both pathways apply the same routing rule:

- `XgDecisionId` (from a `.xg` source) → `XgpExporter.ToBytes(xgFile, game,
  moveNumber, isCube)` — the producer's **slice** surface, analysis panes
  carried through, XG-SaveAs-equivalent, re-consumable by our own iterator.
- `XgpDecisionId` (from an `.xgp` source) → the source file bytes
  **verbatim** (see Pitfalls).

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
  bookkeeping). Web mode consumes names via `Next`; Local mode uses the
  `Peek`/`Commit` split so failed decisions don't consume a number.

The default pattern `pos{n}` names files byte-identically to the pre-pattern
`{prefix}{number:D{suffixLength}}.xgp` rule; `StartNumber`/`SuffixLength`
govern `{n}`. `Home` owns the option state and its localStorage persistence
(`xg_xgpPattern`, `xg_xgpLastNumber`, `xg_xgpSuffixLength`): the "next
number" field defaults to the persisted last number + 1 while the pattern
matches the persisted pattern (else 1), and both panels report a completed
export through their `OnXgpExported(count)` callback so `Home` can advance
and persist the counter. A missing `xg_xgpPattern` migrates once from the
legacy `xg_xgpPrefix` key (`{prefix}{n}`, falling back to `pos{n}` if the
prefix breaks the grammar); the legacy key is removed on the next successful
export. In Local mode the count is the final
`ProcessingProgress.TotalRows`; cancelled runs advance the counter too
(their files were written), error terminations don't.

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
  - **Its own settings** — the output format, the folder path, the XGP
    pattern, counter, number length and anonymize flag, and the read-once
    legacy prefix — go through `SettingsStorage` (see "Browser storage").
    The restore runs to its end whatever storage answers: each key lands on
    its stored value or its own documented default.
  - **The storage condition's notice**, `#storageUnavailableNotice`, rendered
    from `BrowserStorageCondition` (see "Notices").
- **`LocalModePanel.razor`** — folder/output-path inputs, Run/Stop/Exit
  buttons, polling loop, progress bar (determinate, plus the two
  indeterminate states in "Busy affordance"). Parameters: `OutputFormat`,
  `Source` (the token Home reports), `FilterInEffect` (the filter in effect
  for it, or null — the whole filter half of the Run gate, and the config
  the run POSTs), the folder input's controlled-component trio (`FolderPath`,
  `FolderPathChanged` per keystroke, `OnFolderPathCommitted` at the change
  boundary — Home owns the value; all three `[EditorRequired]`, they are the
  source report's wiring), and the XGP members. The output path stays
  panel-owned, under `xg_outputPath`.
  **A result describes the inputs it was computed from.** A run records
  them when it starts (`RunInputs`: the source, the filter's values, the
  output path, and whether that filter restricted nothing), and the result
  on screen — progress, Done line, zero-match box, run error — is dropped as
  soon as the current source or filter values differ from its run's
  (`OnParametersSet`, compared by value: the host hands a new config on every
  render). Not while busy: a running job's progress and Stop stay with their
  own job, and a run that **completes** after its inputs were superseded
  drops its result on landing (`RunLocalAsync`'s `finally`). Watching "a
  filter is in effect" fall is not enough, and was what this replaced: the
  empty selection stays in effect across a folder change, so nothing falls
  while the folder the result was computed from has gone. The Done line
  names the run's own output path, and the zero-match box asks the run's own
  filter, never the one on screen now.
  On a terminal snapshot whose `Skipped` record is non-empty it shows a
  `skipped-notice` box under the status line (halheinrich/backgammon#223):
  the non-zero counts, then each skipped input with its reason. A file is
  shown by its path under the input folder, and a decision adds its canonical
  `DecisionId`. It sits outside the success/cancelled/error branches, so a
  cancelled or failed run's record shows too; mid-run it stays hidden while
  the record is still growing. The zero-match notice **yields** to it. That
  notice concludes "the filter matched nothing" from zero rows through an
  active filter, and the conclusion holds only if every file was read, so it
  renders only when the record is empty. Suppressing it, rather than rewording
  it to mention the skips, keeps two independent notices, each with one
  condition, instead of adding a third combined state.
- **`WebModePanel.razor`** — file picker, preview table, download; every slow
  gesture runs through `RunBusyAsync` (see "Busy affordance"). Parameters:
  `OutputFormat`, `FilterInEffect` (as Local's), `OnSelectionChanged`
  (`[EditorRequired]`, an `EventCallback<FilterSourceToken?>`: raised once
  per file-selection gesture — every `HandleFileSelectionAsync` invocation,
  since each one replaces or clears the retained selection — with the new
  selection's token, or null when the gesture left nothing selected), and
  the XGP members. Its settings — the download file name and the folder
  hint — go through `SettingsStorage`; the hint's write never stands between
  the user and their files (see "Browser storage").
  **The selection, and its token, are the panel's.** The selected bytes
  (`_fileBytes`) and rows (the cache) live in the panel and die with it, so
  the token does too: minted when a gesture selects files, held beside them,
  dropped when a gesture clears them. Two selections never share a token,
  also across panel instances — the filter setup outlives every page, so a
  page-lived counter would mint an earlier pick's token again after a
  navigation, and a new pick would inherit that pick's consent. The counter
  behind `FromGeneration` is therefore process-wide (one per loaded app),
  and only moves forward. `XgProcessingService` keeps no selection.
  The live row state — loaded decision rows and diagram records, the
  materialized `DecisionFilterSet`, and the filtered projections — lives in
  `FilteredRowCache` (Client `Services/`); the panel's own filtering logic
  is one gate: `OnParametersSet` calls `Refilter(FilterInEffect)` only while
  a filter is in effect. The cache is built for the run's one ranking (see
  "The ranking"): it accepts only rows built for it and filters each record
  through its view for it, built once when the records arrive. It builds
  the filter via `FilterConfig.Build()` once per config **value** — the
  owner's `ConfigInEffectFor` returns a new instance on every call, so a
  reference key would rebuild on every render — and keeps its own copy as
  the key; `ReplaceRows` re-projects fresh rows through the set already in
  effect. Every list it hands out is an immutable array, replaced whole,
  never a live list (the collection rider of halheinrich/backgammon#273).
  The panel exposes the cache internally (`RowCache`) as the wire-test
  seam. When a filter in effect excludes every loaded row (`Rows.Count > 0`
  but `FilteredRows.Count == 0`) the panel renders an explicit zero-match
  notice in place of the bland `N of M rows match` line, so a
  filtered-to-zero result reads as a result rather than a silent success.
  The "filters are active" signal is emergent, not a `FilterConfig`
  re-inspection: an empty (inactive) set passes every row, so zero
  survivors from a non-empty load can only mean the set is non-empty. The
  Web view is derived from the current rows and the current filter on every
  render, so it always describes its inputs.

The Run/Download gate's filter term is `FilterInEffect is not null`, and
nothing else filter-shaped — one fact, one owner. Each panel adds its own
gates beside it (a folder and an output path; loaded rows within the size
limit; valid XGP options); those stay the panels'. A source change (a new
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
- **Readiness** (halheinrich/backgammon#266). With a source, a valid empty
  selection is in effect without Apply. Extract adds no empty-filter
  exception of its own, and no copy says Apply is needed for it: Local's
  "Apply filters first" shows only while a source exists and nothing is in
  effect for it — with no folder, the blank folder box is why Run is dark.
- **Derived facts are keyed by their inputs.** Local's result by its run's
  source and filter values; Web's cache by the filter's value. Neither
  compares configs by reference.

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
  reported to `BrowserStorageCondition` before the call returns. Anything
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

The keys: Home's `xg_outputFormat`, `xg_folderPath`, `xg_xgpPattern`,
`xg_xgpLastNumber`, `xg_xgpSuffixLength`, `xg_xgpAnonymize` and the
read-once legacy `xg_xgpPrefix`; `LocalModePanel`'s `xg_outputPath`;
`WebModePanel`'s `xg_csvFileName` and `xg_lastFolderHint`. The filter
surface's keys are its own and are not named here.

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
| `skipped-notice` (Local) | gate reason — the record of what a run lost | no | assertive |
| the selected-size limit (Web) | gate reason — why nothing loaded and Download is dark | no | assertive |
| `busy-notice` (Web) | live indicator | no | polite |

**A dismissal belongs to one occurrence, lives as long as it, is never
stored, and has one holder — the occurrence's owner**, because only the owner
knows when a new occurrence begins and whether the old one outlives the page.
Every dismissible box here is caller-held (`@bind-Dismissed:get/set`), so the
component keeps no bit that could disagree with the owner's:

- **The run error — the panel's `_runError`.** One occurrence is one failed
  gesture: in Local mode a run the client could not start or follow, in Web
  mode a file read or a download build that threw. `_runError` (nullable) is
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
and all three download formats — through one private `RunBusyAsync(message,
body)`, which raises `_busy`/`_busyMessage`, renders, **yields**, runs the
body, and releases in a `finally`. It drives a single `busy-notice` box at
the top of the panel (the Download button's own "Building…" label is the local
half of the same state). The yield is load-bearing, not stylistic — see
Pitfalls. `RunBusyForTest` is its test seam, sibling of `RowCache`.

Local mode's progress bar already covers the CSV, Diagram JSON and Xgp
pathways end to end (2 s runs, reporting throughout). Two states carry no
fraction and get an indeterminate striped bar instead:

- **Before the first status poll** (`_busy && _progress is null`) — the
  polling loop's opening `Task.Delay(1000)` alone guarantees a second of it.
- **The deck pathway's atomic render** (`Phase == JobPhase.Rendering`) — 144 s
  for PPTX and 405 s for PDF at corpus scale, during which the determinate bar
  would sit solid at 100% beside an elapsed figure frozen near 1 s. The
  branch also suppresses those stale elapsed/throughput numbers.

**The cursor** is the half of the affordance the words can't cover, and the one
the user found missing (issue halheinrich/backgammon#77): through that
multi-minute deck render the bar and the phase message were right and the
pointer stayed a plain arrow. Both panels put an `is-busy` class on their
**root** element for exactly as long as their own busy flag is up — `_busy` in
each, unchanged — and `wwwroot/app.css` turns that one class into
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
- Run → `POST /api/process/start` → `jobId`. Client polls
  `GET /api/process/{jobId}/status` every second.
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
- Stop → `POST /api/process/{jobId}/cancel`. Exit → `ShutdownController`.

#### Web/Azure (`"AppMode": "Web"`)

- Browser file picker, 50 MB cap.
- WASM processes everything client-side. Both `ExtractDecisions` and
  `ExtractDiagramRequests` run on file selection.
- Download button produces CSV or JSON from the pre-extracted in-memory data
  based on the current output-format toggle.
- PPTX and PDF are unavailable (radios disabled) — see "Client WASM" above.

### Job lifecycle (Local mode)

`JobStore` is a singleton holding `ConcurrentDictionary<string, JobEntry>`.
Each `JobEntry` carries a `ProcessingProgress` snapshot and a
`CancellationTokenSource`. The client polls once per second
(the processor uses `reportEvery = 10`, writing progress every 10th file).

Cleanup rides the status read. `JobStore.ReadStatus` returns the current
snapshot and, when it is terminal (`Complete` — success, cancellation, and
failure alike), removes the entry and disposes its `CancellationTokenSource`
after capturing the snapshot to return. So a job's terminal snapshot is served
exactly once, and the polling client always consumes the terminal state (the
Done line, the XGP counter advance) before the entry vanishes — nothing is
removed on the background job's completion itself, which would race the poll and
404 the client before it saw `Complete`. A late `POST /cancel` for an
already-cleaned-up job no-ops (404) rather than throwing off the disposed CTS.
The controller's `Status`/`Cancel` actions are thin pass-throughs to
`ReadStatus`/`Cancel`, so CTS-lifecycle knowledge stays inside `JobStore`. One
gap remains — an abandoned job whose client never polls to completion lingers
for the process lifetime (see the "Abandoned-job expiry" next step).

**Skips are recorded; cancellation is never one**
(halheinrich/backgammon#223). One bad file must not kill a batch, so every
pathway catches a file that fails and moves on — and the Xgp pathway also
catches a single decision whose write fails. Every one of those catches calls
one seam, `LocalFolderProcessor.RecordSkip`, and none logs on its own: the
seam logs the skip and appends a `SkippedItem` (the file's path relative to
the input folder; the decision's id for a decision skip; the exception's
message as the reason) to the run's `SkipRecord`. Every snapshot the run
reports, progress and terminal alike, carries a copy of that record as
`ProcessingProgress.Skipped`, with the file and decision counts derived from
it. Every skip catch excludes `OperationCanceledException`
(`when (ex is not OperationCanceledException)`), so a cancellation raised
inside a file's body cancels the run on all four pathways rather than being
recorded as a skipped file.

The record belongs to the run, so it reaches every terminal snapshot, the two
the job runner builds as well as the one the processor reports. The runner in
`ProcessController` creates one `SkipRecord` per job and passes it to the
processor, and its cancel and error catches both copy it onto the snapshot
they set. They cannot take it from the last reported snapshot: a run-ending
exception reports nothing, and the streaming pathways report only every tenth
file. The processor rejects a record that already holds entries, since one run
must not report another's skips. A failed run therefore shows its error and
its skips together.

The runner's progress sink applies each report inline (`InlineProgress`),
not through `Progress<T>`. `Progress<T>` posts every report to the thread
pool, so a report posted just before a failure could be applied after the
catch had set the terminal snapshot. It would overwrite that snapshot with a
non-terminal one, and the client would poll forever.

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
app only *locates* the book and hands the loaded instance to the iterators via
`XgIteratorOptions.OpeningBook`. Enrichment is strictly additive — it never
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
`LocalFolderProcessor` builds its one `XgIteratorOptions` from the book and
the run's ranking, holds it in a field, and passes it as `options:` at all
four iterator call sites (CSV, Diagram JSON, Xgp, deck). A null provider, a
disabled key, a missing file, or an unreadable book all resolve to the same
null-book / unenriched path.
`GET /api/openingbook/status` surfaces the load state so `LocalModePanel` can
show a status line by the folder input.

**Web mode (client).** The browser parses `.xg` files, so it loads the book too
— from an optional **Opening book (.ob)** `InputFile` beside the `.xg` picker.
`XgProcessingService.TryLoadOpeningBook(bytes, out book)` is the seam: the
producer's loader is path-based and WASM has no real path for picked bytes, so
it stages the image to an Emscripten MEMFS temp file, calls
`OpeningBook.TryLoad`, and deletes it — keeping components off both the path API
and the temp-file mechanics. `ExtractDecisions` / `ExtractDiagramRequests` take
the run's ranking and an optional `OpeningBook?`, and map the two to options
through one private `OptionsFor` helper, which always states options, so the
iterator's own default ranking never stands in. `WebModePanel` retains the loaded book alongside the raw
file bytes and **re-extracts** on a book change, so a book chosen after the
files still enriches them — pick order is irrelevant. A status line reports the
entry count (or an invalid-file error).

### Filter pipeline

`FilterConfig.Build()` (lib-owned, in `XgFilter_Lib.Filtering`) materializes
a `DecisionFilterSet` from the serializable DTO. The server calls
`request.Filters.Build()` in `ProcessController.Start`; Web mode's build
lives in `FilteredRowCache.Refilter`, driven by `WebModePanel`'s
`OnParametersSet` gate. Same method, two call-sites —
see "FilterConfig materialization differs by mode" in Pitfalls. CSV,
Diagram JSON, PPTX, and PDF pathways all feed the same filter set via
`IDecisionFilterData` — one filter implementation, four extraction outputs.
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
- `Make20PtSmokeTests` — wire test for the Make20Pt filter category.
  Exercises `FilterConfig.Build` + `FilteredDecisionIterator` under the
  app's ranking, against the real fixture
  `.xg` files; the reference set is computed directly from the lib's
  XOR semantics so adding/removing fixtures shifts both sides together.
- `OutputConsistencyTests` — verifies that the CSV pathway and the Diagram
  JSON pathway see the same decisions through the same filter set, under the
  app's ranking: each row is built for `AppRanking.Ranking`, each record is
  read through its view for it, and every view member the two share agrees.
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
  play surfaces as a contextual warning through the logger the deck pathway
  hands the iterator.
- `LocalFolderProcessorSkipTests` — the skip contract
  (halheinrich/backgammon#223) on all four pathways, twice each. *Skips are
  recorded*: a temp folder holds one fixture and one malformed file the test
  synthesizes (`MalformedXg`: garbage bytes under an `.xg` name, never
  committed, placed in a subfolder that sorts first so the good file comes
  after the skip); the run completes, the good file's rows are all there
  (checked against the producer read directly), and the bad file is recorded
  once, by its path under the input folder, with a non-empty reason. Two
  same-named files in different subfolders are recorded by their distinct
  relative paths while the log keeps the bare name; a caller's `SkipRecord`
  holds exactly what the snapshots carried and is refused for a second run.
  *Cancellation is not a skip*: a one-file run cancelled from its
  first progress report throws `OperationCanceledException`, its last
  snapshot records no skip, and nothing logged the cancellation as a skip.
  The log half is what catches the Diagram JSON pathway: there, the old
  catches' swallow was re-thrown by the final write before any snapshot could
  show it. Uses a synchronous `IProgress<T>`, because `Progress<T>` posts its
  callback and would race the cancellation point.
- `ProcessingProgressWireTests` — the skip record round-trips under
  `JsonSerializerDefaults.Web`, the defaults both ends of the status endpoint
  actually use: record constructor binding, `DecisionId` in its canonical
  form, and the derived counts. A body with no `skipped` field reads as an
  empty record, not null. And the record handed out is an immutable copy —
  never the list the JSON reader built, nor one a caller set and kept (the
  collection rider).
- `ProcessEndpointTests` — hosted pins (`WebApplicationFactory`, Local
  config) for the terminal snapshot the job runner builds itself when a run
  fails, which nothing below the controller can observe. A Diagram JSON run
  that skips a file and then fails on an unwritable output path, and a deck
  run whose every file is skipped (nothing to render), both end with the
  error and the skip together. Each polls the real status endpoint as the
  panel does.
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
  pick again inheriting nothing.
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
  disposed attachment hears nothing.
- `WebModePanelStorageTests` / `LocalModePanelStorageTests` — each panel's
  own keys under a refusing browser, planned. **A refused folder-hint write
  and the upload still reads its files** — at v1.12.1 that write threw out of
  the selection handler before any file was read; a refused restore lands on
  the defaults and is reported; a refused output-path write keeps the typed
  value and is reported; and working storage restores, saves and reports
  nothing.
- `WebModePanelNoticeTests` / `LocalModePanelNoticeTests` — what kind of box
  each of a panel's notices is (see "Notices"), through real DOM events: every
  box's announcement read off the content wrapper with no role on the box;
  each gate reason and the live indicator rendering no close button and
  answering a click with `MissingEventHandlerException`; the run error
  dismissing by the box and by the close button and **showing fresh on the
  next failure although its text is identical**; the busy notice's flex row
  inside the content wrapper and not on the box; and the over-limit
  selection stated once, with no run error beside it. All through
  `NoticeAssert`, whose `IsNoticeBox` also fails on any attribute the call
  site did not name — the splat trap (see Pitfalls). What the zero-match and
  skipped notices *say*, and when, stays with their own classes.
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
  the same held run, released unchanged, as the control.
- `LocalModePanelZeroMatchTests` — the Local zero-match notice, through real
  runs against a zero-row server: shown beside the Done line for an active
  filter, not for the empty one, and not when an active filter comes into
  effect after a zero-row run through the empty one — the box asks the run's
  own filter.
- `LocalModePanelSkippedNoticeTests` — the client half of
  halheinrich/backgammon#223: a completed snapshot with skips shows the
  notice beside the Done line, with the counts and every name and reason (a
  decision by its file's relative path and its canonical id); a cancelled one
  shows its partial record; one without skips shows nothing, and neither does
  a mid-run snapshot that already carries skips; and zero rows through an
  active filter with skips — a real run — shows the skip notice and not the
  zero-match one.
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
  copy); `ReplaceRows`
  re-projects through the set in effect without rebuilding; `Clear` empties
  rows and projections but keeps the materialized filter. **One run, one
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
  real file selection loads under `AppRanking.Ranking`: the cache's ranking,
  every row's, and the same decisions in both projections.
- `WebModePanelBusyAffordanceTests` — the busy contract, pinned as two
  independent halves because the failure mode is ordering, not duration: the
  busy state is *rendered* before the body runs (drop the `StateHasChanged`
  and it fails), and the wrapper *yields* before the body runs (drop the
  `Task.Yield` and it fails). The yield half checks on the same synchronous
  stack that called the wrapper — if the body already ran by the time the
  returned task is in hand, the thread was never handed back, which is exactly
  the state in which a queued render can't paint. Plus one pin per gesture
  (file pick, book pick, each download format) that it routes through the
  wrapper at all. A component test can only pin the wiring; the paint itself
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
- `WebModeRankingTests` — the same in Web mode: extraction for the cache's
  ranking and the cache's filtering, under `DepthFirst`, admit exactly the
  producer's `DepthFirst` decisions in both projections.
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
  failed decisions from consuming a number, `{n}` never uniquifying, and
  `Create`'s per-shape `ArgumentException`s. The case-insensitivity of the
  bookkeeping is pinned by comparer reflection — no current token can render
  case-differing base names, so it isn't behaviorally observable yet.
- `HomeXgpPatternTests` — bUnit tests for the pattern UI wiring: textbox
  binding, registry-driven insert-token dropdown, preview/error branches,
  the one-time `xg_xgpPrefix` migration (including the brace-prefix
  fallback), and post-export persistence — storage planned, so the
  migration's reads and the export's writes and removal are each declared
  and verified.
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
  throwing) and that both extract entry points enrich the `ajhhBG0407` book
  decision with a book and degrade without one.
- `WebModePanelOpeningBookTests` — bUnit wire tests for the `.ob` input: the
  status line reports a loaded / invalid book, and a book chosen after the `.xg`
  files re-extracts the retained bytes (pick order doesn't affect enrichment).
- `WebModePanelRefilterOnLoadTests` — bUnit tests pinning the *panel-layer*
  projection rule: rows handed to the cache while a filter is held in effect
  are projected through the set in effect (`ReplaceRows`, no rebuild). End
  to end, a new selection is a new source: an applied selection is dropped,
  while the empty one stays in effect (`HomeWiringTests` pins both); these
  isolate what the cache routing guarantees whatever the host keeps in
  effect across a selection.

## Public API

### HTTP endpoints (Local mode only)

```
POST /api/process/start
  body:  ProcessRequest { FolderPath, OutputPath, Filters, OutputFormat,
                          XgpOptions }
  200 →  { JobId }
         — for OutputFormat.Xgp, OutputPath names the output FOLDER;
           for every other format it names the output file.

GET  /api/process/{jobId}/status
  200 →  ProcessingProgress { Current, Total, Phase, FileName, TotalRows,
                              Complete, Cancelled, ElapsedSec, FilesPerSec,
                              ErrorMessage?, Skipped[],
                              PercentComplete, SkippedFileCount,
                              SkippedDecisionCount (all three computed) }
         — PercentComplete is derived from Current/Total; ErrorMessage is
           non-null only on the catch path (terminal error state); Phase is
           the JobPhase discriminator the client picks its progress bar from
           (FileName is presentation and is never parsed for it); Skipped is
           the run's skip record so far, SkippedItem { FileName, DecisionId?,
           Reason } with FileName relative to the input folder, on every
           snapshot including the error one; the two counts are derived
           from it.

POST /api/process/{jobId}/cancel
  200 →  (empty)

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

`ProcessRequest` lives in `ExtractFromXgToCsv.Client/Shared/ProcessRequest.cs`
— the client owns the wire shape and the server deserializes against it.
The server's `ProcessController` references the same type via the Client
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
  both modes (see the XGP export section).
- `StrictJsonStringEnumConverter<T>` — bundled by type-level attribute onto
  `OutputFormat` and `JobPhase`, so every enum on the Local-mode wire crosses as
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
  Local-mode processor and the WASM zip builder both name files through it.
- Naming engine internals (`XgpNameTokens`, `XgpNameToken`, `XgpTokenSource`,
  `XgpNameContext`, `XgpNameTemplate`) — **`internal`** to the client assembly
  (test-reachable via the existing `InternalsVisibleTo`); reached only through
  `XgpNameAllocator`. See the XGP export section. App-level home beside
  `XgpExportOptions`, but they couple only to primitives and lib types
  (`FilterConfig`, `DecisionRow`); the DTO stops at `XgpNameAllocator.Create`,
  so a future move into a library is relocation-only. `XgpNamePreview`
  (`internal`, the preview's sample row) sits beside the engine and not in
  it, because it reads `AppRanking`, an app type. `XgProcessingService`
  (the WASM extraction/zip service in `Client/Services`) is `internal` for the
  same reason — a client-assembly implementation detail, not public surface.
- `ProcessRequest` — POST body for `/api/process/start`
  (`FolderPath`, `OutputPath`, `Filters` of type
  `XgFilter_Lib.Filtering.FilterConfig`, `OutputFormat`, `XgpOptions`).
- `ProcessingProgress` — status-endpoint payload; a settable DTO the
  processor fills and the panel reads. `Skipped`
  (`IReadOnlyList<SkippedItem>`, empty by default) is the run's skip record as
  of the snapshot. `SkippedFileCount` and `SkippedDecisionCount` are get-only
  and derived from it, like `PercentComplete`: they serialize for any reader
  of the wire but are never bound back, so they cannot disagree with the list.
  Every snapshot gets its own copy of the list, so a reported snapshot never
  changes afterwards; and setting `Skipped` takes an immutable copy, so what
  it hands out is never a live list — on the client, not the `List<T>` the
  JSON reader builds (the collection rider of halheinrich/backgammon#273).
- `SkippedItem` — `sealed record SkippedItem(string FileName, DecisionId?
  DecisionId, string Reason)`, one entry of that record. `FileName` is the
  source file's path relative to the run's input folder, unambiguous under the
  recursive search, whereas rows and logs keep the bare name (see Job
  lifecycle for why the two differ);
  `DecisionId` is null for a whole-file skip and set for the Xgp pathway's
  per-decision skip, crossing in the library's canonical string form through
  `DecisionId`'s own bundled converter; `Reason` is the exception's message.
  A positional record like `AppModeResponse` and `OpeningBookStatus`: an entry
  is a fact about what happened and has no reason to change.

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
  Every box is therefore pinned off the rendered DOM for its announcement and
  its dismissibility, and `NoticeAssert.IsNoticeBox` fails on any box attribute
  the call site did not name. A new box needs the same pins; the compiler
  will not ask for them. `Kind` and `ChildContent` are the exception — they
  are `[EditorRequired]`, so omitting one is a build error.
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
- **A per-file exception is a recorded skip, never a swallowed cancellation.**
  Skip catches go through `RecordSkip` and filter out
  `OperationCanceledException`; a bare `catch (Exception)` in the processor
  either hides a dropped file or turns a cancel into a skip (see Job
  lifecycle).
- **The job runner's progress sink is inline — don't restore `Progress<T>`.**
  A posted report can land after the runner's catch and overwrite a terminal
  snapshot with a non-terminal one, so the client polls forever (see Job
  lifecycle).
- **`prerender:false` is required.** Filter state and file pickers live in
  the WASM runtime; a prerendered server pass would double-init components
  and lose state. Don't enable prerendering on the routable components.
- **Server project has no .xg parsing in Web mode.** `JobStore`,
  `LocalFolderProcessor`, and `XgFilter_Lib` wiring are registered only
  inside the `Local` mode guard in `Program.cs`. Moving that registration
  outside the guard will break Azure deployment.
- **Server services stay `public` by framework constraint, not by need.**
  `JobStore`, `JobEntry`, `LocalFolderProcessor`, and `AppModeService` are
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
  pin it.
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
  `BuildXgpZip`, an `XgpDecisionId` decision copies its source file
  byte-for-byte: the source already is a single-position analyzed `.xgp`,
  so the verbatim copy is byte-identical, cheaper than a re-slice, and stays
  strictly more faithful — a re-slice would clear the match-level comments
  the copy preserves. Don't "unify" this branch through the slice surface —
  byte equality with the source is pinned by `XgpExportServiceTests`.
- **The XGP counter is client-owned.** The persisted numbering state
  (`xg_xgpLastNumber` et al.) lives in `Home` and advances via the panels'
  `OnXgpExported(count)` callback. Nothing scans previously exported files;
  a user who bypasses the counter (edits "next number" backwards, exports
  into the same place twice with a reset pattern) gets colliding names —
  by design, the editable field is the escape hatch.
- **The ranking is stated once, and always stated.** Every producer
  operation that takes a ranking gets `AppRanking.Ranking` (or, in Web mode,
  the cache's, which is built from it) — never a `PlayRanking` literal, and
  never nothing: a producer whose ranking is optional (`XgIteratorOptions`,
  whose default is `Equity`) is still handed the app's, so `OptionsFor` and
  the processor build options even with no book. That today's choice and
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
  `ProcessXgpAsync`, a decision's name is Peeked before the write and the
  slot Committed only after it succeeds — that's what keeps "failed
  decisions don't consume a number" true, which the client's persisted
  counter (advanced from the reported `TotalRows`) depends on. Collapsing
  the split into an unconditional `Next` makes the next batch's numbers
  collide with what's on disk.
- **The name uniquifier is per-run only.** `XgpNameAllocator` appends
  ` (2)`, ` (3)`, … within one export run; it never looks at the output
  folder, so cross-run overwrites in Local mode remain by design — the
  counter (`{n}`) stays the cross-run collision guard. Corollary: a pattern
  without `{n}` relies entirely on the uniquifier and will overwrite its own
  previous run's files.
- **`xg_xgpPrefix` is a read-once legacy key.** It exists only as migration
  input for a missing `xg_xgpPattern` and is removed on the next successful
  export — never write it again.
- **The naming engine is deliberately lib-ready.** `XgpNameContext` carries
  primitives and lib types only; the app's `XgpExportOptions` DTO must not
  leak below `XgpNameAllocator.Create`. Keep it that way so a future move of
  the engine into a library stays relocation-only.
- **`ProcessXgpAsync` reports progress on every file, deliberately.** The
  siblings use `reportEvery = 10`, but the Xgp pathway's final/last-reported
  `TotalRows` is what the client persists as its numbering counter — so the
  processor reports before each per-file cancellation point and keeps
  decision writes non-cancellable within a file. Reintroducing `reportEvery`
  (or observing the token between a file's decision writes) makes a
  cancelled run's counter drift from what's on disk.
- **Xgp `OutputPath` is a folder.** `ProcessXgpAsync` treats it as a
  directory (created if absent) and `LocalModePanel` skips the
  extension-swap logic for Xgp. Same-named files are overwritten; the
  counter, not the processor, is the collision guard.
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
- **A late-chosen Web book must re-extract.** `WebModePanel` re-runs extraction
  over the retained file bytes whenever the book changes, so the status line
  ("Book loaded") never contradicts the data (enrichment applied). Dropping the
  re-extract would silently leave files parsed before the book unenriched while
  the UI claims otherwise.
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
- **Abandoned-job expiry in `JobStore`.** Completed jobs are now removed when
  their terminal snapshot is read (see Job lifecycle), so normal runs
  self-clear. What remains is the abandoned case — a client that never polls to
  completion leaves its entry for the process lifetime. A TTL sweep would close
  that gap; a single-user local app tolerates it until then.
- **Pipeline performance.** The Local-mode pipeline is I/O-dominated but has
  not been profiled. Candidate wins: parallelizing per-file work, reducing
  per-decision allocations in `XgDecisionIterator` consumers, and cutting
  reflection or LINQ in the filter hot path.
- **CSV download button for Azure/browser mode.**
- **PPTX download for Azure/browser mode** — SkiaSharp native isn't available
  under Blazor WASM.
- **`ColumnSelector` wired into UI.**
