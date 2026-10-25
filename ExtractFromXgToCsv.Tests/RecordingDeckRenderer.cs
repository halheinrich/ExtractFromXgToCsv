using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.ExportRaster;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A deck renderer stand-in for <c>LocalFolderProcessor.ProcessDeckAsync</c>:
/// it draws nothing, records the requests it is handed, in order, and returns
/// <see cref="Bytes"/>. The deck walk runs as the product runs it; only the
/// library's render, which deck conformance and the golden run own, is left out.
/// </summary>
internal sealed class RecordingDeckRenderer
{
    private readonly List<DiagramRequest> _requests = [];

    /// <summary>What every render returns: a few bytes no real deck begins with.</summary>
    public static IReadOnlyList<byte> Bytes { get; } = [0xDE, 0xC0, 0x01, 0x02];

    /// <summary>Every request handed to <see cref="Render"/>, in the order handed.</summary>
    public IReadOnlyList<DiagramRequest> Requests => _requests;

    /// <summary>How many times <see cref="Render"/> was called.</summary>
    public int Renders { get; private set; }

    /// <summary>The renderer, in the shape <c>ProcessDeckAsync</c> takes.</summary>
    public byte[] Render(IEnumerable<DiagramRequest> requests, DiagramOptions options, CancellationToken cancellationToken)
    {
        Renders++;
        _requests.AddRange(requests);
        return [.. Bytes];
    }
}
