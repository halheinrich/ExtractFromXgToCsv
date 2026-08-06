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
  for the per-decision .xgp export pathway; `OpeningBook` + `XgIteratorOptions`
  for opening-book enrichment (the loaded book rides `XgIteratorOptions` into
  the iterators — see "Opening-book enrichment").
- **XgFilter_Lib** — `DecisionFilterSet`, `FilteredDecisionIterator`,
  `ColumnSelector`, `IDecisionFilter`, `IMatchFilter` for filter pipeline.
- **BgDataTypes_Lib** — `DecisionRow`, `BgDecisionData`, `IDecisionFilterData`
  and constituent types. All four output pathways
  (CSV, Diagram JSON, PPTX, and PDF) share the filter pipeline via
  `IDecisionFilterData`.
- **XgFilter_Razor** — `FilterSurface` (the one consumer-facing filter
  composite: filter panel + saved-filters panel + all wiring, the
  applied-holder mediation, and the source-change rule) plus the non-visual
  interaction model it drives: `AppliedFilter`, `FilterSourceToken`,
  `IFilterDocumentStorage` / `FilterStorageException`, `SavedFiltersDocument`.
  Referenced by the WASM Client csproj only — the server has no filter UI to
  host, and its saved-filters file relay must stay ignorant of the document
  names (see Pitfalls).
- **BackgammonDiagram_Lib** — `DiagramRequest.FromDecisionData` and the
  diagram model/options types. Native-free (SVG-only) core.
- **BackgammonDiagram_Lib.ExportRaster** — `DiagramRasterRenderer.RenderPptx`
  / `RenderPdf` for the PPTX and PDF output pathways. Server-side only; the
  client csproj does not reference it (the native rasterization deps —
  SkiaSharp, QuestPDF, OpenXml — live here and aren't available under WASM,
  see Pitfalls).
- **QuestPDF** (transitive, via BackgammonDiagram_Lib.ExportRaster) — PDF
  builder. A license must be configured at server startup before `RenderPdf`
  is invoked; see Pitfalls.

## Directory tree

