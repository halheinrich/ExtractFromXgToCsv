using System.Collections.Immutable;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The XGP numbering a browser keeps: the pattern the last export named its
/// files by, the last number it used, and the number length — what the next
/// export under the same pattern continues from.
/// </summary>
internal sealed record XgpNumbering
{
    /// <summary>The numbering <paramref name="pattern"/>, <paramref name="lastNumber"/> and <paramref name="suffixLength"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lastNumber"/> is negative, or <paramref name="suffixLength"/> is not 1–9.</exception>
    public XgpNumbering(string pattern, int lastNumber, int suffixLength)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentOutOfRangeException.ThrowIfNegative(lastNumber);
        ArgumentOutOfRangeException.ThrowIfLessThan(suffixLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(suffixLength, 9);
        Pattern = pattern;
        LastNumber = lastNumber;
        SuffixLength = suffixLength;
    }

    /// <summary>The pattern the numbered files were named by.</summary>
    public string Pattern { get; }

    /// <summary>The last number used; zero when none was.</summary>
    public int LastNumber { get; }

    /// <summary>The number length the files were named with.</summary>
    public int SuffixLength { get; }

    /// <summary>
    /// The numbering after <paramref name="commit"/>: its pattern and length,
    /// and the last number it used.
    /// </summary>
    public static XgpNumbering After(XgpExportCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return new(commit.Options.NamePattern, commit.NextNumber - 1, commit.Options.SuffixLength);
    }
}

/// <summary>
/// A Local XGP job this browser is owed: it minted the job's identity and
/// recorded it before sending the start, and has not yet applied the job's
/// accounting or resolved it (the umbrella's <c>SPEC-notices.md</c> §7.5). It
/// captures what names the export if it is ever lost: its options — pattern
/// and first number — and the folder it writes to.
/// </summary>
/// <param name="JobId">The identity the browser minted for the start.</param>
/// <param name="Instance">The server instance the start named.</param>
/// <param name="Options">The naming options the job captured.</param>
/// <param name="Folder">The output folder the job writes to.</param>
internal sealed record OwedXgpJob(string JobId, string Instance, XgpExportOptions Options, string Folder);

/// <summary>
/// Home's numbering record (the umbrella's <c>SPEC-notices.md</c> §7.5): one
/// browser-storage document holding the numbering, the mark — the last XGP
/// ledger entry applied — and the jobs this browser is owed. Home is its one
/// owner; it is always written whole, from a fresh read, in one storage write
/// (<see cref="XgpNumberingKeeper"/>). Immutable: every change makes a new
/// record.
/// </summary>
internal sealed record XgpNumberingRecord
{
    /// <summary>The record of a browser that has numbered nothing, applied nothing and is owed nothing.</summary>
    public static XgpNumberingRecord Empty { get; } = new();

    /// <summary>The numbering; <see langword="null"/> until an export has committed a file.</summary>
    public XgpNumbering? Numbering { get; init; }

    /// <summary>The last ledger entry applied; <see langword="null"/> when none has been.</summary>
    public LedgerMark? Mark { get; init; }

    /// <summary>The jobs this browser is owed, in the order recorded.</summary>
    public ImmutableArray<OwedXgpJob> Owed { get; init; } = [];

    /// <summary>
    /// This record with <paramref name="commit"/>'s numbering, as Home sets it
    /// from an export: an export that committed nothing changes no number.
    /// </summary>
    public XgpNumberingRecord WithCommit(XgpExportCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return commit.Count > 0 ? this with { Numbering = XgpNumbering.After(commit) } : this;
    }

    /// <summary>Whether this record and <paramref name="other"/> hold the same numbering, mark and owed jobs, in order.</summary>
    public bool Equals(XgpNumberingRecord? other) =>
        other is not null
        && Numbering == other.Numbering
        && Mark == other.Mark
        && Owed.SequenceEqual(other.Owed);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Numbering);
        hash.Add(Mark);
        foreach (var job in Owed)
            hash.Add(job);
        return hash.ToHashCode();
    }
}
