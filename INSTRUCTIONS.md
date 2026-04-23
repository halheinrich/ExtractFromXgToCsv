# ExtractFromXgToCsv

> Session conventions: [`../CLAUDE.md`](../CLAUDE.md)
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
  for .xg/.xgp reading and decision iteration.
- **XgFilter_Lib** — `DecisionFilterSet`, `FilteredDecisionIterator`,
  `ColumnSelector`, `IDecisionFilter`, `IMatchFilter` for filter pipeline.
- **BgDataTypes_Lib** — `DecisionRow`, `BgDecisionData`, `IDecisionFilterData`
  and constituent types. All four output pathways
  (CSV / Diagram JSON / PPTX / PDF) share the filter pipeline via
  `IDecisionFilterData`.
- **BackgammonDiagram_Lib** — `DiagramRequest.FromDecisionData`,
  `DiagramRenderer.RenderPptx` / `RenderPdf` for the PPTX and PDF output
  pathways. Server-side only; the client csproj does not reference it
  (Skia native isn't available under WASM — see Pitfalls).
- **QuestPDF** (transitive, via BackgammonDiagram_Lib) — PDF builder. A
  license must be configured at server startup before `RenderPdf` is
  invoked; see Pitfalls.

## Directory tree

```
ExtractFromXgToCsv.slnx
ExtractFromXgToCsv/                     — server host (thin)
  ExtractFromXgToCsv.csproj
  Program.cs
  appsettings.json
  appsettings.Development.json
  Controllers/
    AppModeController.cs
    ProcessController.cs                — primary-constructor DI
    ShutdownController.cs
  Services/
    FilterSetBuilder.cs                 — public static, builds DecisionFilterSet
    JobStore.cs                         — singleton, job registry
    LocalFolderProcessor.cs             — scoped, runs the pipeline for Local mode
    XgProcessingService.cs
ExtractFromXgToCsv.Client/              — WASM
  ExtractFromXgToCsv.Client.csproj
  Program.cs
  Pages/
    Home.razor                          — mode-detecting shell (~75 lines)
  Components/
    FilterPanel.razor                   — filter UI, always visible both modes
    LocalModePanel.razor                — folder/output inputs, polling loop
    WebModePanel.razor                  — file picker, in-memory preview, download
  Services/
    XgProcessingService.cs
  Shared/
    FilterConfig.cs                     — serializable filter DTO
    OutputFormat.cs                     — Csv | DiagramJson | Pptx | Pdf
    ProcessingProgress.cs
ExtractFromXgToCsv.Tests/
  ExtractFromXgToCsv.Tests.csproj
  FixtureHelper.cs
  FilterSetBuilderTests.cs
  LocalFolderProcessorPptxTests.cs
  LocalFolderProcessorPdfTests.cs
  XgProcessingServiceTests.cs
  OutputConsistencyTests.cs
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
filters, emit CSV or Diagram JSON. File cap is 50 MB.

PPTX and PDF are **not** offered in Web mode — `BackgammonDiagram_Lib`'s
deck paths go through `SkiaSharp` to rasterize SVG into the slide/page PNG,
and Skia's native binary isn't available under Blazor WASM. The PPTX and
PDF radios are disabled client-side when `AppMode != "Local"` and a
persisted `xg_outputFormat` of `Pptx` or `Pdf` is sanitised to `Csv` on
load. (See Pitfalls.)

Web mode extracts both `DecisionRow` (for CSV) and `BgDecisionData` (for
Diagram JSON) on file selection — keeps the two output formats in sync so
toggling the output-format radio doesn't require re-processing.

### Components

- **`Home.razor`** — shell. Detects app mode, owns shared state (output format,
  filter set, filter config, filter applied/dirty), renders the output-format
  radio and `FilterPanel`, and delegates to `LocalModePanel` or `WebModePanel`.
- **`FilterPanel.razor`** — owns all filter state. Raises
  `OnFiltersChanged EventCallback<DecisionFilterSet>`,
  `OnFilterConfigChanged EventCallback<FilterConfig>`, and
  `OnFilterDirty EventCallback`. Always visible in both modes so the workflow
  is consistent: configure filters → select files → run.
- **`LocalModePanel.razor`** — folder/output-path inputs, Run/Stop/Exit
  buttons, polling loop, progress bar. Parameters:
  `OutputFormat`, `FilterConfig`, `FilterApplied`, `FilterDirty`.
  Takes `FilterConfig` (serializable) so it can POST it to the server.
- **`WebModePanel.razor`** — file picker, in-memory rows and diagram rows,
  live filtering in `OnParametersSet`, preview table, download. Parameters:
  `OutputFormat`, `FilterSet`, `FilterApplied`, `FilterDirty`.
  Takes `DecisionFilterSet` directly — no HTTP boundary to serialize across.

Run button is disabled whenever the filter panel is dirty, forcing the user
to apply or discard pending changes before a run.

### Modes

**Local** (`"AppMode": "Local"`):

- Folder path + output path inputs. Output format: CSV, Diagram JSON, PPTX,
  or PDF; persisted to `localStorage` under key `xg_outputFormat`.
- Run → `POST /api/process/start` → jobId. Client polls
  `GET /api/process/{jobId}/status` every second.
- `ProcessController` dispatches to `LocalFolderProcessor.ProcessAsync` (CSV),
  `ProcessDiagramAsync` (Diagram JSON), `ProcessPptxAsync` (PPTX), or
  `ProcessPdfAsync` (PDF) based on `ProcessRequest.OutputFormat`. The PPTX
  and PDF public methods are one-line wrappers around a shared private
  `ProcessDeckAsync` helper parameterised on the renderer delegate — both
  collect filtered decisions, map them via `DiagramRequest.FromDecisionData`,
  and expand into Problem/Solution pairs rendered via
  `DiagramRenderer.RenderPptx` or `RenderPdf`.
- Stop → `POST /api/process/{jobId}/cancel`. Exit → `ShutdownController`.

**Web/Azure** (`"AppMode": "Web"`):

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
(`reportEvery = 10` in the processor writes progress on every 10th file).

Jobs are not auto-removed — this is a single-user local app and the dictionary
lives only for the process lifetime.

### Filter pipeline

`FilterSetBuilder` (public static, server-side) converts a `FilterConfig` DTO
into a `DecisionFilterSet`. Extracted from `ProcessController` so Local mode
and the test project share the same construction path. CSV, Diagram JSON, and
PPTX, and PDF pathways all feed the same filter set via
`IDecisionFilterData` — one filter implementation, four extraction outputs.

### Diagram JSON output

Emitted as a single in-memory JSON array, not NDJSON. Simpler consumer side;
streaming is deferred (see next steps) and will matter only for very large
corpora.

### Deck output (PPTX / PDF)

Local mode only. Filtered `BgDecisionData` is buffered in memory, mapped to
`DiagramRequest` via `DiagramRequest.FromDecisionData`, then expanded to a
Problem/Solution pair via `DiagramRequestExtensions.ToProblemSolutionPair`.
The pairs are flattened into a single deck (two slides/pages per decision —
problem first, then solution) via `DiagramRenderer.RenderPptx` or
`RenderPdf`. Render defaults to `new DiagramOptions()` — default theme,
16:9 aspect, no pip count. Render is atomic (the lib returns `byte[]`);
cancel works during the per-file collect loop, not during render.

`ProcessPptxAsync` and `ProcessPdfAsync` are thin wrappers around a private
`ProcessDeckAsync` helper that takes a
`Func<IEnumerable<DiagramRequest>, DiagramOptions, byte[]>` renderer and a
format label used only in progress messages. The two public methods differ
only in which renderer they pass.

### Test project

xUnit, targets .NET 10. Fixture files live in the umbrella
`TestData/FixtureFiles/*.xg` directory and are referenced from the test
project via relative path — not duplicated here.

- `FilterSetBuilderTests` — DTO → filter-set round trips.
- `XgProcessingServiceTests` — end-to-end Local-mode pipeline against
  fixture files.
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

## Public API

### HTTP endpoints (Local mode only)

```
POST /api/process/start
  body:  ProcessRequest { FolderPath, OutputPath, FilterConfig, OutputFormat }
  200 →  { JobId }

GET  /api/process/{jobId}/status
  200 →  ProcessingProgress { FilesProcessed, TotalFiles, DecisionsWritten,
                              IsComplete, ErrorMessage? }

POST /api/process/{jobId}/cancel
  200 →  (empty)

GET  /api/appmode
  200 →  { Mode: "Local" | "Web" }

POST /api/shutdown
  200 →  (empty; host begins graceful shutdown)
```

`ProcessRequest` lives in the server `ProcessController.cs`. The server owns
its own request shape; the client serialises `FilterConfig` into it.

### Client-shared types (`ExtractFromXgToCsv.Client/Shared/`)

- `OutputFormat` — enum `Csv | DiagramJson | Pptx | Pdf`. Server references
  it too, which is why it lives under `Client/Shared` rather than being
  duplicated. `Pptx` and `Pdf` are Local-mode only.
- `FilterConfig` — serializable DTO consumed by `FilterSetBuilder`.
- `ProcessingProgress` — status-endpoint payload.

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
- **Deck output (PPTX / PDF) requires server-side rendering.** Both deck
  paths go through `BackgammonDiagram_Lib.DiagramRenderer`
  (`RenderPptx` / `RenderPdf`), which rasterizes SVG via SkiaSharp before
  assembling the deck. SkiaSharp's native binary isn't available under
  Blazor WASM, so PPTX and PDF are offered in Local mode only. Don't add a
  `BackgammonDiagram_Lib` reference to `ExtractFromXgToCsv.Client.csproj` —
  it would either fail at runtime or pull
  `SkiaSharp.NativeAssets.WebAssembly` into the WASM payload (multi-MB
  bloat, AOT/Skia friction).
- **QuestPDF license must be set at server startup.** `RenderPdf` throws if
  `QuestPDF.Settings.License` is unset. `Program.cs` assigns
  `LicenseType.Community` unconditionally (Web mode never calls
  `RenderPdf`, so there's no harm in setting it always). Community is
  appropriate for the current non-commercial posture
  (revenue ≤ \$1M / year); revisit if commercial distribution ever becomes
  a goal.
- **Two DTOs on the mode boundary.** `LocalModePanel` takes a `FilterConfig`
  (serializable) because it crosses the HTTP boundary; `WebModePanel` takes
  a `DecisionFilterSet` (in-memory, non-serializable) because it doesn't.
  Don't unify them — the serializability split is load-bearing.
- **`FilterPanel` lives in the Client project only.** The server doesn't
  reference Razor components. `FilterSetBuilder` is the server-side
  equivalent — it takes a `FilterConfig` DTO, not a panel.
- **Run button dirty-gating.** The Run button is disabled whenever the
  filter panel has unapplied changes. If a test or UI change ever makes
  the button appear enabled with a dirty filter, that's a regression — the
  dirty state is the gate, not a cosmetic hint.
- **CA1859 in `OutputConsistencyTests`.** The interface usage is the thing
  under test (the shared-pipeline contract). Don't "fix" the warning by
  switching to concrete types — you'd defeat the test.
- **Fixture files are not in this repo.** They live in umbrella `TestData/`
  and are not tracked by git (contents are gitignored; structure is held by
  `.gitkeep`). A fresh clone of this subproject alone cannot run the tests
  without the umbrella `TestData/` present.

## Subproject-internal next steps

- **Streaming JSON write for large datasets.** Current Diagram JSON output is
  a single in-memory array. Very large corpora may need a streaming writer
  (NDJSON or JSON-array streaming) rather than building the full document
  before serialising.
- **Job cleanup / expiry in `JobStore`.** Entries currently live for the
  process lifetime. A single-user local app tolerates that, but long-running
  sessions will accumulate completed jobs. A simple TTL or explicit
  "clear completed" action would address it.
- **Pipeline performance.** The Local-mode pipeline is I/O-dominated but has
  not been profiled. Candidate wins: parallelising per-file work, reducing
  per-decision allocations in `XgDecisionIterator` consumers, and cutting
  reflection or LINQ in the filter hot path.
