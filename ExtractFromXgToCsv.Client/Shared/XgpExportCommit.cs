using System.Text.Json.Serialization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// What one XGP export committed: how many position files, named under which
/// options — the authoritative accounting both modes hand <c>Home</c>, which
/// owns the persisted numbering (the umbrella's <c>SPEC-notices.md</c> §7.1,
/// "XGP numbering follows what is committed").
/// </summary>
/// <remarks>
/// <para>
/// Only committed entries consume numbers: a Local-mode file once it is
/// written to the output folder, a Web-mode archive entry once the finalized
/// archive has been handed to the browser. A skipped preparation consumes
/// none. So the numbers this export used are exactly
/// <see cref="XgpExportOptions.StartNumber"/> through
/// <c>StartNumber + Count − 1</c> under <see cref="Options"/>' pattern, and
/// the next export continues at <see cref="NextNumber"/>.
/// </para>
/// <para>
/// <see cref="Options"/> are the ones the export captured when it started,
/// never the controls' current values: a control changed while the export ran
/// does not reinterpret what it committed. The Local-mode processor builds one
/// as it writes, the job runner carries it on every terminal snapshot —
/// completed, cancelled or failed — and the panel hands it on; Web mode builds
/// one at a successful hand-off.
/// </para>
/// </remarks>
public sealed record XgpExportCommit
{
    /// <summary>Records <paramref name="count"/> files committed under <paramref name="options"/>.</summary>
    /// <param name="options">The naming options the export captured; valid, since an export never starts under invalid ones.</param>
    /// <param name="count">The files committed; zero when the export committed nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> fails <see cref="XgpExportOptions.TryValidate"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    [JsonConstructor]
    public XgpExportCommit(XgpExportOptions options, int count)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.TryValidate(out var error))
            throw new ArgumentException(error, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Options = options;
        Count = count;
    }

    /// <summary>The naming options the export captured when it started.</summary>
    public XgpExportOptions Options { get; }

    /// <summary>The number of files the export committed.</summary>
    public int Count { get; }

    /// <summary>
    /// Whether the export committed no file: it consumed no number, so it
    /// changes no numbering, and nothing follows from it.
    /// </summary>
    [JsonIgnore]
    public bool CommittedNothing => Count == 0;

    /// <summary>
    /// The number the next export under the same pattern continues at: the
    /// one after the last this export used, or its own start when it
    /// committed nothing.
    /// </summary>
    [JsonIgnore]
    public int NextNumber => Options.StartNumber + Count;
}
