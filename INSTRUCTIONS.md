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

Blazor web app. Extracts decisions from .xg/.xgp files, applies XgFilter_Lib filters, exports CSV.

## Depends on

* **ConvertXgToJson_Lib** — XgDecisionIterator, XgFileReader, XgIteratorState, XgMatchInfo, XgGameInfo
* **XgFilter_Lib** — DecisionFilterSet, FilteredDecisionIterator, ColumnSelector, IDecisionFilter, IMatchFilter
* **BgDataTypes_Lib** (transitive via ConvertXgToJson_Lib and XgFilter_Lib) — DecisionRow, IDecisionFilterData

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
      ProcessingProgress.cs
  ExtractFromXgToCsv.slnx
```

## Architecture

### Server project
- Thin host only — all browser processing in client
- Services: LocalFolderProcessor, JobStore, XgProcessingService
- Controllers: ProcessController, AppModeController, ShutdownController

### Client project
- WASM — owns all .xg parsing, filtering, CSV generation for web mode

### Components
- `Home.razor` — file input (web mode), `_rows` (List<DecisionRow>), `_filterSet`, output path/filename, localStorage persistence, CSV write/download, polling loop (local mode)
- `FilterPanel.razor` — all filter state; raises `OnFiltersChanged EventCallback<DecisionFilterSet>` and `OnFilterConfigChanged EventCallback<FilterConfig>`

### Modes

**Local** (`"AppMode": "Local"`):
- Folder path + output CSV path inputs
- Run → POST `/api/process/start` → jobId; client polls `/api/process/{jobId}/status` every second
- Stop → POST `/api/process/{jobId}/cancel`; Exit → ShutdownController
- Server processes in background Task.Run, writes CSV to disk

**Web/Azure** (`"AppMode": "Web"`):
- Browser file picker; 50MB cap
- WASM processes files in browser
- CSV download button not yet implemented

### Job lifecycle (local mode)
- `JobStore` (singleton) holds `ConcurrentDictionary<jobId, JobEntry>`
- `JobEntry` has `ProcessingProgress` and `CancellationTokenSource`
- Jobs not auto-removed (single-user local app)

## Current status

🔧 In progress — local mode end-to-end working (6,660 files → 951,973 rows in 447s)

## Deferred

* CSV download button for Azure/browser mode
* ColumnSelector wired into UI (column projection)
* 0-rows bug after XGID fix — to be diagnosed
* Job cleanup / expiry in JobStore
* Performance optimization (~14 files/sec)

## Key decisions

* FilterPanel is a separate component under `ExtractFromXgToCsv.Client/Components/`
* All rendering is InteractiveWebAssembly with prerender:false
* XgFilter_Lib and LocalFolderProcessor are server-side only
* Board not exposed in CSV/ColumnSelector
* Home.razor applies DecisionFilterSet to `_rows` before CSV output
* WASM cannot stream HTTP responses — polling used instead
* `reportEvery = 10` (client polls every second)
* ProcessRequest class lives in server ProcessController.cs
* JobStore: AddSingleton; LocalFolderProcessor: AddScoped (both inside Local mode guard)
* Run button disabled when filter is dirty