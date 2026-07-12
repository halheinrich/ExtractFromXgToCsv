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
  for the per-decision .xgp export pathway.
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
    ProcessController.cs                — primary-constructor DI
    ShutdownController.cs
  Properties/
    launchSettings.json
  Services/
    AppModeService.cs                   — singleton, exposes configured mode
    JobStore.cs                         — singleton, job registry
    LocalFolderProcessor.cs             — scoped, runs the pipeline for Local mode
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
    XgProcessingService.cs              — WASM-side decision/diagram extraction
  Shared/
    AppModeResponse.cs                  — { Mode } body for GET /api/appmode
    OutputFormat.cs                     — Csv | DiagramJson | Pptx | Pdf | Xgp
    ProcessRequest.cs                   — POST body for /api/process/start
    ProcessingProgress.cs
    XgpExportOptions.cs                 — .xgp batch naming (prefix/number/length)
ExtractFromXgToCsv.Tests/
  ExtractFromXgToCsv.Tests.csproj
  bUnitTestHelpers.cs                   — reflection accessor + StubAppModeHandler
  FixtureHelper.cs
  HomeWiringTests.cs                    — FilterPanel → Home wiring (bUnit)
  LocalFolderProcessorIllegalPlayTests.cs
  LocalFolderProcessorPdfTests.cs
  LocalFolderProcessorPptxTests.cs
  LocalFolderProcessorXgpTests.cs       — Local-mode .xgp folder output wiring
  LocalModePanelGateTests.cs            — Run-button dirty-gating + error render (bUnit)
  Make20PtSmokeTests.cs
  OutputConsistencyTests.cs
  WebModePanelFilteringTests.cs         — FilterConfig-identity rebuild cache (bUnit)
  WebModePanelXgpExportTests.cs         — select→export→download wire (bUnit)
  XgpExportServiceTests.cs              — BuildXgpZip round-trip oracle
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

Entry naming is `{prefix}{number:D{suffixLength}}.xgp` — single-sourced in
`XgpExportOptions.EntryName`. `Home` owns the option state and its
localStorage persistence (`xg_xgpPrefix`, `xg_xgpLastNumber`,
`xg_xgpSuffixLength`): the "next number" field defaults to the persisted
last number + 1 while the prefix matches the persisted prefix (else 1), and
both panels report a completed export through their `OnXgpExported(count)`
callback so `Home` can advance and persist the counter. In Local mode the
count is the final `ProcessingProgress.TotalRows`; cancelled runs advance
the counter too (their files were written), error terminations don't.

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
- **`WebModePanel.razor`** — file picker, in-memory rows and diagram rows,
  live filtering in `OnParametersSet`, preview table, download. Parameters:
  `OutputFormat`, `FilterConfig`, `FilterApplied`, `FilterDirty`.
  Takes `FilterConfig` and materializes a `DecisionFilterSet` locally
  (cached by `FilterConfig` reference identity) — no HTTP boundary to
  serialize across. When an applied filter set excludes every loaded row
  (`_rows.Count > 0` but `_filteredRows.Count == 0`) it renders an explicit
  zero-match notice in place of the bland `N of M rows match` line, so a
  filtered-to-zero result reads as a result rather than a silent success.
  The "filters are active" signal is emergent, not a `FilterConfig`
  re-inspection: an empty (inactive) set passes every row, so zero survivors
  from a non-empty load can only mean the set is non-empty.

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

Jobs are not auto-removed — this is a single-user local app and the dictionary
lives only for the process lifetime. `JobStore` exposes a `Remove(string jobId)`
method, currently unused by callers (kept for an eventual explicit cleanup
hook; see the "Job cleanup / expiry" entry under subproject-internal next
steps).

### Filter pipeline

`FilterConfig.Build()` (lib-owned, in `XgFilter_Lib.Filtering`) materializes
a `DecisionFilterSet` from the serializable DTO. The server calls
`request.Filters.Build()` in `ProcessController.Start`; `WebModePanel` calls
`FilterConfig.Build()` in `OnParametersSet`. Same method, two call-sites —
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
- `WebModePanelFilteringTests` — bUnit tests pinning the `WebModePanel`
  `FilterConfig`-by-reference-identity rebuild cache: no build before
  Apply; build on first Apply; cache hit on same `FilterConfig` ref;
  rebuild on a new ref; dirty filter doesn't rebuild. Reflection
  accessors are deliberate — the cache fields aren't a public seam, but
  the invariant is load-bearing enough to pin directly.

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
- `OutputFormat` — enum `Csv | DiagramJson | Pptx | Pdf | Xgp`. Server
  references it too, which is why it lives under `Client/Shared` rather than
  being duplicated. `Pptx` and `Pdf` are Local-mode only; `Xgp` works in
  both modes (see the XGP export section).
- `XgpExportOptions` — naming options for a batch of per-decision `.xgp`
  exports (`Prefix`, `StartNumber`, `SuffixLength`). Single source of the
  `{prefix}{number:D{len}}.xgp` rule via `EntryName(index)`. Permissive wire
  DTO — validation is the separate `TryValidate(out error)` (the UI gates on
  it; export pathways throw `ArgumentException`).
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
  `WebModePanel` calls `FilterConfig.Build()` directly in
  `OnParametersSet`, caching the result by `FilterConfig` reference
  identity so the build runs once per Apply, not once per `Matches` call.
  Same `FilterConfig.Build()` method on both sides; the materialization
  point is the only difference.
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
  and re-slicing it would only strip comments. Don't "unify" this branch
  through the slice surface — byte equality with the source is pinned by
  `XgpExportServiceTests`.
- **The XGP counter is client-owned.** The persisted numbering state
  (`xg_xgpLastNumber` et al.) lives in `Home` and advances via the panels'
  `OnXgpExported(count)` callback. Nothing scans previously exported files;
  a user who bypasses the counter (edits "next number" backwards, exports
  into the same place twice with a reset prefix) gets colliding names —
  by design, the editable field is the escape hatch.
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
- **Fixture files are not in this repo.** They live in umbrella `TestData/`
  and are not tracked by git (contents are gitignored; structure is held by
  `.gitkeep`). A fresh clone of this subproject alone cannot run the tests
  without the umbrella `TestData/` present.

## Subproject-internal next steps

- **Streaming JSON write for large datasets.** Current Diagram JSON output is
  a single in-memory array. Very large corpora may need a streaming writer
  (NDJSON or JSON-array streaming) rather than building the full document
  before serializing.
- **Job cleanup / expiry in `JobStore`.** Entries currently live for the
  process lifetime. A single-user local app tolerates that, but long-running
  sessions will accumulate completed jobs. A simple TTL or explicit
  "clear completed" action would address it.
- **Pipeline performance.** The Local-mode pipeline is I/O-dominated but has
  not been profiled. Candidate wins: parallelizing per-file work, reducing
  per-decision allocations in `XgDecisionIterator` consumers, and cutting
  reflection or LINQ in the filter hot path.
