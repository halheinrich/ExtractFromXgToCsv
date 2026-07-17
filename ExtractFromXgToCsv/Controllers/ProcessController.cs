using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// HTTP surface for Local-mode processing jobs: start a run, poll its status
/// (self-cleaning on the terminal snapshot — see <see cref="JobStore.ReadStatus"/>),
/// and cancel it.
/// </summary>
/// <param name="processor">The pipeline that runs a job's work.</param>
/// <param name="jobs">The registry that tracks running jobs and their progress.</param>
/// <param name="logger">Logs job-level failures.</param>
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
        var filterSet = request.Filters.Build();

        // Fire and forget — progress updates are stored in JobStore
        _ = Task.Run(async () =>
        {
            var progress = new Progress<ProcessingProgress>(p =>
            {
                entry.Progress = p;
            });

            try
            {
                switch (request.OutputFormat)
                {
                    case OutputFormat.DiagramJson:
                        await processor.ProcessDiagramAsync(
                            request.FolderPath, request.OutputPath,
                            filterSet, progress, entry.Cts.Token);
                        break;
                    case OutputFormat.Pptx:
                        await processor.ProcessPptxAsync(
                            request.FolderPath, request.OutputPath,
                            filterSet, progress, entry.Cts.Token);
                        break;
                    case OutputFormat.Pdf:
                        await processor.ProcessPdfAsync(
                            request.FolderPath, request.OutputPath,
                            filterSet, progress, entry.Cts.Token);
                        break;
                    case OutputFormat.Xgp:
                        // The unbuilt Filters ride along besides filterSet:
                        // batch-constant name tokens (e.g. {min-move}) read
                        // the configured values, not the materialized set.
                        await processor.ProcessXgpAsync(
                            request.FolderPath, request.OutputPath,
                            filterSet, request.XgpOptions, request.Filters,
                            progress, request.Anonymize, entry.Cts.Token);
                        break;
                    default:
                        await processor.ProcessAsync(
                            request.FolderPath, request.OutputPath,
                            filterSet, progress, entry.Cts.Token);
                        break;
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
                    ErrorMessage = ex.Message,
                };
            }
        });

        return Ok(new { jobId });
    }

    /// <summary>
    /// GET /api/process/{jobId}/status
    /// Returns current ProcessingProgress for the job. Reading a terminal
    /// snapshot cleans the job up (see <see cref="JobStore.ReadStatus"/>): the
    /// snapshot is served exactly once, so the polling client must consume the
    /// completion it observes here — a repeat poll for a finished job is a 404.
    /// </summary>
    [HttpGet("{jobId}/status")]
    public IActionResult Status(string jobId)
    {
        var progress = jobs.ReadStatus(jobId);
        return progress is null ? NotFound() : Ok(progress);
    }

    /// <summary>
    /// POST /api/process/{jobId}/cancel
    /// Cancels the running job. A late cancel for a job that already reached its
    /// terminal snapshot (and was cleaned up) is a no-op 404, never a 500.
    /// </summary>
    [HttpPost("{jobId}/cancel")]
    public IActionResult Cancel(string jobId)
        => jobs.Cancel(jobId) ? Ok() : NotFound();
}
