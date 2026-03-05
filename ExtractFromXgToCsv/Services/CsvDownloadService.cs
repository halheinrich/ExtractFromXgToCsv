using Microsoft.JSInterop;
using System.Text;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Triggers a browser "Save As" download for CSV content via JS interop.
/// </summary>
public class CsvDownloadService
{
    private readonly IJSRuntime _js;

    public CsvDownloadService(IJSRuntime js) => _js = js;

    public async Task DownloadAsync(string csvContent, string fileName = "decisions.csv")
    {
        var bytes = Encoding.UTF8.GetBytes(csvContent);
        await _js.InvokeVoidAsync("downloadFile", fileName, "text/csv", bytes);
    }
}
