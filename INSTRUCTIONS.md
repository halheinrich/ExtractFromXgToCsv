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
the server reads and writes them. C# / .NET 10 Blazor Web App: an ASP.NET
Core host (`ExtractFromXgToCsv`) that runs each job over the file system,
and a WebAssembly client (`ExtractFromXgToCsv.Client`) that is the whole UI.
Solution
`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\ExtractFromXgToCsv\ExtractFromXgToCsv.slnx`;
repo https://github.com/halheinrich/ExtractFromXgToCsv, branch `main`.

Depends on ConvertXgToJson_Lib (`XgSourceReader`, the one read of a source;
discovery; the XGP slice and anonymize-copy; the opening book), XgFilter_Lib
(`FilterConfig.Build`, which the server calls on the filter a run sends, and
`FilteredDecisionIterator`, which filters every decoded file),
BgDataTypes_Lib (`DecisionRow`, the decision records, `PlayRanking`, the
strict enum converter), XgFilter_Razor (the filter surface and its
app-scoped owner `FilterSetup`; client only), BgUiPrimitives_Razor
(`Notice`, browser storage and its refusal condition) and
BackgammonDiagram_Lib with its ExportRaster assembly (the decks; server
only); the tests add the TestSupport assemblies of BgDataTypes_Lib,
BgUiPrimitives_Razor and XgFilter_Razor.

## Layout

- **`ExtractFromXgToCsv`** — the host: `Program.cs` (the registrations, the
  QuestPDF licence), `Components/` (the root document, `prerender: false`),
  `wwwroot/app.css` (the busy-cursor rule), vendored Bootstrap.
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
  `HttpDocumentStorage` (the saved-filters store over the relay).
  `Components/`: `Pages/Home.razor` (the settings, the XGP options and their
  preview, the filter surface), `LocalModePanel` (Run and Stop, progress and
  the result lines), `SkipListNotice` (the one rendering of a skip record).
  `Shared/`: the wire types both projects use (`ProcessRequest`, the
  protocol's `StartRequest` / `StartResponse` / `ProtocolRefusal`,
  `ProcessingProgress` with `SkipRecord`, `XgpExportCommit` and
  `StoppedOutput`, `StopStage`, `JobPhase`, `OutputFormat`), the XGP naming engine
  (`XgpExportOptions`, `XgpNameAllocator` and its internal parts,
  `XgpNamePreview`), `XgpPayload`, `NumberedFileName` (the one ` (n)` rule),
  `AppRanking`.
- **`ExtractFromXgToCsv.Tests`** — xUnit and bUnit, one class per contract,
  each stating its contract in its doc comment. The harnesses:
  `LocalProtocolTesting` (the real server in memory behind a `ProtocolWire`
  a test can fault, a `FileGate` that holds a run at its next create, and
  `LocalPage`), `LocalPanelHost` and `CapturingProcessHandler` (the panel
  over a stub server), `HomeTesting`, `ScriptedDestination`,
  `RecordingProgress`, `LocalGoldenBaseline`.

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
  reported and Home shows no panel, since `ReportSource(null)` is a real
  answer that ends a setup.
- **The source** is the folder path latched at the input's change boundary;
  its token is `FilterSourceToken.FromPath`'s, so path identity is the
  library's, and the output path is no source. A result on screen describes
  the source *visit* and the filter values it was computed from, and goes
  when they move; the run's skip record lives by `../SPEC-notices.md` §7.2:
  a filter change keeps it, a new source or the next run clears it.
- **The job protocol** is `../SPEC-notices.md` §7.5, followed here and not
  restated. The browser mints each start's identity, and `JobStore` judges a
  start by it — a repeat finds its admission and runs nothing, a changed
  request is refused — and keeps the job until its page acknowledges it. The
  page re-sends a start it could not make, follows the job's status (a read
  that fails is retried, and said once failures persist), and stops at the
  terminal result or a 404, the server no longer holding the job. A Stop
  pressed before the start is answered is pending, hinted "Stop requested;
  waiting for the server." (Hal, 2026-10-22, halheinrich/backgammon#424),
  and is delivered once the answer admits the job.
- **The processor's one walk** (`ForEachFileAsync`, §7.1): each file read
  once; a failed read or conversion is a skip, recorded through one seam
  (`RecordSkip`) onto the run's `RunRecord`; the file's contribution staged
  whole; the commit outside the skip boundary, so a destination failure
  fails the run; a cancellation never a skip. Every output file is created
  new under the next free ` (n)` name, never overwriting (§7.5), and "Done"
  names it as created (`WrittenPath`) — for XGP, whose output path is a
  folder, the folder. An unfinished XGP file is removed; the other formats'
  one file is not.
