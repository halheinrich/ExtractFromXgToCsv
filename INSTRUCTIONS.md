# ExtractFromXgToCsv

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## What it is

Hal's local tool for getting decisions out of eXtreme Gammon files: point it
at a folder of `.xg` / `.xgp` files, filter the decisions in them, and write
what passes as CSV, Diagram JSON, a PPTX or PDF deck, or one `.xgp` position
file per decision. Local-only since Hal's ruling of 2026-10-19
(halheinrich/backgammon#424): the browser names folders on this machine and
the server reads and writes them, so `Local` in a name (`LocalFolderProcessor`,
`LocalModePanel`) now distinguishes nothing. C# / .NET 10 Blazor Web App: an
ASP.NET Core host (`ExtractFromXgToCsv`) that runs each job over the file
system, and a WebAssembly client (`ExtractFromXgToCsv.Client`) that is the
whole UI. Nothing consumes this repo.

Depends on:

- **ConvertXgToJson_Lib**: `XgSourceReader`, the one read of a source;
  discovery; the XGP slice and anonymize-copy; the opening book;
  `XgIteratorState`, whose `XgOmission`s are what the walk left out of a file.
  BgMoveGen comes with it: nothing here names it, and the solution lists it
  because the solution lists what its references reach.
- **XgFilter_Lib**: `FilterConfig.Build`, which the server calls on the filter
  a run sends, and `FilteredDecisionIterator`, which filters every decoded
  file under that file's own state.
- **BgDataTypes_Lib**: `DecisionRow`, the decision records, `PlayRanking`, the
  strict enum converter.
- **XgFilter_Razor**: the filter surface and its app-scoped owner
  `FilterSetup`. The client's reference; no host code names it.
- **BgUiPrimitives_Razor**: `Notice`, browser storage and its refusal
  condition. The client's reference; no host code names it.
- **BackgammonDiagram_Lib with its ExportRaster assembly**: the decks. Host
  only.
- **Tests only**: the TestSupport assemblies of BgDataTypes_Lib,
  BgUiPrimitives_Razor and XgFilter_Razor.

## Layout

- **`ExtractFromXgToCsv`** — the host: `Program.cs` (the registrations, the
  QuestPDF licence), `Components/` (the root document, the routes, the static
  error page), `wwwroot/app.css` (the busy-cursor rule), vendored Bootstrap.
  `Controllers/`: `ProcessController` (the job protocol and the job runner),
  `OpeningBookController`, `FilterDocumentController` (the saved-filters
  file relay, over `FilterDocumentStore`). `Services/`:
  `LocalFolderProcessor` (the four pathways' one walk), `RunRecord` (a run's
  accounting), `JobStore`, `IOutputDestination` and the destination failures
  (`DestinationFailures.cs`), `CsvLayout` (the CSV's columns, stated once),
  `OpeningBookProvider`.
- **`ExtractFromXgToCsv.Client`** — `Services/`: `ExtractApp` (the one
  coordinator), `LocalRunController` (the run it holds), `ILocalJobs` /
  `LocalJobsClient` (the protocol's wire), `LocalJobTiming` and
  `IJobIdentities` (seams for a test's waits and identities),
  `HttpDocumentStorage` (the saved-filters store over the relay: one instance,
  the coordinator's, whose doc says why).
  `Components/`: `Pages/Home.razor` (the settings, the XGP options and their
  preview, the filter surface), `LocalModePanel` (Run and Stop, progress and
  the result lines), `SkipListNotice` (the one rendering of a skip record).
  `Shared/`: the wire types both projects use (`ProcessRequest`, the
  protocol's `StartRequest` / `StartResponse` / `ProtocolRefusal`,
  `ProcessingProgress` with `SkipRecord`, `XgpExportCommit` and
  `StoppedOutput`, `StopStage`, `JobPhase`, `OutputFormat`,
  `OpeningBookStatus`), the XGP naming engine (`XgpExportOptions`,
  `XgpNameAllocator` and its internal parts, `XgpNamePreview`), `XgpPayload`,
  `NumberedFileName` (the one ` (n)` rule), `AppRanking`.
- **`ExtractFromXgToCsv.Tests`** — xUnit and bUnit, one class per contract,
  each stating its contract in its doc comment. The harnesses:
  `LocalProtocolTesting` (the real server in memory behind a `ProtocolWire`
  a test can fault, a `FileGate` that holds a run at its next create, and
  `LocalPage`), `LocalPanelHost` and `CapturingProcessHandler` (the panel
  over a stub server), `HomeTesting`, `ScriptedDestination`,
  `RecordingProgress`, `RecordingDeckRenderer`, `SyntheticXg`,
  `LocalGoldenBaseline`.

## How it works

`ExtractApp` takes BgQuiz_Blazor's `QuizApp` shape (Hal's ruling,
2026-10-19, halheinrich/backgammon#424): one coordinator, scoped — one per
loaded app — owns the **phase** (`Restoring`, `Idle`, `Running`: the one
operation in flight), the **settings** and their storage, the **source**
reported to `FilterSetup`, and the **run**, a `LocalRunController` it holds
privately. Pages render it, send commands and re-render on `Changed`; a
command that does not fit the phase is refused before any await. The run in
hand (`IsRunInHand`, click to published outcome) is a second fact beside the
phase (`Running`, to the job's acknowledgement): it drives Stop, the busy
cursor and "Starting…", while the phase keeps Run refused until an XGP
commit has reached the controls.

- **The restore** (`EnsureRestoredAsync`) runs once per app, whoever asks:
  every setting read, then the source reported. Until it ends nothing is
  reported and Home shows neither the settings nor the panel, since
  `ReportSource(null)` is a real answer that ends a setup.
- **The source** is the folder path latched at the input's change boundary;
  its token is `FilterSourceToken.FromPath`'s, so path identity is the
  library's, and the output path is no source. How long a result and a skip
  record last is `../SPEC-notices.md` §7.2; `LocalRunController` holds both,
  judged against the source *visit* (`SourceVisit`), so a return to the same
  folder is a new visit.
- **The job protocol** is `../SPEC-notices.md` §7.5. The page's half is
  `LocalRunController`, the server's `JobStore`, which judges a start by the
  identity the browser minted; `ProcessController` only carries the
  judgements. What §7.5 leaves to the app is the words: a Stop pressed before
  the start is answered is hinted "Stop requested; waiting for the server."
  (Hal, 2026-10-22, halheinrich/backgammon#424), and a delivered one shows
  "Stopping…" until the terminal result.
- **The processor's one walk** (`ForEachFileAsync`) is where the boundaries of
  `../SPEC-notices.md` §7.1 live, and all four pathways share it: a pathway
  supplies only its stage and its commit. An entry reaches the run's
  `RunRecord` through one of two seams: a failure through `RecordSkip`, and
  what the walk left out of a file it read through `RecordOmissions`. The
  second reads the file's own `XgIteratorState` when its stage ends, however
  it ended, so a file's omissions precede its conversion skip and outlive a
  stop or a failed commit, and it logs nothing, since the producer's warning
  is the one line an omission gets. A file with omissions is read and
  finished like any other, and contributes what it has. A forced play and an
  illegal play are no entry, and `ForcedPlayCount` goes unread (Hal's rulings,
  2026-11-02, halheinrich/backgammon#514). The record covers what the run
  examined: a game a filter's early exit skipped says nothing, so a filtered
  run can list less than an unfiltered one, and an empty record does not show
  that the inputs are clean. The files it writes follow §7.5's create-new
  under ` (n)`; "Done" names the file as created (`WrittenPath`), and for XGP,
  whose output path is a folder, the folder.
- **A run's accounting is its record's.** The pathways report every tenth
  file and a run-ending exception reports nothing, so the job runner builds
  the stopped and failed snapshots from the `RunRecord` alone: the skips, the
  XGP commit, and for a stop the file reached, where it stood (`StopStage`,
  which `RunRecord` derives from the files reached and finished, and says why
  never from the total), the time at the stop and what it left at its output
  (`StoppedOutput`: nothing, a CSV closed holding exactly its rows, or a file
  whose contents are uncertain). A run's token is checked between files,
  during a read, and immediately before a CSV, Diagram JSON or deck file is
  created, so a run stopped before that write writes nothing. An XGP run's
  position files are created without a check: a stop takes effect at the next
  input file, and the commit counts what was written. A deck's render takes
  the run's token (BackgammonDiagram_Lib's Architecture, Export, says how soon
  that ends it); the check before the create still catches a render that
  returns despite a stop. The stopped line composes where the run stood and
  what it left, opening then ending, in Hal's wording (chat, 2026-10-23,
  halheinrich/backgammon#424 6371257599).
- **XGP naming.** A pattern of literals and `{token}`s (`XgpNameTokens.All`
  is the registry), parsed by `XgpNameTemplate` and issued per run by
  `XgpNameAllocator`: Peek, then Commit once the file is written, so a
  skipped decision consumes no number. Numbering continuity is not a
  requirement (Hal's ruling, 2026-10-19, halheinrich/backgammon#424; §7.5):
  the pattern, the next number and its length are three remembered
  settings, and create-new under ` (n)` is what protects files.
- **One ranking.** `AppRanking.Ranking` is stated once and handed to every
  producer operation that depends on a ranking, the processor's constructor
  included (`../SPEC-scoring.md` §2a); no producer default stands in, even
  where it agrees today (Hal, 2026-09-29, halheinrich/backgammon#273).
- **The opening book** (`OpeningBookProvider`, loaded once): the
  `OpeningBookPath` key when it holds a path, XG's default install when absent
  (`appsettings.json` carries no key), none when empty or blank. A missing or
  unreadable book never fails a run: book decisions then show level Unknown,
  and the panel says no book is loaded. The book enriches depth metadata, and
  recovered levels can change which decisions pass depth filters.
- **Browser storage.** Every call goes through BgUiPrimitives_Razor's
  `BrowserStorage` (Hal's ruling, halheinrich/backgammon#374), its owners
  shared because common code has one shared owner (Hal's ruling, 2026-10-19,
  halheinrich/backgammon#424): the coordinator's keys through
  `ReportingBrowserStorage`, each with its consequence phrase, the filter
  surface's refusals through XgFilter_Razor's adapter, both to the one
  `BrowserStorageCondition` Home's notice reads; `AddFilterSurface()`
  registers it all.
- **The tests' layers.** bUnit renders over `AddClientServices`, the app's
  own registration. Storage is planned (`BrowserStoragePlan`) where it is the
  subject and `Loose` where it is incidental; the tests that need a store to
  outlive a page run over `MemoryBrowserStorage`, a fake `IJSRuntime` in
  `LocalProtocolTesting.cs` that spells the wire BgUiPrimitives_Razor owns
  (halheinrich/backgammon#464). State is reached through commands, gestures
  and the wire, never seeded; the one reflection is the name allocator's
  private-state reader (`bUnitTestHelpers`). A test that reads reports or
  depends on their timing uses `RecordingProgress`; a handlerless
  `new Progress<ProcessingProgress>()` that nothing reads stays.

## Running it

`dotnet build ExtractFromXgToCsv.slnx` builds this repo and the sibling
projects it reaches by relative path, so it needs the umbrella checkout;
warnings are errors (`Directory.Build.props`).

`dotnet run --project ExtractFromXgToCsv` serves its launch profile,
https://localhost:7195 and http://localhost:5195. A Release candidate is
`dotnet publish ExtractFromXgToCsv/ExtractFromXgToCsv.csproj -c Release`,
then `dotnet ExtractFromXgToCsv.dll --urls http://localhost:5196` from
`ExtractFromXgToCsv/bin/Release/net10.0/publish`. A clean publish says
nothing about trimming: its trim analysis is silenced
(halheinrich/backgammon#180). Stop the app from Visual Studio or with Ctrl+C:
the page has no Exit (Hal, 2026-10-22, halheinrich/backgammon#433), and
closing the tab leaves the host, and any run, going.

`dotnet test ExtractFromXgToCsv.slnx` runs the suite, by hand: no CI workflow
runs it. Its fixtures are the umbrella's gitignored `TestData/FixtureFiles`,
linked into the test build's output by the test csproj rather than read in
place (halheinrich/backgammon#452 asks why). Without them the tests that read
a fixture fail, and most of those carry no `RequiresFixtureFiles` trait
(halheinrich/backgammon#369), so a trait filter does not exclude them. The
ordinary deck tests run the deck walk with `RecordingDeckRenderer`, and the
real renderers over one decision, so a suite run no longer renders the whole
fixture folder (3ecb1f6, 2026-10-24, halheinrich/backgammon#438); corpus-scale
deck output is the golden run's.

The known flake is a native crash in SkiaSharp during a deck test, which kills
the test host (halheinrich/backgammon#426). Report it by test name with the
host's exit, then re-run; never retry it silently.

The golden baseline, `LocalGoldenBaseline`, is one test that every `dotnet
test` invokes; with `EXTRACT_GOLDEN_LABEL` unset it returns without rendering
or writing anything. The full run is opt-in:
`EXTRACT_GOLDEN_LABEL=<sha>-a dotnet test --filter "Category=Golden"` runs
every pathway over `TestData/Golden/input` into a new
`TestData/Golden/<label>/` with a manifest of each output's size and hash.
Compare with an earlier label: CSV, Diagram JSON and XGP byte for byte;
PPTX by its entries' hashes and slide count, PDF by its date-masked hash and
page count, since both decks embed render timestamps. A change in which
decisions a run yields moves everything after the first one, so byte equality
says nothing there: compare CSV and Diagram JSON by decision, XGP by each
file's content, since its names are numbered in output order, and the decks
by slide and page count and by what repeats. The manifest's `Skips` counts
entries of every unit.

## Pitfalls

- **`prerender: false` on `Home`, and on any interactive page added.** The
  coordinator and the filter owner live in the WASM runtime; a prerender would
  initialise them twice and lose state. `Error.razor` is static and needs
  none.
- **The busy cursor is one class and one rule**: `is-busy` on the panel's
  root while a run is in hand, and the one `wwwroot/app.css` rule, whose
  comment says why its descendant half and `!important` stay.
  `BusyCursorTests` pins state to class only, so rename both together and
  add `cursor:` nowhere else.
- **The root document must link this host's scoped-CSS bundle**
  (`@Assets["ExtractFromXgToCsv.styles.css"]`). Its `@import` of
  BgUiPrimitives_Razor's CSS makes a dismissible notice's whole box the
  click target; without it the notice still renders and its close button
  works, and no component test sees the loss (`ScopedCssBundleLinkTests`
  pins the delivery, not that the rules apply). Re-check in a browser after
  touching the links or the static-assets pipeline.
- **A green build proves nothing about a `<Notice>` tag.** Pin every box with
  `NoticeBox.ShouldBe`, and name no `alert-*`, `btn-close` or role in a test
  of one: BgUiPrimitives_Razor's Pitfalls, Consumer tests.
- **A dismissal has one holder, the occurrence's owner**
  (`../SPEC-notices.md`): the run error's is `LocalRunController`
  (`_runError`, non-null exactly while one stands), the storage notice's
  `BrowserStorageCondition`. Add no dismissed flag beside either and hold
  none in a page; and never also write a standing gate reason (the
  zero-match notice, the skip record) as a run error.
- **bUnit timing.** BgUiPrimitives_Razor's Pitfalls, Consumer tests, gives the
  rule: wait on the operation, not a render. Here that means awaiting the
  gesture's task (`ClickAsync`) or settling a dismissal
  (`NoticeTesting.DismissAndSettleAsync`). The coordinator moves before the
  page re-renders, so assert the DOM with `LocalPage.WaitForScreen`, and poll
  a call made after the last render, such as the next-number write a commit
  makes.
- **A run's terminal accounting comes from its record, never a progress
  report** (How it works says why; `RunRecordTests`,
  `LocalFolderProcessorStopTests`). And a CSV, Diagram JSON or deck run's
  token is checked before its output is created, not after.
- **The filter is the owner's** (XgFilter_Razor's Pitfalls, The setup state).
  `ConfigInEffectFor` hands a new instance on every ask, so compare
  `FilterConfig`s by value, or every render reads as a change. The Run gate's
  filter term is read once (`ExtractApp.FilterInEffect`): no token minted at a
  use site, no config kept beside an "applied" flag, no empty-filter exception
  (halheinrich/backgammon#266). And latch the source at the folder input's
  change boundary, never `@oninput`: a half-typed path is not a source.
- **No host code names XgFilter_Razor**, though the assembly reaches it through
  the client project: the relay validates a saved-filters file name's shape
  and never learns the names, which the filter surface's store passes down
  through the client adapter from `SavedFiltersDocument`.
- **Storage goes through the shared access, and the restore runs to its end.**
  BgUiPrimitives_Razor's Pitfalls, Browser storage, is the policy: no latch,
  and a refusal disables nothing. Here, don't call `localStorage` through
  `IJSRuntime`, and don't wrap the restore in a method-wide `try`, whose
  handler would restate every default.
- **Decks render on the server.** ExportRaster carries native rendering and no
  WebAssembly client ships it (BackgammonDiagram_Lib's Layout), so the client
  takes no reference to it. `RenderPdf` throws without the QuestPDF licence
  `Program.cs` sets; the rendering test classes set their own, so the suite is
  no guard for `Program.cs`'s.
- **The server's services stay `public`**, because a controller can't take an
  `internal` constructor parameter (CS0051).
- **An `.xgp` source exports verbatim**, its bytes kept by the read when not
  anonymizing (ConvertXgToJson_Lib's Architecture, the read operation;
  `LocalFolderProcessorXgpTests` pins byte equality). `XgpPayload.Prepare`
  routes by the decision's id as the producer stamped it, never by extension.
- **The XGP counter is fed only by commits**: from a terminal result's
  `XgpExportCommit`, never `TotalRows` or a snapshot, and never over a
  control the user edited since the run started — judged by the edit, not by
  comparing values (§7.5, "has changed the control";
  `HomeXgpAccountingTests`). Add no record, ledger or migration to promise
  continuity, and infer no number from exported files' names (Hal's F3
  ruling, 2026-10-12: a pattern may lack `{n}` or carry other tokens;
  `../SPEC-notices.md` §7.4).
- **The naming engine stays liftable into a library, and numbering has one
  rule.** Add a token only to `XgpNameTokens.All`; a token never fails to
  render; `XgpExportOptions` stops at `XgpNameAllocator.Create`, so nothing
  below it couples to the app. Number a name only through `NumberedFileName`,
  which the per-run uniquifier and the free-name loop share.
- **Never check for a file and then write it.** Every output is one
  `FileMode.CreateNew`; `File.Exists` after a refused create only classifies
  it. A file is the run's only once its create succeeds, so a collision or a
  failed create removes nothing (`LocalFolderProcessorNoOverwriteTests`).
- **`{dice}`'s `00` for a cube decision is file-name presentation**: never a
  roll handed to a domain API (`XgpNameTemplateTests` holds the ruling).
- **An unmapped POST is 405 under `WebApplicationFactory` and 404
  published** (measured 2026-10-22, recorded in 91cffb2,
  halheinrich/backgammon#433; cause not established): a test that a route is
  gone compares it with one that never existed, as
  `ShutdownEndpointRetiredTests` does.
- **Opening-book config: absent ≠ empty.** No `OpeningBookPath` key
  auto-detects the install and `""` disables enrichment, so don't add an empty
  key as a placeholder.
