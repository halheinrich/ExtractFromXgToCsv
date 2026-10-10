using ExtractFromXgToCsv.Client.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The storage condition's own contract (SPEC-notices.md §2): one occurrence
/// per visit, begun by the first refusal; its dismissal held beside it; every
/// later refusal the same occurrence; observers told of each change, and of
/// the current state when they attach. Home's use of it is pinned in
/// <see cref="HomeStorageUnavailableTests"/>; the reload row — a fresh holder —
/// can only be staged here.
/// </summary>
public class BrowserStorageConditionTests
{
    private static BrowserStorageCondition NewCondition() =>
        new(NullLogger<BrowserStorageCondition>.Instance);

    private static JSException Refusal() => new("SecurityError: The operation is insecure.");

    [Fact]
    public void AFreshHolder_HasNoOccurrence()
    {
        // A reload constructs a fresh holder: everything returns.
        var condition = NewCondition();

        Assert.False(condition.HasOccurred);
        Assert.False(condition.IsNoticeDismissed);
    }

    [Fact]
    public void TheFirstRefusal_BeginsTheOccurrence_AndLaterOnesAreTheSameOccurrence()
    {
        var condition = NewCondition();
        var notified = 0;
        using var _ = condition.Attach(() => notified++);
        notified = 0;

        condition.ReportRefused(Refusal());
        condition.ReportRefused(Refusal());
        condition.ReportRefused(Refusal());

        Assert.True(condition.HasOccurred);
        Assert.Equal(1, notified);
    }

    [Fact]
    public void ADismissal_SurvivesFurtherRefusals()
    {
        var condition = NewCondition();
        condition.ReportRefused(Refusal());

        condition.DismissNotice();
        condition.ReportRefused(Refusal());

        Assert.True(condition.HasOccurred);
        Assert.True(condition.IsNoticeDismissed);
    }

    [Fact]
    public void Dismissing_BeforeAnyRefusal_Throws_AndRecordsNothing()
    {
        var condition = NewCondition();

        Assert.Throws<InvalidOperationException>(condition.DismissNotice);
        Assert.False(condition.IsNoticeDismissed);
    }

    [Fact]
    public void Attach_DeliversTheCurrentStateBeforeItReturns_ThenEachChange()
    {
        var condition = NewCondition();
        condition.ReportRefused(Refusal());
        var seen = new List<(bool Occurred, bool Dismissed)>();

        using var _ = condition.Attach(() => seen.Add((condition.HasOccurred, condition.IsNoticeDismissed)));
        Assert.Equal([(true, false)], seen);

        condition.DismissNotice();
        condition.DismissNotice(); // already dismissed: no change, no notification

        Assert.Equal([(true, false), (true, true)], seen);
    }

    [Fact]
    public void DisposingTheAttachment_StopsTheObservation()
    {
        var condition = NewCondition();
        var notified = 0;
        var attachment = condition.Attach(() => notified++);
        notified = 0;

        attachment.Dispose();
        attachment.Dispose(); // twice is harmless
        condition.ReportRefused(Refusal());

        Assert.Equal(0, notified);
        Assert.True(condition.HasOccurred);
    }
}