```
ExtractFromXgToCsv.slnx
Directory.Packages.props
README.md
ExtractFromXgToCsv/                     — server host (thin)
  ExtractFromXgToCsv.csproj
  Program.cs
  appsettings.json
  appsettings.Development.json
  Components/
    App.razor                           — root document
    Routes.razor                        — router host
    _Imports.razor
    Layout/
      MainLayout.razor
    Pages/
      Error.razor                       — server-rendered error page
  Controllers/
    AppModeController.cs
    FilterDocumentController.cs         — saved-filters file relay (Local-only via action guard)
    OpeningBookController.cs            — GET book status (Local mode only)
    ProcessController.cs                — primary-constructor DI
    ShutdownController.cs
  Properties/
    launchSettings.json
  Services/
    AppModeService.cs                   — singleton, exposes configured mode
    FilterDocumentStore.cs              — singleton, named-text-file IO + filename-shape rule
    JobStore.cs                         — singleton, job registry
    LocalFolderProcessor.cs             — scoped, runs the pipeline for Local mode
    OpeningBookProvider.cs              — singleton, resolves + loads the opening book
  wwwroot/
    app.css                             — the app's only bespoke CSS: the
                                          busy-cursor rule (see Busy affordance)
    app.js
    bootstrap/
ExtractFromXgToCsv.Client/              — WASM
  ExtractFromXgToCsv.Client.csproj
  Program.cs
  _Imports.razor
  Components/
    LocalModePanel.razor                — folder/output inputs, polling loop
    WebModePanel.razor                  — file picker, in-memory preview, download
    Pages/
      Home.razor                        — mode-detecting shell
  Properties/
    launchSettings.json
  Services/
    FilteredRowCache.cs                 — Web-mode rows + filtered projections + identity-cached Build
    HttpFilterDocumentStorage.cs        — IFilterDocumentStorage over the server's file relay
    XgProcessingService.cs              — WASM-side decision/diagram extraction
  Shared/
    AppModeResponse.cs                  — { Mode } body for GET /api/appmode
    JobPhase.cs                         — Processing | Rendering (progress-snapshot stage)
    OpeningBookStatus.cs                — { Loaded, EntryCount } for GET /api/openingbook/status
    OutputFormat.cs                     — Csv | DiagramJson | Pptx | Pdf | Xgp
    ProcessRequest.cs                   — POST body for /api/process/start
    ProcessingProgress.cs
    XgpExportOptions.cs                 — .xgp batch naming (pattern/number/length)
    XgpNameAllocator.cs                 — per-run name source: render + " (2)" uniquifier
    XgpNameContext.cs                   — token render inputs (primitives + lib types)
    XgpNameTemplate.cs                  — parsed name pattern (TryParse/Render/Sanitize)
    XgpNameToken.cs                     — one {token} definition
    XgpNameTokens.cs                    — token registry (SSOT) + preview SampleRow
    XgpTokenSource.cs                   — Batch | PerItem token classification
ExtractFromXgToCsv.Tests/
  ExtractFromXgToCsv.Tests.csproj
  bUnitTestHelpers.cs                   — reflection accessor + stub HTTP handlers
  BusyCursorTests.cs                    — busy marker ↔ busy state, both panels (bUnit)
  FilterDocumentEndpointTests.cs        — hosted wire pins for the file relay (WebApplicationFactory)
  FilterDocumentStoreTests.cs           — filename-shape rule + IO contracts (direct)
  FilteredRowCacheTests.cs              — projection + identity-cache invariants (direct)
  FixtureHelper.cs
  HomeWiringTests.cs                    — FilterSurface → Home wiring + per-mode re-gate (bUnit)
  HttpFilterDocumentStorageTests.cs     — client relay adapter contracts (direct)
  HomeXgpPatternTests.cs                — pattern UI, migration, persistence (bUnit)
  LocalFolderProcessorIllegalPlayTests.cs
  LocalFolderProcessorPdfTests.cs
  LocalFolderProcessorPhaseTests.cs     — JobPhase reporting per pathway
  LocalFolderProcessorPptxTests.cs
  LocalFolderProcessorOpeningBookTests.cs — book reaches every processor pathway
  LocalFolderProcessorXgpTests.cs       — Local-mode .xgp folder output wiring
  LocalModePanelBusyAffordanceTests.cs  — no-fraction progress states (bUnit)
  LocalModePanelGateTests.cs            — Run-button dirty-gating + error render (bUnit)
  OpeningBookProviderTests.cs           — path resolution + load/degrade
  Make20PtSmokeTests.cs
  OutputConsistencyTests.cs
  WebModePanelBusyAffordanceTests.cs    — busy render/yield ordering (bUnit)
  WebModePanelFilteringTests.cs         — panel → FilteredRowCache routing (bUnit)
  WebModePanelOpeningBookTests.cs       — .ob input status + late-book re-extract (bUnit)
  WebModePanelRefilterOnLoadTests.cs    — post-Apply file selection filters immediately (bUnit)
  WebModePanelXgpExportTests.cs         — select→export→download wire (bUnit)
  XgpExportServiceTests.cs              — BuildXgpZip round-trip oracle
  XgpNameAllocatorTests.cs              — uniquifier + Peek/Commit rules
  XgpNameTemplateTests.cs               — pattern grammar + token rendering
  XgProcessingServiceOpeningBookTests.cs — book bridge + extract enrichment (WASM path)
  XgProcessingServiceTests.cs
```

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
  lib-ready).
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

