# ExtractFromXgToCsv — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon

## Repo

https://github.com/halheinrich/ExtractFromXgToCsv
**Branch:** main

## Stack

C# / .NET 10 / Blazor / Visual Studio 2026 / Windows

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv\ExtractFromXgToCsv.slnx`

## Purpose

Blazor web app. Extracts decisions from .xg/.xgp files, applies XgFilter_Lib filters, exports CSV or Diagram JSON.

## Depends on

* **ConvertXgToJson_Lib** — XgDecisionIterator, XgFileReader, XgIteratorState, XgMatchInfo, XgGameInfo
* **XgFilter_Lib** — DecisionFilterSet, FilteredDecisionIterator, ColumnSelector, IDecisionFilter, IMatchFilter
* **BgDataTypes_Lib** — DecisionRow, BgDecisionData, PositionData, DecisionData, DescriptiveData, IDecisionFilterData

## Dependency files

### ConvertXgToJson_Lib
* ConvertXgToJson_Lib/XgDecisionIterator.cs
* ConvertXgToJson_Lib/XgFileReader.cs
* ConvertXgToJson_Lib/XgIteratorState.cs

### XgFilter_Lib
* XgFilter_Lib/Filtering/IDecisionFilter.cs
* XgFilter_Lib/Filtering/DecisionFilterSet.cs
* XgFilter_Lib/FilteredDecisionIterator.cs
* XgFilter_Lib/Projection/ColumnSelector.cs

### BgDataTypes_Lib
* BgDataTypes_Lib/DecisionRow.cs
* BgDataTypes_Lib/IDecisionFilterData.cs
* BgDataTypes_Lib/BgDecisionData.cs
* BgDataTypes_Lib/PositionData.cs
* BgDataTypes_Lib/DecisionData.cs
* BgDataTypes_Lib/DescriptiveData.cs

## Naming convention

Always specify which Program.cs is being modified:
- Server: `ExtractFromXgToCsv\ExtractFromXgToCsv\Program.cs`
- Client: `ExtractFromXgToCsv\ExtractFromXgToCsv.Client\Program.cs`

## Directory tree

```
ExtractFromXgToCsv/
ExtractFromXgToCsv/
ExtractFromXgToCsv.csproj
Program.cs
Controllers/
AppModeController.cs
ProcessController.cs
ShutdownController.cs
Services/
JobStore.cs
LocalFolderProcessor.cs
XgProcessingService.cs
appsettings.json
appsettings.Development.json
ExtractFromXgToCsv.Client/
ExtractFromXgToCsv.Client.csproj
Program.cs
Components/
FilterPanel.razor
Pages/
Home.razor
Shared/
FilterConfig.cs
OutputFormat.cs
ProcessingProgress.cs
ExtractFromXgToCsv.slnx
```

## Architecture

### Server project
- Thin host only — all browser processing in client
- Services: LocalFolderProcessor, JobStore, XgProcessingService
- Controllers: ProcessController, AppModeController, ShutdownController

### Client project
- WASM — owns all .xg parsing, filtering, CSV/JSON generation for web mode

### Components
- `Home.razor` — file input (web mode), `_rows` (List<DecisionRow>), `_diagramRows` (List<BgDecisionData>), `_filterSet`, `_outputFormat`, output path/filename, localStorage persistence, CSV/JSON download, polling loop (local mode), radio buttons for output format
- `FilterPanel.razor` — all filter state; raises `OnFiltersChanged EventCallback<DecisionFilterSet>` and `OnFilterConfigChanged EventCallback<FilterConfig>`

### Modes

**Local** (`"AppMode": "Local"`):
- Folder path + output CSV/JSON path inputs
- Output format radio buttons: CSV / Diagram JSON
- Run → POST `/api/process/start` → jobId; client polls `/api/process/{jobId}/status` every second
- Server calls `ProcessAsync` (CSV) or `ProcessDiagramAsync` (Diagram JSON) based on `ProcessRequest.OutputFormat`
- Stop → POST `/api/process/{jobId}/cancel`; Exit → ShutdownController

**Web/Azure** (`"AppMode": "Web"`):
- Browser file picker; 50MB cap
- WASM processes files in browser; both `ExtractDecisions` and `ExtractDiagramRequests` run on file selection
- Download button produces CSV or JSON based on output format toggle
- CSV download button not yet implemented (pre-existing — download logic is wired but untested)

### Job lifecycle (local mode)
- `JobStore` (singleton) holds `ConcurrentDictionary<jobId, JobEntry>`
- `JobEntry` has `ProcessingProgress` and `CancellationTokenSource`
- Jobs not auto-removed (single-user local app)

## Current status

🔧 In progress — local mode end-to-end working for both CSV and Diagram JSON output

## Deferred

* Streaming JSON write for large datasets (in-memory approach may hit limits on very large corpora)
* xUnit test project — planned: BuildFilterSet tests, output consistency tests, XgProcessingService tests
* Home.razor refactor into mode-specific components (LocalModePanel / WebModePanel)
* FilterSetBuilder extraction from ProcessController.BuildFilterSet
* ColumnSelector wired into UI (column projection)
* 0-rows bug after XGID fix — to be diagnosed
* Job cleanup / expiry in JobStore
* Performance optimization (~14 files/sec)

## Key decisions

* FilterPanel is a separate component under `ExtractFromXgToCsv.Client/Components/`
* All rendering is InteractiveWebAssembly with prerender:false
* XgFilter_Lib and LocalFolderProcessor are server-side only
* Board not exposed in CSV/ColumnSelector
* Home.razor applies DecisionFilterSet to `_rows` / `_diagramRows` before output
* WASM cannot stream HTTP responses — polling used instead
* `reportEvery = 10` (client polls every second)
* ProcessRequest class lives in server ProcessController.cs — server owns it
* JobStore: AddSingleton; LocalFolderProcessor: AddScoped (both inside Local mode guard)
* Run button disabled when filter is dirty
* Diagram JSON output uses in-memory JSON array serialization (not NDJSON)
* Both CSV and Diagram JSON output share the same filter pipeline via IDecisionFilterData
* OutputFormat enum lives in client Shared/ so both server and client can reference it
* Web mode extracts both DecisionRow and BgDecisionData on file selection to keep formats in sync
