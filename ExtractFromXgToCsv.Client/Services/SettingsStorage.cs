using BgUiPrimitives_Razor;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// This app's own settings in the browser's local storage area — Home's keys
/// and both mode panels' — over BgUiPrimitives_Razor's
/// <see cref="BrowserStorage"/>, with every refusal reported to the app's
/// <see cref="BrowserStorageCondition"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Access and reporting only.</b> Each caller names its own key and keeps
/// its own parsing and default: a read answers the stored string, or
/// <see langword="null"/> when nothing is stored <em>or</em> the browser
/// refused, which every caller but one reads as "use the default". The
/// refusal is not lost by that: it has been reported before the result
/// returns, so the condition knows of it whoever asked. The numbering record
/// tells the two apart (<see cref="ReadEntryAsync"/>).
/// </para>
/// <para>
/// <b>Every call is made.</b> No latch: a refusal disables nothing, and the
/// next call is simply made (BgUiPrimitives_Razor's "Browser storage access").
/// A refused write leaves the caller's in-memory value standing and returns
/// normally, so a refused write never stops the gesture that made it — an
/// upload, a run, a format change.
/// </para>
/// <para>
/// Only the browser's refusal is a result. Anything else the accessor
/// propagates — static rendering, a disconnected runtime, an interop timeout
/// — propagates from here too.
/// </para>
/// </remarks>
internal sealed class SettingsStorage(BrowserStorage storage, BrowserStorageCondition condition)
{
    /// <summary>
    /// Reads <paramref name="key"/>: the stored value, possibly the empty
    /// string, or <see langword="null"/> when nothing is stored or the browser
    /// refused (the refusal reported).
    /// </summary>
    public async Task<string?> ReadAsync(string key)
    {
        var read = await ReadEntryAsync(key);
        return read.IsStored ? read.Value : null;
    }

    /// <summary>
    /// Reads <paramref name="key"/> as the browser answered it — stored,
    /// absent or refused (the refusal reported) — for the one caller that
    /// must tell a refusal from nothing stored: Home's numbering record, whose
    /// read has distinct outcomes (the umbrella's <c>SPEC-notices.md</c> §7.5).
    /// </summary>
    public async Task<BrowserStorageReadResult> ReadEntryAsync(string key)
    {
        var read = await storage.ReadAsync(BrowserStorageArea.Local, key);
        if (read.IsRefused)
            condition.ReportRefused(read.Refusal);
        return read;
    }

    /// <summary>
    /// Writes <paramref name="value"/> under <paramref name="key"/>. A refusal
    /// is reported and the call returns normally.
    /// </summary>
    /// <returns>
    /// Whether the browser stored the value — for a caller whose next step
    /// depends on it, such as removing what a stored value replaces. Most
    /// callers need not ask.
    /// </returns>
    public async Task<bool> WriteAsync(string key, string value)
    {
        var written = await storage.WriteAsync(BrowserStorageArea.Local, key, value);
        if (written.IsRefused)
            condition.ReportRefused(written.Refusal);
        return written.Succeeded;
    }

    /// <summary>
    /// Removes <paramref name="key"/>. A refusal is reported and the call
    /// returns normally.
    /// </summary>
    public async Task RemoveAsync(string key)
    {
        var removed = await storage.RemoveAsync(BrowserStorageArea.Local, key);
        if (removed.IsRefused)
            condition.ReportRefused(removed.Refusal);
    }
}
