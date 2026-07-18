# ExtractFromXgToCsv

Blazor Web App (Auto / .NET 9) that reads `.xg` and `.xgp` files from eXtreme Gammon and exports checker-play & cube decisions as CSV.

Part of the [backgammon tools umbrella repo](https://github.com/halheinrich/backgammon).

---

## Architecture

| Project | Purpose |
|---|---|
| `ExtractFromXgToCsv` | Server-side Blazor (SSR + Interactive Server) |
| `ExtractFromXgToCsv.Client` | WebAssembly client project (Auto render mode) |

### Key services

| Class | Responsibility |
|---|---|
| `XgProcessingService` | Detect local vs Azure; scan folders; call `XgDecisionIterator`; build CSV; write to disk |
| `CsvDownloadService` | JS-interop helper — triggers browser "Save As" download |

### Data model

`DecisionRow` record — mirrors `DecisionRow` from **ConvertXgToJson_Lib**:

```
Xgid | Error | MatchScore | MatchLength | Player | SourceFile | Game | MoveNumber | Roll | AnalysisDepth | Equity
```

---

## Opening-book enrichment

`.xg` files stamp opening-book–analysed plays with a bare "book" marker that
does not record which cached rollout XG used. When XG's opening-book database
(`OpeningBookV2.ob`, ~13.6 MB, installed with eXtreme Gammon 2) is supplied,
those decisions are **enriched**: the `AnalysisDepth` column reports the cached
rollout's parameters — e.g. `Book V2: 12960 trials. 4-ply` instead of a bare
`Book V2` — and the decision's analysis level is recovered, so depth filters
see `4-ply` rather than `Unknown`. Enrichment is strictly additive: it changes
labels and levels, never which decisions are emitted. The book is optional —
without it, extraction still runs and book decisions report level `Unknown`.

### Local mode (server)

The server loads the book once. Path resolution:

1. the `OpeningBookPath` key in `appsettings.json`, when set to a non-empty path;
2. when the key is **absent**, the default install location
   `C:\Program Files (x86)\eXtreme Gammon 2\OpeningBookV2.ob` is auto-detected;
3. when the key is present but **empty** (`""`), enrichment is disabled.

A missing or unreadable book is logged and skipped — it never fails a run. The
folder-input status line reports whether the server loaded a book (and its
entry count).

### Web mode (browser)

The browser parses `.xg` files client-side, so the book is offered the same
way: an optional **Opening book (.ob)** file input beside the `.xg` picker.
Pick `OpeningBookV2.ob` to enrich; skip it to extract unenriched. A status line
shows the loaded entry count. A book chosen *after* the `.xg` files re-processes
the retained bytes, so pick order doesn't matter.

---

## Local vs Azure detection

`XgProcessingService.IsLocalEnvironment` returns `true` when:
- Not in Production **or**
- The `WEBSITE_INSTANCE_ID` environment variable is absent (set by Azure App Service)

In **local mode** the UI shows a folder-path text box.  
In **Azure mode** the UI shows a multi-file `<InputFile>` upload control.  
Both modes produce the same CSV output and offer a browser download.

---

## Wiring up ConvertXgToJson_Lib

1. Add the submodule:
   ```
   git submodule add https://github.com/halheinrich/ConvertXgToJson_Lib ConvertXgToJson_Lib
   ```
2. In `ExtractFromXgToCsv.csproj` uncomment:
   ```xml
   <ProjectReference Include="..\ConvertXgToJson_Lib\ConvertXgToJson_Lib.csproj" />
   ```
3. In `XgProcessingService.ExtractDecisions` replace the stub block with:
   ```csharp
   var iterator = new XgDecisionIterator(fileBytes);
   return iterator.ToList();
   ```

---

## GitHub setup

```bash
# From D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv
git init
git remote add origin https://github.com/halheinrich/ExtractFromXgToCsv
git add .
git commit -m "Initial project skeleton"
git push -u origin main

# Then in the umbrella repo
cd ..
git submodule add https://github.com/halheinrich/ExtractFromXgToCsv ExtractFromXgToCsv
git commit -m "Add ExtractFromXgToCsv submodule"
```

---

## Running locally

```
dotnet run --project ExtractFromXgToCsv
```

Then open `https://localhost:5001`, enter a folder path such as `C:\XG\Matches`, and click **Process Folder**.
