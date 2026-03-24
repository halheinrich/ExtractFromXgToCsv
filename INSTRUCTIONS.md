# ExtractFromXgToCsv — Project Instructions

Part of the Backgammon tools ecosystem: https://github.com/halheinrich/backgammon
**After committing here, return to the Backgammon Umbrella project to update hashes and instructions doc.**

## Repo
https://github.com/halheinrich/ExtractFromXgToCsv
**Branch:** main
**Current commit:** `cd1d198`

## Stack
C# / .NET 10 / Blazor / Visual Studio 2026 / Windows

## Solution
`"D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv\ExtractFromXgToCsv.slnx"`

## Depends on
- ConvertXgToJson_Lib (commit `a18e669`)
- XgFilter_Lib (commit `3514390`)

## Naming convention
Always specify which Program.cs is being modified:
- Server: `ExtractFromXgToCsv\ExtractFromXgToCsv\Program.cs`
- Client: `ExtractFromXgToCsv\ExtractFromXgToCsv.Client\Program.cs`

## Key files

### This subproject (commit cd1d198)
- ExtractFromXgToCsv.csproj: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/ExtractFromXgToCsv.csproj
- Program.cs (server): https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/Program.cs
- Program.cs (client): https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv.Client/Program.cs
- Services/XgProcessingService.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/Services/XgProcessingService.cs
- Services/LocalFolderProcessor.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/Services/LocalFolderProcessor.cs
- Services/JobStore.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/Services/JobStore.cs
- Controllers/ProcessController.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/Controllers/ProcessController.cs
- Controllers/AppModeController.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv/Controllers/AppModeController.cs
- Components/Pages/Home.razor: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv.Client/Components/Pages/Home.razor
- Components/FilterPanel.razor: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv.Client/Components/FilterPanel.razor
- Shared/FilterConfig.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv.Client/Shared/FilterConfig.cs
- Shared/ProcessingProgress.cs: https://raw.githubusercontent.com/halheinrich/ExtractFromXgToCsv/cd1d198/ExtractFromXgToCsv.Client/Shared/ProcessingProgress.cs

### ConvertXgToJson_Lib dependency (commit a18e669)
- DecisionRow.cs: https://raw.githubusercontent.com/halheinrich/ConvertXgToJson_Lib/a18e669fa8cb4de458a0b7379dab3e8ce592f2f0/ConvertXgToJson_Lib/Models/DecisionRow.cs

### XgFilter_Lib dependency (commit 3514390)
- Filtering/IDecisionFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/IDecisionFilter.cs
- Filtering/DecisionFilterSet.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/DecisionFilterSet.cs
- Filtering/PlayerFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/PlayerFilter.cs
- Filtering/DecisionTypeFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/DecisionTypeFilter.cs
- Filtering/MatchScoreFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/MatchScoreFilter.cs
- Filtering/ErrorRangeFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/ErrorRangeFilter.cs
- Filtering/PositionTypeFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/PositionTypeFilter.cs
- Filtering/PlayTypeFilter.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Filtering/PlayTypeFilter.cs
- Enums/PositionType.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Enums/PositionType.cs
- Enums/PlayType.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Enums/PlayType.cs
- Classification/IPositionClassifier.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Classification/IPositionClassifier.cs
- Classification/RaceClassifier.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Classification/RaceClassifier.cs
- Classification/ContactClassifier.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Classification/ContactClassifier.cs
- Projection/ColumnSelector.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/Projection/ColumnSelector.cs
- FilteredDecisionIterator.cs: https://raw.githubusercontent.com/halheinrich/XgFilter_Lib/3514390455b9e8795125d3c629560566230f8343/XgFilter_Lib/FilteredDecisionIterator.cs

## Architecture

### Server project
- Thin host only — all browser processing in client
- Owns: AppModeService, LocalFolderProcessor, JobStore, ProcessController, AppModeController

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
  - Server processes files in background Task.Run, writes CSV to disk
  - Progress bar + status line update live via polling
  - No data size limit

- **Web/Azure:** (`appsettings.json` `"AppMode": "Web"`)
  - Browser file picker; 50MB total size cap enforced in UI
  - WASM processes files in browser
  - Download CSV button
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

## In progress
- Nothing

## Deferred
- CSV download button for Azure/browser mode
- ColumnSelector wired into UI (column projection)
- PlayTypeFilter implementation in XgFilter_Lib
- Priming, Blitz, HoldingGame classifiers in XgFilter_Lib
- Job cleanup / expiry in JobStore (not needed for single-user local app)
- Performance optimization (currently ~14 files/sec on 6,660 file dataset)

## Key decisions
- FilterPanel is a separate component under `ExtractFromXgToCsv.Client/Components/`
- All rendering is InteractiveWebAssembly with prerender:false
- XgFilter_Lib and LocalFolderProcessor are server-side only
- Board not exposed in CSV/ColumnSelector
- Always quote paths with spaces in PowerShell
- FilterPanel.razor owns all filter UI state; raises OnFiltersChanged and OnFilterConfigChanged
- Home.razor applies DecisionFilterSet to _rows before CSV output (web mode)
- WASM cannot stream HTTP responses (net_http_synchronous_reads_not_supported) — polling used instead
- LocalFolderProcessor.reportEvery = 10 (client polls every second; no need to update every file)
- ProcessRequest class lives in server ProcessController.cs (not client Shared/) — server owns it
- JobStore registered as AddSingleton; LocalFolderProcessor as AddScoped (both inside Local mode guard)

## Shared rules
Fetch and apply before starting work:
`https://raw.githubusercontent.com/halheinrich/backgammon/main/AGENTS.md`
(GitHub raw URLs are blocked in Claude's container — paste this URL into chat and ask Claude to fetch it.)

## Fetching source files
GitHub (raw.githubusercontent.com) is blocked in Claude's container.
**Workaround:** Ask Claude for the URLs needed, paste them back into the chat.
Claude can then use web_fetch via the user-provided URLs.

## Session handoff
After committing:
1. `git rev-parse HEAD` in this subproject dir — note the short hash
2. Update commit hash in this doc and all raw URLs
3. Add URLs for any new files created
4. Update In progress / Deferred / Key decisions
5. Return to Backgammon Umbrella project — update umbrella instructions doc
