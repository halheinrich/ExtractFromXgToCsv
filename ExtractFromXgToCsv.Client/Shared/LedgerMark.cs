namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// A position in a server instance's XGP ledger: the instance, and the
/// number of an entry in it — the umbrella's <c>SPEC-notices.md</c> §7.5. As
/// the mark Home's numbering record keeps, it names the last ledger entry
/// this browser applied; a Local XGP start carries it, and the server admits
/// the start only when it names the instance's latest entry.
/// </summary>
/// <remarks>
/// Entries are ordered by number within an instance (<see cref="Follows"/>).
/// Marks of different instances are not ordered: across instances an entry is
/// newer only under a reconciliation pass's authority, which the pass
/// establishes and checks (§7.5). No wall-clock time orders them — system
/// time can be set back.
/// </remarks>
public sealed record LedgerMark
{
    /// <summary>The mark at entry <paramref name="number"/> of <paramref name="instance"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="instance"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is less than 1.</exception>
    public LedgerMark(string instance, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        Instance = instance;
        Number = number;
    }

    /// <summary>The server instance whose ledger this is a position in.</summary>
    public string Instance { get; }

    /// <summary>The entry's number, from 1, in admission order within <see cref="Instance"/>.</summary>
    public int Number { get; }

    /// <summary>
    /// Whether this mark comes after <paramref name="other"/> in their one
    /// instance's ledger: a higher number of the same instance. Marks of
    /// different instances are not ordered, so neither follows the other.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    public bool Follows(LedgerMark other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Instance == other.Instance && Number > other.Number;
    }
}
