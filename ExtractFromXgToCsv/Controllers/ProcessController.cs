using System.Text.RegularExpressions;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// HTTP surface for Local-mode jobs and the recovery protocol that keeps an
/// XGP run's accounting whatever happens to the page following it (the
/// umbrella's <c>SPEC-notices.md</c> §7.5): start a job under the identity the
/// browser minted, read its status, cancel it, acknowledge it, read the
/// instance's XGP ledger, and settle an owed identity. The judgements are the
/// <see cref="JobStore"/>'s; this surface carries them.
/// </summary>
/// <param name="processor">The pipeline that runs a job's work.</param>
/// <param name="jobs">This instance's jobs, start dispositions and XGP ledger.</param>
/// <param name="logger">Logs job-level failures.</param>
[ApiController]
[Route("api/[controller]")]
public partial class ProcessController(
    LocalFolderProcessor processor,
    JobStore jobs,
    ILogger<ProcessController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/process/start — a <see cref="StartRequest"/>. Admitted, new
    /// or repeated: 200 with a <see cref="StartResponse"/>. Refused — another
    /// instance, a changed request, a retired identity, or for XGP a running
    /// job or a stale mark — 409 with a <see cref="ProtocolRefusal"/>; nothing
    /// starts. An identity that is not at most 64 letters, digits and hyphens
    /// is 400.
    /// </summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] StartRequest request)
    {
        if (!IsJobId(request.JobId))
            return BadRequest();

        return jobs.Start(request, job => Launch(job, request.Run)) switch
        {
            StartJudgement.Admitted admitted => Ok(new StartResponse(request.JobId, admitted.LedgerNumber)),
            StartJudgement.Refused refused => Conflict(new ProtocolRefusal(refused.Reason)),
            _ => throw new InvalidOperationException("A start is admitted or refused."),
        };
    }

    /// <summary>
    /// GET /api/process/{jobId}/status — the job's current snapshot, its
    /// terminal result once it has one: 200, as often as asked, removing
    /// nothing. 404 when this instance does not hold the job — never started
    /// here, or released by its acknowledgement.
    /// </summary>
    [HttpGet("{jobId}/status")]
    public IActionResult Status(string jobId)
    {
        var progress = jobs.ReadStatus(jobId);
        return progress is null ? NotFound() : Ok(progress);
    }

    /// <summary>
    /// POST /api/process/{jobId}/cancel — cancels the running job: 200. A
    /// cancel for a job that has ended, or that this instance does not hold,
    /// is a no-op 404.
    /// </summary>
    [HttpPost("{jobId}/cancel")]
    public IActionResult Cancel(string jobId)
        => jobs.Cancel(jobId) ? Ok() : NotFound();

    /// <summary>
    /// POST /api/process/{jobId}/acknowledge — the page that started the job
    /// has taken its terminal result (and for XGP applied its entry): the
    /// result is released. Always 204: acknowledging a job the instance no
    /// longer holds succeeds, so a retry is harmless.
    /// </summary>
    [HttpPost("{jobId}/acknowledge")]
    public IActionResult Acknowledge(string jobId)
    {
        jobs.Acknowledge(jobId);
        return NoContent();
    }

    /// <summary>GET /api/process/ledger — this instance and its whole XGP ledger: 200 with a <see cref="LedgerSnapshot"/>.</summary>
    [HttpGet("ledger")]
    public ActionResult<LedgerSnapshot> Ledger() => jobs.Ledger();

    /// <summary>
    /// POST /api/process/{jobId}/settle — a <see cref="SettleRequest"/>: 200
    /// with a <see cref="SettleResponse"/>, retired or admitted. A settle
    /// naming another instance is 409 with a <see cref="ProtocolRefusal"/>.
    /// </summary>
    [HttpPost("{jobId}/settle")]
    public IActionResult Settle(string jobId, [FromBody] SettleRequest request)
    {
        if (!IsJobId(jobId))
            return BadRequest();

        return jobs.Settle(jobId, request.Instance) switch
        {
            SettleJudgement.Settled settled => Ok(new SettleResponse(settled.Outcome)),
            SettleJudgement.Refused refused => Conflict(new ProtocolRefusal(refused.Reason)),
            _ => throw new InvalidOperationException("A settle is settled or refused."),
        };
    }

    // The browser mints identities as GUIDs; anything else is not one. \z,
    // not $: $ also matches before a final newline.
    [GeneratedRegex(@"^[A-Za-z0-9-]{1,64}\z")]
    private static partial Regex JobIdShape();

    private static bool IsJobId(string? jobId) => jobId is not null && JobIdShape().IsMatch(jobId);

    /// <summary>Runs an admitted job's work in the background, every snapshot and its terminal result kept by the store.</summary>
    private void Launch(JobEntry job, ProcessRequest request) => _ = Task.Run(async () =>
    {
        var progress = new InlineProgress(snapshot =>
        {
            if (snapshot.Complete)
                jobs.Complete(job, snapshot);
            else
                jobs.Report(job, snapshot);
        });

        // The run's record, held here so the two terminal results this
        // runner builds itself carry its skips and its XGP commit. Neither can
        // take them from the last snapshot the run reported: a run-ending
        // exception reports nothing, and the pathways report only every tenth
        // file, so the last report can trail the record.
        var record = new RunRecord();

        try
        {
            var filterSet = request.Filters.Build();
            switch (request.OutputFormat)
            {
                case OutputFormat.DiagramJson:
                    await processor.ProcessDiagramAsync(
                        request.FolderPath, request.OutputPath,
                        filterSet, progress, record, job.Cancellation);
                    break;
                case OutputFormat.Pptx:
                    await processor.ProcessPptxAsync(
                        request.FolderPath, request.OutputPath,
                        filterSet, progress, record, job.Cancellation);
                    break;
                case OutputFormat.Pdf:
                    await processor.ProcessPdfAsync(
                        request.FolderPath, request.OutputPath,
                        filterSet, progress, record, job.Cancellation);
                    break;
                case OutputFormat.Xgp:
                    // The unbuilt Filters ride along besides filterSet:
                    // batch-constant name tokens (e.g. {min-move}) read
                    // the configured values, not the materialized set.
                    await processor.ProcessXgpAsync(
                        request.FolderPath, request.OutputPath,
                        filterSet, request.XgpOptions, request.Filters,
                        progress, request.Anonymize, record, job.Cancellation);
                    break;
                default:
                    await processor.ProcessAsync(
                        request.FolderPath, request.OutputPath,
                        filterSet, progress, record, job.Cancellation);
                    break;
            }
        }
        catch (OperationCanceledException ex)
        {
            // A new snapshot, recorded whole and built from the last report
            // in the same step: a status read never sees one half-made
            // terminal result. The cancellation stands; a failure closing the
            // output it left unfinished is reported beside it.
            var closeFailure = (ex as CancelledWithUnfinishedOutputException)?.CloseFailureMessage;
            jobs.Complete(job, last => record.CancelledSnapshot(last, closeFailure));
        }
        catch (Exception ex)
        {
            // The terminal result first: the job ends, and its ledger entry
            // with it, whatever logging then does.
            jobs.Complete(job, record.FailedSnapshot(ex.Message));
            logger.LogError(ex, "Job {JobId} failed", job.JobId);
        }
    });

    /// <summary>
    /// The job runner's progress sink: applies each snapshot inline, on the
    /// run's own flow, in the order reported. <see cref="Progress{T}"/> would
    /// post each one to the thread pool instead, so a snapshot reported just
    /// before a failure could be applied after the runner's catch has set the
    /// terminal one — which the store now refuses, but the order is the run's.
    /// </summary>
    private sealed class InlineProgress(Action<ProcessingProgress> apply)
        : IProgress<ProcessingProgress>
    {
        public void Report(ProcessingProgress value) => apply(value);
    }
}
