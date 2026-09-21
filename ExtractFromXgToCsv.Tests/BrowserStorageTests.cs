using Bunit;
using ExtractFromXgToCsv.Client.Services;
using Microsoft.JSInterop;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Direct tests of <see cref="BrowserStorage"/>: the guarded
/// <c>localStorage</c> seam (halheinrich/backgammon#91) and the owner of the
/// one condition it discovers — that storage is unavailable — together with
/// the dismissal of the notice reporting it (halheinrich/backgammon#248).
/// What the condition costs the page is <see cref="HomeStorageUnavailableTests"/>'
/// subject; this class pins the owner's own contract, including the two rows
/// of the notices model a page test cannot reach: a dismissal cannot precede
/// its occurrence, and a fresh instance — a full reload — remembers nothing.
/// </summary>
public class BrowserStorageTests : BunitContext
{
    private static JSException StorageRefused() =>
        new("SecurityError: The operation is insecure.");

    private BrowserStorage NewStorage() => new(JSInterop.JSRuntime);

    private int CallsTo(string identifier) =>
        JSInterop.Invocations.Count(i => i.Identifier == identifier);

    [Fact]
    public async Task WorkingStorage_ReadsTheStoredValue_AndReportsNothing()
    {
        JSInterop.Setup<string?>("localStorage.getItem", "xg_folderPath").SetResult(@"D:\xg");
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        JSInterop.SetupVoid("localStorage.removeItem", _ => true).SetVoidResult();
        var storage = NewStorage();

        Assert.Equal(@"D:\xg", await storage.TryGetItemAsync("xg_folderPath"));
        await storage.TrySetItemAsync("xg_folderPath", @"D:\other");
        await storage.TryRemoveItemAsync("xg_xgpPrefix");

        Assert.False(storage.IsUnavailable);
        Assert.Equal(1, CallsTo("localStorage.setItem"));
        Assert.Equal(1, CallsTo("localStorage.removeItem"));
    }

    [Fact]
    public async Task RefusedRead_ReadsAsNothingStored_AndLatches()
    {
        JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetException(StorageRefused());
        var storage = NewStorage();

        Assert.Null(await storage.TryGetItemAsync("xg_folderPath"));

        Assert.True(storage.IsUnavailable);
    }

    [Fact]
    public async Task RefusedWrite_Latches()
    {
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetException(StorageRefused());
        var storage = NewStorage();

        await storage.TrySetItemAsync("xg_folderPath", @"D:\xg");

        Assert.True(storage.IsUnavailable);
    }

    [Fact]
    public async Task OnceUnavailable_StorageIsNeverTriedAgain()
    {
        JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetException(StorageRefused());
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetException(StorageRefused());
        var storage = NewStorage();
        await storage.TryGetItemAsync("xg_folderPath");

        // Storage does not come back mid-session: one refused call, then none.
        Assert.Null(await storage.TryGetItemAsync("xg_outputFormat"));
        await storage.TrySetItemAsync("xg_outputFormat", "Csv");
        await storage.TryRemoveItemAsync("xg_xgpPrefix");

        Assert.Equal(1, CallsTo("localStorage.getItem"));
        Assert.Equal(0, CallsTo("localStorage.setItem"));
        Assert.Equal(0, CallsTo("localStorage.removeItem"));
    }

    [Fact]
    public async Task AFailureThatIsNotTheStores_StillSurfaces()
    {
        // JSException only: anything else is a bug, not a refused store, and
        // swallowing it would report a working browser as storage-dead.
        JSInterop.Setup<string?>("localStorage.getItem", _ => true)
                 .SetException(new InvalidOperationException("not a storage refusal"));
        var storage = NewStorage();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.TryGetItemAsync("xg_folderPath"));
        Assert.False(storage.IsUnavailable);
    }

    // ── The notice's dismissal: one occurrence, one holder ──────────────────

    [Fact]
    public void Dismissal_CannotPrecedeTheOccurrence()
    {
        var storage = NewStorage();

        // Recorded early, it would hide the condition's one occurrence before
        // it began.
        Assert.Throws<InvalidOperationException>(storage.DismissUnavailableNotice);
        Assert.False(storage.IsUnavailableNoticeDismissed);
    }

    [Fact]
    public async Task Dismissal_LastsTheInstance_AndAFreshInstanceRemembersNothing()
    {
        JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetException(StorageRefused());
        var storage = NewStorage();
        await storage.TryGetItemAsync("xg_folderPath");

        storage.DismissUnavailableNotice();

        Assert.True(storage.IsUnavailableNoticeDismissed);
        // Later refusals are the same occurrence, not new ones.
        await storage.TrySetItemAsync("xg_folderPath", @"D:\xg");
        Assert.True(storage.IsUnavailableNoticeDismissed);

        // A full reload is a fresh instance: nothing about dismissal is
        // stored, so the condition is re-discovered and reported again.
        var afterReload = NewStorage();
        await afterReload.TryGetItemAsync("xg_folderPath");
        Assert.True(afterReload.IsUnavailable);
        Assert.False(afterReload.IsUnavailableNoticeDismissed);
    }
}
