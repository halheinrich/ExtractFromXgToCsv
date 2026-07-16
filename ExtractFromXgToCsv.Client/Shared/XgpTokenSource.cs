namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Where an <see cref="XgpNameToken"/>'s value comes from: the export batch
/// as a whole (options, active filters, the running counter) or the
/// individual decision being exported. Purely descriptive — UI grouping and
/// documentation; the render pipeline treats every token identically.
/// </summary>
public enum XgpTokenSource
{
    /// <summary>Driven by batch-level state (options or the active filters).</summary>
    Batch,

    /// <summary>Driven by the individual decision being exported.</summary>
    PerItem,
}
