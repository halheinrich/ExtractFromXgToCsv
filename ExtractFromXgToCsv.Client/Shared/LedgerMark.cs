namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// A position in a server instance's XGP ledger: the instance, and the
/// number of an entry in it — the umbrella's <c>SPEC-notices.md</c> §7.5. As
/// the mark Home's numbering record keeps, it names the last ledger entry
/// this browser applied; a Local XGP start carries it, and the server admits
/// the start only when it names the instance's latest entry.
/// </summary>
/// <remarks>
/// Entries are ordered by number within an instance, and every entry of the
/// current instance is newer than any mark from an earlier one
/// (<see cref="IsNewerThan"/>).
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
    /// Whether this mark is newer than <paramref name="other"/>, judged as the
    /// page that knows <paramref name="currentInstance"/> as the server's: a
    /// later entry of the same instance is newer, and so is any entry of the
    /// current instance than one of an earlier instance. Two marks of
    /// different instances, neither of them current, are not ordered; neither
    /// is newer.
    /// </summary>
    /// <param name="other">The mark to compare with; <see langword="null"/>, no mark, is older than any.</param>
    /// <param name="currentInstance">The server's instance as the page knows it; <see langword="null"/> when unknown.</param>
    public bool IsNewerThan(LedgerMark? other, string? currentInstance) =>
        other is null
        || (Instance == other.Instance
            ? Number > other.Number
            : Instance == currentInstance);
}
