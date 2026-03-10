using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessController : ControllerBase
{
    private readonly LocalFolderProcessor _processor;
    private readonly ILogger<ProcessController> _logger;

    public ProcessController(LocalFolderProcessor processor, ILogger<ProcessController> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/process
    /// Accepts folder path, output path, and filter config.
    /// Streams SSE progress events until complete.
    /// </summary>
    [HttpPost]
    public async Task RunAsync([FromBody] ProcessRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        var filterSet = BuildFilterSet(request.Filters);

        async Task SendEvent(ProcessingProgress p)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(p);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        var progress = new Progress<ProcessingProgress>(p =>
        {
            // Fire-and-forget inside the SSE loop — awaited via the semaphore pattern below
            _ = SendEvent(p);
        });

        try
        {
            await _processor.ProcessAsync(
                request.FolderPath,
                request.OutputPath,
                filterSet,
                progress,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed");
            var error = new ProcessingProgress { Complete = true, FileName = $"Error: {ex.Message}" };
            await SendEvent(error);
        }
    }

    private static DecisionFilterSet BuildFilterSet(FilterConfig cfg)
    {
        var set = new DecisionFilterSet();

        if (cfg.Players.Count > 0)
            set.Add(new PlayerFilter(cfg.Players));

        if (Enum.TryParse<DecisionTypeOption>(cfg.DecisionType, out var dt))
            set.Add(new DecisionTypeFilter(dt));
        else
            set.Add(new DecisionTypeFilter(DecisionTypeOption.Both));

        if (cfg.MatchScores.Count > 0)
            set.Add(new MatchScoreFilter(cfg.MatchScores));

        if (cfg.ErrorMin.HasValue || cfg.ErrorMax.HasValue)
            set.Add(new ErrorRangeFilter(cfg.ErrorMin, cfg.ErrorMax));

        var posTypes = cfg.PositionTypes
            .Select(s => Enum.TryParse<PositionType>(s, out var v) ? v : (PositionType?)null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToHashSet();
        if (posTypes.Count > 0)
            set.Add(new PositionTypeFilter(posTypes));

        var playTypes = cfg.PlayTypes
            .Select(s => Enum.TryParse<PlayType>(s, out var v) ? v : (PlayType?)null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToHashSet();
        if (playTypes.Count > 0)
            set.Add(new PlayTypeFilter(playTypes));

        return set;
    }
}

public class ProcessRequest
{
    public string FolderPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public FilterConfig Filters { get; set; } = new();
}