- **`Home.razor`** — shell, and the `FilterSurface` host. Detects app mode,
  owns shared state (output format, last-committed filter config, filter
  dirty), renders the output-format radio and the producer composite, and
  delegates to `LocalModePanel` or `WebModePanel`. As the host it binds
  facts and side effects only:
  - **The applied holder.** XgFilter_Razor's `AppliedFilter` (DI-scoped,
    injected) is the applied-state SSOT: the composite mediates it — a
    commit `Set`s it stamped with the source token, an uncommitted-edit
    report `Clear`s it, a source change expires it. The mode panels'
    `FilterApplied` parameter is fed straight from `AppliedFilter.IsApplied`;
    `_filterDirty` is assigned from each `OnAppliedStateChanged` payload's
    null-ness, statelessly per the producer contract. `_filterConfig` (the
    last-committed config the panels POST / refilter with and the XGP
    preview reads) is a distinct fact and stays a Home field.
  - **Source identity (the #78 re-gate).** Local mode's source is the input
    folder path, hoisted into Home: `_folderPathText` follows every
    keystroke (and persists under `xg_folderPath`), while the separate
    `_localSourcePath` latches only at the input's `@onchange` boundary —
    and once at the localStorage restore, which is a committed value. The
    token is minted from the latch only (`FromPath`, normalized: trailing
    separator trimmed + upper-cased for Windows' case-insensitive identity;
    IO keeps the user's spelling), never from the live text — per-keystroke
    re-gating was ruled out. Web mode's source is the file selection:
    Home bumps `_webSelectionGeneration` on the panel's selection event and
    mints `FromGeneration`. Blank path / no selection yet = null token = no
    source: applies made then are deliberately unrecorded, and the first
    real source runs the composite's end-setup, re-arming Apply. The output
    path is **not** a source and never re-gates (umbrella-ratified).
  - **The saved-filters seam.** One `HttpFilterDocumentStorage` instance,
    constructed over a delegate reading the latched path (the composite
    rebuilds its store when the bound `Storage` *reference* changes, so the
    live folder rides the delegate). Bound in Local mode while a folder is
    latched; null otherwise — a blank path renders no saved-filters
    section, never a load failure — and always null in Web mode (ruled: a
    second store is forbidden drift; no localStorage fallback).
- **`LocalModePanel.razor`** — folder/output-path inputs, Run/Stop/Exit
  buttons, polling loop, progress bar (determinate, plus the two
  indeterminate states in "Busy affordance"). Parameters: `OutputFormat`,
  `FilterConfig`, `FilterApplied`, `FilterDirty`, the folder input's
  controlled-component trio (`FolderPath`, `FolderPathChanged` per
  keystroke, `OnFolderPathCommitted` at the change boundary — Home owns the
  value; all three `[EditorRequired]`, they are the re-gate's wiring), and
  the XGP members. The output path stays panel-owned. Takes `FilterConfig`
  (serializable) so it can POST it to the server.
- **`WebModePanel.razor`** — file picker, preview table, download; every slow
  gesture runs through `RunBusyAsync` (see "Busy affordance"). Parameters:
  `OutputFormat`, `FilterConfig`, `FilterApplied`, `FilterDirty`,
  `OnSelectionChanged` (`[EditorRequired]`; raised once per file-selection
  gesture — every `HandleFileSelectionAsync` invocation, since each one
  replaces or clears the retained selection — so Home can bump the
  selection generation), and the XGP members.
  The live row state — loaded decision and diagram rows, the materialized
  `DecisionFilterSet`, and the filtered projections — lives in
  `FilteredRowCache` (Client `Services/`); the panel's own filtering logic
  is one gate: `OnParametersSet` calls `Refilter(FilterConfig)` only when
  `FilterApplied && !FilterDirty` (Apply remains the materialization
  point — no HTTP boundary to serialize across). The cache builds via
  `FilterConfig.Build()` once per config instance (reference-identity
  cached); `ReplaceRows` re-projects fresh rows through the set already in
  effect — a panel-layer rule only, since end to end a selection is a
  source change that re-gates first (see Pitfalls). The panel exposes the
  cache internally (`RowCache`) as the wire-test seam. When an applied
  filter set excludes every loaded row (`Rows.Count > 0` but
  `FilteredRows.Count == 0`) the panel renders an explicit zero-match
  notice in place of the bland `N of M rows match` line, so a
  filtered-to-zero result reads as a result rather than a silent success.
  The "filters are active" signal is emergent, not a `FilterConfig`
  re-inspection: an empty (inactive) set passes every row, so zero
  survivors from a non-empty load can only mean the set is non-empty.

The Run/Download gate is `FilterApplied && !FilterDirty` — the holder half
says a commit is recorded against the *current* source, the dirty half says
the buffers still equal it. A source change (new folder commit, new file
selection) closes the gate through the composite's end-setup: the setup ends,
Apply re-arms, and the user re-applies against the new source. The old
"configure filters → select files → run" ordering is superseded by that rule
— filters can still be staged first, but the *commit* that arms a run is
per-source.

### Busy affordance

Measured on a 266-file / 14.4 MB corpus (issue #53). Web mode is the severe
half: the WASM interpreter runs this pipeline ~30–80× slower than native, so
selecting five files freezes the tab for **3.8 s cold** (~760 ms/file cold,
~240 ms/file once the jiterpreter warms), the full corpus for **57 s**, a
`.xgp` zip of 386 decisions for **6.6 s**, and a 6,515-row Diagram JSON for
**15.6 s**. Apply Filter, by contrast, is 30–71 ms and deliberately gets
nothing.

`WebModePanel` routes every slow gesture — file selection, opening-book pick,
and all three download formats — through one private `RunBusyAsync(message,
body)`, which raises `_busy`/`_busyMessage`, renders, **yields**, runs the
body, and releases in a `finally`. It drives a single `busy-notice` alert at
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
the user found missing (issue #77): through that multi-minute deck render the
bar and the phase message were right and the pointer stayed a plain arrow. Both
panels put an `is-busy` class on their **root** element for exactly as long as
their own busy flag is up — `_busy` in each, unchanged — and `wwwroot/app.css`
turns that one class into `cursor: progress` for the root and everything under
it. Two details are load-bearing:

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
  `DiagramRequest.FromDecisionData`, and expand into Problem/Solution pairs
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
tested; loading is the IO half. The provider exposes `IteratorOptions`
(null when no book); `LocalFolderProcessor` holds that single value in a field
and passes it as `options:` at all four iterator call sites (CSV, Diagram JSON,
Xgp, deck). A null provider, a disabled key, a missing file, or an unreadable
book all resolve to the same null-options / unenriched path.
`GET /api/openingbook/status` surfaces the load state so `LocalModePanel` can
show a status line by the folder input.

**Web mode (client).** The browser parses `.xg` files, so it loads the book too
— from an optional **Opening book (.ob)** `InputFile` beside the `.xg` picker.
`XgProcessingService.TryLoadOpeningBook(bytes, out book)` is the seam: the
producer's loader is path-based and WASM has no real path for picked bytes, so
it stages the image to an Emscripten MEMFS temp file, calls
`OpeningBook.TryLoad`, and deletes it — keeping components off both the path API
and the temp-file mechanics. `ExtractDecisions` / `ExtractDiagramRequests` take
an optional `OpeningBook?` and map it to options through one private
`OptionsFor` helper. `WebModePanel` retains the loaded book alongside the raw
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
`DiagramRequest` via `DiagramRequest.FromDecisionData`, then expanded to a
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

- `XgProcessingServiceTests` — end-to-end Local-mode pipeline against
  fixture files.
- `Make20PtSmokeTests` — wire test for the Make20Pt filter category.
  Exercises the same `FilterConfig.Build` + `FilteredDecisionIterator`
  call sequence `LocalFolderProcessor` uses, against the real fixture
  `.xg` files; the reference set is computed directly from the lib's
  XOR semantics so adding/removing fixtures shifts both sides together.
- `OutputConsistencyTests` — verifies that the CSV pathway and the Diagram
  JSON pathway see the same decisions through the same filter set. Uses
  `IDecisionFilterData` explicitly (CA1859 suppressed — the interface
  contract is what's being tested).
- `LocalFolderProcessorPptxTests` — wiring test for the Local-mode PPTX
  pathway. Runs the processor against the fixture folder, asserts the
  written file is a valid OOXML zip with at least one slide. Deck-level
  conformance is owned by `BackgammonDiagram_Lib`'s `PptxConformanceTests`.
- `LocalFolderProcessorPdfTests` — wiring test for the Local-mode PDF
  pathway. Runs the processor against the fixture folder, asserts the
  written file begins with the `%PDF-` magic bytes. Document-level
  conformance is owned by `BackgammonDiagram_Lib`'s own tests.
- `HomeWiringTests` — bUnit wire tests pinning the `FilterSurface` → `Home`
  integration through the composite's rendered DOM (the panels are
  producer-internal; `FindComponent` over them is banned, host tests
  included): the holder derivation feeding both mode panels' gates, the
  per-mode #78 re-gate (folder commit and file re-selection end the setup —
  holder cleared, Run/Download re-gated, Apply re-armed without an edit),
  the not-a-source pins (output path; a same-value folder recommit), the
  first-latch transitions (an apply before any source is unrecorded; the
  first folder commit / file selection re-arms), and the saved-filters
  round-trip over the file relay (document rows render, save-as writes into
  the latched folder under the producer's file name, a folder change
  reloads the context, Web mode renders no section and never calls the
  relay). Fails closed if a binding to `OnFilterConfigChanged` or
  `OnAppliedStateChanged` is ever silently dropped (Razor compiles
  unrecognized component attributes cleanly; `[EditorRequired]` catches only
  the missing-binding half, at compile time). Uses `StubAppModeHandler` from
  `bUnitTestHelpers` — which also serves an in-memory edition of the
  filterdocument relay — to drive the mode branch and the saved-filters
  context deterministically.
- `FilterDocumentStoreTests` — the server relay's unit contracts: the
  filename-shape rule's accept/reject matrix, absent file *and* absent
  folder → null, the write round-trip + overwrite, the never-create-folder
  write failure, and the argument guards behind the controller's 400s.
- `FilterDocumentEndpointTests` — hosted wire pins
  (`WebApplicationFactory` over the real `Program` pipeline) for what only
  hosting observes: the Local-config round-trip landing on disk at the real
  route, 204 for absent, 400 for non-simple names, and the Web-config 404
  from the explicit action guard.
- `HttpFilterDocumentStorageTests` — the client adapter's producer
  contracts: 204 → null (absence is a value), body round-trips, folder and
  name travel escaped with the folder read from the delegate at call time,
  non-success and network failures wrap in `FilterStorageException`, and a
  call with no current folder propagates unwrapped as the adapter-contract
  bug it is.
- `LocalModePanelGateTests` — bUnit tests pinning two `LocalModePanel`
  invariants: the Run-button dirty-gating contract (`FilterApplied` &&
  !`FilterDirty` plus non-empty paths to enable) and the `ErrorMessage`
  render branch (a `Complete + ErrorMessage != null` `ProcessingProgress`
  renders in a `.text-danger` span, not the in-progress slot).
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
  before the first `Refilter`; build on the first; cache hit on the same
  `FilterConfig` reference; rebuild on a new reference; `ReplaceRows`
  re-projects through the set in effect without rebuilding; `Clear` empties
  rows and projections but keeps the materialized filter.
- `WebModePanelFilteringTests` — bUnit wire tests pinning that
  `WebModePanel` routes live filtering through `FilteredRowCache` under
  the `FilterApplied && !FilterDirty` gate (no materialization before
  Apply, materialization on Apply, identity cache across re-renders,
  dirty leaves the cache untouched), observed through the panel's
  internal `RowCache` seam. Guards the migration failure mode of a panel
  that compiles but filters beside the cache instead of through it.
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
  was pinned against a real browser during the issue #53 measurement pass.
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
- `XgpNameTemplateTests` — the name-pattern grammar (every `TryParse`
  failure shape) and per-token rendering, including the counter-unification
  pin that `pos{n}` reproduces the old prefix naming, and `Sanitize`
  coverage (internal, via `InternalsVisibleTo`).
- `XgpNameAllocatorTests` — per-run uniquifier (`name.xgp`, `name (2).xgp`,
  …), Peek idempotence and the Peek-without-Commit slot reuse that keeps
  failed decisions from consuming a number, `{n}` never uniquifying, and
  `Create`'s per-shape `ArgumentException`s. The case-insensitivity of the
  bookkeeping is pinned by comparer reflection — no current token can render
  case-differing base names, so it isn't behaviorally observable yet.
- `HomeXgpPatternTests` — bUnit tests for the pattern UI wiring: textbox
  binding, registry-driven insert-token dropdown, preview/error branches,
  the one-time `xg_xgpPrefix` migration (including the brace-prefix
  fallback), and post-export persistence.
- `OpeningBookProviderTests` — the provider's pure path resolution (absent →
  auto-detect, empty → disabled, explicit) and load/degrade (real fixture book
  loads and exposes `IteratorOptions`; empty config and a missing file both
  degrade to no book without throwing).
- `LocalFolderProcessorOpeningBookTests` — pins that the loaded book reaches
  every server processing pathway. CSV and Diagram JSON assert the enriched
  depth label directly (with book) vs. the bare "Book V2" (without); the Xgp and
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
  projection rule: rows handed to the cache while the applied parameters are
  held true are projected through the set in effect (`ReplaceRows`, no
  rebuild). The end-to-end contract these once pinned — "files selected
  after an Apply are filtered immediately" — is superseded by the #78
  source-change rule (a selection re-gates first; `HomeWiringTests` pins
  that); these isolate what the cache routing still guarantees to any host
  that keeps the applied state across a selection.

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
                              ErrorMessage?, PercentComplete (computed) }
         — PercentComplete is derived from Current/Total; ErrorMessage is
           non-null only on the catch path (terminal error state); Phase is
           the JobPhase discriminator the client picks its progress bar from
           (FileName is presentation and is never parsed for it).

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
`HttpFilterDocumentStorage` adapter is its only caller, supplying file names
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
  so a future move into a library is relocation-only. `XgProcessingService`
  (the WASM extraction/zip service in `Client/Services`) is `internal` for the
  same reason — a client-assembly implementation detail, not public surface.
- `ProcessRequest` — POST body for `/api/process/start`
  (`FolderPath`, `OutputPath`, `Filters` of type
  `XgFilter_Lib.Filtering.FilterConfig`, `OutputFormat`, `XgpOptions`).
- `ProcessingProgress` — status-endpoint payload.

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
  fix (issue #53): on a 6.6 s zip build the "Building…" label entered the DOM
  **14 ms after the build finished** and reverted 22 ms later; on CSV and JSON
  it never entered the DOM at all. `WebModePanelBusyAffordanceTests` pins both
  halves separately — that the busy state is rendered before the body runs,
  and that the wrapper yields before it — because a bUnit assertion on the
  markup alone passes with or without the yield.
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
- **`ProcessingProgress.Phase` is the progress-stage SSOT; `FileName` is
  presentation.** The rendering line reads "Rendering PPTX (…)" for humans,
  but the client branches on `JobPhase.Rendering`. Don't re-derive the stage by
  prefix-matching `FileName` — the two would drift, and the string is the half
  that's meant to change freely. `LocalModePanelBusyAffordanceTests` pins the
  direction that catches it: the same snapshot with a "Rendering…" `FileName`
  but `Phase = Processing` keeps the determinate bar.
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
- **`FilterConfig` materialization differs by mode.** Both panels take
  `FilterConfig` — but where it's materialized into a `DecisionFilterSet`
  via `FilterConfig.Build()` differs. Local mode POSTs the unbuilt config
  to the server over HTTP; `ProcessController` calls
  `request.Filters.Build()` to materialize server-side before handing the
  set to `LocalFolderProcessor`. Web mode never crosses an HTTP boundary;
  the build lives in `FilteredRowCache.Refilter`, which `WebModePanel`
  calls from `OnParametersSet` when the filter is applied and settled.
  The cache keys the build on `FilterConfig` reference identity so it
  runs once per Apply, not once per `Matches` call. Same
  `FilterConfig.Build()` method on both sides; the materialization point
  is the only difference.
- **`XgFilter_Razor` is not referenced server-side — and the file relay
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
- **Run/Download gating is holder-plus-dirty — don't re-derive it.** The
  mode panels' `FilterApplied` comes straight from `AppliedFilter.IsApplied`
  (the composite-mediated SSOT) and `FilterDirty` from the per-gesture
  report's null-ness. If a test or UI change ever makes Run or Download
  appear enabled with a dirty filter — or with a commit recorded against a
  *different* source — that's a regression: the holder is the gate, not a
  cosmetic hint.
- **A source change ends the setup (#78) — the token is minted from the
  latch, never the live text.** Local mode latches the folder path at the
  input's `@onchange` boundary (plus the restore); Web mode bumps a
  selection generation once per `HandleFileSelectionAsync`. When the token
  changes, the hosted `FilterSurface` clears the holder, re-arms Apply
  (forget-commit; the null report closes the gate through the normal event
  path), and reloads the saved-filters context — one gesture drives the
  re-gate and the store reload because the source folder *is* the filter
  store's folder. Three consequences that look like bugs but are the
  contract: a re-selection in Web mode blanks the preview until re-Apply
  (ruled UX delta — it supersedes the old "files selected after an Apply
  are filtered immediately" contract, and with it the old "configure
  filters → select files → run" ordering copy: the arming commit is
  per-source); an apply made before any source exists is unrecorded (the
  holder has nothing to stamp it against — the first real source re-arms
  Apply instead); and the output path never re-gates — it is not a source
  (umbrella-ratified). Don't "fix" any of them, and don't wire the token to
  `@oninput` — per-keystroke re-gating was ruled out.
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
- **`HttpFilterDocumentStorage` is one stable instance over a delegate —
  and must never be called without a folder.** The composite rebuilds its
  saved-filters store when the bound `Storage` reference changes, so Home
  constructs the adapter once and the live folder rides the
  `Func<string?>`. While the latched path is blank (and always in Web
  mode) Home binds `Storage = null` — no section renders and nothing calls
  the relay; a call with no folder is therefore an adapter-contract bug
  and throws `InvalidOperationException` unwrapped, while everything that
  means "the I/O failed" wraps in `FilterStorageException` so the store
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
  before serializing. Measured (issue #53, Web mode/WASM): 6,515 filtered rows
  build a **50.9 MB** document in **15.6 s** of blocked main thread — the
  in-memory build is the cost, and it is now behind a busy affordance rather
  than fixed.
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
