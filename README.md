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
Xgid | Error | MatchScore | MatchLength | Player | SourceFile | Game | MoveNum | Roll | AnalysisDepth | Equity
```

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