- **A run's accounting is its record's.** The pathways report every tenth
  file, so the job runner builds the stopped and failed snapshots from the
  `RunRecord` alone: the skips, the XGP commit, and for a stop the file
  reached, where it stood (`StopStage`: before the first input file, during
  the input, after every file was processed — derived from the file reached
  and the files finished, never from the file reached against the total), the
  time at the stop and what it left at its output
  (`StoppedOutput`: nothing, a CSV closed holding exactly its rows, or a file
  whose contents are uncertain). The token is checked immediately before an
  output file is created, so a run stopped before its write writes nothing.
  A deck's render takes the run's token, which BackgammonDiagram_Lib honours
  as it builds, so a Stop ends it promptly; the check before the create still
  catches a render that returns despite a stop. The stopped
  line composes the two facts, opening then ending, in Hal's wording (chat,
  2026-10-23, halheinrich/backgammon#424 6371257599).
- **XGP naming.** A pattern of literals and `{token}`s (`XgpNameTokens.All`
  is the registry), parsed by `XgpNameTemplate` and issued per run by
  `XgpNameAllocator`: Peek, then Commit once the file is written, so a
  skipped decision consumes no number. Numbering continuity is not a
  requirement (Hal's ruling, 2026-10-19, halheinrich/backgammon#424; §7.5):
  the pattern, the next number and its length are three remembered
  settings, the next number following a run's `XgpExportCommit` unless the
  user edited the pattern or the number meanwhile, and create-new under
  ` (n)` is what protects files.
- **One ranking.** `AppRanking.Ranking` is stated once and handed to every
  producer operation that depends on a ranking, the processor's constructor
  included (`../SPEC-scoring.md` §2a); no producer default stands in, even
  where it agrees today (Hal, 2026-09-29, halheinrich/backgammon#273).
- **The opening book** (`OpeningBookProvider`, loaded once): the
  `OpeningBookPath` key when non-empty, XG's default install when absent,
  none when empty. It enriches depth metadata; recovered levels can change
  which decisions pass depth filters.
- **Browser storage.** Every call goes through BgUiPrimitives_Razor's
  `BrowserStorage` (Hal's ruling, halheinrich/backgammon#374), its owners
  shared because common code has one shared owner (Hal's ruling, 2026-10-19,
  halheinrich/backgammon#424): the coordinator's keys through
  `ReportingBrowserStorage`, each with its consequence phrase, the filter
  surface's refusals through XgFilter_Razor's adapter, both to the one
  `BrowserStorageCondition` Home's notice reads; `AddFilterSurface()`
  registers it all. A refusal disables nothing.
- **The tests' layers.** bUnit renders over `AddClientServices`, the app's
  own registration; storage is planned (`BrowserStoragePlan`) where it is
  the subject and `Loose` elsewhere. State is reached through commands,
  gestures and the wire, never seeded; the one reflection is the name
  allocator's private-state reader (`bUnitTestHelpers`). A test that reads
  reports or depends on their timing uses `RecordingProgress`; a handlerless
  `new Progress<ProcessingProgress>()` that nothing reads stays.

## Running it

`dotnet run --project ExtractFromXgToCsv` serves its launch profile,
https://localhost:7195 and http://localhost:5195. A Release candidate is
`dotnet publish ExtractFromXgToCsv/ExtractFromXgToCsv.csproj -c Release`,
then `dotnet ExtractFromXgToCsv.dll --urls http://localhost:5196` from
`ExtractFromXgToCsv/bin/Release/net10.0/publish`. Stop it from Visual Studio
or with Ctrl+C: the page has no Exit (Hal, 2026-10-22,
halheinrich/backgammon#433), and closing the tab leaves the host, and any
run, going.

`dotnet test` runs the suite. Its fixtures are the umbrella's
`TestData/FixtureFiles`, linked into the test build's output, not kept
here: without the umbrella's `TestData` the tests that read a fixture fail.
The golden baseline writes nothing unless asked:
`EXTRACT_GOLDEN_LABEL=<sha>-a dotnet test --filter "Category=Golden"` runs
every pathway over `TestData/Golden/input` into a new
`TestData/Golden/<label>/` with a manifest of each output's size and hash.
Compare with an earlier label: CSV, Diagram JSON and XGP byte for byte;
PPTX by its entries' hashes and slide count, PDF by its date-masked hash and
page count, since both decks embed render timestamps.

## Pitfalls

- **`prerender: false` on every routable component.** The coordinator and
  the filter owner live in the WASM runtime; a prerender would initialise
  them twice and lose state.
- **The busy cursor is one class and one rule**: `is-busy` on the panel's
  root while a run is in hand, and the one `wwwroot/app.css` rule, whose
  descendant half and `!important` beat Bootstrap's pointer on the controls.
  `BusyCursorTests` pins state to class only, so rename both together and
  add `cursor:` nowhere else.
- **The root document must link this host's scoped-CSS bundle**
  (`@Assets["ExtractFromXgToCsv.styles.css"]`). Its `@import` of
  BgUiPrimitives_Razor's CSS makes a dismissible notice's whole box the
  click target; without it the notice still renders and its close button
  works, and no component test sees the loss (`ScopedCssBundleLinkTests`
  pins the delivery, not that the rules apply). Re-check in a browser after
  touching the links or the static-assets pipeline.
- **A green build proves nothing about a `<Notice>` tag.** It captures
  unmatched attributes, so a misspelt `Announcement` compiles and takes the
  default. Pin every box with `NoticeBox.ShouldBe`, and name no `alert-*`,
  `btn-close` or role in a test.
- **A dismissal has one holder, the occurrence's owner**
  (`../SPEC-notices.md`): the run error's is `LocalRunController`
  (`_runError`, non-null exactly while one stands), the storage notice's
  `BrowserStorageCondition`. Add no dismissed flag beside either and hold
  none in a page; and never also write a standing gate reason (the
  zero-match notice, the skip record) as a run error.
- **bUnit timing.** A synchronous event helper can return before a busy
  renderer has run the event: await the gesture's task (`ClickAsync`) or
  settle a dismissal (`NoticeTesting.DismissAndSettleAsync`). The
  coordinator moves before the page re-renders, so assert the DOM with
  `LocalPage.WaitForScreen`; and `WaitForAssertion` re-checks only on a
  render, so a call made after the last one — the next-number write a
  commit makes — is polled.
- **The job runner's progress sink is inline.** `Progress<T>` would post
  each report to the thread pool, so one made just before a failure could
  land after the terminal result; the store refuses that, but the order is
  the run's, and the inline sink keeps it.
- **A run's terminal accounting comes from its record, never a progress
  report**, which trails the run by up to ten files (`RunRecordTests`,
  `LocalFolderProcessorStopTests`); and the token is checked before an
  output is created, not after.
- **The filter is the owner's.** Compare `FilterConfig`s by value — the
  owner hands a new instance on every ask, so a reference comparison reads
  every render as a change. The Run gate's filter term is read once
  (`ExtractApp.FilterInEffect`): no token minted at a use site, no config
  kept beside an "applied" flag, no empty-filter exception
  (halheinrich/backgammon#266). And latch the source at the folder input's
  change boundary, never `@oninput`: a half-typed path is not a source.
- **XgFilter_Razor is not referenced server-side**: the relay validates a
  saved-filters file name's shape and never learns the names, which the
  client adapter sends from `SavedFiltersDocument`.
- **`HttpDocumentStorage` is one instance over a delegate, the
  coordinator's.** The filter surface rebuilds its store whenever the bound
  `Storage` reference changes, so the live folder rides the `Func<string?>`.
  With no folder latched the bound storage is null, so a call without one is
  a contract bug and throws unwrapped.
- **Storage never latches, and the restore runs to its end.** Don't call
  `localStorage` through `IJSRuntime` (the condition would never hear the
  refusal), skip calls behind an "unavailable" flag, or let a refused write
  stop its gesture; and don't wrap the restore in a method-wide `try`, whose
  handler would restate every default.
- **Decks render on the server.** SkiaSharp has no WASM binary: no
  ExportRaster reference from the client. `RenderPdf` throws without the
  QuestPDF licence `Program.cs` sets; the rendering test classes set their
  own, so the suite is no guard for `Program.cs`'s. The server's services
  stay `public` because a controller can't take an `internal` constructor
  parameter (CS0051).
- **An `.xgp` source exports verbatim**, its bytes kept by the read when
  not anonymizing (`LocalFolderProcessorXgpTests` pins byte equality);
  decide by the producer's `CopiesSourceVerbatim`, never by extension.
- **The XGP counter is fed only by commits**: from a terminal result's
  `XgpExportCommit`, never `TotalRows` or a snapshot, and never over a
  control the user edited since the run started — judged by the edit, not by
  comparing values (§7.5, "has changed the control";
  `HomeXgpAccountingTests`). Add no record, ledger or migration to promise
  continuity, and infer no number from exported files' names (Hal's F3
  ruling).
- **The naming engine is lib-ready, and numbering has one rule.** Add a
  token only to `XgpNameTokens.All`; a token never fails to render;
  `XgpExportOptions` stops at `XgpNameAllocator.Create`. Number a name only
  through `NumberedFileName`, which the per-run uniquifier and the free-name
  loop share.
- **Never check for a file and then write it.** Every output is one
  `FileMode.CreateNew`; `File.Exists` after a refused create only classifies
  it. A file is the run's only once its create succeeds, so a collision or a
  failed create removes nothing (`LocalFolderProcessorNoOverwriteTests`).
- **`{dice}`'s `00` for a cube decision is file-name presentation**: never a
  roll handed to a domain API (`XgpNameTemplateTests` holds the ruling).
- **An unmapped POST is 405 under `WebApplicationFactory` and 404
  published** (measured 2026-10-22, cause not established): a test that a
  route is gone compares it with one that never existed.
- **Opening-book config: absent ≠ empty.** No `OpeningBookPath` key
  auto-detects the install; `""` disables enrichment, so don't add an empty
  key as a placeholder.
