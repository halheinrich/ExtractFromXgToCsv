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
    private readonly JobStore _jobs;
    private readonly ILogger<ProcessController> _logger;

    public ProcessController(
        LocalFolderProcessor processor,
        JobStore jobs,
        ILogger<ProcessController> logger)
    {
        _processor = processor;
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/process/start
    /// Starts processing in background. Returns { jobId }.
    /// </summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] ProcessRequest request)
    {
        var jobId = _jobs.CreateJob();
        var entry = _jobs.Get(jobId)!;
        var filterSet = BuildFilterSet(request.Filters);

        // Fire and forget — progress updates are stored in JobStore
        _ = Task.Run(async () =>
        {
            var progress = new Progress<ProcessingProgress>(p =>
            {
                entry.Progress = p;
            });

            try
            {
                await _processor.ProcessAsync(
                    request.FolderPath,
                    request.OutputPath,
                    filterSet,
                    progress,
                    entry.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                entry.Progress.Cancelled = true;
                entry.Progress.Complete = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                entry.Progress = new ProcessingProgress
                {
                    Complete = true,
                    FileName = $"Error: {ex.Message}"
                };
            }
        });

        return Ok(new { jobId });
    }

    /// <summary>
    /// GET /api/process/{jobId}/status
    /// Returns current ProcessingProgress for the job.
    /// </summary>
    [HttpGet("{jobId}/status")]
    public IActionResult Status(string jobId)
    {
        var entry = _jobs.Get(jobId);
        if (entry is null) return NotFound();
        return Ok(entry.Progress);
    }

    /// <summary>
    /// POST /api/process/{jobId}/cancel
    /// Cancels the running job.
    /// </summary>
    [HttpPost("{jobId}/cancel")]
    public IActionResult Cancel(string jobId)
    {
        var entry = _jobs.Get(jobId);
        if (entry is null) return NotFound();
        entry.Cts.Cancel();
        return Ok();
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
