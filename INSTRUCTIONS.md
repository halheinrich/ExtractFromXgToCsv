# ExtractFromXgToCsv — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon
**After committing here, return to the Backgammon Umbrella project to update hashes and instructions doc.**

## Repo
https://github.com/halheinrich/ExtractFromXgToCsv
**Branch:** main
**Current commit:** `2d6902a`

## Stack
C# / .NET 10 / Blazor / Visual Studio 2026 / Windows

## Solution
`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv\ExtractFromXgToCsv.slnx`

## Purpose
Blazor web app. Extracts decisions from .xg/.xgp files, applies XgFilter_Lib filters, exports CSV.

## Depends on
- ConvertXgToJson_Lib (umbrella pinned: `398999f`)
- XgFilter_Lib (umbrella pinned: `ef7c7de`)

## Naming convention
Always specify which Program.cs is being modified:
- Server: `ExtractFromXgToCsv\ExtractFromXgToCsv\Program.cs`
- Client: `ExtractFromXgToCsv\ExtractFromXgToCsv.Client\Program.cs`

## Session start verification

Before any planning or coding, verify the repo hash:
```powershell
cd "D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv"
git rev-parse --short HEAD
git log --oneline -1 origin/main
```
Report the result and wait for explicit confirmation before proceeding.

## Repo directory tree

```
ExtractFromXgToCsv/
  ExtractFromXgToCsv/
    Components/
      Pages/
        (empty — razor pages are in Client project)
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
    ExtractFromXgToCsv.csproj
    Program.cs
  ExtractFromXgToCsv.Client/
    Components/
      Pages/
        Home.razor
      FilterPanel.razor
    Shared/
      FilterConfig.cs
      ProcessingProgress.cs
    ExtractFromXgToCsv.Client.csproj
    Program.cs
  ExtractFromXgToCsv.slnx
  INSTRUCTIONS.md
```

## Key files

All URLs use `raw.githack.com` at pinned commit hash. Source files unchanged since `132a723`; INSTRUCTIONS.md updated at `2d6902a`.

* INSTRUCTIONS.md: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/2d6902a/INSTRUCTIONS.md
* ExtractFromXgToCsv.csproj: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/ExtractFromXgToCsv.csproj
* Program.cs (server): https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Program.cs
* Program.cs (client): https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv.Client/Program.cs
* Services/XgProcessingService.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Services/XgProcessingService.cs
* Services/LocalFolderProcessor.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Services/LocalFolderProcessor.cs
* Services/JobStore.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Services/JobStore.cs
* Controllers/ProcessController.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Controllers/ProcessController.cs
* Controllers/AppModeController.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Controllers/AppModeController.cs
* Controllers/ShutdownController.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv/Controllers/ShutdownController.cs
* Components/Pages/Home.razor: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv.Client/Components/Pages/Home.razor
* Components/FilterPanel.razor: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv.Client/Components/FilterPanel.razor
* Shared/FilterConfig.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv.Client/Shared/FilterConfig.cs
* Shared/ProcessingProgress.cs: https://raw.githack.com/halheinrich/ExtractFromXgToCsv/132a723/ExtractFromXgToCsv.Client/Shared/ProcessingProgress.cs

## Dependency files

Fetch URLs from umbrella INSTRUCTIONS.md at currently pinned hashes.

### ConvertXgToJson_Lib (umbrella pinned: `398999f`)
Files needed:
* ConvertXgToJson_Lib/VersionInfo.cs
* ConvertXgToJson_Lib/Models/DecisionRow.cs
* ConvertXgToJson_Lib/XgDecisionIterator.cs

### XgFilter_Lib (umbrella pinned: `ef7c7de`)
Files needed:
* XgFilter_Lib/Filtering/IDecisionFilter.cs
* XgFilter_Lib/Filtering/DecisionFilterSet.cs
* XgFilter_Lib/FilteredDecisionIterator.cs
* XgFilter_Lib/Projection/ColumnSelector.cs

## Architecture

### Server project
- Thin host only — all browser processing in client
- Owns: AppModeService, LocalFolderProcessor, JobStore, ProcessController, AppModeController, ShutdownController

### Client project
- WASM client — owns all .xg parsing, filtering, and CSV generation for web mode

### Component structure
- `Home.razor` — owns: file input (web mode), `_rows` (List<DecisionRow>), `_filterSet`
  (DecisionFilterSet), output path/filename, localStorage persistence, CSV write/download,
  polling loop for local mode progress
- `FilterPanel.razor` — owns: all filter state; raises `OnFiltersChanged`
  EventCallback<DecisionFilterSet> and `OnFilterConfigChanged` EventCallback<FilterConfig>

### Modes
- **Local:** (`appsettings.json` `"AppMode": "Local"`)
  - Folder path + output CSV path inputs
  - FilterPanel always visible
  - Run button → POST `/api/process/start` → returns `{ jobId }`
  - Client polls `/api/process/{jobId}/status` every second
  - Stop button → POST `/api/process/{jobId}/cancel`
  - Exit button → ShutdownController
  - Server processes files in background Task.Run, writes CSV to disk
  - Progress bar + status line update live via polling
  - No data size limit

- **Web/Azure:** (`appsettings.json` `"AppMode": "Web"`)
  - Browser file picker; 50MB total size cap enforced in UI
  - WASM processes files in browser
  - Download CSV button (not yet implemented)
  - Preview table (first 20 rows)

### Job lifecycle (local mode)
- `JobStore` (singleton) holds `ConcurrentDictionary<jobId, JobEntry>`
- `JobEntry` has `ProcessingProgress Progress` and `CancellationTokenSource Cts`
- `ProcessController.Start` fires `Task.Run`, returns jobId immediately
- `ProcessController.Status` returns current `JobEntry.Progress`
- `ProcessController.Cancel` calls `entry.Cts.Cancel()`
- Jobs are not auto-removed from store (low volume; single user local app)

## Current status
✅ End-to-end working — local mode tested: 6,660 files → 951,973 rows in 447s (14 files/sec)

## Deferred
- CSV download button for Azure/browser mode
- ColumnSelector wired into UI (column projection)
- PlayTypeFilter implementation in XgFilter_Lib
- Priming, Blitz, HoldingGame classifiers in XgFilter_Lib
- Job cleanup / expiry in JobStore (not needed for single-user local app)
- Performance optimization (currently ~14 files/sec on 6,660 file dataset)
- ExtractFromXgToCsv gets 0 rows after XGID fix — to be diagnosed

## Key decisions
- FilterPanel is a separate component under `ExtractFromXgToCsv.Client/Components/`
- All rendering is InteractiveWebAssembly with prerender:false
- XgFilter_Lib and LocalFolderProcessor are server-side only
- Board not exposed in CSV/ColumnSelector
- Always quote paths with spaces in PowerShell
- FilterPanel.razor owns all filter UI state; raises OnFiltersChanged and OnFilterConfigChanged
- Home.razor applies DecisionFilterSet to _rows before CSV output (web mode)
- WASM cannot stream HTTP responses — polling used instead
- LocalFolderProcessor.reportEvery = 10 (client polls every second; no need to update every file)
- ProcessRequest class lives in server ProcessController.cs — server owns it
- JobStore registered as AddSingleton; LocalFolderProcessor as AddScoped (both inside Local mode guard)
- Run button disabled when filter is dirty (not yet applied)

## Shared rules
See AGENTS.md in the umbrella repo. Fetch with pinned hash — never `main`:
```powershell
git log --oneline -3 -- AGENTS.md
```
from the umbrella root to get the correct hash.