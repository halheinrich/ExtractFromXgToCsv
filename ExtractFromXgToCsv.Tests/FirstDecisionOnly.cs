using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A filter that passes the run's first decision and no other. It counts the
/// decisions it is asked about, so each run takes a fresh instance.
/// </summary>
internal sealed class FirstDecisionOnly : IDecisionFilter
{
    private int _calls;

    public bool Matches(IDecisionFilterData data) => ++_calls == 1;
}
