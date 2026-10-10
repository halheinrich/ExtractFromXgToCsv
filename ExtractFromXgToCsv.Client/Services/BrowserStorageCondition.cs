using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// This app's one storage condition: whether the browser has refused a
/// storage call this visit, and whether the user has dismissed the notice that
/// says so. App-scoped (one per loaded app under WebAssembly), and the
/// <see cref="IFilterStorageRefusalSink"/> this app registers with
/// <c>AddFilterSurface</c> (halheinrich/backgammon#374).
/// </summary>
/// <remarks>
/// <para>
/// <b>One condition, every reporter.</b> Home's own settings and both mode
/// panels' settings report their refusals here through
/// <see cref="SettingsStorage"/>, and XgFilter_Razor reports every refusal of
/// the filter surface's own calls here as its sink — also after the page that
/// started the call has gone. They feed one occurrence because the user is
/// told one thing: the browser is not keeping what this page asks it to keep.
/// Which key was refused first, and by whom, changes nothing the user can do.
/// </para>
/// <para>
/// <b>The occurrence and its dismissal</b> (<c>SPEC-notices.md</c> §2). The
/// occurrence begins at the first refusal reported and lasts for the rest of
/// the visit: every later refusal, from any reporter, is the same occurrence,
/// so a dismissal survives navigation and further refusals. A success ends
/// nothing and begins nothing: one call's success says nothing about another
/// key, and a read served says nothing about writes (a full quota refuses
/// writes while reads are still served). A full reload constructs a fresh
/// holder, which is a fresh visit with no occurrence. Neither bit is ever
/// stored.
/// </para>
/// <para>
/// <b>It reports; it never gates.</b> Nothing consults this before calling
/// storage, and nothing here can stop a call: every requested operation is
/// attempted (BgUiPrimitives_Razor's <c>BrowserStorage</c> has no latch, and
/// this is not one). It records that a refusal happened, for the notice.
/// </para>
/// <para>
/// <b>Observed, not polled.</b> A refusal can arrive while no gesture of the
/// observing page is running — a mode panel's restore after Home has
/// rendered, or the filter surface's commit answered after a navigation — so
/// the page attaches (<see cref="Attach"/>) and re-renders off the
/// notification. Every member is called on the renderer's synchronization
/// context; not thread-safe.
/// </para>
/// </remarks>
internal sealed class BrowserStorageCondition(ILogger<BrowserStorageCondition> logger)
    : IFilterStorageRefusalSink
{
    private readonly List<Action> _observers = [];

    /// <summary>
    /// Whether the browser has refused a storage call this visit — the
    /// occurrence. Once true it stays true until a reload.
    /// </summary>
    public bool HasOccurred { get; private set; }

    /// <summary>
    /// Whether the user has dismissed the notice reporting
    /// <see cref="HasOccurred"/>. Only ever <see langword="true"/> while
    /// <see cref="HasOccurred"/> is.
    /// </summary>
    public bool IsNoticeDismissed { get; private set; }

    /// <summary>
    /// Report that the browser refused a storage call. Every refusal is
    /// logged; the first begins the occurrence and notifies the observers, and
    /// every later one is the same occurrence and changes nothing.
    /// </summary>
    /// <param name="refusal">The browser's exception, as the accessor returned it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="refusal"/> is <see langword="null"/>.</exception>
    public void ReportRefused(JSException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        logger.LogWarning(refusal,
            "The browser refused a storage call; the page keeps working, but some settings may not be "
            + "restored or saved.");

        if (HasOccurred) return;
        HasOccurred = true;
        Publish();
    }

    /// <summary>Records that the user dismissed the notice.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing has been refused, so no such notice can be on screen. A
    /// dismissal recorded then would hide the occurrence before it began.
    /// </exception>
    public void DismissNotice()
    {
        if (!HasOccurred)
            throw new InvalidOperationException(
                "No storage call has been refused, so there is no notice to dismiss.");
        if (IsNoticeDismissed) return;

        IsNoticeDismissed = true;
        Publish();
    }

    /// <summary>
    /// Observe the condition: <paramref name="observer"/> is called once
    /// before this returns — the current state, read off
    /// <see cref="HasOccurred"/> and <see cref="IsNoticeDismissed"/> — and
    /// then after each change, until the returned handle is disposed
    /// (disposing twice is harmless). An observer reads the state and
    /// schedules its own render; it must not throw.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
    public IDisposable Attach(Action observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        _observers.Add(observer);
        observer();
        return new Attachment(this, observer);
    }

    // A copy, so an observer that detaches while being told changes nothing
    // about who else is told this time.
    private void Publish()
    {
        foreach (var observer in _observers.ToArray())
            observer();
    }

    private sealed class Attachment(BrowserStorageCondition owner, Action observer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._observers.Remove(observer);
        }
    }
}
