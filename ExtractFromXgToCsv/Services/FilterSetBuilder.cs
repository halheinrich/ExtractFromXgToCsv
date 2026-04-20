using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Constructs a <see cref="DecisionFilterSet"/> from a serializable <see cref="FilterConfig"/>.
/// </summary>
public static class FilterSetBuilder
{
    public static DecisionFilterSet Build(FilterConfig cfg)
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

        // cfg.PlayTypes is intentionally unwired: PlayTypeFilter was removed in
        // XgFilter_Lib pending a three-board IDecisionFilterData substrate.

        return set;
    }
}