using ConvertXgToJson_Lib;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Web mode's output builders — CSV, Diagram JSON and the XGP archive — and
/// its opening-book loader. Reading and converting the picked files is
/// <see cref="WebSession"/>'s. Runs entirely in WebAssembly on the user's
/// machine — no data is transferred to the server.
/// </summary>
internal class XgProcessingService
{
    /// <summary>
    /// Loads an opening book from an uploaded <c>.ob</c> file image so Web-mode
    /// extraction can enrich book-analysed decisions the same way Local mode
    /// does. The producer's loader is path-based and WASM has no real filesystem
    /// path for picked bytes, so the image is staged to a temporary
    /// (Emscripten MEMFS) file and read through
    /// <see cref="OpeningBook.TryLoad"/>, then the temp file is removed. Bridging
    /// the bytes here — not in the UI — keeps components off both
    /// <see cref="OpeningBook"/>'s path API and the temp-file mechanics.
    /// </summary>
    /// <param name="fileBytes">The uploaded <c>.ob</c> file's raw bytes.</param>
    /// <param name="book">On success, the loaded book; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the bytes were a valid opening book;
    /// <see langword="false"/> when they were not (the caller then extracts
    /// unenriched — a missing book is never fatal).
    /// </returns>
    public bool TryLoadOpeningBook(byte[] fileBytes, [NotNullWhen(true)] out OpeningBook? book)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        var tempPath = Path.Combine(Path.GetTempPath(), $"xg-openingbook-{Guid.NewGuid():N}.ob");
        try
        {
            File.WriteAllBytes(tempPath, fileBytes);
            return OpeningBook.TryLoad(tempPath, out book);
        }
        catch
        {
            // Staging failure degrades exactly like an invalid book: no throw.
            book = null;
            return false;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort cleanup of the MEMFS temp file */ }
        }
    }

    public string BuildCsv(IEnumerable<DecisionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(DecisionRow.CsvHeader);
        foreach (var row in rows)
            sb.AppendLine(row.ToCsvLine());
        return sb.ToString();
    }

    public string BuildDiagramJson(IEnumerable<BgDecisionData> items)
    {
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Builds the zip archive of an XGP download: one <c>.xgp</c> position
    /// file per decision whose payload could be prepared, named by
    /// <paramref name="options"/>' pattern through an
    /// <see cref="XgpNameAllocator"/> in the order given (numbering order =
    /// filtered order; duplicate rendered names get Windows-style
    /// <c>" (2)"</c> suffixes). Batch-constant tokens draw from
    /// <paramref name="filters"/>; per-item tokens from each row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each payload is prepared from the decision's retained decoded file by
    /// <see cref="XgpPayload"/> — sliced from an <c>.xg</c> source, an
    /// <c>.xgp</c> source copied verbatim from its retained bytes, both
    /// anonymized when <paramref name="anonymize"/> is set — within the
    /// decision's own boundary: a failure is that decision's export skip,
    /// reported to <paramref name="skipped"/> as it happens, and consumes no
    /// number (the umbrella's <c>SPEC-notices.md</c> §7.1, F1 of §7.4).
    /// </para>
    /// <para>
    /// The download commits once, at the finalized archive's hand-off to the
    /// browser, which is the caller's. Each prepared entry advances this
    /// archive's allocator provisionally; writing the entries and finalizing
    /// the archive are preparation of that one commit, outside the decisions'
    /// boundaries, so a failure there propagates and fails the download.
    /// </para>
    /// </remarks>
    /// <param name="decisions">The decisions to export, each with the decoded file it came from.</param>
    /// <param name="options">The naming options the download captured.</param>
    /// <param name="filters">The filter the download captured, for batch-constant tokens.</param>
    /// <param name="anonymize">Whether every entry's player names are rewritten to the producer's role preset.</param>
    /// <param name="skipped">Receives each decision skipped, as it is skipped — so a build that fails later keeps the skips it found.</param>
    /// <returns>The archive, and the numbering its hand-off would commit.</returns>
    /// <exception cref="ArgumentException"><paramref name="options"/> fails validation.</exception>
    public XgpArchive BuildXgpArchive(
        IReadOnlyList<XgpDecision> decisions,
        XgpExportOptions options,
        FilterConfig filters,
        bool anonymize,
        Action<SkippedItem> skipped)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(skipped);

        // Create validates the options — the single ArgumentException throw
        // point shared with the Local-mode pathway.
        var allocator = XgpNameAllocator.Create(options, filters);

        using var zipStream = new MemoryStream();
        // .xgp content is already zlib-compressed internally, so deflating
        // the entries again buys almost nothing — store fast.
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (row, source) in decisions)
            {
                // The decision's payload, within its own boundary.
                ReadOnlyMemory<byte> payload;
                try
                {
                    payload = XgpPayload.Prepare(row.Id, source.File, source.VerbatimBytes, anonymize);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    skipped(new SkippedItem(source.Name, row.Id, ex.Message));
                    continue;
                }

                // Preparation of the commit: the entry, under the
                // provisional allocator's next name.
                var entry = zip.CreateEntry(allocator.Next(row).Name, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(payload.Span);
            }
        }
        return new XgpArchive(zipStream.ToArray(), new XgpExportCommit(options, allocator.CommittedCount));
    }
}

/// <summary>
/// An XGP download's finalized archive, and what its hand-off to the browser
/// would commit: its entries, under the options the download captured.
/// Nothing is committed until that hand-off succeeds.
/// </summary>
/// <param name="Bytes">The finalized zip archive.</param>
/// <param name="Commit">The numbering a successful hand-off commits.</param>
internal sealed record XgpArchive(byte[] Bytes, XgpExportCommit Commit);
