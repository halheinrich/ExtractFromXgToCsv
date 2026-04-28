using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Shared;

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

        if (cfg.MoveNumberMin.HasValue || cfg.MoveNumberMax.HasValue)
            set.Add(new MoveNumberFilter(cfg.MoveNumberMin, cfg.MoveNumberMax));

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