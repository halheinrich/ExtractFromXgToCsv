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
- **XgFilter_Razor** — `FilterPanel` Razor component. Referenced by the
  WASM Client csproj only; the server has no filter UI to host.
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
    OpeningBookController.cs            — GET book status (Local mode only)
    ProcessController.cs                — primary-constructor DI
    ShutdownController.cs
  Properties/
    launchSettings.json
  Services/
    AppModeService.cs                   — singleton, exposes configured mode
    JobStore.cs                         — singleton, job registry
    LocalFolderProcessor.cs             — scoped, runs the pipeline for Local mode
    OpeningBookProvider.cs              — singleton, resolves + loads the opening book
  wwwroot/
    app.css
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
    XgProcessingService.cs              — WASM-side decision/diagram extraction
  Shared/
    AppModeResponse.cs                  — { Mode } body for GET /api/appmode
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
  bUnitTestHelpers.cs                   — reflection accessor + StubAppModeHandler
  FilteredRowCacheTests.cs              — projection + identity-cache invariants (direct)
  FixtureHelper.cs
  HomeWiringTests.cs                    — FilterPanel → Home wiring (bUnit)
  HomeXgpPatternTests.cs                — pattern UI, migration, persistence (bUnit)
  LocalFolderProcessorIllegalPlayTests.cs
  LocalFolderProcessorPdfTests.cs
  LocalFolderProcessorPptxTests.cs
  LocalFolderProcessorOpeningBookTests.cs — book reaches every processor pathway
  LocalFolderProcessorXgpTests.cs       — Local-mode .xgp folder output wiring
  LocalModePanelGateTests.cs            — Run-button dirty-gating + error render (bUnit)
  OpeningBookProviderTests.cs           — path resolution + load/degrade
  Make20PtSmokeTests.cs
  OutputConsistencyTests.cs
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

- **`Home.razor`** — shell. Detects app mode, owns shared state (output format,
  filter config, filter applied/dirty), renders the output-format radio and
  `FilterPanel`, and delegates to `LocalModePanel` or `WebModePanel`.
- **`FilterPanel.razor`** — owns all filter state. Raises
  `OnFilterConfigChanged EventCallback<FilterConfig>` on Apply / Reset, and
  `OnFilterDirty EventCallback` on every input change so the parent can
  disable Run until Apply. Always visible in both modes so the workflow
  is consistent: configure filters → select files → run.
- **`LocalModePanel.razor`** — folder/output-path inputs, Run/Stop/Exit
  buttons, polling loop, progress bar. Parameters:
  `OutputFormat`, `FilterConfig`, `FilterApplied`, `FilterDirty`.
  Takes `FilterConfig` (serializable) so it can POST it to the server.
- **`WebModePanel.razor`** — file picker, preview table, download. Parameters:
  `OutputFormat`, `FilterConfig`, `FilterApplied`, `FilterDirty`.
  The live row state — loaded decision and diagram rows, the materialized
  `DecisionFilterSet`, and the filtered projections — lives in
  `FilteredRowCache` (Client `Services/`); the panel's own filtering logic
  is one gate: `OnParametersSet` calls `Refilter(FilterConfig)` only when
  `FilterApplied && !FilterDirty` (Apply remains the materialization
  point — no HTTP boundary to serialize across). The cache builds via
  `FilterConfig.Build()` once per config instance (reference-identity
  cached) and re-projects rows handed to `ReplaceRows` through the set
  already in effect, so files selected after an Apply are filtered
  immediately. The panel exposes the cache internally (`RowCache`) as the
  wire-test seam. When an applied filter set excludes every loaded row
  (`Rows.Count > 0` but `FilteredRows.Count == 0`) the panel renders an
  explicit zero-match notice in place of the bland `N of M rows match`
  line, so a filtered-to-zero result reads as a result rather than a
  silent success. The "filters are active" signal is emergent, not a
  `FilterConfig` re-inspection: an empty (inactive) set passes every row,
  so zero survivors from a non-empty load can only mean the set is
  non-empty.

