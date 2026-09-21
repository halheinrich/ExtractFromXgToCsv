using Microsoft.JSInterop;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The guarded <c>localStorage</c> seam (halheinrich/backgammon#91), and the
/// owner of the one condition it can discover: that browser storage is
/// unavailable.
/// </summary>
/// <remarks>
/// <para>
/// <b>None of the three calls throws.</b> <c>localStorage</c> is not guaranteed
/// to exist: a disabled-storage or hostile-privacy setting makes
/// <c>getItem</c>/<c>setItem</c> raise a <c>SecurityError</c>, which reaches
/// Blazor as a <see cref="JSException"/>. Unguarded that faulted Home's
/// first-render restore, and since the mount gate went in
/// (halheinrich/backgammon#85) the cost was structural rather than cosmetic —
/// the restore never completed, so <c>FilterSurface</c> never mounted and the
/// page had no filtering at all, silently: this app registers no
/// <c>#blazor-error-ui</c>, so nothing on screen said why.
/// </para>
/// <para>
/// The guard is per call and translates the failure into "nothing is stored",
/// which is a state every read site already handles — each one carries its own
/// documented default in its <c>??</c> or <c>TryParse</c>. Nothing restates
/// those defaults here, so there is no second copy to drift from the caller's
/// field initializers. The invariant this buys: every read lands either on its
/// stored value or on its documented default, and the page's <em>shape</em> is
/// never at stake. A first failure partway through a restore therefore leaves a
/// mix of the two — coherent and truthful, each key answered for itself — and
/// the realistic case (storage refused outright) throws on the first read and
/// yields defaults throughout.
/// </para>
/// <para>
/// <see cref="JSException"/> only. A parse or migration bug in a caller is not
/// a storage failure and must still surface.
/// </para>
/// <para>
/// <b>Why this is app-scoped, and why the notice's dismissal lives here.</b>
/// Storage does not come back mid-session, so "storage is unavailable" is one
/// occurrence per loaded app: it begins at the first refused call and ends only
/// with a full reload, which constructs a fresh instance. The umbrella's
/// notices model (<c>SPEC-notices.md</c> §2) gives a dismissal exactly its
/// occurrence's lifetime and one holder, the occurrence's owner — and only
/// this type knows when the occurrence begins. A page that held either bit
/// would re-discover the same condition on every navigate-back and show a
/// notice the user had already dismissed. So the latch and the dismissal are
/// both here, registered Scoped (one per loaded app under WebAssembly), and
/// neither is ever stored.
/// </para>
/// <para>
/// State changes raise no notification. Every caller awaits one of the three
/// calls from a lifecycle method or an event handler and renders afterwards,
/// which is what carries a freshly flipped <see cref="IsUnavailable"/> to the
/// screen.
/// </para>
/// </remarks>
public sealed class BrowserStorage(IJSRuntime js)
{
    /// <summary>
    /// Whether a <c>localStorage</c> call has been refused since the app
    /// loaded. Latched, never retried: storage does not come back mid-session,
    /// and a failed call per key would be one identical console error each.
    /// </summary>
    public bool IsUnavailable { get; private set; }

    /// <summary>
    /// Whether the user has dismissed the notice that reports
    /// <see cref="IsUnavailable"/>. Only ever <see langword="true"/> while
    /// <see cref="IsUnavailable"/> is: there is no notice to dismiss before
    /// the condition exists, and the condition never ends within one
    /// instance's lifetime, so there is no later occurrence to show fresh.
    /// </summary>
    public bool IsUnavailableNoticeDismissed { get; private set; }

    /// <summary>
    /// Records that the user dismissed the storage-unavailable notice.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Storage is not unavailable, so no such notice can be on screen. A
    /// dismissal recorded then would hide the condition's one occurrence
    /// before it began.
    /// </exception>
    public void DismissUnavailableNotice()
    {
        if (!IsUnavailable)
            throw new InvalidOperationException(
                "Browser storage is not unavailable, so there is no notice to dismiss.");

        IsUnavailableNoticeDismissed = true;
    }

    /// <summary>
    /// Reads <paramref name="key"/>, or returns <see langword="null"/> —
    /// "nothing is stored" — when the key is absent or storage is unavailable.
    /// </summary>
    public async Task<string?> TryGetItemAsync(string key)
    {
        if (IsUnavailable) return null;

        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (JSException)
        {
            IsUnavailable = true;
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> under <paramref name="key"/>; does
    /// nothing when storage is unavailable.
    /// </summary>
    public Task TrySetItemAsync(string key, string value) =>
        TryWriteAsync("localStorage.setItem", key, value);

    /// <summary>
    /// Removes <paramref name="key"/>; does nothing when storage is
    /// unavailable.
    /// </summary>
    public Task TryRemoveItemAsync(string key) =>
        TryWriteAsync("localStorage.removeItem", key);

    // Writes fail the same way reads do, and the notice's claim — that nothing
    // on the page is remembered — has to keep holding for a session that
    // started with storage working and lost it.
    private async Task TryWriteAsync(string identifier, params object?[] args)
    {
        if (IsUnavailable) return;

        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (JSException)
        {
            IsUnavailable = true;
        }
    }
}
