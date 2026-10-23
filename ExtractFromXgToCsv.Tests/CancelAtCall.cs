using BgDataTypes_Lib;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A filter that passes every decision and stops the run at call
/// <c>stopAt</c>, inside the conversion of the file that decision belongs to:
/// the file is committed — a file is the cancellation's granularity — and the
/// walk sees the stop before the next one. No real input stops a run at a
/// chosen file.
/// </summary>
/// <param name="stopAt">The call, from 1, that stops the run.</param>
/// <param name="stop">What stopping is: cancelling the run's token, and anything the test does at that moment.</param>
internal sealed class CancelAtCall(int stopAt, Action stop) : IDecisionFilter
{
    private int _calls;

    /// <summary>A set whose filter cancels <paramref name="cts"/> at call <paramref name="cancelAt"/>.</summary>
    public static DecisionFilterSet Set(int cancelAt, CancellationTokenSource cts) =>
        Set(cancelAt, cts.Cancel);

    /// <summary>A set whose filter runs <paramref name="stop"/> at call <paramref name="stopAt"/>.</summary>
    public static DecisionFilterSet Set(int stopAt, Action stop) =>
        new DecisionFilterSet().Add(new CancelAtCall(stopAt, stop));

    public bool Matches(IDecisionFilterData data)
    {
        if (++_calls == stopAt)
            stop();
        return true;
    }
}
