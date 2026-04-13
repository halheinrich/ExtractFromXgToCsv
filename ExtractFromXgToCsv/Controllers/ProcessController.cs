using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessController(
    LocalFolderProcessor processor,
    JobStore jobs,
    ILogger<ProcessController> logger) : ControllerBase
{

    /// <summary>
    /// POST /api/process/start
    /// Starts processing in background. Returns { jobId }.
    /// </summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] ProcessRequest request)
    {
        var jobId = jobs.CreateJob();
        var entry = jobs.Get(jobId)!;
        var filterSet = FilterSetBuilder.Build(request.Filters);

        // Fire and forget — progress updates are stored in JobStore
        _ = Task.Run(async () =>
        {
            var progress = new Progress<ProcessingProgress>(p =>
            {
                entry.Progress = p;
            });

            try
            {
                if (request.OutputFormat == OutputFormat.DiagramJson)
                {
                    await processor.ProcessDiagramAsync(
                        request.FolderPath,
                        request.OutputPath,
                        filterSet,
                        progress,
                        entry.Cts.Token);
                }
                else
                {
                    await processor.ProcessAsync(
                        request.FolderPath,
                        request.OutputPath,
                        filterSet,
                        progress,
                        entry.Cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                entry.Progress.Cancelled = true;
                entry.Progress.Complete = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId} failed", jobId);
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
        var entry = jobs.Get(jobId);
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
        var entry = jobs.Get(jobId);
        if (entry is null) return NotFound();
        entry.Cts.Cancel();
        return Ok();
    }
}