Run button is disabled whenever the filter panel is dirty, forcing the user
to apply or discard pending changes before a run.

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
- `HomeWiringTests` — bUnit wire test pinning the `FilterPanel` →
  `Home` integration. Fails closed if a binding to
  `OnFilterConfigChanged` or `OnFilterDirty` is ever silently dropped
  (Razor compiles unrecognized component attributes cleanly, so a stale
  attribute name binds nothing and produces no error). Uses
  `StubAppModeHandler` from `bUnitTestHelpers` to drive the mode branch
  deterministically.
- `LocalModePanelGateTests` — bUnit tests pinning two `LocalModePanel`
  invariants: the Run-button dirty-gating contract (`FilterApplied` &&
  !`FilterDirty` plus non-empty paths to enable) and the `ErrorMessage`
  render branch (a `Complete + ErrorMessage != null` `ProcessingProgress`
  renders in a `.text-danger` span, not the in-progress slot).
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
  deck pathways — whose output carries no label — use the `BookRollout` × `Ply4`
  filter differential (book present ⇒ the enriched decisions survive and are
  written / rendered; book absent ⇒ they degrade to Unknown and nothing does).
  Runs against a temp folder holding only `ajhhBG0407.xg` for deterministic
  counts. Enrichment correctness itself is owned by ConvertXgToJson_Lib.
- `XgProcessingServiceOpeningBookTests` — the WASM counterpart: the bytes→book
  bridge (`TryLoadOpeningBook` loads valid `.ob` bytes, rejects garbage without
  throwing) and that both extract entry points enrich the `ajhhBG0407` book
  decision with a book and degrade without one.
- `WebModePanelOpeningBookTests` — bUnit wire tests for the `.ob` input: the
  status line reports a loaded / invalid book, and a book chosen after the `.xg`
  files re-extracts the retained bytes (pick order doesn't affect enrichment).
- `WebModePanelRefilterOnLoadTests` — bUnit wire tests pinning that a file
  selection made while a filter set is applied is filtered through it
  immediately (no second Apply): a selective filter yields the independently
  computed count and the rendered `N of M` line for the fresh selection, and a
  new selection the applied filter rejects entirely lands in the zero-match
  notice. The eager load-time refilter arrived incidentally with the
  opening-book re-extract restructure; these tests make it load-bearing.

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
  200 →  ProcessingProgress { Current, Total, FileName, TotalRows,
                              Complete, Cancelled, ElapsedSec, FilesPerSec,
                              ErrorMessage?, PercentComplete (computed) }
         — PercentComplete is derived from Current/Total; ErrorMessage is
           non-null only on the catch path (terminal error state).

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
- **`FilterPanel` is not referenced server-side.** It lives in
  `XgFilter_Razor` (lib-owned) and is consumed only by the WASM client.
  The server's bridge from a configured filter to a runnable filter set is
  `FilterConfig.Build()`, not the panel — see the materialization pitfall
  above.
- **Run button dirty-gating.** The Run button is disabled whenever the
  filter panel has unapplied changes. If a test or UI change ever makes
  the button appear enabled with a dirty filter, that's a regression — the
  dirty state is the gate, not a cosmetic hint.
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
  (a `BookRollout` × `Ply4` filter admits the enriched decisions with a book and
  nothing without one), not by reading a label out of the output — see
  `LocalFolderProcessorOpeningBookTests`.
- **Fixture files are not in this repo.** They live in umbrella `TestData/`
  and are not tracked by git (contents are gitignored; structure is held by
  `.gitkeep`). A fresh clone of this subproject alone cannot run the tests
  without the umbrella `TestData/` present.

## Subproject-internal next steps

- **Streaming JSON write for large datasets.** Current Diagram JSON output is
  a single in-memory array. Very large corpora may need a streaming writer
  (NDJSON or JSON-array streaming) rather than building the full document
  before serializing.
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
- **Adopt `SavedFiltersPanel` (saved named filters) in Local mode.** The
  finding-(Q) arc lands the shared document (`NamedFilterCollection`,
  XgFilter_Lib) and picker component (XgFilter_Razor) in libraries this app
  already references, so adoption is host wiring only: server-side disk
  persistence (Local mode does real `System.IO`; no FS-Access ladder) + the
  FilterPanel handshake. If/when the app deploys (Web mode), where its
  saved-filters file lives is an open question for that arc. Surfaced by the
  user during the finding-(Q) design pass (2026-07-22).
