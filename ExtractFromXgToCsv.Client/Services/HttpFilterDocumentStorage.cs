namespace ExtractFromXgToCsv.Client.Services;

using System.Net;
using System.Text;
using XgFilter_Razor;

/// <summary>
/// XgFilter_Razor's <see cref="IFilterDocumentStorage"/> seam over the server's
/// Local-mode file relay (<c>GET</c>/<c>PUT /api/filterdocument</c>), so the
/// saved-filters document lives beside the corpus in the Local-mode source
/// folder — the same folder BgQuiz's picked-folder adapter writes, which is
/// what makes filters saved there appear here. The seam is name-only by
/// producer design; the folder half of the address comes from the
/// host-supplied delegate, resolved <b>at call time</b> — correct lazily,
/// because <c>FilterSurface</c> re-reads the document exactly when the host's
/// source token changes, which is the same boundary the host latches the
/// folder at.
///
/// <para>
/// <b>Error translation is the whole job</b> (the producer's adapter
/// contract): everything that means "the I/O failed" — a network-level
/// <see cref="HttpRequestException"/>, a non-success status — is wrapped in
/// <see cref="FilterStorageException"/> so the composite's store degrades
/// instead of faulting the page. An absent document is the 204 → null mapping,
/// never an exception. A call while the delegate has no folder is an
/// <b>adapter-contract bug</b> (the host passes <c>Storage = null</c> while
/// the source is blank, so the composite never calls here) and propagates as
/// <see cref="InvalidOperationException"/>.
/// </para>
/// </summary>
internal sealed class HttpFilterDocumentStorage : IFilterDocumentStorage
{
    private readonly HttpClient _http;
    private readonly Func<string?> _sourceFolder;

    /// <summary>
    /// Create the adapter over the app's <paramref name="http"/> client and
    /// the host's <paramref name="sourceFolder"/> accessor. One stable
    /// instance per page: <c>FilterSurface</c> rebuilds its store when the
    /// bound <c>Storage</c> <em>reference</em> changes, so the live-folder
    /// fact rides the delegate, never a new instance.
    /// </summary>
    /// <param name="http">The client for the server's file relay.</param>
    /// <param name="sourceFolder">Reads the host's latched source folder; null/blank = none current.</param>
    public HttpFilterDocumentStorage(HttpClient http, Func<string?> sourceFolder)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _sourceFolder = sourceFolder ?? throw new ArgumentNullException(nameof(sourceFolder));
    }

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(string fileName)
    {
        var url = BuildUrl(fileName);
        try
        {
            using var response = await _http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NoContent) return null;
            if (!response.IsSuccessStatusCode)
                throw new FilterStorageException(
                    $"Reading '{fileName}' from the source folder failed (HTTP {(int)response.StatusCode}).");
            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            throw new FilterStorageException(
                $"Reading '{fileName}' from the source folder failed.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string fileName, string json)
    {
        var url = BuildUrl(fileName);
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "text/plain");
            using var response = await _http.PutAsync(url, content);
            if (!response.IsSuccessStatusCode)
                throw new FilterStorageException(
                    $"Writing '{fileName}' into the source folder failed (HTTP {(int)response.StatusCode}).");
        }
        catch (HttpRequestException ex)
        {
            throw new FilterStorageException(
                $"Writing '{fileName}' into the source folder failed.", ex);
        }
    }

    private string BuildUrl(string fileName)
    {
        var folder = _sourceFolder();
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException(
                "No source folder is current — the host must pass Storage = null while the folder is blank.");
        return "/api/filterdocument"
            + $"?folder={Uri.EscapeDataString(folder)}&name={Uri.EscapeDataString(fileName)}";
    }
}
